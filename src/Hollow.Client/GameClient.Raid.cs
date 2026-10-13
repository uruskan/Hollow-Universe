using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Net.Protocol;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Raid;

namespace Hollow.Client;

// Raiding in the client (GROUND.md §6.5, G5): the explosives in range as the server has them (drawn on their arcs, with
// their fuses), the detonations, for the visual layer and sound, and raid alerts (LocalPlayer).
public sealed partial class GameClient
{
    private readonly Dictionary<EntityId, ClientExplosive> _explosives = new();
    private readonly List<DetonationEvent> _detonations = new();

    /// <summary>Explosives in range: thrown, stuck, resting, duds.</summary>
    public IReadOnlyCollection<ClientExplosive> Explosives => _explosives.Values;

    public ClientExplosive? ExplosiveOf(EntityId id) => _explosives.GetValueOrDefault(id);

    /// <summary>Explosions of the last two seconds, newest last (for views that poll rather than listen).</summary>
    public IReadOnlyList<DetonationEvent> Detonations => _detonations;

    /// <summary>The server's tick as the client estimates it now, fractional (explosives' arcs and fuses count by it).</summary>
    public double ServerTickNow => _remoteClock;

    /// <summary>An explosive appeared or changed (thrown, bounced, stuck, at rest, a dud, lit again): sound and model.</summary>
    public event Action<ClientExplosive>? ExplosiveChanged;

    /// <summary>An explosive went out of range or away without a blast.</summary>
    public event Action<ClientExplosive>? ExplosiveRemoved;

    /// <summary>An explosion: the flash, smoke, debris and the sound, by its item and radius.</summary>
    public event Action<DetonationEvent>? Detonated;

    private void UpdateRaid()
    {
        double now = ClockSeconds;
        _detonations.RemoveAll(d => now - d.ClientSeconds > 2);
    }

    private bool ReceiveRaid(uint type)
    {
        switch (type)
        {
            case ExplosiveUpdate.TypeId:
            {
                ExplosiveUpdate m = ExplosiveUpdate.Read(_reader);
                if (_reader.Failed || Content.Item((int)Math.Min(m.Item, ushort.MaxValue)) is not { } item || Zone == null)
                {
                    return true;
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
                e.Revision++;
                ExplosiveChanged?.Invoke(e);
                return true;
            }
            case ExplosiveGone.TypeId:
            {
                ExplosiveGone m = ExplosiveGone.Read(_reader);
                if (!_reader.Failed && _explosives.Remove(new EntityId(m.Id), out ClientExplosive? e))
                {
                    ExplosiveRemoved?.Invoke(e);
                }
                return true;
            }
            case Detonation.TypeId:
            {
                Detonation m = Detonation.Read(_reader);
                if (_reader.Failed)
                {
                    return true;
                }
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
                return true;
            }
            case RaidAlertMessage.TypeId:
            {
                RaidAlertMessage m = RaidAlertMessage.Read(_reader);
                if (!_reader.Failed)
                {
                    Local.OnRaidAlert(m.Alert, whileAway: false);
                }
                return true;
            }
            case RaidAlertList.TypeId:
            {
                RaidAlertList m = RaidAlertList.Read(_reader);
                if (!_reader.Failed)
                {
                    foreach (RaidAlertNet alert in m.Alerts ?? Array.Empty<RaidAlertNet>())
                    {
                        Local.OnRaidAlert(alert, whileAway: true);
                    }
                }
                return true;
            }
            default:
                return false;
        }
    }

    /// <summary>A dud within reach of the eye along a direction, for the interact key (light it again).</summary>
    private void AimAtDuds(Vector3 eye, Vector3 direction, float range, Action<float, EntityId, string> consider)
    {
        foreach (ClientExplosive e in _explosives.Values)
        {
            if (!e.Dud || Vector3.DistanceSquared(e.ArcStart, eye) > (range + 1) * (range + 1))
            {
                continue;
            }
            float t = Simulation.Combat.Geometry.RaySphere(eye, direction, range, e.PositionAt(ServerTickNow), 0.35f);
            consider(t, e.Id, e.Item.Name);
        }
    }
}
