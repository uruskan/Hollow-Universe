using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Hollow.Simulation.Building;

/// <summary>What a piece is, structurally (RustMVP's families): it decides its sockets' geometry and its support losses.</summary>
public enum PieceFamily : byte
{
    Foundation,
    Floor,
    Wall,
    Stairs,
    Roof,
}

/// <summary>A piece's footprint: a 3 m square, an equilateral triangle of 3 m sides, or none (walls stand on an edge).</summary>
public enum PieceShape : byte
{
    Square,
    Triangle,
    Wall,
}

/// <summary>The opening a wall piece leaves, which decides what can be fitted into it.</summary>
public enum Opening : byte
{
    None,
    Door,
    Double,
    Window,
}

/// <summary>The kind of staircase, which decides its collision (GROUND.md §6.3).</summary>
public enum StairsKind : byte
{
    None,
    L,
    U,
    Spiral,
    Ramp,
    Steps,
}

/// <summary>Where on a piece a socket is (RustMVP's socket ids: edges 0–3, centre 16, top 32, edges up 64+i, down 68+i).</summary>
public enum SocketKind : byte
{
    Edge,
    EdgeUp,
    EdgeDown,
    Centre,
    Top,
}

/// <summary>Damage types the building rules know (RustMVP's channels, with melee for the soft side). Explosives come in G5.</summary>
public enum DamageType : byte
{
    Bullet,
    Melee,
    Explosive,
    Heat,
}

/// <summary>A socket of a piece and what it accepts.</summary>
public sealed class SocketDef
{
    public SocketKind At { get; init; }

    public IReadOnlyList<PieceFamily> Families { get; init; } = Array.Empty<PieceFamily>();

    /// <summary>Pieces accepted by key, beyond the families (RustMVP's square roof takes square roofs only).</summary>
    public IReadOnlyList<string> Pieces { get; init; } = Array.Empty<string>();

    /// <summary>The child must have the same footprint (stairs on slabs, floors on stairs).</summary>
    public bool SameShape { get; init; }

    public bool Accepts(PieceDef parent, PieceDef child)
    {
        bool listed = false;
        foreach (PieceFamily f in Families)
        {
            listed |= f == child.Family;
        }
        foreach (string key in Pieces)
        {
            listed |= key == child.Key;
        }
        return listed && (!SameShape || parent.Shape == child.Shape);
    }
}

/// <summary>A building piece from <c>building.json</c>: its family, footprint, height, costs per grade and sockets.</summary>
public sealed class PieceDef
{
    /// <summary>Stable id (RustMVP's part id); saves and the wire use it, and the wheels' glyph has the same number.</summary>
    public int Id { get; init; }

    public string Key { get; init; } = "";

    public string Name { get; init; } = "";

    public string Description { get; init; } = "";

    public PieceFamily Family { get; init; }

    public PieceShape Shape { get; init; }

    /// <summary>Height above the pivot in metres: walls, stairs and roofs; 0 for slabs.</summary>
    public double Height { get; init; }

    public Opening Opening { get; init; }

    public StairsKind Stairs { get; init; }

    /// <summary>A floor frame: a slab with an open centre.</summary>
    public bool Frame { get; init; }

    /// <summary>Cost in each grade's material, by grade index.</summary>
    public IReadOnlyList<int> Cost { get; init; } = Array.Empty<int>();

    /// <summary>How far the hammer turns it, in 30° steps (0: it can't be rotated).</summary>
    public int RotateSteps { get; init; }

    public IReadOnlyList<SocketDef> Sockets { get; init; } = Array.Empty<SocketDef>();

    public bool IsTriangle => Shape == PieceShape.Triangle;

    public bool IsSlab => Family is PieceFamily.Foundation or PieceFamily.Floor;

    public bool IsSpiral => Stairs == StairsKind.Spiral;

    /// <summary>Footprint edges (0 for walls).</summary>
    public int EdgeCount => Shape switch { PieceShape.Square => 4, PieceShape.Triangle => 3, _ => 0 };

    public override string ToString() => Key;
}

/// <summary>How much of each damage type reaches a piece, 0 (immune) to 1.</summary>
public sealed class Protection
{
    private readonly double[] _transmission = new double[4];

    public double this[DamageType type]
    {
        get => _transmission[(int)type];
        init => _transmission[(int)type] = value;
    }

    internal void Set(DamageType type, double value) => _transmission[(int)type] = value;
}

/// <summary>A building grade: twig, wood, stone, metal, armoured.</summary>
public sealed class GradeDef
{
    public int Index { get; init; }

    public string Key { get; init; } = "";

    public string Name { get; init; } = "";

    /// <summary>Item key paid for placing (twig), upgrading to, repairing and keeping up this grade.</summary>
    public string Material { get; init; } = "";

    public double Health { get; init; }

    /// <summary>An unpaid piece of this grade loses all its health in this many hours.</summary>
    public double DecayHours { get; init; }

    /// <summary>Damage transmission from the hard side (and for pieces without sides).</summary>
    public Protection Hard { get; init; } = new();

    /// <summary>Damage transmission from a wall's soft side (Rust's weak side).</summary>
    public Protection Soft { get; init; } = new();
}

/// <summary>A door leaf: its mesh, its hinge in the frame's piece-local metres, its size and how far it swings open.</summary>
public sealed class LeafDef
{
    private ConvexPart[]? _fallbackCollision;

    public string Mesh { get; init; } = "";

    /// <summary>Additional meshes sharing this leaf's pose and physics body (for example a reinforced pane and its wire).</summary>
    public IReadOnlyList<string> AdditionalMeshes { get; init; } = Array.Empty<string>();

    /// <summary>The hinge (the leaf's origin) in the doorway's frame: metres, Y up, outside +Z.</summary>
    public System.Numerics.Vector3 Hinge { get; init; }

    /// <summary>Width (negative: the leaf reaches toward −X), height and thickness.</summary>
    public System.Numerics.Vector3 Size { get; init; }

    /// <summary>Yaw of the open leaf about its hinge, degrees (from the kit's assembly manifest).</summary>
    public double OpenYaw { get; init; }

    /// <summary>
    /// How far out from the leaf's middle plane a lock's back rests on either face (DOORS.md §6): the front of the handle
    /// plate the kit puts there, the same on both faces.
    /// </summary>
    public float LockDepth { get; init; }

    /// <summary>Authored convex parts in leaf-local metres. Omitted for the existing solid rectangular leaves.</summary>
    public ConvexPart[] Collision { get; init; } = Array.Empty<ConvexPart>();

    /// <summary>The same solid parts for authoritative collision, placement and targeting; openings stay empty.</summary>
    public ConvexPart[] Parts => Collision.Length > 0 ? Collision : _fallbackCollision ??= new[] { PieceShapes.Leaf(this) };

    /// <summary>The mesh of this leaf hung on the other side, the kit's leaf mirrored in X ("" for a door whose hinge side is fixed).</summary>
    public string MirrorMesh { get; init; } = "";

    /// <summary>The same parts hung on the other side (mirrored in X), for a right-hinged leaf.</summary>
    public ConvexPart[] MirroredParts => _mirrored ??= Parts.Select(p => p.MirroredX()).ToArray();

    /// <summary>The key a mirrored leaf's shared body shape is kept under.</summary>
    internal object MirrorKey { get; } = new();

    /// <summary>
    /// The height of a rolling curtain's body while its lower edge is <paramref name="lift"/> metres up, in whole centimetres
    /// (rounded up, so shapes are shared and the top never falls short of the header): only the part still hanging, from the
    /// lower edge up to the header, never the curtain's full size lifted (DOORS.md §5).
    /// </summary>
    public int HangingCentimetres(float lift) => Math.Clamp((int)MathF.Ceiling((Size.Y - lift) * 100f - 0.01f), 5, (int)MathF.Ceiling(Size.Y * 100f));

    /// <summary>A rolling curtain's body of that height, standing on its (lifted) origin.</summary>
    public ConvexPart[] HangingParts(int centimetres) => _hanging.GetOrAdd(centimetres, cm =>
    {
        float x0 = Math.Min(0, Size.X), x1 = Math.Max(0, Size.X), t = Size.Z / 2;
        return new[] { ConvexPart.Box(x0, x1, 0, cm / 100f, -t, t) };
    });

    /// <summary>The key a hanging body's shared shape of that height is kept under.</summary>
    internal object HangingKey(int centimetres) => (this, centimetres);

    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, ConvexPart[]> _hanging = new();

    /// <summary>
    /// How far the hinge pin lies from the leaf's middle plane, toward the face the leaf swings to: half its thickness, so
    /// the open leaf stands clear of the jamb it hangs on (DOORS.md §3).
    /// </summary>
    public float PinDepth => MathF.Abs(Size.Z) / 2;

    private ConvexPart[]? _mirrored;
}

/// <summary>
/// Where a leaf hangs in a layout (<see cref="DoorDef.Hinging"/>): its hinge pin in the frame, where the leaf's own origin
/// (its middle plane at the hinge) lies from the pin when shut, and the yaw of the open leaf about the pin.
/// </summary>
public readonly record struct LeafHinging(System.Numerics.Vector3 Pin, System.Numerics.Vector3 Offset, float OpenYaw, bool Mirrored);

/// <summary>A fitted aperture attachment: a swinging/rolling door or a stationary window insert.</summary>
public sealed class DoorDef
{
    public string Key { get; init; } = "";

    public string Name { get; init; } = "";

    public Opening Opening { get; init; }

    public double Health { get; init; }

    /// <summary>The grade whose protection the door takes (wood, metal, armoured).</summary>
    public GradeDef Protection { get; init; } = null!;

    public string RepairMaterial { get; init; } = "";

    /// <summary>What repairing from zero to full would cost.</summary>
    public int RepairFull { get; init; }

    /// <summary>A garage door rolls up instead of swinging.</summary>
    public bool Rolls { get; init; }

    /// <summary>Whether this attachment can open. Stationary inserts still share placement, damage, repair and persistence.</summary>
    public bool Opens { get; init; } = true;

    /// <summary>Whether a key/code lock may be fitted.</summary>
    public bool Lockable { get; init; } = true;

    public IReadOnlyList<LeafDef> Leaves { get; init; } = Array.Empty<LeafDef>();

    public string FrameMesh { get; init; } = "";

    /// <summary>Seconds a door takes to swing open or shut, or a garage curtain to roll (the kit's clip: 3 s).</summary>
    public double TravelSeconds { get; init; } = 1.0;

    /// <summary>The progress a tick adds to a door that is moving (the same arithmetic on every machine).</summary>
    public float ProgressPerTick => (float)(1.0 / (TravelSeconds * Movement.CharacterMotor.TickRate));

    /// <summary>
    /// For a rolling door: how high the curtain's lower edge has risen, in metres, at equal steps of progress from shut to
    /// open (measured on the kit's clip; the first is 0). Empty: the curtain rises by its height in proportion to progress.
    /// </summary>
    public IReadOnlyList<float> Lift { get; init; } = Array.Empty<float>();

    /// <summary>How far a rolling door's curtain has risen at a progress, in metres (<see cref="Lift"/>, read between its steps).</summary>
    public float LiftAt(float progress, float height)
    {
        if (Lift.Count < 2)
        {
            return height * Math.Clamp(progress, 0f, 1f);
        }
        float at = Math.Clamp(progress, 0f, 1f) * (Lift.Count - 1);
        int i = Math.Min((int)at, Lift.Count - 2);
        return Lift[i] + (Lift[i + 1] - Lift[i]) * (at - i);
    }

    /// <summary>Whether the builder chooses the hinge side (a single door; layout bit 0 hangs the leaf on the right).</summary>
    public bool Hinges { get; init; }

    /// <summary>Whether the builder chooses the swing side (layout bit 1 swings the leaves out, toward +Z).</summary>
    public bool Swings { get; init; }

    /// <summary>The layout bits that mean something for this door (DOORS.md §3).</summary>
    public int LayoutMask => (Hinges ? 1 : 0) | (Swings ? 2 : 0);

    /// <summary>How many layouts the door has: 4 for a single door, 2 for a double door, 1 otherwise.</summary>
    public int Layouts => (Hinges ? 2 : 1) * (Swings ? 2 : 1);

    /// <summary>
    /// The layouts in the order R steps through them, each press changing one choice: left/in, right/in, right/out, left/out
    /// for a single door; in, out for a double door.
    /// </summary>
    public IReadOnlyList<int> LayoutCycle => (Hinges, Swings) switch
    {
        (true, true) => new[] { 0, 1, 3, 2 },
        (false, true) => new[] { 0, 2 },
        (true, false) => new[] { 0, 1 },
        _ => new[] { 0 },
    };

    /// <summary>
    /// The layout a door starts at when aimed at from one side of its frame: hinged on the viewer's left, swinging away from
    /// the viewer (from the +Z side that is hinge left and in, from −Z hinge right and out).
    /// </summary>
    public int DefaultLayout(bool fromOutside) => fromOutside ? 0 : LayoutMask & 3;

    /// <summary>The layout <paramref name="turns"/> presses of R after <paramref name="from"/>.</summary>
    public int Turned(int from, int turns)
    {
        IReadOnlyList<int> cycle = LayoutCycle;
        int at = 0;
        for (int i = 0; i < cycle.Count; i++)
        {
            if (cycle[i] == (from & LayoutMask))
            {
                at = i;
            }
        }
        return cycle[(((at + turns) % cycle.Count) + cycle.Count) % cycle.Count];
    }

    /// <summary>Where leaf <paramref name="index"/> hangs and where it swings to in a layout (bits this door does not choose are ignored).</summary>
    public LeafHinging Hinging(int index, int layout)
    {
        LeafDef leaf = Leaves[index];
        layout &= LayoutMask;
        bool mirrored = (layout & 1) != 0, swingOut = (layout & 2) != 0;
        bool swings = leaf.OpenYaw != 0;
        float pinZ = !swings ? 0f : swingOut ? leaf.PinDepth : -leaf.PinDepth;
        var pin = new System.Numerics.Vector3(mirrored ? -leaf.Hinge.X : leaf.Hinge.X, leaf.Hinge.Y, leaf.Hinge.Z + pinZ);
        float yaw = (float)(leaf.OpenYaw * Math.PI / 180) * (mirrored ? -1 : 1) * (swingOut ? -1 : 1);
        return new LeafHinging(pin, new System.Numerics.Vector3(0, 0, -pinZ), yaw, mirrored);
    }

    /// <summary>How far open a leaf is at a progress from 0 (shut) to 1 (open): a swing eases at both ends.</summary>
    public static float Ease(float progress)
    {
        float p = Math.Clamp(progress, 0f, 1f);
        return p * p * (3 - 2 * p);
    }
}

/// <summary>The building rules' numbers (<c>game/data/ground/building.json</c>), read by the server and the client.</summary>
public sealed class BuildingData
{
    public const int GradeCount = 5;

    private readonly Dictionary<string, PieceDef> _pieces = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DoorDef> _doors = new(StringComparer.Ordinal);
    private readonly Dictionary<Opening, (double Width, double Height, double Sill)> _apertures = new();
    private readonly Dictionary<string, string> _materialNames = new(StringComparer.Ordinal);

    private BuildingData()
    {
    }

    /// <summary>Problems found while reading (an empty list when the file is sound).</summary>
    public IReadOnlyList<string> Problems { get; private set; } = Array.Empty<string>();

    // ---------------------------------------------------------------- module (metres)

    public double Width { get; private set; } = 3;
    public double Storey { get; private set; } = 3;
    public double HalfWall { get; private set; } = 1.5;
    public double LowWall { get; private set; } = 1;
    public double RampHeight { get; private set; } = 0.75;
    public double FoundationDepth { get; private set; } = 1;
    public double FloorThickness { get; private set; } = 0.2;
    public double WallThickness { get; private set; } = 0.3;
    public double FrameBorder { get; private set; } = 0.2;
    public (double Width, double Height) DoorOpening { get; private set; } = (1.2, 2.3);
    public (double Width, double Height) DoubleOpening { get; private set; } = (2.7, 2.85);
    public (double Width, double Height) WindowOpening { get; private set; } = (1.8, 1.2);
    public double WindowSill { get; private set; } = 0.9;

    /// <summary>The registered wall aperture's clear rectangle in piece-local metres.</summary>
    public bool TryOpening(Opening opening, out (double Width, double Height, double Sill) bounds) => _apertures.TryGetValue(opening, out bounds);

    // ---------------------------------------------------------------- placement

    public double Reach { get; private set; } = 8;
    public double SocketAimRadius { get; private set; } = 1.75;
    public double FreeFoundationDistance { get; private set; } = 3.5;
    public double FoundationClearance { get; private set; } = 0.05;
    public double GroundNormalMin { get; private set; } = 0.75;
    public double SightTolerance { get; private set; } = 0.35;
    public double ClaimPadding { get; private set; } = 2.25;
    public int CandidatesTried { get; private set; } = 8;
    public int MaxPiecesPerBuilding { get; private set; } = 4000;
    public double ContactTolerance { get; private set; } = 0.01;

    // ---------------------------------------------------------------- hammer (seconds)

    public double EditWindow { get; private set; } = 600;
    public double RepairDelay { get; private set; } = 30;
    public double RepairPerHit { get; private set; } = 100;
    public double RepairCostFraction { get; private set; } = 0.2;

    // ---------------------------------------------------------------- stability (thousandths)

    public int GroundStability { get; private set; } = 1000;
    public int MinimumStability { get; private set; } = 1;
    public double CollapseStep { get; private set; } = 0.2;
    public int LossFoundationToFoundation { get; private set; }
    public int LossToWall { get; private set; }
    public int LossCentre { get; private set; } = 100;
    public int LossFromWall { get; private set; } = 100;
    public int LossFromStairs { get; private set; } = 100;
    public int LossToRoof { get; private set; } = 100;
    public int LossSlabEdge { get; private set; } = 200;

    // ---------------------------------------------------------------- tables

    public IReadOnlyList<GradeDef> Grades { get; private set; } = Array.Empty<GradeDef>();

    /// <summary>Pieces by id (RustMVP's part order).</summary>
    public IReadOnlyList<PieceDef> Pieces { get; private set; } = Array.Empty<PieceDef>();

    /// <summary>The plan wheel's pieces, clockwise (RustMVP's order, matched to Rust).</summary>
    public IReadOnlyList<PieceDef> Wheel { get; private set; } = Array.Empty<PieceDef>();

    public IReadOnlyList<DoorDef> Doors { get; private set; } = Array.Empty<DoorDef>();

    public IReadOnlyList<string> Materials { get; private set; } = Array.Empty<string>();

    // ---------------------------------------------------------------- locks

    public int CodeLength { get; private set; } = 4;
    public int LockMaxAuthorised { get; private set; } = 64;

    /// <summary>Seconds a player waits after their n-th wrong code (the last entry repeats).</summary>
    public IReadOnlyList<double> WrongCodeWait { get; private set; } = new double[] { 1, 5, 5, 5, 30 };

    public string KeyLockItem { get; private set; } = "key-lock";
    public string KeyLockName { get; private set; } = "Key Lock";
    public string CodeLockItem { get; private set; } = "code-lock";
    public string CodeLockName { get; private set; } = "Code Lock";

    /// <summary>The two locks as things to place (GROUND_SYSTEMS.md §6): their keys (the items'), names and kinds.</summary>
    public IReadOnlyList<LockDef> Locks { get; private set; } = Array.Empty<LockDef>();

    /// <summary>A lock by its key, or null.</summary>
    public LockDef? Lock(string key)
    {
        foreach (LockDef l in Locks)
        {
            if (l.Key == key)
            {
                return l;
            }
        }
        return null;
    }

    /// <summary>The item a key lock's keys are (its condition holds the lock's number), and what cutting one costs.</summary>
    public string KeyItem { get; private set; } = "door-key";
    public string KeyCostMaterial { get; private set; } = "wood";
    public int KeyCost { get; private set; } = 75;

    // ---------------------------------------------------------------- claim device

    public string ClaimItem { get; private set; } = "claim-device";
    public string ClaimName { get; private set; } = "Tool Cupboard";

    /// <summary>The device's box: width, height, depth in metres.</summary>
    public System.Numerics.Vector3 ClaimSize { get; private set; } = new(0.9f, 1.8f, 0.7f);
    public double ClaimPieceRadius { get; private set; } = 16;
    public double ClaimDeviceRadius { get; private set; } = 25;
    public int ClaimMaxAuthorised { get; private set; } = 64;

    /// <summary>The device's hit points and the grade whose (hard side) protection it takes.</summary>
    public double ClaimHealth { get; private set; } = 200;
    public GradeDef ClaimProtection { get; private set; } = new();

    /// <summary>Slots of its upkeep store.</summary>
    public int ClaimSlots { get; private set; } = 24;

    /// <summary>A live zone applies upkeep and decay this often, in seconds (closed form, so any interval agrees).</summary>
    public double AdvanceSeconds { get; private set; } = 60;
    public double UpkeepPeriodHours { get; private set; } = 24;

    /// <summary>Rust's upkeep brackets: the next <c>Pieces</c> pieces pay <c>Fraction</c> of their cost a period (0: the rest).</summary>
    public IReadOnlyList<(int Pieces, double Fraction)> UpkeepBrackets { get; private set; } = Array.Empty<(int, double)>();

    // ---------------------------------------------------------------- lookups

    public PieceDef? Piece(string key) => _pieces.TryGetValue(key, out PieceDef? p) ? p : null;

    /// <summary>A piece by id, or null for an id out of range (ids from the wire are untrusted).</summary>
    public PieceDef? Piece(int id) => id >= 0 && id < Pieces.Count ? Pieces[id] : null;

    public DoorDef? Door(string key) => _doors.TryGetValue(key, out DoorDef? d) ? d : null;

    /// <summary>A door by its place in building.json's list (the wire and saves use it), or null.</summary>
    public DoorDef? Door(int index) => index >= 0 && index < Doors.Count ? Doors[index] : null;

    public int IndexOf(DoorDef door)
    {
        for (int i = 0; i < Doors.Count; i++)
        {
            if (Doors[i] == door)
            {
                return i;
            }
        }
        return -1;
    }

    public string MaterialName(string key) => _materialNames.TryGetValue(key, out string? name) ? name : key;

    /// <summary>
    /// The item a material is (its ID, for an answer's line: "Not enough wood"), linked when the game data is read
    /// (<c>GameContent</c>); 0 for building data read on its own.
    /// </summary>
    public int MaterialItem(string key) => _materialItems.TryGetValue(key, out int item) ? item : 0;

    internal void LinkMaterial(string key, int item) => _materialItems[key] = item;

    private readonly Dictionary<string, int> _materialItems = new(StringComparer.Ordinal);

    /// <summary>A grade by key, or null.</summary>
    public GradeDef? Grade(string key) => Grades.FirstOrDefault(g => g.Key == key);

    /// <summary>The piece's cost in a grade's material.</summary>
    public int CostOf(PieceDef piece, int grade) => grade >= 0 && grade < piece.Cost.Count ? piece.Cost[grade] : 0;

    // ---------------------------------------------------------------- loading

    /// <summary>The data file next to the game's other ground data: walks up from <paramref name="start"/> to the repository.</summary>
    public static string FindFile(string? start = null)
    {
        string? dir = start ?? AppContext.BaseDirectory;
        while (dir != null)
        {
            string candidate = Path.Combine(dir, "game", "data", "ground", "building.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
            candidate = Path.Combine(dir, "data", "ground", "building.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException("game/data/ground/building.json not found above " + (start ?? AppContext.BaseDirectory));
    }

    public static BuildingData Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>Reads the data, noting every problem in <see cref="Problems"/> rather than stopping at the first.</summary>
    public static BuildingData Parse(string json)
    {
        var data = new BuildingData();
        var problems = new List<string>();
        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        JsonElement root = document.RootElement;
        var r = new Reader(problems);
        data.ReadModule(r, root);
        data.ReadMaterials(r, root);
        data.ReadGrades(r, root);
        data.ReadPieces(r, root);
        data.ReadDoors(r, root);
        data.ReadLocks(r, root);
        data.ReadClaim(r, root);
        data.Check(problems);
        data.Problems = problems;
        return data;
    }

    private void ReadModule(Reader r, JsonElement root)
    {
        JsonElement m = r.Object(root, "module");
        Width = r.Number(m, "width", 3);
        Storey = r.Number(m, "storey", 3);
        HalfWall = r.Number(m, "halfWall", 1.5);
        LowWall = r.Number(m, "lowWall", 1);
        RampHeight = r.Number(m, "ramp", 0.75);
        FoundationDepth = r.Number(m, "foundationDepth", 1);
        FloorThickness = r.Number(m, "floorThickness", 0.2);
        WallThickness = r.Number(m, "wallThickness", 0.3);
        FrameBorder = r.Number(m, "frameBorder", 0.2);
        DoorOpening = r.Pair(m, "doorOpening", DoorOpening);
        DoubleOpening = r.Pair(m, "doubleOpening", DoubleOpening);
        WindowOpening = r.Pair(m, "windowOpening", WindowOpening);
        WindowSill = r.Number(m, "windowSill", 0.9);
        _apertures[Opening.Door] = (DoorOpening.Width, DoorOpening.Height, 0);
        _apertures[Opening.Double] = (DoubleOpening.Width, DoubleOpening.Height, 0);
        _apertures[Opening.Window] = (WindowOpening.Width, WindowOpening.Height, WindowSill);

        JsonElement p = r.Object(root, "placement");
        SocketAimRadius = r.Number(p, "socketAimRadius", 1.75);
        FreeFoundationDistance = r.Number(p, "freeFoundationDistance", 3.5);
        FoundationClearance = r.Number(p, "foundationClearance", 0.05);
        GroundNormalMin = r.Number(p, "groundNormalMin", 0.75);
        CandidatesTried = (int)r.Number(p, "candidatesTried", 8);
        MaxPiecesPerBuilding = (int)r.Number(p, "maxPiecesPerBuilding", 4000);
        ContactTolerance = r.Number(p, "contactTolerance", 0.01);

        JsonElement h = r.Object(root, "hammer");
        EditWindow = r.Number(h, "editWindow", 600);
        RepairDelay = r.Number(h, "repairDelay", 30);
        RepairPerHit = r.Number(h, "repairPerHit", 100);
        RepairCostFraction = r.Number(h, "repairCostFraction", 0.2);

        JsonElement s = r.Object(root, "stability");
        GroundStability = (int)r.Number(s, "ground", 1000);
        MinimumStability = (int)r.Number(s, "minimum", 1);
        CollapseStep = r.Number(s, "collapseStep", 0.2);
        JsonElement loss = r.Object(s, "loss");
        LossFoundationToFoundation = (int)r.Number(loss, "foundationToFoundation", 0);
        LossToWall = (int)r.Number(loss, "toWall", 0);
        LossCentre = (int)r.Number(loss, "centre", 100);
        LossFromWall = (int)r.Number(loss, "fromWall", 100);
        LossFromStairs = (int)r.Number(loss, "fromStairs", 100);
        LossToRoof = (int)r.Number(loss, "toRoof", 100);
        LossSlabEdge = (int)r.Number(loss, "slabEdge", 200);
    }

    private void ReadMaterials(Reader r, JsonElement root)
    {
        var keys = new List<string>();
        foreach (JsonElement m in r.Array(root, "materials"))
        {
            string key = r.Text(m, "key", "");
            keys.Add(key);
            _materialNames[key] = r.Text(m, "name", key);
        }
        Materials = keys;
    }

    private void ReadGrades(Reader r, JsonElement root)
    {
        var grades = new List<GradeDef>();
        foreach (JsonElement g in r.Array(root, "grades"))
        {
            int index = grades.Count;
            var grade = new GradeDef
            {
                Index = index,
                Key = r.Text(g, "key", ""),
                Name = r.Text(g, "name", ""),
                Material = r.Text(g, "material", ""),
                Health = r.Number(g, "health", 1),
                DecayHours = r.Number(g, "decayHours", 1),
                Hard = ReadProtection(r, g, "hard"),
                Soft = ReadProtection(r, g, "soft"),
            };
            grades.Add(grade);
        }
        Grades = grades;
    }

    private static Protection ReadProtection(Reader r, JsonElement grade, string name)
    {
        var protection = new Protection();
        JsonElement o = r.Object(grade, name);
        foreach (DamageType type in Enum.GetValues<DamageType>())
        {
            protection.Set(type, r.Number(o, type.ToString().ToLowerInvariant(), 1));
        }
        return protection;
    }

    private void ReadPieces(Reader r, JsonElement root)
    {
        var pieces = new List<PieceDef>();
        foreach (JsonElement p in r.Array(root, "pieces"))
        {
            string key = r.Text(p, "key", "");
            int id = (int)r.Number(p, "id", pieces.Count);
            if (id != pieces.Count)
            {
                r.Problem($"piece {key}: ids must run 0, 1, 2… in order (found {id} at {pieces.Count})");
            }
            PieceFamily family = r.Enum(p, "family", PieceFamily.Foundation);
            PieceShape shape = r.Enum(p, "shape", PieceShape.Square);
            var sockets = new List<SocketDef>();
            foreach (JsonElement s in r.Array(p, "sockets"))
            {
                var families = new List<PieceFamily>();
                var named = new List<string>();
                foreach (JsonElement a in r.Array(s, "accepts"))
                {
                    string text = a.GetString() ?? "";
                    if (text.StartsWith("piece:", StringComparison.Ordinal))
                    {
                        named.Add(text["piece:".Length..]);
                    }
                    else if (Enum.TryParse(text, ignoreCase: true, out PieceFamily f))
                    {
                        families.Add(f);
                    }
                    else
                    {
                        r.Problem($"piece {key}: socket accepts unknown family \"{text}\"");
                    }
                }
                string at = r.Text(s, "at", "edges");
                SocketKind kind = at switch
                {
                    "edges" => SocketKind.Edge,
                    "edges-up" => SocketKind.EdgeUp,
                    "edges-down" => SocketKind.EdgeDown,
                    "centre" => SocketKind.Centre,
                    "top" => SocketKind.Top,
                    _ => Bad(r, $"piece {key}: unknown socket place \"{at}\"", SocketKind.Edge),
                };
                sockets.Add(new SocketDef
                {
                    At = kind,
                    Families = families,
                    Pieces = named,
                    SameShape = s.TryGetProperty("sameShape", out JsonElement same) && same.ValueKind == JsonValueKind.True,
                });
            }
            string opening = r.Text(p, "opening", "none");
            string stairs = r.Text(p, "stairs", "none");
            var def = new PieceDef
            {
                Id = id,
                Key = key,
                Name = r.Text(p, "name", key),
                Description = r.Text(p, "description", ""),
                Family = family,
                Shape = shape,
                Height = r.Number(p, "height", 0),
                Opening = opening switch
                {
                    "door" => Opening.Door,
                    "double" => Opening.Double,
                    "window" => Opening.Window,
                    _ => Opening.None,
                },
                Stairs = stairs switch
                {
                    "l" => StairsKind.L,
                    "u" => StairsKind.U,
                    "spiral" => StairsKind.Spiral,
                    "ramp" => StairsKind.Ramp,
                    "steps" => StairsKind.Steps,
                    _ => StairsKind.None,
                },
                Frame = p.TryGetProperty("frame", out JsonElement frame) && frame.ValueKind == JsonValueKind.True,
                Cost = r.Array(p, "cost").Select(c => c.TryGetInt32(out int n) ? n : 0).ToArray(),
                RotateSteps = (int)r.Number(p, "rotate", 0),
                Sockets = sockets,
            };
            pieces.Add(def);
            _pieces[key] = def;
        }
        Pieces = pieces;
        var wheel = new List<PieceDef>();
        foreach (JsonElement w in r.Array(root, "wheel"))
        {
            string key = w.GetString() ?? "";
            if (_pieces.TryGetValue(key, out PieceDef? def))
            {
                wheel.Add(def);
            }
            else
            {
                r.Problem($"wheel: unknown piece \"{key}\"");
            }
        }
        Wheel = wheel;
    }

    private void ReadDoors(Reader r, JsonElement root)
    {
        var doors = new List<DoorDef>();
        foreach (JsonElement d in r.Array(root, "doors"))
        {
            string key = r.Text(d, "key", "");
            var leaves = new List<LeafDef>();
            foreach (JsonElement l in r.Array(d, "leaves"))
            {
                System.Numerics.Vector3 size = r.Vector(l, "size");
                leaves.Add(new LeafDef
                {
                    Mesh = r.Text(l, "mesh", ""),
                    Hinge = r.Vector(l, "hinge"),
                    Size = size,
                    OpenYaw = r.Number(l, "openYaw", 0),
                    MirrorMesh = r.Text(l, "mirrorMesh", ""),
                    LockDepth = (float)r.Number(l, "lockDepth", Math.Abs(size.Z) / 2 + 0.002),
                    AdditionalMeshes = r.Array(l, "additionalMeshes").Select(e => e.ValueKind == JsonValueKind.String
                        ? e.GetString() ?? "" : Bad(r, "attachment mesh name must be text", "")).ToArray(),
                    Collision = ReadLeafCollision(r, l),
                });
            }
            string protection = r.Text(d, "protection", "wood");
            GradeDef? grade = Grade(protection);
            if (grade == null)
            {
                r.Problem($"door {key}: unknown protection grade \"{protection}\"");
            }
            JsonElement repair = r.Object(d, "repair");
            var door = new DoorDef
            {
                Key = key,
                Name = r.Text(d, "name", key),
                Opening = r.Enum(d, "opening", Opening.Door),
                Health = r.Number(d, "health", 100),
                Protection = grade ?? (Grades.Count > 0 ? Grades[0] : new GradeDef()),
                RepairMaterial = r.Text(repair, "material", "wood"),
                RepairFull = (int)r.Number(repair, "full", 1),
                Rolls = d.TryGetProperty("rolls", out JsonElement rolls) && rolls.ValueKind == JsonValueKind.True,
                Opens = r.Boolean(d, "opens", true),
                Lockable = r.Boolean(d, "lockable", true),
                Leaves = leaves,
                FrameMesh = r.Text(d, "frameMesh", ""),
                Hinges = r.Boolean(d, "hinges", false),
                Swings = r.Boolean(d, "swings", false),
                TravelSeconds = r.Number(d, "travel", 1.0),
                Lift = r.Array(d, "lift").Select(e => e.ValueKind == JsonValueKind.Number ? (float)e.GetDouble() : 0f).ToArray(),
            };
            doors.Add(door);
            _doors[key] = door;
        }
        Doors = doors;
    }

    private static ConvexPart[] ReadLeafCollision(Reader r, JsonElement leaf)
    {
        var parts = new List<ConvexPart>();
        foreach (JsonElement hull in r.Array(leaf, "collision"))
        {
            System.Numerics.Vector3[] points = r.Array(hull, "points").Select(r.Point).Distinct().ToArray();
            if (points.Length is < 4 or > 64)
            {
                r.Problem("attachment collision needs 4 to 64 distinct finite points per convex part");
                continue;
            }
            var part = new ConvexPart(points);
            if (part.Planes.Length < 4 || part.Min.X >= part.Max.X || part.Min.Y >= part.Max.Y || part.Min.Z >= part.Max.Z)
            {
                r.Problem("attachment collision must enclose a three-dimensional convex solid");
                continue;
            }
            parts.Add(part);
        }
        return parts.ToArray();
    }

    private void ReadLocks(Reader r, JsonElement root)
    {
        JsonElement l = r.Object(root, "locks");
        CodeLength = (int)r.Number(l, "codeLength", 4);
        LockMaxAuthorised = (int)r.Number(l, "maxAuthorised", 64);
        double[] waits = r.Array(l, "wrongCodeWait").Select(e => e.TryGetDouble(out double v) ? v : 1).ToArray();
        if (waits.Length > 0)
        {
            WrongCodeWait = waits;
        }
        JsonElement key = r.Object(l, "keyLock");
        KeyLockItem = r.Text(key, "key", "key-lock");
        KeyLockName = r.Text(key, "name", "Key Lock");
        KeyItem = r.Text(key, "keyItem", "door-key");
        JsonElement keyCost = r.Object(key, "keyCost");
        KeyCostMaterial = r.Text(keyCost, "material", "wood");
        KeyCost = (int)r.Number(keyCost, "amount", 75);
        JsonElement code = r.Object(l, "codeLock");
        CodeLockItem = r.Text(code, "key", "code-lock");
        CodeLockName = r.Text(code, "name", "Code Lock");
        Locks = new[] { new LockDef(KeyLockItem, KeyLockName, LockKind.Key, "keyLock"), new LockDef(CodeLockItem, CodeLockName, LockKind.Code, "codeLock") };
    }

    /// <summary>
    /// The piece rule's reach, sight slack and claim padding (placement.json, GROUND_SYSTEMS.md §6): the building's reach is
    /// the plan's and the hammer's.
    /// </summary>
    internal void UsePlacement(double reach, double sightTolerance, double claimPadding)
    {
        Reach = reach;
        SightTolerance = sightTolerance;
        ClaimPadding = claimPadding;
    }

    private void ReadClaim(Reader r, JsonElement root)
    {
        JsonElement c = r.Object(root, "claim");
        ClaimItem = r.Text(c, "key", "claim-device");
        ClaimName = r.Text(c, "name", "Tool Cupboard");
        ClaimSize = r.Vector(c, "size");
        ClaimPieceRadius = r.Number(c, "pieceRadius", 16);
        ClaimDeviceRadius = r.Number(c, "deviceRadius", 25);
        ClaimMaxAuthorised = (int)r.Number(c, "maxAuthorised", 64);
        ClaimHealth = r.Number(c, "health", 200);
        string protection = r.Text(c, "protection", "wood");
        GradeDef? grade = Grade(protection);
        if (grade == null)
        {
            r.Problem($"claim: unknown protection grade \"{protection}\"");
        }
        ClaimProtection = grade ?? (Grades.Count > 0 ? Grades[0] : new GradeDef());
        ClaimSlots = (int)r.Number(c, "slots", 24);
        AdvanceSeconds = r.Number(c, "advanceSeconds", 60);
        UpkeepPeriodHours = r.Number(c, "upkeepPeriodHours", 24);
        UpkeepBrackets = r.Array(c, "upkeepBrackets")
            .Select(b => ((int)r.Number(b, "pieces", 0), r.Number(b, "fraction", 0.1)))
            .ToArray();
    }

    private void Check(List<string> problems)
    {
        if (Grades.Count != GradeCount)
        {
            problems.Add($"grades: expected {GradeCount}, found {Grades.Count}");
        }
        for (int i = 1; i < Grades.Count; i++)
        {
            if (!(Grades[i].Health > Grades[i - 1].Health))
            {
                problems.Add($"grades: health must grow with the grade ({Grades[i].Key})");
            }
        }
        foreach (GradeDef g in Grades)
        {
            if (!Materials.Contains(g.Material))
            {
                problems.Add($"grade {g.Key}: unknown material \"{g.Material}\"");
            }
            if (!(g.DecayHours > 0))
            {
                problems.Add($"grade {g.Key}: decayHours must be positive");
            }
        }
        foreach (PieceDef p in Pieces)
        {
            if (p.Cost.Count != Grades.Count || p.Cost.Any(c => c <= 0))
            {
                problems.Add($"piece {p.Key}: needs a positive cost for each of the {Grades.Count} grades");
            }
            if ((p.Family is PieceFamily.Wall or PieceFamily.Stairs or PieceFamily.Roof) && !(p.Height > 0))
            {
                problems.Add($"piece {p.Key}: a {p.Family} needs a height");
            }
            if ((p.Family == PieceFamily.Wall) != (p.Shape == PieceShape.Wall))
            {
                problems.Add($"piece {p.Key}: walls, and only walls, have the wall shape");
            }
            foreach (SocketDef s in p.Sockets)
            {
                foreach (string key in s.Pieces)
                {
                    if (!_pieces.ContainsKey(key))
                    {
                        problems.Add($"piece {p.Key}: socket accepts unknown piece \"{key}\"");
                    }
                }
                bool edges = s.At is SocketKind.Edge or SocketKind.EdgeUp or SocketKind.EdgeDown;
                if (edges && !p.IsSlab)
                {
                    problems.Add($"piece {p.Key}: only foundations and floors have edge sockets");
                }
            }
        }
        foreach (DoorDef d in Doors)
        {
            if (d.Leaves.Count == 0)
            {
                problems.Add($"door {d.Key}: needs at least one leaf");
            }
            if (d.Leaves.Count > 2)
            {
                problems.Add($"door {d.Key}: at most two physics leaves fit the shared network state; group fixed meshes into additionalMeshes");
            }
            if (d.Opening == Opening.None || (!d.Opens && (d.Rolls || d.Lockable || d.Leaves.Any(l => l.OpenYaw != 0))))
            {
                problems.Add($"door {d.Key}: needs an aperture and consistent opening/locking capabilities");
            }
            if (!Materials.Contains(d.RepairMaterial))
            {
                problems.Add($"door {d.Key}: unknown repair material \"{d.RepairMaterial}\"");
            }
            if ((d.Hinges || d.Swings) && (!d.Opens || d.Rolls || d.Leaves.Any(l => l.OpenYaw == 0)))
            {
                problems.Add($"door {d.Key}: a hinge or swing side can only be chosen on a door whose leaves swing");
            }
            if (!(d.TravelSeconds is > 0.1 and <= 30))
            {
                problems.Add($"door {d.Key}: travel is 0.1 to 30 seconds");
            }
            if (d.Lift.Count == 1 || d.Lift.Any(h => !float.IsFinite(h) || h < 0))
            {
                problems.Add($"door {d.Key}: lift needs at least two finite heights");
            }
            if (d.Hinges && d.Leaves.Any(l => l.MirrorMesh.Length == 0))
            {
                problems.Add($"door {d.Key}: a door with a hinge choice needs each leaf's mirrorMesh");
            }
        }
        if (UpkeepBrackets.Count == 0)
        {
            problems.Add("claim: needs upkeep brackets");
        }
        if (!(ClaimHealth > 0) || ClaimSlots is < 1 or > 64 || !(AdvanceSeconds > 0))
        {
            problems.Add("claim: health and advanceSeconds must be positive, slots 1 … 64");
        }
        if (KeyCost < 1 || !Materials.Contains(KeyCostMaterial))
        {
            problems.Add("locks: a key costs at least 1 of a material");
        }
    }

    private static T Bad<T>(Reader r, string problem, T fallback)
    {
        r.Problem(problem);
        return fallback;
    }

    // Reads fields, noting problems instead of throwing.
    private sealed class Reader(List<string> problems)
    {
        public void Problem(string text) => problems.Add(text);

        public JsonElement Object(JsonElement o, string name)
        {
            if (o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Object)
            {
                return v;
            }
            problems.Add($"\"{name}\" is missing or not an object");
            return default;
        }

        public IEnumerable<JsonElement> Array(JsonElement o, string name)
        {
            if (o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Array)
            {
                return v.EnumerateArray().ToArray();
            }
            if (o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out _))
            {
                problems.Add($"\"{name}\" must be a list");
            }
            return System.Array.Empty<JsonElement>();
        }

        public double Number(JsonElement o, string name, double fallback)
        {
            if (o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out JsonElement v))
            {
                if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d) && double.IsFinite(d))
                {
                    return d;
                }
                problems.Add($"\"{name}\" must be a number");
            }
            return fallback;
        }

        public string Text(JsonElement o, string name, string fallback)
        {
            if (o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out JsonElement v))
            {
                if (v.ValueKind == JsonValueKind.String)
                {
                    return v.GetString() ?? fallback;
                }
                problems.Add($"\"{name}\" must be text");
            }
            return fallback;
        }

        public bool Boolean(JsonElement o, string name, bool fallback)
        {
            if (o.ValueKind != JsonValueKind.Object || !o.TryGetProperty(name, out JsonElement v)) return fallback;
            if (v.ValueKind is JsonValueKind.True or JsonValueKind.False) return v.GetBoolean();
            problems.Add($"\"{name}\" must be true or false");
            return fallback;
        }

        public System.Numerics.Vector3 Point(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Array)
            {
                double[] xyz = value.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out double v) ? v : double.NaN).ToArray();
                if (xyz.Length == 3 && xyz.All(v => double.IsFinite(v) && Math.Abs(v) <= float.MaxValue))
                    return new System.Numerics.Vector3((float)xyz[0], (float)xyz[1], (float)xyz[2]);
            }
            problems.Add("collision point must be three finite numbers");
            return default;
        }

        public (double, double) Pair(JsonElement o, string name, (double, double) fallback)
        {
            double[] values = Array(o, name).Select(e => e.TryGetDouble(out double d) ? d : double.NaN).ToArray();
            if (values.Length != 2 || values.Any(v => !double.IsFinite(v)))
            {
                problems.Add($"\"{name}\" must be two numbers");
                return fallback;
            }
            return (values[0], values[1]);
        }

        public System.Numerics.Vector3 Vector(JsonElement o, string name)
        {
            double[] values = Array(o, name).Select(e => e.TryGetDouble(out double d) ? d : double.NaN).ToArray();
            if (values.Length != 3 || values.Any(v => !double.IsFinite(v)))
            {
                problems.Add($"\"{name}\" must be three numbers");
                return default;
            }
            return new System.Numerics.Vector3((float)values[0], (float)values[1], (float)values[2]);
        }

        public T Enum<T>(JsonElement o, string name, T fallback) where T : struct, System.Enum
        {
            string text = Text(o, name, "");
            if (System.Enum.TryParse(text, ignoreCase: true, out T value))
            {
                return value;
            }
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"\"{name}\": unknown value \"{text}\""));
            return fallback;
        }
    }
}
