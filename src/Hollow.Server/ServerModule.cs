using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Net.Bits;
using Hollow.Net.Protocol;
using Hollow.Net.Replication;
using Hollow.Persistence;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Diagnostics;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Surface;
using LiteNetLib;

namespace Hollow.Server;

/// <summary>
/// One concern of the server (GROUND_SYSTEMS.md §12.5, K10): its own state, its message handlers and event
/// subscriptions, and the hooks the host (<see cref="GameServer"/>) calls in one fixed order, named for what they do.
/// What it shares with the other modules is the host's (the zone, the content, the sessions, the writer, the sends,
/// the intents' budget); what it keeps of a player goes in a typed slot of the session (<see cref="PlayerSession.State{T}"/>);
/// what it holds is its own, and nothing outside reads it but through its accessor (<c>server.Raid()</c>).
/// </summary>
public abstract class ServerModule : IDisposable
{
    private GameServer? _server;

    /// <summary>The server this module runs in (set by the host before <see cref="Init"/>).</summary>
    public GameServer Server => _server ?? throw new InvalidOperationException($"{GetType().Name} is not in a server yet");

    internal void Attach(GameServer server) => _server = server;

    // ---------------------------------------------------------------- the hooks, in the order the host calls them

    /// <summary>Once, after every module is in the server: register this module's messages (one handler per type) and its event subscriptions.</summary>
    protected internal virtual void Init(MessageHandlers<PlayerSession> handlers)
    {
    }

    /// <summary>
    /// The stored zone, loaded at start (with a database only): the records this module owns come back into the zone;
    /// the ID of every thing it restores, or fails to, goes into <paramref name="saved"/>, so that a later save says it
    /// is gone when it is.
    /// </summary>
    protected internal virtual void Restore(ZoneRecord stored, HashSet<EntityId> saved)
    {
    }

    /// <summary>After the world is loaded: what runs from now on (the services, the world's props, the populations).</summary>
    protected internal virtual void Start()
    {
    }

    /// <summary>A player is in the world, just after their Welcome; <paramref name="newCharacter"/> on a first life.</summary>
    protected internal virtual void Joined(PlayerSession session, bool newCharacter)
    {
    }

    /// <summary>A player left (their character stays, asleep).</summary>
    protected internal virtual void Left(PlayerSession session)
    {
    }

    /// <summary>Each tick, after the logins and before the inputs are applied: what waited for this tick.</summary>
    protected internal virtual void BeforeStep()
    {
    }

    /// <summary>A playing player's input for this tick, as it is applied to their character.</summary>
    protected internal virtual void Input(PlayerSession session, in InputCommand input)
    {
    }

    /// <summary>Each tick, after the zone's step: what the step changed, to whom it concerns; the intents are answered after every module's.</summary>
    protected internal virtual void AfterStep()
    {
    }

    /// <summary>Each snapshot, before the players are served: this module's entities placed, its net IDs given.</summary>
    protected internal virtual void Prepare()
    {
    }

    /// <summary>The Spawn of a dynamic entity this module presented (<see cref="Present"/>), when it comes into a player's range.</summary>
    protected internal virtual void Describe(uint netId, ref Spawn spawn)
    {
    }

    /// <summary>Whether a dynamic entity this module presented need not go in this player's snapshot (it rests, and they have it).</summary>
    protected internal virtual bool AtRest(PlayerSession session, uint netId, in CharacterNetState state) => false;

    /// <summary>A character in a player's range, before its state is offered to their snapshot; <paramref name="arrived"/> when it just came into range.</summary>
    protected internal virtual void Seen(PlayerSession session, uint netId, Entity entity, bool arrived)
    {
    }

    /// <summary>
    /// The snapshot about to go to a player, after the dynamic entities' spawns and despawns: this module's part of it
    /// (the header, the ships offered), and what must arrive before it.
    /// </summary>
    protected internal virtual void Snapshot(PlayerSession session, Vector3 centre, ref SnapshotHeader header, List<ShipCandidate> ships)
    {
    }

    /// <summary>Each snapshot, per player, after their snapshot went: this module's replication by relevance.</summary>
    protected internal virtual void Replicate(PlayerSession session, Vector3 centre)
    {
    }

    /// <summary>Each snapshot, after every player was served.</summary>
    protected internal virtual void Finish()
    {
    }

    /// <summary>
    /// The zone is being saved: the records this module owns go into the save; the ID of every thing it writes into
    /// <paramref name="current"/>, so the host can say which are gone since the last save.
    /// </summary>
    protected internal virtual void Save(ZoneSave save, HashSet<EntityId> current)
    {
    }

    public virtual void Dispose()
    {
    }

    // ---------------------------------------------------------------- what every module shares, by the names the code uses

    protected Zone Zone => Server.Zone;

    protected ZoneSpec Spec => Server.Spec;

    /// <summary>The game's data, as every client must have it too.</summary>
    protected GameContent Content => Server.Content;

    protected ServerOptions Options => Server.Options;

    /// <summary>The database worker; null without persistence.</summary>
    protected PersistenceWorker? Persistence => Server.Persistence;

    /// <summary>The zone's sky, whose spin is the zone's clock; null on the stand-in ground.</summary>
    protected SkyEphemeris? Sky => Server.Sky;

    /// <summary>The one writer every message is written with, on the tick thread.</summary>
    protected BitWriter Writer => Server.Writer;

    /// <summary>Everyone connected (playing or logging in).</summary>
    protected IReadOnlyCollection<PlayerSession> Players => Server.Players;

    protected TickProfile Profile => Server.Profile;

    protected const int TickRate = GameServer.TickRate;

    /// <summary>Sends what is in the writer to one player, reliably and in order.</summary>
    protected void Send(PlayerSession session) => Server.Send(session);

    /// <summary>Sends what is in the writer to every playing player within the relevance radius of a point.</summary>
    protected void SendNear(Vector3 point, DeliveryMethod method = DeliveryMethod.ReliableOrdered)
    {
        float radius = Options.RelevanceRadius;
        foreach (PlayerSession session in Players)
        {
            if (session.Playing && Vector3.DistanceSquared(Zone.WorldOf(Zone.Entities.Get<Character>(session.Entity).State), point) <= radius * radius)
            {
                session.Peer.Send(Writer.Data, method);
            }
        }
    }

    /// <summary>
    /// A view from the wire as a heading from local north and a pitch where a player's character stands
    /// (docs/WHOLE_WORLD.md §5): what the zone's rules take for a shot or a throw. Aboard a ship (IRON_WAKE.md §4.11) the
    /// view is turned into the ship's frame if the client gave it in another (<paramref name="frame"/>, a ship's net ID),
    /// and read against the deck's up.
    /// </summary>
    protected (float Yaw, float Pitch) ViewAt(PlayerSession session, Vector3 view, uint frame = 0)
    {
        if (!session.Playing || !Zone.Entities.Has<Character>(session.Entity))
        {
            return (0, 0);
        }
        CharacterState state = Zone.Entities.Get<Character>(session.Entity).State;
        view = Zone.Frames.ViewIn(view, Server.Ships().FrameKey(frame), state.Frame, Zone.Tick);
        Vector3 up = state.Frame == 0 ? Zone.Frame.UpAt(state.Position) : Vector3.UnitY;
        return (Facing.YawOf(view, up), Facing.PitchOf(view, up));
    }

    /// <summary>A chat line to everyone.</summary>
    protected void Broadcast(ChatLine line) => Server.Broadcast(line);

    /// <summary>A server line in one player's chat.</summary>
    protected void Notify(EntityId player, string text)
    {
        if (SessionOf(player) is not { } to)
        {
            return;
        }
        Writer.Reset();
        new ChatLine { Kind = ChatKind.Server, From = "", Text = text }.WriteMessage(Writer);
        Send(to);
    }

    /// <summary>Tells a player an entity (a thing, a ship) is gone.</summary>
    protected void Despawned(PlayerSession session, uint netId) => Server.Despawned(session, netId);

    /// <summary>The session of a player in the world, by their character's ID; null when they are not on.</summary>
    protected PlayerSession? SessionOf(EntityId player) => Server.SessionOf(player);

    /// <summary>A character's net ID, or 0 for none.</summary>
    protected uint NetIdOf(EntityId id) =>
        !id.IsNone && Zone.Entities.TryFind(id, out Entity entity) && Zone.Entities.Has<NetPresence>(entity) ? Zone.Entities.Get<NetPresence>(entity).NetId : 0;

    protected string NameOf(EntityId id) => Zone.Players().TryFindPlayer(id, out _, out Player p) ? p.Name : id.ToString();

    /// <summary>A net ID for an entity this module replicates (kept while it lives).</summary>
    protected uint NextNetId() => Server.NextNetId();

    /// <summary>
    /// A dynamic entity of this module's own (an animal) into this snapshot: in the relevance grid with its packed
    /// state; this module describes its Spawn (<see cref="Describe"/>) and says when it rests (<see cref="AtRest"/>).
    /// </summary>
    protected void Present(uint netId, Vector3 position, in CharacterNetState state) => Server.Present(this, netId, position, state);

    /// <summary>
    /// Every intent (GROUND_SYSTEMS.md §3.2): it spends from the player's budget (a bad one too), is applied at once,
    /// and is answered with one IntentResult after this tick's changes (containers) have gone out.
    /// </summary>
    protected bool Intent(PlayerSession session, uint seq, Func<Outcome> act, bool whileDead = false)
    {
        Answer(session, seq, TakeIntent(session, out Outcome refused) ? act() : refused);
        return true;
    }

    /// <summary>Spends an intent's token: false, and why, if it can't be done at all (over the budget, or not playing).</summary>
    protected bool TakeIntent(PlayerSession session, out Outcome refused)
    {
        SurvivalTuning s = Content.Survival;
        refused = OutcomeCode.Done;
        if (!session.SpendIntentToken(Zone.Tick, s.IntentBurst, s.IntentsPerSecond))
        {
            refused = OutcomeCode.RateLimited;
            session.IntentsRateLimited++;
            return false;
        }
        if (!session.Playing)
        {
            refused = OutcomeCode.Dead;
            return false;
        }
        return true;
    }

    /// <summary>Queues an intent's answer (the host sends them after this tick's changes) and counts it.</summary>
    protected static void Answer(PlayerSession session, uint seq, Outcome result)
    {
        if (result.Ok)
        {
            session.IntentsAccepted++;
        }
        else
        {
            session.IntentsRefused++;
        }
        session.PendingResults.Add(new IntentResult { Seq = seq, Code = result.Code, Detail = (uint)Math.Max(0, result.Detail) });
        session.LastIntentSeq = Math.Max(session.LastIntentSeq, seq);
    }

    protected static int Amount(uint value) => (int)Math.Min(value, int.MaxValue);

    /// <summary>An item ID from outside: one that can't be is "expect nothing", which then can't match.</summary>
    protected static ushort Expected(uint value) => value > ushort.MaxValue ? ushort.MaxValue : (ushort)value;

    protected void Log(string line) => Server.Log(line);

    protected static string Format(Vector3 p) => GameServer.Format(p);
}
