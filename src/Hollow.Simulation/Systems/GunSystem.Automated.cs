using System;
using System.Numerics;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;

namespace Hollow.Simulation;

public sealed partial class GunSystem
{
    /// <summary>Automated mounts enter the ordinary projectile, trace, damage and replication pipeline.</summary>
    public Bullet LaunchAutomated(EntityId mount, string name, ItemDef round, FirearmStats gun, in ItemStack weapon,
        Vector3 origin, Vector3 direction, uint sequence)
    {
        GunMods mods = GunMods.Of(Content, weapon, gun);
        var bullet = new Bullet
        {
            Id = Combat._nextProjectile++, Shooter = mount, ShooterName = name, Seq = sequence,
            Attack = new AttackId(AttackDomain.Firearm, sequence), ActionTick = (uint)Tick,
            Gun = gun, Round = round.Bullet!, Ammo = round, Origin = origin, Position = origin,
            Velocity = direction * (float)(gun.Speed * round.Bullet!.Speed * mods.Speed),
            Drag = gun.Drag, Decay = Ballistics.Decay(gun.Drag, BulletStepSeconds),
            Gravity = (float)gun.GravityOf(round.Bullet, Spec.SurfaceGravity),
            Damage = (float)(gun.Damage * round.Bullet.Damage * mods.Damage), Range = (float)gun.Range,
            LifetimeTicks = Math.Max(1, CharacterTicks.From(gun.LifetimeSeconds)), Silenced = mods.Silenced,
        };
        _bullets.Add(bullet);
        Events.Add(new ProjectileLaunched(mount, bullet.Id, round.Id, origin, bullet.Velocity, sequence, bullet.Silenced)
        {
            Attack = bullet.Attack, Tick = (uint)Tick, Weapon = gun.Item.Id, Gravity = bullet.Gravity, Drag = (float)bullet.Drag,
        });
        return bullet;
    }
}
