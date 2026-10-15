using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Hollow.Simulation.Content.Readers;

/// <summary>
/// weapons.json (G4): the firearms with their cadence, ballistics, spread and recoil, the kinds of rounds by calibre
/// and the attachments; attachments and rounds first, since the guns name them. Each hangs off its item.
/// </summary>
internal sealed class WeaponsReader : IContentReader
{
    public string File => "weapons.json";

    public void Read(ContentReading r)
    {
        JsonFields f = r.Fields;
        GameContent.Loaded loaded = r.Loaded;
        const string file = "weapons.json";
        JsonElement root = r.Root(file, "weapons", "ammo", "attachments");
        JsonElement[] attachments = f.List(root, "attachments", file);
        for (int i = 0; i < attachments.Length; i++)
        {
            JsonElement e = attachments[i];
            string key = f.Text(e, "item", $"{file}: attachments[{i}]");
            string where = $"{file}: attachment {JsonFields.Quote(key)}";
            f.OnlyKnown(e, where, "item", "slot", "zoom", "hipSpread", "aimedSpread", "recoil", "speed", "damage", "silenced", "light", "note");
            ItemDef? item = r.Resolve(key, where);
            string slotName = f.Text(e, "slot", where);
            AttachmentSlot? slot = slotName switch
            {
                "sight" => AttachmentSlot.Sight,
                "muzzle" => AttachmentSlot.Muzzle,
                "rail" => AttachmentSlot.Rail,
                _ => null,
            };
            if (slot == null)
            {
                f.Problem(where, $"slot \"{slotName}\" is not sight, muzzle or rail");
            }
            if (item == null)
            {
                continue;
            }
            var stats = new AttachmentStats
            {
                Item = item,
                Index = loaded.Attachments.Count,
                Slot = slot ?? AttachmentSlot.Sight,
                Zoom = f.Double(e, "zoom", where, 0),
                HipSpread = f.Double(e, "hipSpread", where, 1),
                AimedSpread = f.Double(e, "aimedSpread", where, 1),
                Recoil = f.Double(e, "recoil", where, 1),
                Speed = f.Double(e, "speed", where, 1),
                Damage = f.Double(e, "damage", where, 1),
                Silenced = f.Flag(e, "silenced", where, false),
                Light = f.Flag(e, "light", where, false),
            };
            if (item.Attachment != null)
            {
                f.Problem(where, "is listed twice");
                continue;
            }
            item.Attachment = stats;
            loaded.Attachments.Add(stats);
        }

        JsonElement[] ammo = f.List(root, "ammo", file);
        for (int i = 0; i < ammo.Length; i++)
        {
            JsonElement e = ammo[i];
            string key = f.Text(e, "item", $"{file}: ammo[{i}]");
            string where = $"{file}: ammo {JsonFields.Quote(key)}";
            f.OnlyKnown(e, where, "item", "calibre", "damage", "speed", "gravity", "bleed", "burn", "structure", "blast", "note");
            ItemDef? item = r.Resolve(key, where);
            if (item == null)
            {
                continue;
            }
            var stats = new BulletStats
            {
                Item = item,
                Calibre = f.Text(e, "calibre", where),
                Damage = f.Double(e, "damage", where, 1),
                Speed = f.Double(e, "speed", where, 1),
                Gravity = f.Double(e, "gravity", where, 1),
                Bleed = f.Double(e, "bleed", where, 0),
                Burn = f.Double(e, "burn", where, 0),
                Structure = f.Double(e, "structure", where, 0),
                BlastKey = f.Has(e, "blast") ? f.Text(e, "blast", where) : null,
            };
            if (item.Bullet != null)
            {
                f.Problem(where, "is listed twice");
                continue;
            }
            item.Bullet = stats;
            loaded.Bullets.Add(stats);
        }

        JsonElement[] weapons = f.List(root, "weapons", file);
        for (int i = 0; i < weapons.Length; i++)
        {
            JsonElement e = weapons[i];
            string key = f.Text(e, "item", $"{file}: weapons[{i}]");
            string where = $"{file}: weapon {JsonFields.Quote(key)}";
            f.OnlyKnown(e, where, "item", "calibre", "magazine", "fire", "interval", "reload", "equip", "speed", "drag", "gravity",
                "range", "lifetime", "damage", "falloff", "head", "body", "limb", "spread", "recoil", "zoom", "attachments", "note");
            ItemDef? item = r.Resolve(key, where);
            string mode = f.Text(e, "fire", where);
            if (mode is not ("semi" or "auto"))
            {
                f.Problem(where, $"fire \"{mode}\" is not semi or auto");
            }
            double[] falloff = f.Numbers(e, "falloff", where, 3) ?? new double[] { 0, 0, 1 };
            var stats = new FirearmStats
            {
                Item = item!,
                Calibre = f.Text(e, "calibre", where),
                Magazine = f.Integer(e, "magazine", where),
                Mode = mode == "auto" ? FireMode.Auto : FireMode.Semi,
                IntervalSeconds = f.Double(e, "interval", where),
                ReloadSeconds = f.Double(e, "reload", where),
                EquipSeconds = f.Double(e, "equip", where),
                Speed = f.Double(e, "speed", where),
                Drag = f.Double(e, "drag", where, 0),
                Gravity = f.Double(e, "gravity", where, 1),
                Range = f.Double(e, "range", where),
                LifetimeSeconds = f.Double(e, "lifetime", where),
                Damage = f.Double(e, "damage", where),
                FalloffStart = falloff[0],
                FalloffEnd = falloff[1],
                FalloffLeast = falloff[2],
                Head = f.Double(e, "head", where),
                Body = f.Double(e, "body", where),
                Limb = f.Double(e, "limb", where),
                Spread = Spread(f, e, where + ".spread"),
                Recoil = Recoil(f, e, where + ".recoil"),
                Zoom = f.Double(e, "zoom", where, 1),
                AttachmentKeys = f.Texts(e, "attachments", where),
            };
            if (item == null)
            {
                continue;
            }
            if (item.Firearm != null)
            {
                f.Problem(where, "is listed twice");
                continue;
            }
            stats.Attachments = stats.AttachmentKeys.Select(k => r.Resolve(k, where + ".attachments")).OfType<ItemDef>().ToArray();
            stats.Ammo = loaded.Bullets.Where(b => b.Calibre == stats.Calibre).Select(b => b.Item).ToArray();
            item.Firearm = stats;
            loaded.Firearms.Add(stats);
        }
    }

    private static SpreadStats Spread(JsonFields f, JsonElement weapon, string where)
    {
        if (!f.TryChild(weapon, "spread", out JsonElement e))
        {
            f.Problem(where, "is missing");
            return new SpreadStats();
        }
        f.OnlyKnown(e, where, "hip", "aimed", "bloom", "maxBloom", "bloomRecovery", "moving", "movingAimed", "airborne", "crouched", "note");
        return new SpreadStats
        {
            Hip = f.Double(e, "hip", where),
            Aimed = f.Double(e, "aimed", where),
            BloomPerShot = f.Double(e, "bloom", where, 0),
            MaxBloom = f.Double(e, "maxBloom", where, 0),
            BloomRecovery = f.Double(e, "bloomRecovery", where, 0),
            Moving = f.Double(e, "moving", where, 0),
            MovingAimed = f.Double(e, "movingAimed", where, 0),
            Airborne = f.Double(e, "airborne", where, 1),
            Crouched = f.Double(e, "crouched", where, 1),
        };
    }

    private static RecoilStats Recoil(JsonFields f, JsonElement weapon, string where)
    {
        if (!f.TryChild(weapon, "recoil", out JsonElement e))
        {
            f.Problem(where, "is missing");
            return new RecoilStats();
        }
        f.OnlyKnown(e, where, "mode", "pitch", "pattern", "pitchLimit", "yawLimit", "reset", "kick", "recoveryDelay", "recovery", "note");
        string mode = f.Text(e, "mode", where);
        if (mode is not ("vertical" or "pattern"))
        {
            f.Problem(where, $"mode \"{mode}\" is not vertical or pattern");
        }
        bool pattern = mode == "pattern";
        var steps = new List<RecoilStep>();
        if (pattern)
        {
            foreach (JsonElement step in f.List(e, "pattern", where))
            {
                if (step.ValueKind == JsonValueKind.Array && step.GetArrayLength() == 2 &&
                    step[0].ValueKind == JsonValueKind.Number && step[1].ValueKind == JsonValueKind.Number)
                {
                    steps.Add(new RecoilStep(step[0].GetDouble(), step[1].GetDouble()));
                }
                else
                {
                    f.Problem(where, "each pattern step is [pitch, yaw]");
                }
            }
        }
        return new RecoilStats
        {
            Mode = pattern ? RecoilMode.Pattern : RecoilMode.Vertical,
            PitchPerShot = pattern ? 0 : f.Double(e, "pitch", where),
            Pattern = steps,
            PitchLimit = f.Double(e, "pitchLimit", where),
            YawLimit = f.Double(e, "yawLimit", where),
            ResetSeconds = f.Double(e, "reset", where, pattern ? null : 0.4),
            KickSeconds = f.Double(e, "kick", where),
            RecoveryDelaySeconds = f.Double(e, "recoveryDelay", where),
            RecoverySeconds = f.Double(e, "recovery", where),
        };
    }

    /// <summary>The checks of G4 (RustMVP's valid_weapon_definition): each gun's numbers in range, rounds of its calibre, attachments it takes.</summary>
    public void Check(ContentChecking c)
    {
        GameContent.Loaded loaded = c.Loaded;
        foreach (FirearmStats g in loaded.Firearms)
        {
            string where = $"weapons.json: weapon \"{g.Item.Key}\"";
            if (!g.Item.HasCondition)
            {
                c.Problem(where, "a firearm has condition (shots before it breaks)");
            }
            if (g.Magazine is < 1 or > 255 || g.IntervalSeconds <= 0 || g.ReloadSeconds <= 0 || g.EquipSeconds < 0)
            {
                c.Problem(where, "magazine 1 … 255, interval and reload positive, equip not negative");
            }
            if (g.Speed is <= 0 or > 2000 || g.Drag is < 0 or > 5 || g.Gravity is < 0 or > 10 || g.Range <= 0 || g.LifetimeSeconds is <= 0 or > 5)
            {
                c.Problem(where, "speed 0 … 2000 m/s, drag 0 … 5, gravity 0 … 10, range positive, lifetime 0 … 5 s");
            }
            if (g.Damage < 0 || g.FalloffStart < 0 || g.FalloffEnd < g.FalloffStart || g.FalloffLeast is <= 0 or > 1 ||
                new[] { g.Head, g.Body, g.Limb }.Any(m => m is < 0 or > 10))
            {
                c.Problem(where, "damage not negative; falloff [start ≤ end, least 0 … 1]; multipliers 0 … 10");
            }
            SpreadStats sp = g.Spread;
            if (new[] { sp.Hip, sp.Aimed, sp.BloomPerShot, sp.MaxBloom, sp.BloomRecovery, sp.Moving, sp.MovingAimed, sp.Airborne, sp.Crouched }.Any(v => v < 0) ||
                sp.Hip + sp.MaxBloom >= 45 || sp.Aimed + sp.MaxBloom >= 45)
            {
                c.Problem(where, "spread: nothing negative, hip or aimed + the most bloom under 45°");
            }
            RecoilStats r = g.Recoil;
            if (r.PitchLimit is < 0 or > 85 || r.YawLimit is < 0 or > 180 || r.KickSeconds < 0 || r.KickSeconds > g.IntervalSeconds ||
                r.RecoveryDelaySeconds < 0 || r.RecoverySeconds < 0)
            {
                c.Problem(where, "recoil: limits up to 85° and 180°, the kick no longer than the interval, nothing negative");
            }
            if (r.Mode == RecoilMode.Vertical && (r.PitchPerShot < 0 || r.PitchPerShot > r.PitchLimit))
            {
                c.Problem(where, "recoil: the kick within the pitch limit");
            }
            if (r.Mode == RecoilMode.Pattern && (r.Pattern.Count is < 1 or > 32 || r.ResetSeconds <= 0 ||
                r.Pattern.Any(st => st.Pitch < 0 || st.Pitch > r.PitchLimit || System.Math.Abs(st.Yaw) > r.YawLimit)))
            {
                c.Problem(where, "recoil: a pattern of 1 … 32 steps within the limits, and a reset time");
            }
            if (g.Zoom < 1)
            {
                c.Problem(where, "zoom at least 1");
            }
            if (g.Ammo.Count == 0)
            {
                c.Problem(where, $"no rounds of calibre \"{g.Calibre}\"");
            }
            foreach (ItemDef a in g.Attachments.Where(a => a.Attachment == null))
            {
                c.Problem(where, $"\"{a.Key}\" is not an attachment");
            }
        }
        foreach (BulletStats b in loaded.Bullets)
        {
            string where = $"weapons.json: ammo \"{b.Item.Key}\"";
            if (!loaded.Firearms.Any(g => g.Calibre == b.Calibre))
            {
                c.Problem(where, $"no weapon fires calibre \"{b.Calibre}\"");
            }
            if (b.Damage is <= 0 or > 10 || b.Speed is <= 0 or > 10 || b.Gravity is < 0 or > 10 || b.Bleed is < 0 or > 2 || b.Burn is < 0 or > 100 ||
                b.Structure is < 0 or > 1)
            {
                c.Problem(where, "damage and speed 0 … 10 times, gravity 0 … 10 times, bleed 0 … 2, burn 0 … 100, structure 0 … 1");
            }
            if (b.Item.Ammo != null || b.Item.Firearm != null)
            {
                c.Problem(where, "a round is not an arrow or a gun");
            }
        }
        foreach (AttachmentStats a in loaded.Attachments)
        {
            string where = $"weapons.json: attachment \"{a.Item.Key}\"";
            if ((a.Zoom != 0 && a.Zoom < 1) || new[] { a.HipSpread, a.AimedSpread, a.Recoil, a.Speed, a.Damage }.Any(m => m is <= 0 or > 10))
            {
                c.Problem(where, "zoom 0 (the gun's) or at least 1; multipliers 0 … 10");
            }
            if (!loaded.Firearms.Any(g => g.Attachments.Contains(a.Item)))
            {
                c.Problem(where, "no weapon takes it");
            }
        }
        if (loaded.Attachments.Count > 8)
        {
            c.Problem("weapons.json: attachments", "at most 8 (others see a held gun's attachments as 8 bits)");
        }
    }
}
