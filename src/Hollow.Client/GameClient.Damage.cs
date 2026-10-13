using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Net.Protocol;
using Hollow.Simulation;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;

namespace Hollow.Client;

/// <summary>
/// Something in range was destroyed (GROUND.md §6.4.2), for break-apart pieces, debris and the sound of it breaking.
/// <see cref="Receiver"/> is the kind of thing (deployable, building, ship) and <see cref="Kind"/> what it was, which says
/// what <see cref="Key"/> names: a deployable's or a world prop's model key, a building piece's key (with its
/// <see cref="Grade"/>), a door's key, the claim device's item key, a ship's key. <see cref="Id"/> is its entity ID (a
/// piece, door or claim device by the building's IDs, a deployable or prop by its thing's <c>RemoteThing.Id</c>).
/// <see cref="Position"/> and <see cref="Rotation"/> are where it stood as it was drawn (zone frame: a piece's, claim
/// device's or ship's own transform; a door's frame piece's, with <see cref="Open"/>; a deployable's or prop's base on the
/// ground turned as <c>NatureView.Upright</c> turns it). <see cref="Direction"/> is the blow's (unit, zero for a
/// collapse, decay or demolition); <see cref="Cause"/> what did it, <see cref="Attacker"/> who (a character's net ID, 0
/// for none) and <see cref="Item"/> with what.
/// </summary>
public readonly record struct DestroyedEvent(HitTarget Receiver, DestroyedKind Kind, EntityId Id, string Key, int Grade, Vector3 Position,
    Quaternion Rotation, Vector3 Direction, DestroyCause Cause, bool Open, uint Attacker, ItemDef? Item, double ClientSeconds);

// What the damage pipeline destroyed in range (GROUND.md §6.4.2), for the visual layer and sound.
public sealed partial class GameClient
{
    private readonly List<DestroyedEvent> _destroyed = new();

    /// <summary>What was destroyed in range in the last two seconds, newest last (for views that poll rather than listen).</summary>
    public IReadOnlyList<DestroyedEvent> Destructions => _destroyed;

    /// <summary>
    /// Something in range was destroyed: break it apart where it stood. The event carries all it takes to draw it: the
    /// thing may already be gone from the client's lists, or go a moment later.
    /// </summary>
    public event Action<DestroyedEvent>? Destroyed;

    private void UpdateDestroyed()
    {
        double now = ClockSeconds;
        _destroyed.RemoveAll(d => now - d.ClientSeconds > 2);
    }

    private bool ReceiveDestroyed(uint type)
    {
        if (type != ThingDestroyed.TypeId)
        {
            return false;
        }
        ThingDestroyed m = ThingDestroyed.Read(_reader);
        if (_reader.Failed)
        {
            return true;
        }
        var rotation = new Quaternion(m.RotationX, m.RotationY, m.RotationZ, m.RotationW);
        var direction = new Vector3(m.DirectionX, m.DirectionY, m.DirectionZ);
        var e = new DestroyedEvent(m.Receiver, m.Kind, new EntityId(m.Id), m.Key ?? "", m.Grade, new Vector3(m.X, m.Y, m.Z),
            rotation.LengthSquared() > 0.25f ? Quaternion.Normalize(rotation) : Quaternion.Identity,
            direction.LengthSquared() > 0.25f ? Vector3.Normalize(direction) : Vector3.Zero, m.Cause, m.Open, m.Attacker,
            Content.Item((int)Math.Min(m.Item, ushort.MaxValue)), ClockSeconds);
        _destroyed.Add(e);
        Destroyed?.Invoke(e);
        return true;
    }
}
