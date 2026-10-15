using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using Hollow.Net.Protocol;
using Hollow.Persistence;
using Hollow.Simulation;
using Hollow.Simulation.Animals;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;

namespace Hollow.Server;

/// <summary>
/// Animals on the server (GROUND.md §6.9): the zone's population turned on, animals replicated as entities of their own
/// kind in the snapshots (their state packed by AnimalNet), corpses saved while their timers run, admin commands.
/// </summary>
public sealed class AnimalModule : ServerModule
{
    private readonly Dictionary<EntityId, uint> _animalNetIds = new();
    private readonly Dictionary<uint, Animal> _animalsByNet = new();

    // Each animal's last packed state and for how many snapshots in a row it hasn't changed.
    private readonly Dictionary<uint, (CharacterNetState State, int Same)> _animalStill = new();
    private readonly List<ThingRecord> _storedCorpses = new();

    protected internal override void Start()
    {
        if (Zone.Animals().World is not { } animals)
        {
            return;
        }
        animals.NaturalSpawning = Options.Animals;
        if (Options.AnimalCap > 0)
        {
            animals.MaxAnimals = Math.Min(Options.AnimalCap, animals.MaxAnimals);
        }
        if (Options.AnimalPopulation > 0)
        {
            animals.Populate(Zone.Landing, Options.AnimalRadius, Options.AnimalPopulation);
            Log($"{animals.Count} animals placed around the landing site");
        }
    }

    // After every step: animals gone free their net IDs (their despawns go out with the next snapshot).
    protected internal override void AfterStep()
    {
        if (Zone.Animals().World is not { } animals)
        {
            return;
        }
        foreach (Animal a in animals.Gone)
        {
            if (_animalNetIds.Remove(a.Id, out uint netId))
            {
                _animalsByNet.Remove(netId);
                _animalStill.Remove(netId);
            }
        }
    }

    // Once per snapshot: every animal (and corpse) in the relevance grid with its packed state.
    protected internal override void Prepare()
    {
        if (Zone.Animals().World is not { } animals)
        {
            return;
        }
        foreach (Animal a in animals.All)
        {
            if (a.Removed)
            {
                continue;
            }
            if (!_animalNetIds.TryGetValue(a.Id, out uint netId))
            {
                netId = NextNetId();
                _animalNetIds.Add(a.Id, netId);
                _animalsByNet.Add(netId, a);
            }
            CharacterNetState state = AnimalNet.Pack(a);
            Present(netId, a.Position, state);
            int same = _animalStill.TryGetValue(netId, out (CharacterNetState State, int Same) last) &&
                state.DeltaBits(last.State) <= Unchanged ? last.Same + 1 : 0;
            _animalStill[netId] = (state, same);
        }
    }

    private static readonly int Unchanged = default(CharacterNetState).DeltaBits(default);

    /// <summary>
    /// An animal standing still (the same state three snapshots running, so a client has it at least twice and draws
    /// it at rest) whose state this client has confirmed: nothing to send it until it moves. Grazing, idle and sleeping
    /// animals cost the snapshots nothing.
    /// </summary>
    protected internal override bool AtRest(PlayerSession session, uint netId, in CharacterNetState state) =>
        _animalStill.TryGetValue(netId, out (CharacterNetState State, int Same) still) && still.Same >= 2 && session.Snapshots.Holds(netId, state);

    /// <summary>The Spawn of an animal: its species.</summary>
    protected internal override void Describe(uint netId, ref Spawn spawn)
    {
        if (_animalsByNet.TryGetValue(netId, out Animal? animal))
        {
            spawn = new Spawn { NetId = netId, EntityId = animal.Id.Value, Kind = EntityKind.Animal, Name = animal.Species.Key };
        }
    }

    // ---------------------------------------------------------------- corpses saved and restored

    protected internal override void Save(ZoneSave save, HashSet<EntityId> current)
    {
        if (Zone.Animals().World is not { } animals)
        {
            return;
        }
        foreach (Animal a in animals.All)
        {
            if (!a.IsDead || a.Removed)
            {
                continue;
            }
            save.Things.Add(new ThingRecord
            {
                Id = a.Id,
                SystemAddress = Spec.SystemAddress,
                BodyPath = Spec.BodyPath,
                Kind = AnimalCodec.ThingKind,
                Data = AnimalCodec.Encode(new CorpseSave(a.Species.Key, a.Position, a.Yaw, a.HarvestLeft, Math.Max(0, a.DespawnTick - Zone.Tick),
                    a.KilledBy, a.KilledByName), Zone.Frame.ToBody),
            });
            current.Add(a.Id);
        }
    }

    protected internal override void Restore(ZoneRecord stored, HashSet<EntityId> saved)
    {
        foreach (ThingRecord t in stored.Things)
        {
            if (t.Kind == AnimalCodec.ThingKind)
            {
                _storedCorpses.Add(t);
                saved.Add(t.Id);
            }
        }
        RestoreCorpses();
    }

    private void RestoreCorpses()
    {
        if (Zone.Animals().World is not { } animals)
        {
            _storedCorpses.Clear();
            return;
        }
        int restored = 0;
        foreach (ThingRecord t in _storedCorpses)
        {
            try
            {
                CorpseSave c = AnimalCodec.Decode(t.Data, Zone.Frame.ToLocal);
                if (Content.Animal(c.Species) is { } species && animals.Find(t.Id) == null)
                {
                    animals.RestoreCorpse(t.Id, species, c.Position, c.Yaw, c.HarvestLeft, Zone.Tick + c.TicksLeft, c.KilledByName);
                    restored++;
                }
            }
            catch (Exception e) when (e is EndOfStreamException or InvalidDataException or ArgumentException)
            {
                Log($"animal corpse {t.Id} doesn't load ({e.Message}); left out");
            }
        }
        if (_storedCorpses.Count > 0)
        {
            Log($"restored {restored} of {_storedCorpses.Count} animal corpses with their timers");
        }
        _storedCorpses.Clear();
    }

    // ---------------------------------------------------------------- admin

    /// <summary>The <c>animals</c> command: how many, awake, of each kind, what the AI costs.</summary>
    public string AnimalStatus()
    {
        if (Zone.Animals().World is not { } animals)
        {
            return "no animals in this zone";
        }
        string kinds = string.Join(", ", animals.All.Where(a => !a.Removed).GroupBy(a => a.Species.Key)
            .Select(g => $"{g.Count(a => !a.IsDead)} {g.Key}" + (g.Any(a => a.IsDead) ? $" ({g.Count(a => a.IsDead)} dead)" : "")));
        double perTick = Zone.Tick > 0 ? Zone.Animals().AnimalTime.TotalMilliseconds / Zone.Tick : 0;
        return string.Create(CultureInfo.InvariantCulture,
            $"{animals.Count} animals ({animals.Awake} awake): {kinds}; spawning {(animals.NaturalSpawning ? "on" : "off")}, " +
            $"{animals.Areas.Count(a => a.Active)} areas out; {perTick:0.000} ms a tick on animals; {Zone.Animals().AnimalAttacks} attacks landed, " +
            $"{Zone.Animals().AnimalsKilled} killed");
    }
}

/// <summary>The accessor on the server (GROUND_SYSTEMS.md §12.5): the animals' replication, corpses and status.</summary>
public static class AnimalModuleAccess
{
    public static AnimalModule Animals(this GameServer server) => server.Require<AnimalModule>();
}
