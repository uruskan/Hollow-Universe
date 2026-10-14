using System;
using System.Collections.Generic;

namespace Hollow.Simulation;

/// <summary>Why items arrived, for the UI's notifications.</summary>
public enum GainReason : byte
{
    Gathered,
    PickedUp,
    Crafted,
    Looted,
    Refunded,
    Broke,
    Used,
}

/// <summary>
/// Something that happened in a tick that a player, the server or another system should hear about (GROUND.md §4.3:
/// hit markers, gather yields, damage taken, a craft finished; GROUND_SYSTEMS.md §12.3, K7): one record struct per kind,
/// in <c>Events/</c>.
/// </summary>
public interface IZoneEvent
{
}

/// <summary>What a subscriber does with an event, as it happens.</summary>
public delegate void ZoneEventHandler<T>(in T e) where T : struct, IZoneEvent;

/// <summary>
/// The zone's events (GROUND_SYSTEMS.md §12.3, K7): by type, in the order they happened. A system adds one; every
/// handler subscribed to its type runs at once (on the tick thread, inside the step), and the event stays in its list
/// (<see cref="Of{T}"/>) until whoever consumes the lists clears them (the server after each tick; the tests when they
/// look). The union struct and its kind enum are gone (K8): a new kind of event is one record and its subscribers.
/// </summary>
public sealed class ZoneEvents
{
    private readonly Dictionary<Type, Channel> _channels = new();
    private readonly List<Channel> _all = new();

    /// <summary>Adds an event and tells its subscribers.</summary>
    public void Add<T>(in T e) where T : struct, IZoneEvent
    {
        Channel<T> channel = ChannelOf<T>();
        channel.Events.Add(e);
        foreach (ZoneEventHandler<T> handler in channel.Handlers)
        {
            handler(in e);
        }
    }

    /// <summary>The events of a kind since the last <see cref="Clear"/>, oldest first.</summary>
    public IReadOnlyList<T> Of<T>() where T : struct, IZoneEvent => ChannelOf<T>().Events;

    /// <summary>Subscribes to a kind of event (at start-up: a server's route, a system's hearing).</summary>
    public void On<T>(ZoneEventHandler<T> handler) where T : struct, IZoneEvent
    {
        Channel<T> channel = ChannelOf<T>();
        var handlers = new ZoneEventHandler<T>[channel.Handlers.Length + 1];
        channel.Handlers.CopyTo(handlers, 0);
        handlers[^1] = handler;
        channel.Handlers = handlers;
    }

    /// <summary>Events of every kind since the last <see cref="Clear"/>.</summary>
    public int Count
    {
        get
        {
            int count = 0;
            foreach (Channel channel in _all)
            {
                count += channel.Count;
            }
            return count;
        }
    }

    /// <summary>Forgets every event (the subscribers already had them).</summary>
    public void Clear()
    {
        foreach (Channel channel in _all)
        {
            channel.Clear();
        }
    }

    private Channel<T> ChannelOf<T>() where T : struct, IZoneEvent
    {
        if (!_channels.TryGetValue(typeof(T), out Channel? channel))
        {
            channel = new Channel<T>();
            _channels.Add(typeof(T), channel);
            _all.Add(channel);
        }
        return (Channel<T>)channel;
    }

    private abstract class Channel
    {
        public abstract int Count { get; }

        public abstract void Clear();
    }

    private sealed class Channel<T> : Channel where T : struct, IZoneEvent
    {
        public readonly List<T> Events = new();

        public ZoneEventHandler<T>[] Handlers = Array.Empty<ZoneEventHandler<T>>();

        public override int Count => Events.Count;

        public override void Clear() => Events.Clear();
    }
}
