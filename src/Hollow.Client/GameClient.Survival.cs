using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hollow.Net.Bits;
using Hollow.Net.Protocol;
using Hollow.Simulation;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Nature;
using Hollow.Simulation.World;
using LiteNetLib;

namespace Hollow.Client;

// The survival game in the client (GROUND.md §4.3, §6): the local player's state and intents (LocalPlayer), static
// things, natural objects as players changed them, projectiles in flight. What the interact key acts on is the one
// targeting query's (GameClient.Interaction.cs).
public sealed partial class GameClient
{
    private readonly Dictionary<uint, RemoteThing> _things = new();
    private readonly Dictionary<EntityId, RemoteThing> _thingsById = new();
    private readonly List<ClientProjectile> _projectiles = new();
    private bool _primaryLatched;

    /// <summary>The game's data (the same as the server's: checked at connect).</summary>
    public GameContent Content { get; }

    /// <summary>The player's own survival state and actions: the UI binds to this.</summary>
    public LocalPlayer Local { get; }

    /// <summary>Where the natural objects are, and what players did to them (as the server says).</summary>
    public INaturalObjects? Nature { get; private set; }

    public NaturalState? Natural { get; private set; }

    /// <summary>Static things in range: deployables, bags of dropped items, corpses.</summary>
    public IEnumerable<RemoteThing> Things => _things.Values;

    public RemoteThing? ThingOf(EntityId id) => _thingsById.GetValueOrDefault(id);

    /// <summary>Projectiles in flight (and for a moment after they stop), for drawing.</summary>
    public IReadOnlyList<ClientProjectile> Projectiles => _projectiles;

    /// <summary>Seconds on the client's clock (what projectiles and countdowns are timed by).</summary>
    public double ClockSeconds => _clock.Elapsed.TotalSeconds;

    /// <summary>Raised when a character's look (clothing) changes or arrives.</summary>
    public event Action<RemoteEntity>? LookChanged;

    private bool SendReliable(Action<BitWriter> write) => SendWith(write, DeliveryMethod.ReliableOrdered);

    // Shots and reloads: reliable but unordered, so one lost and resent holds nothing else up (they carry their tick and
    // sequence; the server puts them in order).
    private bool SendUnordered(Action<BitWriter> write) => SendWith(write, DeliveryMethod.ReliableUnordered);

    private bool SendWith(Action<BitWriter> write, DeliveryMethod method)
    {
        if (_peer == null || State != ClientState.Playing)
        {
            return false;
        }
        _writer.Reset();
        write(_writer);
        _peer.Send(_writer.Data, method);
        return true;
    }

    // How far behind this input's tick the player sees others: the input's tick against the remote render tick.
    private byte ViewDelay(uint tick)
    {
        if (!_remoteClockStarted)
        {
            return 0;
        }
        double behind = tick - RenderTick;
        return (byte)Math.Clamp(Math.Round(behind * 4), 0, 255);
    }

    private void StartSurvival()
    {
        Nature = Zone!.CreateNature(_surface!, Zone.CreateFrame(_surface!), Content);
        if (_options.SharedNature != null)
        {
            Natural = _options.SharedNature(Zone, _surface!);
        }
        else
        {
            Natural = new NaturalState(Content, Nature);
            _ = new NaturalBodies(_predictor!.World, Natural);
        }
        StartBuilding();
        StartTargets();
    }

    private void UpdateSurvival()
    {
        Local.ServerTick = _remoteClock;
        Local.ClockSeconds = ClockSeconds;
        Local.WorkbenchLevel = ThingOf(new EntityId(BenchNear().Id))?.Deployable(Content)?.Workbench ?? 0;
        Local.StationNear ??= StationNear;
        Local.Update();
        Local.UpdateBuilding();
        UpdateRaid();
        UpdateDestroyed();
        UpdateArrows();
        double now = ClockSeconds;
        _projectiles.RemoveAll(p => (p.EndedAt != null && now - p.EndedTime > 1.0) || now - p.LaunchedAt > 8);
    }

    // The eye, for the reach of stations: as the server measures it (Zone.EyeOf).
    private Vector3 StationEye => Character.Position + Frame.UpAt(Character.Position) * EyeHeight;

    // The nearest deployable of a kind within a station's reach, as the server's Zone.StationNear finds a ship's vehicle
    // workbench.
    private ulong StationNear(DeployableDef def)
    {
        if (!HasCharacter)
        {
            return 0;
        }
        Vector3 eye = StationEye;
        float reach = Content.Survival.StationReach;
        ulong best = 0;
        float bestDistance = float.MaxValue;
        foreach (RemoteThing thing in _things.Values)
        {
            if (thing.Kind != StaticKind.Deployable || thing.Deployable(Content) != def)
            {
                continue;
            }
            float distance = Vector3.Distance(Geometry.NearestOnBox(thing.Position, Frame.UpAt(thing.Position), thing.Yaw, def.HalfExtents, eye), eye);
            if (distance <= reach && distance < bestDistance)
            {
                (best, bestDistance) = (thing.Id.Value, distance);
            }
        }
        return best;
    }

    private void RemoveThing(uint netId)
    {
        if (_things.Remove(netId, out RemoteThing? thing))
        {
            _thingsById.Remove(thing.Id);
            ThingBodyRemoved(thing); // G7
        }
    }

    private void ReceiveSurvival(uint type)
    {
        switch (type)
        {
            case ThingSpawn.TypeId:
            {
                ThingSpawn m = ThingSpawn.Read(_reader);
                if (_reader.Failed)
                {
                    return;
                }
                RemoveThing(m.NetId);
                var thing = new RemoteThing(m.NetId)
                {
                    Id = new EntityId(m.EntityId),
                    Kind = m.Kind,
                    Look = (ushort)Math.Min(m.Look, ushort.MaxValue),
                    Position = new Vector3(m.X, m.Y, m.Z),
                    Yaw = Quantize.FromAngle16(m.Yaw),
                    Health = m.Health / 255f,
                    Count = (int)Math.Min(m.Count, int.MaxValue),
                    Flags = m.Flags,
                    Name = m.Name ?? "",
                    Capabilities = new Simulation.Capabilities.CapabilitySet(m.Capabilities),
                };
                _things[m.NetId] = thing;
                _thingsById[thing.Id] = thing;
                ThingBodyAdded(thing); // G7
                return;
            }
            case ThingUpdate.TypeId:
            {
                ThingUpdate m = ThingUpdate.Read(_reader);
                if (!_reader.Failed && _things.TryGetValue(m.NetId, out RemoteThing? thing))
                {
                    thing.Health = m.Health / 255f;
                    thing.Count = (int)Math.Min(m.Count, int.MaxValue);
                    thing.Flags = m.Flags;
                    thing.Revision++;
                }
                return;
            }
            case CharacterLook.TypeId:
            {
                CharacterLook m = CharacterLook.Read(_reader);
                if (!_reader.Failed)
                {
                    RemoteEntity remote = Remote(m.NetId);
                    remote.Wear = (m.Wear ?? Array.Empty<WornItem>()).Select(w => (ushort)Math.Min(w.Item, ushort.MaxValue)).ToArray();
                    remote.Body = Math.Min(m.Body, (byte)1);
                    LookChanged?.Invoke(remote);
                }
                return;
            }
            case NaturalStates.TypeId:
            {
                NaturalStates m = NaturalStates.Read(_reader);
                if (!_reader.Failed && Natural != null && Nature != null)
                {
                    foreach (NaturalNet n in m.States ?? Array.Empty<NaturalNet>())
                    {
                        ApplyNatural(n);
                    }
                }
                return;
            }
            case ProjectileLaunch.TypeId:
            {
                ProjectileLaunch m = ProjectileLaunch.Read(_reader);
                if (_reader.Failed || Zone == null)
                {
                    return;
                }
                var origin = new Vector3(m.X, m.Y, m.Z);
                ItemDef? ammo = Content.Item((int)Math.Min(m.Ammo, ushort.MaxValue));
                if (m.Seq != 0 && m.Shooter == NetId && _projectiles.FirstOrDefault(p => p.Own && p.Seq == m.Seq && p.Id == 0) is { } mine)
                {
                    // Our own shot, drawn since we fired it: now it has the server's ID (its end will name it).
                    mine.Id = m.Id;
                    return;
                }
                FirearmStats? gun = ammo?.Bullet is { } round ? Content.Firearms.FirstOrDefault(g => g.Calibre == round.Calibre) : null;
                float gravity = (float)(Zone.SurfaceGravity * 9.80665) * (float)(ammo?.Ammo?.Gravity ?? gun?.Gravity ?? 1);
                _projectiles.Add(new ClientProjectile
                {
                    Id = m.Id,
                    Shooter = m.Shooter,
                    Ammo = ammo?.Id ?? 0,
                    Origin = origin,
                    Velocity = new Vector3(m.VelocityX, m.VelocityY, m.VelocityZ),
                    Gravity = -Frame.UpAt(origin) * gravity,
                    Drag = (float)(gun?.Drag ?? 0),
                    Seq = m.Seq,
                    Silenced = m.Silenced,
                    LaunchedAt = ClockSeconds,
                });
                return;
            }
            case ProjectileEnd.TypeId:
            {
                ProjectileEnd m = ProjectileEnd.Read(_reader);
                if (!_reader.Failed && _projectiles.FirstOrDefault(p => p.Id == m.Id) is { } projectile)
                {
                    projectile.EndedAt = new Vector3(m.X, m.Y, m.Z);
                    projectile.EndedTime = ClockSeconds;
                    projectile.EndTarget = m.Target;
                }
                return;
            }
            case IntentResult.TypeId:
            {
                IntentResult m = IntentResult.Read(_reader);
                if (!_reader.Failed)
                {
                    Local.OnResult(m);
                }
                return;
            }
            case ContainerContents.TypeId:
            {
                ContainerContents m = ContainerContents.Read(_reader);
                if (!_reader.Failed)
                {
                    Local.OnContents(m);
                }
                return;
            }
            case ContainerSlots.TypeId:
            {
                ContainerSlots m = ContainerSlots.Read(_reader);
                if (!_reader.Failed)
                {
                    Local.OnSlots(m);
                }
                return;
            }
            case LootState.TypeId:
            {
                LootState m = LootState.Read(_reader);
                if (!_reader.Failed)
                {
                    DeployableDef? device = ThingOf(new EntityId(m.Target))?.Deployable(Content);
                    Local.OnLoot(m, device is { IsMachine: true } ? device : null);
                }
                return;
            }
            case VitalsUpdate.TypeId:
            {
                VitalsUpdate m = VitalsUpdate.Read(_reader);
                if (!_reader.Failed)
                {
                    Local.OnVitals(m);
                }
                return;
            }
            case CraftQueueState.TypeId:
            {
                CraftQueueState m = CraftQueueState.Read(_reader);
                if (!_reader.Failed)
                {
                    Local.OnCraftQueue(m);
                }
                return;
            }
            case HitMarker.TypeId:
            {
                HitMarker m = HitMarker.Read(_reader);
                if (!_reader.Failed)
                {
                    Local.OnHitMarker(m);
                }
                return;
            }
            case ItemsChanged.TypeId:
            {
                ItemsChanged m = ItemsChanged.Read(_reader);
                if (!_reader.Failed)
                {
                    Local.OnItemsChanged(m);
                }
                return;
            }
            case DamageTaken.TypeId:
            {
                DamageTaken m = DamageTaken.Read(_reader);
                if (!_reader.Failed)
                {
                    Local.OnDamage(m);
                }
                return;
            }
            case CraftFinished.TypeId:
            {
                CraftFinished m = CraftFinished.Read(_reader);
                if (!_reader.Failed)
                {
                    Local.OnCraftFinished(m);
                }
                return;
            }
            case GunUpdate.TypeId:
            {
                GunUpdate m = GunUpdate.Read(_reader);
                if (!_reader.Failed)
                {
                    Local.Gun.OnUpdate(m);
                }
                return;
            }
            case LifeState.TypeId:
            {
                LifeState m = LifeState.Read(_reader);
                if (!_reader.Failed)
                {
                    Local.ClockSeconds = ClockSeconds;
                    Local.OnLife(m);
                }
                return;
            }
            default:
                if (!ReceiveRaid(type) && !ReceiveWorld(type) && !ReceiveDestroyed(type) && !ReceiveArrows(type))
                {
                    ReceiveBuilding(type);
                }
                return;
        }
    }

    /// <summary>A natural object's last hit at most this many ticks old when sent is news: the views play its effects.</summary>
    public const int FreshHitTicks = 30;

    private void ApplyNatural(in NaturalNet n)
    {
        if (n.Health == 255 && !n.Depleted && !n.HasWeakSpot)
        {
            Natural!.Apply(n.Id, null);
            return;
        }
        float max = 1;
        if (Nature!.TryGet(n.Id, out NaturalObject o) && Content.Natural(o.Kind) is { IsHarvested: true } kind)
        {
            max = NaturalShapes.MaxHealth(o, kind);
        }
        // A fresh hit counts one more for the views (its effects); an old one (an area coming into range) doesn't.
        byte seen = Natural!.TryGetOverride(n.Id, out NaturalOverride was) ? was.SeenHits : (byte)0;
        Natural!.Apply(n.Id, new NaturalOverride
        {
            Health = n.Health / 255f * max, HasWeakSpot = n.HasWeakSpot, WeakSpotYaw = n.WeakSpotYaw, WeakSpotLift = n.WeakSpotLift, Depleted = n.Depleted,
            Streak = n.Streak, LastHit = n.HitOffset, SeenHits = n.HitAge <= FreshHitTicks ? (byte)(seen + 1) : seen,
        });
    }

    /// <summary>
    /// Where a view from the eye meets the ground within <paramref name="reach"/> (for placing a deployable), or null;
    /// the surface's own height, so it agrees with where the server will put it.
    /// </summary>
    public Vector3? AimGround(Vector3 eye, Vector3 direction, float reach)
    {
        if (_surface == null || Zone == null)
        {
            return null;
        }
        GroundProbe probe = new(_surface, Frame);
        float previous = probe.Height(eye);
        if (previous < 0)
        {
            return null;
        }
        const float step = 0.25f;
        for (float t = step; t <= reach + 1e-3f; t += step)
        {
            if (probe.Height(eye + direction * t) < 0)
            {
                float lo = t - step, hi = t;
                for (int i = 0; i < 10; i++)
                {
                    float mid = (lo + hi) / 2;
                    if (probe.Height(eye + direction * mid) < 0)
                    {
                        hi = mid;
                    }
                    else
                    {
                        lo = mid;
                    }
                }
                return eye + direction * hi;
            }
        }
        return null;
    }

    private readonly struct GroundProbe(Surface.ISurface surface, Surface.ZoneFrame frame)
    {
        public float Height(Vector3 point)
        {
            Core.Vector3D body = frame.ToBody(point);
            double r = body.Length;
            return (float)(r - (surface.Radius + surface.HeightAt(body / r)));
        }
    }

    // Our own shot: drawn at once, from the eye along the predicted direction (the server's ProjectileLaunch then names it).
    private void OnOwnShot(ShotEvent shot)
    {
        if (Zone == null)
        {
            return;
        }
        FirearmStats gun = shot.Weapon.Firearm!;
        _projectiles.Add(new ClientProjectile
        {
            Shooter = NetId,
            Ammo = shot.Round.Id,
            Origin = shot.Origin,
            Velocity = shot.Direction * shot.Speed,
            Gravity = -Frame.UpAt(shot.Origin) * (float)(Zone.SurfaceGravity * 9.80665 * gun.Gravity),
            Drag = (float)gun.Drag,
            Seq = shot.Seq,
            Own = true,
            Silenced = shot.Silenced,
            LaunchedAt = ClockSeconds,
        });
    }
}
