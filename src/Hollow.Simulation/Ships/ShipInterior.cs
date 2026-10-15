using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Core;

namespace Hollow.Simulation.Ships;

/// <summary>How one of a ship's parts moves (the data file's part kinds, IRON_WAKE.md §9.1).</summary>
public enum ShipPartKind : byte
{
    /// <summary>The hull: never moves.</summary>
    Static,

    /// <summary>Turns about its axis through its pivot, from its closed angle to its open one (a ramp's leaf).</summary>
    Hinge,

    /// <summary>Slides along its axis, from its closed distance to its open one (the airlock's door).</summary>
    Slide,

    /// <summary>A turret's platform: turns about its axis through its pivot by its angle.</summary>
    Yaw,

    /// <summary>A turret's guns: elevate about their axis through their pivot by their angle.</summary>
    Pitch,

    /// <summary>A barrel running back along its axis by its travel in metres.</summary>
    Recoil,

    /// <summary>A loading mechanism's piece (presentation until IW3: it never moves in the rules).</summary>
    Loader,
}

/// <summary>
/// One part of a ship's model as the rules know it (IRON_WAKE.md §9.1): its name, what carries it, how it moves and
/// where about. Every vector is in the ship's frame at its rest pose (the data file's parked pose).
/// </summary>
public sealed class ShipPartDef
{
    public string Name { get; init; } = "";

    /// <summary>Its place in <see cref="ShipInterior.Parts"/>: what a part state, a piece and a body name it by.</summary>
    public int Index { get; init; }

    /// <summary>The part that carries it (a ramp's leaf rides its main leaf), or −1 for the ship itself.</summary>
    public int Parent { get; init; } = -1;

    public ShipPartKind Kind { get; init; }

    /// <summary>The point it turns about, and the axis it turns about or slides along (unit).</summary>
    public Vector3 Pivot { get; init; }

    public Vector3 Axis { get; init; } = Vector3.UnitY;

    /// <summary>A hinge's angles (radians) or a slide's distances (metres), shut and open: its state goes from 0 to 1 between them.</summary>
    public float Closed { get; init; }

    public float Open { get; init; }

    /// <summary>Whether convex pieces ride on it: then it is a body of its own in the ship's space.</summary>
    public bool HasPieces { get; internal set; }

    /// <summary>A door's kind of motion: a hinge or a slide, whose state is its progress from shut (0) to open (1).</summary>
    public bool IsDoor => Kind is ShipPartKind.Hinge or ShipPartKind.Slide;

    public override string ToString() => Name;
}

/// <summary>One convex piece of a ship's collision (the asset's convex pieces): its part, the asset's name, its points at rest, glass or not.</summary>
public sealed record ShipPiece(int Part, string Name, Vector3[] Points, bool Glass);

/// <summary>
/// One box of a ship's boarding volume (IRON_WAKE.md §4.5): the rooms, every walkable top with the headroom over it, the
/// open ramp. <see cref="Part"/> carries it (a turret's platform), −1 the hull; <see cref="When"/> is a part that must be
/// fully open for it to count (the open ramp's box), −1 none.
/// </summary>
public readonly record struct BoardingBox(Vector3 Min, Vector3 Max, int Part, int When);

/// <summary>A named point of a ship's model, with its orientation, and the part that carries it (−1 the hull).</summary>
public readonly record struct ShipSocket(Vector3 Position, Vector3 Forward, Vector3 Up, int Part);

/// <summary>
/// Where things may be built aboard (IRON_WAKE.md §8, the data file's <c>buildZones</c>): a box of the ship's frame over a
/// deck (the fabrication bay, the lower decks, the mezzanine), its floor's height and the headroom over it.
/// </summary>
public sealed record ShipBuildZone(string Id, string Name, Vector3 Min, Vector3 Max, float Floor, float Headroom)
{
    /// <summary>The height of its ceiling in the ship's frame.</summary>
    public float Ceiling => Floor + Headroom;
}

/// <summary>
/// A box of a ship's frame kept clear of anything built aboard (IRON_WAKE.md §8, the data file's <c>exclusions</c>): the
/// ramp's top and its swing, the airlock's approach, the stairs and their landings, the bridge, the lanes to the stations
/// and the stations' own room; <see cref="Why"/> says what it keeps clear.
/// </summary>
public sealed record ShipExclusion(string Name, string Why, Vector3 Min, Vector3 Max);

/// <summary>One barrel of a station as the data file has it: its recoil part, its muzzle, its breech end at rest, its side of the mount (+1 port), its length.</summary>
public sealed record StationBarrelData(int RecoilPart, ShipSocket Muzzle, Vector3 Breech, float Side, float Length);

/// <summary>
/// A station as the data file has it (IRON_WAKE.md §6.1, the import's <c>stations</c>): its id, its battery's kind, its side
/// (+1 port, −1 starboard, 0 centre), its yaw and pitch parts, its barrels, its gunner's camera, and the arcs (degrees from
/// the bow, + to port) its barrels clear the hull through at level.
/// </summary>
public sealed record StationGeometry(string Id, string Kind, float Side, int YawPart, int PitchPart, IReadOnlyList<StationBarrelData> Barrels,
    ShipSocket Camera, IReadOnlyList<(float From, float To)> Arcs);

/// <summary>A lamp fixture of a ship's data file (IRON_WAKE.md §9.4): its name, where it is, which way it shines (a spot), its room.</summary>
public sealed record ShipFixture(string Name, Vector3 Position, Vector3 Direction, bool Spot, string Room);

/// <summary>A rigid motion of a ship's frame: a point p goes to <c>Rotation·p + Translation</c>.</summary>
public readonly record struct PartPose(Quaternion Rotation, Vector3 Translation)
{
    public static readonly PartPose Rest = new(Quaternion.Identity, Vector3.Zero);

    public Vector3 Apply(Vector3 point) => Vector3.Transform(point, Rotation) + Translation;

    /// <summary>The point this motion carries to <paramref name="moved"/>.</summary>
    public Vector3 Undo(Vector3 moved) => Vector3.Transform(moved - Translation, Quaternion.Conjugate(Rotation));

    /// <summary>This motion after <paramref name="inner"/>: a point is carried by <paramref name="inner"/> first.</summary>
    public PartPose Then(in PartPose inner) => new(Quaternion.Normalize(Rotation * inner.Rotation), Vector3.Transform(inner.Translation, Rotation) + Translation);
}

/// <summary>
/// The inside of a ship that people walk in (IRON_WAKE.md §4.3, a ships.json entry's <c>interior</c> block): its convex
/// pieces by part, its moving parts and how they move, its boarding volume, its sockets, and the hull boxes and landing
/// pads the import fitted for the world's sweeps, all read from the ship's data file (<c>game/data/ground/ships/</c>,
/// written by the import, IRON_WAKE.md §9.1) in the ship's frame: +Z the nose, +Y up, +X the left, metres from the model's
/// origin at the rest pose. A ship of this kind gets a <c>ShipSpace</c> (its own small physics) when it is made, posed
/// from its parts' states (<see cref="Ship.Parts"/>).
/// </summary>
public sealed class ShipInterior
{
    /// <summary>The data file, as the content names it (<c>ships/iron-wake.json</c>).</summary>
    public string File { get; init; } = "";

    /// <summary>Every part, the hull (index 0, static) first; a part's index is what its state and its body are known by.</summary>
    public IReadOnlyList<ShipPartDef> Parts { get; init; } = Array.Empty<ShipPartDef>();

    public IReadOnlyList<ShipPiece> Pieces { get; init; } = Array.Empty<ShipPiece>();

    public IReadOnlyList<BoardingBox> Boarding { get; init; } = Array.Empty<BoardingBox>();

    public IReadOnlyDictionary<string, ShipSocket> Sockets { get; init; } = new Dictionary<string, ShipSocket>();

    /// <summary>The boxes the import fitted to the model for the world's sweeps (IRON_WAKE.md §5.4), at rest.</summary>
    public IReadOnlyList<HullBox> HullBoxes { get; init; } = Array.Empty<HullBox>();

    /// <summary>The landing pads' feet, and each pad's footprint (x by z).</summary>
    public IReadOnlyList<Vector3> Pads { get; init; } = Array.Empty<Vector3>();

    public IReadOnlyList<Vector2> PadSizes { get; init; } = Array.Empty<Vector2>();

    /// <summary>The asset's centre-of-mass hint.</summary>
    public Vector3 CentreOfMass { get; init; }

    /// <summary>The data file's stations by their ids (IRON_WAKE.md §6.1): what a station's seat names.</summary>
    public IReadOnlySet<string> Stations { get; init; } = new HashSet<string>();

    /// <summary>The data file's stations (IRON_WAKE.md §6.1), in its order: what ships.json's stations are made from (IW3).</summary>
    public IReadOnlyList<StationGeometry> StationData { get; init; } = Array.Empty<StationGeometry>();

    /// <summary>
    /// Whether a part is a station's mount (its yaw or pitch): a parked ship's body keeps them at rest, so a gunner's aim
    /// never makes the body again (IRON_WAKE.md §6.2).
    /// </summary>
    public bool IsMount(int part)
    {
        foreach (StationGeometry s in StationData)
        {
            if (s.YawPart == part || s.PitchPart == part)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>The data file's lamp fixtures, its drive glows left out (IRON_WAKE.md §9.4): what ships.json's lights light.</summary>
    public IReadOnlyList<ShipFixture> Fixtures { get; init; } = Array.Empty<ShipFixture>();

    /// <summary>Where things may be built aboard (IRON_WAKE.md §8), in the data file's order.</summary>
    public IReadOnlyList<ShipBuildZone> BuildZones { get; init; } = Array.Empty<ShipBuildZone>();

    /// <summary>What is kept clear of anything built aboard (IRON_WAKE.md §8), in the data file's order.</summary>
    public IReadOnlyList<ShipExclusion> Exclusions { get; init; } = Array.Empty<ShipExclusion>();

    /// <summary>How far outside the boarding volume, with nothing of the ship under the feet, a character leaves (IRON_WAKE.md §4.5: 0.3 m).</summary>
    public float Margin { get; init; } = 0.3f;

    /// <summary>The decks' gravity, as a share of a character's on the ground (IRON_WAKE.md §4.4: 1).</summary>
    public float DeckGravity { get; init; } = 1f;

    /// <summary>Each part's state as a ship is made (the interior block's <c>poses</c>: the ramp open or shut, IW1).</summary>
    public float[] Rest { get; init; } = Array.Empty<float>();

    /// <summary>The box holding every piece at rest, in the ship's frame.</summary>
    public Vector3 BoundsMin { get; internal set; }

    public Vector3 BoundsMax { get; internal set; }

    /// <summary>
    /// A sphere about the centre of mass that holds every piece at any pose of the moving parts (a ramp swung out, a turret
    /// turned): what a query tests first.
    /// </summary>
    public float Reach { get; internal set; }

    private readonly Dictionary<string, int> _byName = new(StringComparer.Ordinal);

    /// <summary>A part's index by its name, or −1.</summary>
    public int PartIndex(string name) => _byName.GetValueOrDefault(name, -1);

    /// <summary>
    /// Where a part is at some states of the parts: the motion that carries its pieces from rest, its carriers' motions
    /// included (a ramp's leaf turns about its own hinge and goes with the main leaf).
    /// </summary>
    public PartPose PartPose(int part, ReadOnlySpan<float> states)
    {
        if (part <= 0 || part >= Parts.Count)
        {
            return Ships.PartPose.Rest;
        }
        ShipPartDef def = Parts[part];
        PartPose own = Motion(def, part < states.Length ? states[part] : 0);
        return def.Parent >= 0 ? PartPose(def.Parent, states).Then(own) : own;
    }

    // A part's own motion about its pivot, for its state.
    private static PartPose Motion(ShipPartDef def, float state)
    {
        switch (def.Kind)
        {
            case ShipPartKind.Hinge:
                return Turn(def, def.Closed + (def.Open - def.Closed) * state);
            case ShipPartKind.Yaw:
            case ShipPartKind.Pitch:
                return Turn(def, state);
            case ShipPartKind.Slide:
                return new PartPose(Quaternion.Identity, def.Axis * (def.Closed + (def.Open - def.Closed) * state));
            case ShipPartKind.Recoil:
                return new PartPose(Quaternion.Identity, def.Axis * state);
            default:
                return Ships.PartPose.Rest;
        }
    }

    // A turn about the part's axis through its pivot (deterministic sines, the same on every machine).
    private static PartPose Turn(ShipPartDef def, float angle)
    {
        if (angle == 0)
        {
            return Ships.PartPose.Rest;
        }
        double half = angle * 0.5;
        float s = (float)DMath.Sin(half), c = (float)DMath.Cos(half);
        var rotation = Quaternion.Normalize(new Quaternion(def.Axis * s, c));
        return new PartPose(rotation, def.Pivot - Vector3.Transform(def.Pivot, rotation));
    }

    /// <summary>Whether a box of the boarding volume counts at these states (its condition's part fully open).</summary>
    public bool Counts(in BoardingBox box, ReadOnlySpan<float> states) =>
        box.When < 0 || (box.When < states.Length && states[box.When] >= 1 - 1e-4f);

    /// <summary>
    /// How far a point of the ship's frame is outside its boarding volume at these states: 0 inside a box that counts,
    /// else the distance to the nearest (a box riding a part is moved with it).
    /// </summary>
    public float OutsideBoarding(Vector3 point, ReadOnlySpan<float> states)
    {
        float best = float.MaxValue;
        foreach (BoardingBox box in Boarding)
        {
            if (!Counts(box, states))
            {
                continue;
            }
            Vector3 p = box.Part > 0 ? PartPose(box.Part, states).Undo(point) : point;
            Vector3 outside = Vector3.Max(Vector3.Max(box.Min - p, p - box.Max), Vector3.Zero);
            float d = outside.Length();
            if (d < best)
            {
                best = d;
                if (d == 0)
                {
                    break;
                }
            }
        }
        return best;
    }

    /// <summary>A socket's position, carried by its part at these states.</summary>
    public Vector3 SocketAt(in ShipSocket socket, ReadOnlySpan<float> states) =>
        socket.Part > 0 ? PartPose(socket.Part, states).Apply(socket.Position) : socket.Position;

    /// <summary>Indexes the parts by name and works out the bounds and the reach (once, by the reader).</summary>
    internal void Finish()
    {
        _byName.Clear();
        foreach (ShipPartDef part in Parts)
        {
            _byName.TryAdd(part.Name, part.Index);
        }
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        foreach (ShipPiece piece in Pieces)
        {
            if (piece.Part >= 0 && piece.Part < Parts.Count)
            {
                Parts[piece.Part].HasPieces = true;
            }
            foreach (Vector3 p in piece.Points)
            {
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
        }
        BoundsMin = min;
        BoundsMax = max;
        // Each piece can be anywhere its carriers can take it: the farthest it gets from the centre of mass is bounded by
        // the chain of pivots, each with the most a point of its part (or of a part it carries) is from it.
        float reach = 0;
        foreach (ShipPiece piece in Pieces)
        {
            foreach (Vector3 p in piece.Points)
            {
                reach = MathF.Max(reach, Farthest(piece.Part, p));
            }
        }
        foreach (BoardingBox box in Boarding)
        {
            reach = MathF.Max(reach, Farthest(box.Part, box.Min));
            reach = MathF.Max(reach, Farthest(box.Part, box.Max));
        }
        Reach = reach + 0.5f;
    }

    // The farthest a point of a part can be from the centre of mass, whatever the parts' states.
    private float Farthest(int part, Vector3 point)
    {
        if (part <= 0 || part >= Parts.Count)
        {
            return Vector3.Distance(point, CentreOfMass);
        }
        ShipPartDef def = Parts[part];
        float slide = def.Kind is ShipPartKind.Slide or ShipPartKind.Recoil ? MathF.Max(MathF.Abs(def.Closed), MathF.Abs(def.Open)) + 1 : 0;
        float about = Vector3.Distance(point, def.Pivot) + slide;
        return Farthest(def.Parent, def.Pivot) + about;
    }
}
