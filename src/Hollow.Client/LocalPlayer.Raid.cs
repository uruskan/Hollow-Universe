using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Net.Bits;
using Hollow.Net.Protocol;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;

namespace Hollow.Client;

// The player's raiding (GROUND.md §6.6, G5): throwing and placing explosives, and the raid alerts of the bases they are
// authorised on (a dud is lit again with a verb, Interact).
public sealed partial class LocalPlayer
{
    private readonly List<RaidAlertView> _raidAlerts = new();

    /// <summary>Raid alerts this session, oldest first: those that waited while the player was away (at login), then live ones.</summary>
    public IReadOnlyList<RaidAlertView> RaidAlerts => _raidAlerts;

    /// <summary>A raid alert came: a base the player is authorised on is being raided (or was, while they were away).</summary>
    public event Action<RaidAlertView>? RaidAlertReceived;

    /// <summary>
    /// Throws the explosive in a belt slot (a timed charge, bag charge or tin grenade) along a view (yaw and pitch in
    /// radians, as the input carries them; the camera's), gently with <paramref name="lob"/>. It leaves the belt at once;
    /// the explosive appears in <see cref="GameClient.Explosives"/> when the server says so.
    /// </summary>
    public uint Throw(int slot, float yaw, float pitch, bool lob = false)
    {
        ushort expected = ItemAt(ContainerRef.Belt, slot);
        return Intend(new Pending(p => p.TakeThrowable(slot, expected)), w => new ThrowIntent
        {
            Seq = _nextSeq, Slot = (byte)Math.Clamp(slot, 0, 7), Expected = expected, Yaw = Quantize.Angle16(yaw), Pitch = Quantize.Angle16(pitch), Lob = lob,
        }.WriteMessage(w));
    }

    private bool TakeThrowable(int slot, ushort expected) =>
        Content.Item(expected)?.Throwable != null && InventoryRules.Remove(Inventory.Belt, slot, 1, expected, out _) == Simulation.OutcomeCode.Done;

    internal void OnRaidAlert(in RaidAlertNet a, bool whileAway)
    {
        var alert = new RaidAlertView(new EntityId(a.Base), new Vector3(a.X, a.Y, a.Z), DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Min(a.FirstUnixMs, long.MaxValue / 2)),
            DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Min(a.LastUnixMs, long.MaxValue / 2)), (int)Math.Min(a.Hits, int.MaxValue),
            (int)Math.Min(a.Destroyed, int.MaxValue), a.What ?? "", Content.Item((int)Math.Min(a.Item, ushort.MaxValue)), whileAway);
        _raidAlerts.Add(alert);
        if (_raidAlerts.Count > 200)
        {
            _raidAlerts.RemoveRange(0, _raidAlerts.Count - 200);
        }
        RaidAlertReceived?.Invoke(alert);
    }
}
