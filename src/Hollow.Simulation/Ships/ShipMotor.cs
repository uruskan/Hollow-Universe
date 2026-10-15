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
        float edge = 0)
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

    /// <summary>
    /// The farthest a ship may be from the world's centre, in metres (0: <see cref="ShipMotor.EdgeRadii"/> world radii, well
    /// past the leaving height: docs/WHOLE_WORLD.md §6.5, a whole world has no edge on the ground).
    /// </summary>
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

    /// <summary>A ship is held within this many world radii of the centre (the leaving height parks it in orbit long before).</summary>
    public const float EdgeRadii = 4f;

    /// <summary>Within this height over the ground (plus a second's travel) the hull and skids look for contacts.</summary>
    public const float NearGround = 40f;

    /// <summary>The skids' rays start this far above each pad.</summary>
    public const float SkidProbe = 0.5f;

    /// <summary>
    /// A ship with its engines off that touches something and moves less than this in a tick (metres, radians) is held
    /// where it stands: its contacts push it about a few millimetres a tick while it goes nowhere (IRON_WAKE.md §5.4).
    /// </summary>
    public const float HeldMetres = 0.005f;

    public const float HeldTurn = 0.002f;

    /// <summary>
    /// A ship with its engines off that touches something (its skids or its hull) and goes slower than this (m/s, rad/s)
    /// is at rest, whatever its contacts do to its position: a carrier on uneven ground, its hull pushed out of a bump
    /// beside its pads a millimetre or a few centimetres a tick, never moves off (IRON_WAKE.md §5.4, the F1-kill finding).
    /// </summary>
    public const float RestSpeed = 0.25f;

    public const float RestSpin = 0.03f;

    /// <summary>A sleeping ship's hull, lowered this far, still overlapping what it rests on: it is supported there.</summary>
    public const float SupportProbe = 0.03f;

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
            // The hull fills a band round its centre of mass (ships.json water: the Tin Finch's 1.2 m each way).
            submerged = Math.Clamp((float)(ctx.Surface.SeaRadius - radius + def.WaterBelow) / (def.WaterBelow + def.WaterAbove), 0, 1);
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
                if (def.LevelPitch > 0 || def.LevelRoll > 0)
                {
                    rates = HoldLevel(rates, q, up, input, def);
                }
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
        // Contacts are looked for within a quarter second's travel of the ground (a tick's sweep is a thirtieth); the
        // ground's tiles are built ahead on worker threads from further up, so a tick seldom waits for one.
        bool near = collider != null && overGround < NearGround + speed * 0.25f + def.BoundingRadius;
        // How far this tick's queries reach from the centre of mass: the hull, a tick's travel (and a little more that
        // thrust may add), the skids' rays. Only ground within it can change the step (GROUND.md §4.6).
        float reach = Reach(def, speed);
        if (collider != null && overGround < 3 * NearGround + speed * 2)
        {
            // The ground along the next second and a half of its way, built on worker threads before the near band
            // needs it: a corridor as wide as the hull's reach, not the 3 × 3 tiles round a point.
            collider.World.PrefetchAlong(s.Position, s.Position + s.Velocity * PrefetchSeconds, reach);
        }
        if (near)
        {
            // Only the tiles this tick's queries can meet: one to four.
            collider!.World.EnsureGroundAround(s.Position, reach);
        }
        int steps = near ? 2 : 1;
        float h = dt / steps;
        // A load for each skid, and as many contacts as the hull has boxes (IRON_WAKE.md §5.4: fixed at 8 and 4 they held
        // the Tin Finch, not a carrier).
        Span<SkidLoad> loads = stackalloc SkidLoad[def.SkidPoints.Count];
        Span<ShipContact> contacts = stackalloc ShipContact[def.MostContacts];
        int loaded = 0;
        float closing = 0;
        Vector3 groundNormal = Vector3.Zero;
        // The hull met something this tick (a sweep's contact or an overlap pushed out): it rests on it, if slow.
        bool touched = false;
        for (int step = 0; step < steps; step++)
        {
            Vector3 from = s.Position, under = ctx.Frame.UpAt(from);
            q = s.Rotation;
            qi = Quaternion.Conjugate(q);
            vLocal = Vector3.Transform(s.Velocity, qi);
            Vector3 accel = Vector3.Transform(thrust + Drag(vLocal, density, def), q) - up * ctx.Gravity;
            Vector3 angular = turn - s.AngularVelocity * (def.TurnDrag * airRatio);
            if (submerged > 0)
            {
                accel += up * (WaterDensity * def.WaterVolume * submerged * ctx.Gravity / m) - s.Velocity * (2f * submerged);
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
                    touched = true;
                }
                else
                {
                    s.Position += move;
                }
                // Out of anything the hull ended up in (a turn into the ground, a pad pressed past its travel).
                int found = collider!.Overlaps(def, s.Position, s.Rotation, ignoreBody, contacts);
                touched |= found > 0;
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
            KeepInZone(ref s, ctx.Edge > 0 ? ctx.Edge : (float)(ctx.Surface.Radius * EdgeRadii));
            // The ground turns under a ship as it goes (docs/WHOLE_WORLD.md §6.5): its attitude and its velocity turn with
            // it, and a straight step over the curve doesn't lift it (only its way up does), so the ball flies as the flat
            // ground did (gravity straight down, no orbit below the leaving height: on a 2.41 km world at 0.92 g about
            // 147 m/s would be one), and straight ahead with the stick still goes round the world at its height.
            Vector3 over = ctx.Frame.UpAt(s.Position);
            double rise = ctx.Frame.ToBody(s.Position).Length - ctx.Frame.ToBody(from).Length - Vector3.Dot(s.Position - from, under);
            s.Position -= over * (float)rise;
            Quaternion carry = Facing.Turn(under, over);
            s.Rotation = Quaternion.Normalize(carry * s.Rotation);
            s.Velocity = Vector3.Transform(s.Velocity, carry);
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
        // At rest: on its skids, slow and level enough (landed); or, its engines off, held where it stands by its skids and
        // whatever else it touches (a big ship's hull on a bump of the ground beside its pads, IRON_WAKE.md §5.4: such a
        // contact keeps a velocity the ground never lets it move by, or pushes the hull out of the ground a few millimetres
        // every tick while it goes nowhere; a ship left on its side, on its hull alone). A ship with nobody at its helm
        // that never rested never slept: it never parked, and a client, which knows a ship at rest by its sleep, offered
        // nothing at it but "land first" (the F1-kill finding, KNOWN_PROBLEMS.md).
        bool barely = Vector3.DistanceSquared(s.Position, start.Position) < HeldMetres * HeldMetres &&
            Vector3.DistanceSquared(Vector3.Transform(Vector3.UnitZ, s.Rotation), Vector3.Transform(Vector3.UnitZ, start.Rotation)) < HeldTurn * HeldTurn &&
            Vector3.DistanceSquared(Vector3.Transform(Vector3.UnitY, s.Rotation), Vector3.Transform(Vector3.UnitY, start.Rotation)) < HeldTurn * HeldTurn;
        bool slow = speed < RestSpeed && s.AngularVelocity.Length() < RestSpin;
        bool held = !input.Engines && (loaded > 0 || touched || submerged > 0) && (barely || slow);
        if ((landed && speed < 0.05f && s.AngularVelocity.Length() < 0.02f && !input.WantsMotion) || held)
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
        if (height > def.CeilingOn(ctx.Surface.Radius))
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
        submerged <= 0 ? Vector3.Zero : Vector3.Transform(up * (WaterDensity * def.WaterVolume * submerged * gravity / def.Mass), qi);

    /// <summary>
    /// Flight assist's level hold (ships.json <c>assist.level</c>, IRON_WAKE.md §5.4): the stick's pitch and roll rates
    /// stop short of the most pitch and roll the ship may take, and with the stick centred the ship comes back toward
    /// level at <see cref="ShipDef.LevelReturn"/> radians a second per radian off. The rates are the ship's own (x: +
    /// nose down, z: + roll right); a heavy carrier never stands on its tail.
    /// </summary>
    private static Vector3 HoldLevel(Vector3 rates, Quaternion q, Vector3 up, in ShipInput input, ShipDef def)
    {
        float k = def.LevelReturn;
        if (def.LevelPitch > 0)
        {
            // The nose's elevation, + up; its rate up is −x.
            float pitch = (float)DMath.Asin(Math.Clamp(Vector3.Dot(Vector3.Transform(Vector3.UnitZ, q), up), -1f, 1f));
            float noseUp = -rates.X;
            if (MathF.Abs(input.Pitch) < 0.05f)
            {
                noseUp = Math.Clamp(-k * pitch, -def.Pitch.MaxRate, def.Pitch.MaxRate);
            }
            else if (noseUp > 0)
            {
                noseUp = MathF.Min(noseUp, k * (def.LevelPitch - pitch));
            }
            else
            {
                noseUp = MathF.Max(noseUp, -k * (def.LevelPitch + pitch));
            }
            rates.X = -noseUp;
        }
        if (def.LevelRoll > 0)
        {
            // The roll, + right wing down: the ship's right (−X) dips below the horizontal.
            float roll = (float)DMath.Asin(Math.Clamp(-Vector3.Dot(Vector3.Transform(-Vector3.UnitX, q), up), -1f, 1f));
            float right = rates.Z;
            if (MathF.Abs(input.Roll) < 0.05f)
            {
                right = Math.Clamp(-k * roll, -def.Roll.MaxRate, def.Roll.MaxRate);
            }
            else if (right > 0)
            {
                right = MathF.Min(right, k * (def.LevelRoll - roll));
            }
            else
            {
                right = MathF.Max(right, -k * (def.LevelRoll + roll));
            }
            rates.Z = right;
        }
        return rates;
    }

    /// <summary>The ground a ship's prefetch builds ahead covers this much of its way.</summary>
    public const float PrefetchSeconds = 1.5f;

    /// <summary>
    /// How far a tick's queries reach from a ship's centre of mass at a speed: its bounding sphere, the tick's travel
    /// with 2 m/s more than it has (what thrust and gravity can add in a tick), the skids' rays and half a metre.
    /// </summary>
    public static float Reach(ShipDef def, float speed) =>
        def.BoundingRadius + (speed + 2f) * CharacterMotor.DeltaTime + SkidProbe + def.SkidTravel + 0.5f;

    /// <summary>
    /// Whether what a sleeping ship rests on is still there (it checks now and then): three of its skids find ground below,
    /// the sea holds it up, or its hull, lowered <see cref="SupportProbe"/>, still meets what it lay on (a ship held on its
    /// hull beside its pads, or on its side).
    /// </summary>
    private static bool Supported(in ShipState s, ShipDef def, in ShipContext ctx, uint ignoreBody)
    {
        ctx.Collider!.World.EnsureGroundAround(s.Position, Reach(def, 0));
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
        if (touching >= 3)
        {
            return true;
        }
        if (ctx.Surface.SeaRadius > 0 && ctx.Surface.SeaRadius + def.WaterBelow > ctx.Frame.ToBody(s.Position).Length)
        {
            return true; // afloat
        }
        Span<ShipContact> contacts = stackalloc ShipContact[1];
        return ctx.Collider.Overlaps(def, s.Position - ctx.Frame.UpAt(s.Position) * SupportProbe, s.Rotation, ignoreBody, contacts) > 0;
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

    // A whole world has no edge on the ground: a ship is only held within a sphere round the centre (the leaving height
    // parks it in orbit long before), its outward speed stopped there.
    private static void KeepInZone(ref ShipState s, float edge)
    {
        float r = s.Position.Length();
        if (r <= edge || r <= 0)
        {
            return;
        }
        Vector3 outward = s.Position / r;
        s.Position = outward * edge;
        float away = Vector3.Dot(s.Velocity, outward);
        if (away > 0)
        {
            s.Velocity -= outward * away;
        }
        s.Flags |= ShipFlags.AtEdge;
    }

    /// <summary>
    /// Rests a parked ship's pose on the wire's grid (IRON_WAKE.md §4.6): its centre of mass on the 1/256 m grid and its
    /// turn on the smallest-three grid, settled until encoding it again changes nothing. A client decodes exactly these
    /// numbers from the ship's state, so the static body it puts in its own physics is the server's, bit for bit: a
    /// character walking up the ramp, or handed over onto the deck, is predicted exactly. A sleeping ship never moves, so
    /// the millimetre this costs is never seen.
    /// </summary>
    public static void RestOnGrid(ref ShipState s)
    {
        s.Position = new Vector3(OnGrid(s.Position.X), OnGrid(s.Position.Y), OnGrid(s.Position.Z));
        Quaternion q = s.Rotation;
        for (int i = 0; i < 4; i++)
        {
            (byte largest, short a, short b, short c) = Quantize.SmallestThree(q);
            Quaternion next = Quantize.FromSmallestThree(largest, a, b, c);
            if (next.Equals(q))
            {
                break;
            }
            q = next;
        }
        s.Rotation = q;
    }

    private static float OnGrid(float value) => Quantize.FromFixed(Quantize.Fixed(value, Quantize.PositionScale), Quantize.PositionScale);

    /// <summary>
    /// A ship that crossed its ceiling parked at its orbit marker (GROUND.md §6.7): out of the physics, still. The zone does
    /// this when no transfer takes it, and a pilot's prediction does the same.
    /// </summary>
    public static void ParkInOrbit(ref ShipState s)
    {
        s.Flags |= ShipFlags.InOrbit;
        s.Flags &= ~(ShipFlags.EnginesOn | ShipFlags.Boosting | ShipFlags.Landed | ShipFlags.Touching);
        s.Velocity = Vector3.Zero;
        s.AngularVelocity = Vector3.Zero;
        s.Thrust = Vector3.Zero;
        s.BoostTicks = 0;
        s.RestTicks = 0;
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
