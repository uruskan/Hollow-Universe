using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation.Content;
using Hollow.Simulation.Nature;
using Hollow.Surface;

namespace Hollow.Simulation.Animals;

/// <summary>
/// A square of the zone where animals live (GROUND.md §6.9): its habitat (biomes, tree cover, land) worked out once from
/// the planet, and the herds it holds (its slots). Its animals are out while a player is near and go when everyone has
/// left; the slots remember who is alive and who is waiting to come back.
/// </summary>
public sealed class SpawnArea
{
    public SpawnArea(int x, int z, Vector3 centre)
    {
        X = x;
        Z = z;
        Centre = centre;
    }

    public int X { get; }

    public int Z { get; }

    public Vector3 Centre { get; }

    public List<SpawnSlot> Slots { get; } = new();

    /// <summary>Its animals are in the world.</summary>
    public bool Active { get; internal set; }

    /// <summary>The last tick a player was near it.</summary>
    internal long LastNear;

    /// <summary>Its habitat: shares of each biome, tree cover 0 … 1, and the share of it that is land.</summary>
    public float[] Biomes { get; } = new float[BiomeTable.Count];

    public float TreeCover { get; internal set; }

    public float Land { get; internal set; }
}

/// <summary>
/// One herd's place in a spawn area (or one placed by hand): its species and size, its herd while it is out, and the
/// times its dead come back. It never holds more than its size.
/// </summary>
public sealed class SpawnSlot
{
    public SpawnSlot(AnimalSpecies species, int size, Vector3 home, float range, SpawnArea? area, int index)
    {
        Species = species;
        Size = size;
        Home = home;
        Range = range;
        Area = area;
        Index = index;
    }

    public AnimalSpecies Species { get; }

    /// <summary>The most it holds: the herd as it spawned.</summary>
    public int Size { get; }

    public Vector3 Home { get; internal set; }

    public float Range { get; }

    public SpawnArea? Area { get; }

    public int Index { get; }

    /// <summary>The herd while its animals are out.</summary>
    public Herd? Herd { get; internal set; }

    /// <summary>World ticks at which dead members come back, one each.</summary>
    public List<long> Respawns { get; } = new();

    /// <summary>Living members (out or not): its size less those waiting to come back.</summary>
    public int Living => Size - Respawns.Count;

    internal int Spawns;

    internal void MemberDied(AnimalWorld world, Animal a) =>
        Respawns.Add(world.TickNow + CharacterTicks.From(Species.RespawnSeconds));
}

// The population (GROUND.md §6.9): spawn areas on a grid over the zone, each with herds sized from its habitat and the
// species' densities (Rust's population a square kilometre), out while a player is within reach and gone when nobody
// is; the dead replaced after their species' respawn time, never above an area's numbers and never within sight of a
// player. Deterministic: an area's herds come from the zone's seed, the area and the planet.
public sealed partial class AnimalWorld
{
    private readonly Dictionary<(int X, int Z), SpawnArea> _areas = new();
    private readonly List<SpawnSlot> _manual = new();
    private readonly HashSet<(int X, int Z)> _nearAreas = new();

    /// <summary>Animals come and go with the players by themselves (the server turns it on; tests and sandboxes may not).</summary>
    public bool NaturalSpawning { get; set; }

    /// <summary>The spawn areas looked at so far.</summary>
    public IReadOnlyCollection<SpawnArea> Areas => _areas.Values;

    /// <summary>Slots placed by hand (the load test's population, a sandbox's herds): always out, refilled like any other.</summary>
    public IReadOnlyList<SpawnSlot> ManualSlots => _manual;

    internal long TickNow => Tick;

    /// <summary>
    /// A herd that stays: <paramref name="size"/> animals of a species around a point, roaming within
    /// <paramref name="range"/> metres, whose dead come back after the species' respawn time.
    /// </summary>
    public SpawnSlot AddSlot(AnimalSpecies species, Vector3 home, int size, float range = 80)
    {
        var slot = new SpawnSlot(species, size, home, range, null, _manual.Count);
        _manual.Add(slot);
        slot.Herd = SpawnHerd(species, home, size, range, slot);
        slot.Spawns++;
        return slot;
    }

    /// <summary>
    /// The load test's population: <paramref name="count"/> animals in herds around a point, within
    /// <paramref name="radius"/> metres, of every species in proportion to its density (each its own herds).
    /// </summary>
    public void Populate(Vector3 centre, float radius, int count)
    {
        var rng = new Rng(Hash64.Of(_seed, 0x9091));
        double total = 0;
        foreach (AnimalSpecies s in Content.Animals)
        {
            total += s.Density;
        }
        int placed = 0, guard = 0;
        while (placed < count && guard++ < count * 4)
        {
            double pick = rng.NextDouble() * total;
            AnimalSpecies species = Content.Animals[0];
            foreach (AnimalSpecies s in Content.Animals)
            {
                if ((pick -= s.Density) <= 0)
                {
                    species = s;
                    break;
                }
            }
            int size = Math.Min(count - placed, species.GroupLeast + rng.NextInt(species.GroupMost - species.GroupLeast + 1));
            for (int attempt = 0; attempt < 12; attempt++)
            {
                double angle = rng.NextDouble() * DMath.TwoPi;
                float r = radius * (float)Math.Sqrt(rng.NextDouble());
                float x = centre.X + r * (float)DMath.Cos(angle), z = centre.Z + r * (float)DMath.Sin(angle);
                Nav.Ensure(x, z);
                if (Nav.Walkable(x, z, species.MaxSlope))
                {
                    AddSlot(species, new Vector3(x, Nav.Height(x, z), z), size, MathF.Min(radius, 90));
                    placed += size;
                    break;
                }
            }
        }
    }

    // Once a second: which areas players are near, areas in and out, the dead replaced.
    private void Populate()
    {
        if (NaturalSpawning)
        {
            UpdateAreas();
        }
        foreach (SpawnSlot slot in _manual)
        {
            Refill(slot);
        }
        foreach (SpawnArea area in _areas.Values)
        {
            if (area.Active)
            {
                foreach (SpawnSlot slot in area.Slots)
                {
                    Refill(slot);
                }
            }
        }
    }

    private void UpdateAreas()
    {
        AnimalSpawning spawning = Content.AnimalSpawning;
        float size = spawning.AreaSize;
        _nearAreas.Clear();
        foreach (AnimalObserver o in Observers)
        {
            int x0 = (int)MathF.Floor((o.Position.X - spawning.Activate) / size), x1 = (int)MathF.Floor((o.Position.X + spawning.Activate) / size);
            int z0 = (int)MathF.Floor((o.Position.Z - spawning.Activate) / size), z1 = (int)MathF.Floor((o.Position.Z + spawning.Activate) / size);
            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    if (DistanceToArea(o.Position, x, z, size) <= spawning.Activate)
                    {
                        _nearAreas.Add((x, z));
                    }
                }
            }
        }
        foreach ((int x, int z) in _nearAreas)
        {
            if (!_areas.TryGetValue((x, z), out SpawnArea? area))
            {
                area = Survey(x, z);
                _areas.Add((x, z), area);
            }
            area.LastNear = Tick;
            if (!area.Active && _animals.Count < spawning.MaxAnimals)
            {
                Activate(area);
            }
        }
        long linger = CharacterTicks.From(spawning.LingerSeconds);
        foreach (SpawnArea area in _areas.Values)
        {
            if (!area.Active || _nearAreas.Contains((area.X, area.Z)))
            {
                continue;
            }
            bool anyone = false;
            foreach (AnimalObserver o in Observers)
            {
                anyone |= DistanceToArea(o.Position, area.X, area.Z, size) <= spawning.Deactivate;
            }
            if (anyone)
            {
                area.LastNear = Tick;
            }
            else if (Tick - area.LastNear > linger)
            {
                Deactivate(area);
            }
        }
    }

    private static float DistanceToArea(Vector3 p, int x, int z, float size)
    {
        float dx = MathF.Max(0, MathF.Max(x * size - p.X, p.X - (x + 1) * size));
        float dz = MathF.Max(0, MathF.Max(z * size - p.Z, p.Z - (z + 1) * size));
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>
    /// An area's habitat and herds, worked out once: biome shares and land from the surface, tree cover from the
    /// planet's vegetation (or the natural objects on a stand-in surface), and for each species a number of herds from
    /// its density and how well it likes the place (a seeded draw for the fraction).
    /// </summary>
    internal SpawnArea Survey(int x, int z)
    {
        float size = Content.AnimalSpawning.AreaSize;
        var area = new SpawnArea(x, z, _zone.GroundAt((x + 0.5f) * size, (z + 0.5f) * size));
        ISurface surface = _zone.Surface;
        const int samples = 4;
        int land = 0;
        double cover = 0;
        Span<double> weights = stackalloc double[BiomeTable.Count];
        for (int j = 0; j < samples; j++)
        {
            for (int i = 0; i < samples; i++)
            {
                double px = (x + (i + 0.5) / samples) * size, pz = (z + (j + 0.5) / samples) * size;
                Vector3D direction = _zone.Frame.ToBody(px, 0, pz).Normalized;
                if (surface is PlanetSurface planet)
                {
                    SurfaceSample sample = planet.Sample(direction);
                    if (planet.SeaRadius > 0 && sample.Height < 0)
                    {
                        continue;
                    }
                    ClimateSample climate = planet.Climate(direction, sample);
                    planet.BiomeWeights(direction, sample, climate, weights);
                    VegetationSample v = planet.Vegetation(direction, sample, climate, weights, 0);
                    cover += Math.Clamp(v.Trees / 0.02, 0, 1);
                    for (int b = 0; b < BiomeTable.Count; b++)
                    {
                        area.Biomes[b] += (float)weights[b];
                    }
                }
                else
                {
                    if (surface.SeaRadius > 0 && surface.Radius + surface.HeightAt(direction) < surface.SeaRadius)
                    {
                        continue;
                    }
                    area.Biomes[(int)surface.BiomeAt(direction)] += 1;
                }
                land++;
            }
        }
        area.Land = land / (float)(samples * samples);
        if (land > 0)
        {
            for (int b = 0; b < BiomeTable.Count; b++)
            {
                area.Biomes[b] /= land;
            }
        }
        area.TreeCover = surface is PlanetSurface ? (float)(cover / Math.Max(1, land)) : StandInCover(x, z, size);

        // The herds: expected animals from the density, as herds of the species' size.
        float km2 = size * size / 1_000_000f;
        foreach (AnimalSpecies s in Content.Animals)
        {
            var rng = new Rng(Hash64.Of(_seed, (ulong)(uint)x, (ulong)(uint)z, s.Id));
            float expected = s.Density * km2 * area.Land * s.Suitability(area.Biomes, area.TreeCover);
            float meanGroup = (s.GroupLeast + s.GroupMost) / 2f;
            float herds = expected / meanGroup;
            int count = (int)MathF.Floor(herds) + (rng.NextDouble() < herds - MathF.Floor(herds) ? 1 : 0);
            for (int h = 0; h < count; h++)
            {
                int groupSize = s.GroupLeast + rng.NextInt(s.GroupMost - s.GroupLeast + 1);
                Vector3 home = area.Centre;
                area.Slots.Add(new SpawnSlot(s, groupSize, home, size * 0.6f, area, area.Slots.Count));
            }
        }
        return area;
    }

    // Tree cover on a stand-in surface: the natural objects' trees in the area against a closed forest's 200 a hectare.
    private float StandInCover(int x, int z, float size)
    {
        if (_zone.Nature == null)
        {
            return 0;
        }
        var objects = new List<NaturalObject>();
        _zone.Nature.Query(x * size, z * size, (x + 1) * size, (z + 1) * size, objects);
        int trees = 0;
        foreach (NaturalObject o in objects)
        {
            trees += Content.Natural(o.Kind)?.Class == NaturalClass.Tree ? 1 : 0;
        }
        float hectares = size * size / 10_000f;
        return Math.Clamp(trees / hectares / 200f, 0, 1);
    }

    private void Activate(SpawnArea area)
    {
        // Its ground mapped first (on workers); the herds come out once it is.
        float half = Content.AnimalSpawning.AreaSize / 2;
        Nav.Prefetch(area.Centre, half + 8);
        if (!Nav.IsReady(area.Centre.X, area.Centre.Z))
        {
            return;
        }
        area.Active = true;
        foreach (SpawnSlot slot in area.Slots)
        {
            if (slot.Herd == null && slot.Living > 0 && PlaceSlot(slot, out Vector3 at))
            {
                slot.Home = at;
                slot.Herd = SpawnHerd(slot.Species, at, slot.Living, slot.Range, slot);
                slot.Spawns++;
            }
        }
    }

    private void Deactivate(SpawnArea area)
    {
        area.Active = false;
        foreach (SpawnSlot slot in area.Slots)
        {
            if (slot.Herd is not { } herd)
            {
                continue;
            }
            foreach (Animal a in herd.Members)
            {
                if (!a.IsDead)
                {
                    Remove(a);
                }
            }
            slot.Herd = null;
        }
    }

    // The dead come back after their time: into the herd if it is out and nobody watches, or as a new herd.
    private void Refill(SpawnSlot slot)
    {
        if (slot.Respawns.Count == 0)
        {
            return;
        }
        for (int i = slot.Respawns.Count - 1; i >= 0; i--)
        {
            if (Tick < slot.Respawns[i] || _animals.Count >= Content.AnimalSpawning.MaxAnimals)
            {
                continue;
            }
            if (slot.Herd is { } herd && herd.Alive > 0)
            {
                Animal leader = herd.Leader!;
                if (!PlayerWithin(leader.Position, Content.AnimalSpawning.AwayFromPlayers))
                {
                    AddMember(herd, leader.Position, herd.Members.Count);
                    slot.Respawns.RemoveAt(i);
                }
                continue;
            }
            if (PlaceSlot(slot, out Vector3 at))
            {
                slot.Home = at;
                slot.Herd = SpawnHerd(slot.Species, at, 1, slot.Range, slot);
                slot.Spawns++;
                slot.Respawns.RemoveAt(i);
            }
        }
    }

    // Where a slot's herd appears: a seeded point in its area (or around its home) on ground it walks, out of players' sight.
    private bool PlaceSlot(SpawnSlot slot, out Vector3 at)
    {
        float size = Content.AnimalSpawning.AreaSize;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            var rng = new Rng(Hash64.Of(_seed, (ulong)(slot.Area?.X ?? 0) * 7919 + (ulong)(slot.Area?.Z ?? 0), (ulong)slot.Index,
                (ulong)(slot.Spawns * 16 + attempt)));
            float x, z;
            if (slot.Area is { } area)
            {
                x = (area.X + (float)rng.Range(0.1, 0.9)) * size;
                z = (area.Z + (float)rng.Range(0.1, 0.9)) * size;
            }
            else
            {
                double angle = rng.NextDouble() * DMath.TwoPi;
                float r = slot.Range * 0.5f * (float)Math.Sqrt(rng.NextDouble());
                x = slot.Home.X + r * (float)DMath.Cos(angle);
                z = slot.Home.Z + r * (float)DMath.Sin(angle);
            }
            if (!Nav.IsReady(x, z))
            {
                Nav.Prefetch(new Vector3(x, 0, z), 8);
                continue;
            }
            if (!Nav.Walkable(x, z, slot.Species.MaxSlope))
            {
                continue;
            }
            var point = new Vector3(x, Nav.Height(x, z), z);
            if (PlayerWithin(point, Content.AnimalSpawning.AwayFromPlayers))
            {
                continue;
            }
            at = point;
            return true;
        }
        at = default;
        return false;
    }

    private bool PlayerWithin(Vector3 point, float radius)
    {
        foreach (AnimalObserver o in Observers)
        {
            float dx = o.Position.X - point.X, dz = o.Position.Z - point.Z;
            if (dx * dx + dz * dz < radius * radius)
            {
                return true;
            }
        }
        return false;
    }
}
