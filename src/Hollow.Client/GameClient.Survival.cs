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

/// <summary>What the player is aiming at that the interact key acts on.</summary>
public enum InteractionKind : byte
{
    /// <summary>A pickup or berry bush: pick it.</summary>
    PickNatural,

    /// <summary>A dropped item: pick it up.</summary>
    PickItem,

    /// <summary>A box, device or corpse: open it.</summary>
    Open,

    /// <summary>A sleeping player: loot them.</summary>
    LootSleeper,

    /// <summary>A wounded player: help them up (G4).</summary>
    HelpUp,

    /// <summary>A dud explosive: light it again (G5).</summary>
    Rearm,

    /// <summary>A player close by, awake: invite them to the team the player leads (G7).</summary>
    InviteToTeam,
}

/// <summary>Something within reach the interact key acts on, and how to say so ("E: Pick up Wood pile").</summary>
public readonly record struct Interaction(InteractionKind Kind, ulong NaturalId, EntityId Entity, string Verb, string Name, float Distance);

// The survival game in the client (GROUND.md §4.3, §6): the local player's state and intents (LocalPlayer), static
// things, natural objects as players changed them, projectiles in flight, and what the interact key would act on.
public sealed partial class GameClient
{
    /// <summary>The interact key reaches this far from the eye (survival.json, plus a little for the aim).</summary>
    public float InteractReach => Content.Survival.InteractReach;

    private readonly Dictionary<uint, RemoteThing> _things = new();
    private readonly Dictionary<EntityId, RemoteThing> _thingsById = new();
    private readonly List<ClientProjectile> _projectiles = new();
    private readonly List<NaturalObject> _nearby = new();
    private bool _primaryLatched;

    /// <summary>The game's data (the same as the server's: checked at connect).</summary>
    public GameContent Content { get; }

    /// <summary>The player's own survival state and actions: the UI binds to this.</summary>
    public LocalPlayer Local { get; }

    /// <summary>Where the natural objects are, and what players did to them (as the server says).</summary>
    public INaturalObjects? Nature { get; private set; }

    public NaturalState? Natural { get; private set; }

    /// <summary>Static things in range: deployables, dropped items, corpses.</summary>
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
    }

    private void UpdateSurvival()
    {
        Local.ServerTick = _remoteClock;
        Local.ClockSeconds = ClockSeconds;
        Local.WorkbenchLevel = WorkbenchNear();
        Local.Update();
        Local.UpdateBuilding();
        UpdateRaid();
        double now = ClockSeconds;
        _projectiles.RemoveAll(p => (p.EndedAt != null && now - p.EndedTime > 1.0) || now - p.LaunchedAt > 8);
    }

    private int WorkbenchNear()
    {
        if (!HasCharacter)
        {
            return 0;
        }
        Vector3 at = Character.Position;
        int best = 0;
        foreach (RemoteThing thing in _things.Values)
        {
            if (thing.Deployable(Content) is { Workbench: > 0 } d && d.Workbench > best &&
                Vector3.Distance(thing.Position + Vector3.UnitY * d.HalfExtents.Y, at) <= Simulation.Zone.WorkbenchReach + d.HalfExtents.X)
            {
                best = d.Workbench;
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
                if (!ReceiveRaid(type) && !ReceiveWorld(type))
                {
                    ReceiveBuilding(type);
                }
                return;
        }
    }

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
        Natural!.Apply(n.Id, new NaturalOverride
        {
            Health = n.Health / 255f * max, HasWeakSpot = n.HasWeakSpot, WeakSpotYaw = n.WeakSpotYaw, WeakSpotLift = n.WeakSpotLift, Depleted = n.Depleted,
        });
    }

    /// <summary>
    /// What the interact key would act on from the eye along a direction: the nearest pickup or berry bush, dropped
    /// item, box, device, corpse or sleeper within reach (for the prompt, and for bots).
    /// </summary>
    public Interaction? Aim(Vector3 eye, Vector3 direction, float? reach = null)
    {
        float range = reach ?? InteractReach;
        Interaction? best = null;
        void Consider(float t, InteractionKind kind, ulong natural, EntityId entity, string verb, string name)
        {
            if (t >= 0 && t <= range && (best == null || t < best.Value.Distance))
            {
                best = new Interaction(kind, natural, entity, verb, name, t);
            }
        }
        if (Nature != null && Natural != null)
        {
            _nearby.Clear();
            Nature.Query(eye.X - range - 1, eye.Z - range - 1, eye.X + range + 1, eye.Z + range + 1, _nearby);
            foreach (NaturalObject o in _nearby)
            {
                if (Content.Natural(o.Kind) is { IsPicked: true } kind && !Natural.IsDepleted(o.Id))
                {
                    float r = kind.Radius * o.Scale;
                    float t = Geometry.RaySphere(eye, direction, range, NaturalShapes.Middle(o, kind), r + 0.2f);
                    Consider(t, InteractionKind.PickNatural, o.Id, EntityId.None, kind.Class == NaturalClass.Bush ? "Pick" : "Pick up", kind.Name);
                }
            }
        }
        foreach (RemoteThing thing in _things.Values)
        {
            if (Vector3.DistanceSquared(thing.Position, eye) > (range + 2) * (range + 2))
            {
                continue;
            }
            switch (thing.Kind)
            {
                case StaticKind.DroppedItem:
                    Consider(Geometry.RaySphere(eye, direction, range, thing.Position + Frame.UpAt(thing.Position) * 0.1f, 0.35f),
                        InteractionKind.PickItem, 0, thing.Id, "Pick up", $"{Content.Item(thing.Look)?.Name ?? "item"} ×{thing.Count}");
                    break;
                case StaticKind.Corpse:
                    Consider(Geometry.RaySphere(eye, direction, range, thing.Position + Frame.UpAt(thing.Position) * 0.25f, 0.6f),
                        InteractionKind.Open, 0, thing.Id, "Loot", $"{thing.Name}'s body");
                    break;
                case StaticKind.Prop when thing.Prop(Content) is { Openable: true } prop:
                {
                    // G7: a crate or recycler opens; a barrel is broken instead.
                    DeployableDef d = prop.Deployable;
                    Vector3 up = Frame.UpAt(thing.Position);
                    (Vector3 right, Vector3 forward) = Geometry.Axes(up, thing.Yaw);
                    float t = Geometry.RayBox(eye, direction, range, thing.Position + up * d.HalfExtents.Y, d.HalfExtents + new Vector3(0.1f), right, up, forward);
                    Consider(t, InteractionKind.Open, 0, thing.Id, "Open", prop.Name);
                    break;
                }
                case StaticKind.Deployable when thing.Deployable(Content) is { } d && (d.Slots > 0):
                {
                    Vector3 up = Frame.UpAt(thing.Position);
                    (Vector3 right, Vector3 forward) = Geometry.Axes(up, thing.Yaw);
                    float t = Geometry.RayBox(eye, direction, range, thing.Position + up * d.HalfExtents.Y, d.HalfExtents + new Vector3(0.1f), right, up, forward);
                    Consider(t, InteractionKind.Open, 0, thing.Id, "Open", d.Item.Name);
                    break;
                }
            }
        }
        foreach (RemoteEntity remote in _remotes.Values)
        {
            if (!remote.Spawned || !remote.HasState || remote.Kind != EntityKind.Character || (remote.Flags & Simulation.Movement.MoveFlags.Dead) != 0)
            {
                continue;
            }
            if (remote.Sleeping)
            {
                Consider(Geometry.RaySphere(eye, direction, range, remote.Position + Frame.UpAt(remote.Position) * 0.25f, 0.7f),
                    InteractionKind.LootSleeper, 0, remote.Id, "Loot", $"{remote.Name} (asleep)");
            }
            else if (remote.Wounded)
            {
                // Down: the interact key helps them up (G4).
                Consider(Geometry.RaySphere(eye, direction, MathF.Min(range, Content.Survival.HelpReach + 0.5f),
                    remote.Position + Frame.UpAt(remote.Position) * 0.25f, 0.7f), InteractionKind.HelpUp, 0, remote.Id, "Help up", remote.Name);
            }
            else if (Local.LeadsTeam && !Local.Team!.Has(remote.Id) && Local.Team.Members.Count < Content.World.TeamSize)
            {
                // G7: the leader invites a player close by (Rust's).
                Consider(Geometry.RaySphere(eye, direction, range, remote.Position + Frame.UpAt(remote.Position) * 1.1f, 0.5f),
                    InteractionKind.InviteToTeam, 0, remote.Id, "Invite to team", remote.Name);
            }
        }
        AimAtDuds(eye, direction, range, (t, id, name) => Consider(t, InteractionKind.Rearm, 0, id, "Light", name));
        return best;
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

    /// <summary>Acts on what <see cref="Aim"/> found: picks, picks up, opens or helps up. Returns the intent's sequence number.</summary>
    public uint Interact(in Interaction target) => target.Kind switch
    {
        InteractionKind.PickNatural => Local.PickUpNatural(target.NaturalId),
        InteractionKind.PickItem => Local.PickUpItem(target.Entity),
        InteractionKind.HelpUp => Local.HelpUp(target.Entity),
        InteractionKind.Rearm => Local.Rearm(target.Entity),
        InteractionKind.InviteToTeam => Local.InviteToTeam(target.Entity),
        _ => Local.Open(target.Entity),
    };

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
