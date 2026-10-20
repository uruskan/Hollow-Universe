using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation.Movement;

namespace Hollow.Simulation.Ships;

/// <summary>
/// One wheel (ships.json <c>ground.wheels</c>, a module's <c>wheels</c>; VEHICLES.md §6.2), in the ship's frame: its hub at
/// ride height (the tyre touching flat ground at half its travel), its tyre's radius and width, its suspension (travel,
/// spring, damper), its grip, and what it does: steers, is driven, holds with the handbrake.
/// </summary>
public sealed record WheelDef(string Key, Vector3 Hub, float Radius, float Width, float Travel, float Stiffness, float Damping, float Grip,
    bool Steers, bool Drives, bool Handbrake)
{
    /// <summary>This wheel moved by an offset (a module's, set at its socket).</summary>
    public WheelDef Moved(Vector3 by, string prefix) => this with { Hub = Hub + by, Key = prefix + Key };
}

/// <summary>
/// How a vehicle on wheels drives (ships.json <c>ground</c>, VEHICLES.md §6): top speeds forward and back, the most the
/// brakes and the tyres can push, the steering's lock at a crawl and at top speed, rolling resistance, what grip the rear
/// tyres keep with the handbrake on, and how far over the ground the tyres' grip acts (as Unity's wheels' force point: a
/// tall truck turning hard slides before it rolls, a tall build on it rolls first), and the engine's braking off the
/// throttle as a share of the brakes; and running people over (VEHICLES.md §6.4): above <see cref="RunOverSpeed"/> (m/s) the
/// square of what is over it times <see cref="RunOverScale"/>. Its power is its engines' (<see cref="ShipDef.EnginePower"/>).
/// </summary>
public sealed record GroundDef(float TopSpeed, float ReverseSpeed, float Brake, float Traction, float Steer, float SteerFast, float Rolling,
    float HandbrakeGrip, float GripHeight = 0, float Coast = 0.1f, float RunOverSpeed = 4, float RunOverScale = 2);

/// <summary>
/// A vehicle on wheels (VEHICLES.md §6.2): the ship's six degrees of freedom under the planet's gravity, held up by a sprung
/// wheel at each hub. Each substep a ray from each hub's bump stop meets the static world (ground, trees, rocks, building,
/// deployables, parked ships: what the ships' collider queries); its spring and damper push along the ground's normal; then
/// each loaded tyre's grip: drive, brakes and rolling along it, the sliding across it taken out, all within its friction
/// (grip × load). No wheel spins in the state: traction is the engines' power over the speed, capped by the grip. The hull
/// (the chassis and its modules' boxes) is swept as a ship's is, the ground turns under it as the ball curves, and it rests
/// and sleeps as a ship does. Input: <see cref="ShipInput.Throttle"/> forward and back (the brakes against the motion),
/// <see cref="ShipInput.Yaw"/> the steering (+ right), <see cref="ShipInput.Vertical"/> over a half the handbrake.
/// </summary>
public sealed class GroundMotor : IShipMotor
{
    public const string Key = "ground";

    public static readonly GroundMotor Instance = new();

    /// <summary>Substeps a tick: springs as soft as an off-road truck's stay steady at four.</summary>
    public const int Substeps = 4;

    /// <summary>Passes over the tyres' sideways grip a substep (each wheel pulls on the others through the body's turn).</summary>
    private const int GripPasses = 4;

    /// <summary>Below this along its way, with no throttle, the brakes hold it where it is (it doesn't creep down a slope).</summary>
    public const float HoldSpeed = 1.5f;

    /// <summary>The drive's force is the power over at least this speed (m/s): the most a standing start gets is the traction cap.</summary>
    private const float LeastDriveSpeed = 3f;

    /// <summary>Over the last this many m/s before its top speed the drive fades to nothing.</summary>
    private const float TopFade = 3f;

    /// <summary>A bump stop this many times stiffer than the spring.</summary>
    private const float BumpStop = 20f;

    private const float Restitution = 0.1f;

    /// <summary>The most wheels a vehicle has (a car's four, a long one's six, a track's road wheels later).</summary>
    public const int MostWheels = 16;

    private struct WheelLoad
    {
        public int Wheel;
        public Vector3 Arm;
        public Vector3 Normal;
        public float Force;
        public Vector3 Forward;
        public Vector3 Side;
        public Vector3 Given;
    }

    string IShipMotor.Key => Key;

    public bool Flies => false;

    public IEnumerable<string> Problems(ShipDef ship)
    {
        if (ship.Ground is not { } g)
        {
            yield return "a vehicle on wheels has a \"ground\" block";
            yield break;
        }
        if (g.TopSpeed is <= 0 or > 80 || g.ReverseSpeed < 0 || g.Brake <= 0 || g.Traction <= 0 || g.Steer is <= 0 or > 1.2f ||
            g.SteerFast < 0 || g.SteerFast > g.Steer || g.Rolling is < 0 or > 0.2f || g.HandbrakeGrip is < 0 or > 1 || g.GripHeight is < 0 or > 2 ||
            g.Coast is < 0 or > 1)
        {
            yield return "ground: a top speed of 0 … 80 m/s, reverse 0 or more, positive brakes and traction, steering of 0 … 70° " +
                "that narrows with speed, rolling 0 … 0.2, the handbrake's grip 0 … 1, the grip's height 0 … 2 m, coasting 0 … 1";
        }
        if (ship.Wheels.Count > MostWheels || (ship.Wheels.Count < 2 && ship.Sockets.Count == 0))
        {
            yield return $"2 … {MostWheels} wheels (a kind that takes modules may leave them to its running gear)";
        }
        foreach (WheelDef w in ship.Wheels)
        {
            if (w.Radius <= 0 || w.Width <= 0 || w.Travel <= 0 || w.Stiffness <= 0 || w.Damping <= 0 || w.Grip is <= 0 or > 2)
            {
                yield return $"wheel \"{w.Key}\": a positive radius, width, travel, spring and damper, grip 0 … 2";
            }
        }
    }

    /// <summary>How far the front wheels turn for full steering at a speed (m/s): the lock narrows as it goes faster.</summary>
    public static float SteerAngle(GroundDef g, float speed) =>
        g.Steer + (g.SteerFast - g.Steer) * Math.Clamp(MathF.Abs(speed) / g.TopSpeed, 0, 1);

    /// <summary>The drive's whole force for a throttle at a speed along the vehicle's way (N; signed as the throttle).</summary>
    public static float DriveForce(ShipDef def, float throttle, float speed)
    {
        GroundDef g = def.Ground!;
        if (throttle == 0 || def.EnginePower <= 0)
        {
            return 0;
        }
        float top = throttle > 0 ? g.TopSpeed : g.ReverseSpeed;
        float along = throttle > 0 ? speed : -speed;
        float fade = Math.Clamp((top - along) / TopFade, 0, 1);
        return throttle * fade * MathF.Min(def.EnginePower / MathF.Max(MathF.Abs(speed), LeastDriveSpeed), g.Traction);
    }

    public ShipEvents Step(ref ShipState s, in ShipInput input, ShipDef def, in ShipContext ctx, uint ignoreBody)
    {
        var events = new ShipEvents();
        const float dt = CharacterMotor.DeltaTime;
        GroundDef g = def.Ground!;
        if (s.Has(ShipFlags.Destroyed) || s.Has(ShipFlags.InOrbit))
        {
            s.Thrust = Vector3.Zero;
            s.Flags &= ~(ShipFlags.EnginesOn | ShipFlags.Boosting);
            return events;
        }
        bool engines = input.Engines && !s.Has(ShipFlags.EngineStopped) && s.Fuel > 0;
        float throttle = engines ? Math.Clamp(input.Throttle, -1, 1) : 0;
        bool wants = engines && (throttle != 0 || input.Yaw != 0);
        ShipCollider? collider = ctx.Collider;

        // Asleep: costs nothing until the driver asks for motion, or the ground under its wheels goes.
        if (s.Asleep)
        {
            if (!wants)
            {
                s.RestTicks = s.RestTicks >= 60_000 ? (ushort)ShipMotor.SleepTicks : (ushort)(s.RestTicks + 1);
                s.Thrust = Vector3.Zero;
                s.Flags = engines ? s.Flags | ShipFlags.EnginesOn : s.Flags & ~ShipFlags.EnginesOn;
                Burn(ref s, def, engines, 0, ref events);
                bool check = (s.RestTicks - ShipMotor.SleepTicks) % ShipMotor.SupportCheckTicks == 0;
                if (!check || collider == null || OnWheels(s, def, collider, ignoreBody))
                {
                    return events;
                }
            }
            s.RestTicks = 0;
        }

        ShipState start = s;
        Vector3 up = ctx.Frame.UpAt(s.Position);
        Vector3D bodyPoint = ctx.Frame.ToBody(s.Position);
        float height = (float)(bodyPoint.Length - ctx.Surface.Radius);
        float density = ctx.AirDensity * (float)DMath.Exp(-Math.Max(height, 0) / Math.Max(ctx.ScaleHeight, 1));
        float airRatio = ctx.AirDensity > 0 ? density / ctx.AirDensity : 0;
        Burn(ref s, def, engines, MathF.Abs(throttle), ref events);
        if (events.OutOfFuel)
        {
            engines = false;
            throttle = 0;
        }
        bool handbrake = !engines || input.Vertical > 0.5f;
        s.Thrust = new Vector3(0, 0, throttle);
        s.Flags = (s.Flags & ~(ShipFlags.EnginesOn | ShipFlags.Boosting | ShipFlags.AtEdge | ShipFlags.Wet))
            | (engines ? ShipFlags.EnginesOn : ShipFlags.None);

        float speed = s.Velocity.Length();
        float reach = def.BoundingRadius + speed * dt * 2 + 2;
        if (collider != null)
        {
            collider.World.PrefetchAlong(s.Position, s.Position + s.Velocity * ShipMotor.PrefetchSeconds, reach);
            collider.World.EnsureGroundAround(s.Position, reach);
        }
        IReadOnlyList<WheelDef> wheels = def.Wheels;
        int driven = 0;
        foreach (WheelDef w in wheels)
        {
            driven += w.Drives ? 1 : 0;
        }
        float steer = Math.Clamp(input.Yaw, -1, 1) * SteerAngle(g, Vector3.Dot(s.Velocity, s.Forward));
        Span<WheelLoad> loads = stackalloc WheelLoad[Math.Min(wheels.Count, MostWheels)];
        Span<ShipContact> contacts = stackalloc ShipContact[def.MostContacts];
        int loaded = 0;
        float closing = 0;
        Vector3 groundNormal = Vector3.Zero;
        bool touched = false;
        const float h = dt / Substeps;
        float m = def.Mass;
        for (int step = 0; step < Substeps; step++)
        {
            Vector3 from = s.Position, under = ctx.Frame.UpAt(from);
            Quaternion q = s.Rotation, qi = Quaternion.Conjugate(q);
            Vector3 vLocal = Vector3.Transform(s.Velocity, qi);
            Vector3 accel = Vector3.Transform(ShipMotor.Drag(vLocal, density, def), q) - under * ctx.Gravity;
            Vector3 angular = -s.AngularVelocity * (def.TurnDrag * airRatio);
            Vector3 bodyUp = Vector3.Transform(Vector3.UnitY, q), bodyForward = Vector3.Transform(Vector3.UnitZ, q);

            // The springs: each wheel's ray from its bump stop down its travel and its tyre.
            loaded = 0;
            if (collider != null)
            {
                Vector3 spin = Vector3.Transform(s.AngularVelocity, q);
                for (int i = 0; i < wheels.Count && loaded < loads.Length; i++)
                {
                    WheelDef w = wheels[i];
                    Vector3 top = s.Position + Vector3.Transform(w.Hub - def.CentreOfMass, q) + bodyUp * (w.Travel / 2);
                    if (!collider.Ray(top, -bodyUp, w.Travel + w.Radius, ignoreBody, out ShipRayHit hit))
                    {
                        continue;
                    }
                    float compression = w.Travel + w.Radius - hit.Distance;
                    if (compression <= 0)
                    {
                        continue;
                    }
                    Vector3 arm = hit.Point - s.Position;
                    float into = -Vector3.Dot(s.Velocity + Vector3.Cross(spin, arm), hit.Normal);
                    float force = w.Stiffness * MathF.Min(compression, w.Travel) + w.Stiffness * BumpStop * MathF.Max(0, compression - w.Travel) +
                        w.Damping * into;
                    if (force <= 0)
                    {
                        continue;
                    }
                    Vector3 f = hit.Normal * force;
                    accel += f / m;
                    angular += Vector3.Transform(Vector3.Cross(arm, f), qi) / def.Inertia;
                    // The tyre's way along the ground: the body's forward, turned by the steering for a wheel that steers.
                    Vector3 way = w.Steers ? Vector3.Transform(bodyForward, Quaternion.CreateFromAxisAngle(bodyUp, -steer)) : bodyForward;
                    way -= hit.Normal * Vector3.Dot(way, hit.Normal);
                    way = way.LengthSquared() > 1e-8f ? Vector3.Normalize(way) : bodyForward;
                    loads[loaded++] = new WheelLoad
                    {
                        Wheel = i, Arm = arm, Normal = hit.Normal, Force = force, Forward = way, Side = Vector3.Cross(hit.Normal, way),
                    };
                    closing = MathF.Max(closing, into);
                    groundNormal += hit.Normal;
                }
            }
            s.Velocity += accel * h;
            s.AngularVelocity += angular * h;

            // The tyres along their way: the drive shared by the driven wheels, the brakes against the motion, rolling, or
            // held still (no throttle and slow, or the handbrake on a wheel it holds), each within its grip.
            float along = Vector3.Dot(s.Velocity, bodyForward);
            float drive = DriveForce(def, throttle, along);
            bool braking = throttle != 0 && throttle * along < -0.5f;
            bool hold = throttle == 0 && MathF.Abs(along) < HoldSpeed;
            for (int k = 0; k < loaded; k++)
            {
                ref WheelLoad l = ref loads[k];
                WheelDef w = wheels[l.Wheel];
                // The grip acts this far up the body from the contact (GroundDef.GripHeight).
                l.Arm += Vector3.Transform(Vector3.UnitY, s.Rotation) * g.GripHeight;
                float most = w.Grip * l.Force * h;
                float u = Vector3.Dot(PointVelocity(s, l.Arm), l.Forward);
                float stop = u / Effective(s, def, l.Arm, l.Forward);
                float j;
                if (hold || (handbrake && w.Handbrake))
                {
                    j = -stop;
                }
                else if (braking || (throttle == 0 && engines))
                {
                    // The brakes against the motion; off the throttle, the engine's braking (a share of them).
                    float brake = braking ? g.Brake : g.Brake * g.Coast;
                    j = -MathF.Sign(u) * MathF.Min(brake / Math.Max(1, loaded) * h, MathF.Abs(stop));
                }
                else
                {
                    j = (w.Drives && driven > 0 ? drive / driven * h : 0) - MathF.Sign(u) * MathF.Min(g.Rolling * l.Force * h, MathF.Abs(stop));
                }
                j = Math.Clamp(j, -most, most);
                l.Given = l.Forward * j;
                ShipMotor.Apply(ref s, def, l.Arm, l.Given);
            }
            // Across their way: the sliding taken out, the whole tyre's impulse within its grip (the friction circle).
            for (int pass = 0; pass < GripPasses && loaded > 0; pass++)
            {
                for (int k = 0; k < loaded; k++)
                {
                    ref WheelLoad l = ref loads[k];
                    WheelDef w = wheels[l.Wheel];
                    float grip = w.Grip * (handbrake && w.Handbrake && engines ? g.HandbrakeGrip : 1);
                    float most = grip * l.Force * h;
                    float sideways = Vector3.Dot(PointVelocity(s, l.Arm), l.Side);
                    Vector3 total = l.Given - l.Side * (sideways / Effective(s, def, l.Arm, l.Side));
                    float length = total.Length();
                    if (length > most && length > 0)
                    {
                        total *= most / length;
                    }
                    ShipMotor.Apply(ref s, def, l.Arm, total - l.Given);
                    l.Given = total;
                }
            }

            s.Rotation = ShipMotor.Integrate(s.Rotation, s.AngularVelocity, h);
            Vector3 move = s.Velocity * h;
            if (collider != null)
            {
                if (move.LengthSquared() > 1e-12f && collider.Cast(def, s.Position, s.Rotation, move, ignoreBody, out float fraction, out ShipContact hit) &&
                    Vector3.Dot(move, hit.Normal) < 0)
                {
                    s.Position += move * fraction + hit.Normal * 0.002f;
                    events.ImpactSpeed = MathF.Max(events.ImpactSpeed, ShipMotor.Collide(ref s, def, hit.Point - s.Position, hit.Normal, Restitution));
                    touched = true;
                }
                else
                {
                    s.Position += move;
                }
                int found = collider.Overlaps(def, s.Position, s.Rotation, ignoreBody, contacts);
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
                    events.ImpactSpeed = MathF.Max(events.ImpactSpeed, ShipMotor.Collide(ref s, def, contacts[i].Point - s.Position, contacts[i].Normal, 0));
                }
            }
            else
            {
                s.Position += move;
            }
            ShipMotor.KeepInZone(ref s, ctx.Edge > 0 ? ctx.Edge : (float)(ctx.Surface.Radius * ShipMotor.EdgeRadii));
            ShipMotor.CarryOverCurve(ref s, ctx, from, under);
        }

        // ---------------------------------------------------------------- what the step found
        events.Contacts = loaded;
        speed = s.Velocity.Length();
        int standing = Math.Min(3, wheels.Count);
        bool wasLanded = s.Has(ShipFlags.Landed);
        bool onWheels = loaded >= standing && standing > 0;
        s.Flags = onWheels ? s.Flags | ShipFlags.Landed | ShipFlags.Touching : s.Flags & ~(ShipFlags.Landed | ShipFlags.Touching);
        if (onWheels && !wasLanded)
        {
            events.Touchdown = true;
            events.TouchdownSpeed = closing;
        }
        if (!ShipMotor.Finite(s.Position) || !ShipMotor.Finite(s.Velocity) || !ShipMotor.Finite(s.AngularVelocity) || !float.IsFinite(s.Rotation.W))
        {
            s = start;
            s.Velocity = Vector3.Zero;
            s.AngularVelocity = Vector3.Zero;
        }
        // At rest on its wheels (nobody asking it to move), or with nobody driving and held by whatever it touches.
        bool slow = speed < ShipMotor.RestSpeed && s.AngularVelocity.Length() < ShipMotor.RestSpin;
        if ((onWheels && speed < 0.05f && s.AngularVelocity.Length() < 0.02f && !wants) || (!engines && (loaded > 0 || touched) && slow))
        {
            s.RestTicks++;
            if (s.RestTicks >= ShipMotor.SleepTicks)
            {
                s.Velocity = Vector3.Zero;
                s.AngularVelocity = Vector3.Zero;
                s.Thrust = Vector3.Zero;
            }
        }
        else
        {
            s.RestTicks = 0;
        }
        events.CrashDamage = def.CrashDamage(events.ImpactSpeed, Vector3.Dot(s.Up, ctx.Frame.UpAt(s.Position)));
        if (events.CrashDamage > 0)
        {
            s.Health = MathF.Max(0, s.Health - events.CrashDamage);
            if (s.Health <= 0)
            {
                s.Flags |= ShipFlags.Destroyed;
            }
        }
        _ = groundNormal;
        return events;
    }

    // An idle burn while the engine runs, more with the throttle (ships.json fuel: idle and full a second).
    private static void Burn(ref ShipState s, ShipDef def, bool engines, float effort, ref ShipEvents events)
    {
        if (!engines)
        {
            return;
        }
        float requested = (def.FuelIdle + effort * (def.FuelFull - def.FuelIdle)) * CharacterMotor.DeltaTime;
        events.FuelUsed = Math.Min(s.Fuel, requested);
        ShipFuel.Split(s.Fuel, s.FuelReserve, out int units, out float reserve);
        ShipFuel.Burn(ref units, ref reserve, events.FuelUsed);
        s.FuelReserve = reserve;
        s.Fuel = units + reserve;
        if (s.Fuel <= 0)
        {
            s.Fuel = 0;
            events.OutOfFuel = true;
        }
    }

    // The velocity of a point of the body (an arm from its centre of mass), in the zone's frame.
    private static Vector3 PointVelocity(in ShipState s, Vector3 arm) =>
        s.Velocity + Vector3.Cross(Vector3.Transform(s.AngularVelocity, s.Rotation), arm);

    // How hard the body is to move at a point along a direction: an impulse j there changes the point's speed by j × this.
    private static float Effective(in ShipState s, ShipDef def, Vector3 arm, Vector3 direction)
    {
        Vector3 r = Vector3.Cross(arm, direction);
        return 1 / def.Mass + Vector3.Dot(r, ShipMotor.InverseInertia(s, def, r));
    }

    /// <summary>A resting vehicle still has ground under a wheel (a sleeping one checks now and then).</summary>
    private static bool OnWheels(in ShipState s, ShipDef def, ShipCollider collider, uint ignoreBody)
    {
        Vector3 bodyUp = s.Up;
        foreach (WheelDef w in def.Wheels)
        {
            Vector3 top = s.Position + Vector3.Transform(w.Hub - def.CentreOfMass, s.Rotation) + bodyUp * (w.Travel / 2);
            if (collider.Ray(top, -bodyUp, w.Travel + w.Radius + 0.05f, ignoreBody, out _))
            {
                return true;
            }
        }
        return false;
    }
}
