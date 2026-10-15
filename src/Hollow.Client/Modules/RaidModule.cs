using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Net.Protocol;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Raid;

namespace Hollow.Client;

// Raiding in the client (GROUND.md §6.6, G5): the explosives in range as the server has them (drawn on their arcs, with
// their fuses), the detonations, for the visual layer and sound, and raid alerts (LocalPlayer).
public sealed class RaidModule : ClientModule
{
    private readonly Dictionary<EntityId, ClientExplosive> _explosives = new();
    private readonly List<DetonationEvent> _detonations = new();

    /// <summary>Explosives in range: thrown, stuck, resting, duds.</summary>
    public Dictionary<EntityId, ClientExplosive>.ValueCollection Explosives => _explosives.Values;

    public ClientExplosive? ExplosiveOf(EntityId id) => _explosives.GetValueOrDefault(id);

    /// <summary>Explosions of the last two seconds, newest last (for views that poll rather than listen).</summary>
    public IReadOnlyList<DetonationEvent> Detonations => _detonations;

    /// <summary>An explosive appeared or changed (thrown, bounced, stuck, at rest, a dud, lit again): sound and model.</summary>
    public event Action<ClientExplosive>? ExplosiveChanged;

    /// <summary>An explosive went out of range or away without a blast.</summary>
    public event Action<ClientExplosive>? ExplosiveRemoved;

    /// <summary>An explosion: the flash, smoke, debris and the sound, by its item and radius.</summary>
    public event Action<DetonationEvent>? Detonated;

    protected internal override void Init(MessageHandlers<GameClient> handlers)
    {
        handlers
            .On<ExplosiveUpdate>((_, in m) => OnExplosiveUpdate(m))
            .On<ExplosiveGone>((_, in m) => OnExplosiveGone(m))
            .On<Detonation>((_, in m) => OnDetonation(m))
            .On<RaidAlertMessage>((_, in m) => Local.OnRaidAlert(m.Alert, whileAway: false))
            .On<RaidAlertList>((_, in m) => OnRaidAlertList(m));
    }

    protected internal override void Update(double delta)
    {
        double now = ClockSeconds;
        _detonations.RemoveAll(d => now - d.ClientSeconds > 2);
    }

    private void OnExplosiveUpdate(in ExplosiveUpdate m)
    {
        if (Content.Item((int)Math.Min(m.Item, ushort.MaxValue)) is not { } item || Zone == null)
        {
            return;
        }
        var id = new EntityId(m.Id);
        if (!_explosives.TryGetValue(id, out ClientExplosive? e))
        {
            e = new ClientExplosive { Id = id };
            _explosives.Add(id, e);
        }
        var start = new Vector3(m.X, m.Y, m.Z);
        var normal = new Vector3(m.NormalX, m.NormalY, m.NormalZ);
        e.Item = item;
        e.Owner = m.Owner;
        e.State = m.State;
        e.Dud = m.Dud;
        e.ArcStart = start;
        e.ArcVelocity = new Vector3(m.VelocityX, m.VelocityY, m.VelocityZ);
        e.Normal = normal.LengthSquared() > 0.25f ? Vector3.Normalize(normal) : Frame.UpAt(start);
        e.StateTick = m.StateTick;
        e.FuseEndTick = m.FuseEndTick;
        e.FuseTicks = (int)Math.Min(m.FuseTicks, int.MaxValue);
        e.Gravity = -Frame.UpAt(start) * (float)(Zone.SurfaceGravity * 9.80665);
        e.Centre = Frame.ToLocal(Core.Vector3D.Zero);
        e.Revision++;
        ExplosiveChanged?.Invoke(e);
    }

    private void OnExplosiveGone(in ExplosiveGone m)
    {
        if (_explosives.Remove(new EntityId(m.Id), out ClientExplosive? e))
        {
            ExplosiveRemoved?.Invoke(e);
        }
    }

    private void OnDetonation(in Detonation m)
    {
        var id = new EntityId(m.Id);
        if (!id.IsNone)
        {
            _explosives.Remove(id);
        }
        var position = new Vector3(m.X, m.Y, m.Z);
        var normal = new Vector3(m.NormalX, m.NormalY, m.NormalZ);
        var blast = new DetonationEvent(id, Content.Item((int)Math.Min(m.Item, ushort.MaxValue)), position,
            normal.LengthSquared() > 0.25f ? Vector3.Normalize(normal) : Frame.UpAt(position), m.Radius, m.Target, m.Round, m.Owner, ClockSeconds);
        _detonations.Add(blast);
        Detonated?.Invoke(blast);
    }

    private void OnRaidAlertList(in RaidAlertList m)
    {
        foreach (RaidAlertNet alert in m.Alerts ?? Array.Empty<RaidAlertNet>())
        {
            Local.OnRaidAlert(alert, whileAway: true);
        }
    }
}

/// <summary>The accessor on the client (GROUND_SYSTEMS.md §12.5, K11).</summary>
public static class RaidModuleAccess
{
    public static RaidModule Raid(this GameClient client) => client.Require<RaidModule>();
}
