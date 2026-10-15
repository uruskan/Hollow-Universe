using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hollow.Core;
using Hollow.Net.Bits;
using Hollow.Net.Protocol;
using Hollow.Simulation;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Nature;
using Hollow.Simulation.World;

namespace Hollow.Client;

// The survival game in the client (GROUND.md §4.3, §6): the local player's state and intents (LocalPlayer), static
// things, natural objects as players changed them, projectiles in flight. What the interact key acts on is the one
// targeting query's (InteractionModule).
public sealed class SurvivalModule : ClientModule
{
    private readonly Dictionary<uint, RemoteThing> _things = new();
    private readonly Dictionary<EntityId, RemoteThing> _thingsById = new();
    private readonly List<ClientProjectile> _projectiles = new();
    private WorldModule? _worldModule;
    private WorldModule World => _worldModule ??= Client.Require<WorldModule>();
    private PlacementModule? _placementModule;
    private PlacementModule Placement => _placementModule ??= Client.Require<PlacementModule>();
    private InteractionModule? _interactionModule;
    private InteractionModule Interaction => _interactionModule ??= Client.Require<InteractionModule>();
    private ShipModule? _shipModule;
    private ShipModule Ships => _shipModule ??= Client.Require<ShipModule>();

    /// <summary>Where the natural objects are, and what players did to them (as the server says).</summary>
    public INaturalObjects? Nature { get; private set; }

    public NaturalState? Natural { get; private set; }

    /// <summary>Static things in range: deployables, bags of dropped items, corpses.</summary>
    public Dictionary<uint, RemoteThing>.ValueCollection Things => _things.Values;

    public RemoteThing? ThingOf(EntityId id) => _thingsById.GetValueOrDefault(id);

    /// <summary>Projectiles in flight (and for a moment after they stop), for drawing.</summary>
    public IReadOnlyList<ClientProjectile> Projectiles => _projectiles;

    /// <summary>Raised when a character's look (clothing) changes or arrives.</summary>
    public event Action<RemoteEntity>? LookChanged;

    /// <summary>A natural object's last hit at most this many ticks old when sent is news: the views play its effects.</summary>
    public const int FreshHitTicks = 30;

    protected internal override void Init(MessageHandlers<GameClient> handlers)
    {
        Local.Gun.ShotFired += OnOwnShot;
        handlers
            .On<ThingSpawn>((_, in m) => OnThingSpawn(m))
            .On<ThingUpdate>((_, in m) => OnThingUpdate(m))
            .On<CharacterLook>((_, in m) => OnCharacterLook(m))
            .On<NaturalStates>((_, in m) => OnNaturalStates(m))
            .On<ProjectileLaunch>((_, in m) => OnProjectileLaunch(m))
            .On<ProjectileEnd>((_, in m) => OnProjectileEnd(m))
            .On<IntentResult>((_, in m) => Local.OnResult(m))
            .On<ContainerContents>((_, in m) => Local.OnContents(m))
            .On<ContainerSlots>((_, in m) => Local.OnSlots(m))
            .On<LootState>((_, in m) => OnLootState(m))
            .On<VitalsUpdate>((_, in m) => Local.OnVitals(m))
            .On<CraftQueueState>((_, in m) => Local.OnCraftQueue(m))
            .On<HitMarker>((_, in m) => Local.OnHitMarker(m))
            .On<ItemsChanged>((_, in m) => Local.OnItemsChanged(m))
            .On<DamageTaken>((_, in m) => Local.OnDamage(m))
            .On<CraftFinished>((_, in m) => Local.OnCraftFinished(m))
            .On<GunUpdate>((_, in m) => Local.Gun.OnUpdate(m))
            .On<LifeState>((_, in m) => OnLifeState(m));
    }

    protected internal override void Welcome()
    {
        Nature = Zone!.CreateNature(Surface!, Zone.CreateFrame(Surface!), Content);
        if (Client.Options.SharedNature != null)
        {
            Natural = Client.Options.SharedNature(Zone, Surface!);
        }
        else
        {
            Natural = new NaturalState(Content, Nature);
            _ = new NaturalBodies(Prediction!.World, Natural);
        }
    }

    protected internal override void Update(double delta)
    {
        Local.ServerTick = ServerTickNow;
        Local.ClockSeconds = ClockSeconds;
        Local.WorkbenchLevel = ThingOf(new EntityId(Interaction.BenchNear().Id))?.Deployable(Content)?.Workbench ?? 0;
        Local.StationNear ??= StationNear;
        Local.Update();
        Local.UpdatePlacements();
        double now = ClockSeconds;
        _projectiles.RemoveAll(p => (p.EndedAt != null && now - p.EndedTime > 1.0) || now - p.LaunchedAt > 15);
    }

    protected internal override void Despawned(uint netId) => RemoveThing(netId);

    // The eye, for the reach of stations: as the server measures it (Zone.EyeOf; aboard a ship, where the ship carries the
    // player: IRON_WAKE.md §4.8).
    internal Vector3 StationEye => Prediction?.WorldPose(EyeHeight).Eye ?? default;

    // The nearest deployable of a kind within a station's reach, as the server's Zone.Ships().StationNear finds a ship's vehicle
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
            World.ThingBodyRemoved(thing); // G7
            Placement.ThingSupportRemoved(thing); // P2
            if (thing.Aboard)
            {
                _aboard.Remove(thing);
                Ships.ThingAboard(thing, false); // IW5
            }
        }
    }

    private readonly List<RemoteThing> _aboard = new();

    /// <summary>The things aboard ships (IRON_WAKE.md §8), placed on their ships as drawn every frame.</summary>
    public IReadOnlyList<RemoteThing> ThingsAboard => _aboard;

    private void OnThingSpawn(in ThingSpawn m)
    {
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
            Support = m.SupportFamily is Simulation.Targets.TargetFamily.Piece or Simulation.Targets.TargetFamily.Ship
                ? new Simulation.Targets.TargetRef(m.SupportFamily, m.Support)
                : default,
            Capabilities = new Simulation.Capabilities.CapabilitySet(m.Capabilities),
        };
        thing.Up = Frame.UpAt(thing.Position);
        if (thing.Aboard)
        {
            // Aboard a ship (IRON_WAKE.md §8): its pose is the ship's frame's, carried where the ship is drawn.
            thing.LocalPosition = thing.Position;
            thing.LocalYaw = thing.Yaw;
            _aboard.Add(thing);
            Ships.ThingAboard(thing, true);
        }
        _things[m.NetId] = thing;
        _thingsById[thing.Id] = thing;
        World.ThingBodyAdded(thing); // G7
        Placement.ThingSupportAdded(thing); // P2
    }

    private void OnThingUpdate(in ThingUpdate m)
    {
        if (_things.TryGetValue(m.NetId, out RemoteThing? thing))
        {
            thing.Health = m.Health / 255f;
            thing.Count = (int)Math.Min(m.Count, int.MaxValue);
            thing.Flags = m.Flags;
            thing.Revision++;
        }
    }

    private void OnCharacterLook(in CharacterLook m)
    {
        RemoteEntity remote = Remote(m.NetId);
        remote.Wear = (m.Wear ?? Array.Empty<WornItem>()).Select(w => (ushort)Math.Min(w.Item, ushort.MaxValue)).ToArray();
        remote.Body = Math.Min(m.Body, (byte)1);
        LookChanged?.Invoke(remote);
    }

    private void OnNaturalStates(in NaturalStates m)
    {
        if (Natural != null && Nature != null)
        {
            foreach (NaturalNet n in m.States ?? Array.Empty<NaturalNet>())
            {
                ApplyNatural(n);
            }
        }
    }

    private void OnProjectileLaunch(ProjectileLaunch m)
    {
        if (Zone == null)
        {
            return;
        }
        var origin = new Vector3(m.X, m.Y, m.Z);
        ItemDef? ammo = Content.Item((int)Math.Min(m.Ammo, ushort.MaxValue));
        if (m.Seq != 0 && m.Shooter == NetId && _projectiles.FirstOrDefault(p => p.Own && p.Seq == m.Seq && p.Battery == m.Battery && p.Id == 0) is { } mine)
        {
            // Our own shot, drawn since we fired it: now it has the server's ID (its end will name it).
            mine.Id = m.Id;
            return;
        }
        // A battery's rocket (IW3): under its motor, the battery's launch rules for its round.
        Simulation.Combat.RocketMotor? motor = m.Battery > 0 && Content.Battery(m.Battery - 1) is { } battery && ammo != null && battery.Launch.Of(ammo.Id) is { } profile
            ? Simulation.Combat.RocketMotor.Of(battery.Launch, profile) : null;
        FirearmStats? gun = ammo?.Bullet is { } round ? Content.Firearms.FirstOrDefault(g => g.Calibre == round.Calibre) : null;
        // An arrow's own, a round's its gun's times its own (IW4), as the server flies them.
        float gravity = ammo?.Ammo is { } arrow ? (float)(Zone.SurfaceGravity * 9.80665 * arrow.Gravity)
            : gun != null ? (float)gun.GravityOf(ammo?.Bullet, Zone.SurfaceGravity)
            : (float)(Zone.SurfaceGravity * 9.80665);
        _projectiles.Add(new ClientProjectile
        {
            Id = m.Id,
            Shooter = m.Shooter,
            Ammo = ammo?.Id ?? 0,
            Origin = origin,
            Velocity = new Vector3(m.VelocityX, m.VelocityY, m.VelocityZ),
            Gravity = -Frame.UpAt(origin) * gravity,
            Centre = Frame.ToLocal(Core.Vector3D.Zero),
            Drag = (float)(gun?.Drag ?? 0),
            Seq = m.Seq,
            Silenced = m.Silenced,
            LaunchedAt = ClockSeconds,
            Battery = motor != null ? m.Battery : 0,
            Motor = motor,
        });
    }

    /// <summary>
    /// A gunner's predicted rocket (IW3, IRON_WAKE.md §6.9): drawn at once from the muzzle, its number the shot's; the
    /// server's launch then names it, or a refusal takes it away (<see cref="RemoveOwnRocket"/>).
    /// </summary>
    public ClientProjectile? AddOwnRocket(Simulation.Content.BatteryDef battery, ushort round, Vector3 origin, Vector3 velocity, uint number)
    {
        if (Zone == null || battery.Launch.Of(round) is not { } profile)
        {
            return null;
        }
        var rocket = new ClientProjectile
        {
            Shooter = NetId,
            Ammo = round,
            Origin = origin,
            Velocity = velocity,
            Gravity = -Frame.UpAt(origin) * (float)(Zone.SurfaceGravity * 9.80665),
            Centre = Frame.ToLocal(Core.Vector3D.Zero),
            Seq = number,
            Own = true,
            LaunchedAt = ClockSeconds,
            Battery = battery.Index + 1,
            Motor = Simulation.Combat.RocketMotor.Of(battery.Launch, profile),
        };
        _projectiles.Add(rocket);
        return rocket;
    }

    /// <summary>Takes a predicted rocket away (the server refused its shot).</summary>
    public void RemoveOwnRocket(ClientProjectile rocket) => _projectiles.Remove(rocket);

    private void OnProjectileEnd(ProjectileEnd m)
    {
        if (_projectiles.FirstOrDefault(p => p.Id == m.Id) is { } projectile)
        {
            projectile.EndedAt = new Vector3(m.X, m.Y, m.Z);
            projectile.EndedTime = ClockSeconds;
            projectile.EndTarget = m.Target;
        }
    }

    private void OnLootState(in LootState m)
    {
        DeployableDef? device = ThingOf(new EntityId(m.Target))?.Deployable(Content);
        Local.OnLoot(m, device is { IsMachine: true } ? device : null);
    }

    private void OnLifeState(in LifeState m)
    {
        Local.ClockSeconds = ClockSeconds;
        Local.OnLife(m);
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
        // A fresh hit counts one more for the views (its effects); an old one (an area coming into range) doesn't.
        byte seen = Natural!.TryGetOverride(n.Id, out NaturalOverride was) ? was.SeenHits : (byte)0;
        Natural!.Apply(n.Id, new NaturalOverride
        {
            Health = n.Health / 255f * max, HasWeakSpot = n.HasWeakSpot, WeakSpotYaw = n.WeakSpotYaw, WeakSpotLift = n.WeakSpotLift, Depleted = n.Depleted,
            Streak = n.Streak, LastHit = n.HitOffset, SeenHits = n.HitAge <= FreshHitTicks ? (byte)(seen + 1) : seen,
        });
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
            Velocity = shot.Direction * shot.Speed + shot.Carried,
            Gravity = -Frame.UpAt(shot.Origin) * (float)gun.GravityOf(shot.Round.Bullet, Zone.SurfaceGravity),
            Centre = Frame.ToLocal(Core.Vector3D.Zero),
            Drag = (float)gun.Drag,
            Seq = shot.Seq,
            Own = true,
            Silenced = shot.Silenced,
            LaunchedAt = ClockSeconds,
        });
    }
}

/// <summary>The accessor on the client (GROUND_SYSTEMS.md §12.5, K11).</summary>
public static class SurvivalModuleAccess
{
    public static SurvivalModule Survival(this GameClient client) => client.Require<SurvivalModule>();
}
