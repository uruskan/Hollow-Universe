using Hollow.Net.Bits;

namespace Hollow.Net.Protocol;

/// <summary>
/// A message of the protocol (GROUND.md §4.4): its type byte, its reader and its writers, so a handler registry can
/// route a packet to its handler by type (GROUND_SYSTEMS.md §12.4). The generator makes every <c>[NetMessage]</c>
/// struct one (ARCHITECTURE.md §11.2); <see cref="MessageHandlers{TContext}"/> is the registry.
/// </summary>
public interface INetMessage<TSelf> where TSelf : struct, INetMessage<TSelf>
{
    /// <summary>The type byte (the struct's <c>TypeId</c>): the first byte of every packet that carries this message.</summary>
    static abstract byte Id { get; }

    /// <summary>Reads the message after its type byte; bad data marks the reader <see cref="BitReader.Failed"/> rather than throwing.</summary>
    static abstract TSelf Read(BitReader r);

    /// <summary>The type byte, then the message.</summary>
    void WriteMessage(BitWriter w);

    /// <summary>The message alone.</summary>
    void Write(BitWriter w);
}
