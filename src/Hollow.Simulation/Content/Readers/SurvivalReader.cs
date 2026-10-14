using System;
using System.Linq;
using System.Text.Json;

namespace Hollow.Simulation.Content.Readers;

/// <summary>
/// survival.json: vitals, metabolism, bleeding, burning, the wounded state, temperature, the climate stand-in, the
/// body-part multipliers and rewind limit, the lifetimes of loose things (GROUND_SYSTEMS.md §3.7), the bags dropped
/// items lie in, the reaches of actions on things (§3.4), the action rate limits and the arrows that stick.
/// </summary>
internal sealed class SurvivalReader : IContentReader
{
    public string File => "survival.json";

    public void Read(ContentReading r)
    {
        JsonFields f = r.Fields;
        GameContent.Loaded loaded = r.Loaded;
        JsonElement root = r.Root("survival.json", "vitals", "metabolism", "bleeding", "burning", "wounded", "temperature",
            "climateStandIn", "combat", "world", "bag", "rateLimits", "reach", "lifetimes", "arrows");
        string w = "survival.json";
        JsonElement Section(string name, params string[] fields)
        {
            if (!f.TryChild(root, name, out JsonElement child))
            {
                if (r.Documents["survival.json"].ValueKind == JsonValueKind.Object)
                {
                    f.Problem(w, $"\"{name}\" is missing");
                }
                return default;
            }
            f.OnlyKnown(child, $"{w}: {name}", fields.Append("note").ToArray());
            return child;
        }
        JsonElement vitals = Section("vitals", "maxHealth", "maxCalories", "maxHydration", "spawn");
        JsonElement spawn = f.TryChild(vitals, "spawn", out JsonElement s) ? s : default;
        JsonElement m = Section("metabolism", "caloriesPerSecond", "hydrationPerSecond", "sprintFactor", "coldFactor",
            "starvationDamagePerSecond", "thirstDamagePerSecond", "regenMinCalories", "regenMinHydration", "regenPerSecond",
            "comfortRegenFactor", "healPerSecond");
        JsonElement b = Section("bleeding", "damagePerSecond", "clotPerSecond");
        JsonElement t = Section("temperature", "comfortable", "coldScale", "coldDamageFrom", "coldDamagePerSecond", "wetCooling", "wetSeconds", "fireWarmsTo");
        JsonElement c = Section("climateStandIn", "meanCelsius", "daySwing", "lapsePerKilometre", "dayLengthSeconds");
        JsonElement k = Section("combat", "head", "body", "limb", "maxRewindSeconds", "equipSeconds");
        JsonElement o = Section("world", "beachCooldownSeconds", "respawnKit");
        JsonElement lifetimes = Section("lifetimes", "bag", "body", "dud", "arrow");
        LifetimeRule Lifetime(string name)
        {
            string where = $"{w}: lifetimes.{name}";
            if (!f.TryChild(lifetimes, name, out JsonElement e))
            {
                f.Problem(where, "is missing");
                return new LifetimeRule(300, LifetimeRestart.None, true);
            }
            f.OnlyKnown(e, where, "seconds", "restart", "emptied", "note");
            string restart = f.Text(e, "restart", where, "none");
            if (restart is not ("none" or "deposit"))
            {
                f.Problem(where, $"restart \"{restart}\" (known: none, deposit)");
            }
            float seconds = f.Number(e, "seconds", where);
            if (seconds is <= 0 or > 86400)
            {
                f.Problem(where, "seconds 0 … 86400");
            }
            return new LifetimeRule(seconds, restart == "deposit" ? LifetimeRestart.Deposit : LifetimeRestart.None, f.Flag(e, "emptied", where, false));
        }
        JsonElement reach = Section("reach", "interact", "open", "keepOpen", "help", "invite", "station");
        JsonElement arrows = Section("arrows", "most", "depth");
        JsonElement bag = Section("bag", "name", "slots", "mergeRadius");
        JsonElement limits = Section("rateLimits", "intentBurst", "intentsPerSecond", "gunBurst", "gunActionsPerSecond");
        JsonElement burning = Section("burning", "damagePerSecond");
        JsonElement wounded = Section("wounded", "maxDamage", "health", "seconds", "recoverChance", "fedBonus");
        (float Least, float Most) Pair(JsonElement e, string name, string where, float least, float most)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out JsonElement v))
            {
                if (e.ValueKind == JsonValueKind.Object)
                {
                    f.Problem(where, $"\"{name}\" is missing");
                }
                return (least, most);
            }
            if (v.ValueKind == JsonValueKind.Array && v.GetArrayLength() == 2 && v[0].ValueKind == JsonValueKind.Number &&
                v[1].ValueKind == JsonValueKind.Number)
            {
                return ((float)v[0].GetDouble(), (float)v[1].GetDouble());
            }
            f.Problem(where, $"\"{name}\" must be [least, most]");
            return (least, most);
        }
        (float woundedHealthLeast, float woundedHealthMost) = Pair(wounded, "health", w + ": wounded", 7, 12);
        (float woundedSecondsLeast, float woundedSecondsMost) = Pair(wounded, "seconds", w + ": wounded", 40, 50);
        loaded.Survival = new SurvivalTuning
        {
            MaxHealth = f.Number(vitals, "maxHealth", w + ": vitals", 100),
            MaxCalories = f.Number(vitals, "maxCalories", w + ": vitals", 500),
            MaxHydration = f.Number(vitals, "maxHydration", w + ": vitals", 250),
            SpawnHealth = f.Number(spawn, "health", w + ": vitals.spawn", 60),
            SpawnCalories = f.Number(spawn, "calories", w + ": vitals.spawn", 260),
            SpawnHydration = f.Number(spawn, "hydration", w + ": vitals.spawn", 160),
            CaloriesPerSecond = f.Number(m, "caloriesPerSecond", w + ": metabolism"),
            HydrationPerSecond = f.Number(m, "hydrationPerSecond", w + ": metabolism"),
            SprintFactor = f.Number(m, "sprintFactor", w + ": metabolism", 1),
            ColdFactor = f.Number(m, "coldFactor", w + ": metabolism", 1),
            StarvationDamagePerSecond = f.Number(m, "starvationDamagePerSecond", w + ": metabolism"),
            ThirstDamagePerSecond = f.Number(m, "thirstDamagePerSecond", w + ": metabolism"),
            RegenMinCalories = f.Number(m, "regenMinCalories", w + ": metabolism"),
            RegenMinHydration = f.Number(m, "regenMinHydration", w + ": metabolism"),
            RegenPerSecond = f.Number(m, "regenPerSecond", w + ": metabolism"),
            ComfortRegenFactor = f.Number(m, "comfortRegenFactor", w + ": metabolism", 1),
            HealPerSecond = f.Number(m, "healPerSecond", w + ": metabolism", 1),
            BleedDamagePerSecond = f.Number(b, "damagePerSecond", w + ": bleeding"),
            BleedClotPerSecond = f.Number(b, "clotPerSecond", w + ": bleeding"),
            ComfortableCelsius = f.Number(t, "comfortable", w + ": temperature"),
            ColdScale = f.Number(t, "coldScale", w + ": temperature"),
            ColdDamageFrom = f.Number(t, "coldDamageFrom", w + ": temperature"),
            ColdDamagePerSecond = f.Number(t, "coldDamagePerSecond", w + ": temperature"),
            WetCooling = f.Number(t, "wetCooling", w + ": temperature", 0),
            WetSeconds = f.Number(t, "wetSeconds", w + ": temperature", 0),
            FireWarmsTo = f.Number(t, "fireWarmsTo", w + ": temperature", 25),
            ClimateMeanCelsius = f.Number(c, "meanCelsius", w + ": climateStandIn"),
            ClimateDaySwing = f.Number(c, "daySwing", w + ": climateStandIn"),
            ClimateLapsePerKilometre = f.Number(c, "lapsePerKilometre", w + ": climateStandIn"),
            DayLengthSeconds = f.Number(c, "dayLengthSeconds", w + ": climateStandIn"),
            HeadMultiplier = f.Number(k, "head", w + ": combat"),
            BodyMultiplier = f.Number(k, "body", w + ": combat"),
            LimbMultiplier = f.Number(k, "limb", w + ": combat"),
            MaxRewindSeconds = f.Number(k, "maxRewindSeconds", w + ": combat"),
            EquipSeconds = f.Number(k, "equipSeconds", w + ": combat", 0.3f),
            BagLifetime = Lifetime("bag"),
            BodyLifetime = Lifetime("body"),
            DudLifetime = Lifetime("dud"),
            ArrowLifetime = Lifetime("arrow"),
            ArrowsMost = Math.Clamp(f.Integer(arrows, "most", w + ": arrows"), 0, 65536),
            ArrowDepth = Math.Clamp(f.Number(arrows, "depth", w + ": arrows"), 0, 0.5f),
            BagName = f.Text(bag, "name", w + ": bag"),
            BagSlots = Math.Clamp(f.Integer(bag, "slots", w + ": bag"), 1, 255),
            BagMergeRadius = f.Number(bag, "mergeRadius", w + ": bag"),
            InteractReach = f.Number(reach, "interact", w + ": reach"),
            OpenReach = f.Number(reach, "open", w + ": reach"),
            KeepOpenReach = f.Number(reach, "keepOpen", w + ": reach"),
            HelpReach = f.Number(reach, "help", w + ": reach"),
            InviteReach = f.Number(reach, "invite", w + ": reach"),
            StationReach = f.Number(reach, "station", w + ": reach"),
            BeachCooldownSeconds = f.Number(o, "beachCooldownSeconds", w + ": world", 0),
            RespawnKitKey = f.Text(o, "respawnKit", w + ": world"),
            IntentBurst = f.Integer(limits, "intentBurst", w + ": rateLimits", 16),
            IntentsPerSecond = f.Number(limits, "intentsPerSecond", w + ": rateLimits", 20),
            GunBurst = f.Integer(limits, "gunBurst", w + ": rateLimits", 20),
            GunActionsPerSecond = f.Number(limits, "gunActionsPerSecond", w + ": rateLimits", 30),
            BurnDamagePerSecond = f.Number(burning, "damagePerSecond", w + ": burning"),
            WoundMaxDamage = f.Number(wounded, "maxDamage", w + ": wounded"),
            WoundedHealthLeast = woundedHealthLeast,
            WoundedHealthMost = woundedHealthMost,
            WoundedSecondsLeast = woundedSecondsLeast,
            WoundedSecondsMost = woundedSecondsMost,
            WoundedRecoverChance = f.Number(wounded, "recoverChance", w + ": wounded"),
            WoundedFedBonus = f.Number(wounded, "fedBonus", w + ": wounded", 0),
        };
    }

    /// <summary>The respawn kit exists; the spawn vitals, rewind, rate limits, the wounded state, burning and the reaches in range.</summary>
    public void Check(ContentChecking c)
    {
        GameContent.Loaded loaded = c.Loaded;
        SurvivalTuning s = loaded.Survival;
        if (!loaded.Loot.Any(t => t.Key == s.RespawnKitKey))
        {
            c.Problem("survival.json: world.respawnKit", $"no loot table \"{s.RespawnKitKey}\"");
        }
        if (s.MaxHealth <= 0 || s.SpawnHealth <= 0 || s.SpawnHealth > s.MaxHealth || s.SpawnCalories > s.MaxCalories ||
            s.SpawnHydration > s.MaxHydration || s.MaxRewindSeconds is < 0 or > 1 || s.IntentBurst < 1 || s.IntentsPerSecond <= 0)
        {
            c.Problem("survival.json", "spawn vitals within the maxima; rewind 0 … 1 s; positive rate limits");
        }
        if (s.WoundMaxDamage < 0 || s.WoundedHealthLeast <= 0 || s.WoundedHealthMost < s.WoundedHealthLeast || s.WoundedHealthMost > s.MaxHealth ||
            s.WoundedSecondsLeast <= 0 || s.WoundedSecondsMost < s.WoundedSecondsLeast || s.WoundedRecoverChance is < 0 or > 1 ||
            s.WoundedFedBonus is < 0 or > 1 || s.BurnDamagePerSecond < 0 || s.GunBurst < 1 || s.GunActionsPerSecond <= 0)
        {
            c.Problem("survival.json", "wounded: health and seconds [least ≤ most], chances 0 … 1; burning and the gun budget positive");
        }
        if (new[] { s.InteractReach, s.OpenReach, s.HelpReach, s.InviteReach, s.StationReach }.Any(r => r is <= 0 or > 10) ||
            s.KeepOpenReach < s.OpenReach)
        {
            c.Problem("survival.json", "reach: every reach 0 … 10 m, and keepOpen at least open");
        }
    }
}
