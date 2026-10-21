using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Hollow.Core;
using Hollow.Simulation.Content;
using Hollow.Simulation.Movement;

namespace Hollow.Simulation.Ships;

/// <summary>
/// A rotor (docs/MINICOPTER.md §5, the KA-FA 1500, Rust's minicopter our way): one thrust along the craft's up, set each
/// tick to hold the vertical speed the collective asks for (none held: hold the height), so the craft moves by tilting.
/// The stick asks for a tilt of the rotor from the ground's up along the heading, not a rate, so with it centred the craft
/// levels itself; the pedals ask for a yaw rate, and at speed the tail turns the nose into the air it flies through. The
/// rotor's lift is <see cref="Lift"/> × spool² × the air's share (thinner air lifts less: the craft's ceiling), the spool
/// (<see cref="ShipState.Spool"/>) rising while the engine runs and falling while it doesn't; under water the engine
/// drowns. On its skids with the collective not raised the rotor stays flat and the stick does nothing.
/// Its lift may come from its modules (docs/MINICOPTER.md §4, VEHICLES.md §9.5): an engine's <c>lift</c> and a rotor's
/// <c>rotor</c> in the build's drive sums (<see cref="Compose"/>); with no rotor turning on it, it has none.
/// </summary>
public sealed record ShipRotor : IShipDrive
{
    public const string Key = "rotor";

    public string Kind => Key;

    /// <summary>The rotor works on the ground: it spools up and lifts off from its skids.</summary>
    public bool IdlesOnSkids => false;

    /// <summary>
    /// The rotor's most thrust at full spool in air of <see cref="AirReference"/> (N): the frame's own (ships.json
    /// <c>drive.lift</c>) and, composed, its engine modules' (<see cref="Compose"/>); 0 without a rotor.
    /// </summary>
    public float Lift { get; init; }

    /// <summary>The frame carries its own rotor (ships.json <c>drive.rotor</c>, true by default); false: a rotor module does.</summary>
    public bool OwnRotor { get; init; } = true;

    /// <summary>Seconds from still to working speed with the engine running, and back to still without it.</summary>
    public float SpoolUp { get; init; } = 5;

    public float SpoolDown { get; init; } = 7;

    /// <summary>The air's share of the lift: (density / reference)^exponent, at most the cap.</summary>
    public float AirReference { get; init; } = 1.225f;

    public float AirExponent { get; init; } = 0.5f;

    public float AirCap { get; init; } = 1.3f;

    /// <summary>The most tilt the stick asks for, nose down or up and to either side (radians).</summary>
    public float MaxPitch { get; init; }

    public float MaxRoll { get; init; }

    /// <summary>How fast it turns toward the wanted tilt: radians a second per radian off (1/s).</summary>
    public float LevelGain { get; init; } = 3;

    /// <summary>The vertical speeds the collective asks for at full travel (m/s), and how soon it reaches one (s).</summary>
    public float Climb { get; init; }

    public float Sink { get; init; }

    public float Response { get; init; } = 0.4f;

    /// <summary>
    /// Weathercocking: radians a second of yaw toward the air's way per unit of sideslip (sideways over horizontal speed),
    /// growing from 3 m/s ahead to its whole at 15 m/s.
    /// </summary>
    public float Weathervane { get; init; } = 1;

    /// <summary>Tipped past this (radians from level) the rotor's pull is useless: the collective lets go.</summary>
    public float Tipped { get; init; } = MathF.PI / 3;

    /// <summary>The engine drowns with this much of the hull's water band under the sea (0 … 1).</summary>
    public float Floods { get; init; } = 0.6f;

    internal static IShipDrive Read(JsonFields f, JsonElement block, string where)
    {
        f.OnlyKnown(block, where, "kind", "lift", "rotor", "spoolUp", "spoolDown", "airReference", "airExponent", "airCap", "pitch", "roll",
            "level", "climb", "sink", "response", "weathervane", "tipped", "floods");
        const float degrees = MathF.PI / 180;
        return new ShipRotor
        {
            Lift = f.Number(block, "lift", where, 0),
            OwnRotor = f.Flag(block, "rotor", where, true),
            SpoolUp = f.Number(block, "spoolUp", where, 5),
            SpoolDown = f.Number(block, "spoolDown", where, 7),
            AirReference = f.Number(block, "airReference", where, 1.225f),
            AirExponent = f.Number(block, "airExponent", where, 0.5f),
            AirCap = f.Number(block, "airCap", where, 1.3f),
            MaxPitch = f.Number(block, "pitch", where) * degrees,
            MaxRoll = f.Number(block, "roll", where) * degrees,
            LevelGain = f.Number(block, "level", where, 3),
            Climb = f.Number(block, "climb", where),
            Sink = f.Number(block, "sink", where),
            Response = f.Number(block, "response", where, 0.4f),
            Weathervane = f.Number(block, "weathervane", where, 1),
            Tipped = f.Number(block, "tipped", where, 60) * degrees,
            Floods = f.Number(block, "floods", where, 0.6f),
        };
    }

    /// <summary>
    /// The rotor a build makes (VEHICLES.md §9.5's drive sums, docs/MINICOPTER.md §4.2): the engines' <c>lift</c> added to
    /// the frame's, while a rotor turns on it (its own, or a module's <c>rotor</c>); none: no lift at all.
    /// </summary>
    public IShipDrive Compose(IReadOnlyDictionary<string, float> sums)
    {
        float rotors = sums.TryGetValue("rotor", out float r) ? r : 0;
        float lift = Lift + (sums.TryGetValue("lift", out float l) ? l : 0);
        return this with { Lift = OwnRotor || rotors > 0 ? lift : 0 };
    }

    /// <summary>The air's share of the rotor's lift at a density (kg/m³).</summary>
    public float AirFactor(float density) =>
        density <= 0 || AirReference <= 0 ? 0 : MathF.Min(AirCap, (float)DMath.Pow(density / AirReference, AirExponent));

    /// <summary>The engine drowns: too much of the hull is under the sea.</summary>
    public bool Drowned(float submerged) => submerged >= Floods;

    /// <summary>The rotor's most thrust now, as an acceleration of the craft (m/s²).</summary>
    public float MostLift(in ShipState s, float density, float submerged, ShipDef def) =>
        Drowned(submerged) ? 0 : Lift * s.Spool * s.Spool * AirFactor(density) / def.Mass;

    public void Advance(ref ShipState s, in ShipDriveContext c, in ShipInput input, ShipDef def)
    {
        const float dt = CharacterMotor.DeltaTime;
        // No lift, no engine to run: a frame without its engine or rotor (docs/MINICOPTER.md §4.1). In the sea the blades
        // meet the water and stop at once, and the drowned engine won't turn them while the hull floats that low.
        bool drowned = Drowned(c.Submerged);
        bool runs = c.Engines && Lift > 0 && !drowned;
        s.Spool = drowned ? 0 : runs ? MathF.Min(1, s.Spool + dt / MathF.Max(SpoolUp, dt)) : MathF.Max(0, s.Spool - dt / MathF.Max(SpoolDown, dt));
        if (runs || s.Spool > 0)
        {
            // A turning rotor never sleeps: the craft is never parked under it, and its spin goes on to the end.
            s.RestTicks = 0;
        }
    }

    public void Command(in ShipDriveContext c, ref ShipState s, in ShipInput input, ShipDef def, out Vector3 thrust, out Vector3 turn)
    {
        const float dt = CharacterMotor.DeltaTime;
        Quaternion q = c.Rotation;
        Vector3 up = c.Up;
        Vector3 bodyUp = Vector3.Transform(Vector3.UnitY, q);
        float upness = Vector3.Dot(bodyUp, up);
        float most = MostLift(s, c.Density, c.Submerged, def);
        // Sitting: on its skids with the collective not raised, the rotor stays flat and the stick does nothing.
        bool sitting = s.Has(ShipFlags.Touching) && input.Vertical <= 0;

        // ---------------------------------------------------------------- the collective
        float lift = 0;
        if (!sitting && most > 0 && upness > (float)DMath.Cos(Tipped))
        {
            float want = input.Vertical >= 0 ? input.Vertical * Climb : input.Vertical * Sink;
            if (want < 0 && c.OverGround < ShipMotor.LandingAssistHeight)
            {
                // Coming down near the ground, the descent eases (the Tin Finch's landing assist).
                want = MathF.Max(want, -(def.LandingDescent + MathF.Max(c.OverGround, 0) * def.LandingDescentPerMetre));
            }
            Vector3 velocity = Vector3.Transform(c.VelocityLocal, q);
            float rising = Vector3.Dot(velocity, up);
            Vector3 others = Vector3.Transform(ShipMotor.Drag(c.VelocityLocal, c.Density, def) +
                ShipMotor.Buoyancy(c.Inverse, up, c.Submerged, c.World.Gravity, def), q);
            float need = ((want - rising) / MathF.Max(Response, dt) + c.World.Gravity - Vector3.Dot(others, up)) / MathF.Max(upness, 0.5f);
            lift = Math.Clamp(need, 0, most);
        }
        thrust = new Vector3(0, lift, 0);

        // ---------------------------------------------------------------- the stick, the pedals and the tail
        if (sitting || most <= 0)
        {
            turn = Vector3.Zero;
            return;
        }
        Vector3 forward = Vector3.Transform(Vector3.UnitZ, q);
        Vector3 flat = forward - up * Vector3.Dot(forward, up);
        if (flat.LengthSquared() < 1e-6f)
        {
            // Nose straight up or down: the heading is the belly's way.
            flat = Vector3.Transform(-Vector3.UnitY, q);
            flat -= up * Vector3.Dot(flat, up);
        }
        flat = Vector3.Normalize(flat);
        Vector3 right = Vector3.Cross(flat, up);
        // The wanted tilt: + nose down along the heading, + right side down.
        float noseDown = Math.Clamp(-input.Pitch, -1, 1) * MaxPitch;
        float rollRight = Math.Clamp(input.Roll, -1, 1) * MaxRoll;
        Vector3 wantUp = Vector3.Normalize(up + flat * Tan(noseDown) + right * Tan(rollRight));
        Vector3 axis = Vector3.Cross(bodyUp, wantUp);
        float sin = axis.Length();
        float angle = (float)DMath.Atan2(sin, Vector3.Dot(bodyUp, wantUp));
        // The error in the craft's frame: x + nose down, z + roll right (its y, about the rotor's axis, is nothing).
        Vector3 error = sin > 1e-7f ? Vector3.Transform(axis / sin, c.Inverse) * angle : Vector3.Zero;
        var rates = new Vector3(error.X * LevelGain, -Math.Clamp(input.Yaw, -1, 1) * def.Yaw.MaxRate, error.Z * LevelGain);
        // The air's way over the ground, measured against the heading (a banked craft's body axes would hide the slip).
        Vector3 moving = Vector3.Transform(c.VelocityLocal, q);
        moving -= up * Vector3.Dot(moving, up);
        float ahead = Math.Clamp((Vector3.Dot(moving, flat) - 3) / 12, 0, 1);
        if (ahead > 0)
        {
            // Sideways air turns the nose into it: slipping right turns the nose right (−y).
            rates.Y -= Weathervane * ahead * Vector3.Dot(moving, right) / MathF.Max(moving.Length(), 1);
        }
        rates = new Vector3(Math.Clamp(rates.X, -def.Pitch.MaxRate, def.Pitch.MaxRate), Math.Clamp(rates.Y, -def.Yaw.MaxRate, def.Yaw.MaxRate),
            Math.Clamp(rates.Z, -def.Roll.MaxRate, def.Roll.MaxRate));
        Vector3 angular = (rates - s.AngularVelocity) / dt + s.AngularVelocity * (def.TurnDrag * c.AirRatio + c.Submerged * 2f);
        // The rotor's authority over the craft grows with its spin (the cyclic and the tail rotor both).
        float authority = MathF.Min(1, s.Spool * s.Spool * AirFactor(c.Density));
        turn = new Vector3(Math.Clamp(angular.X, -def.Pitch.Acceleration * authority, def.Pitch.Acceleration * authority),
            Math.Clamp(angular.Y, -def.Yaw.Acceleration * authority, def.Yaw.Acceleration * authority),
            Math.Clamp(angular.Z, -def.Roll.Acceleration * authority, def.Roll.Acceleration * authority));
    }

    private static float Tan(float angle) => (float)(DMath.Sin(angle) / DMath.Cos(angle));

    /// <summary>The collective's share of the rotor's whole lift: the fuel burns from idle to full by it.</summary>
    public float Effort(Vector3 thrust, ShipDef def) => Lift > 0 ? Math.Clamp(thrust.Y * def.Mass / Lift, 0, 1) : 0;

    /// <summary>The collective's share, along up (what the view's wash and the sound's load follow).</summary>
    public Vector3 Output(Vector3 thrust, ShipDef def) => new(0, Lift > 0 ? Math.Clamp(thrust.Y * def.Mass / Lift, 0, 2) : 0, 0);

    public IEnumerable<string> Problems(ShipDef ship)
    {
        if (Lift < 0 || (Lift > 0 && !(Lift > ship.Mass * 9.80665f * 1.05f)))
        {
            // A frame whose lift comes from its modules has none of its own (its default build's is a test's to measure).
            yield return "rotor: its lift, if it has its own, must carry the craft at 1 g with some to spare";
        }
        if (!(SpoolUp > 0) || !(SpoolDown > 0) || !(Response > 0) || !(LevelGain > 0))
        {
            yield return "rotor: positive spool times, response and level gain";
        }
        if (!(MaxPitch > 0 && MaxPitch < Tipped) || !(MaxRoll > 0 && MaxRoll < Tipped) || !(Tipped > 0 && Tipped < MathF.PI / 2))
        {
            yield return "rotor: the stick's tilts between 0 and the tipped angle, which is under 90°";
        }
        if (!(Climb > 0) || !(Sink > 0) || !(AirReference > 0) || !(AirExponent >= 0) || !(AirCap > 0) || !(Floods > 0 && Floods <= 1))
        {
            yield return "rotor: positive climb, sink and air numbers, and a flooding share in (0, 1]";
        }
        if (new[] { ship.Pitch, ship.Yaw, ship.Roll }.Any(t => t.MaxRate <= 0 || t.Acceleration <= 0))
        {
            yield return "rates: positive rates and accelerations";
        }
    }
}
