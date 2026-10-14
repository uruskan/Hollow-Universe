using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hollow.Simulation.Nature;

namespace Hollow.Simulation.Content;

/// <summary>Which of a tool's gather rates a class of natural objects takes (items.json <c>tree</c>, <c>ore</c>).</summary>
public enum GatherRate : byte
{
    Tree,
    Ore,
}

/// <summary>
/// Rust's weak spot (gather.json <c>classes.*.spot</c>, GROUND.md §6.1): where it shows and what a hit on it does. It
/// appears with the first hit, facing the hitter; a hit within <see cref="Radius"/> of it pays <see cref="Scale"/>
/// (rising with each hit on it in a row, up to <see cref="Max"/>) and moves it; a hit elsewhere resets the run and leaves
/// it where it is. A hit takes that much more health too, so the object's whole yield stays the same: the spot makes it
/// quicker, not richer.
/// </summary>
public sealed record SpotRule
{
    /// <summary>A hit whose point on the drawn surface is this close to the spot (metres) is on it.</summary>
    public float Radius { get; init; } = 0.3f;

    /// <summary>A new spot faces the hitter within this many degrees either way.</summary>
    public float FacingDegrees { get; init; } = 50;

    /// <summary>Each hit on the spot in a row adds this much to the multiplier (the first: 1 + step).</summary>
    public float Step { get; init; } = 0.5f;

    /// <summary>The multiplier's ceiling.</summary>
    public float Max { get; init; } = 3f;

    /// <summary>Where on the object it sits: the drawn surface (<see cref="TrunkSurface"/>, <see cref="MeasuredSurface"/>).</summary>
    public ISpotSurface Surface { get; init; } = null!;

    /// <summary>What a hit pays and takes after <paramref name="streak"/> hits on the spot in a row (1 when none).</summary>
    public float Scale(int streak) => streak <= 0 ? 1f : Math.Min(1f + Step * streak, Max);
}

/// <summary>
/// What the last hit pays (gather.json <c>classes.*.last</c>): a share of the whole yield kept back from the hits
/// (Rust's trees: half when it falls), and a bonus on top for a proper tool (Rust's ore nodes: about 20%, not with a
/// rock).
/// </summary>
public sealed record LastHitRule
{
    /// <summary>The share of the whole yield the last hit pays; the hits pay the rest as they take health.</summary>
    public float Share { get; init; }

    /// <summary>A share of the whole yield paid on top by the last hit with one of <see cref="Tools"/>.</summary>
    public float Bonus { get; init; }

    /// <summary>The tools that earn the bonus (items.json IDs).</summary>
    public IReadOnlySet<ushort> Tools { get; init; } = new HashSet<ushort>();

    public bool PaysBonus(ItemDef? tool) => Bonus > 0 && tool != null && Tools.Contains(tool.Id);
}

/// <summary>
/// One thing that happens on screen and in the ears (gather.json <c>look</c>): an effect of fx.json and a sound event
/// of sounds.json, both optional. <c>{stage}</c> in <see cref="Sound"/> becomes the stage reached. With
/// <see cref="Gibs"/>, the object's model breaks into its gibs where it stood (GROUND.md §9.4: an outcrop's last remnant
/// crumbling).
/// </summary>
public sealed record GatherCue(string? Fx, string? Sound, float FxScale = 1, float SoundDelay = 0, float PitchStep = 0, bool Gibs = false);

/// <summary>The weak spot drawn (gather.json <c>look.marker</c>): a decal style and its glints.</summary>
public sealed record MarkerLook
{
    /// <summary>The painter of its decal (the client's marker textures: "painted-x", "hotspot").</summary>
    public string Style { get; init; } = "";

    /// <summary>Width of the decal in metres, and how deep it projects (to cover bark and rock that bulge or dip).</summary>
    public float Size { get; init; } = 0.3f;

    public float Depth { get; init; } = 0.4f;

    /// <summary>Its colour, as picked (sRGB, 0 … 1): the paint's, or the glow's.</summary>
    public Vector3 Colour { get; init; } = Vector3.One;

    /// <summary>How brightly it glows, as seen whatever the exposure (0: paint).</summary>
    public float Glow { get; init; }

    /// <summary>Pulses per second of the glow and the glint (0: steady).</summary>
    public float Pulse { get; init; }

    /// <summary>A glint drawn over it, this wide in metres (0: none).</summary>
    public float Glint { get; init; }

    /// <summary>An effect of fx.json played on it every <see cref="SparkleEvery"/> seconds (null: none).</summary>
    public string? Sparkle { get; init; }

    public float SparkleEvery { get; init; } = 1;
}

/// <summary>A felled object falling (gather.json <c>look.fall</c>): seconds to tip over, to lie, to sink; the landing's effect.</summary>
public sealed record FallLook(float Tip, float Lie, float Sink, string? Fx);

/// <summary>A hit shaking the object's crown (gather.json <c>look.shake</c>): how far its top sways and for how long.</summary>
public sealed record ShakeLook(float Metres, float Seconds);

/// <summary>How a class of natural objects shows being gathered (gather.json <c>look</c>; a kind may replace parts).</summary>
public sealed record GatherLook
{
    public MarkerLook? Marker { get; init; }

    /// <summary>Every hit, where it met the drawn surface.</summary>
    public GatherCue? Hit { get; init; }

    /// <summary>A hit on the weak spot (its sound rises by <see cref="GatherCue.PitchStep"/> with each in a row).</summary>
    public GatherCue? SpotHit { get; init; }

    /// <summary>A run of weak-spot hits broken by a hit elsewhere.</summary>
    public GatherCue? Missed { get; init; }

    /// <summary>A depletion stage reached (an outcrop breaking up).</summary>
    public GatherCue? Stage { get; init; }

    /// <summary>Gone: felled or broken.</summary>
    public GatherCue? Depleted { get; init; }

    public FallLook? Fall { get; init; }

    public ShakeLook? Shake { get; init; }
}

/// <summary>
/// How a class of natural objects is gathered (gather.json <c>classes</c>, GROUND.md §6.1): the tool rate it takes, its
/// weak spot, what the last hit pays and how it all shows. One rule for every class (<see cref="NaturalState.Hit"/>);
/// each harvested kind carries its own copy, with its drawn surface and any parts of the look it replaces.
/// </summary>
public sealed record GatherRules(GatherRate Rate, SpotRule Spot, LastHitRule Last, GatherLook Look);
