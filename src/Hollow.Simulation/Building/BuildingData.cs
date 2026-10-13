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
    public string Mesh { get; init; } = "";

    /// <summary>The hinge (the leaf's origin) in the doorway's frame: metres, Y up, outside +Z.</summary>
    public System.Numerics.Vector3 Hinge { get; init; }

    /// <summary>Width (negative: the leaf reaches toward −X), height and thickness.</summary>
    public System.Numerics.Vector3 Size { get; init; }

    /// <summary>Yaw of the open leaf about its hinge, degrees (from the kit's assembly manifest).</summary>
    public double OpenYaw { get; init; }
}

/// <summary>A door: a deployable item fitted into a doorway (single) or a wall frame (double, garage).</summary>
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

    public IReadOnlyList<LeafDef> Leaves { get; init; } = Array.Empty<LeafDef>();

    public string FrameMesh { get; init; } = "";
}

/// <summary>The building rules' numbers (<c>game/data/ground/building.json</c>), read by the server and the client.</summary>
public sealed class BuildingData
{
    public const int GradeCount = 5;

    private readonly Dictionary<string, PieceDef> _pieces = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DoorDef> _doors = new(StringComparer.Ordinal);
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

    /// <summary>The item a key lock's keys are (its condition holds the lock's number), and what cutting one costs.</summary>
    public string KeyItem { get; private set; } = "door-key";
    public string KeyCostMaterial { get; private set; } = "wood";
    public int KeyCost { get; private set; } = 75;

    // ---------------------------------------------------------------- claim device

    public string ClaimItem { get; private set; } = "claim-device";
    public string ClaimName { get; private set; } = "Claim Device";

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

    public string MaterialName(string key) => _materialNames.TryGetValue(key, out string? name) ? name : key;

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

        JsonElement p = r.Object(root, "placement");
        Reach = r.Number(p, "reach", 8);
        SocketAimRadius = r.Number(p, "socketAimRadius", 1.75);
        FreeFoundationDistance = r.Number(p, "freeFoundationDistance", 3.5);
        FoundationClearance = r.Number(p, "foundationClearance", 0.05);
        GroundNormalMin = r.Number(p, "groundNormalMin", 0.75);
        SightTolerance = r.Number(p, "sightTolerance", 0.35);
        ClaimPadding = r.Number(p, "claimPadding", 2.25);
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
                leaves.Add(new LeafDef
                {
                    Mesh = r.Text(l, "mesh", ""),
                    Hinge = r.Vector(l, "hinge"),
                    Size = r.Vector(l, "size"),
                    OpenYaw = r.Number(l, "openYaw", 0),
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
                Opening = r.Text(d, "opening", "door") == "double" ? Opening.Double : Opening.Door,
                Health = r.Number(d, "health", 100),
                Protection = grade ?? (Grades.Count > 0 ? Grades[0] : new GradeDef()),
                RepairMaterial = r.Text(repair, "material", "wood"),
                RepairFull = (int)r.Number(repair, "full", 1),
                Rolls = d.TryGetProperty("rolls", out JsonElement rolls) && rolls.ValueKind == JsonValueKind.True,
                Leaves = leaves,
                FrameMesh = r.Text(d, "frameMesh", ""),
            };
            doors.Add(door);
            _doors[key] = door;
        }
        Doors = doors;
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
    }

    private void ReadClaim(Reader r, JsonElement root)
    {
        JsonElement c = r.Object(root, "claim");
        ClaimItem = r.Text(c, "key", "claim-device");
        ClaimName = r.Text(c, "name", "Claim Device");
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
            if (!Materials.Contains(d.RepairMaterial))
            {
                problems.Add($"door {d.Key}: unknown repair material \"{d.RepairMaterial}\"");
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
