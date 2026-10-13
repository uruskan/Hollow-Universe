using System;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Physics;
using Hollow.Simulation.Players;
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

    /// <summary>Ticks left until a dead character comes back.</summary>
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
/// With <see cref="GameContent"/> (G2) the zone also runs the survival game after movement, in GROUND.md §4.2's
/// order: actions (swings, shots, crafting), projectiles and damage, survival, devices, timers (Zone.*.cs).
/// Without it, it is G1's movement-only zone, where the dead come back by themselves.
/// </para>
/// </summary>
public sealed partial class Zone : IDisposable
{
    /// <summary>A dead character comes back at the spawn point after this long (death and respawn proper are G2).</summary>
    public const int RespawnDelayTicks = 3 * CharacterMotor.TickRate;

    /// <summary>Ground tiles nobody stood near for this long are dropped.</summary>
    public const int GroundKeepTicks = 10 * CharacterMotor.TickRate;

    public Zone(ZoneSpec spec, MovementTuning? tuning = null, GameContent? content = null)
        : this(spec.CreateSurface(), spec, tuning, content)
    {
    }

    /// <summary>A zone on a given surface (tests use surfaces of their own, such as one with a sea).</summary>
    public Zone(ISurface surface, ZoneSpec spec, MovementTuning? tuning = null, GameContent? content = null)
    {
        Spec = spec;
        Surface = surface;
        Frame = spec.CreateFrame(surface);
        Tuning = tuning ?? MovementTuning.Default;
        Movement = new MoveContext(Frame, surface.SeaRadius, MoveContext.CharacterGravity(spec.SurfaceGravity, Tuning), Tuning);
        Physics = new PhysicsWorld(surface, Frame);
        if (content != null)
        {
            InitGame(content);
        }
    }

    public ZoneSpec Spec { get; }

    public ISurface Surface { get; }

    public ZoneFrame Frame { get; }

    public MovementTuning Tuning { get; }

    public MoveContext Movement { get; }

    public PhysicsWorld Physics { get; }

    public EntityStore Entities { get; } = new();

    /// <summary>Ticks stepped so far.</summary>
    public long Tick { get; private set; }

    /// <summary>Adds a character at a state (a new player's spawn, or one restored from storage).</summary>
    public Entity AddCharacter(EntityId id, string name, in CharacterState state)
    {
        Entity entity = Entities.Create(id);
        // The tiles around are built in parallel, then waited for: one tick's hitch at most where nobody was.
        Physics.Prefetch(state.Position, 1);
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
        Physics.Prefetch(position, 1);
        Physics.EnsureGround(position);
    }

    /// <summary>The ground under a point of the zone's tangent plane, in the zone frame.</summary>
    public Vector3 GroundAt(float x, float z)
    {
        Vector3D direction = Frame.ToBody(x, 0, z).Normalized;
        return Frame.ToLocal(direction * (Surface.Radius + Surface.HeightAt(direction)));
    }

    /// <summary>
    /// Where a new character appears: on a ring of 3–8 m around the zone's origin, at a place fixed by
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

    /// <summary>One tick: every character moves with its input; the dead count down to respawning.</summary>
    public void Step()
    {
        Tick++;
        ComponentPool<Character> characters = Entities.Pool<Character>();
        // The ground under everyone first, outside Jolt's lock: a tile that has to be built or waited for then
        // doesn't hold up a client predicting in the same process (Host mode).
        foreach (ref Character character in characters.Values)
        {
            if (!character.State.IsDead)
            {
                Physics.EnsureGround(character.State.Position);
                Physics.Prefetch(character.State.Position);
            }
        }
        lock (JoltRuntime.Gate)
        {
            Physics.OptimizeBroadPhase();
            Span<Character> values = characters.Values;
            ReadOnlySpan<int> slots = characters.Slots;
            for (int i = 0; i < values.Length; i++)
            {
                ref Character character = ref values[i];
                if (character.State.IsDead)
                {
                    if (Content != null)
                    {
                        // G2: the dead wait for a respawn of their choice (Zone.Players.cs).
                        character.Events = default;
                        continue;
                    }
                    if (character.RespawnTicks <= 0)
                    {
                        character.RespawnTicks = RespawnDelayTicks;
                    }
                    else if (--character.RespawnTicks == 0)
                    {
                        character.State = NewCharacter(SpawnPoint(Entities.IdOf(Entities.AtSlot(slots[i])).Value + (ulong)Tick));
                    }
                    character.Events = default;
                    continue;
                }
                character.Events = CharacterMotor.Step(ref character.State, character.Input, character.Body, Movement);
            }
        }
        Physics.ReleaseUnusedGround(GroundKeepTicks);
        if (Content != null)
        {
            StepGame();
        }
    }

    public void Dispose()
    {
        ComponentPool<Character> characters = Entities.Pool<Character>();
        foreach (Character character in characters.Values)
        {
            character.Body.Dispose();
        }
        Physics.Dispose();
    }
}
