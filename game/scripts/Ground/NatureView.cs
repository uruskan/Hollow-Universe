using System;
using System.Collections.Generic;
using System.Globalization;
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
/// scatter tiles when that changes, and draws what the surface view doesn't: the outcrops at their depletion stage
/// (<see cref="GroundModels"/>), the weak spots near the player (<see cref="NatureMarkers"/>), and gathering's feedback.
/// Every hit plays its kind's look from gather.json where it met the drawn surface (effects, sounds, a run of weak-spot
/// hits rising in pitch, a stage breaking, the crown shaking), and a felled tree near the player tips over, lands and
/// goes. Nothing here knows trees from outcrops: the look says what to do.
/// </summary>
public partial class NatureView : Node3D
{
    /// <summary>Outcrops are drawn out to this distance (64 m tiles around the player: 5 × 5).</summary>
    private const int OutcropTiles = 2;

    /// <summary>Trees felled within this distance fall; farther ones just go.</summary>
    private const float FallDistance = 150;

    /// <summary>Hits are seen and heard within this distance.</summary>
    private const float CueDistance = 70;

    /// <summary>Weak spots are drawn within this distance of the player.</summary>
    private const float MarkerDistance = 12;

    private readonly Dictionary<(int X, int Z), List<(ulong Id, Node3D Node, int Stage)>> _outcrops = new();
    private readonly List<NaturalObject> _scratch = new();
    private readonly List<TreeFall> _falls = new();
    private readonly HashSet<ulong> _falling = new();
    private readonly HashSet<(int, int)> _changedTiles = new();
    private readonly HashSet<ulong> _spotted = new();
    private readonly List<PendingSound> _pending = new();
    private NatureMarkers _markers = null!;
    private (int X, int Z) _centre = (int.MinValue, 0);
    private uint _revision = uint.MaxValue;
    private uint _markedRevision = uint.MaxValue;
    private Num.Vector3 _markedAt = new(float.MaxValue);
    private Num.Vector3 _scannedAt = new(float.MaxValue);
    private bool _hooked;

    private readonly record struct PendingSound(double At, string Key, Vector3 Position);

    public GameClient Client { get; set; } = null!;

    /// <summary>The surface view whose plants this changes.</summary>
    public SurfaceView World { get; set; } = null!;

    public override void _Ready()
    {
        _markers = new NatureMarkers { Name = "Markers" };
        AddChild(_markers);
        foreach (NaturalKind kind in Client.Content.NaturalKinds)
        {
            if (kind.Gather?.Look.Marker is { } look)
            {
                _markers.Prepare(look);
            }
        }
    }

    /// <summary>
    /// The weak spots' markers of every kind, shown once while the world opens (their decals' textures enter the decal
    /// atlas and their pipelines are built then, not in the middle of play).
    /// </summary>
    public static Node3D Warmup(GameContent content)
    {
        var markers = new NatureMarkers { Name = "Markers" };
        foreach (NaturalKind kind in content.NaturalKinds)
        {
            if (kind.Gather?.Look.Marker is { } look)
            {
                markers.Prepare(look);
            }
        }
        markers.RevealForWarmup();
        return markers;
    }

    private static double Now => Time.GetTicksMsec() / 1000.0;

    private static long _costTicks, _costBytes, _costFrames, _quietFrames, _quietBytes;

    /// <summary>What <see cref="Refresh"/> has cost so far: mean time a frame, and bytes allocated a frame in all and on
    /// frames when nothing natural changed (the steady state: none expected).</summary>
    public static string CostReport() => _costFrames == 0 ? "no frames" : string.Create(CultureInfo.InvariantCulture,
        $"{_costTicks * 1e6 / System.Diagnostics.Stopwatch.Frequency / _costFrames:0} µs a frame over {_costFrames} frames, " +
        $"{(double)_costBytes / _costFrames:0} B allocated a frame ({(_quietFrames > 0 ? (double)_quietBytes / _quietFrames : 0):0} B on {_quietFrames} quiet frames)");

    /// <summary>Where the player is (feet), for what is drawn near them.</summary>
    public void Refresh(Num.Vector3 feet, double delta)
    {
        if (Client.Survival().Nature == null || Client.Survival().Natural == null)
        {
            return;
        }
        long bytes = GC.GetAllocatedBytesForCurrentThread(), start = System.Diagnostics.Stopwatch.GetTimestamp();
        bool quiet = Client.Survival().Natural.Revision == _revision;
        RefreshNow(feet, delta);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
        _costTicks += System.Diagnostics.Stopwatch.GetTimestamp() - start;
        _costBytes += allocated;
        _costFrames++;
        if (quiet && _falls.Count == 0)
        {
            _quietFrames++;
            _quietBytes += allocated;
        }
    }

    private void RefreshNow(Num.Vector3 feet, double delta)
    {
        long t = System.Diagnostics.Stopwatch.GetTimestamp();
        Hook();
        TakeChanges(feet);
        Lap("changes", ref t);
        RedrawPlants();
        Lap("redraw", ref t);
        UpdateOutcrops(feet, _changedTiles);
        Lap("outcrops", ref t);
        UpdateMarkers(feet);
        Lap("markers", ref t);
        UpdateFalls(delta);
        UpdateShake();
        PlayPending();
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
        if (Client.Survival().Natural is { } state)
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
        NaturalState? state = Client.Survival().Natural;
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

    // The objects whose state changed since last frame: their hits play, their scatter tiles are drawn again, felled
    // trees near the player fall, and the 64 m tiles of outcrops that changed are rebuilt (into _changedTiles).
    private void TakeChanges(Num.Vector3 feet)
    {
        _changedTiles.Clear();
        NaturalState state = Client.Survival().Natural!;
        if (state.Revision == _revision)
        {
            return;
        }
        _revision = state.Revision;
        foreach (ulong id in state.Changed)
        {
            // The ID names the scatter tile and kind: no generating a far tile on this thread just to look at it.
            if (ScatterNaturalObjects.Decode(id) is not var (tile, index))
            {
                continue;
            }
            Num.Vector3 centre = Client.Frame.ToLocal(tile.Centre * Client.Surface!.Radius);
            float distance = Num.Vector2.Distance(new Num.Vector2(centre.X, centre.Z), new Num.Vector2(feet.X, feet.Z));
            bool tree = World.Plants?.Library?.For((ScatterKind)(index >> 24)) is { Tree: true };
            bool falls = false;
            if (distance < FallDistance + 60 && Cached(id, out NaturalObject o))
            {
                _changedTiles.Add(PhysicsWorld.TileOf(o.Position));
                distance = Num.Vector3.Distance(o.Position, feet);
                falls = React(o, tile, distance);
            }
            else
            {
                _spotted.Remove(id);
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
    }

    // What each object near the player was when last seen, so a change shows what happened to it.
    private readonly record struct Seen(float Health, int Stage, byte Hits, byte Streak, bool Gone);

    private readonly Dictionary<ulong, Seen> _was = new();

    // An object changed: its look's cues for what happened (GROUND.md §9.3: a fresh hit where it met the surface, a run
    // of weak-spot hits or its end, a stage reached, gone), its weak spot kept track of, a felled tree near the player
    // falling. Returns whether it starts to fall.
    private bool React(in NaturalObject o, TileKey tile, float distance)
    {
        NaturalState state = Client.Survival().Natural!;
        if (Client.Content.Natural(o.Kind) is not { Gather: { } rules } kind)
        {
            return false;
        }
        bool has = state.TryGetOverride(o.Id, out NaturalOverride now);
        var current = new Seen(has ? now.Health : float.MaxValue, state.Stage(o, kind), has ? now.SeenHits : (byte)0,
            has ? now.Streak : (byte)0, has && now.Depleted);
        bool known = _was.TryGetValue(o.Id, out Seen before);
        if (!known)
        {
            before = new Seen(float.MaxValue, 0, 0, 0, false);
            if (_was.Count > 4000)
            {
                _was.Clear();
            }
        }
        _was[o.Id] = current;
        if (has && now.HasWeakSpot && !now.Depleted)
        {
            _spotted.Add(o.Id);
        }
        else
        {
            _spotted.Remove(o.Id);
        }
        // Only a fresh hit plays: an area coming into range sends old changes (GameClient.FreshHitTicks).
        if (current.Hits == before.Hits)
        {
            return false;
        }
        GatherLook look = rules.Look;
        Num.Vector3 point = o.Position + now.LastHit;
        Num.Vector3 normal = rules.Spot.Surface.NormalAt(o, kind, current.Stage, point);
        float ground = GroundView.ToGodot(o.Position).Y;
        if (distance < CueDistance)
        {
            Cue(look.Hit, point, normal, ground);
            if (current.Streak > 0)
            {
                Cue(look.SpotHit, point, normal, ground, 1 + (look.SpotHit?.PitchStep ?? 0) * (current.Streak - 1));
            }
            else if (before.Streak > 0)
            {
                Cue(look.Missed, point, normal, ground);
            }
            Num.Vector3 body = o.Position + o.Up * (0.3f * kind.Height * o.Scale);
            if (known && current.Stage > before.Stage && !current.Gone)
            {
                Cue(look.Stage, body, o.Up, ground, scale: Math.Clamp(o.Scale, 0.6f, 1.6f), stage: current.Stage);
            }
            if (current.Gone)
            {
                Cue(look.Depleted, body, o.Up, ground, scale: Math.Clamp(o.Scale, 0.6f, 1.6f));
                if (look.Depleted?.Gibs == true)
                {
                    // Its model breaks into its gibs where it stood, from where the last hit came (GROUND.md §9.4).
                    var drawn = new Transform3D(Upright(o.Up, o.Yaw).Scaled(Vector3.One * o.Scale), GroundView.ToGodot(o.Position));
                    HollowUniverse.Gibs.GibDirector.Break(kind.Key, 0, drawn, GroundView.ToGodot(-normal), Hollow.Simulation.Combat.DestroyCause.Melee,
                        GroundView.ToGodot(o.Up), Client.Zone is { } zone ? (float)(zone.SurfaceGravity * 9.80665) : 0f, o.Id);
                }
            }
            else if (look.Shake is { } shake)
            {
                Shake(o, shake, -normal);
            }
        }
        // A look that falls: the object's own plant model tips over (StartFall finds it in its scatter tile).
        if (current.Gone && !before.Gone && look.Fall is { } fall && distance < FallDistance && !_falling.Contains(o.Id))
        {
            StartFall(o, tile, -normal, fall);
            return true;
        }
        return false;
    }

    // One cue of a look: its effect (debris landing on the ground under the object) and its sound, at a point.
    private void Cue(GatherCue? cue, Num.Vector3 at, Num.Vector3 normal, float ground, float pitch = 1, float scale = 1, int stage = -1)
    {
        if (cue == null)
        {
            return;
        }
        Vector3 point = GroundView.ToGodot(at), n = GroundView.ToGodot(normal);
        if (cue.Fx != null)
        {
            FxDirector.Play(cue.Fx, point, n, new FxParams { Scale = cue.FxScale * scale, Floor = ground + 0.02f });
        }
        if (cue.Sound == null)
        {
            return;
        }
        // "{stage}" names the stage reached (an outcrop breaking up has a sound for each); made only when it happens.
        string key = stage >= 0 && cue.Sound.Contains("{stage}", StringComparison.Ordinal)
            ? cue.Sound.Replace("{stage}", stage.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            : cue.Sound;
        if (cue.SoundDelay > 0)
        {
            _pending.Add(new PendingSound(Now + cue.SoundDelay, key, point));
        }
        else
        {
            Sounds.At(key, point, 0, pitch);
        }
    }

    private void PlayPending()
    {
        double now = Now;
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            if (_pending[i].At <= now)
            {
                Sounds.At(_pending[i].Key, _pending[i].Position);
                _pending.RemoveAt(i);
            }
        }
    }

    // An object whose scatter tile is generated already (those around the player are, for the prediction's physics):
    // a frame never waits for a tile to be generated.
    private bool Cached(ulong id, out NaturalObject o)
    {
        if (Client.Survival().Nature is ScatterNaturalObjects scatter)
        {
            return scatter.TryGetCached(id, out o);
        }
        return Client.Survival().Nature!.TryGet(id, out o);
    }

    /// <summary>The surface view draws plant meshes out to about this far; beyond, only tree impostors.</summary>
    private const float NearPlants = 320;

    private readonly Dictionary<ulong, double> _nearRedraw = new();
    private readonly Dictionary<ulong, double> _blockRedraw = new();
    private int _nearBuilds;

    private static void Schedule(Dictionary<ulong, double> pending, ulong tile, double after)
    {
        double due = Now + after;
        pending[tile] = pending.TryGetValue(tile, out double was) ? Math.Min(was, due) : due;
    }

    private static bool Due(Dictionary<ulong, double> pending, double now)
    {
        foreach (double at in pending.Values)
        {
            if (at <= now)
            {
                return true;
            }
        }
        return false;
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
        double now = Now;
        if (_nearRedraw.Count > 0 && Due(_nearRedraw, now))
        {
            foreach (ulong packed in _nearRedraw.Keys)
            {
                plants.Refresh(TileKey.FromPacked(packed)!.Value, near: true, trees: false);
            }
            _nearRedraw.Clear();
            FrameEvents.Note("plants redrawn");
        }
        if (_blockRedraw.Count > 0 && Due(_blockRedraw, now))
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
        Client.Survival().Nature!.Query(tx * PhysicsWorld.TileSize, tz * PhysicsWorld.TileSize, (tx + 1) * PhysicsWorld.TileSize, (tz + 1) * PhysicsWorld.TileSize, _scratch);
        NaturalState state = Client.Survival().Natural!;
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

    /// <summary>
    /// A basis with +Y along <paramref name="up"/>, turned by <paramref name="yaw"/>: the frame models of natural objects
    /// are drawn in (<see cref="SpotFrames.ModelFrame"/>: X right, Y up, Z back), which the rules measure them in too.
    /// </summary>
    public static Basis Upright(Num.Vector3 up, float yaw)
    {
        (Num.Vector3 x, Num.Vector3 y, Num.Vector3 z) = SpotFrames.ModelFrame(new NaturalObject { Up = up, Yaw = yaw });
        return new Basis(GroundView.ToGodot(x), GroundView.ToGodot(y), GroundView.ToGodot(z));
    }

    // ---------------------------------------------------------------- weak spots

    // The weak spots near the player, drawn where the rules put them (their look's marker). The set is gathered again
    // when the natural state changes or the player has moved; the markers pulse and sparkle every frame.
    private void UpdateMarkers(Num.Vector3 feet)
    {
        NaturalState state = Client.Survival().Natural!;
        if (Num.Vector3.DistanceSquared(feet, _scannedAt) > 16 * 16)
        {
            // Every spot in the zone's changes now and then: those met on the way, which changed before they were near.
            _scannedAt = feet;
            _spotted.Clear();
            foreach (KeyValuePair<ulong, NaturalOverride> o in state.Overrides)
            {
                if (o.Value.HasWeakSpot && !o.Value.Depleted)
                {
                    _spotted.Add(o.Key);
                }
            }
            _markedRevision = uint.MaxValue;
        }
        if (state.Revision != _markedRevision || Num.Vector3.DistanceSquared(feet, _markedAt) > 0.25f)
        {
            _markedRevision = state.Revision;
            _markedAt = feet;
            _markers.Begin();
            foreach (ulong id in _spotted)
            {
                if (!Cached(id, out NaturalObject o) || Num.Vector3.DistanceSquared(o.Position, feet) > MarkerDistance * MarkerDistance ||
                    Client.Content.Natural(o.Kind) is not { Gather.Look.Marker: { } look } kind || state.WeakSpotAt(o, kind) is not { } spot)
                {
                    continue;
                }
                _markers.Show(look, GroundView.ToGodot(spot.Point), GroundView.ToGodot(spot.Normal), GroundView.ToGodot(o.Up), o.Id);
            }
        }
        _markers.Animate(Now);
    }

    // ---------------------------------------------------------------- a crown shaken by a blow

    private static readonly StringName ShakeAt = "hu_shake_at", ShakeWay = "hu_shake_dir";
    private ShakeLook? _shake;
    private double _shakeStart;
    private Vector4 _shakeAt;
    private Vector3 _shakeWay;

    // The plants' shader sways the plant standing at a point (global uniforms hu_shake_at, hu_shake_dir): the struck
    // tree's crown shakes away from the blow and settles, at no cost but a test per vertex.
    private void Shake(in NaturalObject o, ShakeLook shake, Num.Vector3 away)
    {
        Vector3 at = GroundView.ToGodot(o.Position);
        _shake = shake;
        _shakeStart = Now;
        _shakeAt = new Vector4(at.X, at.Y, at.Z, shake.Metres);
        _shakeWay = GroundView.ToGodot(away).Normalized();
    }

    private void UpdateShake()
    {
        if (_shake == null)
        {
            return;
        }
        float age = (float)(Now - _shakeStart);
        if (age > _shake.Seconds)
        {
            _shake = null;
            RenderingServer.GlobalShaderParameterSet(ShakeAt, Vector4.Zero);
            return;
        }
        RenderingServer.GlobalShaderParameterSet(ShakeAt, _shakeAt);
        RenderingServer.GlobalShaderParameterSet(ShakeWay, new Vector4(_shakeWay.X, _shakeWay.Y, _shakeWay.Z, age));
    }

    // ---------------------------------------------------------------- a tree falls

    private sealed class TreeFall
    {
        public ulong Id;
        public FallLook Look = null!;
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

    // The tree's own model (its species and variant, from its scatter item) tips over away from the blow.
    private void StartFall(in NaturalObject o, TileKey tile, Num.Vector3 away, FallLook look)
    {
        if (World.Surface is not { } surface)
        {
            return;
        }
        _falling.Add(o.Id);
        (TileKey _, int index) = ScatterNaturalObjects.Decode(o.Id)!.Value;
        away -= o.Up * Num.Vector3.Dot(away, o.Up);
        if (away.LengthSquared() < 0.01f)
        {
            away = new Num.Vector3(1, 0, 0);
        }
        Vector3 up = GroundView.ToGodot(o.Up), dir = GroundView.ToGodot(Num.Vector3.Normalize(away));
        _falls.Add(new TreeFall
        {
            Id = o.Id,
            Look = look,
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
            FallLook look = fall.Look;
            float t = (float)Math.Min(fall.Time / look.Tip, 1);
            float angle = 1.45f * t * t * t;
            float sink = fall.Time > look.Tip + look.Lie ? (float)((fall.Time - look.Tip - look.Lie) / look.Sink) : 0;
            Basis turn = new(fall.Axis, angle);
            fall.Instance!.Transform = new Transform3D(turn, fall.Base.Origin - fall.Up * (sink * 1.2f));
            if (t >= 1 && !fall.Landed)
            {
                // The trunk lands: dust and litter along it.
                fall.Landed = true;
                float half = fall.Height * 0.45f;
                if (look.Fx != null)
                {
                    FxDirector.Play(look.Fx, fall.Base.Origin + fall.Away * half, fall.Up,
                        new FxParams { Direction = fall.Away, Area = new Vector3(half, 0.2f, 0.7f) });
                }
            }
            if (fall.Time > look.Tip + look.Lie + look.Sink)
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
