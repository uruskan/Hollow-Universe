using System;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation.Movement;
using Hollow.Surface;

namespace Hollow.Simulation.Ships;

/// <summary>What a ship's step needs to know about where it happens.</summary>
public readonly struct ShipContext
{
    public ShipContext(ZoneFrame frame, ISurface surface, float gravity, float airDensity, float scaleHeight, ShipCollider? collider,
        float edge = ShipMotor.ZoneEdge)
    {
        Frame = frame;
        Surface = surface;
        Gravity = gravity;
        AirDensity = airDensity;
        ScaleHeight = scaleHeight;
        Collider = collider;
        Edge = edge;
    }

    public readonly ZoneFrame Frame;

    /// <summary>The ground, for the height over it (the flight assist's landing descent, whether to look for contacts).</summary>
    public readonly ISurface Surface;

    /// <summary>The planet's surface gravity in m/s² (not the characters' 1.8 ×).</summary>
    public readonly float Gravity;

    /// <summary>The air's density at the planet's mean radius (kg/m³) and its scale height (m).</summary>
    public readonly float AirDensity;
    public readonly float ScaleHeight;

    /// <summary>The static world, or null for a ship in open air (tests).</summary>
    public readonly ShipCollider? Collider;

    /// <summary>The half width of the square of the zone a ship may fly in (its ground's extent, less a margin).</summary>
    public readonly float Edge;

    /// <summary>The planet's gravity in m/s² for a surface gravity in g.</summary>
    public static float GravityOf(double surfaceGravityG) => (float)(surfaceGravityG * 9.80665);
}

/// <summary>
/// Ship flight (GROUND.md §6.6): six degrees of freedom in the zone's frame under the planet's gravity (along −up from
/// <see cref="ZoneFrame.UpAt"/>) and its air's drag. Flight assist (on by default) holds the velocity the throttle,
/// strafe and up/down ask for and the rotation rates the stick asks for, within the thrusters' limits, and so hovers
/// against gravity; with it off the controls are thrust and torque (Newtonian, but the air still drags). Boost, fuel,
/// landing on sprung skids with friction (so a ship stands on a slope it may land on and slides off a steeper one),
/// the hull swept against the static world each substep (no tunnelling), crash damage, sleep at rest. The server and a
/// pilot's prediction run exactly this, one fixed tick at a time. Callers hold <see cref="Physics.JoltRuntime.Gate"/>.
/// </summary>
public static class ShipMotor
{
    /// <summary>A ship resting this many ticks in a row sleeps: nothing runs until something wakes it.</summary>
    public const int SleepTicks = CharacterMotor.TickRate;

    /// <summary>A sleeping ship checks every this many ticks that it still has ground under its skids.</summary>
    public const int SupportCheckTicks = 2 * CharacterMotor.TickRate;

    /// <summary>The ground tiles reach ±8.2 km; ships stay within this (G8's zones that follow players lift it).</summary>
    public const float ZoneEdge = 7800f;

    /// <summary>Within this height over the ground (plus a second's travel) the hull and skids look for contacts.</summary>
    public const float NearGround = 40f;

    /// <summary>The skids' rays start this far above each pad.</summary>
    public const float SkidProbe = 0.5f;

    /// <summary>Flight assist's gentle descent works below this height over the ground.</summary>
    public const float LandingAssistHeight = 30f;

    private const float WaterDensity = 1000f;
    private const float Restitution = 0.15f;
    private const float HullFriction = 0.35f;
    private const int FrictionPasses = 8;

    /// <summary>A contact found by the skids in a substep.</summary>
    private struct SkidLoad
    {
        public Vector3 Arm;
        public Vector3 Normal;
        public float Force;

        /// <summary>The friction impulse given so far this substep.</summary>
        public Vector3 Friction;
    }

    /// <summary>Advances a ship by one tick. <paramref name="ignoreBody"/> is the ship's own parked body, if it has one.</summary>
    public static ShipEvents Step(ref ShipState s, in ShipInput input, ShipDef def, in ShipContext ctx, uint ignoreBody = ShipCollider.NoBody)
    {
        var events = new ShipEvents();
        const float dt = CharacterMotor.DeltaTime;
        if (s.Has(ShipFlags.Destroyed) || s.Has(ShipFlags.InOrbit))
        {
            s.Thrust = Vector3.Zero;
            s.Flags &= ~(ShipFlags.EnginesOn | ShipFlags.Boosting);
            return events;
        }

        // Asleep: costs nothing until the pilot asks for motion, or the ground under it goes.
        if (s.Asleep)
        {
            if (!input.WantsMotion)
            {
                s.RestTicks = s.RestTicks >= 60_000 ? (ushort)SleepTicks : (ushort)(s.RestTicks + 1);
                s.Thrust = Vector3.Zero;
                s.Flags &= ~(ShipFlags.EnginesOn | ShipFlags.Boosting);
                bool check = (s.RestTicks - SleepTicks) % SupportCheckTicks == 0;
                if (!check || ctx.Collider == null || Supported(s, def, ctx, ignoreBody))
                {
                    TickBoost(ref s, def, false, ref events);
                    return events;
                }
            }
            s.RestTicks = 0;
        }

        ShipState start = s;
        Vector3 up = ctx.Frame.UpAt(s.Position);
        Vector3D bodyPoint = ctx.Frame.ToBody(s.Position);
        double radius = bodyPoint.Length;
        float height = (float)(radius - ctx.Surface.Radius);
        float overGround = (float)(radius - (ctx.Surface.Radius + ctx.Surface.HeightAt(bodyPoint / radius)));
        float density = ctx.AirDensity * (float)DMath.Exp(-Math.Max(height, 0) / Math.Max(ctx.ScaleHeight, 1));
        float airRatio = ctx.AirDensity > 0 ? density / ctx.AirDensity : 0;
        float submerged = 0;
        if (ctx.Surface.SeaRadius > 0)
        {
            // The hull reaches about 1.2 m below its centre of mass and as far above.
            submerged = Math.Clamp((float)(ctx.Surface.SeaRadius - radius + 1.2) / 2.4f, 0, 1);
        }

        bool engines = input.Engines && s.Fuel > 0;
        bool boosting = TickBoost(ref s, def, engines && input.Has(ShipButtons.Boost), ref events);
        // On its skids with nothing asked of it, the engines idle and the ship sits (or slides off what is too steep).
        bool landedIdle = s.Has(ShipFlags.Touching) && !input.WantsMotion;
        bool assist = !input.Has(ShipButtons.AssistOff);
        float m = def.Mass;

        // ---------------------------------------------------------------- what the thrusters do this tick
        Quaternion q = s.Rotation;
        Quaternion qi = Quaternion.Conjugate(q);
        Vector3 vLocal = Vector3.Transform(s.Velocity, qi);
        Vector3 gLocal = Vector3.Transform(-up * ctx.Gravity, qi);
        float boost = boosting ? def.BoostThrust : 1;
        float capForward = def.MainThrust * boost / m, capBack = def.RetroThrust / m, capSide = def.LateralThrust / m;
        float capUp = def.LiftThrust / m, capDown = def.DownThrust / m;
        Vector3 thrust = Vector3.Zero, turn = Vector3.Zero;
        if (engines && !landedIdle)
        {
            if (assist)
            {
                var target = new Vector3(
                    -input.Strafe * def.LateralSpeed,
                    input.Vertical >= 0 ? input.Vertical * def.UpSpeed : input.Vertical * def.DownSpeed,
                    boosting ? def.BoostSpeed : input.Throttle >= 0 ? input.Throttle * def.ForwardSpeed : input.Throttle * def.ReverseSpeed);
                if (!boosting && MathF.Abs(input.Throttle) < 0.05f && overGround < LandingAssistHeight)
                {
                    // Hovering down near the ground: flight assist eases the descent (it never stops one at full throttle).
                    Vector3 world = Vector3.Transform(target, q);
                    float descent = def.LandingDescent + MathF.Max(overGround, 0) * def.LandingDescentPerMetre;
                    float along = Vector3.Dot(world, up);
                    if (along < -descent)
                    {
                        target = Vector3.Transform(world + up * (-descent - along), qi);
                    }
                }
                Vector3 need = (target - vLocal) / dt - gLocal - Drag(vLocal, density, def) - Buoyancy(qi, up, submerged, ctx.Gravity, def);
                thrust = new Vector3(Math.Clamp(need.X, -capSide, capSide), Math.Clamp(need.Y, -capDown, capUp), Math.Clamp(need.Z, -capBack, capForward));
                var rates = new Vector3(-input.Pitch * def.Pitch.MaxRate, -input.Yaw * def.Yaw.MaxRate, input.Roll * def.Roll.MaxRate);
                Vector3 angular = (rates - s.AngularVelocity) / dt + s.AngularVelocity * (def.TurnDrag * airRatio + submerged * 2f);
                turn = new Vector3(Math.Clamp(angular.X, -def.Pitch.Acceleration, def.Pitch.Acceleration),
                    Math.Clamp(angular.Y, -def.Yaw.Acceleration, def.Yaw.Acceleration),
                    Math.Clamp(angular.Z, -def.Roll.Acceleration, def.Roll.Acceleration));
            }
            else
            {
                thrust = new Vector3(-input.Strafe * capSide,
                    input.Vertical >= 0 ? input.Vertical * capUp : input.Vertical * capDown,
                    boosting ? capForward : input.Throttle >= 0 ? input.Throttle * capForward : input.Throttle * capBack);
                turn = new Vector3(-input.Pitch * def.Pitch.Acceleration, -input.Yaw * def.Yaw.Acceleration, input.Roll * def.Roll.Acceleration);
            }
            // Fuel: an idle burn, more with the thrust given, more again boosting.
            float effort = Math.Min(1f, (MathF.Abs(thrust.X) + MathF.Abs(thrust.Y) + MathF.Abs(thrust.Z)) * m / (def.MainThrust + def.LiftThrust));
            s.Fuel -= (def.FuelIdle + effort * (def.FuelFull - def.FuelIdle)) * (boosting ? def.FuelBoost : 1) * dt;
            if (s.Fuel <= 0)
            {
                s.Fuel = 0;
                events.OutOfFuel = true;
            }
        }
        s.Thrust = new Vector3(thrust.X / capSide, thrust.Y >= 0 ? thrust.Y / capUp : thrust.Y / capDown,
            thrust.Z >= 0 ? thrust.Z * m / def.MainThrust : thrust.Z / capBack);
        s.Flags = (s.Flags & ~(ShipFlags.EnginesOn | ShipFlags.Boosting | ShipFlags.AssistOff | ShipFlags.AtEdge | ShipFlags.Wet))
            | (engines && !landedIdle ? ShipFlags.EnginesOn : ShipFlags.None)
            | (boosting ? ShipFlags.Boosting : ShipFlags.None)
            | (assist ? ShipFlags.None : ShipFlags.AssistOff)
            | (submerged > 0 ? ShipFlags.Wet : ShipFlags.None);

        // ---------------------------------------------------------------- substeps
        ShipCollider? collider = ctx.Collider;
        float speed = s.Velocity.Length();
        bool near = collider != null && overGround < NearGround + speed + def.BoundingRadius;
        if (near)
        {
            collider!.World.EnsureGround(s.Position);
            collider.World.Prefetch(s.Position + s.Velocity, radius: 1);
        }
        int steps = near ? 2 : 1;
        float h = dt / steps;
        Span<SkidLoad> loads = stackalloc SkidLoad[8];
        Span<ShipContact> contacts = stackalloc ShipContact[4];
        int loaded = 0;
        float closing = 0;
        Vector3 groundNormal = Vector3.Zero;
        for (int step = 0; step < steps; step++)
        {
            q = s.Rotation;
            qi = Quaternion.Conjugate(q);
            vLocal = Vector3.Transform(s.Velocity, qi);
            Vector3 accel = Vector3.Transform(thrust + Drag(vLocal, density, def), q) - up * ctx.Gravity;
            Vector3 angular = turn - s.AngularVelocity * (def.TurnDrag * airRatio);
            if (submerged > 0)
            {
                accel += up * (WaterDensity * HullVolume(def) * submerged * ctx.Gravity / m) - s.Velocity * (2f * submerged);
                angular -= s.AngularVelocity * (2f * submerged);
            }

            // The skids: a spring and damper at each pad that touches, along the ground's normal.
            loaded = 0;
            if (near)
            {
                Vector3 down = -Vector3.Transform(Vector3.UnitY, q);
                Vector3 spin = Vector3.Transform(s.AngularVelocity, q);
                foreach (Vector3 pad in def.SkidPoints)
                {
                    Vector3 at = s.Position + Vector3.Transform(pad - def.CentreOfMass, q);
                    if (!collider!.Ray(at - down * SkidProbe, down, SkidProbe + def.SkidTravel, ignoreBody, out ShipRayHit hit))
                    {
                        continue;
                    }
                    float compression = SkidProbe - hit.Distance;
                    if (compression <= 0)
                    {
                        continue;
                    }
                    Vector3 arm = hit.Point - s.Position;
                    float into = -Vector3.Dot(s.Velocity + Vector3.Cross(spin, arm), hit.Normal);
                    float force = def.SkidStiffness * MathF.Min(compression, def.SkidTravel * 1.5f) + def.SkidDamping * into;
                    if (force <= 0)
                    {
                        continue;
                    }
                    Vector3 f = hit.Normal * force;
                    accel += f / m;
                    angular += Vector3.Transform(Vector3.Cross(arm, f), qi) / def.Inertia;
                    if (loaded < loads.Length)
                    {
                        loads[loaded++] = new SkidLoad { Arm = arm, Normal = hit.Normal, Force = force };
                    }
                    closing = MathF.Max(closing, into);
                    groundNormal += hit.Normal;
                }
            }

            s.Velocity += accel * h;
            s.AngularVelocity += angular * h;

            // Friction at the pads: as much of their sliding as the load allows is taken out. Several passes, as the pads
            // pull on each other through the rotation; each pad's impulse over both stays within its friction.
            for (int pass = 0; pass < FrictionPasses && loaded > 0; pass++)
            {
                for (int i = 0; i < loaded; i++)
                {
                    loads[i].Friction = Friction(ref s, def, loads[i].Arm, loads[i].Normal, def.SkidFriction * loads[i].Force * h, loads[i].Friction);
                }
            }
            if (!assist && engines)
            {
                ClampFlightAssistOff(ref s, def);
            }

            s.Rotation = Integrate(s.Rotation, s.AngularVelocity, h);
            Vector3 move = s.Velocity * h;
            if (near)
            {
                if (move.LengthSquared() > 1e-12f && collider!.Cast(def, s.Position, s.Rotation, move, ignoreBody, out float fraction, out ShipContact hit) &&
                    Vector3.Dot(move, hit.Normal) < 0)
                {
                    // Up to the contact (and a hair back out), then the impulse there.
                    s.Position += move * fraction + hit.Normal * 0.002f;
                    float impact = Collide(ref s, def, hit.Point - s.Position, hit.Normal, Restitution);
                    events.ImpactSpeed = MathF.Max(events.ImpactSpeed, impact);
                }
                else
                {
                    s.Position += move;
                }
                // Out of anything the hull ended up in (a turn into the ground, a pad pressed past its travel).
                int found = collider!.Overlaps(def, s.Position, s.Rotation, ignoreBody, contacts);
                Vector3 push = Vector3.Zero;
                for (int i = 0; i < found; i++)
                {
                    float left = contacts[i].Depth - Vector3.Dot(push, contacts[i].Normal);
                    if (left > 0)
                    {
                        push += contacts[i].Normal * left;
                    }
                }
                s.Position += push;
                for (int i = 0; i < found; i++)
                {
                    float impact = Collide(ref s, def, contacts[i].Point - s.Position, contacts[i].Normal, 0);
                    events.ImpactSpeed = MathF.Max(events.ImpactSpeed, impact);
                }
            }
            else
            {
                s.Position += move;
            }
            KeepInZone(ref s, ctx.Edge);
        }

        // ---------------------------------------------------------------- what the step found
        events.Contacts = loaded;
        speed = s.Velocity.Length();
        bool wasLanded = s.Has(ShipFlags.Landed);
        bool landed = false;
        if (loaded >= 3 && speed < 1.5f)
        {
            float cos = Vector3.Dot(Vector3.Normalize(groundNormal), up);
            landed = cos >= MathF.Cos(def.MaxLandingSlope);
        }
        s.Flags = landed ? s.Flags | ShipFlags.Landed : s.Flags & ~ShipFlags.Landed;
        s.Flags = loaded >= 3 ? s.Flags | ShipFlags.Touching : s.Flags & ~ShipFlags.Touching;
        if (landed && !wasLanded)
        {
            events.Touchdown = true;
            events.TouchdownSpeed = closing;
        }
        if (!Finite(s.Position) || !Finite(s.Velocity) || !Finite(s.AngularVelocity) || !float.IsFinite(s.Rotation.W))
        {
            // Never let a bad number loose (a degenerate contact): back to where the tick began, at rest.
            s = start;
            s.Velocity = Vector3.Zero;
            s.AngularVelocity = Vector3.Zero;
        }
        if (landed && speed < 0.05f && s.AngularVelocity.Length() < 0.02f && !input.WantsMotion)
        {
            s.RestTicks++;
            if (s.RestTicks >= SleepTicks)
            {
                s.Velocity = Vector3.Zero;
                s.AngularVelocity = Vector3.Zero;
                s.Thrust = Vector3.Zero;
                s.Flags &= ~ShipFlags.EnginesOn;
            }
        }
        else
        {
            s.RestTicks = 0;
        }
        if (events.ImpactSpeed > def.CrashSafeSpeed)
        {
            float over = events.ImpactSpeed - def.CrashSafeSpeed;
            events.CrashDamage = over * over * def.CrashScale;
            s.Health = MathF.Max(0, s.Health - events.CrashDamage);
            if (s.Health <= 0)
            {
                s.Flags |= ShipFlags.Destroyed;
            }
        }
        if (height > def.Ceiling)
        {
            events.ReachedCeiling = true;
        }
        return events;
    }

    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    /// <summary>Counts the boost down and starts one when asked and ready. Returns whether it is boosting this tick.</summary>
    private static bool TickBoost(ref ShipState s, ShipDef def, bool asked, ref ShipEvents events)
    {
        if (s.BoostTicks > 0)
        {
            s.BoostTicks--;
            if (s.BoostTicks == 0)
            {
                s.CooldownTicks = (ushort)Math.Round(def.BoostCooldownSeconds * CharacterMotor.TickRate);
            }
            return true;
        }
        if (s.CooldownTicks > 0)
        {
            s.CooldownTicks--;
            return false;
        }
        if (asked)
        {
            s.BoostTicks = (ushort)Math.Max(1, Math.Round(def.BoostSeconds * CharacterMotor.TickRate) - 1);
            events.BoostStarted = true;
            return true;
        }
        return false;
    }

    /// <summary>Aerodynamic drag as an acceleration in the ship's frame, for a velocity in the ship's frame.</summary>
    private static Vector3 Drag(Vector3 vLocal, float density, ShipDef def)
    {
        float speed = vLocal.Length();
        if (speed < 1e-6f || density <= 0)
        {
            return Vector3.Zero;
        }
        float k = -0.5f * density * speed / def.Mass;
        return new Vector3(vLocal.X * def.DragSide, vLocal.Y * def.DragTop, vLocal.Z * def.DragForward) * k;
    }

    // The sea's lift on the hull, as an acceleration in the ship's frame.
    private static Vector3 Buoyancy(Quaternion qi, Vector3 up, float submerged, float gravity, ShipDef def) =>
        submerged <= 0 ? Vector3.Zero : Vector3.Transform(up * (WaterDensity * HullVolume(def) * submerged * gravity / def.Mass), qi);

    private static float HullVolume(ShipDef def)
    {
        float volume = 0;
        foreach (HullBox box in def.Hull)
        {
            if (!box.Skid)
            {
                volume += 8 * box.HalfExtents.X * box.HalfExtents.Y * box.HalfExtents.Z;
            }
        }
        return volume;
    }

    /// <summary>Whether the skids still find ground below (a sleeping ship checks now and then).</summary>
    private static bool Supported(in ShipState s, ShipDef def, in ShipContext ctx, uint ignoreBody)
    {
        ctx.Collider!.World.EnsureGround(s.Position);
        Vector3 down = -Vector3.Transform(Vector3.UnitY, s.Rotation);
        int touching = 0;
        foreach (Vector3 pad in def.SkidPoints)
        {
            Vector3 at = s.Position + Vector3.Transform(pad - def.CentreOfMass, s.Rotation);
            if (ctx.Collider.Ray(at - down * SkidProbe, down, SkidProbe + def.SkidTravel, ignoreBody, out ShipRayHit hit) &&
                hit.Distance < SkidProbe + 0.05f)
            {
                touching++;
            }
        }
        return touching >= 3;
    }

    /// <summary>The ship's inverse inertia applied to a world-frame vector.</summary>
    private static Vector3 InverseInertia(in ShipState s, ShipDef def, Vector3 world) =>
        Vector3.Transform(Vector3.Transform(world, Quaternion.Conjugate(s.Rotation)) / def.Inertia, s.Rotation);

    /// <summary>
    /// An impulse at a contact (arm from the centre of mass, normal out of what was hit) that stops the hull going into
    /// it (bouncing back by <paramref name="restitution"/>) and takes out sliding up to the hull's friction. Returns the
    /// speed it went in at (0 if it was leaving).
    /// </summary>
    private static float Collide(ref ShipState s, ShipDef def, Vector3 arm, Vector3 normal, float restitution)
    {
        Vector3 spin = Vector3.Transform(s.AngularVelocity, s.Rotation);
        Vector3 u = s.Velocity + Vector3.Cross(spin, arm);
        float un = Vector3.Dot(u, normal);
        if (un >= 0)
        {
            return 0;
        }
        Vector3 rn = Vector3.Cross(arm, normal);
        float k = 1 / def.Mass + Vector3.Dot(rn, InverseInertia(s, def, rn));
        float jn = -(1 + restitution) * un / k;
        Vector3 impulse = normal * jn;
        Vector3 ut = u - normal * un;
        float slide = ut.Length();
        if (slide > 1e-5f)
        {
            Vector3 t = ut / slide;
            Vector3 rt = Vector3.Cross(arm, t);
            float kt = 1 / def.Mass + Vector3.Dot(rt, InverseInertia(s, def, rt));
            impulse -= t * MathF.Min(slide / kt, HullFriction * jn);
        }
        Apply(ref s, def, arm, impulse);
        return -un;
    }

    /// <summary>
    /// Takes a pad's sliding out, the pad's friction impulse over the substep (<paramref name="given"/> so far) staying
    /// within <paramref name="most"/> (N·s). Returns the impulse given in all.
    /// </summary>
    private static Vector3 Friction(ref ShipState s, ShipDef def, Vector3 arm, Vector3 normal, float most, Vector3 given)
    {
        Vector3 spin = Vector3.Transform(s.AngularVelocity, s.Rotation);
        Vector3 u = s.Velocity + Vector3.Cross(spin, arm);
        Vector3 ut = u - normal * Vector3.Dot(u, normal);
        float slide = ut.Length();
        if (slide < 1e-6f || most <= 0)
        {
            return given;
        }
        Vector3 t = ut / slide;
        Vector3 rt = Vector3.Cross(arm, t);
        float k = 1 / def.Mass + Vector3.Dot(rt, InverseInertia(s, def, rt));
        Vector3 total = given - t * (slide / k);
        float length = total.Length();
        if (length > most)
        {
            total *= most / length;
        }
        Apply(ref s, def, arm, total - given);
        return total;
    }

    private static void Apply(ref ShipState s, ShipDef def, Vector3 arm, Vector3 impulse)
    {
        s.Velocity += impulse / def.Mass;
        Vector3 spin = Vector3.Transform(s.AngularVelocity, s.Rotation) + InverseInertia(s, def, Vector3.Cross(arm, impulse));
        s.AngularVelocity = Vector3.Transform(spin, Quaternion.Conjugate(s.Rotation));
    }

    // Flight assist off: Newtonian, but no faster than the boost (Elite caps it too) and no wilder a spin than half again
    // the most rates.
    private static void ClampFlightAssistOff(ref ShipState s, ShipDef def)
    {
        float speed = s.Velocity.Length();
        float most = def.BoostSpeed * 1.05f;
        if (speed > most)
        {
            s.Velocity *= most / speed;
        }
        Vector3 w = s.AngularVelocity;
        s.AngularVelocity = new Vector3(Math.Clamp(w.X, -def.Pitch.MaxRate * 1.5f, def.Pitch.MaxRate * 1.5f),
            Math.Clamp(w.Y, -def.Yaw.MaxRate * 1.5f, def.Yaw.MaxRate * 1.5f), Math.Clamp(w.Z, -def.Roll.MaxRate * 1.5f, def.Roll.MaxRate * 1.5f));
    }

    /// <summary>Turns a rotation by an angular velocity in its own frame for a time.</summary>
    public static Quaternion Integrate(Quaternion q, Vector3 bodyRate, float seconds)
    {
        float angle = bodyRate.Length() * seconds;
        if (angle < 1e-9f)
        {
            return q;
        }
        Vector3 axis = bodyRate / bodyRate.Length();
        float half = angle / 2;
        float sin = (float)DMath.Sin(half);
        var delta = new Quaternion(axis * sin, (float)DMath.Cos(half));
        return Quaternion.Normalize(q * delta);
    }

    // The ground's tiles end at ±8.2 km: the ship is held inside the zone's square (G8 lifts this).
    private static void KeepInZone(ref ShipState s, float edge)
    {
        Vector3 p = s.Position;
        bool out_ = false;
        if (MathF.Abs(p.X) > edge)
        {
            p.X = MathF.CopySign(edge, p.X);
            if (s.Velocity.X * p.X > 0)
            {
                s.Velocity.X = 0;
            }
            out_ = true;
        }
        if (MathF.Abs(p.Z) > edge)
        {
            p.Z = MathF.CopySign(edge, p.Z);
            if (s.Velocity.Z * p.Z > 0)
            {
                s.Velocity.Z = 0;
            }
            out_ = true;
        }
        if (out_)
        {
            s.Position = p;
            s.Flags |= ShipFlags.AtEdge;
        }
    }

    // ---------------------------------------------------------------- frames and helpers

    /// <summary>
    /// A ship standing level at a point: its pivot on <paramref name="ground"/>, up along <paramref name="up"/>, its nose
    /// toward <paramref name="yaw"/> (the characters' convention: 0 faces north, −Z). The state's position is the centre of
    /// mass above that.
    /// </summary>
    public static ShipState Standing(ShipDef def, Vector3 ground, Vector3 up, float yaw)
    {
        Quaternion rotation = Orientation(up, yaw);
        return new ShipState
        {
            Position = ground + Vector3.Transform(def.CentreOfMass, rotation),
            Rotation = rotation,
            Health = def.Health,
            Flags = ShipFlags.Landed,
        };
    }

    /// <summary>The rotation whose +Y is <paramref name="up"/> and whose nose (+Z) points along a characters' yaw.</summary>
    public static Quaternion Orientation(Vector3 up, float yaw)
    {
        (Vector3 forward, _) = CharacterMotor.HorizontalBasis(yaw, up);
        return Basis(forward, up);
    }

    /// <summary>The rotation from a nose direction and an up (made square to it).</summary>
    public static Quaternion Basis(Vector3 forward, Vector3 up)
    {
        Vector3 z = Vector3.Normalize(forward);
        Vector3 x = Vector3.Normalize(Vector3.Cross(up, z));
        Vector3 y = Vector3.Cross(z, x);
        var m = new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m));
    }

    /// <summary>The characters' yaw of a ship's nose, laid on the horizontal at a point with this up.</summary>
    public static float YawOf(Quaternion rotation, Vector3 up)
    {
        Vector3 forward = Vector3.Transform(Vector3.UnitZ, rotation);
        Vector3 flat = forward - up * Vector3.Dot(forward, up);
        if (flat.LengthSquared() < 1e-8f)
        {
            flat = Vector3.Transform(-Vector3.UnitY, rotation); // straight up or down: the belly's way
            flat -= up * Vector3.Dot(flat, up);
        }
        // HorizontalBasis(yaw).forward = (−sin, 0, −cos) on flat ground: yaw = atan2(−x, −z) in the plane of up.
        (Vector3 north, Vector3 east) = CharacterMotor.HorizontalBasis(0, up);
        return (float)DMath.Atan2(-Vector3.Dot(flat, east), Vector3.Dot(flat, north));
    }
}
