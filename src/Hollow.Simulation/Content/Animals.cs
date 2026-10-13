using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Hollow.Simulation.Combat;
using Hollow.Surface;

namespace Hollow.Simulation.Content;

/// <summary>How an animal meets a threat (GROUND.md §6.9).</summary>
public enum Temperament : byte
{
    /// <summary>Runs from anything it notices (the deer).</summary>
    Flee,

    /// <summary>Leaves people alone until hurt or crowded, then charges (the boar).</summary>
    Defend,

    /// <summary>Hunts when hungry, defends otherwise (the wolf, the bear).</summary>
    Hunt,
}

/// <summary>How an animal's attack lands.</summary>
public enum AttackKind : byte
{
    /// <summary>A lunge and a bite (the wolf).</summary>
    Bite,

    /// <summary>A charge that ends in a gore with the tusks (the boar).</summary>
    Charge,

    /// <summary>A swipe of a paw (the bear).</summary>
    Swipe,
}

/// <summary>An animal's attack (animals.json <c>attack</c>): damage before armour, reach from its head, one every so often.</summary>
public sealed class AttackStats
{
    public AttackKind Kind { get; init; }

    /// <summary>Damage to a character before armour; armour against melee on <see cref="Region"/> takes its share.</summary>
    public float Damage { get; init; }

    /// <summary>The bite or swipe lands when the target is within this many metres of the animal's front.</summary>
    public float Reach { get; init; }

    /// <summary>Seconds from one attack to the next.</summary>
    public float RateSeconds { get; init; }

    /// <summary>Seconds from the start of an attack to its hit: the wind-up a player can read and step out of.</summary>
    public float WindupSeconds { get; init; }

    /// <summary>Bleeding started, as a fraction of the damage.</summary>
    public float Bleed { get; init; }

    /// <summary>Where on a person it lands (which armour counts).</summary>
    public HitRegion Region { get; init; } = HitRegion.Body;

    /// <summary>Damage to another animal (wolves on deer), before its region.</summary>
    public float AnimalDamage { get; init; }

    public int RateTicks => Math.Max(1, (int)Math.Round(RateSeconds * CharacterTicks.Rate));

    public int WindupTicks => Math.Clamp((int)Math.Round(WindupSeconds * CharacterTicks.Rate), 1, RateTicks);
}

/// <summary>
/// One kind of animal (animals.json <c>species</c>, GROUND.md §6.9): health and size, speeds, senses, temperament, group
/// size, attack, where it lives and how many, how fast it comes back, and what its corpse gives.
/// </summary>
public sealed class AnimalSpecies
{
    private readonly Dictionary<Biome, float> _biomes = new();

    /// <summary>Stable small number (the wire carries it).</summary>
    public byte Id { get; init; }

    public string Key { get; init; } = "";

    public string Name { get; init; } = "";

    public float Health { get; init; }

    /// <summary>Body length (nose to tail), shoulder height and body radius, metres: the hit shapes and the steering.</summary>
    public float Length { get; init; }

    public float Height { get; init; }

    public float Radius { get; init; }

    public float HeadRadius { get; init; }

    /// <summary>Gaits in metres a second; turning in radians a second; acceleration in m/s².</summary>
    public float WalkSpeed { get; init; }

    public float TrotSpeed { get; init; }

    public float RunSpeed { get; init; }

    public float TurnRate { get; init; }

    public float Acceleration { get; init; }

    /// <summary>Sees this far (metres) within its field of view (radians, full width); hears noises this many times as far.</summary>
    public float Sight { get; init; }

    public float Cone { get; init; }

    public float Hearing { get; init; }

    public Temperament Temperament { get; init; }

    /// <summary>A flighty animal runs from a person who comes this close (metres), and calms down beyond <see cref="Calm"/>.</summary>
    public float FleeDistance { get; init; }

    public float Calm { get; init; }

    /// <summary>A person this close provokes it (a warning first); 0 never.</summary>
    public float PersonalSpace { get; init; }

    /// <summary>Below this fraction of its health it runs instead of fighting.</summary>
    public float FleeHealth { get; init; }

    /// <summary>It gives up a chase after this many seconds, or this far from where the chase began.</summary>
    public float ChaseSeconds { get; init; }

    public float ChaseRange { get; init; }

    /// <summary>Hunger gained a second (0 … 1 is full to starving); a hunter hunts above <see cref="HuntHunger"/>.</summary>
    public float HungerRate { get; init; }

    public float HuntHunger { get; init; }

    /// <summary>Hunters chase it (deer, boars).</summary>
    public bool Prey { get; init; }

    /// <summary>A herd or pack holds this many (least, most); its members keep within <see cref="Cohesion"/> metres.</summary>
    public int GroupLeast { get; init; }

    public int GroupMost { get; init; }

    public float Cohesion { get; init; }

    public AttackStats? Attack { get; init; }

    /// <summary>Animals a square kilometre where it lives best.</summary>
    public float Density { get; init; }

    /// <summary>How much it likes open ground and woods (tree cover 0 and 1), multiplied with its biome's weight.</summary>
    public float Open { get; init; }

    public float Wooded { get; init; }

    /// <summary>The steepest ground it walks (radians).</summary>
    public float MaxSlope { get; init; }

    /// <summary>Seconds until a dead one is replaced; seconds a corpse lies before it goes.</summary>
    public float RespawnSeconds { get; init; }

    public float CorpseSeconds { get; init; }

    /// <summary>How much a corpse takes to harvest (a tool's rate a hit), and everything it gives in all.</summary>
    public float HarvestHealth { get; init; }

    public IReadOnlyList<ItemAmount> Harvest { get; internal set; } = Array.Empty<ItemAmount>();

    /// <summary>How well it lives in a biome (0: not at all).</summary>
    public float BiomeWeight(Biome biome) => _biomes.GetValueOrDefault(biome);

    public IReadOnlyDictionary<Biome, float> Biomes => _biomes;

    internal void SetBiome(Biome biome, float weight) => _biomes[biome] = weight;

    /// <summary>Its habitat's suitability for a mix of biomes (weights summing to 1) under a tree cover (0 … 1).</summary>
    public float Suitability(ReadOnlySpan<float> biomeShares, float treeCover)
    {
        float biome = 0;
        for (int i = 0; i < biomeShares.Length; i++)
        {
            biome += biomeShares[i] * BiomeWeight((Biome)i);
        }
        return biome * (Open + (Wooded - Open) * Math.Clamp(treeCover, 0, 1));
    }

    public override string ToString() => Key;
}

/// <summary>How far noises carry to animals (animals.json <c>senses</c>), in metres before a species' hearing.</summary>
public sealed class AnimalSenses
{
    public float Sprint { get; init; } = 28;
    public float Walk { get; init; } = 10;
    public float Crouch { get; init; } = 3;
    public float Swim { get; init; } = 12;
    public float Gunshot { get; init; } = 180;
    public float Silenced { get; init; } = 45;
    public float Bow { get; init; } = 14;
    public float Melee { get; init; } = 18;
    public float Gather { get; init; } = 30;
    public float Explosion { get; init; } = 400;

    /// <summary>A crouching person is seen at this fraction of the range, one standing still at this one.</summary>
    public float CrouchSight { get; init; } = 0.55f;

    public float StillSight { get; init; } = 0.8f;

    /// <summary>Anything this close is noticed whichever way the animal faces.</summary>
    public float Near { get; init; } = 5;
}

/// <summary>Where animals live and how they are kept (animals.json <c>spawning</c>).</summary>
public sealed class AnimalSpawning
{
    /// <summary>Spawn areas are squares of this many metres of the zone's plane.</summary>
    public float AreaSize { get; init; } = 256;

    /// <summary>An area's animals are out while a player is within this many metres of it, and go this long after the last one left beyond <see cref="Deactivate"/>.</summary>
    public float Activate { get; init; } = 380;

    public float Deactivate { get; init; } = 560;

    public float LingerSeconds { get; init; } = 30;

    /// <summary>Animals don't appear (or come back) within this many metres of a player.</summary>
    public float AwayFromPlayers { get; init; } = 70;

    /// <summary>At most this many animals live in the zone at once.</summary>
    public int MaxAnimals { get; init; } = 600;
}

/// <summary>How fast tools harvest a corpse (animals.json <c>harvest</c>): a tool's rate, or a share of its damage.</summary>
public sealed class HarvestRates
{
    private readonly Dictionary<ItemDef, float> _tools = new();

    /// <summary>For a melee item not listed: this share of its damage a hit.</summary>
    public float DefaultShare { get; init; } = 0.4f;

    public IReadOnlyDictionary<ItemDef, float> Tools => _tools;

    internal void Set(ItemDef item, float rate) => _tools[item] = rate;

    /// <summary>Harvest health a hit with this item takes (0: it can't harvest).</summary>
    public float Rate(ItemDef item) =>
        _tools.TryGetValue(item, out float rate) ? rate : item.Melee is { } melee ? melee.Damage * DefaultShare : 0;
}

/// <summary>Reads animals.json (GROUND.md §6.9) and links its yields and tools to items.</summary>
internal static class AnimalsReader
{
    public const string File = "animals.json";

    public static void Read(JsonFields f, JsonElement root, GameContent.Loaded loaded, Func<string, string, ItemDef?> resolve)
    {
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
                    ItemDef? item = resolve(tool.Name, where + ".tools");
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
            f.OnlyKnown(size, where + ".size", "length", "height", "radius", "head");
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
                CorpseSeconds = f.Number(e, "corpse", where, 300),
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
                if (f.Tuple(y, where + ".harvest.yield", 1, out string itemKey, out double[] values) && resolve(itemKey, where + ".harvest.yield") is { } item)
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
    public static void Check(GameContent.Loaded c, List<string> problems)
    {
        void Problem(string where, string what) => problems.Add($"{where}: {what}");
        foreach (IGrouping<byte, AnimalSpecies> g in c.Animals.GroupBy(s => s.Id).Where(g => g.Count() > 1))
        {
            Problem(File, $"id {g.Key} is used by {string.Join(", ", g.Select(s => s.Key))}");
        }
        foreach (IGrouping<string, AnimalSpecies> g in c.Animals.GroupBy(s => s.Key).Where(g => g.Count() > 1))
        {
            Problem(File, $"\"{g.Key}\" is used twice");
        }
        foreach (AnimalSpecies s in c.Animals)
        {
            string where = $"{File}: \"{s.Key}\"";
            if (s.Health <= 0 || s.Length <= 0 || s.Height <= 0 || s.Radius <= 0 || s.HeadRadius <= 0 || s.Radius > s.Height)
            {
                Problem(where, "health and size positive; the body's radius under its height");
            }
            if (s.WalkSpeed <= 0 || s.TrotSpeed < s.WalkSpeed || s.RunSpeed < s.TrotSpeed || s.RunSpeed > 25 || s.TurnRate <= 0 || s.Acceleration <= 0)
            {
                Problem(where, "speeds: 0 < walk ≤ trot ≤ run ≤ 25 m/s; turning and acceleration positive");
            }
            if (s.Sight <= 0 || s.Sight > 300 || s.Cone <= 0 || s.Cone > 2 * MathF.PI + 1e-4f || s.Hearing < 0)
            {
                Problem(where, "senses: sight 0 … 300 m, a cone up to 360°, hearing not negative");
            }
            if (s.GroupLeast < 1 || s.GroupMost < s.GroupLeast || s.GroupMost > 12)
            {
                Problem(where, "group: 1 ≤ least ≤ most ≤ 12");
            }
            if (s.FleeHealth is < 0 or > 1 || s.HuntHunger is < 0 or > 1 || s.HungerRate < 0 || s.ChaseSeconds < 0 || s.ChaseRange < 0 ||
                s.FleeDistance < 0 || s.PersonalSpace < 0 || s.Calm < s.FleeDistance)
            {
                Problem(where, "behaviour: fractions 0 … 1, nothing negative, calm beyond the flight distance");
            }
            if (s.Temperament != Temperament.Flee && s.Attack == null)
            {
                Problem(where, "an animal that defends itself or hunts needs an attack");
            }
            if (s.Attack is { } a && (a.Damage <= 0 || a.Reach is <= 0 or > 4 || a.RateSeconds <= 0 || a.WindupSeconds <= 0 ||
                a.WindupSeconds > a.RateSeconds || a.Bleed is < 0 or > 2 || a.AnimalDamage < 0))
            {
                Problem(where, "attack: damage positive, reach 0 … 4 m, the wind-up within the rate, bleed 0 … 2");
            }
            if (s.Density < 0 || s.Density > 50 || s.Open < 0 || s.Wooded < 0 || s.Biomes.Values.Any(w => w < 0))
            {
                Problem(where, "density 0 … 50 a km², habitat weights not negative");
            }
            if (s.MaxSlope <= 0 || s.MaxSlope > 1.2f)
            {
                Problem(where, "slope 0 … 69°");
            }
            if (s.RespawnSeconds <= 0 || s.CorpseSeconds <= 0)
            {
                Problem(where, "respawn and corpse seconds positive");
            }
            if (s.HarvestHealth <= 0 || s.Harvest.Count == 0 || s.Harvest.Any(y => y.Count < 1))
            {
                Problem(where, "harvest: health positive and a yield, each at least 1");
            }
        }
        AnimalSpawning sp = c.AnimalSpawning;
        if (sp.AreaSize is < 32 or > 2048 || sp.Activate <= 0 || sp.Deactivate < sp.Activate || sp.LingerSeconds < 0 || sp.AwayFromPlayers < 0 ||
            sp.MaxAnimals is < 0 or > 4000)
        {
            Problem($"{File}: spawning", "area 32 … 2048 m, activate positive and within deactivate, nothing negative, at most 4000 animals");
        }
        if (c.HarvestRates.DefaultShare < 0 || c.HarvestRates.Tools.Values.Any(r => r < 0))
        {
            Problem($"{File}: harvest", "rates not negative");
        }
    }
}
