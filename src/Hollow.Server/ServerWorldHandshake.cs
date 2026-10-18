using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Hollow.Net.Bits;
using Hollow.Net.Protocol;
using LiteNetLib;

namespace Hollow.Server;

/// <summary>
/// Owner-thread connection admission and environment negotiation. Pending peers own no player session or login.
/// The host creates those only after a matching acceptance; no host callbacks are retained.
/// </summary>
internal sealed class ServerWorldHandshake
{
    private readonly Dictionary<NetPeer, PendingWorldJoin> _pending = new();
    private readonly Dictionary<NetPeer, WorldOffer> _accepted = new();
    private readonly List<NetPeer> _expired = new();
    private readonly BitReader _reader = new();
    private readonly BitWriter _writer = new(512);
    private readonly ZoneInfo _zone;
    private readonly EnvironmentIdentity _environment;
    private uint _nextOffer = 1;
    private sealed record PendingWorldJoin(AcceptedWorldJoin Join, long Started);

    internal readonly record struct AcceptedWorldJoin(string Name, byte Body, WorldOffer Offer);

    public ServerWorldHandshake(ZoneInfo zone, EnvironmentIdentity environment)
    {
        if (!NetConvert.TryResolveEnvironment(zone, environment, out _, out _, out string? reason))
            throw new InvalidOperationException("The acquired server world cannot be offered: " + reason);
        _zone = zone;
        _environment = environment;
    }

    public int PendingCount => _pending.Count;
    public bool IsPending(NetPeer peer) => _pending.ContainsKey(peer);
    public WorldOffer AcceptedOffer(NetPeer peer) => _accepted[peer];
    public void Confirm(NetPeer peer, AcceptedWorldJoin join) => _accepted.Add(peer, join.Offer);
    public void ForgetAccepted(NetPeer peer) => _accepted.Remove(peer);
    public void ClearAccepted() => _accepted.Clear();

    public bool RemovePeer(NetPeer peer)
    {
        _accepted.Remove(peer);
        return _pending.Remove(peer);
    }

    public void Request(ConnectionRequest request, ulong contentHash, IEnumerable<string> activeNames,
        int activeCount, int maxPlayers, Action<string> log)
    {
        if (request.Data.AvailableBytes > Protocol.MaxWorldHandshakeBytes)
        {
            Refuse(request, GoodbyeReason.ProtocolMismatch, "Connection request exceeds the protocol limit.", log);
            return;
        }
        byte[] data = request.Data.GetRemainingBytes();
        _reader.Reset(data, 0, data.Length);
        Hello hello = default;
        bool parsed = _reader.ReadBits(8) == Hello.TypeId;
        if (parsed)
        {
            hello = Hello.Read(_reader);
            parsed = !_reader.Failed;
        }
        if (!parsed || hello.ProtocolVersion != Protocol.Version || hello.SchemaHash != Protocol.SchemaHash)
        {
            Refuse(request, GoodbyeReason.ProtocolMismatch, $"This server speaks protocol {Protocol.Version} ({Protocol.SchemaHash:X16}); update the game.", log);
            return;
        }
        if (hello.ContentHash != contentHash)
        {
            Refuse(request, GoodbyeReason.ContentMismatch, $"This server runs other game data ({contentHash:X16}); update the game.", log);
            return;
        }
        string name = (hello.PlayerName ?? "").Trim();
        if (!IsValidName(name))
        {
            Refuse(request, GoodbyeReason.BadName, "Names are 1 to 32 letters, digits, spaces, '-', '_' or '.'.", log);
            return;
        }
        if (_pending.Values.Any(p => string.Equals(p.Join.Name, name, StringComparison.OrdinalIgnoreCase)) ||
            activeNames.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
        {
            Refuse(request, GoodbyeReason.NameInUse, $"{name} is already playing.", log);
            return;
        }
        if (activeCount + _pending.Count >= maxPlayers || _pending.Count >= Protocol.MaxPendingWorldJoins)
        {
            Refuse(request, GoodbyeReason.ServerFull, "The server is full.", log);
            return;
        }
        Offer(request.Accept(), name, hello.Body);
    }

    public static bool IsValidName(string name) =>
        name.Length is >= 1 and <= Protocol.MaxPlayerNameBytes && name == name.Trim() &&
        name.All(c => char.IsAsciiLetterOrDigit(c) || c is ' ' or '-' or '_' or '.');

    private void Refuse(ConnectionRequest request, GoodbyeReason reason, string text, Action<string> log)
    {
        log($"refused {request.RemoteEndPoint}: {reason}");
        _writer.Reset();
        new Goodbye { Reason = reason, Text = text }.WriteMessage(_writer);
        request.Reject(_writer.ToArray());
    }

    private void Offer(NetPeer peer, string name, byte body)
    {
        // Never wrap a connection token into a formerly offered identity within this server lifetime.
        if (_nextOffer == 0)
        {
            Refuse(peer, GoodbyeReason.ServerFull, "World handshake identifiers are exhausted.");
            return;
        }
        var offer = new WorldOffer { OfferId = _nextOffer++, Zone = _zone, Environment = _environment };
        _pending.Add(peer, new(new(name, Math.Min(body, (byte)1), offer), Stopwatch.GetTimestamp()));
        _writer.Reset();
        offer.WriteMessage(_writer);
        peer.Send(_writer.Data, DeliveryMethod.ReliableOrdered);
    }

    public bool TryAccept(NetPeer peer, NetPacketReader packet, DeliveryMethod method, out AcceptedWorldJoin join)
    {
        join = default;
        if (!_pending.TryGetValue(peer, out PendingWorldJoin? pending)) return false;
        if (Expired(pending, Stopwatch.GetTimestamp()))
        {
            Refuse(peer, GoodbyeReason.Timeout, "World compatibility confirmation timed out.");
            return false;
        }
        if (method != DeliveryMethod.ReliableOrdered || packet.AvailableBytes > Protocol.MaxWorldHandshakeBytes)
        {
            Refuse(peer, GoodbyeReason.EnvironmentMismatch, "Invalid world confirmation.");
            return false;
        }
        _reader.Reset(packet.RawData, packet.Position, packet.AvailableBytes);
        bool valid = _reader.ReadBits(8) == WorldAccept.TypeId;
        WorldAccept accepted = valid ? WorldAccept.Read(_reader) : default;
        if (!valid || _reader.Failed || accepted.OfferId != pending.Join.Offer.OfferId ||
            !NetConvert.SameEnvironment(accepted.Environment, pending.Join.Offer.Environment))
        {
            Refuse(peer, GoodbyeReason.EnvironmentMismatch, "The world confirmation does not match this connection's offer.");
            return false;
        }
        _pending.Remove(peer);
        join = pending.Join;
        return true;
    }

    private static bool Expired(PendingWorldJoin pending, long now) =>
        now - pending.Started >= (long)Protocol.WorldHandshakeTimeoutSeconds * Stopwatch.Frequency;

    public void Expire(long now)
    {
        _expired.Clear();
        foreach ((NetPeer peer, PendingWorldJoin pending) in _pending)
            if (Expired(pending, now)) _expired.Add(peer);
        foreach (NetPeer peer in _expired)
            Refuse(peer, GoodbyeReason.Timeout, "World compatibility confirmation timed out.");
        _expired.Clear();
    }

    public void Refuse(NetPeer peer, GoodbyeReason reason, string text)
    {
        _pending.Remove(peer);
        _writer.Reset();
        new Goodbye { Reason = reason, Text = text }.WriteMessage(_writer);
        peer.Disconnect(_writer.ToArray());
    }

    public void ClosePending()
    {
        var failures = new List<Exception>();
        foreach (NetPeer peer in _pending.Keys.ToArray())
        {
            try { Refuse(peer, GoodbyeReason.ServerShutdown, "The server is shutting down."); }
            catch (Exception error) { failures.Add(error); }
        }
        _pending.Clear();
        if (failures.Count != 0) throw new AggregateException("Closing pending world handshakes failed.", failures);
    }
}
