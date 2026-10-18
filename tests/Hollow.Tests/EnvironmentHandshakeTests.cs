using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using Hollow.Client;
using Hollow.Net.Bits;
using Hollow.Net.Protocol;
using Hollow.Server;
using Hollow.Simulation;
using Hollow.Surface;
using LiteNetLib;

namespace Hollow.Tests;

public static class EnvironmentHandshakeTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Test]
    public static void EnvironmentHandshakeReconstructsLegacyDescriptorsWithoutChangingTheirSeeds()
    {
        foreach (GroundKind ground in new[] { GroundKind.StandIn, GroundKind.Walking })
        {
            ZoneSpec spec = Spec() with { Ground = ground };
            PlanetEnvironmentDescriptor original = spec.EnvironmentDescriptor();
            ZoneInfo info = NetConvert.ToInfo(spec);
            EnvironmentIdentity identity = NetConvert.ToEnvironment(original);
            Assert.True(NetConvert.TryResolveEnvironment(info, identity, out _, out var reconstructed, out _), "known legacy contract is supported");
            Assert.Equal(original.CanonicalIdentity, reconstructed!.CanonicalIdentity, "all canonical physical inputs survive negotiation");
            Assert.Equal(original.SurfaceSeed, reconstructed.SurfaceSeed, "surface seed is unchanged");
            Assert.Equal(original.TerrainSeed, reconstructed.TerrainSeed, "terrain seed is unchanged");
            Assert.Equal(original.QualificationIdentity, identity.QualificationIdentity, "qualification is a separate explicit compatibility fact");
        }
    }

    [Test]
    public static void EnvironmentHandshakeRefusesUnknownMalformedAndMismatchedIdentityFacts()
    {
        ZoneSpec spec = Spec();
        ZoneInfo info = NetConvert.ToInfo(spec);
        EnvironmentIdentity expected = NetConvert.ToEnvironment(spec.EnvironmentDescriptor());
        EnvironmentIdentity changed = expected;
        changed.DescriptorSchema++;
        Refused(info, changed);
        changed = expected; changed.ProfileId = "unknown-profile"; Refused(info, changed);
        changed = expected; changed.DescriptorIdentity = Different(expected.DescriptorIdentity); Refused(info, changed);
        changed = expected; changed.QualificationIdentity = Different(expected.QualificationIdentity); Refused(info, changed);
        changed = expected; changed.DescriptorIdentity = "bad"; Refused(info, changed);
        changed = expected; changed.QualificationIdentity = new string('A', 65); Refused(info, changed);
        changed = expected; changed.ProfileId = new string('x', 65); Refused(info, changed);
        Refused(info, default);
        ZoneInfo bad = info; bad.Ground = byte.MaxValue; Refused(bad, expected);
        bad = info; bad.Radius = double.NaN; Refused(bad, expected);
        bad = info; bad.LatitudeDegrees = 91; Refused(bad, expected);
        bad = info; bad.RotationTimeScale = float.NaN; Refused(bad, expected);
        bad = info; bad.RotationTimeScale = 0; Refused(bad, expected);
        bad = info; bad.SurfaceSeed++; Refused(bad, expected);
    }

    [Test]
    public static void EnvironmentHandshakeWireMessagesFitTheirBoundAndRoundTripQualification()
    {
        WorldOffer offer = Offer();
        var writer = new BitWriter();
        offer.WriteMessage(writer);
        Assert.True(writer.ByteCount <= Protocol.MaxWorldHandshakeBytes, "offer has a fixed small bound");
        var reader = new BitReader();
        byte[] bytes = writer.ToArray();
        reader.Reset(bytes, 0, bytes.Length);
        Assert.Equal((uint)WorldOffer.TypeId, reader.ReadBits(8), "offer type");
        WorldOffer decoded = WorldOffer.Read(reader);
        Assert.True(!reader.Failed && NetConvert.SameZone(offer.Zone, decoded.Zone) &&
            NetConvert.SameEnvironment(offer.Environment, decoded.Environment), "offer preserves every compatibility field");
        writer.Reset();
        new WorldAccept { OfferId = offer.OfferId, Environment = offer.Environment }.WriteMessage(writer);
        Assert.True(writer.ByteCount <= Protocol.MaxWorldHandshakeBytes, "accept fits the bound");
        writer.Reset();
        WelcomeFor(offer).WriteMessage(writer);
        Assert.True(writer.ByteCount <= Protocol.MaxWorldHandshakeBytes, "welcome fits the bound");
        bytes = writer.ToArray();
        reader.Reset(bytes, 0, bytes.Length - 1);
        _ = reader.ReadBits(8);
        _ = Welcome.Read(reader);
        Assert.True(reader.Failed, "truncated welcome fails decoding");
    }

    [Test]
    public static void EnvironmentHandshakeClientRejectsBeforeAnyWorldAcquisitionOrPrediction()
    {
        int builds = 0;
        using var store = new WorldStore(new WorldStoreOptions(), (descriptor, _) =>
        { builds++; return new SphereSurface(descriptor.Radius, 1, 0); });
        WorldOffer valid = Offer();
        using (var client = Client(store))
        {
            Welcome bare = WelcomeFor(valid);
            Invoke(client, "OnWelcome", bare);
            RejectedClient(client);
        }
        using (var client = Client(store))
        {
            WorldOffer bad = valid;
            bad.Environment.QualificationIdentity = Different(valid.Environment.QualificationIdentity);
            Invoke(client, "OnWorldOffer", bad);
            RejectedClient(client);
        }
        using (var client = Client(store))
        {
            Invoke(client, "OnWorldOffer", valid);
            Assert.Equal(ClientState.Joining, client.State, "valid metadata alone does not publish a world");
            Assert.True(client.Surface == null && client.Prediction == null, "offer has no physics or world acquisition");
            Welcome changed = WelcomeFor(valid);
            changed.Zone.SurfaceGravity += 1;
            Invoke(client, "OnWelcome", changed);
            RejectedClient(client);
        }
        using (var client = Client(store))
        {
            Invoke(client, "OnWorldOffer", valid);
            Welcome stale = WelcomeFor(valid);
            stale.OfferId++;
            Invoke(client, "OnWelcome", stale);
            RejectedClient(client);
        }
        Assert.Equal(0, builds, "all refusals precede factory dispatch");
        Assert.Equal(0, store.Diagnostics.ActiveLeases, "no refusal retains a world pin");
    }

    [Test]
    public static void EnvironmentHandshakeClientExpiresOnePendingOfferWithoutAcquisition()
    {
        int builds = 0;
        using var store = new WorldStore(new WorldStoreOptions(), (descriptor, _) =>
        { builds++; return new SphereSurface(descriptor.Radius, 1, 0); });
        using var client = Client(store);
        Invoke(client, "OnWorldOffer", Offer());
        Invoke(client, "CheckWorldHandshakeTimeout", double.MaxValue);
        RejectedClient(client);
        Assert.Equal(0, builds, "timeout does not construct a world");
    }

    [Test]
    public static void EnvironmentHandshakePendingPeersReserveCapacityWithoutPublishingPlayers()
    {
        using var server = new ServerFixture(maxPlayers: 1);
        int baseline = server.Host.Inspect(s => s.Zone.Entities.Count);
        using var first = new RawPeer(server.Host, "Pending");
        Pump(() => first.Offer.HasValue, first);
        Assert.Equal(1, server.Host.Inspect(s => s.PendingWorldJoins), "one bounded pending peer");
        Assert.Equal(0, server.Host.Inspect(s => s.Players.Count), "no PlayerSession before matching acceptance");
        using (var duplicate = new RawPeer(server.Host, "Pending"))
        {
            Pump(() => duplicate.Disconnected, first, duplicate);
            Assert.Equal(GoodbyeReason.NameInUse, duplicate.Reason!.Value, "pending names are reserved");
        }
        using (var excess = new RawPeer(server.Host, "Excess"))
        {
            Pump(() => excess.Disconnected, first, excess);
            Assert.Equal(GoodbyeReason.ServerFull, excess.Reason!.Value, "pending peers consume admission capacity");
        }
        WorldOffer offer = first.Offer!.Value;
        var bad = new WorldAccept { OfferId = offer.OfferId, Environment = offer.Environment };
        bad.Environment.QualificationIdentity = Different(bad.Environment.QualificationIdentity);
        first.Send(bad);
        Pump(() => first.Disconnected, first);
        Assert.Equal(GoodbyeReason.EnvironmentMismatch, first.Reason!.Value, "mismatched qualification is refused before login");
        Assert.Equal(0, server.Host.Inspect(s => s.PendingWorldJoins + s.Players.Count), "refusal returns admission capacity");
        Assert.Equal(baseline, server.Host.Inspect(s => s.Zone.Entities.Count), "refused connections neither create nor wake a character");
        Assert.Equal(0, first.Welcomes, "no Welcome before a matching confirmation");
    }

    [Test]
    public static void EnvironmentHandshakeTimeoutAndReconnectRejectAnEarlierConnectionsAcceptance()
    {
        using var server = new ServerFixture(maxPlayers: 1);
        WorldOffer stale;
        using (var first = new RawPeer(server.Host, "Reconnect"))
        {
            Pump(() => first.Offer.HasValue, first);
            stale = first.Offer!.Value;
            server.Host.Inspect(s =>
            {
                Invoke(s, "ExpireWorldOffers", Stopwatch.GetTimestamp() + (long)(Protocol.WorldHandshakeTimeoutSeconds + 1) * Stopwatch.Frequency);
                return 0;
            });
            Pump(() => first.Disconnected, first);
            Assert.Equal(GoodbyeReason.Timeout, first.Reason!.Value, "live pending peer has a bounded deadline");
        }
        using var second = new RawPeer(server.Host, "Reconnect");
        Pump(() => second.Offer.HasValue, second);
        Assert.True(second.Offer!.Value.OfferId != stale.OfferId, "reconnection gets a new offer token");
        second.Send(new WorldAccept { OfferId = stale.OfferId, Environment = stale.Environment });
        Pump(() => second.Disconnected, second);
        Assert.Equal(GoodbyeReason.EnvironmentMismatch, second.Reason!.Value, "old confirmation cannot log in the new connection");
        Assert.Equal(0, server.Host.Inspect(s => s.Players.Count), "neither connection publishes a player");
    }

    [Test]
    public static void EnvironmentHandshakeValidTransportPublishesTheAcquiredDescriptorExactlyOnce()
    {
        using var server = new ServerFixture();
        using var raw = new RawPeer(server.Host, "Accepted");
        Pump(() => raw.Offer.HasValue, raw);
        WorldOffer offer = raw.Offer!.Value;
        Assert.Equal(server.Descriptor!.Identity, offer.Environment.DescriptorIdentity, "offer comes from the acquired server descriptor");
        Assert.Equal(server.Descriptor.QualificationIdentity, offer.Environment.QualificationIdentity, "acquired qualification is offered separately");
        var acceptance = new WorldAccept { OfferId = offer.OfferId, Environment = offer.Environment };
        raw.Send(acceptance);
        raw.Send(acceptance);
        Pump(() => raw.Welcomes == 1, raw);
        Assert.Equal(1, server.Host.Inspect(s => s.Players.Count), "duplicate confirmation creates one session");
        Assert.True(NetConvert.SameEnvironment(offer.Environment, raw.Welcome!.Value.Environment) &&
            NetConvert.SameZone(offer.Zone, raw.Welcome.Value.Zone) && raw.Welcome.Value.OfferId == offer.OfferId,
            "Welcome is bound to the accepted offer");
        int builds = 0;
        using var store = new WorldStore(new WorldStoreOptions(), (descriptor, _) =>
        { builds++; return new SphereSurface(descriptor.Radius, 1, 0); });
        using var client = Client(store);
        client.Connect("127.0.0.1", server.Host.Port);
        var clock = Stopwatch.StartNew();
        while (client.State != ClientState.Playing)
        {
            Assert.True(clock.Elapsed.TotalSeconds < 10, "normal GameClient completes negotiation: " + client.DisconnectReason);
            client.Update(0.01, default);
            Thread.Sleep(2);
        }
        using WorldLease lease = client.ForkWorldLease();
        Assert.Equal(server.Descriptor.CanonicalIdentity, lease.Descriptor.CanonicalIdentity, "prediction owns the same physical descriptor");
        Assert.Equal(1, builds, "normal client acquires once after confirmation");
    }

    private static ZoneSpec Spec() => ZoneSpec.StartingWorld(15, 30) with { Radius = 1000, Ground = GroundKind.StandIn };
    private static WorldOffer Offer() => new() { OfferId = 7, Zone = NetConvert.ToInfo(Spec()), Environment = NetConvert.ToEnvironment(Spec().EnvironmentDescriptor()) };
    private static Welcome WelcomeFor(WorldOffer offer) => new() { OfferId = offer.OfferId, Environment = offer.Environment,
        Zone = offer.Zone, AttackSession = 1, NetId = 1, EntityId = 1, ServerName = "handshake fixture" };
    private static GameClient Client(WorldStore store) => new(new ClientOptions { Name = "Client", Content = ContentFixture.Content, WorldStore = store, Log = _ => { } });
    private static string Different(string digest) => (digest[0] == 'A' ? "B" : "A") + digest[1..];
    private static void Refused(ZoneInfo info, EnvironmentIdentity identity) =>
        Assert.True(!NetConvert.TryResolveEnvironment(info, identity, out var zone, out var descriptor, out var reason) &&
            zone == null && descriptor == null && reason != null, "invalid input returns an explicit refusal with no descriptor");
    private static void RejectedClient(GameClient client) => Assert.True(client.State == ClientState.Disconnected &&
        client.Goodbye == GoodbyeReason.EnvironmentMismatch && client.Surface == null && client.Zone == null && client.Prediction == null,
        "refusal publishes no world or prediction");
    private static void Invoke(object target, string method, object argument) =>
        target.GetType().GetMethod(method, Private)!.Invoke(target, new[] { argument });
    private static void Pump(Func<bool> done, params RawPeer[] peers)
    {
        var clock = Stopwatch.StartNew();
        while (!done())
        {
            Assert.True(clock.Elapsed.TotalSeconds < 10, "bounded raw handshake completes");
            foreach (RawPeer peer in peers) peer.Poll();
            Thread.Sleep(2);
        }
    }

    private sealed class ServerFixture : IDisposable
    {
        private readonly WorldStore _store;
        public PlanetEnvironmentDescriptor? Descriptor { get; private set; }
        public GameServer Host { get; }
        public ServerFixture(int maxPlayers = 4)
        {
            _store = new(new WorldStoreOptions(), (descriptor, _) =>
            { Descriptor = descriptor; return new SphereSurface(descriptor.Radius, 1, 0); });
            try
            {
                Host = new(new ServerOptions { Port = 0, DatabasePath = null, WorldStore = _store, Content = ContentFixture.Content,
                    MaxPlayers = maxPlayers, Ground = GroundKind.StandIn, StandIn = true, SpawnAtOrigin = true,
                    World = false, Animals = false, Log = _ => { } });
                Host.StartThread();
            }
            catch { _store.Dispose(); throw; }
        }
        public void Dispose() { try { Host.Dispose(); } finally { _store.Dispose(); } }
    }

    private sealed class RawPeer : INetEventListener, IDisposable
    {
        private readonly NetManager _net;
        private readonly NetPeer _peer;
        private readonly BitReader _reader = new();
        public WorldOffer? Offer { get; private set; }
        public Welcome? Welcome { get; private set; }
        public int Welcomes { get; private set; }
        public bool Disconnected { get; private set; }
        public GoodbyeReason? Reason { get; private set; }
        public RawPeer(GameServer server, string name)
        {
            _net = new NetManager(this) { AutoRecycle = true, IPv6Enabled = false };
            Assert.True(_net.StartInManualMode(0), "raw peer starts");
            var writer = new BitWriter();
            new Hello { ProtocolVersion = Protocol.Version, SchemaHash = Protocol.SchemaHash, PlayerName = name, ContentHash = server.Content.Hash }.WriteMessage(writer);
            _peer = _net.Connect(new IPEndPoint(IPAddress.Loopback, server.Port), writer.Data);
        }
        public void Send(WorldAccept accepted)
        {
            var writer = new BitWriter();
            accepted.WriteMessage(writer);
            _peer.Send(writer.Data, DeliveryMethod.ReliableOrdered);
        }
        public void Poll() { _net.PollEvents(); _net.ManualUpdate(10); }
        public void OnNetworkReceive(NetPeer peer, NetPacketReader packet, byte channel, DeliveryMethod method)
        {
            _reader.Reset(packet.RawData, packet.Position, packet.AvailableBytes);
            uint type = _reader.ReadBits(8);
            if (type == WorldOffer.TypeId) Offer = WorldOffer.Read(_reader);
            else if (type == Hollow.Net.Protocol.Welcome.TypeId) { Welcome = Hollow.Net.Protocol.Welcome.Read(_reader); Welcomes++; }
        }
        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
        {
            Disconnected = true;
            byte[] data = info.AdditionalData.GetRemainingBytes();
            _reader.Reset(data, 0, data.Length);
            if (_reader.ReadBits(8) == Goodbye.TypeId)
            {
                Goodbye bye = Goodbye.Read(_reader);
                if (!_reader.Failed) Reason = bye.Reason;
            }
        }
        public void OnPeerConnected(NetPeer peer) { }
        public void OnNetworkError(IPEndPoint endPoint, SocketError socketError) { }
        public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }
        public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }
        public void OnConnectionRequest(ConnectionRequest request) => request.Reject();
        public void OnMessageDelivered(NetPeer peer, object userData) { }
        public void OnNtpResponse(LiteNetLib.Utils.NtpPacket packet) { }
        public void OnPeerAddressChanged(NetPeer peer, IPEndPoint previousAddress) { }
        public void Dispose() { _peer.Disconnect(); _net.ManualUpdate(0); _net.Stop(); }
    }
}
