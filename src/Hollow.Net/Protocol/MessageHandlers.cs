using System;
using Hollow.Net.Bits;

namespace Hollow.Net.Protocol;

/// <summary>What a handler does with a message that arrived, in the context it arrived in (a connection, a client, a server).</summary>
public delegate void MessageHandler<TContext, TMessage>(TContext context, in TMessage message) where TMessage : struct;

/// <summary>
/// One handler per message type, found by a packet's type byte (GROUND_SYSTEMS.md §12.4, K9): registered once at
/// start-up, a second for the same type throws; a type with no handler or a message that doesn't read is reported as
/// bad, as the switches did. A lookup is an array index by the type byte: no switch, no dictionary, nothing allocated
/// per packet.
/// </summary>
public sealed class MessageHandlers<TContext>
{
    private readonly Entry?[] _entries = new Entry?[256];

    /// <summary>Gives <typeparamref name="T"/> its handler; throws when its type byte already has one.</summary>
    public MessageHandlers<TContext> On<T>(MessageHandler<TContext, T> handler) where T : struct, INetMessage<T>
    {
        ArgumentNullException.ThrowIfNull(handler);
        byte type = T.Id;
        if (_entries[type] is { } other)
        {
            throw new InvalidOperationException($"message type {type} has two handlers ({other.Name}, {typeof(T).Name})");
        }
        _entries[type] = new Entry<T>(handler);
        return this;
    }

    /// <summary>Whether a type byte has a handler.</summary>
    public bool Handles(byte type) => _entries[type] != null;

    /// <summary>The name of the message a type byte stands for, for logs; null when it has no handler.</summary>
    public string? NameOf(byte type) => _entries[type]?.Name;

    /// <summary>
    /// Reads the message after its type byte and hands it to its handler. False, with no handler called, when the type
    /// has none or the message doesn't read (the reader is <see cref="BitReader.Failed"/>): the packet is bad.
    /// </summary>
    public bool Dispatch(byte type, BitReader reader, TContext context)
    {
        Entry? entry = _entries[type];
        return entry != null && entry.Dispatch(reader, context);
    }

    private abstract class Entry
    {
        public abstract string Name { get; }

        public abstract bool Dispatch(BitReader reader, TContext context);
    }

    private sealed class Entry<T>(MessageHandler<TContext, T> handler) : Entry where T : struct, INetMessage<T>
    {
        public override string Name => typeof(T).Name;

        public override bool Dispatch(BitReader reader, TContext context)
        {
            T message = T.Read(reader);
            if (reader.Failed)
            {
                return false;
            }
            handler(context, in message);
            return true;
        }
    }
}
