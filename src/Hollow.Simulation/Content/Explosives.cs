using System;
using System.Collections.Generic;
using System.Linq;

namespace Hollow.Simulation.Content;

/// <summary>
/// One explosion (explosives.json <c>blasts</c>, GROUND.md §6.6): how far it reaches, how it falls off, what it does to
/// people and deployables, and how much one hit takes from a building piece of each grade (by side) and from a door of
/// each protection grade. The piece it is stuck to or hit takes the table's damage whole; other pieces in sight take
/// <see cref="Splash"/> of it with the falloff; whatever is behind a piece or the ground takes nothing.
/// </summary>
public sealed class BlastStats
{
    private readonly Dictionary<string, (double Hard, double Soft)> _pieces = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _doors = new(StringComparer.Ordinal);

    public string Key { get; init; } = "";

    /// <summary>Nothing beyond this many metres.</summary>
    public double Radius { get; init; }

    /// <summary>Full damage within this fraction of the radius, then less, down to none at the radius (hollow-orbit's 40%).</summary>
    public double Inner { get; init; } = 0.4;

    /// <summary>The share of the table's damage other pieces in sight take (the one hit takes it whole).</summary>
    public double Splash { get; init; }

    /// <summary>Damage to a character at the centre, before armour.</summary>
    public double Flesh { get; init; }

    /// <summary>
    /// Damage to the person or animal a round strikes (or a charge is stuck to), before armour, in place of <see cref="Flesh"/>
    /// (IW4: the high-velocity rocket kills whoever it strikes, KNOWN_PROBLEMS.md finding 26); 0: the struck take
    /// <see cref="Flesh"/> whole, as everything else at the centre does.
    /// </summary>
    public double Direct { get; init; }

    /// <summary>Damage to a deployable at the centre.</summary>
    public double Deployable { get; init; }

    /// <summary>The fire this blast leaves where it went off (explosives.json fires, IW4), by key and resolved; null for none.</summary>
    public string? FireKey { get; init; }

    public FireDef? Fire { get; internal set; }

    /// <summary>The grade keys the piece table names (building.json's grades).</summary>
    public IEnumerable<string> PieceGrades => _pieces.Keys;

    public IEnumerable<string> DoorGrades => _doors.Keys;

    /// <summary>One hit's damage to a piece of a grade (building.json's key), from its hard or soft side; 0 for a grade not listed.</summary>
    public double Piece(string grade, bool soft) =>
        _pieces.TryGetValue(grade, out (double Hard, double Soft) d) ? soft ? d.Soft : d.Hard : 0;

    /// <summary>One hit's damage to a door whose protection is a grade (wood, metal, armoured); 0 for one not listed.</summary>
    public double Door(string protection) => _doors.GetValueOrDefault(protection);

    internal void SetPiece(string grade, double hard, double soft) => _pieces[grade] = (hard, soft);

    internal void SetDoor(string protection, double damage) => _doors[protection] = damage;

    /// <summary>The share of full damage at a distance from the centre: 1 within the inner radius, falling to 0 at the radius.</summary>
    public double Falloff(double distance)
    {
        double inner = Radius * Inner;
        if (distance <= inner)
        {
            return 1;
        }
        if (distance >= Radius || Radius <= inner)
        {
            return 0;
        }
        return 1 - (distance - inner) / (Radius - inner);
    }

    public override string ToString() => Key;
}

/// <summary>
/// An explosive thrown or placed by hand (explosives.json <c>throwables</c>): the timed charge, the bag charge, the tin
/// grenade. Which are also put on a surface by hand is the placement system's list (placement.json: a charge's item
/// places it, <see cref="ItemDef.Places"/>). Its fuse is a random length between two bounds, it may be a dud (and can be lit again), and it sticks to
/// what it touches or bounces and comes to rest.
/// </summary>
public sealed class ThrowableStats
{
    public ItemDef Item { get; init; } = null!;

    public string BlastKey { get; init; } = "";

    public BlastStats Blast { get; internal set; } = null!;

    /// <summary>The fuse burns between these many seconds (equal: always the same).</summary>
    public double FuseLeast { get; init; }

    public double FuseMost { get; init; }

    /// <summary>The chance that the fuse goes out instead (Rust's satchels and beancans): a dud, which can be lit again.</summary>
    public double DudChance { get; init; }

    /// <summary>Sticks to the first surface it touches (charges); otherwise it bounces and rolls to rest (grenades).</summary>
    public bool Sticky { get; init; }

    /// <summary>Speed of a throw and of a gentle lob (m/s).</summary>
    public double ThrowSpeed { get; init; }

    public double LobSpeed { get; init; }

    /// <summary>A player throws or places at most one every this many seconds.</summary>
    public double CooldownSeconds { get; init; }

    /// <summary>Restitution of a bounce (0: it stops dead).</summary>
    public double Bounce { get; init; }

    /// <summary>Its size, as a ball, for collisions.</summary>
    public double Radius { get; init; }

    public int FuseTicks(double unit) => Math.Max(1, CharacterTicks.From(FuseLeast + (FuseMost - FuseLeast) * Math.Clamp(unit, 0, 1)));

    public override string ToString() => Item.Key;
}

/// <summary>
/// A kind of fire (explosives.json <c>fires.kinds</c>, IRON_WAKE.md §7, IW4): a pool of burning fuel a blast leaves, of a
/// radius, burning for a while and hurting what is within its radius and in its sight every so often, by distance from its
/// middle (full within <see cref="Inner"/> of the radius, nothing at its edge, as a blast's falloff), through the damage
/// pipeline's heat delivery: people and animals, the burning it sets in people, deployables that burn and the building
/// through its rules' heat channel. Amounts are a second's, at the middle.
/// </summary>
public sealed class FireDef
{
    public string Key { get; init; } = "";

    /// <summary>Nothing beyond this many metres of its middle.</summary>
    public double Radius { get; init; }

    /// <summary>The share of the radius it does all its damage within; then less, down to none at the radius.</summary>
    public double Inner { get; init; } = 0.4;

    /// <summary>How long it burns.</summary>
    public double Seconds { get; init; }

    /// <summary>How often it hurts what is in it, in seconds.</summary>
    public double Every { get; init; } = 0.5;

    /// <summary>To people (before what they wear on the body) and animals, a second.</summary>
    public double Flesh { get; init; }

    /// <summary>The health set burning in people a second while they are in it (survival.json's burning vital burns it off).</summary>
    public double Burning { get; init; }

    /// <summary>To deployables whose data says they burn (deployables.json <c>burns</c>), a second.</summary>
    public double Deployables { get; init; }

    /// <summary>To building pieces, doors and the tool cupboard through the building rules' heat channel, a second.</summary>
    public double Building { get; init; }

    public int Ticks => Math.Max(1, CharacterTicks.From(Seconds));

    public int EveryTicks => Math.Max(1, CharacterTicks.From(Every));

    /// <summary>The share of a second's damage it does at a distance from its middle: 1 within the inner radius, 0 at the radius.</summary>
    public double Falloff(double distance)
    {
        double inner = Radius * Inner;
        if (distance <= inner)
        {
            return 1;
        }
        if (distance >= Radius || Radius <= inner)
        {
            return 0;
        }
        return 1 - (distance - inner) / (Radius - inner);
    }

    public override string ToString() => Key;
}

/// <summary>
/// How every fire behaves (explosives.json <c>fires</c>, IW4): how many may burn in a zone (the oldest goes first), how far
/// burning fuel runs and falls from a steep surface or the air to one that holds it, and how steep a surface holds it.
/// </summary>
public sealed class FireRules
{
    public int Most { get; init; } = 64;

    /// <summary>Metres the fuel falls at most to a surface that holds it; none that near: no fire.</summary>
    public double RunsDown { get; init; } = 12;

    /// <summary>The steepest surface (degrees from up) that holds burning fuel.</summary>
    public double SteepestDegrees { get; init; } = 50;
}

/// <summary>How raid alerts go out (explosives.json <c>alerts</c>): one per base at most every so often, kept for a while for those offline.</summary>
public sealed class RaidAlertSettings
{
    /// <summary>A base raises an alert at most once in this many seconds; hits meanwhile are summed into the next.</summary>
    public double CooldownSeconds { get; init; } = 60;

    /// <summary>Alerts queued for an offline player are dropped after this many days.</summary>
    public double KeepDays { get; init; } = 7;

    /// <summary>At most this many alerts wait for one player (the oldest go first).</summary>
    public int MaxQueued { get; init; } = 50;
}
