using System;
using System.IO;
using System.Text.Json;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>The grass's habitats (docs/WALKING_GROUND.md §18), in the data's order.</summary>
public enum GrassHabitat
{
    Meadow = 0,
    ForestEdge = 1,
    ForestFloor = 2,
    Dry = 3,
    Tundra = 4,
}

/// <summary>
/// What the ground offers grass at a point, as the drawn terrain carries it: the shared presentation cover
/// (<see cref="GroundDetailCover"/>'s grass and litter), the climate's moisture and temperature (°C), the tree cover
/// (0 open to 1 closed forest) and the grass colour's dryness there (0 lush to 1 straw, the same the shaders tint with).
/// </summary>
public readonly record struct GrassSite(double Grass, double Litter, double Moisture, double Temperature, double Canopy,
    double Dryness);

/// <summary>
/// One placement cell's ground cover: the clump growing there (<see cref="Species"/> −1: none) and its variant, height
/// and width multipliers and stable thinning rank; an authored accent standing there instead (<see cref="Accent"/> −1:
/// none) at its scale; and whether litter lies there, its variant and rank.
/// </summary>
public readonly record struct GrassPlan(int Species, int Variant, double Height, double Width, double Rank, int Accent,
    double AccentScale, bool Litter, int LitterVariant, double LitterRank)
{
    public bool Grows => Species >= 0;
}

/// <summary>
/// The one coverage rule for the ground's grass (docs/WALKING_GROUND.md §18, docs/TERRAIN_SCENERY_PLAN.md T4): for every
/// placement cell, the habitats' weights from the cover and climate, then once each, the occupied share of the ground,
/// the mix of clump species, the clumps' size and the patches they vary in. Patches move grass about without changing
/// how much there is: a patch's threshold rises and falls around the habitat's share, so the mean stays the share. The
/// numbers are data (Data/ground-cover-habitats.json). Deterministic: the same cell and site always give the same plan,
/// on every machine (Hash64, Rng, DMath only).
/// </summary>
public sealed class GroundCoverHabitats
{
    public const int HabitatCount = 5;

    private const ulong PlanSalt = 0x4752415353504C41UL;      // "GRASSPLA"
    private const ulong PatchSalt = 0x4752415353504154UL;     // "GRASSPAT"

    private sealed record Habitat(string Key, double Ceiling, double GrassFrom, double GrassTo, double Patchiness,
        double Height, double[] Mix, double[] Accents, double VariantB, double FlowerShare);

    private sealed record Patch(double Metres, double Amplitude);

    private readonly Habitat[] _habitats;
    private readonly Patch[] _speciesPatches;
    private readonly Patch _occupancyPatch, _heightPatch;
    private readonly double _litterCeiling, _litterNeedles, _cottonFrom, _cottonTo;
    private readonly int _cotton, _dry, _herb;

    /// <summary>The clump species, in the order the game's catalogue (game/assets/surface/grass/grass.json) lists them.</summary>
    public string[] Species { get; }

    /// <summary>The authored accents (game/assets/surface/ground-cover/catalog.json's families), in their order.</summary>
    public string[] Accents { get; }

    private static readonly Lazy<GroundCoverHabitats> DefaultLazy = new(() =>
    {
        using Stream data = typeof(GroundCoverHabitats).Assembly.GetManifestResourceStream("Hollow.Surface.Data.ground-cover-habitats.json")
            ?? throw new InvalidOperationException("Hollow.Surface.Data.ground-cover-habitats.json is not embedded.");
        using var reader = new StreamReader(data);
        return Parse(reader.ReadToEnd());
    });

    /// <summary>The habitats in Data/ground-cover-habitats.json.</summary>
    public static GroundCoverHabitats Default => DefaultLazy.Value;

    private GroundCoverHabitats(string[] species, string[] accents, Habitat[] habitats, Patch[] speciesPatches,
        Patch occupancy, Patch height, double litterCeiling, double litterNeedles, double cottonFrom, double cottonTo)
    {
        Species = species;
        Accents = accents;
        _habitats = habitats;
        _speciesPatches = speciesPatches;
        _occupancyPatch = occupancy;
        _heightPatch = height;
        _litterCeiling = litterCeiling;
        _litterNeedles = litterNeedles;
        _cottonFrom = cottonFrom;
        _cottonTo = cottonTo;
        _cotton = Array.IndexOf(accents, "cotton");
        _dry = Array.IndexOf(species, "dry");
        _herb = Array.IndexOf(species, "herb");
    }

    /// <summary>Reads the habitat table; refuses an incomplete or inconsistent one.</summary>
    public static GroundCoverHabitats Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        string[] species = Strings(root.GetProperty("species"));
        string[] accents = Strings(root.GetProperty("accents"));
        var habitats = new Habitat[HabitatCount];
        int h = 0;
        foreach (JsonElement e in root.GetProperty("habitats").EnumerateArray())
        {
            if (h == HabitatCount) throw new InvalidDataException("More than five grass habitats.");
            string key = e.GetProperty("key").GetString()!;
            if (!string.Equals(key, ((GrassHabitat)h).ToString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Grass habitat {h} is '{key}', not {(GrassHabitat)h}.");
            double[] mix = Numbers(e.GetProperty("mix"), species.Length, "mix of " + key);
            double total = 0;
            foreach (double m in mix) total += m;
            if (Math.Abs(total - 1) > 1e-6) throw new InvalidDataException($"The {key} mix sums to {total}, not 1.");
            habitats[h++] = new Habitat(key, Unit(e, "ceiling"), Unit(e, "grassFrom"), Unit(e, "grassTo"), Unit(e, "patchiness"),
                Positive(e, "height"), mix, Numbers(e.GetProperty("accents"), accents.Length, "accents of " + key),
                Unit(e, "variantB"), Unit(e, "flowerShare"));
            if (habitats[h - 1].GrassTo <= habitats[h - 1].GrassFrom)
                throw new InvalidDataException($"The {key} habitat's grass range is empty.");
        }
        if (h != HabitatCount) throw new InvalidDataException("Five grass habitats are needed.");
        JsonElement patches = root.GetProperty("speciesPatches");
        if (patches.GetArrayLength() != species.Length) throw new InvalidDataException("One patch scale per clump species.");
        var speciesPatches = new Patch[species.Length];
        int s = 0;
        foreach (JsonElement p in patches.EnumerateArray()) speciesPatches[s++] = ReadPatch(p);
        JsonElement litter = root.GetProperty("litter"), cotton = root.GetProperty("cotton");
        return new GroundCoverHabitats(species, accents, habitats, speciesPatches, ReadPatch(root.GetProperty("occupancyPatch")),
            ReadPatch(root.GetProperty("heightPatch")), Unit(litter, "ceiling"), Unit(litter, "needles"),
            Unit(cotton, "moistureFrom"), Unit(cotton, "moistureTo"));

        static string[] Strings(JsonElement e)
        {
            var list = new string[e.GetArrayLength()];
            int i = 0;
            foreach (JsonElement x in e.EnumerateArray()) list[i++] = x.GetString()!;
            if (list.Length == 0) throw new InvalidDataException("An empty list in the grass habitats.");
            return list;
        }
        static double[] Numbers(JsonElement e, int count, string what)
        {
            if (e.GetArrayLength() != count) throw new InvalidDataException($"The {what} needs {count} numbers.");
            var list = new double[count];
            int i = 0;
            foreach (JsonElement x in e.EnumerateArray())
            {
                double v = x.GetDouble();
                if (!(v >= 0 && v <= 1)) throw new InvalidDataException($"The {what} holds {v}, outside 0 to 1.");
                list[i++] = v;
            }
            return list;
        }
        static double Unit(JsonElement e, string name)
        {
            double v = e.GetProperty(name).GetDouble();
            return v >= 0 && v <= 1 ? v : throw new InvalidDataException($"{name} {v} is outside 0 to 1.");
        }
        static double Positive(JsonElement e, string name)
        {
            double v = e.GetProperty(name).GetDouble();
            return v > 0 && v <= 4 ? v : throw new InvalidDataException($"{name} {v} is outside 0 to 4.");
        }
        static Patch ReadPatch(JsonElement e)
        {
            double metres = e.GetProperty("metres").GetDouble();
            if (!(metres >= 0.5 && metres <= 500)) throw new InvalidDataException($"A patch of {metres} m.");
            return new Patch(metres, Unit(e, "amplitude"));
        }
    }

    /// <summary>
    /// The habitats' weights at a site (adding up to one): forest floor under closed canopy, forest edge under broken
    /// canopy, then tundra where it is cold, dry country where it is dry, and meadow for the rest.
    /// </summary>
    public static void Weigh(in GrassSite site, Span<double> weights)
    {
        if (weights.Length < HabitatCount) throw new ArgumentException("Five habitat weights.", nameof(weights));
        double floor = DMath.SmoothStep(0.45, 0.8, site.Canopy);
        double edge = DMath.SmoothStep(0.06, 0.3, site.Canopy) * (1 - floor);
        double open = 1 - floor - edge;
        double tundra = open * (1 - DMath.SmoothStep(-2, 4, site.Temperature));
        double dry = (open - tundra) * (1 - DMath.SmoothStep(0.18, 0.42, site.Moisture));
        weights[(int)GrassHabitat.Meadow] = Math.Max(0, open - tundra - dry);
        weights[(int)GrassHabitat.ForestEdge] = edge;
        weights[(int)GrassHabitat.ForestFloor] = floor;
        weights[(int)GrassHabitat.Dry] = dry;
        weights[(int)GrassHabitat.Tundra] = tundra;
    }

    /// <summary>The share of the ground clumps occupy at a site, before patches: each habitat's curve of the cover's
    /// grass, weighted. The one occupancy decision; nothing else thins the clumps.</summary>
    public double Occupancy(in GrassSite site, ReadOnlySpan<double> weights)
    {
        double share = 0;
        for (int h = 0; h < HabitatCount; h++)
        {
            Habitat habitat = _habitats[h];
            share += weights[h] * habitat.Ceiling * DMath.SmoothStep(habitat.GrassFrom, habitat.GrassTo, site.Grass);
        }
        return Math.Clamp(share, 0, 1);
    }

    /// <summary>
    /// A cell's plan. <paramref name="cellX"/>, <paramref name="cellY"/> name the cell on <paramref name="face"/> (its
    /// identity); <paramref name="faceU"/>, <paramref name="faceV"/> are its metres along the face, where the patches
    /// are measured.
    /// </summary>
    public GrassPlan Plan(in GrassSite site, int face, long cellX, long cellY, double faceU, double faceV)
    {
        Span<double> weights = stackalloc double[HabitatCount];
        Weigh(site, weights);
        var r = new Rng(Hash64.Of(PlanSalt, (ulong)face, unchecked((ulong)cellX), unchecked((ulong)cellY)));
        double occupancyRoll = r.NextDouble(), speciesRoll = r.NextDouble(), rank = r.NextDouble();
        double heightRoll = r.NextDouble(), widthRoll = r.NextDouble(), variantRoll = r.NextDouble();
        double accentRoll = r.NextDouble(), accentPick = r.NextDouble(), accentScale = r.NextDouble();
        double litterRoll = r.NextDouble(), litterRank = r.NextDouble(), litterVariant = r.NextDouble();

        bool litter = litterRoll < site.Litter * _litterCeiling;
        int litterKind = (litterVariant < _litterNeedles ? 2 : 0) + (litterVariant * 7 % 1 < 0.5 ? 0 : 1);

        // Occupancy: the habitats' share, its patches moving grass about around that share (the mean is kept).
        double share = Occupancy(site, weights);
        double patchiness = 0;
        for (int h = 0; h < HabitatCount; h++) patchiness += weights[h] * _habitats[h].Patchiness;
        double patch = PatchNoise(face, faceU / _occupancyPatch.Metres, faceV / _occupancyPatch.Metres, 1);
        double threshold = Math.Clamp(share + patchiness * (2 * patch - 1) * Math.Min(share, 1 - share), 0, 1);
        if (share <= 0 || occupancyRoll >= threshold)
            return new GrassPlan(-1, 0, 0, 0, rank, -1, 0, litter, litterKind, litterRank);

        // An authored accent in place of the clump.
        double accentTotal = 0;
        Span<double> accents = stackalloc double[Accents.Length];
        for (int a = 0; a < Accents.Length; a++)
        {
            double rate = 0;
            for (int h = 0; h < HabitatCount; h++) rate += weights[h] * _habitats[h].Accents[a];
            if (a == _cotton) rate *= DMath.SmoothStep(_cottonFrom, _cottonTo, site.Moisture);
            accents[a] = rate;
            accentTotal += rate;
        }
        if (accentRoll < accentTotal)
        {
            double pick = accentPick * accentTotal;
            int chosen = 0;
            while (chosen < accents.Length - 1 && pick >= accents[chosen]) pick -= accents[chosen++];
            return new GrassPlan(-1, 0, 0, 0, rank, chosen, 0.8 + 0.45 * accentScale, litter, litterKind, litterRank);
        }

        // The species: the habitats' mix, each species gathered into patches of its own (the mean kept), dry grass
        // following the grass colour's dryness.
        Span<double> mix = stackalloc double[Species.Length];
        double total = 0;
        for (int k = 0; k < Species.Length; k++)
        {
            double m = 0;
            for (int h = 0; h < HabitatCount; h++) m += weights[h] * _habitats[h].Mix[k];
            Patch p = _speciesPatches[k];
            double n = PatchNoise(face, faceU / p.Metres, faceV / p.Metres, (ulong)(k + 2));
            m *= Math.Max(0, 1 + p.Amplitude * (2 * n - 1));
            if (k == _dry) m *= 0.3 + 1.4 * Math.Clamp(site.Dryness, 0, 1);
            mix[k] = m;
            total += m;
        }
        int species = 0;
        if (total > 0)
        {
            double pick = speciesRoll * total;
            while (species < mix.Length - 1 && pick >= mix[species]) pick -= mix[species++];
        }
        double variantB = 0.5;
        if (species == _herb)
        {
            variantB = 0;
            for (int h = 0; h < HabitatCount; h++) variantB += weights[h] * _habitats[h].VariantB;
        }
        double height = 0;
        for (int h = 0; h < HabitatCount; h++) height += weights[h] * _habitats[h].Height;
        double heightPatch = PatchNoise(face, faceU / _heightPatch.Metres, faceV / _heightPatch.Metres, 64);
        height *= (0.8 + 0.4 * heightRoll) * (1 + _heightPatch.Amplitude * (2 * heightPatch - 1));
        return new GrassPlan(species, variantRoll < variantB ? 1 : 0, height, 0.85 + 0.3 * widthRoll, rank, -1, 0,
            litter, litterKind, litterRank);
    }

    /// <summary>The share of flowering clumps the table means for a habitat (for tests and reports).</summary>
    public double FlowerShare(GrassHabitat habitat) => _habitats[(int)habitat].FlowerShare;

    /// <summary>A habitat's species mix (for tests and reports).</summary>
    public ReadOnlySpan<double> Mix(GrassHabitat habitat) => _habitats[(int)habitat].Mix;

    /// <summary>
    /// Smooth value noise in 0–1 on a face (two octaves of hashed corners, smoothstep between them), mean one half:
    /// the patches. <paramref name="x"/>, <paramref name="y"/> are in patch sizes.
    /// </summary>
    public static double PatchNoise(int face, double x, double y, ulong layer)
    {
        return 0.65 * Octave(face, x, y, layer) + 0.35 * Octave(face, x * 2.13 + 17.3, y * 2.13 + 5.1, layer + 101);

        static double Octave(int face, double x, double y, ulong layer)
        {
            double fx = Math.Floor(x), fy = Math.Floor(y);
            long ix = (long)fx, iy = (long)fy;
            double u = DMath.SmoothStep(0, 1, x - fx), v = DMath.SmoothStep(0, 1, y - fy);
            double a = Corner(face, ix, iy, layer), b = Corner(face, ix + 1, iy, layer);
            double c = Corner(face, ix, iy + 1, layer), d = Corner(face, ix + 1, iy + 1, layer);
            return (a * (1 - u) + b * u) * (1 - v) + (c * (1 - u) + d * u) * v;
        }

        static double Corner(int face, long x, long y, ulong layer) =>
            (Hash64.Of(PatchSalt ^ layer, (ulong)face, unchecked((ulong)x), unchecked((ulong)y)) >> 11) * (1.0 / 9007199254740992.0);
    }
}
