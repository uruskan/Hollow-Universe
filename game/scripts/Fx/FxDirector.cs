using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using HollowUniverse.Characters;

namespace HollowUniverse.Fx;

/// <summary>
/// Optional details of an effect (GROUND.md §9.3). Every field may be left at its default.
/// </summary>
public struct FxParams
{
    /// <summary>The way something travelled (a bullet's flight, a falling tree, a collapse's push); zero for none.</summary>
    public Vector3 Direction;

    /// <summary>Size of the whole effect (1 when 0).</summary>
    public float Scale;

    /// <summary>Half extents of a box the effect spreads over (a collapsing wall), in the effect's frame; zero for none.</summary>
    public Vector3 Area;

    /// <summary>Multiplies the layers' colours (white when its alpha is 0).</summary>
    public Color Tint;

    /// <summary>Flashes, lights and loops follow this node (a muzzle socket, a torch's flame socket, a burning body).</summary>
    public Node3D? Follow;

    /// <summary>The first-person version: flashes and flames on the view model's layer and field of view, particles moved
    /// to where the view model draws its muzzle.</summary>
    public bool FirstPerson;

    /// <summary>The height debris lands on (world Y); unset: the hit point on the ground, 1.2 m below it on a wall.</summary>
    public float? Floor;

    /// <summary>Loops: stop by themselves after this many seconds (0: run until stopped).</summary>
    public float Duration;

    /// <summary>A seed for repeatable effects (0: the director's running sequence).</summary>
    public ulong Seed;

    internal readonly float ScaleOr1 => Scale > 0 ? Scale : 1f;

    internal readonly Color TintOrWhite => Tint.A > 0 ? Tint : Colors.White;
}

/// <summary>The kinds of surface a hit can land on (GROUND.md §9.3); each has its impact effect.</summary>
public enum FxSurface
{
    Dirt,
    Grass,
    Sand,
    Rock,
    Wood,
    Metal,
    Water,
    Flesh,
}

/// <summary>
/// The effects library (GROUND.md §9.3): one node under the scene tree's root that plays every effect of
/// <c>data/ground/fx.json</c>. Call the static methods from anywhere; the director creates itself on first use.
/// <list type="bullet">
/// <item><see cref="Play"/>: a one-shot effect at a point on a surface (impacts, blood, gathering, blasts, world).</item>
/// <item><see cref="Start"/> / <see cref="Stop"/>: loops (fires, torches, burning, trails, fuses).</item>
/// <item><see cref="Muzzle"/>, <see cref="Tracer"/>, <see cref="EndTracer"/>, <see cref="Impact"/>: shots.</item>
/// <item><see cref="CameraShake"/>: the offset to add to the camera every frame; <see cref="Shaken"/> when one starts.</item>
/// </list>
/// Particles go into a dozen world batches (<see cref="FxBatch"/>), so cost grows with what is alive, not with how many
/// effects play. Each effect has a cap on how many play at once (<c>cap</c>), a distance past which it is skipped
/// (<c>cull</c>) and fewer particles with distance; a frame has a particle budget.
/// </summary>
public partial class FxDirector : Node3D
{
    /// <summary>
    /// The render layer of every effect drawn in the world (layer 20): decals leave it alone, so a scorch mark or a
    /// bullet hole never stains the dust and smoke over it. Cameras draw it by default (all twenty layers).
    /// </summary>
    public const uint RenderLayer = 1u << 19;

    private static FxDirector? _instance;

    private Dictionary<string, FxBatch> _batches = null!;
    private FxLights _lights = null!;
    private FxDecals _decals = null!;
    private FxFlashes _flashes = null!;
    private FxTracers _tracers = null!;
    private readonly FxShake _shake = new();
    private readonly FxRandom _random = new(0x5EEDF00DUL);
    private readonly Dictionary<string, Queue<double>> _playing = new(StringComparer.Ordinal);
    private readonly List<(double At, FxLayer Layer, Spot Spot)> _pending = new();
    private readonly Dictionary<int, FxLoop> _loops = new();
    private readonly List<int> _done = new();
    private readonly List<(string Key, Vector3 Position, Vector3 Normal, FxParams Params)> _early = new();
    private double _time;
    private int _nextLoop = 1;
    private int _frameParticles;
    private bool _ready;

    /// <summary>Particles a frame may emit before further particle layers are skipped.</summary>
    public int FrameBudget { get; set; } = 1500;

    /// <summary>Fewer particles with distance from the camera (off in the viewer's contact sheets).</summary>
    public bool DistanceLod { get; set; } = true;

    /// <summary>Effects past their <c>cull</c> distance are skipped (off in the viewer's contact sheets).</summary>
    public bool Culling { get; set; } = true;

    /// <summary>Effects at their <c>cap</c> are skipped (off in the viewer's contact sheets).</summary>
    public bool Capped { get; set; } = true;

    /// <summary>Tuning: when set, only particle layers of this batch are emitted.</summary>
    public string? OnlyBatch { get; set; }

    /// <summary>The camera distances and the first-person correction use (the viewport's own camera when null).</summary>
    public Camera3D? CameraOverride { get; set; }

    private Camera3D? ActiveCamera => CameraOverride ?? GetViewport()?.GetCamera3D();

    /// <summary>Freezes every effect where it is (screenshots).</summary>
    public bool Paused
    {
        get => _paused;
        set
        {
            _paused = value;
            if (_batches != null)
            {
                foreach (FxBatch b in _batches.Values)
                {
                    b.SetPaused(value);
                }
            }
        }
    }

    private bool _paused;

    /// <summary>Microseconds the director spent in its last frame: its own update plus every effect played since the frame
    /// before (emission included).</summary>
    public double LastFrameMicroseconds { get; private set; }

    /// <summary>Of <see cref="LastFrameMicroseconds"/>, the part spent playing effects.</summary>
    public double LastPlayMicroseconds { get; private set; }

    private long _playTicks;

    public int PlayedThisFrame { get; private set; }

    private int _played;

    public double Now => _time;

    public IReadOnlyDictionary<string, FxBatch> Batches => _batches;

    public int ActiveLights => _lights.Active;

    public int ActiveDecals => _decals.Active;

    public int ActiveTracers => _tracers.Active;

    public int ActiveLoops => _loops.Count;

    /// <summary>The director, created under the scene tree's root on first use.</summary>
    public static FxDirector Instance
    {
        get
        {
            if (_instance == null || !IsInstanceValid(_instance))
            {
                _instance = new FxDirector { Name = "FxDirector" };
                var tree = (SceneTree)Engine.GetMainLoop();
                tree.Root.CallDeferred(Node.MethodName.AddChild, _instance);
            }
            return _instance;
        }
    }

    /// <summary>Puts the director under <paramref name="parent"/> now (instead of on first use, a frame later).</summary>
    public static FxDirector Install(Node parent)
    {
        if (_instance != null && IsInstanceValid(_instance))
        {
            return _instance;
        }
        _instance = new FxDirector { Name = "FxDirector" };
        parent.AddChild(_instance);
        return _instance;
    }

    public override void _Ready()
    {
        ProcessPriority = 1000;
        _batches = FxBatches.Create(this);
        _lights = new FxLights(this, 24);
        _decals = new FxDecals(this);
        // Every decal texture's pool now, so the first impact of a kind doesn't add nodes in the middle of a fight.
        foreach (FxDef def in FxCatalog.All)
        {
            foreach (FxLayer layer in def.Layers)
            {
                if (layer.Type == FxLayerType.Decal)
                {
                    foreach (string tex in layer.Textures)
                    {
                        _decals.Prepare(tex);
                    }
                }
            }
        }
        _flashes = new FxFlashes(this, 24);
        _tracers = new FxTracers(this, 160);
        _ready = true;
        Paused = _paused;
        foreach ((string key, Vector3 position, Vector3 normal, FxParams p) in _early)
        {
            PlayNow(key, position, normal, p);
        }
        _early.Clear();
    }

    public override void _ExitTree()
    {
        if (_instance == this)
        {
            _instance = null;
        }
    }

    // ---------------------------------------------------------------- the API

    /// <summary>
    /// Plays a one-shot effect at <paramref name="position"/> on a surface facing <paramref name="normal"/> (up for
    /// effects in the open). Returns false when it was skipped (unknown key, too far, at its cap).
    /// </summary>
    public static bool Play(string key, Vector3 position, Vector3 normal, FxParams p = default) => Instance.PlayNow(key, position, normal, p);

    /// <summary>A bullet's impact on a surface; <paramref name="incoming"/> is the way it flew.</summary>
    public static bool Impact(FxSurface surface, Vector3 position, Vector3 normal, Vector3 incoming = default) =>
        Play("impact.bullet." + FxSurfaces.Name(surface), position, normal, new FxParams { Direction = incoming });

    /// <summary>
    /// A shot's muzzle effect on <paramref name="muzzle"/> (a gun's muzzle socket, +X along the barrel), first or third
    /// person. <paramref name="key"/> is muzzle.rifle, muzzle.rifle.brake, muzzle.pistol, muzzle.pistol.brake,
    /// muzzle.silenced or muzzle.rocket; <see cref="MuzzleKey"/> picks it from the gun and its attachments.
    /// </summary>
    public static bool Muzzle(string key, Node3D muzzle, bool firstPerson)
    {
        Transform3D t = muzzle.GlobalTransform.Orthonormalized();
        return Play(key, t.Origin, t.Basis.X, new FxParams { Follow = muzzle, FirstPerson = firstPerson, Direction = t.Basis.X });
    }

    /// <summary>The muzzle effect for a gun: silenced, braked or plain, rifle-sized or pistol-sized.</summary>
    public static string MuzzleKey(string weaponKey, bool silenced, bool brake)
    {
        if (silenced)
        {
            return "muzzle.silenced";
        }
        if (weaponKey == "rocket-launcher")
        {
            return "muzzle.rocket";
        }
        string size = weaponKey == "pistol" ? "pistol" : "rifle";
        return brake ? $"muzzle.{size}.brake" : $"muzzle.{size}";
    }

    /// <summary>Whether this shot draws a tracer: some do, as in Rust (every <c>tracerEvery</c>-th per shooter).</summary>
    public static bool WantsTracer(uint shooter, uint seq) => (seq + shooter * 7u) % (uint)FxCatalog.TracerEvery == 0;

    /// <summary>
    /// A tracer (tracer.bullet, tracer.incendiary) or trail (trail.arrow) flying the projectile's path: launched at
    /// <paramref name="origin"/> with <paramref name="velocity"/>, falling with <paramref name="gravity"/> and losing
    /// <paramref name="drag"/> of its speed a second, as ClientProjectile does. Returns an id for <see cref="EndTracer"/>.
    /// </summary>
    public static int Tracer(string key, Vector3 origin, Vector3 velocity, Vector3 gravity, float drag)
    {
        FxDirector d = Instance;
        FxDef? def = FxCatalog.Find(key);
        if (!d._ready || def?.Tracer == null)
        {
            return 0;
        }
        return d._tracers.Launch(d._time, def.Tracer, origin, velocity, gravity, drag, Colors.White);
    }

    /// <summary>The projectile of tracer <paramref name="id"/> stopped at <paramref name="at"/>.</summary>
    public static void EndTracer(int id, Vector3 at)
    {
        if (id != 0 && _instance != null && _instance._ready)
        {
            _instance._tracers.End(id, at);
        }
    }

    /// <summary>
    /// Starts a loop (fire.campfire, fire.furnace, fire.torch, fire.burning, trail.rocket, fuse.burning) at a point, or on
    /// <see cref="FxParams.Follow"/>. Returns its handle for <see cref="Stop"/> and <see cref="Move"/>.
    /// </summary>
    public static FxHandle Start(string key, Vector3 position, Vector3 normal, FxParams p = default) => Instance.StartNow(key, position, normal, p);

    /// <summary>Stops a loop: it fades out (at once with <paramref name="immediate"/>).</summary>
    public static void Stop(FxHandle handle, bool immediate = false)
    {
        if (_instance == null || !_instance._loops.TryGetValue(handle.Id, out FxLoop? loop))
        {
            return;
        }
        loop.Stopping = true;
        if (immediate)
        {
            loop.Strength = 0;
        }
    }

    /// <summary>Moves a loop that follows no node.</summary>
    public static void Move(FxHandle handle, Vector3 position)
    {
        if (_instance != null && _instance._loops.TryGetValue(handle.Id, out FxLoop? loop))
        {
            loop.Position = position;
        }
    }

    /// <summary>The camera's shake for this frame (pitch, yaw, roll in radians): add it to the camera, never to the view the
    /// player controls (like the gun's ViewKick).</summary>
    public static Vector3 CameraShake(Vector3 cameraPosition) => _instance == null ? Vector3.Zero : _instance._shake.Sample(_instance._time, cameraPosition);

    /// <summary>A shake started (a blast): position, strength, radius, duration.</summary>
    public static event Action<FxShakeEvent>? Shaken;

    /// <summary>
    /// Where a point of the first-person view model (drawn with its own, narrower field of view) appears in the world's
    /// projection: particles emitted there line up with the gun on screen.
    /// </summary>
    public static Vector3 ViewmodelToWorld(Vector3 world)
    {
        Camera3D? camera = _instance?.ActiveCamera;
        if (camera == null)
        {
            return world;
        }
        Transform3D view = camera.GlobalTransform;
        Vector3 local = view.AffineInverse() * world;
        float k = MathF.Tan(Mathf.DegToRad(camera.Fov) * 0.5f) / MathF.Tan(Mathf.DegToRad(CharacterMaterials.ViewmodelFov) * 0.5f);
        return view * new Vector3(local.X * k, local.Y * k, local.Z);
    }

    /// <summary>Removes every effect (the viewer between scenes).</summary>
    public void ClearAll()
    {
        _pending.Clear();
        foreach (FxLoop loop in _loops.Values)
        {
            FreeLoop(loop);
        }
        _loops.Clear();
        _decals.Clear();
        _tracers.Clear();
        _shake.Clear();
        _playing.Clear();
        foreach (FxBatch b in _batches.Values)
        {
            b.Node.Restart();
            b.Node.Emitting = false;
        }
    }

    /// <summary>Seeds the director's random sequence (repeatable screenshots).</summary>
    public void Reseed(ulong seed) => _random.Reseed(seed);

    // ---------------------------------------------------------------- playing

    /// <summary>Where and how an effect plays, after the parameters are applied.</summary>
    internal struct Spot
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector3 Direction;
        public float Scale;
        public Color Tint;
        public Vector3 Area;
        public Basis AreaBasis;
        public float Floor;
        public bool FirstPerson;
        public Node3D? Follow;
        public float Lod;
        public float Distance;
    }

    private bool PlayNow(string key, Vector3 position, Vector3 normal, FxParams p)
    {
        if (!_ready)
        {
            _early.Add((key, position, normal, p));
            return true;
        }
        long started = Stopwatch.GetTimestamp();
        try
        {
            return PlayTimed(key, position, normal, p);
        }
        finally
        {
            _playTicks += Stopwatch.GetTimestamp() - started;
        }
    }

    private bool PlayTimed(string key, Vector3 position, Vector3 normal, FxParams p)
    {
        FxDef? def = FxCatalog.Find(key);
        if (def == null)
        {
            GD.PushWarning($"FxDirector: no effect '{key}'.");
            return false;
        }
        if (def.IsLoop)
        {
            StartNow(key, position, normal, p);
            return true;
        }
        float distance = p.FirstPerson ? 0f : CameraDistance(position);
        if (Culling && distance > def.Cull)
        {
            return false;
        }
        if (!Admit(def))
        {
            return false;
        }
        if (p.Seed != 0)
        {
            _random.Reseed(p.Seed);
        }
        Spot spot = MakeSpot(position, normal, p, distance);
        foreach (FxLayer layer in def.Layers)
        {
            if (layer.Delay > 0)
            {
                _pending.Add((_time + layer.Delay, layer, spot));
            }
            else
            {
                EmitLayer(layer, spot);
            }
        }
        _played++;
        return true;
    }

    /// <summary>Counts the effect in against its cap.</summary>
    private bool Admit(FxDef def)
    {
        if (!_playing.TryGetValue(def.Key, out Queue<double>? ends))
        {
            ends = new Queue<double>();
            _playing[def.Key] = ends;
        }
        while (ends.Count > 0 && ends.Peek() <= _time)
        {
            ends.Dequeue();
        }
        if (Capped && ends.Count >= def.Cap)
        {
            return false;
        }
        ends.Enqueue(_time + def.Duration);
        return true;
    }

    private Spot MakeSpot(Vector3 position, Vector3 normal, FxParams p, float distance)
    {
        Vector3 n = normal.LengthSquared() > 1e-8f ? normal.Normalized() : Vector3.Up;
        Vector3 dir = p.Direction.LengthSquared() > 1e-8f ? p.Direction.Normalized() : Vector3.Zero;
        // The area's frame: X along the direction where it lies in the surface (a falling trunk, a door), Y the normal.
        (Vector3 t, Vector3 b) = FxRandom.Frame(n);
        Vector3 flat = dir - n * dir.Dot(n);
        if (flat.LengthSquared() > 1e-4f)
        {
            t = flat.Normalized();
            b = t.Cross(n);
        }
        return new Spot
        {
            Position = position,
            Normal = n,
            Direction = dir,
            Scale = p.ScaleOr1,
            Tint = p.TintOrWhite,
            Area = p.Area,
            AreaBasis = new Basis(t, n, b),
            Floor = p.Floor ?? (n.Y > 0.6f ? position.Y : position.Y - 1.2f),
            FirstPerson = p.FirstPerson,
            Follow = p.Follow,
            Lod = DistanceLod ? Lod(distance) : 1f,
            Distance = distance,
        };
    }

    private float CameraDistance(Vector3 position)
    {
        Camera3D? camera = ActiveCamera;
        return camera == null ? 0f : camera.GlobalPosition.DistanceTo(position);
    }

    /// <summary>Share of the particles drawn at a distance: all within 20 m, half by 55 m, a quarter past 110 m.</summary>
    private static float Lod(float distance) => distance <= 20f ? 1f : distance <= 55f ? 1f - 0.5f * (distance - 20f) / 35f
        : Math.Max(0.25f, 0.5f - 0.25f * (distance - 55f) / 55f);

    private void EmitLayer(FxLayer layer, in Spot spot)
    {
        switch (layer.Type)
        {
            case FxLayerType.Light:
                if (!Culling || spot.Distance < layer.Range * 10f + 20f)
                {
                    Vector3 at = spot.Position + spot.Normal * layer.Height * spot.Scale;
                    Color c = layer.Color * spot.Tint;
                    _lights.Flash(_time, at, c, layer.Energy * Mathf.Sqrt(spot.Scale), layer.Range * Mathf.Sqrt(spot.Scale), layer.Duration, layer.Attack,
                        spot.Follow);
                }
                return;
            case FxLayerType.Decal:
                if ((!Culling || spot.Distance < 70f) && layer.Textures.Length > 0)
                {
                    string tex = layer.Textures[(int)(_random.Next() * layer.Textures.Length) % layer.Textures.Length];
                    Color c = layer.Color * spot.Tint;
                    c.A = _random.Range(layer.Alpha);
                    _decals.Place(_time, tex, spot.Position, spot.Normal, _random.Range(layer.Size) * spot.Scale, layer.Depth * Math.Max(1f, spot.Scale),
                        _random.Range(layer.Life), layer.Fade, layer.Glow, _random.Range(0f, MathF.Tau), c);
                }
                return;
            case FxLayerType.Shake:
            {
                var shake = new FxShakeEvent(spot.Position, layer.Strength * spot.Scale, layer.Radius * Mathf.Sqrt(spot.Scale), layer.Duration);
                _shake.Add(_time, shake);
                Shaken?.Invoke(shake);
                return;
            }
            case FxLayerType.Muzzle:
            {
                Transform3D at = spot.Follow != null && IsInstanceValid(spot.Follow) ? spot.Follow.GlobalTransform.Orthonormalized()
                    : new Transform3D(BarrelBasis(spot.Direction.LengthSquared() > 0 ? spot.Direction : spot.Normal), spot.Position);
                _flashes.Fire(_time, layer.Style, spot.Follow, at, spot.FirstPerson, layer.Duration, spot.Scale, _random);
                return;
            }
        }
        if (!_batches.TryGetValue(layer.Batch, out FxBatch? batch))
        {
            GD.PushWarning($"FxDirector: no batch '{layer.Batch}'.");
            return;
        }
        if (OnlyBatch != null && OnlyBatch != layer.Batch)
        {
            return;
        }
        if (Culling && batch.Orient == FxOrient.Tumble && spot.Distance > 45f)
        {
            return;
        }
        float lod = spot.Lod;
        if (_frameParticles > FrameBudget)
        {
            return;
        }
        int count = _random.Count(layer.Count, lod);
        Vector3 origin = spot.FirstPerson ? ViewmodelToWorld(spot.Position) : spot.Position;
        for (int i = 0; i < count; i++)
        {
            Vector3 dir = Direction(layer, spot);
            float speed = _random.Range(layer.Speed) * Mathf.Sqrt(spot.Scale);
            Vector3 pos = origin + spot.Normal * (layer.Offset * spot.Scale);
            if (layer.Jitter > 0)
            {
                pos += _random.UnitVector() * (layer.Jitter * spot.Scale * MathF.Cbrt(_random.Next()));
            }
            if (layer.InArea && spot.Area != Vector3.Zero)
            {
                pos += spot.AreaBasis * new Vector3(_random.Range(-1f, 1f) * spot.Area.X, _random.Range(-1f, 1f) * spot.Area.Y, _random.Range(-1f, 1f) * spot.Area.Z);
            }
            float size = _random.Range(layer.Size) * spot.Scale;
            float life = _random.Range(layer.Life);
            Color c = layer.Color * spot.Tint;
            if (layer.Vary > 0)
            {
                float k = 1f + layer.Vary * (_random.Next() * 2f - 1f);
                c = new Color(c.R * k, c.G * k, c.B * k, 1f);
            }
            c.A = _random.Range(layer.Alpha);
            if (spot.FirstPerson && batch.Orient == FxOrient.Billboard)
            {
                // A shot's smoke an arm's length from the eye: smaller and fainter, or it fills the view.
                size *= 0.55f;
                c.A *= 0.7f;
            }
            float parameter = layer.Grow;
            Basis turn = Basis.Identity;
            switch (batch.Orient)
            {
                case FxOrient.Streak:
                    parameter = layer.Width * spot.Scale;
                    break;
                case FxOrient.Tumble:
                    c.A = spot.Floor;
                    turn = _random.Turn();
                    break;
            }
            batch.Emit(pos, size, dir * speed, c, life, _random.Next(), parameter, spot.Normal, turn);
        }
        _frameParticles += count;
    }

    private Vector3 Direction(FxLayer layer, in Spot spot)
    {
        Vector3 along = spot.Direction.LengthSquared() > 0 ? spot.Direction : -spot.Normal;
        switch (layer.Dir)
        {
            case FxDirection.Up:
                return _random.InCone(Vector3.Up, layer.Cone);
            case FxDirection.Down:
                return _random.InCone(Vector3.Down, layer.Cone);
            case FxDirection.Along:
                return _random.InCone(spot.Direction.LengthSquared() > 0 ? spot.Direction : spot.Normal, layer.Cone);
            case FxDirection.Back:
                return _random.InCone(spot.Direction.LengthSquared() > 0 ? -spot.Direction : spot.Normal, layer.Cone);
            case FxDirection.Reflect:
            {
                Vector3 r = along - 2f * along.Dot(spot.Normal) * spot.Normal;
                return _random.InCone(r.LengthSquared() > 1e-8f ? r : spot.Normal, layer.Cone);
            }
            case FxDirection.Sphere:
                return _random.UnitVector();
            case FxDirection.Ring:
            {
                float a = _random.Range(0f, MathF.Tau);
                float e = Mathf.DegToRad(_random.Range(0f, layer.Cone));
                Vector3 flat = spot.AreaBasis.X * MathF.Cos(a) + spot.AreaBasis.Z * MathF.Sin(a);
                return (flat * MathF.Cos(e) + spot.Normal * MathF.Sin(e)).Normalized();
            }
            default:
                return _random.InCone(spot.Normal, layer.Cone);
        }
    }

    private static Basis BarrelBasis(Vector3 forward)
    {
        Vector3 x = forward.Normalized();
        Vector3 z = Mathf.Abs(x.Y) < 0.95f ? x.Cross(Vector3.Up).Normalized() : x.Cross(Vector3.Right).Normalized();
        return new Basis(x, z.Cross(x), z);
    }

    // ---------------------------------------------------------------- loops

    private FxHandle StartNow(string key, Vector3 position, Vector3 normal, FxParams p)
    {
        if (!_ready)
        {
            _early.Add((key, position, normal, p));
            return default;
        }
        FxDef? def = FxCatalog.Find(key);
        if (def?.Loop == null)
        {
            GD.PushWarning($"FxDirector: no loop '{key}'.");
            return default;
        }
        var loop = new FxLoop
        {
            Id = _nextLoop++,
            Def = def,
            Params = p,
            Position = position,
            Normal = normal,
            Started = _time,
            StopAt = p.Duration > 0 ? _time + p.Duration : double.MaxValue,
            Accumulators = new float[def.Loop.Emit.Count],
            FlickerPhase = _random.Range(0f, 100f),
        };
        if (def.Loop.Flames.Count > 0)
        {
            loop.Flames = new MeshInstance3D
            {
                Name = $"Flames{loop.Id}",
                Mesh = FxFlameMeshes.For(def),
                MaterialOverride = FxFlameMeshes.Material(p.FirstPerson),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Layers = p.FirstPerson ? ViewmodelView.RenderLayer : RenderLayer,
                ExtraCullMargin = 3f,
            };
            loop.Flames.SetInstanceShaderParameter("fx_brightness", def.Loop.FlameBrightness);
            loop.Flames.SetInstanceShaderParameter("fx_speed", def.Loop.FlameSpeed);
            loop.Flames.SetInstanceShaderParameter("fx_strength", 0f);
            AddChild(loop.Flames);
        }
        if (def.Loop.LightEnergy > 0)
        {
            loop.Light = new OmniLight3D
            {
                Name = $"LoopLight{loop.Id}",
                LightColor = def.Loop.LightColor,
                OmniRange = def.Loop.LightRange,
                LightEnergy = 0,
                ShadowEnabled = def.Loop.LightShadows,
                OmniAttenuation = 1.4f,
                DistanceFadeEnabled = true,
                DistanceFadeBegin = 70f,
                DistanceFadeLength = 30f,
            };
            AddChild(loop.Light);
        }
        _loops[loop.Id] = loop;
        UpdateLoop(loop, 0f);
        return new FxHandle(loop.Id);
    }

    private void UpdateLoop(FxLoop loop, float dt)
    {
        FxLoopDef def = loop.Def.Loop!;
        if (_time >= loop.StopAt || (loop.Params.Follow != null && !IsInstanceValid(loop.Params.Follow)))
        {
            loop.Stopping = true;
        }
        float target = loop.Stopping ? 0f : 1f;
        float rate = loop.Stopping ? 1f / Math.Max(0.05f, def.FadeOut) : 1f / Math.Max(0.05f, def.FadeIn);
        loop.Strength = Mathf.MoveToward(loop.Strength, target, rate * dt);
        Transform3D frame = loop.Frame();
        if (loop.Flames != null)
        {
            loop.Flames.GlobalTransform = frame;
            loop.Flames.SetInstanceShaderParameter("fx_strength", loop.Strength);
        }
        if (loop.Light != null)
        {
            float t = (float)_time * 9f + loop.FlickerPhase;
            float flicker = 1f + def.LightFlicker * (0.55f * MathF.Sin(t) + 0.3f * MathF.Sin(t * 2.7f + 1.3f) + 0.15f * MathF.Sin(t * 6.1f + 0.4f));
            loop.Light.LightEnergy = def.LightEnergy * loop.Strength * flicker;
            Vector3 jitter = new Vector3(MathF.Sin(t * 1.3f), MathF.Sin(t * 1.7f + 2f), MathF.Sin(t * 1.1f + 4f)) * (0.02f * def.LightFlicker);
            loop.Light.GlobalPosition = frame * (def.LightOffset + jitter);
        }
        if (dt <= 0 || loop.Strength <= 0.01f)
        {
            return;
        }
        float distance = loop.Params.FirstPerson ? 0f : CameraDistance(frame.Origin);
        if (Culling && distance > loop.Def.Cull)
        {
            return;
        }
        float lod = DistanceLod ? Lod(distance) : 1f;
        for (int i = 0; i < def.Emit.Count; i++)
        {
            (FxLayer layer, float perSecond, Vector3 offset) = def.Emit[i];
            loop.Accumulators[i] += perSecond * dt * loop.Strength * lod;
            int n = (int)loop.Accumulators[i];
            if (n <= 0)
            {
                continue;
            }
            loop.Accumulators[i] -= n;
            Vector3 at = frame * offset;
            var spot = new Spot
            {
                Position = at,
                Normal = Vector3.Up,
                Direction = loop.Params.Direction,
                Scale = loop.Params.ScaleOr1,
                Tint = loop.Params.TintOrWhite,
                AreaBasis = new Basis(Vector3.Right, Vector3.Up, Vector3.Back),
                Floor = at.Y - 1f,
                FirstPerson = loop.Params.FirstPerson,
                Lod = 1f,
                Distance = distance,
            };
            FxLayer once = layer;
            for (int k = 0; k < n; k++)
            {
                EmitLoopParticle(once, spot);
            }
        }
    }

    /// <summary>One particle of a loop's emitter (the layer's count is ignored: the rate decides).</summary>
    private void EmitLoopParticle(FxLayer layer, in Spot spot)
    {
        if (!_batches.TryGetValue(layer.Batch, out FxBatch? batch))
        {
            return;
        }
        Vector3 origin = spot.FirstPerson ? ViewmodelToWorld(spot.Position) : spot.Position;
        Vector3 dir = Direction(layer, spot);
        Vector3 pos = origin;
        if (layer.Jitter > 0)
        {
            pos += _random.UnitVector() * (layer.Jitter * spot.Scale * MathF.Cbrt(_random.Next()));
        }
        Color c = layer.Color * spot.Tint;
        if (layer.Vary > 0)
        {
            float k = 1f + layer.Vary * (_random.Next() * 2f - 1f);
            c = new Color(c.R * k, c.G * k, c.B * k, 1f);
        }
        c.A = _random.Range(layer.Alpha);
        float parameter = batch.Orient == FxOrient.Streak ? layer.Width * spot.Scale : layer.Grow;
        batch.Emit(pos, _random.Range(layer.Size) * spot.Scale, dir * _random.Range(layer.Speed), c, _random.Range(layer.Life), _random.Next(),
            parameter, spot.Normal, Basis.Identity);
        _frameParticles++;
    }

    private void FreeLoop(FxLoop loop)
    {
        loop.Flames?.QueueFree();
        loop.Light?.QueueFree();
    }

    // ---------------------------------------------------------------- the frame

    public override void _Process(double delta)
    {
        if (!_ready)
        {
            return;
        }
        long started = Stopwatch.GetTimestamp();
        float dt = _paused ? 0f : (float)delta;
        _time += dt;
        foreach (FxBatch b in _batches.Values)
        {
            b.EmittedThisFrame = 0;
        }
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            if (_pending[i].At <= _time)
            {
                (double _, FxLayer layer, Spot spot) = _pending[i];
                _pending.RemoveAt(i);
                if (spot.Follow != null && !IsInstanceValid(spot.Follow))
                {
                    spot.Follow = null;
                }
                EmitLayer(layer, spot);
            }
        }
        _done.Clear();
        foreach (FxLoop loop in _loops.Values)
        {
            UpdateLoop(loop, dt);
            if (loop.Stopping && loop.Strength <= 0f)
            {
                _done.Add(loop.Id);
            }
        }
        foreach (int id in _done)
        {
            FreeLoop(_loops[id]);
            _loops.Remove(id);
        }
        _flashes.Update(_time);
        _lights.Update(_time);
        _decals.Update(_time);
        _tracers.Update(_time);
        PlayedThisFrame = _played;
        _played = 0;
        _frameParticles = 0;
        LastPlayMicroseconds = _playTicks * 1e6 / Stopwatch.Frequency;
        LastFrameMicroseconds = (Stopwatch.GetTimestamp() - started + _playTicks) * 1e6 / Stopwatch.Frequency;
        _playTicks = 0;
    }
}

/// <summary>Surfaces by name and from what the game knows of a hit (GROUND.md §9.3).</summary>
public static class FxSurfaces
{
    public static string Name(FxSurface s) => s switch
    {
        FxSurface.Grass => "grass",
        FxSurface.Sand => "sand",
        FxSurface.Rock => "rock",
        FxSurface.Wood => "wood",
        FxSurface.Metal => "metal",
        FxSurface.Water => "water",
        FxSurface.Flesh => "flesh",
        _ => "dirt",
    };

    /// <summary>The surface of a ground material (Hollow.Surface's eight layers).</summary>
    public static FxSurface Of(Hollow.Surface.GroundMaterial m) => m switch
    {
        Hollow.Surface.GroundMaterial.Grass or Hollow.Surface.GroundMaterial.Moss => FxSurface.Grass,
        Hollow.Surface.GroundMaterial.Rock or Hollow.Surface.GroundMaterial.Gravel => FxSurface.Rock,
        Hollow.Surface.GroundMaterial.Sand or Hollow.Surface.GroundMaterial.Snow => FxSurface.Sand,
        _ => FxSurface.Dirt,
    };

    /// <summary>The surface of a building grade (building.json: twig, wood, stone, metal, armoured).</summary>
    public static FxSurface OfGrade(string grade) => grade switch
    {
        "stone" => FxSurface.Rock,
        "metal" or "armoured" => FxSurface.Metal,
        _ => FxSurface.Wood,
    };
}
