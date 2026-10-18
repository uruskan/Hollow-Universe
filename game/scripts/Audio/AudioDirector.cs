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
/// <item>Voice limits: audibility and priority choose voices within the pool and event limits. Loops keep their
/// logical handles while virtual and automatically resume when audible and a voice becomes available.</item>
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
    private static readonly List<AudioDirector> Directors = new();

    /// <summary>Voices placed in the world, and flat (interface, ambience beds, the player's own feet).</summary>
    private int? _voices3DOverride, _voices2DOverride;
    public int Voices3D { get => _voices3DOverride ?? Budget.SpatialVoices; init => _voices3DOverride = Math.Clamp(value, 0, 512); }
    public int Voices2D { get => _voices2DOverride ?? Budget.FlatVoices; init => _voices2DOverride = Math.Clamp(value, 0, 512); }
    public AudioVoiceBudget Budget { get; init; } = SoundLibrary.VoiceBudget;

    /// <summary>Optional catalogue adapter for isolated native checks; ordinary scenes use SoundLibrary.</summary>
    public Func<string, SoundEventData?>? EventLookup { get; init; }

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
        public float UnitSize;
        public float MaxDistance;
        public int Limit;
        public float Pitch;

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

    private sealed class LogicalLoop
    {
        public required SoundHandle Handle;
        public required SoundEventData Event;
        public required SoundLoopEnvelope Envelope;
        public required string File;
        public float BaseDb;
        public bool Positional;
        public Node3D? Follow;
        public ulong EmitterId;
        public Vector3 Position;
        public Voice? Voice;
        public double RetryAt;
    }

    private readonly List<Voice> _voices3D = new();
    private readonly List<Voice> _voices2D = new();
    private readonly Dictionary<string, VariantBag> _bags = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _lastPlayed = new(StringComparer.Ordinal);
    private readonly HashSet<string> _warned = new(StringComparer.Ordinal);
    private readonly Dictionary<int, LogicalLoop> _loops = new();
    private readonly Dictionary<(ulong Emitter, string Event), int> _emitterLoops = new();
    private readonly List<int> _retiredLoops = new();
    private readonly SoundVoiceScheduler _scheduler = new();
    private double _scheduleIn;
    private bool _loopsDirty;
    private Random _rng = new();
    private double _time;
    private int _nextHandle = 1;

    /// <summary>Makes the random choices repeatable (variants, pitch, jitter).</summary>
    public void Seed(int seed) => _rng = new Random(seed);

    public override void _EnterTree()
    {
        Directors.Add(this);
        Instance ??= this;
    }

    public override void _ExitTree()
    {
        StopAll();
        foreach (Voice v in AllVoices())
        {
            if (v.P3 != null)
            {
                v.P3.Stream = null;
            }
            if (v.P2 != null)
            {
                v.P2.Stream = null;
            }
        }
        Directors.Remove(this);
        if (Instance == this) Instance = Directors.Count > 0 ? Directors[0] : null;
        if (Directors.Count == 0) SoundLibrary.Release();
    }

    public override void _Ready()
    {
        Budget.Validate();
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
        if (e.Loop) return CreateLoop(e, null, position, e.Positional, volumeDb, 0, pitch);
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
        if (e.Loop) return CreateLoop(e, null, Vector3.Zero, false, volumeDb, 0, pitch);
        return Start2D(e, volumeDb, pitch * RandomPitch(e), loop: false);
    }

    /// <summary>
    /// Loops an event on a node (a campfire, a fuse) until <see cref="Stop"/> or the node is freed. <paramref name="startGainDb"/>
    /// is the extra gain it starts at (−80 to fade it in with <see cref="SetGain"/>: a crossfaded layer, IRON_WAKE.md §10).
    /// </summary>
    public SoundHandle Loop(string key, Node3D node, float volumeDb = 0f, float startGainDb = 0f)
    {
        SoundEventData? e = Event(key);
        return e == null || !IsInstanceValid(node) || !node.IsInsideTree() ? SoundHandle.None
            : CreateLoop(e, node, node.GlobalPosition, e.Positional, volumeDb, startGainDb, 1);
    }

    /// <summary>
    /// Loops a flat event (an ambience bed) until <see cref="Stop"/>. <paramref name="startGainDb"/> is the extra gain it
    /// starts at (−80 to fade it in with <see cref="SetGain"/>).
    /// </summary>
    public SoundHandle Loop2D(string key, float volumeDb = 0f, float startGainDb = 0f)
    {
        SoundEventData? e = Event(key);
        return e == null ? SoundHandle.None : CreateLoop(e, null, Vector3.Zero, false, volumeDb, startGainDb, 1);
    }

    private SoundHandle CreateLoop(SoundEventData e, Node3D? follow, Vector3 position, bool positional, float db, float gain, float pitch)
    {
        if (e.Files.Count == 0) return SoundHandle.None;
        ulong emitter = follow?.GetInstanceId() ?? 0;
        if (emitter != 0 && _emitterLoops.TryGetValue((emitter, e.Key), out int existing) &&
            _loops.TryGetValue(existing, out LogicalLoop? active) && !active.Envelope.Stopping) return active.Handle;
        var handle = new SoundHandle(_nextHandle++);
        var loop = new LogicalLoop
        {
            Handle = handle, Event = e, Envelope = new SoundLoopEnvelope(gain, pitch),
            File = PickFile(e, 0, e.Files), BaseDb = (float)e.Volume + db, Positional = positional,
            Follow = follow, EmitterId = emitter, Position = position,
        };
        _loops.Add(handle.Id, loop);
        if (emitter != 0) _emitterLoops[(emitter, e.Key)] = handle.Id;
        _loopsDirty = true;
        return handle;
    }

    /// <summary>Fades a sound out over <paramref name="fade"/> seconds and frees its voices.</summary>
    public void Stop(SoundHandle handle, float fade = 0.08f)
    {
        if (!handle.IsValid)
        {
            return;
        }
        if (_loops.TryGetValue(handle.Id, out LogicalLoop? loop))
        {
            loop.Envelope.Stop(fade);
            if (loop.Envelope.Finished) RetireLoop(loop);
            _loopsDirty = true;
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
        if (_loops.TryGetValue(handle.Id, out LogicalLoop? loop))
        {
            loop.Envelope.SetGain(db, fade);
            return;
        }
        foreach (Voice v in AllVoices())
        {
            if (v.Active && v.Handle == handle.Id && !v.Stopping)
            {
                v.TargetDb = db;
                v.FadeRate = fade <= 0 ? 1e6f : Math.Max(1f, Math.Abs(db - v.GainDb) / fade);
            }
        }
    }

    /// <summary>The volume a sound plays at now (its event and caller dB plus its gain), or null when it isn't playing.</summary>
    public float? VolumeOf(SoundHandle handle)
    {
        if (_loops.TryGetValue(handle.Id, out LogicalLoop? loop)) return loop.BaseDb + loop.Envelope.GainDb;
        foreach (Voice v in AllVoices())
        {
            if (v.Active && v.Handle == handle.Id)
            {
                return v.BaseDb + v.GainDb;
            }
        }
        return null;
    }

    /// <summary>True for a retained loop, including a virtual one. Use HasVoice to inspect physical playback.</summary>
    public bool IsPlaying(SoundHandle handle)
    {
        if (_loops.ContainsKey(handle.Id)) return true;
        return HasVoice(handle);
    }

    public bool HasVoice(SoundHandle handle)
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

    /// <summary>Changes playback rate, including the playhead of virtual loops; 1 is the original rate.</summary>
    public void SetPitch(SoundHandle handle, float pitch)
    {
        if (_loops.TryGetValue(handle.Id, out LogicalLoop? loop)) loop.Envelope.SetPitch(pitch);
        float value = float.IsFinite(pitch) ? Math.Clamp(pitch, 0.05f, 8f) : 1;
        foreach (Voice v in AllVoices())
        {
            if (!v.Active || v.Handle != handle.Id) continue;
            v.Pitch = value;
            if (v.P3 != null) v.P3.PitchScale = value;
            if (v.P2 != null) v.P2.PitchScale = value;
        }
    }

    public (int Logical, int Physical, int Virtual) Loops
    {
        get
        {
            int physical = 0;
            foreach (LogicalLoop loop in _loops.Values) if (loop.Voice != null) physical++;
            return (_loops.Count, physical, _loops.Count - physical);
        }
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
        _loops.Clear(); _emitterLoops.Clear(); _retiredLoops.Clear();
        _loopsDirty = false;
    }

    // ---------------------------------------------------------------- every frame

    public override void _Process(double delta)
    {
        _time += delta;
        Vector3 ear = ListenerPosition();
        UpdateLoops(delta);
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
            if (!v.Loop && v.GainDb != v.TargetDb)
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
            if (!playing)
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
        _scheduleIn -= delta;
        if (_scheduleIn <= 0 || _loopsDirty)
        {
            _scheduleIn = 1 / Budget.UpdateHz;
            _loopsDirty = false;
            ReconcileLoops(_voices3D, true, ear);
            ReconcileLoops(_voices2D, false, ear);
        }
    }

    private void UpdateLoops(double delta)
    {
        _retiredLoops.Clear();
        foreach (LogicalLoop loop in _loops.Values)
        {
            if (loop.Follow != null)
            {
                if (!IsInstanceValid(loop.Follow) || !loop.Follow.IsInsideTree())
                {
                    _retiredLoops.Add(loop.Handle.Id);
                    continue;
                }
                loop.Position = loop.Follow.GlobalPosition;
            }
            loop.Envelope.Advance(delta);
            if (loop.Envelope.Finished)
            {
                _retiredLoops.Add(loop.Handle.Id);
                continue;
            }
            if (loop.Voice is { } v)
            {
                v.Position = loop.Position;
                v.GainDb = loop.Envelope.GainDb;
                v.Pitch = loop.Envelope.Pitch;
                if (v.P3 != null) { v.P3.GlobalPosition = loop.Position; v.P3.PitchScale = v.Pitch; }
                if (v.P2 != null) v.P2.PitchScale = v.Pitch;
                v.Apply();
            }
        }
        foreach (int id in _retiredLoops) RetireLoop(_loops[id]);
    }

    private void RetireLoop(LogicalLoop loop)
    {
        if (loop.Voice is { } voice) Free(voice);
        _loops.Remove(loop.Handle.Id);
        if (loop.EmitterId != 0 && _emitterLoops.TryGetValue((loop.EmitterId, loop.Event.Key), out int id) && id == loop.Handle.Id)
            _emitterLoops.Remove((loop.EmitterId, loop.Event.Key));
        _loopsDirty = true;
    }

    private static int CandidateId(Voice voice, int index) => voice.Loop ? voice.Handle : -index - 1;

    private SoundVoiceCandidate Candidate(Voice voice, int index, Vector3 ear)
    {
        double distance = voice.P3 == null ? 0 : ear.DistanceTo(voice.Position);
        double db = SoundVoiceScheduler.AudibleDb(voice.BaseDb + voice.GainDb, distance, voice.UnitSize, voice.MaxDistance, voice.P3 != null);
        return new SoundVoiceCandidate(CandidateId(voice, index), voice.Key, voice.Limit, voice.Priority, db, voice.Loop, voice.Handle);
    }

    private IEnumerable<SoundVoiceCandidate> Candidates(List<Voice> pool, bool positional, Vector3 ear, bool includeVirtual)
    {
        for (int i = 0; i < pool.Count; i++)
        {
            Voice v = pool[i];
            if (v.Active && (!includeVirtual || !v.Loop)) yield return Candidate(v, i, ear);
        }
        if (!includeVirtual) yield break;
        foreach (LogicalLoop loop in _loops.Values)
        {
            if (loop.Positional != positional || loop.RetryAt > _time) continue;
            double distance = positional ? ear.DistanceTo(loop.Position) : 0;
            double db = SoundVoiceScheduler.AudibleDb(loop.BaseDb + loop.Envelope.GainDb, distance,
                loop.Event.UnitSize, loop.Event.MaxDistance, positional);
            yield return new SoundVoiceCandidate(loop.Handle.Id, loop.Event.Key, loop.Event.Limit, loop.Event.Priority,
                db, loop.Voice != null, loop.Handle.Id);
        }
    }

    private void ReconcileLoops(List<Voice> pool, bool positional, Vector3 ear)
    {
        _scheduler.Select(Candidates(pool, positional, ear, includeVirtual: true), pool.Count, Budget);
        for (int i = 0; i < pool.Count; i++)
        {
            Voice v = pool[i];
            if (v.Active && !_scheduler.Selected(CandidateId(v, i))) Free(v);
        }
        foreach (LogicalLoop loop in _loops.Values)
        {
            if (loop.Positional != positional || loop.Voice != null || !_scheduler.Selected(loop.Handle.Id)) continue;
            Voice? available = pool.Find(v => !v.Active);
            if (available == null) break;
            ActivateLoop(loop, available, ear);
        }
    }

    private void ActivateLoop(LogicalLoop loop, Voice voice, Vector3 ear)
    {
        AudioStream? stream = SoundLibrary.Stream(loop.File, loop: true);
        if (stream == null)
        {
            loop.RetryAt = _time + 5; // Missing content cannot cause a per-frame load/retry storm.
            return;
        }
        SoundEventData e = loop.Event;
        Fill(voice, e, loop.Handle, "", loop.BaseDb, 0, loop: true);
        voice.GainDb = loop.Envelope.GainDb;
        voice.Pitch = loop.Envelope.Pitch;
        voice.Position = loop.Position;
        voice.Follow = loop.Follow;
        if (voice.P3 is { } p3)
        {
            p3.Stream = stream;
            p3.UnitSize = voice.UnitSize;
            p3.MaxDistance = voice.MaxDistance;
            p3.GlobalPosition = loop.Position;
            p3.PitchScale = voice.Pitch;
            p3.AttenuationFilterCutoffHz = 7000;
            p3.AttenuationFilterDb = -12;
        }
        else if (voice.P2 is { } p2)
        {
            p2.Stream = stream;
            p2.PitchScale = voice.Pitch;
            voice.Occludable = false;
        }
        loop.Voice = voice;
        double length = stream.GetLength();
        float cursor = length > 0 ? (float)(loop.Envelope.Cursor % length) : 0;
        Started?.Invoke(e.Key, "", loop.Positional ? ear.DistanceTo(loop.Position) : 0, voice.BaseDb + voice.GainDb, voice.Pitch);
        StartVoice(voice, ear, cursor);
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
        Voice? v = Allocate(_voices3D, e, position, db, unitSize, maxDistance);
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
        v.UnitSize = unitSize;
        v.MaxDistance = maxDistance;
        v.Pitch = pitch;
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
        float db = (float)(e.Volume + Jitter(e)) + volumeDb;
        Voice? v = Allocate(_voices2D, e, Vector3.Zero, db, 1, 1);
        if (v == null)
        {
            return SoundHandle.None;
        }
        var handle = new SoundHandle(_nextHandle++);
        AudioStreamPlayer p = v.P2!;
        p.Stream = stream;
        p.PitchScale = Math.Max(0.05f, pitch);
        Fill(v, e, handle, "", db, 0, loop || e.Loop);
        v.Pitch = pitch;
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
        v.Limit = e.Limit;
        v.UnitSize = (float)e.UnitSize;
        v.MaxDistance = (float)e.MaxDistance;
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

    private void StartVoice(Voice v, Vector3 ear, float cursor = 0)
    {
        v.Playing = true;
        if (v.P3 != null)
        {
            v.Occluded = v.Occludable && Blocked(ear, v.Position);
            v.P3.Bus = AudioBuses.For(v.Bus, v.Occluded);
            v.Apply();
            v.P3.Play(cursor);
        }
        else if (v.P2 != null)
        {
            v.P2.Bus = v.Bus;
            v.Apply();
            v.P2.Play(cursor);
        }
    }

    /// <summary>One-shot admission uses the same audibility, event limits and ranking as persistent loops.</summary>
    private Voice? Allocate(List<Voice> pool, SoundEventData e, Vector3 position, float db, float unitSize, float maxDistance)
    {
        bool positional = ReferenceEquals(pool, _voices3D);
        Vector3 ear = ListenerPosition();
        double audible = SoundVoiceScheduler.AudibleDb(db, positional ? ear.DistanceTo(position) : 0, unitSize, maxDistance, positional);
        var incoming = new SoundVoiceCandidate(int.MinValue, e.Key, e.Limit, e.Priority, audible, false, _nextHandle);
        _scheduler.Select(WithIncoming(), pool.Count, Budget);
        if (!_scheduler.Selected(int.MinValue)) return null;
        Voice? available = null;
        for (int i = 0; i < pool.Count; i++)
        {
            Voice v = pool[i];
            if (v.Active && !_scheduler.Selected(CandidateId(v, i))) Free(v);
            if (!v.Active) available ??= v;
        }
        return available;

        IEnumerable<SoundVoiceCandidate> WithIncoming()
        {
            foreach (SoundVoiceCandidate candidate in Candidates(pool, positional, ear, includeVirtual: false)) yield return candidate;
            yield return incoming;
        }
    }

    private void Free(Voice v)
    {
        if (v.Loop && _loops.TryGetValue(v.Handle, out LogicalLoop? loop))
        {
            loop.Voice = null;
            _loopsDirty = true;
        }
        v.P3?.Stop();
        v.P2?.Stop();
        if (v.P3 != null) v.P3.Stream = null;
        if (v.P2 != null) v.P2.Stream = null;
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
        SoundEventData? e = EventLookup != null ? EventLookup(key) : SoundLibrary.Find(key);
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
        return SoundLibrary.Stream(PickFile(e, layer, files), e.Loop);
    }

    private string PickFile(SoundEventData e, int layer, IReadOnlyList<string> files)
    {
        string bagKey = e.Key + "#" + layer;
        if (!_bags.TryGetValue(bagKey, out VariantBag? bag))
        {
            bag = new VariantBag(files.Count);
            _bags[bagKey] = bag;
        }
        return files[bag.Next(_rng)];
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
