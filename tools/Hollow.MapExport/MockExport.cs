using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Hollow.Core;
using Hollow.Galaxy;
using Hollow.Galaxy.Systems;

namespace Hollow.MapExport;

/// <summary>
/// mock.js: a MOCK gameplay layer (stations, bases, danger regions, resources, the player's ship). None of this
/// exists in the game yet; it is invented here, deterministically, so the map prototypes have something to show.
/// </summary>
internal static class MockExport
{
    private const double JumpRange = 12.5;
    private const int FuelJumps = 6;
    private const int StationCount = 36;

    private static readonly string[] Resources =
    {
        "iron", "nickel", "silicates", "ice", "water", "volatiles", "organics", "rare metals",
    };

    private static readonly string[] Onsets =
    {
        "Ka", "Ve", "Tor", "Mi", "Sel", "Dra", "Or", "Ul", "Fen", "Ha", "Bri", "Cas", "Lo", "Ny", "Pra", "Rin", "Sa",
        "Thal", "Ver", "Wy", "Zo", "Ar", "Ell", "Gor",
    };

    private static readonly string[] Codas =
    {
        "ren", "dis", "mar", "vik", "lan", "thas", "nor", "bel", "quin", "dor", "sa", "ven", "rith", "mel", "gard", "lo",
    };

    private static readonly string[] OrbitalWords = { "Station", "Ring", "Spindle", "Anchorage", "Exchange", "Habitat" };
    private static readonly string[] OutpostWords = { "Outpost", "Landing", "Camp", "Works", "Claim" };
    private static readonly string[] DepotWords = { "Depot", "Waypoint", "Relay", "Cache" };

    private static readonly (string Handle, string Relation)[] Players =
    {
        ("Wren", "friendly"), ("Harrow", "neutral"), ("Lumen", "hostile"), ("Quill", "neutral"), ("Ashgrove", "friendly"),
    };

    private static readonly ulong Seed = Hash64.Of("Hollow map-ux mock gameplay layer, version 1");

    public static void Write(string path, LocalData local)
    {
        List<LocalSystem> systems = local.Systems;

        // Resources: a mask per landable body, from its type and temperature plus a seeded roll.
        var systemMask = new int[systems.Count];
        var bodyHints = new List<(int Sys, int Body, int Mask)>();
        for (int i = 0; i < systems.Count; i++)
        {
            LocalSystem s = systems[i];
            foreach (Body b in s.System.Bodies)
            {
                if (!b.Landable)
                {
                    continue;
                }
                int mask = ResourceMask(s.Star.Address.Value, b);
                systemMask[i] |= mask;
                if (s.Distance <= LocalExport.BodyListRadius)
                {
                    bodyHints.Add((i, b.Index, mask));
                }
            }
        }

        // Home base: 10-15 ly from Sol, scoopable star, the most landable bodies (then the nearest).
        int home = Enumerable.Range(0, systems.Count)
            .Where(i => systems[i].Distance >= 10 && systems[i].Distance <= 15
                && StarColours.Scoopable(systems[i].Star.Class) && systems[i].Summary[3] > 0)
            .OrderByDescending(i => systems[i].Summary[3])
            .ThenBy(i => systems[i].Distance)
            .First();
        Body homeBody = systems[home].System.Bodies.Where(b => b.Landable)
            .OrderBy(b => b.Kind == BodyKind.Planet ? 0 : 1).ThenBy(b => b.Index).First();
        Vector3D homePos = systems[home].Star.Position;

        // Stations: weighted sampling without replacement (Efraimidis-Spirakis), weight falling with distance.
        var stations = new List<(int Sys, string Name, string Type, Body? Body, string[] Services)>();
        var usedNames = new HashSet<string>();
        int sol = systems.FindIndex(s => s.Star.IsCatalogue && s.Star.Address.CatalogueId == 0);
        var picks = new List<int> { sol };
        picks.AddRange(Enumerable.Range(0, systems.Count)
            .Where(i => i != sol)
            .Select(i =>
            {
                LocalSystem s = systems[i];
                double weight = Math.Exp(-s.Distance / 18.0) * (s.Star.IsCatalogue ? 1.5 : 1.0)
                    * (s.Summary[1] > 0 ? 1.5 : 0.5);
                var rng = new Rng(Hash64.Of(Seed, 0x5354u, s.Star.Address.Value));
                double key = Math.Log(rng.NextDoubleNonZero()) / weight;
                return (i, key);
            })
            .OrderByDescending(p => p.key)
            .Take(StationCount - 1)
            .Select(p => p.i));
        foreach (int i in picks)
        {
            LocalSystem s = systems[i];
            var rng = new Rng(Hash64.Of(Seed, 0x5355u, s.Star.Address.Value));
            List<Body> landable = s.System.Bodies.Where(b => b.Landable).ToList();
            List<Body> planets = s.System.Bodies.Where(b => b.Kind == BodyKind.Planet).ToList();
            string type;
            Body? body;
            string[] words;
            if (i == sol)
            {
                type = "orbital";
                body = s.System.Bodies.FirstOrDefault(b => b.Name == "Earth");
                words = OrbitalWords;
            }
            else if (landable.Count > 0 && rng.Chance(0.35))
            {
                type = "outpost";
                body = landable[rng.NextInt(landable.Count)];
                words = OutpostWords;
            }
            else if (planets.Count > 0)
            {
                type = "orbital";
                body = planets[rng.NextInt(planets.Count)];
                words = OrbitalWords;
            }
            else
            {
                type = "depot";
                body = null;
                words = DepotWords;
            }
            string name;
            do
            {
                name = Onsets[rng.NextInt(Onsets.Length)] + Codas[rng.NextInt(Codas.Length)] + " " + words[rng.NextInt(words.Length)];
            }
            while (!usedNames.Add(name));
            var services = new List<string> { "refuel" };
            if (rng.Chance(0.8))
            {
                services.Add("repair");
            }
            if (rng.Chance(0.9))
            {
                services.Add("market");
            }
            if (type == "orbital" && rng.Chance(0.3))
            {
                services.Add("shipyard");
            }
            stations.Add((i, name, type, body, services.ToArray()));
        }

        // Other players' bases: 15-100 ly from Sol, on a landable body, weighted toward the home base.
        var players = new List<(string Handle, string Relation, int Sys, Body Body)>();
        List<int> playerSystems = Enumerable.Range(0, systems.Count)
            .Where(i => systems[i].Distance > 15 && systems[i].Distance <= 100 && systems[i].Summary[3] > 0 && i != home)
            .Select(i =>
            {
                double weight = Math.Exp(-(systems[i].Star.Position - homePos).Length / 35.0);
                var rng = new Rng(Hash64.Of(Seed, 0x504Cu, systems[i].Star.Address.Value));
                return (i, key: Math.Log(rng.NextDoubleNonZero()) / weight);
            })
            .OrderByDescending(p => p.key)
            .Take(Players.Length)
            .Select(p => p.i)
            .ToList();
        for (int k = 0; k < Players.Length; k++)
        {
            LocalSystem s = systems[playerSystems[k]];
            List<Body> landable = s.System.Bodies.Where(b => b.Landable).ToList();
            var rng = new Rng(Hash64.Of(Seed, 0x504Du, s.Star.Address.Value));
            players.Add((Players[k].Handle, Players[k].Relation, playerSystems[k], landable[rng.NextInt(landable.Count)]));
        }

        // Danger regions: centred on a system, never covering the home base.
        int raiders = PickCentre(systems, 25, 45, homePos, 12, 0x4441u);
        int storm = PickCentre(systems, 70, 100, homePos, 20, 0x4442u);

        Console.WriteLine($"  home base: {systems[home].Name} ({systems[home].Distance:F1} ly), body {homeBody.Name}");
        Console.WriteLine($"  stations: {stations.Count}, within 20 ly: {stations.Count(t => systems[t.Sys].Distance <= 20)}, " +
            $"within 50 ly: {stations.Count(t => systems[t.Sys].Distance <= 50)}");

        Js.WriteGlobal(path, "HOLLOW_MOCK",
            "Hollow Universe: MOCK gameplay layer for the map prototypes (none of this exists in the game yet).", w =>
        {
            w.WriteStartObject();
            w.WritePropertyName("meta");
            w.WriteStartObject();
            w.WriteBoolean("mock", true);
            w.WriteString("what", "MOCK data, invented deterministically for UX prototypes: stations, bases, players, danger regions, resources and the ship are not part of the game or its generator.");
            w.WriteString("seed", Js.Hex(Seed));
            w.WriteNumber("localCount", systems.Count);
            w.WriteString("sys", "'sys' fields are indices into HOLLOW_LOCAL.systems (same export run; check localCount)");
            w.WriteEndObject();

            w.WritePropertyName("ship");
            w.WriteStartObject();
            w.WriteString("name", "Wayfarer");
            w.WriteNumber("jumpRangeLy", JumpRange);
            w.WriteNumber("fuelJumps", FuelJumps);
            w.WriteNumber("fuel", FuelJumps);
            w.WriteNumber("sys", home);
            w.WriteString("refuelRule", "Each jump of up to 12.5 ly uses one jump of fuel; the tank holds 6. Arriving at a scoopable star (HOLLOW_LOCAL.systems.scoop = 1: main-sequence O/B/A/F/G/K/M) or at a system with a station refills the tank.");
            w.WriteBoolean("refuelAtScoopable", true);
            w.WriteBoolean("refuelAtStations", true);
            w.WriteEndObject();

            w.WritePropertyName("home");
            w.WriteStartObject();
            w.WriteString("name", "Home base");
            WriteSystemRef(w, systems, home, homeBody);
            w.WriteEndObject();

            w.WritePropertyName("stations");
            w.WriteStartArray();
            for (int k = 0; k < stations.Count; k++)
            {
                var t = stations[k];
                w.WriteStartObject();
                w.WriteNumber("id", k);
                w.WriteString("name", t.Name);
                w.WriteString("type", t.Type);
                WriteSystemRef(w, systems, t.Sys, t.Body);
                w.WritePropertyName("services");
                w.WriteStartArray();
                foreach (string sv in t.Services)
                {
                    w.WriteStringValue(sv);
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WritePropertyName("players");
            w.WriteStartArray();
            foreach (var p in players)
            {
                w.WriteStartObject();
                w.WriteString("handle", p.Handle);
                w.WriteString("relation", p.Relation);
                w.WriteString("name", p.Handle + "'s base");
                WriteSystemRef(w, systems, p.Sys, p.Body);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WritePropertyName("danger");
            w.WriteStartArray();
            WriteDanger(w, systems, raiders, "raiders", "Raider ambush zone", "hostile", 12,
                "Hostile ships intercept travellers who drop into systems inside this sphere.");
            WriteDanger(w, systems, storm, "ion-storm", "Ion storm front", "hazard", 20,
                "Charged dust: star scooping is impossible and each jump inside costs two jumps of fuel.");
            w.WriteEndArray();

            w.WritePropertyName("resources");
            w.WriteStartObject();
            w.WritePropertyName("names");
            w.WriteStartArray();
            foreach (string r in Resources)
            {
                w.WriteStringValue(r);
            }
            w.WriteEndArray();
            w.WriteString("mask", "bit k set = resource names[k] present (e.g. mask & 8 -> ice)");
            w.WritePropertyName("systemMask");
            w.WriteStartArray();
            foreach (int m in systemMask)
            {
                w.WriteNumberValue(m);
            }
            w.WriteEndArray();
            w.WriteString("bodiesFields", "[sys, bodyIndex, mask] for every landable body of the systems that have body lists (within 40 ly); bodyIndex indexes HOLLOW_LOCAL.bodies[sys]");
            w.WritePropertyName("bodies");
            w.WriteStartArray();
            foreach ((int sys, int body, int mask) in bodyHints)
            {
                w.WriteStartArray();
                w.WriteNumberValue(sys);
                w.WriteNumberValue(body);
                w.WriteNumberValue(mask);
                w.WriteEndArray();
            }
            w.WriteEndArray();
            w.WriteEndObject();
            w.WriteEndObject();
        });
    }

    private static int ResourceMask(ulong address, Body b)
    {
        var rng = new Rng(Hash64.Of(Seed, address, (ulong)b.Index));
        int m = 0;
        void Add(int bit) => m |= 1 << bit;
        switch (b.PlanetType)
        {
            case PlanetType.MetalRich:
                Add(0);
                Add(1);
                Add(7);
                break;
            case PlanetType.Rocky:
                Add(0);
                Add(2);
                if (rng.Chance(0.4))
                {
                    Add(1);
                }
                if (rng.Chance(0.15))
                {
                    Add(7);
                }
                break;
            case PlanetType.RockyIce:
                Add(2);
                Add(3);
                if (rng.Chance(0.3))
                {
                    Add(0);
                }
                if (rng.Chance(0.05))
                {
                    Add(7);
                }
                break;
            case PlanetType.Icy:
                Add(3);
                Add(5);
                break;
            case PlanetType.WaterWorld:
                Add(4);
                if (rng.Chance(0.2))
                {
                    Add(6);
                }
                break;
            case PlanetType.EarthLike:
                Add(0);
                Add(2);
                Add(4);
                Add(6);
                break;
            case PlanetType.AmmoniaWorld:
                Add(3);
                Add(5);
                break;
            default:
                Add(2);
                break;
        }
        if (b.Temperature < 150 && (m & (1 << 3)) == 0 && rng.Chance(0.5))
        {
            Add(3);
        }
        return m;
    }

    private static int PickCentre(List<LocalSystem> systems, double minLy, double maxLy, Vector3D home, double radius, ulong tag)
    {
        return Enumerable.Range(0, systems.Count)
            .Where(i => systems[i].Distance >= minLy && systems[i].Distance <= maxLy
                && (systems[i].Star.Position - home).Length > radius + 5)
            .OrderBy(i => Hash64.Of(Seed, tag, systems[i].Star.Address.Value))
            .First();
    }

    private static void WriteSystemRef(Utf8JsonWriter w, List<LocalSystem> systems, int sys, Body? body)
    {
        LocalSystem s = systems[sys];
        w.WriteNumber("sys", sys);
        w.WriteString("addr", Js.Hex(s.Star.Address.Value));
        w.WriteString("system", s.Name);
        if (body != null)
        {
            w.WriteNumber("bodyIndex", body.Index);
            w.WriteString("body", body.Name);
            w.WriteString("bodyType", body.TypeName);
        }
        else
        {
            w.WriteNull("bodyIndex");
            w.WriteNull("body");
        }
        w.WritePropertyName("pos");
        w.WriteStartArray();
        w.WriteNumberValue(Js.Round(s.Star.Position.X, 2));
        w.WriteNumberValue(Js.Round(s.Star.Position.Y, 2));
        w.WriteNumberValue(Js.Round(s.Star.Position.Z, 2));
        w.WriteEndArray();
        w.WriteNumber("distanceLy", Js.Round(s.Distance, 2));
    }

    private static void WriteDanger(Utf8JsonWriter w, List<LocalSystem> systems, int centre, string id, string name,
        string kind, double radius, string effect)
    {
        LocalSystem s = systems[centre];
        w.WriteStartObject();
        w.WriteString("id", id);
        w.WriteString("name", name);
        w.WriteString("kind", kind);
        w.WriteString("effect", effect);
        w.WritePropertyName("centre");
        w.WriteStartArray();
        w.WriteNumberValue(Js.Round(s.Star.Position.X, 2));
        w.WriteNumberValue(Js.Round(s.Star.Position.Y, 2));
        w.WriteNumberValue(Js.Round(s.Star.Position.Z, 2));
        w.WriteEndArray();
        w.WriteNumber("radiusLy", radius);
        w.WriteNumber("sys", centre);
        w.WriteString("system", s.Name);
        w.WriteEndObject();
    }
}
