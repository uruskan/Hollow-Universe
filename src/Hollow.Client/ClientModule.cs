using System;
using System.Collections.Generic;
using Hollow.Net.Protocol;
using Hollow.Net.Replication;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Physics;
using Hollow.Surface;

namespace Hollow.Client;

/// <summary>
/// One concern of the client (GROUND_SYSTEMS.md §12.5, K11): its own state, its message handlers, and hooks the host
/// (<see cref="GameClient"/>) calls in a fixed order. What it shares with the other modules is the host's (the
/// connection, the clock, the player's own prediction, the remote entities, the content); what it needs of another
/// module it finds once, with <see cref="GameClient.Require{T}"/>, and keeps. What it holds is its own: the
/// presentation reads it through the module's accessor (<c>client.Raid().Explosives</c>), never through the host.
/// </summary>
public abstract class ClientModule : IDisposable
{
    private GameClient? _client;

    /// <summary>The client this module runs in (set by the host before <see cref="Init"/>).</summary>
    public GameClient Client => _client ?? throw new InvalidOperationException($"{GetType().Name} is not in a client yet");

    internal void Attach(GameClient client) => _client = client;

    /// <summary>
    /// Once, when the client is made: registers this module's message handlers, one per type (GROUND_SYSTEMS.md §12.4);
    /// the content and the local player exist, the zone doesn't yet.
    /// </summary>
    protected internal virtual void Init(MessageHandlers<GameClient> handlers)
    {
    }

    /// <summary>
    /// After the server's Welcome set the zone, the surface, the frame and the predictor: makes what depends on them.
    /// The host called <see cref="Dispose"/> first, so what the last zone's Welcome made is gone.
    /// </summary>
    protected internal virtual void Welcome()
    {
    }

    /// <summary>An entity the server told this client about (a character, an animal, a ship).</summary>
    protected internal virtual void Spawned(in Spawn spawn)
    {
    }

    /// <summary>An entity the server took away, by its net ID.</summary>
    protected internal virtual void Despawned(uint netId)
    {
    }

    /// <summary>
    /// Whether this module has the player flying something this tick (a ship's helm, G6): its controls ride in the tick's
    /// input.
    /// </summary>
    protected internal virtual bool Flies => false;

    /// <summary>Each tick, after its input was recorded and before the character is predicted (a ship is predicted first).</summary>
    protected internal virtual void BeforePredict(uint tick)
    {
    }

    /// <summary>
    /// A snapshot arrived: its remote entities were added and the clock steered; the player's own character is
    /// reconciled next. <paramref name="header"/>.OwnTick is 0 while the character isn't placed, and
    /// <see cref="Prediction"/> is null before the Welcome.
    /// </summary>
    protected internal virtual void AfterSnapshot(in SnapshotHeader header)
    {
    }

    /// <summary>Every frame the remote clock runs, after the remote entities were sampled at <paramref name="renderTick"/>.</summary>
    protected internal virtual void Sample(double renderTick)
    {
    }

    /// <summary>Every frame, before the player's own character is placed for drawing.</summary>
    protected internal virtual void Render(double delta)
    {
    }

    /// <summary>Every frame while playing, after the ticks ran and the remote entities were sampled, in the list's order.</summary>
    protected internal virtual void Update(double delta)
    {
    }

    /// <summary>Lets go of what <see cref="Welcome"/> made: before another Welcome (a new zone), and when the client is disposed.</summary>
    public virtual void Dispose()
    {
    }

    // ---------------------------------------------------------------- what every module shares, by the names the moved code uses

    protected GameContent Content => Client.Content;

    protected LocalPlayer Local => Client.Local;

    protected ClientState State => Client.State;

    protected ClientStats Stats => Client.Stats;

    protected ZoneSpec? Zone => Client.Zone;

    protected ISurface? Surface => Client.Surface;

    protected ZoneFrame Frame => Client.Frame;

    protected PhysicsWorld? Physics => Client.Physics;

    protected Predictor? Prediction => Client.Prediction;

    protected uint NetId => Client.NetId;

    protected EntityId EntityId => Client.EntityId;

    protected bool HasCharacter => Client.HasCharacter;

    protected CharacterState Character => Client.Character;

    protected float EyeHeight => Client.EyeHeight;

    protected double ClockSeconds => Client.ClockSeconds;

    protected double ServerTickNow => Client.ServerTickNow;

    protected double TickClock => Client.TickClock;

    protected uint NextTick => Client.NextTick;

    /// <summary>The remote entities by net ID (characters and animals; ships are a module's).</summary>
    protected Dictionary<uint, RemoteEntity> Remotes => Client.Remotes;

    /// <summary>The remote entity of a net ID, made if it is new.</summary>
    protected RemoteEntity Remote(uint netId) => Client.Remote(netId);

    /// <summary>The ships in the newest snapshot (its ship section, GROUND.md §4.4).</summary>
    protected List<(uint NetId, ShipNetState State)> DecodedShips => Client.DecodedShips;

    /// <summary>Sends a message in order; false, and nothing sent, while not playing.</summary>
    protected bool SendReliable<T>(in T message) where T : struct, INetMessage<T> => Client.SendReliable(message);

    /// <summary>Sends a message reliably but unordered (shots and reloads); false, and nothing sent, while not playing.</summary>
    protected bool SendUnordered<T>(in T message) where T : struct, INetMessage<T> => Client.SendUnordered(message);
}
