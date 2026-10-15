using System;
using System.Collections.Generic;
using System.Numerics;

namespace Hollow.Simulation.Content;

/// <summary>How a battery fires while its trigger is pressed (batteries.json <c>fire.mode</c>, IRON_WAKE.md §6.3).</summary>
public enum BatteryFire : byte
{
    /// <summary>One barrel per press, the next loaded one in turn (the siege battery).</summary>
    Alternate,

    /// <summary>While held, each barrel in order as soon as it is loaded, an interval apart (the defense mount).</summary>
    Ripple,
}

/// <summary>What one loader serves (batteries.json <c>loader.per</c>): a barrel each, or the whole mount (a cassette).</summary>
public enum LoaderPer : byte
{
    Barrel,
    Mount,
}

/// <summary>How a motion eases over its span (batteries.json <c>conventions.easing</c>).</summary>
public enum LoaderEase : byte
{
    Linear,

    /// <summary>t³.</summary>
    In,

    /// <summary>1 − (1 − t)³.</summary>
    Out,

    /// <summary>3t² − 2t³ (smoothstep).</summary>
    InOut,
}

/// <summary>
/// The recoil curve a barrel runs back by after its shot (batteries.json <c>recoil</c>, the asset's curve): its travel in
/// metres along its axis over its seconds, as fractions of the travel at times. Presentation: the rules know only that a
/// barrel recovers for <see cref="Seconds"/> (the loader's first phase waits for it).
/// </summary>
public sealed class RecoilCurve
{
    public float Travel { get; init; }

    public Vector3 Axis { get; init; } = -Vector3.UnitZ;

    public float Seconds { get; init; }

    public IReadOnlyList<float> Times { get; init; } = Array.Empty<float>();

    public IReadOnlyList<float> Fractions { get; init; } = Array.Empty<float>();

    /// <summary>The share of the travel at <paramref name="seconds"/> after the shot (0 before it and once it is over).</summary>
    public float At(double seconds)
    {
        if (seconds <= 0 || Times.Count == 0 || seconds >= Times[^1])
        {
            return 0;
        }
        for (int i = 1; i < Times.Count; i++)
        {
            if (seconds <= Times[i])
            {
                double span = Times[i] - Times[i - 1];
                double t = span > 1e-9 ? (seconds - Times[i - 1]) / span : 1;
                return (float)(Fractions[i - 1] + (Fractions[i] - Fractions[i - 1]) * t);
            }
        }
        return 0;
    }
}

/// <summary>
/// A battery's launch of one kind of rocket (batteries.json <c>launch.profiles</c>, IRON_WAKE.md §6.4): the speed the gun
/// gives it at the muzzle, the motor that lights <see cref="Ignite"/> metres out and pushes it <see cref="Accel"/> m/s² for
/// <see cref="Burn"/> seconds, how long it flies, and (for the sight) its top speed and its reach on the flat. The
/// battery's own gravity and drag (<see cref="BatteryLaunch"/>) go with it.
/// </summary>
public sealed class LaunchProfile
{
    public ItemDef Item { get; init; } = null!;

    public float Muzzle { get; init; }

    public float Ignite { get; init; }

    public float Accel { get; init; }

    public float Burn { get; init; }

    public float Top { get; init; }

    public float Lifetime { get; init; }

    public float Reach { get; init; }
}

/// <summary>A battery's launch rules (batteries.json <c>launch</c>): gravity while the motor burns and after (shares of the planet's), drag a metre, and the profiles.</summary>
public sealed class BatteryLaunch
{
    public float GravityBurning { get; init; } = 0.15f;

    public float Gravity { get; init; } = 1;

    /// <summary>Speed lost a metre flown, as a share of the speed (quadratic drag: dv/dt = −k·|v|·v).</summary>
    public float Drag { get; init; }

    public IReadOnlyList<LaunchProfile> Profiles { get; init; } = Array.Empty<LaunchProfile>();

    /// <summary>The profile for a round, or null.</summary>
    public LaunchProfile? Of(ushort item)
    {
        foreach (LaunchProfile p in Profiles)
        {
            if (p.Item.Id == item)
            {
                return p;
            }
        }
        return null;
    }
}

/// <summary>
/// A battery's mount (batteries.json <c>mount</c>, IRON_WAKE.md §6.2, §6.10): its yaw's motor (rate and acceleration,
/// radians) and how far it turns (the data file's level arcs; a mount also <see cref="Outboard"/> radians each way from its
/// side's beam), its pitch's limits and motor, how close a barrel must point to the asked direction to fire, its health
/// and its sight's zoom.
/// </summary>
public sealed class BatteryMount
{
    public float YawRate { get; init; }

    public float YawAccel { get; init; }

    /// <summary>How far each way from the station's outboard beam it turns (radians), or 0: the arcs alone.</summary>
    public float Outboard { get; init; }

    public float PitchMin { get; init; }

    public float PitchMax { get; init; }

    public float PitchRate { get; init; }

    public float PitchAccel { get; init; }

    /// <summary>Radians between where the barrels point and the asked direction within which they may fire.</summary>
    public float Alignment { get; init; }

    public float Health { get; init; }

    public float Zoom { get; init; } = 1;

    /// <summary>The loader's whole cycle as the table's note says it (checked against the phases).</summary>
    public float LoaderSeconds { get; init; }
}

/// <summary>
/// One motion of a loader's phase (batteries.json <c>phases[].motions</c>, IRON_WAKE.md §9.5): a node (a pattern), translated
/// along or turned about an axis (its own, or "outboard"), from a value to another with its ease over a span of the phase,
/// or the recoil curve; mirrored on a starboard loader when it says so.
/// </summary>
public sealed class LoaderMotion
{
    public string Node { get; init; } = "";

    public bool Rotate { get; init; }

    public Vector3 Axis { get; init; } = Vector3.UnitZ;

    /// <summary>Along the loader's outboard side (+X on a port loader, −X on a starboard one), not <see cref="Axis"/>.</summary>
    public bool Outboard { get; init; }

    public float From { get; init; }

    public float To { get; init; }

    public LoaderEase Ease { get; init; }

    public float SpanStart { get; init; }

    public float SpanEnd { get; init; } = 1;

    /// <summary>Samples the battery's recoil curve over the phase instead.</summary>
    public bool Curve { get; init; }

    public bool Mirror { get; init; }

    /// <summary>Its value (metres or degrees) at a share of its phase.</summary>
    public float At(float phase, RecoilCurve recoil, float phaseSeconds)
    {
        if (Curve)
        {
            return recoil.Travel * recoil.At(phase * phaseSeconds);
        }
        float span = SpanEnd - SpanStart;
        float t = span > 1e-6f ? Math.Clamp((phase - SpanStart) / span, 0, 1) : phase >= SpanEnd ? 1 : 0;
        return From + (To - From) * LoaderPhase.Eased(t, Ease);
    }
}

/// <summary>One phase of a loader's cycle (batteries.json <c>phases</c>): its name, time, sounds, effects and motions.</summary>
public sealed class LoaderPhase
{
    public int Index { get; init; }

    public string Name { get; init; } = "";

    public float Seconds { get; init; }

    /// <summary>Seconds into the cycle it starts at (the phases before it).</summary>
    public float Start { get; internal set; }

    public float End => Start + Seconds;

    public string Sound { get; init; } = "";

    public string EndSound { get; init; } = "";

    public IReadOnlyList<string> Effects { get; init; } = Array.Empty<string>();

    /// <summary>A looping effect from this phase until the start of <see cref="LoopUntil"/> (−1: none).</summary>
    public string LoopEffect { get; init; } = "";

    public int LoopUntil { get; internal set; } = -1;

    internal string LoopUntilName { get; init; } = "";

    public IReadOnlyList<LoaderMotion> Motions { get; init; } = Array.Empty<LoaderMotion>();

    public static float Eased(float t, LoaderEase ease) => ease switch
    {
        LoaderEase.In => t * t * t,
        LoaderEase.Out => 1 - (1 - t) * (1 - t) * (1 - t),
        LoaderEase.InOut => t * t * (3 - 2 * t),
        _ => t,
    };

    public override string ToString() => Name;
}

/// <summary>
/// Nodes that carry rounds (batteries.json <c>holders</c>): a node and its slot empties, which load they show (<see
/// cref="What"/>: counted from the one the cycle loads, or −1 for the one in the breech now), and from which phase to which
/// they show it (−1: the cycle's start, its end).
/// </summary>
public sealed class LoaderHolder
{
    public string Node { get; init; } = "";

    public IReadOnlyList<string> Slots { get; init; } = Array.Empty<string>();

    /// <summary>The load counted from the cycle's own (0), or <see cref="Current"/>.</summary>
    public int What { get; init; }

    public const int Current = -1;

    public int From { get; internal set; } = -1;

    public int Until { get; internal set; } = -1;

    internal string FromName { get; init; } = "";

    internal string UntilName { get; init; } = "";
}

/// <summary>
/// A battery's loader (batteries.json <c>loader</c>, IRON_WAKE.md §6.6): what one serves, the phase an empty magazine holds
/// it before (where it takes its rounds), its phases and holders.
/// </summary>
public sealed class BatteryLoader
{
    public LoaderPer Per { get; init; }

    /// <summary>The phase it waits before with nothing to load, and takes its round(s) at the start of.</summary>
    public int WaitsBefore { get; internal set; }

    internal string WaitsBeforeName { get; init; } = "";

    public IReadOnlyList<LoaderPhase> Phases { get; init; } = Array.Empty<LoaderPhase>();

    public IReadOnlyList<LoaderHolder> Holders { get; init; } = Array.Empty<LoaderHolder>();

    /// <summary>The whole cycle's seconds.</summary>
    public float Seconds => Phases.Count > 0 ? Phases[^1].End : 0;

    /// <summary>Where its rounds are taken (seconds into the cycle).</summary>
    public float TakeAt => WaitsBefore >= 0 && WaitsBefore < Phases.Count ? Phases[WaitsBefore].Start : 0;

    /// <summary>The phase at a time into the cycle (the last once it is over; the first before it starts).</summary>
    public int PhaseAt(float seconds)
    {
        for (int i = 0; i < Phases.Count; i++)
        {
            if (seconds < Phases[i].End)
            {
                return i;
            }
        }
        return Phases.Count - 1;
    }

    /// <summary>The phase index of a name, or −1.</summary>
    public int IndexOf(string name)
    {
        for (int i = 0; i < Phases.Count; i++)
        {
            if (Phases[i].Name == name)
            {
                return i;
            }
        }
        return -1;
    }
}

/// <summary>
/// A kind of crewed battery (batteries.json, IRON_WAKE.md §6): what it fires and how, its recoil, its slots and magazine,
/// its launch profiles, its mount and its loader. A ship's stations name one (the data file's <c>stations[].kind</c>);
/// nothing in the rules tests which.
/// </summary>
public sealed class BatteryDef
{
    public string Key { get; init; } = "";

    /// <summary>Its place in batteries.json (its number on the wire: a launch names it, so a client flies its rockets alike).</summary>
    public int Index { get; init; }

    /// <summary>The rockets it takes, in order (the ammo radial's and a tap of R's).</summary>
    public IReadOnlyList<ItemDef> Ammo { get; init; } = Array.Empty<ItemDef>();

    public BatteryFire Fire { get; init; }

    /// <summary>A ripple's seconds between barrels, and the barrels' order.</summary>
    public float Interval { get; init; }

    public IReadOnlyList<int> Order { get; init; } = Array.Empty<int>();

    public RecoilCurve Recoil { get; init; } = new();

    /// <summary>Magazine slots, and rounds a load holds (a siege barrel one, a defense cassette three).</summary>
    public int MagazineSlots { get; init; }

    public int RoundsPerLoad { get; init; } = 1;

    /// <summary>Loads shown waiting in the ready rack or the feed hood besides the cycle's own (presentation).</summary>
    public int Waiting { get; init; }

    public BatteryLaunch Launch { get; init; } = new();

    public BatteryMount Mount { get; init; } = new();

    public BatteryLoader Loader { get; init; } = new();

    /// <summary>A ripple's interval in ticks.</summary>
    public int IntervalTicks => Math.Max(1, (int)Math.Round(Interval * Movement.CharacterMotor.TickRate));

    /// <summary>Its ammo's index of an item, or −1.</summary>
    public int AmmoIndex(ushort item)
    {
        for (int i = 0; i < Ammo.Count; i++)
        {
            if (Ammo[i].Id == item)
            {
                return i;
            }
        }
        return -1;
    }

    public bool Takes(ushort item) => AmmoIndex(item) >= 0;

    public override string ToString() => Key;
}
