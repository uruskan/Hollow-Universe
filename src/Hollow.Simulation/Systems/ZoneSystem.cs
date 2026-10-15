using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Nature;
using Hollow.Simulation.Physics;
using Hollow.Simulation.Players;
using Hollow.Surface;

namespace Hollow.Simulation;

/// <summary>
/// One concern of a zone's rules (GROUND_SYSTEMS.md §12.2, K1): its own state, stepped by the host in the host's order.
/// What it shares with the other systems is the host's (K2: the entities, the physics, the natural objects, the pose
/// history, the clock, the content, the events); what it needs of another system it finds once, with
/// <see cref="Zone.Require{T}"/>, and keeps. What it holds is its own: nothing outside reads it.
/// </summary>
public abstract class ZoneSystem : IDisposable
{
    private Zone? _zone;

    /// <summary>The zone this system runs in (set by the host before <see cref="Init"/>).</summary>
    public Zone Zone => _zone ?? throw new InvalidOperationException($"{GetType().Name} is not in a zone yet");

    /// <summary>Ticks between its steps: 1, or <see cref="CharacterMotor.TickRate"/> for the once-a-second systems.</summary>
    public virtual int Every => 1;

    /// <summary>
    /// When its <see cref="Init"/> runs among the systems: lower first, so a world (the things, the building) is made
    /// before what registers into it or keeps it. The step order is the list's, not this.
    /// </summary>
    public virtual int InitOrder => 100;

    /// <summary>Whether its step moves physics bodies: then the host runs it under the physics gate, after the characters' movement.</summary>
    public virtual bool InsidePhysicsGate => false;

    /// <summary>Time spent in its steps so far (the host measures it; GROUND_SYSTEMS.md §2, principle 6).</summary>
    public TimeSpan Time { get; internal set; }

    internal void Attach(Zone zone) => _zone = zone;

    /// <summary>Once, after every system is in the zone: find what this one needs, make its registries.</summary>
    protected internal virtual void Init()
    {
    }

    /// <summary>After every system's <see cref="Init"/>: seal what other systems filled in theirs.</summary>
    protected internal virtual void Start()
    {
    }

    /// <summary>Its tick, every <see cref="Every"/> ticks, in the host's order.</summary>
    protected internal virtual void Step()
    {
    }

    public virtual void Dispose()
    {
    }

    // ---------------------------------------------------------------- what every system shares (K2), by the names the rules use

    protected ZoneSpec Spec => Zone.Spec;

    protected ISurface Surface => Zone.Surface;

    protected ZoneFrame Frame => Zone.Frame;

    protected MovementTuning Tuning => Zone.Tuning;

    protected MoveContext Movement => Zone.Movement;

    protected PhysicsWorld Physics => Zone.Physics;

    protected EntityStore Entities => Zone.Entities;

    protected long Tick => Zone.Tick;

    protected long UnixMilliseconds => Zone.UnixMilliseconds;

    /// <summary>
    /// The game's numbers: never null for a system of the game, which is only in a zone that has them (K5). The ship
    /// system, which also runs in G1's movement-only zone, checks it where it needs it.
    /// </summary>
    protected GameContent Content => Zone.Content!;

    protected INaturalObjects Nature => Zone.Nature!;

    protected NaturalState Natural => Zone.Natural!;

    protected IClimate? Climate => Zone.Climate;

    protected Func<EntityId>? NewEntityId => Zone.NewEntityId;

    protected Func<ulong, Vector3>? Beaches => Zone.Beaches;

    protected HitboxHistory History => Zone.History;

    protected ZoneEvents Events => Zone.Events;

    protected EntityId NextId() => Zone.NextId();

    protected Vector3 GroundAt(float x, float z) => Zone.GroundAt(x, z);

    protected GroundProbe Ground => Zone.Ground;

    protected Vector3 Settle(Vector3 point) => Zone.Settle(point);

    protected float HeightAboveGround(Vector3 point) => Zone.HeightAboveGround(point);

    protected (Vector3 Normal, float Slope) GroundNormal(Vector3 point) => Zone.GroundNormal(point);

    protected Vector3 EyeOf(in CharacterState state) => Zone.EyeOf(state);

    /// <summary>A character's feet, up, velocity and view in the zone's frame (aboard a ship, where it carries them: IRON_WAKE.md §4.2).</summary>
    protected Vector3 WorldOf(in CharacterState state) => Zone.WorldOf(state);

    protected Vector3 UpOf(in CharacterState state) => Zone.UpOf(state);

    protected Vector3 VelocityOf(in CharacterState state) => Zone.VelocityOf(state);

    protected Vector3 ViewOf(in Character c) => Zone.ViewOf(c);

    protected CharacterState NewCharacter(Vector3 position) => Zone.NewCharacter(position);

    protected Vector3 SpawnPoint(ulong salt) => Zone.SpawnPoint(salt);

    protected Entity AddCharacter(EntityId id, string name, in CharacterState state) => Zone.AddCharacter(id, name, state);

    protected void RemoveCharacter(Entity entity) => Zone.RemoveCharacter(entity);

    protected void Teleport(Entity entity, Vector3 position) => Zone.Teleport(entity, position);

    protected Diagnostics.TickProfile? Profile => Zone.Profile;

    protected const int RespawnDelayTicks = Zone.RespawnDelayTicks;

    protected const int GroundKeepTicks = Zone.GroundKeepTicks;
}
