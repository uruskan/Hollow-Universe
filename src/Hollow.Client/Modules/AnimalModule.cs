using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Net.Protocol;
using Hollow.Simulation.Animals;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;

namespace Hollow.Client;

/// <summary>What happened to an animal in view, for the visual layer and sound (GROUND.md §6.9).</summary>
public enum AnimalEventKind : byte
{
    /// <summary>It came into view (or was born): make its view.</summary>
    Appeared,

    /// <summary>It began an attack (bite, charge, swipe): the wind-up starts now.</summary>
    Attack,

    /// <summary>It was hit: a flinch, blood, a cry.</summary>
    Hurt,

    /// <summary>It died: the death, its body stays as a corpse.</summary>
    Died,

    /// <summary>It took fright and runs (the alarm call).</summary>
    Fled,

    /// <summary>It started a chase or a warning (a growl, a snort, a roar).</summary>
    Threatens,

    /// <summary>It left view, or its corpse went: drop its view.</summary>
    Gone,
}

public readonly record struct AnimalEvent(AnimalEventKind Kind, ClientAnimal Animal);

/// <summary>
/// An animal as this client sees it (GROUND.md §6.9): interpolated 100 ms behind the server like every remote entity,
/// with its species, what it is doing, its health (or a corpse's harvest left), whether it is alert, and its speed over
/// the ground. The visual layer draws it from these alone.
/// </summary>
public sealed class ClientAnimal
{
    internal ClientAnimal(RemoteEntity remote, AnimalSpecies? species, string key)
    {
        Remote = remote;
        Species = species;
        SpeciesKey = key;
    }

    internal RemoteEntity Remote { get; }

    public uint NetId => Remote.NetId;

    public EntityId Id => Remote.Id;

    /// <summary>Its species from animals.json (null if this client's data doesn't know the key: its own data is older).</summary>
    public AnimalSpecies? Species { get; }

    public string SpeciesKey { get; }

    /// <summary>Its feet on the ground and its heading (the characters' yaw), drawn now.</summary>
    public Vector3 Position => Remote.Position;

    public float Yaw => Remote.Yaw;

    public AnimalState State { get; private set; }

    public bool Dead { get; private set; }

    /// <summary>Aware of a threat: head up, ears forward.</summary>
    public bool Alert { get; private set; }

    /// <summary>Warning someone off before it charges: a growl, a snort, a bear rearing.</summary>
    public bool Warning { get; private set; }

    /// <summary>Health as a fraction (0 … 1); a corpse's: the harvest left.</summary>
    public float Health { get; private set; }

    /// <summary>Speed over the ground (m/s), smoothed: the gait to animate.</summary>
    public float Speed { get; private set; }

    /// <summary>Attacks begun and hits taken (wrapping 4-bit counts), and the last attack's kind.</summary>
    public byte AttackCount { get; private set; }

    public byte HurtCount { get; private set; }

    public AttackKind LastAttack { get; private set; }

    /// <summary>Seconds since it last attacked, was hurt, died (large when never): for one-shot animations.</summary>
    public double SinceAttack { get; private set; } = 1e9;

    public double SinceHurt { get; private set; } = 1e9;

    public double SinceDeath { get; private set; } = 1e9;

    private Vector3 _last;
    private bool _first = true;

    internal void Update(double delta, List<AnimalEvent> events)
    {
        AnimalLook look = AnimalNet.Unpack(new CharacterNetState
        {
            Held = Remote.Held,
            Flags = Remote.Flags,
            Health = Remote.Health,
            ActionCount = Remote.ActionCount,
            Action = Remote.Action,
            HeldMods = Remote.HeldMods,
            Aiming = Remote.Aiming,
        });
        SinceAttack += delta;
        SinceHurt += delta;
        SinceDeath += delta;
        if (_first)
        {
            _first = false;
            _last = Position;
            AttackCount = look.AttackCount;
            HurtCount = look.HurtCount;
            Dead = look.Dead;
            State = look.State;
            events.Add(new AnimalEvent(AnimalEventKind.Appeared, this));
        }
        else
        {
            if (look.AttackCount != AttackCount)
            {
                SinceAttack = 0;
                events.Add(new AnimalEvent(AnimalEventKind.Attack, this));
            }
            if (look.HurtCount != HurtCount)
            {
                SinceHurt = 0;
                events.Add(new AnimalEvent(AnimalEventKind.Hurt, this));
            }
            if (look.Dead && !Dead)
            {
                SinceDeath = 0;
                events.Add(new AnimalEvent(AnimalEventKind.Died, this));
            }
            if (look.State == AnimalState.Flee && State != AnimalState.Flee)
            {
                events.Add(new AnimalEvent(AnimalEventKind.Fled, this));
            }
            if ((look.State == AnimalState.Chase && State is not (AnimalState.Chase or AnimalState.Attack)) || (look.Warning && !Warning))
            {
                events.Add(new AnimalEvent(AnimalEventKind.Threatens, this));
            }
        }
        AttackCount = look.AttackCount;
        HurtCount = look.HurtCount;
        LastAttack = look.Attack;
        Dead = look.Dead;
        State = look.State;
        Alert = look.Alert;
        Warning = look.Warning;
        Health = look.Health;
        if (delta > 1e-6)
        {
            Vector3 d = Position - _last;
            float speed = MathF.Sqrt(d.X * d.X + d.Z * d.Z) / (float)delta;
            float k = 1 - MathF.Exp(-(float)delta * 8);
            Speed = Dead ? 0 : Speed + (MathF.Min(speed, 30) - Speed) * k;
        }
        _last = Position;
    }

    public override string ToString() => $"{SpeciesKey} {NetId} {State}{(Dead ? " (dead)" : "")}";
}

// Animals in the client (GROUND.md §6.9): remote entities of kind Animal, kept apart from the characters (Entities lists
// characters only), with their events as the drawn state passes them.
public sealed class AnimalModule : ClientModule
{
    private readonly Dictionary<uint, ClientAnimal> _animals = new();
    private readonly List<AnimalEvent> _animalEvents = new();
    private readonly List<uint> _animalsGone = new();

    /// <summary>The animals in view (alive, and corpses).</summary>
    public Dictionary<uint, ClientAnimal>.ValueCollection All => _animals.Values;

    public ClientAnimal? AnimalOf(uint netId) => _animals.GetValueOrDefault(netId);

    /// <summary>An animal appeared, attacked, was hurt, died, took fright, threatened, or went (raised from <see cref="Update"/>).</summary>
    public event Action<AnimalEvent>? AnimalEvent;

    protected internal override void Update(double delta)
    {
        _animalEvents.Clear();
        foreach (RemoteEntity remote in Remotes.Values)
        {
            if (remote.Kind != EntityKind.Animal || !remote.Spawned || !remote.HasState)
            {
                continue;
            }
            if (!_animals.TryGetValue(remote.NetId, out ClientAnimal? animal))
            {
                animal = new ClientAnimal(remote, Content.Animal(remote.Name), remote.Name);
                _animals.Add(remote.NetId, animal);
            }
            animal.Update(delta, _animalEvents);
        }
        _animalsGone.Clear();
        foreach ((uint netId, ClientAnimal animal) in _animals)
        {
            if (!Remotes.TryGetValue(netId, out RemoteEntity? remote) || remote != animal.Remote)
            {
                _animalsGone.Add(netId);
            }
        }
        foreach (uint netId in _animalsGone)
        {
            _animals.Remove(netId, out ClientAnimal? animal);
            _animalEvents.Add(new AnimalEvent(AnimalEventKind.Gone, animal!));
        }
        foreach (AnimalEvent e in _animalEvents)
        {
            AnimalEvent?.Invoke(e);
        }
    }
}

/// <summary>The accessor on the client (GROUND_SYSTEMS.md §12.5, K11).</summary>
public static class AnimalModuleAccess
{
    public static AnimalModule Animals(this GameClient client) => client.Require<AnimalModule>();
}
