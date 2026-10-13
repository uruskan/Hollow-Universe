using System;
using System.Collections.Generic;

namespace Hollow.Simulation.Content;

/// <summary>How the trigger works: one shot a press, or shots while held.</summary>
public enum FireMode : byte
{
    Semi,
    Auto,
}

/// <summary>How a firearm kicks (RustMVP <c>RecoilMode</c>): the same step every shot, or a step from a pattern.</summary>
public enum RecoilMode : byte
{
    Vertical,
    Pattern,
}

/// <summary>Where an attachment goes on a firearm: one of each at most.</summary>
public enum AttachmentSlot : byte
{
    Sight,
    Muzzle,
    Rail,
}

/// <summary>One step of a recoil pattern, in degrees: up, and to the right.</summary>
public readonly record struct RecoilStep(double Pitch, double Yaw);

/// <summary>
/// How a firearm's view kicks and recovers (weapons.json <c>recoil</c>; RustMVP weapon_definition.hpp and
/// weapon_rules.hpp): a step a shot, applied over <see cref="KickSeconds"/>, held for <see cref="RecoveryDelaySeconds"/>
/// after the last shot, then returned whole over <see cref="RecoverySeconds"/>. Degrees and seconds.
/// </summary>
public sealed class RecoilStats
{
    public RecoilMode Mode { get; init; }

    /// <summary>The kick of every shot (vertical mode).</summary>
    public double PitchPerShot { get; init; }

    /// <summary>The steps, in order (pattern mode); the last repeats.</summary>
    public IReadOnlyList<RecoilStep> Pattern { get; init; } = Array.Empty<RecoilStep>();

    /// <summary>A shot's step is scaled by 1 ± this (from the shot's seed).</summary>
    public double PitchJitter { get; init; }

    /// <summary>Degrees either way added to a shot's step (from the shot's seed).</summary>
    public double YawJitter { get; init; }

    public double PitchLimit { get; init; }

    public double YawLimit { get; init; }

    /// <summary>The pattern starts again this long after the last shot.</summary>
    public double ResetSeconds { get; init; }

    public double KickSeconds { get; init; }

    public double RecoveryDelaySeconds { get; init; }

    public double RecoverySeconds { get; init; }

    /// <summary>The kick is scaled by this while aiming down the sights, and by <see cref="Crouched"/> while crouching.</summary>
    public double Aimed { get; init; } = 1;

    public double Crouched { get; init; } = 1;
}

/// <summary>
/// How wide a firearm's shots go (weapons.json <c>spread</c>): a cone's half-angle in degrees, at the hip or aimed,
/// widened by bloom (each shot adds some, it fades at <see cref="BloomRecovery"/> a second), by moving, in the air, and
/// narrowed crouching.
/// </summary>
public sealed class SpreadStats
{
    public double Hip { get; init; }

    public double Aimed { get; init; }

    public double BloomPerShot { get; init; }

    public double MaxBloom { get; init; }

    public double BloomRecovery { get; init; }

    /// <summary>The cone grows by this fraction at full sprinting speed (hip, aimed), in proportion below it.</summary>
    public double Moving { get; init; }

    public double MovingAimed { get; init; }

    public double Airborne { get; init; } = 1;

    public double Crouched { get; init; } = 1;
}

/// <summary>A firearm (weapons.json <c>weapons</c>): the gun's own numbers, before its rounds and attachments.</summary>
public sealed class FirearmStats
{
    public ItemDef Item { get; init; } = null!;

    public string Calibre { get; init; } = "";

    public int Magazine { get; init; }

    public FireMode Mode { get; init; }

    public double IntervalSeconds { get; init; }

    public double ReloadSeconds { get; init; }

    public double EquipSeconds { get; init; }

    /// <summary>Muzzle speed in m/s; the speed lost a second as a fraction (drag); gravity as a multiple of the planet's.</summary>
    public double Speed { get; init; }

    public double Drag { get; init; }

    public double Gravity { get; init; } = 1;

    /// <summary>A bullet stops after this many metres or this many seconds.</summary>
    public double Range { get; init; }

    public double LifetimeSeconds { get; init; }

    public double Damage { get; init; }

    /// <summary>Full damage to <see cref="FalloffStart"/> metres, then less, down to <see cref="FalloffLeast"/> at <see cref="FalloffEnd"/>.</summary>
    public double FalloffStart { get; init; }

    public double FalloffEnd { get; init; }

    public double FalloffLeast { get; init; } = 1;

    public double Head { get; init; } = 2;

    public double Body { get; init; } = 1;

    public double Limb { get; init; } = 0.5;

    public SpreadStats Spread { get; init; } = new();

    public RecoilStats Recoil { get; init; } = new();

    /// <summary>How much the view is magnified aimed down the sights (the camera's business; a sight replaces it).</summary>
    public double Zoom { get; init; } = 1;

    public IReadOnlyList<string> AttachmentKeys { get; init; } = Array.Empty<string>();

    /// <summary>The attachments it takes.</summary>
    public IReadOnlyList<ItemDef> Attachments { get; internal set; } = Array.Empty<ItemDef>();

    /// <summary>The rounds of its calibre, in weapons.json order (the first is what a reload loads by default).</summary>
    public IReadOnlyList<ItemDef> Ammo { get; internal set; } = Array.Empty<ItemDef>();

    /// <summary>Time between shots in sixteenths of a tick (cadence is counted in whole sixteenths on both sides).</summary>
    public int IntervalSixteenths => Math.Max(1, (int)Math.Round(IntervalSeconds * CharacterTicks.Rate * 16));

    public int ReloadTicks => Math.Max(1, CharacterTicks.From(ReloadSeconds));

    public int EquipTicks => Math.Max(0, CharacterTicks.From(EquipSeconds));

    public bool Takes(ItemDef attachment)
    {
        foreach (ItemDef a in Attachments)
        {
            if (a == attachment)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Damage for a distance flown: full, then falling linearly to the least.</summary>
    public double Falloff(double metres)
    {
        if (metres <= FalloffStart || FalloffEnd <= FalloffStart)
        {
            return 1;
        }
        double t = Math.Min(1, (metres - FalloffStart) / (FalloffEnd - FalloffStart));
        return 1 - t * (1 - FalloffLeast);
    }

    public double RegionMultiplier(HitRegion region) => region switch
    {
        HitRegion.Head => Head,
        HitRegion.Limb => Limb,
        _ => Body,
    };

    public override string ToString() => Item.Key;
}

/// <summary>A kind of round (weapons.json <c>ammo</c>): what it changes about the gun that fires it.</summary>
public sealed class BulletStats
{
    public ItemDef Item { get; init; } = null!;

    public string Calibre { get; init; } = "";

    /// <summary>Multipliers on the gun's damage and muzzle speed.</summary>
    public double Damage { get; init; } = 1;

    public double Speed { get; init; } = 1;

    /// <summary>Bleeding a hit starts, as a fraction of its damage.</summary>
    public double Bleed { get; init; }

    /// <summary>Health a hit sets burning (incendiary rounds).</summary>
    public double Burn { get; init; }

    /// <summary>Fraction of the damage a deployable takes.</summary>
    public double Structure { get; init; }

    /// <summary>G5: the blast it explodes with where it stops (explosives.json), by key and resolved; null for plain rounds.</summary>
    public string? BlastKey { get; init; }

    public BlastStats? Blast { get; internal set; }

    public override string ToString() => Item.Key;
}

/// <summary>An attachment (weapons.json <c>attachments</c>): where it goes and what it changes.</summary>
public sealed class AttachmentStats
{
    public ItemDef Item { get; init; } = null!;

    /// <summary>Its place in weapons.json: bit <c>1 &lt;&lt; Index</c> of what others see on a held gun.</summary>
    public int Index { get; init; }

    public AttachmentSlot Slot { get; init; }

    /// <summary>The magnification aimed down the sights (sights); 0 keeps the gun's.</summary>
    public double Zoom { get; init; }

    public double HipSpread { get; init; } = 1;

    public double AimedSpread { get; init; } = 1;

    public double Recoil { get; init; } = 1;

    public double Speed { get; init; } = 1;

    public double Damage { get; init; } = 1;

    /// <summary>Hides the muzzle flash and quiets the shot (for the visual layer).</summary>
    public bool Silenced { get; init; }

    /// <summary>A light that shines while the gun is held.</summary>
    public bool Light { get; init; }

    public override string ToString() => Item.Key;
}
