using System;
using System.Collections.Generic;
using Godot;
using Hollow.Client.Audio;

namespace HollowUniverse.Audio;

/// <summary>A playing sound (one event; a layered event's layers share it). Default: nothing.</summary>
public readonly record struct SoundHandle(int Id)
{
    public static readonly SoundHandle None = new(0);

    public bool IsValid => Id != 0;
}

/// <summary>
/// Plays the catalogue's sounds (GROUND.md §4.9). One node in the scene (the ground mode adds it); every other script
/// goes through <see cref="Instance"/>.
/// <list type="bullet">
/// <item>Pools of <see cref="AudioStreamPlayer3D"/> and <see cref="AudioStreamPlayer"/> voices: nothing is created
/// while playing.</item>
/// <item>Voice limits: each event at most its <c>limit</c> at once (the oldest is cut); when the pool is full a sound
/// takes the voice of the lowest priority (then the oldest), or isn't played if all are more important.</item>
/// <item>Variants in shuffled rounds (<see cref="VariantBag"/>), random pitch and volume per play.</item>
/// <item>Distance layers for gunshots and explosions: each layer weighted by the distance to the listener
/// (<see cref="DistanceLayers"/>) and started after the time sound takes to get there.</item>
/// <item>Occlusion: a voice whose line from the listener is blocked by the world (collision layer
/// <see cref="OcclusionMask"/>) plays on its bus's low-passed "Occluded" child; loops look again every 0.3 s.</item>
/// <item>Out of earshot (past an event's <c>maxDistance</c>) nothing starts at all.</item>
/// </list>
/// </summary>
public partial class AudioDirector : Node
{
    public static AudioDirector? Instance { get; private set; }

    /// <summary>Voices placed in the world, and flat (interface, ambience beds, the player's own feet).</summary>
    public int Voices3D { get; init; } = 56;
    public int Voices2D { get; init; } = 20;

    /// <summary>Where the ears are; by default the viewport's current camera.</summary>
    public Node3D? Listener { get; set; }

    /// <summary>Physics layers that muffle sound passing through them.</summary>
    public uint OcclusionMask { get; set; } = 1;

    /// <summary>Replaces the physics ray (listener, source) → blocked; for views without collision.</summary>
    public Func<Vector3, Vector3, bool>? OcclusionTest { get; set; }

    public bool Occlusion { get; set; } = true;

    /// <summary>Raised for each voice started: event key, layer name (or ""), distance, dB, pitch (the viewer shows them).</summary>
    public event Action<string, string, float, float, float>? Started;

    private sealed class Voice
    {
        public AudioStreamPlayer3D? P3;
        public AudioStreamPlayer? P2;
        public int Handle;
        public string Key = "";
        public string Layer = "";
        public string Bus = "";
        public int Priority;
        public double StartedAt;
        public bool Active;
        public bool Playing;
        public bool Loop;
        public double Delay;
        public Node3D? Follow;
        public Vector3 Position;
        public float BaseDb;
        public float GainDb;
        public float TargetDb;
        public float FadeRate;
        public bool Stopping;
        public bool Occludable;
        public bool Occluded;
        public double NextOcclusion;

        public void Apply()
        {
            float db = BaseDb + GainDb;
            if (P3 != null)
            {
                P3.VolumeDb = db;
            }
            if (P2 != null)
            {
                P2.VolumeDb = db;
            }
        }
    }

    private readonly List<Voice> _voices3D = new();
    private readonly List<Voice> _voices2D = new();
    private readonly Dictionary<string, VariantBag> _bags = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _lastPlayed = new(StringComparer.Ordinal);
    private readonly HashSet<string> _warned = new(StringComparer.Ordinal);
    private Random _rng = new();
    private double _time;
    private int _nextHandle = 1;

    /// <summary>Makes the random choices repeatable (variants, pitch, jitter).</summary>
    public void Seed(int seed) => _rng = new Random(seed);

    public override void _EnterTree()
    {
        Instance ??= this;
    }

    public override void _ExitTree()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public override void _Ready()
    {
        AudioBuses.Ensure();
        for (int i = 0; i < Voices3D; i++)
        {
            var p = new AudioStreamPlayer3D
            {
                Name = "Voice3D" + i,
                AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance,
                MaxDb = 3f,
                PanningStrength = 1f,
                DopplerTracking = AudioStreamPlayer3D.DopplerTrackingEnum.Disabled,
            };
            AddChild(p);
            _voices3D.Add(new Voice { P3 = p });
        }
        for (int i = 0; i < Voices2D; i++)
        {
            var p = new AudioStreamPlayer { Name = "Voice2D" + i };
            AddChild(p);
            _voices2D.Add(new Voice { P2 = p });
        }
    }

    // ---------------------------------------------------------------- playing

    /// <summary>
    /// Plays an event at a point in the world (render space); a flat event (interface, ambience) plays flat.
    /// <paramref name="volumeDb"/> adds to the event's volume; <paramref name="pitch"/> multiplies its random pitch.
    /// </summary>
    public SoundHandle Play(string key, Vector3 position, float volumeDb = 0f, float pitch = 1f)
    {
        SoundEventData? e = Event(key);
        if (e == null || CoolingDown(e))
        {
            return SoundHandle.None;
        }
        if (!e.Positional)
        {
            return Start2D(e, volumeDb, pitch, loop: false);
        }
        Vector3 ear = ListenerPosition();
        float distance = ear.DistanceTo(position);
        float shotPitch = pitch * RandomPitch(e);
        var handle = new SoundHandle(_nextHandle++);
        bool any = false;
        if (e.Layered)
        {
            for (int i = 0; i < e.Layers.Count; i++)
            {
                SoundLayer layer = e.Layers[i];
                double w = DistanceLayers.Weight(distance, layer.From, layer.To, layer.Fade);
                if (w < 0.01 || distance > layer.MaxDistance)
                {
                    continue;
                }
                float db = (float)(e.Volume + layer.Volume + DistanceLayers.Db(w) + Jitter(e)) + volumeDb;
                any |= Start3D(e, handle, layer.Name, layer.Files, i, position, null, db, shotPitch, (float)layer.UnitSize,
                    (float)layer.MaxDistance, e.Delay ? DistanceLayers.Delay(distance) : 0, distance, airAbsorption: false);
            }
        }
        else if (distance <= e.MaxDistance)
        {
            float db = (float)(e.Volume + Jitter(e)) + volumeDb;
            any = Start3D(e, handle, "", e.Files, 0, position, null, db, shotPitch, (float)e.UnitSize,
                (float)e.MaxDistance, e.Delay ? DistanceLayers.Delay(distance) : 0, distance, airAbsorption: true);
        }
        return any ? handle : SoundHandle.None;
    }

    /// <summary>Plays an event flat (no position): the interface, the player's own body.</summary>
    public SoundHandle Play2D(string key, float volumeDb = 0f, float pitch = 1f)
    {
        SoundEventData? e = Event(key);
        if (e == null || CoolingDown(e))
        {
            return SoundHandle.None;
        }
        return Start2D(e, volumeDb, pitch * RandomPitch(e), loop: false);
    }

    /// <summary>Loops an event on a node (a campfire, a fuse) until <see cref="Stop"/> or the node is freed.</summary>
    public SoundHandle Loop(string key, Node3D node, float volumeDb = 0f)
    {
        SoundEventData? e = Event(key);
        if (e == null)
        {
            return SoundHandle.None;
        }
        if (!e.Positional)
        {
            return Start2D(e, volumeDb, 1f, loop: true);
        }
        var handle = new SoundHandle(_nextHandle++);
        Vector3 at = node.GlobalPosition;
        float distance = ListenerPosition().DistanceTo(at);
        bool ok = Start3D(e, handle, "", e.Files, 0, at, node, (float)e.Volume + volumeDb, 1f, (float)e.UnitSize,
            (float)e.MaxDistance, 0, distance, airAbsorption: true, loop: true);
        return ok ? handle : SoundHandle.None;
    }

    /// <summary>Loops a flat event (an ambience bed) until <see cref="Stop"/>.</summary>
    public SoundHandle Loop2D(string key, float volumeDb = 0f)
    {
        SoundEventData? e = Event(key);
        return e == null ? SoundHandle.None : Start2D(e, volumeDb, 1f, loop: true);
    }

    /// <summary>Fades a sound out over <paramref name="fade"/> seconds and frees its voices.</summary>
    public void Stop(SoundHandle handle, float fade = 0.08f)
    {
        if (!handle.IsValid)
        {
            return;
        }
        foreach (Voice v in AllVoices())
        {
            if (v.Active && v.Handle == handle.Id)
            {
                v.Stopping = true;
                v.TargetDb = -80f;
                v.FadeRate = fade <= 0 ? 1e6f : (v.GainDb + 80f) / fade;
            }
        }
    }

    /// <summary>Moves a playing sound's extra gain to <paramref name="db"/> over <paramref name="fade"/> seconds.</summary>
    public void SetGain(SoundHandle handle, float db, float fade = 0.5f)
    {
        foreach (Voice v in AllVoices())
        {
            if (v.Active && v.Handle == handle.Id && !v.Stopping)
            {
                v.TargetDb = db;
                v.FadeRate = fade <= 0 ? 1e6f : Math.Max(1f, Math.Abs(db - v.GainDb) / fade);
            }
        }
    }

    public bool IsPlaying(SoundHandle handle)
    {
        foreach (Voice v in AllVoices())
        {
            if (v.Active && v.Handle == handle.Id)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Voices in use, placed and flat.</summary>
    public (int Placed, int Flat) Busy
    {
        get
        {
            int a = 0, b = 0;
            foreach (Voice v in _voices3D)
            {
                a += v.Active ? 1 : 0;
            }
            foreach (Voice v in _voices2D)
            {
                b += v.Active ? 1 : 0;
            }
            return (a, b);
        }
    }

    /// <summary>Stops everything at once.</summary>
    public void StopAll()
    {
        foreach (Voice v in AllVoices())
        {
            Free(v);
        }
    }

    // ---------------------------------------------------------------- every frame

    public override void _Process(double delta)
    {
        _time += delta;
        Vector3 ear = ListenerPosition();
        foreach (Voice v in AllVoices())
        {
            if (!v.Active)
            {
                continue;
            }
            if (v.Follow != null)
            {
                if (!IsInstanceValid(v.Follow) || !v.Follow.IsInsideTree())
                {
                    Free(v);
                    continue;
                }
                v.Position = v.Follow.GlobalPosition;
                if (v.P3 != null)
                {
                    v.P3.GlobalPosition = v.Position;
                }
            }
            if (!v.Playing)
            {
                v.Delay -= delta;
                if (v.Delay <= 0)
                {
                    StartVoice(v, ear);
                }
                continue;
            }
            if (v.GainDb != v.TargetDb)
            {
                float step = v.FadeRate * (float)delta;
                v.GainDb = v.GainDb < v.TargetDb ? Math.Min(v.TargetDb, v.GainDb + step) : Math.Max(v.TargetDb, v.GainDb - step);
                v.Apply();
            }
            if (v.Stopping && v.GainDb <= -79.9f)
            {
                Free(v);
                continue;
            }
            bool playing = v.P3?.Playing ?? v.P2?.Playing ?? false;
            if (!playing && !v.Loop)
            {
                Free(v);
                continue;
            }
            if (v.Loop && v.Occludable && v.P3 != null && _time >= v.NextOcclusion)
            {
                v.NextOcclusion = _time + 0.3;
                bool occluded = Blocked(ear, v.Position);
                if (occluded != v.Occluded)
                {
                    v.Occluded = occluded;
                    v.P3.Bus = AudioBuses.For(v.Bus, occluded);
                }
            }
        }
    }

    // ---------------------------------------------------------------- voices

    private bool Start3D(SoundEventData e, SoundHandle handle, string layer, IReadOnlyList<string> files, int layerIndex,
        Vector3 position, Node3D? follow, float db, float pitch, float unitSize, float maxDistance, double delay,
        float distance, bool airAbsorption, bool loop = false)
    {
        AudioStream? stream = PickStream(e, layerIndex, files);
        if (stream == null)
        {
            return false;
        }
        Voice? v = Allocate(_voices3D, e);
        if (v == null)
        {
            return false;
        }
        AudioStreamPlayer3D p = v.P3!;
        p.Stream = stream;
        p.UnitSize = unitSize;
        p.MaxDistance = maxDistance;
        p.PitchScale = Math.Max(0.05f, pitch);
        // Air absorbs high frequencies with distance; layered sounds have it in their files already.
        p.AttenuationFilterCutoffHz = airAbsorption ? 7000f : 20500f;
        p.AttenuationFilterDb = airAbsorption ? -12f : 0f;
        p.GlobalPosition = position;
        Fill(v, e, handle, layer, db, delay, loop);
        v.Follow = follow;
        v.Position = position;
        Started?.Invoke(e.Key, layer, distance, db, pitch);
        if (delay <= 0)
        {
            StartVoice(v, ListenerPosition());
        }
        return true;
    }

    private SoundHandle Start2D(SoundEventData e, float volumeDb, float pitch, bool loop)
    {
        AudioStream? stream = PickStream(e, 0, e.Files);
        if (stream == null)
        {
            return SoundHandle.None;
        }
        Voice? v = Allocate(_voices2D, e);
        if (v == null)
        {
            return SoundHandle.None;
        }
        var handle = new SoundHandle(_nextHandle++);
        AudioStreamPlayer p = v.P2!;
        p.Stream = stream;
        p.PitchScale = Math.Max(0.05f, pitch);
        float db = (float)(e.Volume + Jitter(e)) + volumeDb;
        Fill(v, e, handle, "", db, 0, loop || e.Loop);
        v.Occludable = false;
        Started?.Invoke(e.Key, "", 0f, db, pitch);
        StartVoice(v, Vector3.Zero);
        return handle;
    }

    private void Fill(Voice v, SoundEventData e, SoundHandle handle, string layer, float db, double delay, bool loop)
    {
        v.Active = true;
        v.Playing = false;
        v.Handle = handle.Id;
        v.Key = e.Key;
        v.Layer = layer;
        v.Bus = e.Bus;
        v.Priority = e.Priority;
        v.StartedAt = _time;
        v.Loop = loop || e.Loop;
        v.Delay = delay;
        v.BaseDb = db;
        v.GainDb = 0f;
        v.TargetDb = 0f;
        v.FadeRate = 0f;
        v.Stopping = false;
        v.Occludable = e.Occlude && Occlusion;
        v.Occluded = false;
        v.NextOcclusion = _time + 0.3;
        v.Follow = null;
        _lastPlayed[e.Key] = _time;
    }

    private void StartVoice(Voice v, Vector3 ear)
    {
        v.Playing = true;
        if (v.P3 != null)
        {
            v.Occluded = v.Occludable && Blocked(ear, v.Position);
            v.P3.Bus = AudioBuses.For(v.Bus, v.Occluded);
            v.Apply();
            v.P3.Play();
        }
        else if (v.P2 != null)
        {
            v.P2.Bus = v.Bus;
            v.Apply();
            v.P2.Play();
        }
    }

    /// <summary>A free voice for an event, after its own limit and the pool's priorities.</summary>
    private Voice? Allocate(List<Voice> pool, SoundEventData e)
    {
        int same = 0;
        Voice? oldestSame = null;
        foreach (Voice v in pool)
        {
            if (v.Active && v.Key == e.Key && !v.Stopping)
            {
                same++;
                if (oldestSame == null || v.StartedAt < oldestSame.StartedAt)
                {
                    oldestSame = v;
                }
            }
        }
        if (same >= e.Limit && oldestSame != null)
        {
            if (oldestSame.Loop)
            {
                return null;
            }
            Free(oldestSame);
            return oldestSame;
        }
        Voice? victim = null;
        foreach (Voice v in pool)
        {
            if (!v.Active)
            {
                return v;
            }
            if (victim == null || v.Priority < victim.Priority || (v.Priority == victim.Priority && v.StartedAt < victim.StartedAt))
            {
                victim = v;
            }
        }
        if (victim == null || victim.Priority > e.Priority)
        {
            return null;
        }
        Free(victim);
        return victim;
    }

    private void Free(Voice v)
    {
        v.P3?.Stop();
        v.P2?.Stop();
        v.Active = false;
        v.Playing = false;
        v.Follow = null;
        v.Handle = 0;
        v.Key = "";
    }

    private IEnumerable<Voice> AllVoices()
    {
        foreach (Voice v in _voices3D)
        {
            yield return v;
        }
        foreach (Voice v in _voices2D)
        {
            yield return v;
        }
    }

    // ---------------------------------------------------------------- helpers

    private SoundEventData? Event(string key)
    {
        SoundEventData? e = SoundLibrary.Find(key);
        if (e == null && _warned.Add(key))
        {
            GD.PushWarning($"No sound event '{key}' in {SoundLibrary.DataPath}.");
        }
        return e;
    }

    private bool CoolingDown(SoundEventData e) =>
        e.Cooldown > 0 && _lastPlayed.TryGetValue(e.Key, out double last) && _time - last < e.Cooldown;

    private AudioStream? PickStream(SoundEventData e, int layer, IReadOnlyList<string> files)
    {
        if (files.Count == 0)
        {
            return null;
        }
        string bagKey = e.Key + "#" + layer;
        if (!_bags.TryGetValue(bagKey, out VariantBag? bag))
        {
            bag = new VariantBag(files.Count);
            _bags[bagKey] = bag;
        }
        return SoundLibrary.Stream(files[bag.Next(_rng)], e.Loop);
    }

    private float RandomPitch(SoundEventData e) => (float)(e.PitchMin + (e.PitchMax - e.PitchMin) * _rng.NextDouble());

    private double Jitter(SoundEventData e) => e.Jitter <= 0 ? 0 : (_rng.NextDouble() * 2 - 1) * e.Jitter;

    public Vector3 ListenerPosition()
    {
        if (Listener != null && IsInstanceValid(Listener))
        {
            return Listener.GlobalPosition;
        }
        Camera3D? camera = GetViewport()?.GetCamera3D();
        return camera?.GlobalPosition ?? Vector3.Zero;
    }

    /// <summary>Whether something solid stands between the listener and a source (the last 0.4 m of the line ignored,
    /// so the source's own body doesn't count).</summary>
    public bool Blocked(Vector3 ear, Vector3 source)
    {
        if (!Occlusion)
        {
            return false;
        }
        if (OcclusionTest != null)
        {
            return OcclusionTest(ear, source);
        }
        Vector3 d = source - ear;
        float length = d.Length();
        if (length < 0.8f)
        {
            return false;
        }
        World3D? world = GetViewport()?.World3D;
        if (world == null)
        {
            return false;
        }
        var query = PhysicsRayQueryParameters3D.Create(ear, ear + d * ((length - 0.4f) / length), OcclusionMask);
        query.CollideWithAreas = false;
        Godot.Collections.Dictionary hit = world.DirectSpaceState.IntersectRay(query);
        return hit.Count > 0;
    }
}
