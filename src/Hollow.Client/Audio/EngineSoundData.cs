using System;
using System.Collections.Generic;

namespace Hollow.Client.Audio;

/// <summary>
/// One recorded speed of an engine (sounds.json <c>engines</c>): its loops on load and off it, made at <see cref="Rpm"/>,
/// pitched to the engine's revs and crossfaded with their neighbours; each loop's level against the others.
/// </summary>
public sealed record EngineLoop(float Rpm, string On, string Off, float OnDb, float OffDb);

/// <summary>
/// What a ground vehicle's engine, gearbox, tyres and suspension sound like, by its kind (sounds.json <c>engines</c>,
/// VEHICLES.md §7.1): the numbers of its revs (<see cref="Hollow.Client.Vehicles.VehicleRevs"/>), its loops by revs and
/// load, its turbo, its gear change, its tyres by the ground under them, its skid and its suspension. Written by
/// art/audio/vehicle_sounds.py.
/// </summary>
public sealed class EngineSoundData
{
    public string Kind { get; init; } = "";

    /// <summary>Revs at idle and at the redline; the limiter cuts in a little above the redline.</summary>
    public float IdleRpm { get; init; } = 800;
    public float Redline { get; init; } = 6000;
    public float Limiter { get; init; } = 6200;

    /// <summary>Each forward gear's road speed at the redline (m/s), lowest first; reverse's.</summary>
    public IReadOnlyList<float> Gears { get; init; } = Array.Empty<float>();
    public float Reverse { get; init; } = 4;

    /// <summary>Seconds the free revs take to follow the throttle (the flywheel's weight).</summary>
    public float Response { get; init; } = 0.1f;

    /// <summary>Changes up past this share of the redline on the throttle, down under that one.</summary>
    public float Upshift { get; init; } = 0.9f;
    public float Downshift { get; init; } = 0.45f;

    /// <summary>Seconds the clutch is out for a change.</summary>
    public float ShiftTime { get; init; } = 0.25f;

    /// <summary>Revs (a share of the redline) the clutch slips to when pulling away.</summary>
    public float Launch { get; init; } = 0.4f;

    /// <summary>Seconds a turbo takes to spool up (0: none).</summary>
    public float TurboSpool { get; init; }

    /// <summary>The whole engine's pitch and level (a variant of a kind: the monster's lower and louder six).</summary>
    public float Pitch { get; init; } = 1;
    public float Volume { get; init; }

    public IReadOnlyList<EngineLoop> Loops { get; init; } = Array.Empty<EngineLoop>();

    /// <summary>Events: the turbo's whistle (a loop), its release, a gear change.</summary>
    public string? Turbo { get; init; }
    public string? Blowoff { get; init; }
    public string? Shift { get; init; }

    /// <summary>The tyres' loops by physical surface (contact-surfaces.json: grass, dirt, gravel, sand, rock, snow, water;
    /// <c>hard</c> for anything else), made at <see cref="TyreSpeed"/> m/s; their pitch and level for this kind.</summary>
    public IReadOnlyDictionary<string, string> Tyres { get; init; } = new Dictionary<string, string>();
    public float TyreSpeed { get; init; } = 10;
    public float TyrePitch { get; init; } = 1;
    public float TyreDb { get; init; }

    /// <summary>A skid's loops on loose ground and on hard.</summary>
    public string? SkidLoose { get; init; }
    public string? SkidHard { get; init; }

    /// <summary>The suspension: a knock (a wheel hitting its stop), a creak (a spring and its bushes), a landing from a jump, a
    /// splash (into water).</summary>
    public string? Knock { get; init; }
    public string? Creak { get; init; }
    public string? Land { get; init; }
    public string? Splash { get; init; }

    /// <summary>A heavy vehicle's air brakes (VEHICLES.md §9.10): their hiss as it comes to rest after driving.</summary>
    public string? AirBrake { get; init; }

    /// <summary>The tyre loop for a surface: its own, else the hard ground's.</summary>
    public string? TyreFor(string surface) =>
        Tyres.TryGetValue(surface, out string? key) ? key : Tyres.TryGetValue("hard", out key) ? key : null;

    /// <summary>Every event this entry names.</summary>
    public IEnumerable<string> Events()
    {
        foreach (EngineLoop l in Loops)
        {
            yield return l.On;
            yield return l.Off;
        }
        foreach (string? key in new[] { Turbo, Blowoff, Shift, SkidLoose, SkidHard, Knock, Creak, Land, Splash, AirBrake })
        {
            if (key != null)
            {
                yield return key;
            }
        }
        foreach (string key in Tyres.Values)
        {
            yield return key;
        }
    }
}
