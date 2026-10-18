using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Simulation.Building;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;
using Hollow.Simulation.Authorization;

namespace Hollow.Simulation;

/// <summary>Authoritative automated mounts: acquisition, rights, power, magazines and the common projectile pipeline.</summary>
public sealed class DefenseSystem : ZoneSystem
{
    private sealed class MountState
    {
        public long NextFire, ReloadEnds = -1;
        public ushort Weapon, ReloadAmmo;
        public uint Sequence;
    }
    private readonly record struct Candidate(TargetRef Target, Vector3 Position, Vector3 Velocity);
    private readonly Dictionary<ulong, MountState> _mounts = new();
    private readonly SortedDictionary<ulong, Deployable> _devices = new();
    private readonly List<Candidate> _candidates = new();
    private readonly Dictionary<DefenseTargets, Action<Deployable, List<Candidate>>> _providers = new();
    private AuthorizationSubjects _subjects = null!;

    protected internal override void Init()
    {
        _subjects = new AuthorizationSubjects(Zone);
        _providers.Add(DefenseTargets.Players, PlayersInRange);
        _providers.Add(DefenseTargets.Aircraft, AircraftInRange);
    }

    public void Register(Deployable mount)
    {
        if (mount.Def.Defense != null && mount.Storage != null) _devices[mount.Id.Value] = mount;
    }

    public void Unregister(Deployable mount)
    {
        _devices.Remove(mount.Id.Value);
        _mounts.Remove(mount.Id.Value);
    }

    public bool CanManage(Deployable mount, EntityId player) => Zone.DeviceAuthorization().CanManage(mount, player);

    public Outcome SetEnabled(Deployable mount, EntityId player, bool enabled)
    {
        if (mount.Def.Defense == null) return OutcomeCode.NotUsable;
        if (!CanManage(mount, player)) return OutcomeCode.NotAuthorised;
        if (mount.PowerEnabled != enabled || mount.DefenseEnabled != enabled)
        {
            mount.PowerEnabled = mount.DefenseEnabled = enabled;
            mount.Revision++;
            Zone.Require<PowerSystem>().Changed(mount);
        }
        return Outcome.Done;
    }

    protected internal override void Step()
    {
        foreach (Deployable mount in _devices.Values)
        {
            DefenseStats stats = mount.Def.Defense!;
            Container storage = mount.Storage!;
            if (!_mounts.TryGetValue(mount.Id.Value, out MountState? state))
                _mounts.Add(mount.Id.Value, state = new MountState());
            ushort installed = stats.WeaponSlot >= 0 && storage.InRange(stats.WeaponSlot) &&
                Content.Item(storage[stats.WeaponSlot].Item) is { } installedItem && stats.TakesWeapon(installedItem)
                    ? installedItem.Id : (ushort)0;
            if (mount.InstalledWeapon != installed) { mount.InstalledWeapon = installed; mount.Revision++; }
            if (state.Weapon != installed) { state.Weapon = installed; state.ReloadEnds = -1; state.NextFire = Tick; }
            if (!mount.Powered || !mount.PowerEnabled || mount.Health <= 0)
            {
                state.ReloadEnds = -1;
                continue;
            }
            // Acquiring at 10 Hz keeps tracking responsive without tracing every object every physics tick.
            if ((Tick + (long)(mount.Id.Value % 3)) % 3 != 0 || Tick < state.NextFire) continue;
            ItemStack weapon = installed != 0 ? storage[stats.WeaponSlot] : default;
            FirearmStats? gun = Content.Item(installed)?.Firearm;
            if (stats.WeaponSlot >= 0 && (gun == null || (gun.Item.MaxCondition > 0 && weapon.Condition == 0))) continue;
            ItemDef? round = ReadyRound(mount, state, gun, ref weapon);
            if (round?.Bullet == null) continue;
            FirearmStats firing = gun ?? Launcher(mount, stats, round);
            Vector3 pivot = mount.Position + mount.Up * stats.MuzzleHeight;
            _candidates.Clear();
            foreach (var provider in _providers)
                if ((stats.Targets & provider.Key) != 0) provider.Value(mount, _candidates);
            Candidate? selected = null;
            Vector3 aim = default, muzzle = default;
            float nearest = stats.Range * stats.Range;
            foreach (Candidate candidate in _candidates)
            {
                Vector3 offset = candidate.Position - pivot;
                float distance = offset.LengthSquared();
                if (distance < .01f || distance > nearest ||
                    distance == nearest && selected is { } previous && candidate.Target.Id > previous.Target.Id) continue;
                Vector3 sight = offset / MathF.Sqrt(distance);
                Vector3 origin = pivot + sight * stats.MuzzleForward;
                TraceHit obstruction = Zone.Damage().Trace(origin, sight, MathF.Sqrt(distance), .015f, mount.Id, Tick, bullet: true);
                if (obstruction.Hit && obstruction.Target.Whole != candidate.Target.Whole) continue;
                GunMods mods = GunMods.Of(Content, weapon, firing);
                float speed = (float)(firing.Speed * round.Bullet.Speed * mods.Speed);
                float seconds = InterceptSeconds(candidate.Position - origin, candidate.Velocity, speed);
                Vector3 target = candidate.Position + candidate.Velocity * seconds + mount.Up *
                    (.5f * (float)firing.GravityOf(round.Bullet, Spec.SurfaceGravity) * seconds * seconds);
                Vector3 direction = Vector3.Normalize(target - origin);
                // Leading a moving target must not put the actual muzzle path through a nearby wall or a friend.
                TraceHit launchBlock = Zone.Damage().Trace(origin, direction, MathF.Min(4, MathF.Sqrt(distance)), .015f, mount.Id, Tick, bullet: true);
                if (launchBlock.Hit && launchBlock.Target.Whole != candidate.Target.Whole) continue;
                nearest = distance; selected = candidate; aim = direction; muzzle = origin;
            }
            if (selected == null || Zone.DeviceAuthorization().IsFriendly(mount, _subjects.Of(selected.Value.Target))) continue;
            mount.DefenseYaw = Facing.YawOf(aim, mount.Up) - mount.Yaw;
            mount.DefensePitch = Facing.PitchOf(aim, mount.Up);
            if (gun != null)
            {
                weapon.Loaded--;
                if (weapon.Condition > 0 && gun.Item.MaxCondition > 0) weapon.Condition--;
                storage.Set(stats.WeaponSlot, weapon);
            }
            else if (!TakeRound(storage, round.Id, stats.WeaponSlot)) continue;
            Zone.Guns().LaunchAutomated(mount.Id, mount.Def.Item.Name, round, firing, weapon, muzzle, aim, ++state.Sequence);
            mount.DefenseLastFireTick = Tick;
            mount.Revision++;
            state.NextFire = Tick + Math.Max(1, CharacterTicks.From(gun?.IntervalSeconds ?? stats.IntervalSeconds));
        }
    }

    private ItemDef? ReadyRound(Deployable mount, MountState state, FirearmStats? gun, ref ItemStack weapon)
    {
        Container storage = mount.Storage!;
        DefenseStats stats = mount.Def.Defense!;
        if (gun == null)
        {
            foreach (ItemStack stack in storage.Slots)
                if (!stack.IsEmpty && stats.Ammo.Contains(stack.Item)) return Content.Item(stack.Item);
            return null;
        }
        if (weapon.Loaded > 0)
            return Content.Item(weapon.LoadedAmmo) is { Bullet: { } loaded } item && loaded.Calibre == gun.Calibre ? item : null;
        if (state.ReloadEnds >= 0)
        {
            if (Tick < state.ReloadEnds) return null;
            int loaded = 0;
            while (loaded < gun.Magazine && TakeRound(storage, state.ReloadAmmo, stats.WeaponSlot)) loaded++;
            weapon.Loaded = (ushort)loaded;
            weapon.LoadedAmmo = state.ReloadAmmo;
            storage.Set(stats.WeaponSlot, weapon);
            state.ReloadEnds = -1;
            return loaded > 0 ? Content.Item(weapon.LoadedAmmo) : null;
        }
        for (int slot = 0; slot < storage.Size; slot++)
        {
            if (slot == stats.WeaponSlot || storage[slot].IsEmpty || Content.Item(storage[slot].Item)?.Bullet is not { } ammo || ammo.Calibre != gun.Calibre) continue;
            state.ReloadAmmo = storage[slot].Item;
            state.ReloadEnds = Tick + gun.ReloadTicks;
            return null;
        }
        return null;
    }

    private static bool TakeRound(Container storage, ushort ammo, int weaponSlot)
    {
        for (int slot = 0; slot < storage.Size; slot++)
        {
            ItemStack stack = storage[slot];
            if (slot == weaponSlot || stack.IsEmpty || stack.Item != ammo) continue;
            storage.Set(slot, stack.WithCount(stack.Count - 1));
            return true;
        }
        return false;
    }

    private static FirearmStats Launcher(Deployable mount, DefenseStats stats, ItemDef ammo) => new()
    {
        Item = mount.Def.Item, Calibre = ammo.Bullet!.Calibre, Speed = stats.ProjectileSpeed / Math.Max(.001, ammo.Bullet.Speed),
        Gravity = stats.ProjectileGravity, Damage = 0, Range = stats.Range * 2, LifetimeSeconds = stats.ProjectileLifetime,
    };

    private void PlayersInRange(Deployable mount, List<Candidate> into)
    {
        ComponentPool<PlayerLink> players = Entities.Pool<PlayerLink>();
        for (int i = 0; i < players.Count; i++)
        {
            Player p = players.Values[i].Player;
            if (p.Dead) continue;
            ref Character c = ref Entities.Get<Character>(Entities.AtSlot(players.Slots[i]));
            // Body centre follows crouching/lying as well as moving ship frames.
            Vector3 centre = (WorldOf(c.State) + EyeOf(c.State)) * .5f;
            Vector3 pivot = mount.Position + mount.Up * mount.Def.Defense!.MuzzleHeight;
            float range = mount.Def.Defense.Range;
            if (Vector3.DistanceSquared(centre, pivot) > range * range ||
                Zone.DeviceAuthorization().IsFriendly(mount, _subjects.Of(p.Ref))) continue;
            into.Add(new Candidate(p.Ref, centre, VelocityOf(c.State)));
        }
    }

    private void AircraftInRange(Deployable mount, List<Candidate> into)
    {
        foreach (Ship ship in Zone.Ships().All)
        {
            DefenseStats stats = mount.Def.Defense!;
            Vector3 pivot = mount.Position + mount.Up * stats.MuzzleHeight;
            if (ship.Destroyed || ship.InOrbit || ship.State.Has(ShipFlags.Landed) || ship.State.Has(ShipFlags.Touching) ||
                Vector3.DistanceSquared(ship.State.Position, pivot) > stats.Range * stats.Range ||
                HeightAboveGround(ship.State.Position) < stats.MinimumAltitude ||
                Zone.DeviceAuthorization().IsFriendly(mount, _subjects.Of(ship.Ref))) continue;
            into.Add(new Candidate(ship.Ref, ship.State.Position, ship.State.Velocity));
        }
    }

    /// <summary>Time to intercept at constant speed; approaching/crossing targets share the same solved lead.</summary>
    public static float InterceptSeconds(Vector3 relative, Vector3 velocity, float speed)
    {
        float a = velocity.LengthSquared() - speed * speed;
        float b = 2 * Vector3.Dot(relative, velocity);
        float c = relative.LengthSquared();
        if (MathF.Abs(a) < .0001f) return b < -.0001f ? Math.Clamp(-c / b, 0, 10) : 0;
        float discriminant = b * b - 4 * a * c;
        if (discriminant < 0) return 0;
        float root = MathF.Sqrt(discriminant);
        float t1 = (-b - root) / (2 * a), t2 = (-b + root) / (2 * a);
        float t = t1 > 0 && t2 > 0 ? MathF.Min(t1, t2) : MathF.Max(t1, t2);
        return Math.Clamp(t, 0, 10);
    }
}

public static class DefenseSystemAccess
{
    public static DefenseSystem Defenses(this Zone zone) => zone.Require<DefenseSystem>();
}
