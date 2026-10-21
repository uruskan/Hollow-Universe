using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Hollow.Net.Bits;
using Hollow.Net.Protocol;
using Hollow.Net.Transport;
using LiteNetLib;

namespace Hollow.Client;

/// <summary>Transport setup, updates and sends. Holds no state; the host owns the network, returned peer and any relay.</summary>
internal static class ClientTransport
{
    public static NetManager CreateNetwork(INetEventListener listener) => new(listener)
    {
        AutoRecycle = true,
        EnableStatistics = true,
        DisconnectTimeout = 10_000,
        IPv6Enabled = false,
        UnconnectedMessagesEnabled = false,
        UnsyncedEvents = false,
        UnsyncedReceiveEvent = false,
        UnsyncedDeliveryEvent = false,
    };

    public static NetPeer? Connect(NetManager net, ClientOptions options, ulong contentHash, BitWriter writer,
        string host, int port, ref ConditionedRelay? ownedRelay, out IPEndPoint server, out IPEndPoint target)
    {
        IPAddress address = IPAddress.TryParse(host, out IPAddress? parsed)
            ? parsed
            : Dns.GetHostAddresses(host).First(a => a.AddressFamily == AddressFamily.InterNetwork);
        server = new IPEndPoint(address, port);
        target = server;
        if (options.SharedRelay != null)
        {
            target = options.SharedRelay.LocalEndPoint;
        }
        else if (!options.Conditions.IsNone)
        {
            // Assign ownership before Connect can throw, so the host's disposal still retires this relay.
            ownedRelay = new ConditionedRelay(server, options.Conditions, (ulong)Environment.ProcessId);
            target = ownedRelay.LocalEndPoint;
        }
        writer.Reset();
        new Hello { ProtocolVersion = Protocol.Version, SchemaHash = Protocol.SchemaHash, PlayerName = options.Name,
            ContentHash = contentHash, Body = options.Body }.WriteMessage(writer);
        return net.Connect(target, writer.Data);
    }

    public static void Update(NetManager net, bool background, float elapsedMilliseconds)
    {
        if (background)
        {
            net.TriggerUpdate();
        }
        else
        {
            net.ManualUpdate(elapsedMilliseconds);
        }
    }

    public static void SendChat(NetPeer? peer, ClientState state, BitWriter writer, string text)
    {
        if (peer == null || state != ClientState.Playing || string.IsNullOrWhiteSpace(text))
        {
            return;
        }
        writer.Reset();
        new ChatSend { Text = text.Trim() }.WriteMessage(writer);
        peer.Send(writer.Data, DeliveryMethod.ReliableOrdered);
    }

    public static bool Send<T>(NetPeer? peer, ClientState state, BitWriter writer, in T message, DeliveryMethod method)
        where T : struct, INetMessage<T>
    {
        if (peer == null || state != ClientState.Playing)
        {
            return false;
        }
        writer.Reset();
        message.WriteMessage(writer);
        peer.Send(writer.Data, method);
        return true;
    }

}
