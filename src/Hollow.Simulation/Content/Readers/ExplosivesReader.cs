using System.Linq;
using System.Text.Json;
using Hollow.Simulation.Building;

namespace Hollow.Simulation.Content.Readers;

/// <summary>Reads explosives.json (G5) and links rounds' blasts (weapons.json) to it; checks both against building.json's grades.</summary>
internal sealed class ExplosivesReader : IContentReader
{
    public string File => "explosives.json";

    public void Read(ContentReading r)
    {
        JsonFields f = r.Fields;
        GameContent.Loaded loaded = r.Loaded;
        JsonElement root = r.Documents[File];
        if (root.ValueKind == JsonValueKind.Object)
        {
            f.OnlyKnown(root, File, "source", "note", "alerts", "blasts", "fires", "throwables");
        }
        ReadFires(f, root, loaded);
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
            f.OnlyKnown(e, where, "key", "radius", "inner", "splash", "flesh", "direct", "deployable", "pieces", "doors", "fire", "note");
            string? fireKey = f.Has(e, "fire") ? f.Text(e, "fire", where) : null;
            var blast = new BlastStats
            {
                Key = key,
                Radius = f.Double(e, "radius", where),
                Inner = f.Double(e, "inner", where, 0.4),
                Splash = f.Double(e, "splash", where, 0),
                Flesh = f.Double(e, "flesh", where, 0),
                Direct = f.Double(e, "direct", where, 0),
                Deployable = f.Double(e, "deployable", where, 0),
                FireKey = fireKey,
                Fire = fireKey == null ? null : loaded.Fires.FirstOrDefault(x => x.Key == fireKey),
            };
            if (fireKey != null && blast.Fire == null)
            {
                f.Problem(where, $"no fire \"{fireKey}\" in {File}'s fires");
            }
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
            f.OnlyKnown(e, where, "item", "blast", "fuse", "dud", "sticky", "placement", "throw", "lob", "cooldown", "bounce", "radius", "note");
            ItemDef? item = r.Resolve(key, where);
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

    // The fires blasts leave (IW4, IRON_WAKE.md §7): the rules every fire follows, and the kinds blasts name.
    private void ReadFires(JsonFields f, JsonElement root, GameContent.Loaded loaded)
    {
        if (!f.TryChild(root, "fires", out JsonElement fires))
        {
            return;
        }
        string where = $"{File}: fires";
        f.OnlyKnown(fires, where, "most", "runsDown", "steepest", "kinds", "note");
        loaded.FireRules = new FireRules
        {
            Most = f.Integer(fires, "most", where, 64),
            RunsDown = f.Double(fires, "runsDown", where, 12),
            SteepestDegrees = f.Double(fires, "steepest", where, 50),
        };
        JsonElement[] kinds = f.List(fires, "kinds", where);
        for (int i = 0; i < kinds.Length; i++)
        {
            JsonElement e = kinds[i];
            string key = f.Text(e, "key", $"{where}.kinds[{i}]");
            string kw = $"{where}: kind {JsonFields.Quote(key)}";
            f.OnlyKnown(e, kw, "key", "radius", "inner", "seconds", "every", "flesh", "burning", "deployables", "building", "note");
            loaded.Fires.Add(new FireDef
            {
                Key = key,
                Radius = f.Double(e, "radius", kw),
                Inner = f.Double(e, "inner", kw, 0.4),
                Seconds = f.Double(e, "seconds", kw),
                Every = f.Double(e, "every", kw, 0.5),
                Flesh = f.Double(e, "flesh", kw, 0),
                Burning = f.Double(e, "burning", kw, 0),
                Deployables = f.Double(e, "deployables", kw, 0),
                Building = f.Double(e, "building", kw, 0),
            });
        }
    }

    /// <summary>The checks across explosives.json, weapons.json and building.json (GROUND.md §7).</summary>
    public void Check(ContentChecking c)
    {
        GameContent.Loaded loaded = c.Loaded;
        BuildingData? building = loaded.Building;
        foreach (IGrouping<string, FireDef> g in loaded.Fires.GroupBy(x => x.Key).Where(g => g.Count() > 1))
        {
            c.Problem($"{File}: fires", $"kind \"{g.Key}\" is listed twice");
        }
        foreach (FireDef fire in loaded.Fires)
        {
            if (fire.Radius is <= 0 or > 20 || fire.Inner is < 0 or > 1 || fire.Seconds is <= 0 or > 300 || fire.Every is < 0.1 or > 5 ||
                fire.Flesh < 0 || fire.Burning < 0 || fire.Deployables < 0 || fire.Building < 0)
            {
                c.Problem($"{File}: fires: kind \"{fire.Key}\"", "radius 0 … 20 m, inner 0 … 1, 0 … 300 s, every 0.1 … 5 s, damage not negative");
            }
        }
        FireRules rules = loaded.FireRules;
        if (rules.Most is < 1 or > 1024 || rules.RunsDown is < 0 or > 50 || rules.SteepestDegrees is < 0 or > 90)
        {
            c.Problem($"{File}: fires", "most 1 … 1024, runsDown 0 … 50 m, steepest 0 … 90°");
        }
        foreach (IGrouping<string, BlastStats> g in loaded.Blasts.GroupBy(b => b.Key).Where(g => g.Count() > 1))
        {
            c.Problem(File, $"blast \"{g.Key}\" is listed twice");
        }
        foreach (BlastStats b in loaded.Blasts)
        {
            string where = $"{File}: blast \"{b.Key}\"";
            if (b.Radius is <= 0 or > 20 || b.Inner is < 0 or > 1 || b.Splash is < 0 or > 1 || b.Flesh < 0 || b.Direct < 0 || b.Deployable < 0)
            {
                c.Problem(where, "radius 0 … 20 m, inner and splash 0 … 1, damage not negative");
            }
            foreach (string grade in b.PieceGrades)
            {
                if (b.Piece(grade, false) < 0 || b.Piece(grade, true) < 0)
                {
                    c.Problem(where, $"damage to {grade} must not be negative");
                }
            }
            if (building != null)
            {
                foreach (GradeDef grade in building.Grades.Where(g => !b.PieceGrades.Contains(g.Key)))
                {
                    c.Problem(where, $"no damage to {grade.Key} pieces (building.json's grades)");
                }
                foreach (string grade in b.PieceGrades.Where(k => building.Grade(k) == null))
                {
                    c.Problem(where, $"\"{grade}\" is not a grade of building.json");
                }
                foreach (string door in building.Doors.Select(d => d.Protection.Key).Distinct().Where(k => !b.DoorGrades.Contains(k)))
                {
                    c.Problem(where, $"no damage to doors of {door} (building.json's doors)");
                }
            }
        }
        foreach (ThrowableStats t in loaded.Throwables)
        {
            string where = $"{File}: throwable \"{t.Item.Key}\"";
            if (t.FuseLeast <= 0 || t.FuseMost < t.FuseLeast || t.FuseMost > 60 || t.DudChance is < 0 or >= 1 || t.ThrowSpeed is <= 0 or > 40 ||
                t.LobSpeed is <= 0 or > 40 || t.CooldownSeconds < 0 || t.Bounce is < 0 or > 1 || t.Radius is <= 0 or > 0.5)
            {
                c.Problem(where, "fuse 0 < least ≤ most ≤ 60 s, dud 0 … <1, throw and lob 0 … 40 m/s, bounce 0 … 1, radius 0 … 0.5 m");
            }
            if (t.Item.Firearm != null || t.Item.Bullet != null || t.Item.Melee != null || t.Item.Deploys != null)
            {
                c.Problem(where, "a throwable is not a gun, a round, a tool or a deployable");
            }
        }
        foreach (BulletStats round in loaded.Bullets.Where(r => r.BlastKey != null && r.Blast == null))
        {
            c.Problem($"weapons.json: ammo \"{round.Item.Key}\"", $"no blast \"{round.BlastKey}\"");
        }
        RaidAlertSettings a = loaded.RaidAlerts;
        if (a.CooldownSeconds < 0 || a.KeepDays <= 0 || a.MaxQueued < 1)
        {
            c.Problem($"{File}: alerts", "cooldown not negative, keep days and the queue positive");
        }
    }
}
