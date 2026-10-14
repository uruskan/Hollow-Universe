using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Net.Bits;
using Hollow.Simulation.Movement;
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
    /// Throws the explosive in a belt slot (a timed charge, bag charge or tin grenade) along a view (a heading from local
    /// north and a pitch in radians where the player stands; the camera's), gently with <paramref name="lob"/>. It leaves
    /// the belt at once; the explosive appears in <see cref="GameClient.Explosives"/> when the server says so.
    /// </summary>
    public uint Throw(int slot, float yaw, float pitch, bool lob = false) =>
        Throw(slot, Facing.View(yaw, pitch, Up?.Invoke() ?? System.Numerics.Vector3.UnitY), lob);

    /// <summary>Throws along a view direction in the zone's frame (docs/WHOLE_WORLD.md §5).</summary>
    public uint Throw(int slot, System.Numerics.Vector3 view, bool lob = false)
    {
        ushort expected = ItemAt(ContainerRef.Belt, slot);
        (ushort u, ushort v) = Quantize.Octahedral(view);
        return Intend(new Pending(p => p.TakeThrowable(slot, expected)), w => new ThrowIntent
        {
            Seq = _nextSeq, Slot = (byte)Math.Clamp(slot, 0, 7), Expected = expected, ViewU = u, ViewV = v, Lob = lob,
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
