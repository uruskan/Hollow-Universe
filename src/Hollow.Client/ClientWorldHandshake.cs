using Hollow.Net.Bits;
using Hollow.Net.Protocol;
using LiteNetLib;

namespace Hollow.Client;

/// <summary>
/// Owner-thread world-offer state and protocol admission. Holds no client, world lease, prediction or callback;
/// the host applies rejections and opens/retires worlds only after these checks.
/// </summary>
internal sealed class ClientWorldHandshake
{
    private WorldOffer? _accepted;
    private bool _welcomeConsumed;
    private double _started;
    private string? _rejection;
    private readonly BitReader _reader = new();

    public void Reset(double now)
    {
        ClearOffer();
        _rejection = null;
        _started = now;
    }

    public void Connected(double now) => _started = now;
    public void ConsumeWelcome() => _welcomeConsumed = true;

    public void ClearOffer()
    {
        _accepted = null;
        _welcomeConsumed = false;
    }

    public bool TryOffer(in WorldOffer offer, double now, out string? rejection)
    {
        rejection = null;
        if (offer.OfferId == 0 || !NetConvert.TryResolveEnvironment(offer.Zone, offer.Environment, out _, out _, out _))
        {
            rejection = "The server offered an unknown or incompatible physical environment or qualification contract.";
            return false;
        }
        if (_accepted is { } prior)
        {
            if (prior.OfferId == offer.OfferId)
            {
                if (!NetConvert.SameEnvironment(prior.Environment, offer.Environment) || !NetConvert.SameZone(prior.Zone, offer.Zone))
                    rejection = "The server changed an existing world offer.";
                // Reliable delivery needs no application-level retransmission, and duplicates cannot restart a world.
                return false;
            }
            if (!_welcomeConsumed)
            {
                rejection = "The server replaced a world offer before completing its handshake.";
                return false;
            }
        }
        _accepted = offer;
        _welcomeConsumed = false;
        _started = now;
        return true;
    }

    public bool ValidateWelcome(in Welcome welcome, out string? rejection)
    {
        rejection = null;
        if (_accepted is not { } offer || welcome.OfferId != offer.OfferId ||
            !NetConvert.SameEnvironment(welcome.Environment, offer.Environment) || !NetConvert.SameZone(welcome.Zone, offer.Zone))
        {
            rejection = "Welcome does not match the world offer accepted on this connection.";
            return false;
        }
        return !_welcomeConsumed;
    }

    public string? Timeout(double now, ClientState state) =>
        state == ClientState.Joining && now - _started >= Protocol.WorldHandshakeTimeoutSeconds
            ? "World compatibility confirmation timed out." : null;

    public void Reject(string reason)
    {
        _rejection = reason;
        ClearOffer();
    }

    public static bool IsHandshake(uint type) => type == WorldOffer.TypeId || type == Welcome.TypeId;

    public static bool AdmitPacket(uint type, int bytes, DeliveryMethod method, ClientState state, out string? rejection)
    {
        rejection = null;
        if (IsHandshake(type) && (bytes > Protocol.MaxWorldHandshakeBytes || method != DeliveryMethod.ReliableOrdered))
        {
            rejection = "Invalid world handshake packet.";
            return false;
        }
        // No replication or module callback may publish the offered world before Welcome validates it.
        return state == ClientState.Playing || IsHandshake(type);
    }

    public (GoodbyeReason? Goodbye, string Reason) Disconnected(DisconnectInfo info, GoodbyeReason? previousGoodbye)
    {
        GoodbyeReason? goodbye = previousGoodbye;
        string reason = info.Reason.ToString();
        if (info.AdditionalData.AvailableBytes > 0)
        {
            byte[] data = info.AdditionalData.GetRemainingBytes();
            _reader.Reset(data, 0, data.Length);
            if (_reader.ReadBits(8) == Goodbye.TypeId)
            {
                Goodbye bye = Goodbye.Read(_reader);
                if (!_reader.Failed)
                {
                    goodbye = bye.Reason;
                    reason = $"{bye.Reason}: {bye.Text}";
                }
            }
        }
        return _rejection != null ? (GoodbyeReason.EnvironmentMismatch, _rejection) : (goodbye, reason);
    }
}
