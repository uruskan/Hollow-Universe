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

// Animals on the server (GROUND.md §6.9): the zone's population turned on, animals replicated as entities of their own
// kind in the snapshots (their state packed by AnimalNet), corpses saved while their timers run, admin commands.
public sealed partial class GameServer
{
    /// <summary>An animal's place in <see cref="_netStates"/>: no zone entity of its own.</summary>
    private static readonly Entity AnimalEntity = Entity.None;

    private readonly Dictionary<EntityId, uint> _animalNetIds = new();
    private readonly Dictionary<uint, Animal> _animalsByNet = new();

    // Each animal's last packed state and for how many snapshots in a row it hasn't changed.
    private readonly Dictionary<uint, (CharacterNetState State, int Same)> _animalStill = new();
    private readonly List<ThingRecord> _storedCorpses = new();

    private void StartAnimals()
    {
        if (Zone.Animals().World is not { } animals)
        {
            return;
        }
        animals.NaturalSpawning = _options.Animals;
        if (_options.AnimalPopulation > 0)
        {
            animals.Populate(Vector3.Zero, _options.AnimalRadius, _options.AnimalPopulation);
            Log($"{animals.Count} animals placed around the origin");
        }
    }

    // After every step: animals gone free their net IDs (their despawns go out with the next snapshot).
    private void AfterAnimals()
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
    private void PrepareAnimals()
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
                netId = _nextNetId++;
                _animalNetIds.Add(a.Id, netId);
                _animalsByNet.Add(netId, a);
            }
            _grid.Add(netId, a.Position);
            CharacterNetState state = AnimalNet.Pack(a);
            _netStates[netId] = (AnimalEntity, state);
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
    private bool AnimalAtRest(PlayerSession session, uint netId, in CharacterNetState state) =>
        _animalStill.TryGetValue(netId, out (CharacterNetState State, int Same) still) && still.Same >= 2 && session.Snapshots.Holds(netId, state);

    /// <summary>The Spawn of a net entity: a character (its name), or an animal (its species).</summary>
    private Spawn SpawnOf(uint netId, Entity entity) => _animalsByNet.TryGetValue(netId, out Animal? animal)
        ? new Spawn { NetId = netId, EntityId = animal.Id.Value, Kind = EntityKind.Animal, Name = animal.Species.Key }
        : new Spawn { NetId = netId, EntityId = Zone.Entities.IdOf(entity).Value, Kind = EntityKind.Character, Name = Zone.Entities.Get<Named>(entity).Name };

    private bool IsAnimal(uint netId) => _animalsByNet.ContainsKey(netId);

    // ---------------------------------------------------------------- corpses saved and restored

    private void AddAnimalsToSave(ZoneSave save, HashSet<EntityId> current)
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

    private bool StoredCorpse(ThingRecord t)
    {
        if (t.Kind != AnimalCodec.ThingKind)
        {
            return false;
        }
        _storedCorpses.Add(t);
        _savedThings.Add(t.Id);
        return true;
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
