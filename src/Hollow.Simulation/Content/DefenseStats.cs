using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Hollow.Simulation.Content.Readers;
using Hollow.Simulation.Items;
using Hollow.Simulation.Authorization;

namespace Hollow.Simulation.Content;

[Flags]
public enum DefenseTargets : byte { None = 0, Players = 1, Aircraft = 2 }

/// <summary>One automated weapon capability. Installed firearms retain their own ammunition, magazine and ballistics.</summary>
public sealed class DefenseStats
{
    public string AuthorizationPolicy { get; init; } = DeviceAuthorizationPolicies.CupboardDevice;
    public DefenseTargets Targets { get; init; }
    public int WeaponSlot { get; init; } = -1;
    public IReadOnlySet<ushort> Ammo { get; init; } = new HashSet<ushort>();
    public float Range { get; init; } = 30;
    public float MuzzleHeight { get; init; } = 1.1f;
    public float MuzzleForward { get; init; } = 0.8f;
    public float IntervalSeconds { get; init; } = 1.5f;
    public float MinimumAltitude { get; init; } = 6;
    public float ProjectileSpeed { get; init; } = 80;
    public float ProjectileGravity { get; init; }
    public float ProjectileLifetime { get; init; } = 8;
    public string YawPart { get; init; } = "Yaw";
    public string PitchPart { get; init; } = "Pitch";
    public string WeaponSocket { get; init; } = "WeaponMount";

    public bool TakesWeapon(ItemDef item) => WeaponSlot >= 0 && item.Firearm is { Damage: > 0, Magazine: > 0 };

    /// <summary>Checked by the same container move rules on client and server. One gun slot; other slots take its rounds.</summary>
    public bool Accepts(GameContent content, Container container, int slot, in ItemStack stack)
    {
        if (content.Item(stack.Item) is not { } item) return false;
        if (slot == WeaponSlot)
        {
            if (stack.Count != 1 || !TakesWeapon(item) || (item.MaxCondition > 0 && stack.Condition == 0)) return false;
            for (int i = 0; i < container.Size; i++)
                if (i != WeaponSlot && !container[i].IsEmpty && content.Item(container[i].Item)?.Bullet?.Calibre != item.Firearm!.Calibre)
                    return false;
            return true;
        }
        if (WeaponSlot < 0) return Ammo.Contains(item.Id);
        return container.InRange(WeaponSlot) && content.Item(container[WeaponSlot].Item)?.Firearm is { } gun &&
            item.Bullet is { } bullet && bullet.Calibre == gun.Calibre;
    }

    internal static DefenseStats Read(ContentReading r, JsonElement e, string where)
    {
        JsonFields f = r.Fields;
        f.OnlyKnown(e, where, "targets", "weaponSlot", "ammo", "range", "muzzleHeight", "muzzleForward", "interval", "minimumAltitude",
            "projectileSpeed", "projectileGravity", "projectileLifetime", "yawPart", "pitchPart", "weaponSocket", "authorizationPolicy");
        DefenseTargets targets = DefenseTargets.None;
        foreach (string name in f.Texts(e, "targets", where))
        {
            if (Enum.TryParse(name, ignoreCase: true, out DefenseTargets target) && target is DefenseTargets.Players or DefenseTargets.Aircraft)
                targets |= target;
            else f.Problem(where + ".targets", "expected players or aircraft");
        }
        var ammo = new HashSet<ushort>();
        foreach (string key in f.Texts(e, "ammo", where))
            if (r.Resolve(key, where + ".ammo") is { } item) ammo.Add(item.Id);
        return new DefenseStats
        {
            AuthorizationPolicy = f.Text(e, "authorizationPolicy", where, DeviceAuthorizationPolicies.CupboardDevice),
            Targets = targets, Ammo = ammo, WeaponSlot = f.Integer(e, "weaponSlot", where, -1),
            Range = f.Number(e, "range", where, 30), MuzzleHeight = f.Number(e, "muzzleHeight", where, 1.1f),
            MuzzleForward = f.Number(e, "muzzleForward", where, .8f), IntervalSeconds = f.Number(e, "interval", where, 1.5f),
            MinimumAltitude = f.Number(e, "minimumAltitude", where, 6), ProjectileSpeed = f.Number(e, "projectileSpeed", where, 80),
            ProjectileGravity = f.Number(e, "projectileGravity", where, 0), ProjectileLifetime = f.Number(e, "projectileLifetime", where, 8),
            YawPart = f.Text(e, "yawPart", where, "Yaw"), PitchPart = f.Text(e, "pitchPart", where, "Pitch"),
            WeaponSocket = f.Text(e, "weaponSocket", where, "WeaponMount"),
        };
    }

    internal static void Check(ContentChecking c, DeployableDef def)
    {
        if (def.Defense is not { } d) return;
        string where = $"deployables.json: {def.Key}.defense";
        if (!DeviceAuthorizationPolicies.Contains(d.AuthorizationPolicy)) c.Problem(where, "unknown authorization policy");
        if (d.Targets == DefenseTargets.None || def.Slots < 1 || d.WeaponSlot < -1 || d.WeaponSlot >= def.Slots ||
            d.Range is <= 0 or > 1000 || d.MuzzleHeight < 0 || d.MuzzleForward < 0 || d.IntervalSeconds <= 0 ||
            d.MinimumAltitude < 0 || d.ProjectileSpeed <= 0 || d.ProjectileGravity < 0 || d.ProjectileLifetime <= 0)
            c.Problem(where, "requires valid targets, storage/weapon slots and positive firing dimensions/range/timing");
        if (d.WeaponSlot < 0 && (d.Ammo.Count == 0 || d.Ammo.Any(id => c.Loaded.Items.FirstOrDefault(i => i.Id == id)?.Bullet?.Blast == null)))
            c.Problem(where, "a fixed launcher requires explosive projectile ammunition");
    }
}
