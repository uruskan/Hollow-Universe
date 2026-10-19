using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Hollow.Core;
using Hollow.Net.Transport;

namespace Hollow.Tests;

/// <summary>
/// <see cref="ConditionedRelay"/> for a stepped test: the same bad line (half the latency each way, jitter, loss) between
/// one client and the server, but on the clock its owner passes to <see cref="Pump"/> instead of the wall clock, and
/// on the calling thread instead of its own threads. A datagram's delay and whether it is lost depend only on the
/// seed and the order of datagrams, never on how busy the computer is, which the relay of the game cannot promise (its
/// delays run on the wall clock and its sockets on pool threads, so a stall reorders what the test thinks it sent).
/// </summary>
public sealed class SimulatedRelay : IDisposable
{
    private readonly IPEndPoint _server;
    private readonly NetConditions _conditions;
    private readonly Socket _front;
    private readonly Socket _toServer;
    private readonly PriorityQueue<Datagram, (double Due, long Order)> _queue = new();
    private readonly byte[] _buffer = new byte[65536];
    private Rng _rng;
    private IPEndPoint? _client;
    private long _order;

    public SimulatedRelay(IPEndPoint server, NetConditions conditions, ulong seed)
    {
        _server = server;
        _conditions = conditions;
        _rng = new Rng(Hash64.Of(seed, 0xC0DD17));
        _front = Open();
        _toServer = Open();
        Port = ((IPEndPoint)_front.LocalEndPoint!).Port;
    }

    /// <summary>Where the client connects to reach the server through the relay.</summary>
    public int Port { get; }

    public long Forwarded { get; private set; }

    public long Dropped { get; private set; }

    /// <summary>
    /// Takes in what both ends have sent so far (each datagram is lost, or due after its delay from <paramref name="now"/>, in
    /// seconds), then delivers what is due.
    /// </summary>
    public void Pump(double now)
    {
        Receive(_front, now, fromClient: true);
        Receive(_toServer, now, fromClient: false);
        while (_queue.TryPeek(out Datagram next, out (double Due, long Order) when) && when.Due <= now)
        {
            _queue.Dequeue();
            try
            {
                next.Via.SendTo(next.Data, next.To);
                Forwarded++;
            }
            catch (Exception e) when (e is SocketException or ObjectDisposedException)
            {
                // The other end went away; that is a lost datagram like any other.
            }
        }
    }

    private void Receive(Socket socket, double now, bool fromClient)
    {
        EndPoint from = new IPEndPoint(IPAddress.Any, 0);
        while (socket.Available > 0)
        {
            int length;
            try
            {
                length = socket.ReceiveFrom(_buffer, ref from);
            }
            catch (SocketException)
            {
                continue; // a reset from a closed port: nothing arrived
            }
            if (fromClient)
            {
                _client = (IPEndPoint)from;
            }
            else if (_client == null)
            {
                continue;
            }
            Schedule(fromClient ? _toServer : _front, fromClient ? _server : _client!, _buffer.AsSpan(0, length), now);
        }
    }

    private void Schedule(Socket via, IPEndPoint to, ReadOnlySpan<byte> data, double now)
    {
        if (_conditions.LossPercent > 0 && _rng.NextDouble() * 100 < _conditions.LossPercent)
        {
            Dropped++;
            return;
        }
        double delay = _conditions.LatencyMs / 2.0 + (_conditions.JitterMs > 0 ? _rng.Range(-_conditions.JitterMs, _conditions.JitterMs) : 0);
        _queue.Enqueue(new Datagram(via, to, data.ToArray()), (now + Math.Max(0, delay) / 1000.0, _order++));
    }

    private static Socket Open()
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        if (OperatingSystem.IsWindows())
        {
            const int SioUdpConnReset = -1744830452;
            socket.IOControl(SioUdpConnReset, new byte[] { 0 }, null);
        }
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return socket;
    }

    public void Dispose()
    {
        _front.Dispose();
        _toServer.Dispose();
    }

    private readonly record struct Datagram(Socket Via, IPEndPoint To, byte[] Data);
}
