using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Hollow.Client;
using Hollow.Core;
using Hollow.Simulation.Content;
using Hollow.Simulation.Nature;
using Hollow.Simulation.Physics;
using Hollow.Surface;
using HollowUniverse.Fx;
using HollowUniverse.Surface;
using HollowUniverse.Surface.Flora;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

/// <summary>
/// The natural objects as the game changed them (GROUND.md §6.1). The surface view draws the planet's trees, bushes,
/// ferns and rocks; this tells it which ones are gone (felled, picked) or bare (a picked berry bush), redraws their
/// scatter tiles when that changes, lets a felled tree fall, and draws what the surface view doesn't: the outcrops,
/// smaller with each depletion stage (<see cref="GroundModels"/>), and the weak spots of the trees and outcrops near
/// the player.
/// </summary>
public partial class NatureView : Node3D
{
    /// <summary>Outcrops are drawn out to this distance (64 m tiles around the player: 5 × 5).</summary>
    private const int OutcropTiles = 2;

    /// <summary>Trees felled within this distance fall; farther ones just go.</summary>
    private const float FallDistance = 150;

    private readonly Dictionary<(int X, int Z), List<(ulong Id, Node3D Node, int Stage)>> _outcrops = new();
    private readonly List<NaturalObject> _scratch = new();
    private readonly List<MeshInstance3D> _spots = new();
    private readonly List<TreeFall> _falls = new();
    private readonly HashSet<ulong> _falling = new();
    private (int X, int Z) _centre = (int.MinValue, 0);
    private uint _revision = uint.MaxValue;
    private static StandardMaterial3D? _spotMaterial;

    /// <summary>A weak spot's glowing mark (also shown once while the world opens, so its shader is ready).</summary>
    public static MeshInstance3D Spot()
    {
        if (_spotMaterial == null)
        {
            _spotMaterial = new StandardMaterial3D
            {
                AlbedoColor = Colors.Black,
                EmissionEnabled = true,
                Emission = new Color(1f, 0.45f, 0.1f),
                DisableReceiveShadows = true,
                Roughness = 1,
                MetallicSpecular = 0,
            };
            Glow.Track(_spotMaterial, 2.5f);
        }
        return new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.07f, Height = 0.14f, RadialSegments = 12, Rings = 6 },
            MaterialOverride = _spotMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }
    private bool _hooked;

    public GameClient Client { get; set; } = null!;

    /// <summary>The surface view whose plants this changes.</summary>
    public SurfaceView World { get; set; } = null!;

    /// <summary>Where the player is (feet), for what is drawn near them.</summary>
    public void Refresh(Num.Vector3 feet, double delta)
    {
        if (Client.Nature == null || Client.Natural == null)
        {
            return;
        }
        long t = System.Diagnostics.Stopwatch.GetTimestamp();
        Hook();
        HashSet<(int, int)> changed = TakeChanges(feet);
        Lap("changes", ref t);
        RedrawPlants();
        Lap("redraw", ref t);
        UpdateOutcrops(feet, changed);
        Lap("outcrops", ref t);
        UpdateSpots(feet);
        Lap("spots", ref t);
        UpdateFalls(delta);
        Lap("falls", ref t);
    }

    private static void Lap(string what, ref long since)
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        double ms = (now - since) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        if (ms > 4)
        {
            FrameEvents.Note($"{what} {ms:0} ms");
        }
        since = now;
    }

    // ---------------------------------------------------------------- the plants of the surface view

    private void Hook()
    {
        if (_hooked || World.Plants is not { } plants)
        {
            return;
        }
        _hooked = true;
        plants.Changes = DrawnKind;
        plants.ChangesSnapshot = FrozenKinds;
    }

    // What DrawnKind answers, frozen for a rebuild of the near plants on a worker thread: the objects the server says
    // are gone (felled, picked) as they are now.
    private Func<TileKey, ScatterItem, ScatterKind> FrozenKinds()
    {
        var gone = new HashSet<ulong>(_falling);
        if (Client.Natural is { } state)
        {
            foreach (KeyValuePair<ulong, NaturalOverride> o in state.Overrides)
            {
                if (o.Value.Depleted)
                {
                    gone.Add(o.Key);
                }
            }
        }
        if (gone.Count == 0)
        {
            return static (_, item) => item.Kind;
        }
        return (tile, item) => !gone.Contains(ScatterNaturalObjects.IdOf(tile, item.Index)) ? item.Kind : Picked(item.Kind);
    }

    // What a felled or picked scatter object is drawn as: a berry bush bare, anything else not at all.
    private static ScatterKind Picked(ScatterKind kind) => kind switch
    {
        ScatterKind.BerryRed => FloraLibrary.BareBerryRed,
        ScatterKind.BerryBlue => FloraLibrary.BareBerryBlue,
        _ => Vegetation.NotDrawn,
    };

    // Felled trees and picked ferns aren't drawn; a picked berry bush is drawn bare.
    private ScatterKind DrawnKind(TileKey tile, ScatterItem item)
    {
        NaturalState? state = Client.Natural;
        if (state == null || state.Count == 0 && _falling.Count == 0)
        {
            return item.Kind;
        }
        ulong id = ScatterNaturalObjects.IdOf(tile, item.Index);
        if (_falling.Contains(id))
        {
            return Vegetation.NotDrawn;
        }
        if (!state.IsDepleted(id))
        {
            return item.Kind;
        }
        return Picked(item.Kind);
    }

    // The objects whose state changed since last frame: their scatter tiles are drawn again, felled trees near the
    // player fall, and the 64 m tiles of outcrops that changed are rebuilt.
    private HashSet<(int, int)> TakeChanges(Num.Vector3 feet)
    {
        var tiles = new HashSet<(int, int)>();
        NaturalState state = Client.Natural!;
        if (state.Revision == _revision)
        {
            return tiles;
        }
        _revision = state.Revision;
        // Many at once is an area coming into range, not things being hit: those make no sound.
        bool audible = state.Changed.Count <= 20;
        foreach (ulong id in state.Changed)
        {
            // The ID names the scatter tile and kind: no generating a far tile on this thread just to look at it.
            if (ScatterNaturalObjects.Decode(id) is not var (tile, index))
            {
                continue;
            }
            Num.Vector3 centre = Client.Frame.ToLocal(tile.Centre * Client.Surface!.Radius);
            float distance = Num.Vector2.Distance(new Num.Vector2(centre.X, centre.Z), new Num.Vector2(feet.X, feet.Z));
            bool tree = (ScatterKind)(index >> 24) is ScatterKind.Fir or ScatterKind.Pine or ScatterKind.Birch or ScatterKind.Broadleaf;
            bool falls = false;
            if (distance < FallDistance + 60 && Cached(id, out NaturalObject o))
            {
                tiles.Add(PhysicsWorld.TileOf(o.Position));
                distance = Num.Vector3.Distance(o.Position, feet);
                if (distance < 70)
                {
                    Sound(o, state, audible);
                }
                falls = tree && state.IsDepleted(id) && distance < FallDistance && !_falling.Contains(id);
                if (falls)
                {
                    StartFall(o, tile, feet);
                }
            }
            // The plants near the camera (within 300 m) are drawn again at once for what is within reach or starts
            // to fall, otherwise every five seconds for everything that changed meanwhile (others gathering).
            if (distance < NearPlants)
            {
                Schedule(_nearRedraw, tile.Packed, falls || distance < 25 ? 0 : 5);
            }
            // A tree's impostor shows beyond 58 m: its block is drawn again every ten seconds.
            if (tree && distance > 45)
            {
                Schedule(_blockRedraw, tile.Packed, 10);
            }
        }
        state.Changed.Clear();
        return tiles;
    }

    // What each object near the player was when last seen (health, stage, weak spot, gone), so a change can sound.
    private readonly Dictionary<ulong, (float Health, int Stage, uint Weak, bool Gone)> _was = new();

    // A hit on a tree or an outcrop (its health dropped), the weak spot moving, an outcrop breaking a stage, a tree
    // felled, a bush picked: their sounds (GROUND.md §4.9) and effects (§9.3: chips, bark, dust and sparks thrown
    // towards the player where the tool bit, a flash on the weak spot, an outcrop breaking up).
    private void Sound(in NaturalObject o, NaturalState state, bool audible)
    {
        if (Client.Content.Natural(o.Kind) is not { } kind || !kind.IsHarvested)
        {
            return;
        }
        state.TryGetOverride(o.Id, out NaturalOverride now);
        bool has = state.TryGetOverride(o.Id, out _);
        float health = has ? now.Health : float.MaxValue;
        var current = (health, state.Stage(o, kind), has ? now.PackedWeakSpot : 0u, has && now.Depleted);
        bool known = _was.TryGetValue(o.Id, out var before);
        _was[o.Id] = current;
        if (_was.Count > 4000)
        {
            _was.Clear();
        }
        if (!audible)
        {
            return;
        }
        float prior = known ? before.Health : float.MaxValue;
        bool tree = kind.Class == NaturalClass.Tree;
        Vector3 at = GroundView.ToGodot(o.Position + o.Up * (tree ? 1.3f : 0.7f) * Math.Max(0.6f, o.Scale));
        if (current.Item4 && !(known && before.Gone))
        {
            // Used up: an outcrop's remnant crumbles into its gibs, with its sound and dust (GibsView, GROUND.md §9.4).
            if (tree)
            {
                Sounds.At("gather.wood.break", at);
            }
            return;
        }
        if (health < prior)
        {
            string hit = tree ? "gather.wood.hit" : kind.Key == "stone-node" ? "gather.stone.hit" : "gather.ore.hit";
            Sounds.At(hit, at);
            // Where the tool bit: on the side towards the player, at the height of a swing.
            Num.Vector3 toMe = Client.Character.Position - o.Position;
            toMe -= o.Up * Num.Vector3.Dot(toMe, o.Up);
            toMe = toMe.LengthSquared() > 1e-4f ? Num.Vector3.Normalize(toMe) : new Num.Vector3(1, 0, 0);
            float height = tree ? 1.1f : 0.5f * Math.Max(0.6f, o.Scale);
            Num.Vector3 bite = o.Position + o.Up * height + toMe * (kind.Radius * o.Scale * (tree ? 1f : 0.8f));
            Vector3 point = GroundView.ToGodot(bite), facing = GroundView.ToGodot(toMe);
            string chips = tree ? "gather.wood" : kind.Key is "metal-node" or "sulphur-node" ? "gather.sparks" : "gather.rock";
            FxDirector.Play(chips, point, facing, new FxParams { Direction = -facing });
            if (current.Item3 != 0 && (!known || before.Weak != current.Item3))
            {
                Sounds.At("gather.weakspot", at, -2);
                FxDirector.Play("gather.weakspot", point, facing);
            }
            if (known && current.Item2 > before.Stage)
            {
                Sounds.At($"gather.node.break.{current.Item2}", at);
                FxDirector.Play("gather.node-break", GroundView.ToGodot(o.Position + o.Up * 0.4f * o.Scale), GroundView.ToGodot(o.Up),
                    new FxParams { Scale = 0.6f });
            }
        }
    }

    // An object whose scatter tile is generated already (those around the player are, for the prediction's physics):
    // a frame never waits for a tile to be generated.
    private bool Cached(ulong id, out NaturalObject o)
    {
        if (Client.Nature is ScatterNaturalObjects scatter)
        {
            return scatter.TryGetCached(id, out o);
        }
        return Client.Nature!.TryGet(id, out o);
    }

    /// <summary>The surface view draws plant meshes out to about this far; beyond, only tree impostors.</summary>
    private const float NearPlants = 320;

    private readonly Dictionary<ulong, double> _nearRedraw = new();
    private readonly Dictionary<ulong, double> _blockRedraw = new();
    private int _nearBuilds;

    private static void Schedule(Dictionary<ulong, double> pending, ulong tile, double after)
    {
        double due = Time.GetTicksMsec() / 1000.0 + after;
        pending[tile] = pending.TryGetValue(tile, out double was) ? Math.Min(was, due) : due;
    }

    // Redrawing the plants rebuilds every mesh near the camera (10–15 ms on a worker thread), so every tile waiting is
    // redrawn in one go once the first is due; impostor blocks likewise.
    private void RedrawPlants()
    {
        if (World.Plants is not { } plants)
        {
            return;
        }
        plants.ApplyRefreshes();
        if (plants.NearBuilds != _nearBuilds)
        {
            _nearBuilds = plants.NearBuilds;
            FrameEvents.Note("plants in place");
        }
        double now = Time.GetTicksMsec() / 1000.0;
        if (_nearRedraw.Count > 0 && _nearRedraw.Values.Min() <= now)
        {
            foreach (ulong packed in _nearRedraw.Keys)
            {
                plants.Refresh(TileKey.FromPacked(packed)!.Value, near: true, trees: false);
            }
            _nearRedraw.Clear();
            FrameEvents.Note("plants redrawn");
        }
        if (_blockRedraw.Count > 0 && _blockRedraw.Values.Min() <= now)
        {
            foreach (ulong packed in _blockRedraw.Keys)
            {
                plants.Refresh(TileKey.FromPacked(packed)!.Value, near: false, trees: true);
            }
            _blockRedraw.Clear();
            FrameEvents.Note("impostors redrawn");
        }
    }

    // ---------------------------------------------------------------- outcrops

    private void UpdateOutcrops(Num.Vector3 feet, HashSet<(int, int)> changed)
    {
        // The placeholders take the plants' rock material: wait for it.
        PlaceholderModels.Library ??= World.Plants?.Library;
        if (PlaceholderModels.Library == null && GroundModels.Provider is PlaceholderModels)
        {
            return;
        }
        (int, int) centre = PhysicsWorld.TileOf(feet);
        if (centre == _centre && changed.Count == 0)
        {
            return;
        }
        _centre = centre;
        foreach ((int X, int Z) key in _outcrops.Keys.Where(k => Math.Max(Math.Abs(k.X - _centre.X), Math.Abs(k.Z - _centre.Z)) > OutcropTiles + 1).ToList())
        {
            Clear(key);
        }
        for (int z = _centre.Z - OutcropTiles; z <= _centre.Z + OutcropTiles; z++)
        {
            for (int x = _centre.X - OutcropTiles; x <= _centre.X + OutcropTiles; x++)
            {
                if (_outcrops.ContainsKey((x, z)) && !changed.Contains((x, z)))
                {
                    continue;
                }
                BuildOutcrops(x, z);
            }
        }
    }

    private void Clear((int X, int Z) key)
    {
        if (_outcrops.Remove(key, out var list))
        {
            foreach ((_, Node3D node, _) in list)
            {
                node.QueueFree();
            }
        }
    }

    private void BuildOutcrops(int tx, int tz)
    {
        Dictionary<ulong, (Node3D Node, int Stage)> old = _outcrops.TryGetValue((tx, tz), out var existing)
            ? existing.ToDictionary(e => e.Id, e => (e.Node, e.Stage))
            : new Dictionary<ulong, (Node3D, int)>();
        var list = new List<(ulong, Node3D, int)>();
        _scratch.Clear();
        Client.Nature!.Query(tx * PhysicsWorld.TileSize, tz * PhysicsWorld.TileSize, (tx + 1) * PhysicsWorld.TileSize, (tz + 1) * PhysicsWorld.TileSize, _scratch);
        NaturalState state = Client.Natural!;
        foreach (NaturalObject o in _scratch)
        {
            if (Client.Content.Natural(o.Kind) is not { Class: NaturalClass.Node } kind || state.IsDepleted(o.Id))
            {
                continue;
            }
            int stage = state.Stage(o, kind);
            if (old.Remove(o.Id, out var kept) && kept.Stage == stage)
            {
                list.Add((o.Id, kept.Node, stage));
                continue;
            }
            kept.Node?.QueueFree();
            int variant = (int)(o.Id % 6);
            Node3D? node = GroundModels.Provider.Outcrop(kind.Key, stage, variant);
            if (node == null)
            {
                continue;
            }
            node.Transform = new Transform3D(Upright(o.Up, o.Yaw).Scaled(Vector3.One * o.Scale), GroundView.ToGodot(o.Position));
            AddChild(node);
            list.Add((o.Id, node, stage));
        }
        foreach ((Node3D node, _) in old.Values)
        {
            node.QueueFree();
        }
        _outcrops[(tx, tz)] = list;
        FrameEvents.Note("outcrops");
    }

    /// <summary>A basis with +Y along <paramref name="up"/>, turned by <paramref name="yaw"/>.</summary>
    public static Basis Upright(Num.Vector3 up, float yaw)
    {
        (Num.Vector3 forward, _) = Hollow.Simulation.Movement.CharacterMotor.HorizontalBasis(yaw, up);
        return Basis.LookingAt(GroundView.ToGodot(forward), GroundView.ToGodot(up));
    }

    // ---------------------------------------------------------------- weak spots

    // Rust's weak spot: a glowing mark on the trees and outcrops within reach once they have been hit.
    private void UpdateSpots(Num.Vector3 feet)
    {
        int used = 0;
        _scratch.Clear();
        Client.Nature!.Query(feet.X - 6, feet.Z - 6, feet.X + 6, feet.Z + 6, _scratch);
        foreach (NaturalObject o in _scratch)
        {
            if (used >= 8 || Client.Content.Natural(o.Kind) is not { IsHarvested: true } kind || Client.Natural!.WeakSpot(o, kind) is not { } weak)
            {
                continue;
            }
            if (used >= _spots.Count)
            {
                MeshInstance3D spot = Spot();
                AddChild(spot);
                _spots.Add(spot);
            }
            // Just outside the bark or the stone, where the drawn surface is.
            Num.Vector3 outward = weak - o.Position;
            outward -= o.Up * Num.Vector3.Dot(outward, o.Up);
            if (kind.Class != NaturalClass.Tree)
            {
                outward = weak - NaturalShapes.Rock(o, kind).Centre;
            }
            Num.Vector3 shown = outward.LengthSquared() > 1e-6f ? weak + Num.Vector3.Normalize(outward) * 0.08f : weak;
            _spots[used].Position = GroundView.ToGodot(shown);
            _spots[used].Visible = true;
            used++;
        }
        for (int i = used; i < _spots.Count; i++)
        {
            _spots[i].Visible = false;
        }
    }

    // ---------------------------------------------------------------- a tree falls

    private sealed class TreeFall
    {
        public ulong Id;
        public MultiMeshInstance3D? Instance;
        public Task<ScatterItem?>? Item;
        public Transform3D Base;
        public Vector3 Axis;
        public Vector3 Up;
        public Vector3 Away;
        public float Height = 12;
        public bool Landed;
        public double Time;
    }

    // The tree's own model (its species and variant, from its scatter item) tips over away from the player.
    private void StartFall(NaturalObject o, TileKey tile, Num.Vector3 feet)
    {
        if (World.Surface is not { } surface)
        {
            return;
        }
        _falling.Add(o.Id);
        (TileKey _, int index) = ScatterNaturalObjects.Decode(o.Id)!.Value;
        Num.Vector3 away = o.Position - feet;
        away -= o.Up * Num.Vector3.Dot(away, o.Up);
        if (away.LengthSquared() < 0.01f)
        {
            away = new Num.Vector3(1, 0, 0);
        }
        Vector3 up = GroundView.ToGodot(o.Up), dir = GroundView.ToGodot(Num.Vector3.Normalize(away));
        _falls.Add(new TreeFall
        {
            Id = o.Id,
            Axis = up.Cross(dir).Normalized(),
            Up = up,
            Away = dir,
            Base = new Transform3D(Basis.Identity, GroundView.ToGodot(o.Position)),
            Item = Task.Run(() => (ScatterItem?)surface.ScatterFor(tile).FirstOrDefault(i => i.Index == index)),
        });
    }

    private void UpdateFalls(double delta)
    {
        for (int i = _falls.Count - 1; i >= 0; i--)
        {
            TreeFall fall = _falls[i];
            if (fall.Instance == null)
            {
                if (fall.Item is not { IsCompleted: true } task)
                {
                    continue;
                }
                if (!task.IsCompletedSuccessfully || task.Result is not { } item || !Build(fall, item))
                {
                    End(i);
                    continue;
                }
            }
            fall.Time += delta;
            // Slowly at first, then faster, as a hinged trunk does; it lies a moment, then sinks away.
            const double Tip = 2.4, Lie = 2.2, Sink = 1.6;
            float t = (float)Math.Min(fall.Time / Tip, 1);
            float angle = 1.45f * t * t * t;
            float sink = fall.Time > Tip + Lie ? (float)((fall.Time - Tip - Lie) / Sink) : 0;
            Basis turn = new(fall.Axis, angle);
            fall.Instance!.Transform = new Transform3D(turn, fall.Base.Origin - fall.Up * (sink * 1.2f));
            if (t >= 1 && !fall.Landed)
            {
                // The trunk lands: dust and litter along it.
                fall.Landed = true;
                float half = fall.Height * 0.45f;
                FxDirector.Play("gather.tree-fall", fall.Base.Origin + fall.Away * half, fall.Up,
                    new FxParams { Direction = fall.Away, Area = new Vector3(half, 0.2f, 0.7f) });
            }
            if (fall.Time > Tip + Lie + Sink)
            {
                End(i);
            }
        }
    }

    private bool Build(TreeFall fall, ScatterItem item)
    {
        FloraLibrary? library = World.Plants?.Library;
        if (library?.For(item.Kind) is not { } species || species.Variants.Count == 0)
        {
            return false;
        }
        FloraLibrary.Variant variant = species.Variants[item.Variant % species.Variants.Count];
        float scale = item.Scale * species.Scale;
        fall.Height = Math.Max(2f, variant.Height * scale);
        Vector3 up = World.Frame!.DirectionToRender(item.Direction);
        Vector3 reference = Math.Abs(up.Z) < 0.9f ? Vector3.Back : Vector3.Right;
        Vector3 x = reference.Cross(up).Normalized();
        Vector3 z = x.Cross(up);
        Basis basis = new Basis(x, up, z).Rotated(up, item.Yaw).Scaled(new Vector3(scale, scale, scale));
        ulong h = Hash64.Of((ulong)(uint)item.Index, item.Variant, 0x54494E54); // the same tint as the surface view's
        var buffer = new float[16];
        Basis b = basis;
        float[] values =
        {
            b.X.X, b.Y.X, b.Z.X, 0, b.X.Y, b.Y.Y, b.Z.Y, -0.05f, b.X.Z, b.Y.Z, b.Z.Z, 0,
            (h & 0xFFFF) / 65535f, ((h >> 16) & 0xFFFF) / 65535f, 0, 0,
        };
        values.CopyTo(buffer, 0);
        fall.Instance = new MultiMeshInstance3D
        {
            Name = "Falling tree",
            Multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseCustomData = true,
                Mesh = variant.Lod0,
                InstanceCount = 1,
                Buffer = buffer,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
        };
        AddChild(fall.Instance);
        FrameEvents.Note("tree falls");
        return true;
    }

    private void End(int index)
    {
        TreeFall fall = _falls[index];
        fall.Instance?.QueueFree();
        _falls.RemoveAt(index);
        _falling.Remove(fall.Id);
    }
}
