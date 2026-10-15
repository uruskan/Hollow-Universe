using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Net.Protocol;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Targets;

namespace Hollow.Client;

/// <summary>
/// A fire as this client knows it (IW4, IRON_WAKE.md §7): what left it (the round or explosive: its looks and sounds) and
/// whose, its radius, what holds it and its middle's place there, and where it burns now (its holder's pose this frame: it
/// rides a ship's deck), from the server tick it was lit at to the one it burns out at.
/// </summary>
public sealed class ClientFire
{
    internal ClientFire(EntityId id)
    {
        Id = id;
    }

    public EntityId Id { get; }

    public ItemDef Item { get; internal set; } = null!;

    /// <summary>Whose (a character's net ID; 0 for none or gone).</summary>
    public uint Owner { get; internal set; }

    public float Radius { get; internal set; }

    /// <summary>What holds it and its middle's place and turn there (+Y the surface's normal).</summary>
    public SupportLink Link { get; internal set; }

    /// <summary>Where it burns now, in the zone frame (its holder's pose this frame; where the server last said, while its holder isn't known here).</summary>
    public TargetPose Pose { get; internal set; }

    /// <summary>Its middle now and the surface's normal there.</summary>
    public Vector3 Position => Pose.Position;

    public Vector3 Normal => Vector3.Transform(Vector3.UnitY, Pose.Rotation);

    /// <summary>The server ticks it was lit at and burns out at.</summary>
    public uint StartTick { get; internal set; }

    public uint EndTick { get; internal set; }

    /// <summary>Raised each time the server tells of it again (it fell onto something else).</summary>
    public uint Revision { get; internal set; }

    /// <summary>Seconds it has left to burn at a server tick.</summary>
    public double SecondsLeft(double serverTick) => Math.Max(0, (EndTick - serverTick) / GameClient.TickRate);

    public override string ToString() => $"fire {Id} of {Item.Key} at {Position}, {Radius:0.0} m, on {Link.Support}";
}

// Fires in the client (IW4, IRON_WAKE.md §7): those within range as the server tells them, each placed every frame where its
// holder is now, for drawing and sound; the views hear one start (lit, or come into range: compare its start tick with
// now), fall onto something else, and end (gone out, leaving its scorch, or out of range).
public sealed class FireModule : ClientModule
{
    private readonly Dictionary<EntityId, ClientFire> _fires = new();
    private readonly List<EntityId> _stale = new();
    private TargetModule? _targetModule;
    private TargetModule Targets => _targetModule ??= Client.Require<TargetModule>();

    /// <summary>The fires in range.</summary>
    public Dictionary<EntityId, ClientFire>.ValueCollection All => _fires.Values;

    public ClientFire? FireOf(EntityId id) => _fires.GetValueOrDefault(id);

    /// <summary>A fire was lit or came into range (or, <c>moved</c>, fell onto something else).</summary>
    public event Action<ClientFire, bool>? Started;

    /// <summary>A fire ended: gone out (<c>true</c>: its scorch is left) or out of range.</summary>
    public event Action<ClientFire, bool>? Ended;

    protected internal override void Init(MessageHandlers<GameClient> handlers)
    {
        handlers
            .On<FireStarted>((_, in m) => OnFireStarted(m))
            .On<FireEnded>((_, in m) => OnFireEnded(m));
    }

    // Every frame: each fire where its holder is now; one the server never ended long after its time is forgotten.
    protected internal override void Update(double delta)
    {
        _stale.Clear();
        double now = ServerTickNow;
        foreach (ClientFire fire in _fires.Values)
        {
            if (Targets.TryPose(fire.Link, out TargetPose pose))
            {
                fire.Pose = pose;
            }
            if (now > fire.EndTick + 5 * GameClient.TickRate)
            {
                _stale.Add(fire.Id);
            }
        }
        foreach (EntityId id in _stale)
        {
            if (_fires.Remove(id, out ClientFire? fire))
            {
                Ended?.Invoke(fire, false);
            }
        }
    }

    private void OnFireStarted(in FireStarted m)
    {
        if (Content.Item((int)Math.Min(m.Item, ushort.MaxValue)) is not { } item || !float.IsFinite(m.X) || !float.IsFinite(m.Y) || !float.IsFinite(m.Z) ||
            !float.IsFinite(m.LocalX) || !float.IsFinite(m.LocalY) || !float.IsFinite(m.LocalZ))
        {
            return;
        }
        var id = new EntityId(m.Id);
        bool moved = _fires.TryGetValue(id, out ClientFire? fire);
        if (fire == null)
        {
            fire = new ClientFire(id);
            _fires.Add(id, fire);
        }
        var turn = new Quaternion(m.RotationX, m.RotationY, m.RotationZ, m.RotationW);
        turn = turn.LengthSquared() > 1e-6f ? Quaternion.Normalize(turn) : Quaternion.Identity;
        var normal = new Vector3(m.NormalX, m.NormalY, m.NormalZ);
        var at = new Vector3(m.X, m.Y, m.Z);
        fire.Item = item;
        fire.Owner = m.Owner;
        fire.Radius = m.Radius;
        fire.Link = new SupportLink(NetConvert.ToRef(m.Support), new Vector3(m.LocalX, m.LocalY, m.LocalZ), turn);
        fire.StartTick = m.StartTick;
        fire.EndTick = m.EndTick;
        fire.Pose = Targets.TryPose(fire.Link, out TargetPose pose) ? pose
            : new TargetPose(at, Geometry.Upright(normal.LengthSquared() > 0.25f ? Vector3.Normalize(normal) : Frame.UpAt(at), 0));
        fire.Revision++;
        Started?.Invoke(fire, moved);
    }

    /// <summary>The fires of the last zone go with it.</summary>
    public override void Dispose() => _fires.Clear();

    private void OnFireEnded(in FireEnded m)
    {
        if (_fires.Remove(new EntityId(m.Id), out ClientFire? fire))
        {
            Ended?.Invoke(fire, m.Out);
        }
    }
}

/// <summary>The accessor on the client (GROUND_SYSTEMS.md §12.5, K11).</summary>
public static class FireModuleAccess
{
    public static FireModule Fires(this GameClient client) => client.Require<FireModule>();
}
