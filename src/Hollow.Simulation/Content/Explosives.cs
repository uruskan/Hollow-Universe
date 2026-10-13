using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Hollow.Simulation.Building;

namespace Hollow.Simulation.Content;

/// <summary>
/// One explosion (explosives.json <c>blasts</c>, GROUND.md §6.5): how far it reaches, how it falls off, what it does to
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

    /// <summary>Damage to a deployable at the centre.</summary>
    public double Deployable { get; init; }

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
/// grenade. Its fuse is a random length between two bounds, it may be a dud (and can be lit again), and it sticks to
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

    /// <summary>Can be placed by hand on the surface aimed at, besides thrown.</summary>
    public bool Placeable { get; init; }

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

/// <summary>Reads explosives.json (G5) and links rounds' blasts (weapons.json) to it; checks both against building.json's grades.</summary>
internal static class ExplosivesReader
{
    public const string File = "explosives.json";

    public static void Read(JsonFields f, JsonElement root, GameContent.Loaded loaded, Func<string, string, ItemDef?> resolve)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            f.OnlyKnown(root, File, "source", "note", "alerts", "blasts", "throwables");
        }
        if (f.TryChild(root, "alerts", out JsonElement alerts))
        {
            string where = $"{File}: alerts";
            f.OnlyKnown(alerts, where, "cooldownSeconds", "keepDays", "maxQueued", "note");
            loaded.RaidAlerts = new RaidAlertSettings
            {
                CooldownSeconds = f.Double(alerts, "cooldownSeconds", where, 60),
                KeepDays = f.Double(alerts, "keepDays", where, 7),
                MaxQueued = f.Integer(alerts, "maxQueued", where, 50),
            };
        }
        JsonElement[] blasts = f.List(root, "blasts", File);
        for (int i = 0; i < blasts.Length; i++)
        {
            JsonElement e = blasts[i];
            string key = f.Text(e, "key", $"{File}: blasts[{i}]");
            string where = $"{File}: blast {JsonFields.Quote(key)}";
            f.OnlyKnown(e, where, "key", "radius", "inner", "splash", "flesh", "deployable", "pieces", "doors", "note");
            var blast = new BlastStats
            {
                Key = key,
                Radius = f.Double(e, "radius", where),
                Inner = f.Double(e, "inner", where, 0.4),
                Splash = f.Double(e, "splash", where, 0),
                Flesh = f.Double(e, "flesh", where, 0),
                Deployable = f.Double(e, "deployable", where, 0),
            };
            if (f.TryChild(e, "pieces", out JsonElement pieces))
            {
                foreach (JsonProperty grade in pieces.EnumerateObject())
                {
                    if (grade.Value.ValueKind == JsonValueKind.Number)
                    {
                        double d = grade.Value.GetDouble();
                        blast.SetPiece(grade.Name, d, d);
                    }
                    else if (grade.Value.ValueKind == JsonValueKind.Array && grade.Value.GetArrayLength() == 2 &&
                        grade.Value[0].ValueKind == JsonValueKind.Number && grade.Value[1].ValueKind == JsonValueKind.Number)
                    {
                        blast.SetPiece(grade.Name, grade.Value[0].GetDouble(), grade.Value[1].GetDouble());
                    }
                    else
                    {
                        f.Problem(where + ".pieces", $"\"{grade.Name}\" must be a number or [hard, soft]");
                    }
                }
            }
            else
            {
                f.Problem(where, "\"pieces\" is missing");
            }
            if (f.TryChild(e, "doors", out JsonElement doors))
            {
                foreach (JsonProperty grade in doors.EnumerateObject())
                {
                    if (grade.Value.ValueKind == JsonValueKind.Number)
                    {
                        blast.SetDoor(grade.Name, grade.Value.GetDouble());
                    }
                    else
                    {
                        f.Problem(where + ".doors", $"\"{grade.Name}\" must be a number");
                    }
                }
            }
            else
            {
                f.Problem(where, "\"doors\" is missing");
            }
            loaded.Blasts.Add(blast);
        }
        BlastStats? Blast(string key, string where)
        {
            BlastStats? b = loaded.Blasts.FirstOrDefault(x => x.Key == key);
            if (b == null)
            {
                f.Problem(where, $"no blast \"{key}\" in {File}");
            }
            return b;
        }
        JsonElement[] throwables = f.List(root, "throwables", File);
        for (int i = 0; i < throwables.Length; i++)
        {
            JsonElement e = throwables[i];
            string key = f.Text(e, "item", $"{File}: throwables[{i}]");
            string where = $"{File}: throwable {JsonFields.Quote(key)}";
            f.OnlyKnown(e, where, "item", "blast", "fuse", "dud", "sticky", "place", "throw", "lob", "cooldown", "bounce", "radius", "note");
            ItemDef? item = resolve(key, where);
            double[] fuse = f.List(e, "fuse", where).Where(x => x.ValueKind == JsonValueKind.Number).Select(x => x.GetDouble()).ToArray();
            if (fuse.Length != 2)
            {
                f.Problem(where, "\"fuse\" must be [least, most] seconds");
                fuse = new double[] { 5, 5 };
            }
            string blastKey = f.Text(e, "blast", where);
            BlastStats? blast = Blast(blastKey, where);
            if (item == null)
            {
                continue;
            }
            var stats = new ThrowableStats
            {
                Item = item,
                BlastKey = blastKey,
                Blast = blast!,
                FuseLeast = fuse[0],
                FuseMost = fuse[1],
                DudChance = f.Double(e, "dud", where, 0),
                Sticky = f.Flag(e, "sticky", where, false),
                Placeable = f.Flag(e, "place", where, false),
                ThrowSpeed = f.Double(e, "throw", where),
                LobSpeed = f.Double(e, "lob", where),
                CooldownSeconds = f.Double(e, "cooldown", where, 1),
                Bounce = f.Double(e, "bounce", where, 0.3),
                Radius = f.Double(e, "radius", where, 0.08),
            };
            if (item.Throwable != null)
            {
                f.Problem(where, "is listed twice");
                continue;
            }
            item.Throwable = stats;
            loaded.Throwables.Add(stats);
        }
        foreach (BulletStats round in loaded.Bullets)
        {
            if (round.BlastKey != null)
            {
                round.Blast = Blast(round.BlastKey, $"weapons.json: ammo {JsonFields.Quote(round.Item.Key)}.blast");
            }
        }
    }

    /// <summary>The checks across explosives.json, weapons.json and building.json (GROUND.md §7).</summary>
    public static void Check(GameContent.Loaded c, BuildingData? building, List<string> problems)
    {
        void Problem(string where, string what) => problems.Add($"{where}: {what}");
        foreach (IGrouping<string, BlastStats> g in c.Blasts.GroupBy(b => b.Key).Where(g => g.Count() > 1))
        {
            Problem(File, $"blast \"{g.Key}\" is listed twice");
        }
        foreach (BlastStats b in c.Blasts)
        {
            string where = $"{File}: blast \"{b.Key}\"";
            if (b.Radius is <= 0 or > 20 || b.Inner is < 0 or > 1 || b.Splash is < 0 or > 1 || b.Flesh < 0 || b.Deployable < 0)
            {
                Problem(where, "radius 0 … 20 m, inner and splash 0 … 1, damage not negative");
            }
            foreach (string grade in b.PieceGrades)
            {
                if (b.Piece(grade, false) < 0 || b.Piece(grade, true) < 0)
                {
                    Problem(where, $"damage to {grade} must not be negative");
                }
            }
            if (building != null)
            {
                foreach (GradeDef grade in building.Grades.Where(g => !b.PieceGrades.Contains(g.Key)))
                {
                    Problem(where, $"no damage to {grade.Key} pieces (building.json's grades)");
                }
                foreach (string grade in b.PieceGrades.Where(k => building.Grade(k) == null))
                {
                    Problem(where, $"\"{grade}\" is not a grade of building.json");
                }
                foreach (string door in building.Doors.Select(d => d.Protection.Key).Distinct().Where(k => !b.DoorGrades.Contains(k)))
                {
                    Problem(where, $"no damage to doors of {door} (building.json's doors)");
                }
            }
        }
        foreach (ThrowableStats t in c.Throwables)
        {
            string where = $"{File}: throwable \"{t.Item.Key}\"";
            if (t.FuseLeast <= 0 || t.FuseMost < t.FuseLeast || t.FuseMost > 60 || t.DudChance is < 0 or >= 1 || t.ThrowSpeed is <= 0 or > 40 ||
                t.LobSpeed is <= 0 or > 40 || t.CooldownSeconds < 0 || t.Bounce is < 0 or > 1 || t.Radius is <= 0 or > 0.5)
            {
                Problem(where, "fuse 0 < least ≤ most ≤ 60 s, dud 0 … <1, throw and lob 0 … 40 m/s, bounce 0 … 1, radius 0 … 0.5 m");
            }
            if (t.Item.Firearm != null || t.Item.Bullet != null || t.Item.Melee != null || t.Item.Deploys != null)
            {
                Problem(where, "a throwable is not a gun, a round, a tool or a deployable");
            }
        }
        foreach (BulletStats round in c.Bullets.Where(r => r.BlastKey != null && r.Blast == null))
        {
            Problem($"weapons.json: ammo \"{round.Item.Key}\"", $"no blast \"{round.BlastKey}\"");
        }
        RaidAlertSettings a = c.RaidAlerts;
        if (a.CooldownSeconds < 0 || a.KeepDays <= 0 || a.MaxQueued < 1)
        {
            Problem($"{File}: alerts", "cooldown not negative, keep days and the queue positive");
        }
    }
}
