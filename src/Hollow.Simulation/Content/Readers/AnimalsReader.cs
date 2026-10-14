using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Hollow.Simulation.Combat;
using Hollow.Surface;

namespace Hollow.Simulation.Content.Readers;

/// <summary>Reads animals.json (GROUND.md §6.9) and links its yields and tools to items.</summary>
internal sealed class AnimalsReader : IContentReader
{
    public string File => "animals.json";

    public void Read(ContentReading r)
    {
        JsonFields f = r.Fields;
        GameContent.Loaded loaded = r.Loaded;
        JsonElement root = r.Documents[File];
        if (root.ValueKind != JsonValueKind.Object)
        {
            return;
        }
        f.OnlyKnown(root, File, "source", "note", "senses", "spawning", "harvest", "species");
        if (f.TryChild(root, "senses", out JsonElement senses))
        {
            string where = $"{File}: senses";
            f.OnlyKnown(senses, where, "note", "noise", "crouchSight", "stillSight", "near");
            JsonElement noise = f.TryChild(senses, "noise", out JsonElement n) ? n : default;
            if (noise.ValueKind == JsonValueKind.Object)
            {
                f.OnlyKnown(noise, where + ".noise", "sprint", "walk", "crouch", "swim", "gunshot", "silenced", "bow", "melee", "gather", "explosion");
            }
            float Noise(string name, float fallback) => noise.ValueKind == JsonValueKind.Object ? f.Number(noise, name, where + ".noise", fallback) : fallback;
            loaded.AnimalSenses = new AnimalSenses
            {
                Sprint = Noise("sprint", 28),
                Walk = Noise("walk", 10),
                Crouch = Noise("crouch", 3),
                Swim = Noise("swim", 12),
                Gunshot = Noise("gunshot", 180),
                Silenced = Noise("silenced", 45),
                Bow = Noise("bow", 14),
                Melee = Noise("melee", 18),
                Gather = Noise("gather", 30),
                Explosion = Noise("explosion", 400),
                CrouchSight = f.Number(senses, "crouchSight", where, 0.55f),
                StillSight = f.Number(senses, "stillSight", where, 0.8f),
                Near = f.Number(senses, "near", where, 5),
            };
        }
        if (f.TryChild(root, "spawning", out JsonElement spawning))
        {
            string where = $"{File}: spawning";
            f.OnlyKnown(spawning, where, "note", "area", "activate", "deactivate", "linger", "awayFromPlayers", "max");
            loaded.AnimalSpawning = new AnimalSpawning
            {
                AreaSize = f.Number(spawning, "area", where, 256),
                Activate = f.Number(spawning, "activate", where, 380),
                Deactivate = f.Number(spawning, "deactivate", where, 560),
                LingerSeconds = f.Number(spawning, "linger", where, 30),
                AwayFromPlayers = f.Number(spawning, "awayFromPlayers", where, 70),
                MaxAnimals = f.Integer(spawning, "max", where, 600),
            };
        }
        if (f.TryChild(root, "harvest", out JsonElement harvest))
        {
            string where = $"{File}: harvest";
            f.OnlyKnown(harvest, where, "note", "default", "tools");
            var rates = new HarvestRates { DefaultShare = f.Number(harvest, "default", where, 0.4f) };
            if (f.TryChild(harvest, "tools", out JsonElement tools))
            {
                foreach (JsonProperty tool in tools.EnumerateObject())
                {
                    ItemDef? item = r.Resolve(tool.Name, where + ".tools");
                    if (tool.Value.ValueKind != JsonValueKind.Number)
                    {
                        f.Problem(where + ".tools", $"\"{tool.Name}\" must be a number");
                    }
                    else if (item != null)
                    {
                        rates.Set(item, (float)tool.Value.GetDouble());
                    }
                }
            }
            loaded.HarvestRates = rates;
        }
        JsonElement[] species = f.List(root, "species", File);
        for (int i = 0; i < species.Length; i++)
        {
            JsonElement e = species[i];
            string key = f.Text(e, "key", $"{File}: species[{i}]");
            string where = $"{File}: {JsonFields.Quote(key)}";
            f.OnlyKnown(e, where, "id", "key", "name", "note", "health", "size", "speed", "senses", "temperament", "behaviour", "group",
                "cohesion", "attack", "habitat", "density", "slope", "respawn", "corpse", "harvest", "prey");
            int id = f.Integer(e, "id", where);
            if (id is < 1 or > 15)
            {
                f.Problem(where, $"id {id} must be 1 … 15");
            }
            JsonElement size = Child(f, e, "size", where);
            JsonElement speed = Child(f, e, "speed", where);
            JsonElement sense = Child(f, e, "senses", where);
            JsonElement behaviour = Child(f, e, "behaviour", where);
            JsonElement habitat = Child(f, e, "habitat", where);
            JsonElement corpse = Child(f, e, "harvest", where);
            f.OnlyKnown(size, where + ".size", "length", "height", "radius", "head", "headAhead", "headUp");
            f.OnlyKnown(speed, where + ".speed", "walk", "trot", "run", "turn", "accel");
            f.OnlyKnown(sense, where + ".senses", "sight", "cone", "hearing");
            f.OnlyKnown(behaviour, where + ".behaviour", "flee", "calm", "space", "fleeHealth", "chase", "chaseRange", "hunger", "hunt");
            f.OnlyKnown(habitat, where + ".habitat", "biomes", "open", "wooded");
            f.OnlyKnown(corpse, where + ".harvest", "health", "yield");
            int[] group = f.Integers(e, "group", where);
            if (group.Length != 2)
            {
                f.Problem(where, "\"group\" must be [least, most]");
                group = new[] { 1, 1 };
            }
            AttackStats? attack = null;
            if (f.TryChild(e, "attack", out JsonElement a) && a.ValueKind == JsonValueKind.Object)
            {
                string at = where + ".attack";
                f.OnlyKnown(a, at, "kind", "damage", "reach", "rate", "windup", "bleed", "region", "animals");
                attack = new AttackStats
                {
                    Kind = Parse(f, f.Text(a, "kind", at), at + ".kind", AttackKind.Bite),
                    Damage = f.Number(a, "damage", at),
                    Reach = f.Number(a, "reach", at),
                    RateSeconds = f.Number(a, "rate", at),
                    WindupSeconds = f.Number(a, "windup", at),
                    Bleed = f.Number(a, "bleed", at, 0),
                    Region = Parse(f, f.Text(a, "region", at, "body"), at + ".region", HitRegion.Body),
                    AnimalDamage = f.Number(a, "animals", at, f.Number(a, "damage", at)),
                };
            }
            var s = new AnimalSpecies
            {
                Id = (byte)Math.Clamp(id, 0, 15),
                Key = key,
                Name = f.Text(e, "name", where),
                Health = f.Number(e, "health", where),
                Length = f.Number(size, "length", where + ".size"),
                Height = f.Number(size, "height", where + ".size"),
                Radius = f.Number(size, "radius", where + ".size"),
                HeadRadius = f.Number(size, "head", where + ".size"),
                HeadAhead = f.Number(size, "headAhead", where + ".size", f.Number(size, "length", where + ".size", 1) * 0.46f),
                HeadUp = f.Number(size, "headUp", where + ".size", f.Number(size, "height", where + ".size", 1) * 0.92f),
                WalkSpeed = f.Number(speed, "walk", where + ".speed"),
                TrotSpeed = f.Number(speed, "trot", where + ".speed"),
                RunSpeed = f.Number(speed, "run", where + ".speed"),
                TurnRate = f.Number(speed, "turn", where + ".speed") * (MathF.PI / 180),
                Acceleration = f.Number(speed, "accel", where + ".speed"),
                Sight = f.Number(sense, "sight", where + ".senses"),
                Cone = f.Number(sense, "cone", where + ".senses") * (MathF.PI / 180),
                Hearing = f.Number(sense, "hearing", where + ".senses", 1),
                Temperament = Parse(f, f.Text(e, "temperament", where), where + ".temperament", Temperament.Flee),
                FleeDistance = f.Number(behaviour, "flee", where + ".behaviour", 0),
                Calm = f.Number(behaviour, "calm", where + ".behaviour", 60),
                PersonalSpace = f.Number(behaviour, "space", where + ".behaviour", 0),
                FleeHealth = f.Number(behaviour, "fleeHealth", where + ".behaviour", 0),
                ChaseSeconds = f.Number(behaviour, "chase", where + ".behaviour", 0),
                ChaseRange = f.Number(behaviour, "chaseRange", where + ".behaviour", 0),
                HungerRate = f.Number(behaviour, "hunger", where + ".behaviour", 0),
                HuntHunger = f.Number(behaviour, "hunt", where + ".behaviour", 1),
                Prey = f.Flag(e, "prey", where, false),
                GroupLeast = group[0],
                GroupMost = group[1],
                Cohesion = f.Number(e, "cohesion", where, 0) is > 0 and var c ? c : 10 + group[1] * 1.5f,
                Attack = attack,
                Density = f.Number(e, "density", where),
                Open = f.Number(habitat, "open", where + ".habitat", 1),
                Wooded = f.Number(habitat, "wooded", where + ".habitat", 1),
                MaxSlope = f.Number(e, "slope", where, 38) * (MathF.PI / 180),
                RespawnSeconds = f.Number(e, "respawn", where),
                Carcass = new LifetimeRule(f.Number(e, "corpse", where, 300), LifetimeRestart.None, EndsWhenEmptied: true),
                HarvestHealth = f.Number(corpse, "health", where + ".harvest"),
            };
            if (f.TryChild(habitat, "biomes", out JsonElement biomes))
            {
                foreach (JsonProperty b in biomes.EnumerateObject())
                {
                    if (Enum.TryParse(b.Name, ignoreCase: false, out Biome biome) && b.Value.ValueKind == JsonValueKind.Number)
                    {
                        s.SetBiome(biome, (float)b.Value.GetDouble());
                    }
                    else
                    {
                        f.Problem(where + ".habitat.biomes", $"\"{b.Name}\" must be a biome ({string.Join(", ", Enum.GetNames<Biome>())}) with a number");
                    }
                }
            }
            var yields = new List<ItemAmount>();
            foreach (JsonElement y in f.List(corpse, "yield", where + ".harvest"))
            {
                if (f.Tuple(y, where + ".harvest.yield", 1, out string itemKey, out double[] values) && r.Resolve(itemKey, where + ".harvest.yield") is { } item)
                {
                    yields.Add(new ItemAmount(item, (int)Math.Round(values[0])));
                }
            }
            s.Harvest = yields;
            loaded.Animals.Add(s);
        }
    }

    // A required object, or an empty one (the missing field is reported by its numbers).
    private static JsonElement Child(JsonFields f, JsonElement e, string name, string where)
    {
        if (f.TryChild(e, name, out JsonElement child) && child.ValueKind == JsonValueKind.Object)
        {
            return child;
        }
        f.Problem(where, $"\"{name}\" is missing");
        using JsonDocument empty = JsonDocument.Parse("{}");
        return empty.RootElement.Clone();
    }

    private static T Parse<T>(JsonFields f, string text, string where, T fallback) where T : struct, Enum
    {
        if (Enum.TryParse(text, ignoreCase: true, out T value) && Enum.IsDefined(value))
        {
            return value;
        }
        f.Problem(where, $"\"{text}\" must be one of {string.Join(", ", Enum.GetNames<T>().Select(n => n.ToLowerInvariant()))}");
        return fallback;
    }

    /// <summary>The checks of GROUND.md §7 for animals.json: unique IDs and keys, sensible ranges, yields that are items.</summary>
    public void Check(ContentChecking c)
    {
        GameContent.Loaded loaded = c.Loaded;
        foreach (IGrouping<byte, AnimalSpecies> g in loaded.Animals.GroupBy(s => s.Id).Where(g => g.Count() > 1))
        {
            c.Problem(File, $"id {g.Key} is used by {string.Join(", ", g.Select(s => s.Key))}");
        }
        foreach (IGrouping<string, AnimalSpecies> g in loaded.Animals.GroupBy(s => s.Key).Where(g => g.Count() > 1))
        {
            c.Problem(File, $"\"{g.Key}\" is used twice");
        }
        foreach (AnimalSpecies s in loaded.Animals)
        {
            string where = $"{File}: \"{s.Key}\"";
            if (s.Health <= 0 || s.Length <= 0 || s.Height <= 0 || s.Radius <= 0 || s.HeadRadius <= 0 || s.Radius > s.Height)
            {
                c.Problem(where, "health and size positive; the body's radius under its height");
            }
            if (s.WalkSpeed <= 0 || s.TrotSpeed < s.WalkSpeed || s.RunSpeed < s.TrotSpeed || s.RunSpeed > 25 || s.TurnRate <= 0 || s.Acceleration <= 0)
            {
                c.Problem(where, "speeds: 0 < walk ≤ trot ≤ run ≤ 25 m/s; turning and acceleration positive");
            }
            if (s.Sight <= 0 || s.Sight > 300 || s.Cone <= 0 || s.Cone > 2 * MathF.PI + 1e-4f || s.Hearing < 0)
            {
                c.Problem(where, "senses: sight 0 … 300 m, a cone up to 360°, hearing not negative");
            }
            if (s.GroupLeast < 1 || s.GroupMost < s.GroupLeast || s.GroupMost > 12)
            {
                c.Problem(where, "group: 1 ≤ least ≤ most ≤ 12");
            }
            if (s.FleeHealth is < 0 or > 1 || s.HuntHunger is < 0 or > 1 || s.HungerRate < 0 || s.ChaseSeconds < 0 || s.ChaseRange < 0 ||
                s.FleeDistance < 0 || s.PersonalSpace < 0 || s.Calm < s.FleeDistance)
            {
                c.Problem(where, "behaviour: fractions 0 … 1, nothing negative, calm beyond the flight distance");
            }
            if (s.Temperament != Temperament.Flee && s.Attack == null)
            {
                c.Problem(where, "an animal that defends itself or hunts needs an attack");
            }
            if (s.Attack is { } a && (a.Damage <= 0 || a.Reach is <= 0 or > 4 || a.RateSeconds <= 0 || a.WindupSeconds <= 0 ||
                a.WindupSeconds > a.RateSeconds || a.Bleed is < 0 or > 2 || a.AnimalDamage < 0))
            {
                c.Problem(where, "attack: damage positive, reach 0 … 4 m, the wind-up within the rate, bleed 0 … 2");
            }
            if (s.Density < 0 || s.Density > 50 || s.Open < 0 || s.Wooded < 0 || s.Biomes.Values.Any(w => w < 0))
            {
                c.Problem(where, "density 0 … 50 a km², habitat weights not negative");
            }
            if (s.MaxSlope <= 0 || s.MaxSlope > 1.2f)
            {
                c.Problem(where, "slope 0 … 69°");
            }
            if (s.RespawnSeconds <= 0 || s.CorpseSeconds <= 0)
            {
                c.Problem(where, "respawn and corpse seconds positive");
            }
            if (s.HarvestHealth <= 0 || s.Harvest.Count == 0 || s.Harvest.Any(y => y.Count < 1))
            {
                c.Problem(where, "harvest: health positive and a yield, each at least 1");
            }
        }
        AnimalSpawning sp = loaded.AnimalSpawning;
        if (sp.AreaSize is < 32 or > 2048 || sp.Activate <= 0 || sp.Deactivate < sp.Activate || sp.LingerSeconds < 0 || sp.AwayFromPlayers < 0 ||
            sp.MaxAnimals is < 0 or > 4000)
        {
            c.Problem($"{File}: spawning", "area 32 … 2048 m, activate positive and within deactivate, nothing negative, at most 4000 animals");
        }
        if (loaded.HarvestRates.DefaultShare < 0 || loaded.HarvestRates.Tools.Values.Any(r => r < 0))
        {
            c.Problem($"{File}: harvest", "rates not negative");
        }
    }
}
