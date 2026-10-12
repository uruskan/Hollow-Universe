using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Hollow.Core;

namespace Hollow.Net.Transport;

/// <summary>
/// A local UDP relay that makes a network worse on purpose (GROUND.md §4.4): clients connect to
/// <see cref="LocalEndPoint"/> instead of the server, and every datagram either way is delayed by half the
/// <see cref="NetConditions.LatencyMs"/> plus jitter, or lost. It works below the transport, so reliable messages are
/// really resent, exactly as on a bad line. One relay serves any number of clients (each gets its own socket toward
/// the server); the loss pattern is seeded, so a test run is repeatable in what it drops.
/// </summary>
public sealed class ConditionedRelay : IDisposable
{
    private readonly IPEndPoint _server;
    private readonly Socket _front;
    private readonly Dictionary<IPEndPoint, Socket> _upstream = new();
    private readonly PriorityQueue<Datagram, long> _queue = new();
    private readonly Lock _lock = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Thread _sender;
    private Rng _rng;
    private long _order;

    public ConditionedRelay(IPEndPoint server, NetConditions conditions, ulong seed = 1)
    {
        _server = server;
        Conditions = conditions;
        _rng = new Rng(Hash64.Of(seed, 0xC0DD17));
        _front = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        IgnoreConnectionResets(_front);
        _front.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        LocalEndPoint = (IPEndPoint)_front.LocalEndPoint!;
        TimerResolution.Begin();
        _sender = new Thread(SendLoop) { IsBackground = true, Name = "Hollow relay" };
        _sender.Start();
        _ = ReceiveFromClients();
    }

    /// <summary>Where clients connect to reach the server through the relay.</summary>
    public IPEndPoint LocalEndPoint { get; }

    /// <summary>The conditions applied from now on (may be changed while running).</summary>
    public NetConditions Conditions { get; set; }

    public long Forwarded { get; private set; }

    public long Dropped { get; private set; }

    private async Task ReceiveFromClients()
    {
        byte[] buffer = new byte[65536];
        EndPoint any = new IPEndPoint(IPAddress.Any, 0);
        while (!_stop.IsCancellationRequested)
        {
            SocketReceiveFromResult result;
            try
            {
                result = await _front.ReceiveFromAsync(buffer, SocketFlags.None, any, _stop.Token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                if (_stop.IsCancellationRequested)
                {
                    return;
                }
                continue;
            }
            var client = (IPEndPoint)result.RemoteEndPoint;
            Socket upstream;
            lock (_lock)
            {
                if (!_upstream.TryGetValue(client, out upstream!))
                {
                    upstream = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    IgnoreConnectionResets(upstream);
                    upstream.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                    _upstream.Add(client, upstream);
                    _ = ReceiveFromServer(upstream, client);
                }
            }
            Schedule(upstream, _server, buffer.AsSpan(0, result.ReceivedBytes));
        }
    }

    private async Task ReceiveFromServer(Socket upstream, IPEndPoint client)
    {
        byte[] buffer = new byte[65536];
        EndPoint any = new IPEndPoint(IPAddress.Any, 0);
        while (!_stop.IsCancellationRequested)
        {
            SocketReceiveFromResult result;
            try
            {
                result = await upstream.ReceiveFromAsync(buffer, SocketFlags.None, any, _stop.Token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                if (_stop.IsCancellationRequested)
                {
                    return;
                }
                continue;
            }
            Schedule(_front, client, buffer.AsSpan(0, result.ReceivedBytes));
        }
    }

    private void Schedule(Socket via, IPEndPoint to, ReadOnlySpan<byte> data)
    {
        lock (_lock)
        {
            NetConditions c = Conditions;
            if (c.LossPercent > 0 && _rng.NextDouble() * 100 < c.LossPercent)
            {
                Dropped++;
                return;
            }
            double delay = c.LatencyMs / 2.0 + (c.JitterMs > 0 ? _rng.Range(-c.JitterMs, c.JitterMs) : 0);
            long due = Stopwatch.GetTimestamp() + (long)(Math.Max(0, delay) * Stopwatch.Frequency / 1000);
            _queue.Enqueue(new Datagram(via, to, data.ToArray()), due * 1024 + (_order++ & 1023));
        }
    }

    private void SendLoop()
    {
        var due = new List<Datagram>();
        while (!_stop.IsCancellationRequested)
        {
            long now = Stopwatch.GetTimestamp() * 1024 + 1023;
            lock (_lock)
            {
                while (_queue.TryPeek(out _, out long when) && when <= now)
                {
                    due.Add(_queue.Dequeue());
                }
            }
            foreach (Datagram d in due)
            {
                try
                {
                    d.Via.SendTo(d.Data, d.To);
                    Forwarded++;
                }
                catch (Exception e) when (e is SocketException or ObjectDisposedException)
                {
                    // The other end went away; that's a lost datagram like any other.
                }
            }
            due.Clear();
            Thread.Sleep(1);
        }
    }

    // On Windows a UDP socket that sent to a closed port fails its next receive with "connection reset"; a relay
    // must not stop for that.
    private static void IgnoreConnectionResets(Socket socket)
    {
        if (OperatingSystem.IsWindows())
        {
            const int SioUdpConnReset = -1744830452;
            socket.IOControl(SioUdpConnReset, new byte[] { 0 }, null);
        }
    }

    public void Dispose()
    {
        if (_stop.IsCancellationRequested)
        {
            return;
        }
        _stop.Cancel();
        _sender.Join(1000);
        _front.Dispose();
        lock (_lock)
        {
            foreach (Socket s in _upstream.Values)
            {
                s.Dispose();
            }
            _upstream.Clear();
        }
        TimerResolution.End();
        _stop.Dispose();
    }

    private readonly record struct Datagram(Socket Via, IPEndPoint To, byte[] Data);
}
