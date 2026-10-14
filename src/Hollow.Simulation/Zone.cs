using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Diagnostics;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Nature;
using Hollow.Simulation.Physics;
using Hollow.Simulation.Players;
using Hollow.Simulation.Targets;
using Hollow.Surface;

namespace Hollow.Simulation;

/// <summary>A character's components: its movement state, this tick's input, its collision and what its last step did.</summary>
public struct Character
{
    public CharacterState State;

    /// <summary>The input of the coming tick (the server puts each player's here before <see cref="Zone.Step"/>).</summary>
    public MoveInput Input;

    /// <summary>The same tick's actions: what is held, attack and aim, how far back the player saw others (G2).</summary>
    public ActionInput Actions;

    public StepEvents Events;

    public CharacterBody Body;

    /// <summary>Ticks left until a dead character comes back (a movement-only zone, <see cref="AutoRespawnSystem"/>).</summary>
    public int RespawnTicks;
}

/// <summary>A display name.</summary>
public struct Named
{
    public string Name;
}

/// <summary>
/// One surface zone's simulation (GROUND.md §4.2): its entities, its Jolt world and its systems, advanced by one fixed
/// tick at a time (<see cref="CharacterMotor.TickRate"/>). The server owns the clock and calls <see cref="Step"/>;
/// nothing here reads the time. One thread at a time.
/// <para>
/// The zone is a host (GROUND_SYSTEMS.md §12.2): it keeps what every system shares (the entities, the physics, the
/// natural objects, the pose history, the clock, the content, the events) and steps its <see cref="ZoneSystem"/>s in
/// the order of its list (<see cref="GroundSystems"/>: with game content the survival game's rules; without, G1's
/// movement-only zone, where the dead come back by themselves). A system is found by its type, <see cref="Get{T}"/>,
/// or by the accessor its file gives the zone (<c>zone.Players()</c>).
/// </para>
/// </summary>
public sealed class Zone : IDisposable
{
    /// <summary>A dead character comes back at the spawn point after this long (a movement-only zone; the game's respawn is the player's choice).</summary>
    public const int RespawnDelayTicks = 3 * CharacterMotor.TickRate;

    /// <summary>Ground tiles nobody stood near for this long are dropped.</summary>
    public const int GroundKeepTicks = 10 * CharacterMotor.TickRate;

    private readonly ZoneSystem[] _systems;
    private readonly ZoneSystem[] _gated;
    private readonly ZoneSystem[] _free;
    private readonly Dictionary<Type, ZoneSystem> _byType = new();
    private long _localIds = 1;

    public Zone(ZoneSpec spec, MovementTuning? tuning = null, GameContent? content = null)
        : this(spec.CreateSurface(), spec, tuning, content)
    {
    }

    /// <summary>
    /// A zone on a given surface (tests use surfaces of their own, such as one with a sea), with its systems: by default
    /// <see cref="GroundSystems.Game"/> with content and <see cref="GroundSystems.MovementOnly"/> without.
    /// </summary>
    public Zone(ISurface surface, ZoneSpec spec, MovementTuning? tuning = null, GameContent? content = null, IReadOnlyList<ZoneSystem>? systems = null)
    {
        Spec = spec;
        Surface = surface;
        Frame = spec.CreateFrame(surface);
        Tuning = tuning ?? MovementTuning.Default;
        Movement = new MoveContext(Frame, surface.SeaRadius, MoveContext.CharacterGravity(spec.SurfaceGravity, Tuning), Tuning, surface.Radius);
        Physics = new PhysicsWorld(surface, Frame);
        Landing = Ground.On(spec.LandingDirection);
        Content = content;
        if (content != null)
        {
            Nature = Spec.CreateNature(Surface, Frame, content);
            Natural = new NaturalState(content, Nature);
            _ = new NaturalBodies(Physics, Natural);
            Climate = new StandInClimate(content.Survival, Frame, Surface.Radius);
        }
        _systems = (systems ?? (content != null ? GroundSystems.Game() : GroundSystems.MovementOnly())).ToArray();
        foreach (ZoneSystem system in _systems)
        {
            if (!_byType.TryAdd(system.GetType(), system))
            {
                throw new InvalidOperationException($"{system.GetType().Name} is in the zone's list twice (GROUND_SYSTEMS.md §12.2)");
            }
            system.Attach(this);
        }
        foreach (ZoneSystem system in _systems.OrderBy(s => s.InitOrder))
        {
            system.Init();
        }
        foreach (ZoneSystem system in _systems)
        {
            system.Start();
        }
        _gated = _systems.Where(s => s.InsidePhysicsGate).ToArray();
        _free = _systems.Where(s => !s.InsidePhysicsGate).ToArray();
    }

    public ZoneSpec Spec { get; }

    public ISurface Surface { get; }

    public ZoneFrame Frame { get; }

    /// <summary>
    /// The ground at the spec's latitude and longitude (the landing site): where <see cref="SpawnPoint"/> puts new
    /// characters when nothing better is known, and where tests lay out their places (docs/WHOLE_WORLD.md §6.7).
    /// </summary>
    public Vector3 Landing { get; }

    public MovementTuning Tuning { get; }

    public MoveContext Movement { get; }

    public PhysicsWorld Physics { get; }

    public EntityStore Entities { get; } = new();

    /// <summary>Ticks stepped so far.</summary>
    public long Tick { get; private set; }

    /// <summary>The game's numbers; null in a movement-only zone (G1).</summary>
    public GameContent? Content { get; }

    /// <summary>Where the trees, outcrops, pickups and bushes are, and what players did to them (null without content).</summary>
    public INaturalObjects? Nature { get; }

    public NaturalState? Natural { get; }

    /// <summary>The air temperature (the stand-in until the server sets the planet's climate).</summary>
    public IClimate? Climate { get; set; }

    /// <summary>The real clock (Unix ms), set by the server before every step: respawn timers and bag cooldowns.</summary>
    public long UnixMilliseconds { get; set; }

    /// <summary>Hands out IDs for new persistent entities (the server's allocator); a counter if not set.</summary>
    public Func<EntityId>? NewEntityId { get; set; }

    /// <summary>A beach point for a respawn (the server sets the planet's real coast, <see cref="Simulation.Beaches"/>); the stand-in ring if not set.</summary>
    public Func<ulong, Vector3>? Beaches { get; set; }

    /// <summary>One second of poses of every character, for hits as the attacker saw them (<see cref="HistorySystem"/> records them).</summary>
    public HitboxHistory History { get; } = new();

    /// <summary>What happened this tick, by kind, for the server, the systems and the tests to hear about (GROUND_SYSTEMS.md §12.3).</summary>
    public ZoneEvents Events { get; } = new();

    /// <summary>The systems, in the order they step.</summary>
    public IReadOnlyList<ZoneSystem> Systems => _systems;

    /// <summary>The system of a type, or null when this zone has none (GROUND_SYSTEMS.md §12.2, K4).</summary>
    public T? Get<T>() where T : ZoneSystem => _byType.TryGetValue(typeof(T), out ZoneSystem? system) ? (T)system : null;

    /// <summary>The system of a type that this zone must have.</summary>
    public T Require<T>() where T : ZoneSystem =>
        Get<T>() ?? throw new InvalidOperationException($"this zone has no {typeof(T).Name} (its list is {string.Join(", ", _systems.Select(s => s.GetType().Name))})");

    /// <summary>Adds a character at a state (a new player's spawn, or one restored from storage).</summary>
    public Entity AddCharacter(EntityId id, string name, in CharacterState state)
    {
        Entity entity = Entities.Create(id);
        // The tiles around are built in parallel, then waited for: one tick's hitch at most where nobody was.
        Physics.PrefetchWithin(state.Position, PhysicsWorld.EnsureReach);
        Physics.EnsureGround(state.Position);
        var body = new CharacterBody(Physics, Tuning, state.Position, Frame.UpAt(state.Position));
        Entities.Add(entity, new Character { State = state, Body = body });
        Entities.Add(entity, new Named { Name = name });
        return entity;
    }

    public void RemoveCharacter(Entity entity)
    {
        if (Entities.Has<Character>(entity))
        {
            Entities.Get<Character>(entity).Body.Dispose();
        }
        Get<SupportSystem>()?.SupportGone(new TargetRef(TargetFamily.Character, Entities.IdOf(entity).Value));
        History.Forget(Entities.IdOf(entity));
        Entities.Destroy(entity);
    }

    /// <summary>A fresh character standing at a point.</summary>
    public CharacterState NewCharacter(Vector3 position) => new()
    {
        Position = position,
        Health = Tuning.MaxHealth,
        Flags = MoveFlags.OnGround,
    };

    /// <summary>Moves a character to a point, at rest (admin teleport, respawn).</summary>
    public void Teleport(Entity entity, Vector3 position)
    {
        ref Character character = ref Entities.Get<Character>(entity);
        character.State.Position = position;
        character.State.Velocity = Vector3.Zero;
        Physics.PrefetchWithin(position, PhysicsWorld.EnsureReach);
        Physics.EnsureGround(position);
    }

    /// <summary>
    /// Sets a character's god mode and noclip (only the server does; its client's prediction follows them from the
    /// player's own state). Leaving noclip, the character stops where it is, and comes up onto the ground if it was
    /// under it.
    /// </summary>
    public void SetCheats(Entity entity, MoveCheats cheats)
    {
        ref Character c = ref Entities.Get<Character>(entity);
        if (c.State.Has(MoveCheats.Noclip) && (cheats & MoveCheats.Noclip) == 0)
        {
            c.State.Velocity = Vector3.Zero;
            if (HeightAboveGround(c.State.Position) < 0)
            {
                Vector3 ground = Settle(c.State.Position);
                c.State.Position = ground + Frame.UpAt(ground) * 0.05f;
            }
            Physics.PrefetchWithin(c.State.Position, PhysicsWorld.EnsureReach);
            Physics.EnsureGround(c.State.Position);
        }
        c.State.Cheats = cheats;
    }

    /// <summary>The surface's own ground, asked as a client asks it (<see cref="GroundProbe"/>).</summary>
    public GroundProbe Ground => new(Surface, Frame);

    /// <summary>The point on the ground below (or above) a point.</summary>
    public Vector3 Settle(Vector3 point) => Ground.Settle(point);

    /// <summary>
    /// The ground <paramref name="east"/> metres east and <paramref name="south"/> metres south of the landing site,
    /// along the ground (<see cref="GroundProbe.Offset"/>): where tests and tools lay out their places.
    /// </summary>
    public Vector3 GroundAt(float east, float south) => Ground.Offset(Landing, east, south);

    /// <summary>How far a point is east and south of the landing site, along the ground (<see cref="GroundAt"/>'s inverse).</summary>
    public (float East, float South) OffsetOf(Vector3 point) => Ground.OffsetOf(Landing, point);

    /// <summary>How far a point is above the ground, in metres (negative below).</summary>
    public float HeightAboveGround(Vector3 point) => Ground.HeightAbove(point);

    /// <summary>The ground's up and its slope (radians) under a point, from the surface by finite differences.</summary>
    public (Vector3 Normal, float Slope) GroundNormal(Vector3 point) => Ground.Normal(point);

    /// <summary>The eye of a character now.</summary>
    public Vector3 EyeOf(in CharacterState state) => state.Position + Frame.UpAt(state.Position) * Tuning.EyeHeight(state.Stance);

    /// <summary>
    /// Where a new character appears: on a ring of 3–8 m around the landing site, at a place fixed by
    /// <paramref name="salt"/> (a name's hash), so players arriving together don't stand inside each other.
    /// </summary>
    public Vector3 SpawnPoint(ulong salt)
    {
        var rng = new Rng(Hash64.Of(salt, 0x5BA77));
        double angle = rng.NextDouble() * DMath.TwoPi;
        double radius = rng.Range(3, 8);
        Vector3 ground = GroundAt((float)(radius * DMath.Cos(angle)), (float)(radius * DMath.Sin(angle)));
        return ground + Frame.UpAt(ground) * 0.05f;
    }

    /// <summary>A new persistent entity ID.</summary>
    public EntityId NextId() => NewEntityId?.Invoke() ?? new EntityId(0x7000_0000_0000_0000UL + (ulong)_localIds++);

    /// <summary>
    /// One tick: the ground under everyone; under the physics gate, the characters' movement and the systems that move
    /// bodies; then the other systems in their order (GROUND.md §4.2).
    /// </summary>
    public void Step()
    {
        Tick++;
        ComponentPool<Character> characters = Entities.Pool<Character>();
        // The ground under everyone first, outside Jolt's lock: a tile that has to be built or waited for then
        // doesn't hold up a client predicting in the same process (Host mode).
        using (Profile.Time(ProfileSection.CharacterGround))
        {
            foreach (ref Character character in characters.Values)
            {
                // A pilot's ground is the ship's business (G6: only near it, ShipMotor).
                if (!character.State.IsDead && character.State.Stance != Stance.Seated)
                {
                    Physics.EnsureGround(character.State.Position);
                    Physics.Prefetch(character.State.Position);
                }
            }
        }
        lock (JoltRuntime.Gate)
        {
            using (Profile.Time(ProfileSection.BroadPhase))
            {
                Physics.OptimizeBroadPhase();
            }
            TickProfile.Scope movement = Profile.Time(ProfileSection.Movement);
            Span<Character> values = characters.Values;
            for (int i = 0; i < values.Length; i++)
            {
                ref Character character = ref values[i];
                if (character.State.IsDead || character.State.Stance == Stance.Seated)
                {
                    // The dead wait for their respawn (PlayerSystem; AutoRespawnSystem without content); a seated
                    // character is carried by its ship (ShipSystem).
                    character.Events = default;
                    continue;
                }
                character.Events = CharacterMotor.Step(ref character.State, character.Input, character.Body, Movement);
            }
            movement.Dispose();
            using (Profile.Time(ProfileSection.Ships))
            {
                foreach (ZoneSystem system in _gated)
                {
                    Run(system);
                }
            }
        }
        using (Profile.Time(ProfileSection.GroundRelease))
        {
            Physics.ReleaseUnusedGround(GroundKeepTicks);
        }
        if (_free.Length > 0)
        {
            using TickProfile.Scope _ = Profile.Time(ProfileSection.Game);
            foreach (ZoneSystem system in _free)
            {
                Run(system);
            }
        }
    }

    private void Run(ZoneSystem system)
    {
        if (Tick % system.Every != 0)
        {
            return;
        }
        long started = Stopwatch.GetTimestamp();
        system.Step();
        system.Time += Stopwatch.GetElapsedTime(started);
    }

    /// <summary>Times the zone's step and its ground on the server (GROUND.md §5.11); null measures nothing.</summary>
    public TickProfile? Profile
    {
        get => Physics.Profile;
        set => Physics.Profile = value;
    }

    public void Dispose()
    {
        for (int i = _systems.Length - 1; i >= 0; i--)
        {
            _systems[i].Dispose();
        }
        ComponentPool<Character> characters = Entities.Pool<Character>();
        foreach (Character character in characters.Values)
        {
            character.Body.Dispose();
        }
        Physics.Dispose();
    }
}
