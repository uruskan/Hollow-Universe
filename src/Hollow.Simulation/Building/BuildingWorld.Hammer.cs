using System;
using System.Numerics;
using Hollow.Core;

namespace Hollow.Simulation.Building;

/// <summary>A hit on a piece or a door: the attack's direction in the zone (from the attacker toward the target) and its damage by type.</summary>
public readonly record struct DamageHit(Vector3 Direction, double Bullet = 0, double Melee = 0, double Explosive = 0, double Heat = 0)
{
    /// <summary>A hit of one channel's damage.</summary>
    public static DamageHit Of(DamageType type, double amount, Vector3 direction) => type switch
    {
        DamageType.Bullet => new DamageHit(direction, Bullet: amount),
        DamageType.Melee => new DamageHit(direction, Melee: amount),
        DamageType.Explosive => new DamageHit(direction, Explosive: amount),
        _ => new DamageHit(direction, Heat: amount),
    };

    public double this[DamageType type] => type switch
    {
        DamageType.Bullet => Bullet,
        DamageType.Melee => Melee,
        DamageType.Explosive => Explosive,
        _ => Heat,
    };
}

/// <summary>What a hit did: health lost, whether it came from the soft side, and whether it destroyed the target.</summary>
public readonly record struct DamageResult(double Loss, bool SoftSide, bool Destroyed);

public sealed partial class BuildingWorld
{
    /// <summary>
    /// Upgrades a piece to a higher grade (skipping grades is fine), paying the new grade's cost in its material; the
    /// piece keeps its fraction of health (Rust's rule). Needs building rights and reach; no time window.
    /// </summary>
    public Outcome Upgrade(ulong player, Piece piece, int grade, IBuildingWallet wallet, Vector3 eye)
    {
        Outcome usable = Usable(player, piece, eye);
        if (!usable.Ok)
        {
            return usable;
        }
        if (grade <= piece.Grade || grade >= Data.Grades.Count)
        {
            return OutcomeCode.NotHigherGrade;
        }
        GradeDef to = Data.Grades[grade];
        int cost = Data.CostOf(piece.Def, grade);
        if (wallet.Count(to.Material) < cost || !wallet.Take(to.Material, cost))
        {
            return OutcomeCode.NotEnough.With(Data.MaterialItem(to.Material));
        }
        double fraction = piece.Health / piece.MaxHealth;
        piece.Grade = grade;
        piece.Health = fraction * to.Health;
        Changed?.Invoke(new BuildingEvent(BuildingChange.PieceChanged, piece, Structure: piece.Structure));
        return Outcome.Done;
    }

    /// <summary>
    /// Turns a piece within its edit window: walls flip (their soft side changes), stairs turn 90° (triangle spirals
    /// 120°). Slabs and roofs don't turn, nor a frame holding a door. The turned piece must fit and stand.
    /// </summary>
    public Outcome Rotate(ulong player, Piece piece, Vector3 eye, double now)
    {
        Outcome usable = Usable(player, piece, eye);
        if (!usable.Ok)
        {
            return usable;
        }
        if (piece.Def.RotateSteps == 0)
        {
            return OutcomeCode.CannotRotate;
        }
        if (now - piece.PlacedAt > Data.EditWindow)
        {
            return OutcomeCode.EditWindowOver;
        }
        if (piece.Door != null)
        {
            return OutcomeCode.DoorInTheWay;
        }
        Pose before = piece.Pose;
        var after = new Pose(before.Position, PieceGeometry.Wrap(before.Yaw + piece.Def.RotateSteps));
        BuildingFrame frame = piece.Structure.Frame;
        (Vector3 position, Quaternion rotation) = frame.ToZone(after);
        if (Physics != null)
        {
            Outcome collision = CheckCollision(piece.Def, frame, after, position, rotation, ignore: piece);
            if (!collision.Ok)
            {
                return collision;
            }
        }
        if (CharacterInside(_parts[piece.Def], position, rotation))
        {
            return OutcomeCode.PlayerInTheWay;
        }
        // Relink at the new yaw; undo if it wouldn't stand.
        Unlink(piece);
        piece.Pose = after;
        Link(piece);
        Solve(piece.Structure);
        if (piece.Stability < Data.MinimumStability)
        {
            Unlink(piece);
            piece.Pose = before;
            Link(piece);
            Solve(piece.Structure);
            return OutcomeCode.Unstable;
        }
        Physics?.Move(piece.Body, position, rotation);
        Changed?.Invoke(new BuildingEvent(BuildingChange.PieceChanged, piece, Structure: piece.Structure));
        return Outcome.Done;
    }

    /// <summary>Removes a piece within its edit window, with no refund; what it held up comes down with it.</summary>
    public Outcome Demolish(ulong player, Piece piece, Vector3 eye, double now)
    {
        Outcome usable = Usable(player, piece, eye);
        if (!usable.Ok)
        {
            return usable;
        }
        if (now - piece.PlacedAt > Data.EditWindow)
        {
            return OutcomeCode.EditWindowOver;
        }
        Remove(piece, now, reason: Removal.Demolished);
        return Outcome.Done;
    }

    /// <summary>
    /// One hammer hit of repair: up to <see cref="BuildingData.RepairPerHit"/> health, not within
    /// <see cref="BuildingData.RepairDelay"/> seconds of damage, for max(1, ⌈cost × fraction × restored / max⌉) of the
    /// grade's material (RustMVP: a stone wall at 400 of 500 takes 12 stone for +100).
    /// </summary>
    public Outcome Repair(ulong player, Piece piece, IBuildingWallet wallet, Vector3 eye, double now)
    {
        Outcome usable = Usable(player, piece, eye);
        if (!usable.Ok)
        {
            return usable;
        }
        double max = piece.MaxHealth;
        if (piece.Health >= max)
        {
            return OutcomeCode.FullHealth;
        }
        double wait = piece.LastDamagedAt + Data.RepairDelay - now;
        if (wait > 0)
        {
            return OutcomeCode.RecentlyDamaged.With((int)Math.Ceiling(wait));
        }
        double restored = Math.Min(Data.RepairPerHit, max - piece.Health);
        GradeDef grade = Data.Grades[piece.Grade];
        int cost = RepairCost(Data.CostOf(piece.Def, piece.Grade), restored, max);
        if (wallet.Count(grade.Material) < cost || !wallet.Take(grade.Material, cost))
        {
            return OutcomeCode.NotEnough.With(Data.MaterialItem(grade.Material));
        }
        piece.Health = Math.Min(max, piece.Health + restored);
        Changed?.Invoke(new BuildingEvent(BuildingChange.PieceChanged, piece, Structure: piece.Structure));
        return Outcome.Done;
    }

    /// <summary>What one repair hit costs: max(1, ⌈full cost × fraction × restored / max⌉), in whole millionths so a sum like 3.0000000000000004 never costs 4.</summary>
    public int RepairCost(int fullCost, double restored, double max)
    {
        long fraction = (long)Math.Round(Data.RepairCostFraction * 1_000_000);
        long units = (long)Math.Round(restored * 1000);
        long numerator = fullCost * fraction * units;
        long denominator = (long)Math.Round(max * 1000) * 1_000_000;
        return (int)Math.Max(1, (numerator + denominator - 1) / denominator);
    }

    /// <summary>The checks every hammer action shares: the piece is there, the player has building rights at it and reaches it.</summary>
    private Outcome Usable(ulong player, Piece piece, Vector3 eye)
    {
        if (piece.Gone || piece.Collapsing)
        {
            return OutcomeCode.NotFound;
        }
        (Vector3 position, _) = piece.ZoneTransform;
        if (PrivilegeAt(player, position, Data.ClaimPadding) == Privilege.Blocked)
        {
            return OutcomeCode.BuildingBlocked;
        }
        BuildingFrame frame = piece.Structure.Frame;
        Vector3D eyeLocal = frame.ToLocal(D(eye));
        if ((PieceGeometry.ClosestPoint(Data, piece.Def, piece.Pose, eyeLocal) - eyeLocal).Length > Data.Reach)
        {
            return OutcomeCode.OutOfReach;
        }
        return Outcome.Done;
    }

    private void Unlink(Piece piece)
    {
        foreach (Link link in piece.SupportedBy)
        {
            link.Other.Supports.RemoveAll(l => l.Other == piece);
        }
        foreach (Link link in piece.Supports)
        {
            link.Other.SupportedBy.RemoveAll(l => l.Other == piece);
        }
        piece.SupportedBy.Clear();
        piece.Supports.Clear();
    }

    // ---------------------------------------------------------------- damage (the hook G5's explosives use)

    /// <summary>
    /// Damages a piece (RustMVP's resolver: each type times the grade's transmission, summed, rounded up once, capped at
    /// the health left). A wall hit from its soft side (its inside, −Z) takes the grade's soft-side transmissions: Rust's
    /// weak side. At zero health the piece is destroyed and what it held up collapses.
    /// </summary>
    public DamageResult Damage(Piece piece, in DamageHit hit, double now)
    {
        if (piece.Gone || piece.Collapsing)
        {
            return default;
        }
        bool soft = piece.Def.Family == PieceFamily.Wall && SoftSide(piece, hit.Direction);
        GradeDef grade = Data.Grades[piece.Grade];
        double loss = Resolve(soft ? grade.Soft : grade.Hard, hit, piece.Health);
        if (loss <= 0)
        {
            return new DamageResult(0, soft, false);
        }
        piece.Health -= loss;
        piece.LastDamagedAt = now;
        bool destroyed = piece.Health <= 0;
        if (destroyed)
        {
            Remove(piece, now, reason: Removal.Destroyed);
        }
        else
        {
            Changed?.Invoke(new BuildingEvent(BuildingChange.PieceChanged, piece, Structure: piece.Structure));
        }
        return new DamageResult(loss, soft, destroyed);
    }

    /// <summary>Damages a piece, door or claim device, whichever it is (the zone's building receiver).</summary>
    public DamageResult Damage(IBuildingPart part, in DamageHit hit, double now) => part switch
    {
        Piece piece => Damage(piece, hit, now),
        Door door => Damage(door, hit, now),
        ClaimDevice device => Damage(device, hit, now),
        _ => default,
    };

    /// <summary>Whether an attack in this zone direction comes from the wall's soft side (it travels toward +Z, from the inside).</summary>
    public static bool SoftSide(Piece wall, Vector3 direction)
    {
        Vector3D local = PieceGeometry.Rotate(wall.Structure.Frame.DirectionToLocal(D(direction)), -wall.Pose.Yaw);
        return local.Z > 0;
    }

    /// <summary>Health lost to a hit through a protection: ⌈Σ amount × transmission⌉, at most what is left.</summary>
    public static double Resolve(Protection protection, in DamageHit hit, double health)
    {
        double total = 0;
        foreach (DamageType type in Enum.GetValues<DamageType>())
        {
            total += Math.Max(0, hit[type]) * protection[type];
        }
        if (total <= 0 || health <= 0)
        {
            return 0;
        }
        return Math.Min(health, Math.Ceiling(total - 1e-9));
    }
}
