using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation.Animals;
using Hollow.Simulation.Building;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Simulation.World;
using Hollow.Simulation.Targets;

namespace Hollow.Simulation;

// Animals in the zone (GROUND.md §6.9): the glue between the animal world (Animals/) and the rest of the game. Players
// are observed and the tick's shots, blasts and blows are heard; animals' attacks hurt players through their armour;
// swings, arrows, bullets and blasts hurt animals (as the attacker saw them); tools harvest corpses like gathering.
public sealed partial class Zone : IAnimalHost
{
    private readonly List<Piece> _animalPieces = new();
    private readonly List<Deployable> _animalThings = new();
    private readonly List<Animal> _animalTrace = new();
    private int _heard;

    /// <summary>The zone's animals (null in a movement-only zone).</summary>
    public AnimalWorld? Animals { get; private set; }

    /// <summary>Time spent on animals (their AI, movement, the population), for statistics and the load test.</summary>
    public TimeSpan AnimalTime { get; private set; }

    private void InitAnimals(GameContent content)
    {
        Animals = new AnimalWorld(this, this, content, Hash64.Of(Spec.SurfaceSeed, 0xA417A15));
    }

    private void StepAnimals()
    {
        if (Animals == null)
        {
            return;
        }
        long started = Stopwatch.GetTimestamp();
        Observe();
        Listen();
        Animals.Step();
        foreach (Animal gone in Animals.Gone)
        {
            SupportGone(gone.Ref);
            if (gone.IsDead)
            {
                Unloose(gone);
            }
        }
        AnimalTime += Stopwatch.GetElapsedTime(started);
    }

    // Players as the animals see and hear them: where, how fast, how loud their steps are, crouching, lying.
    private void Observe()
    {
        AnimalWorld animals = Animals!;
        AnimalSenses senses = Content!.AnimalSenses;
        animals.Observers.Clear();
        ComponentPool<PlayerLink> players = Entities.Pool<PlayerLink>();
        for (int i = 0; i < players.Count; i++)
        {
            Player p = players.Values[i].Player;
            if (p.Dead)
            {
                continue;
            }
            ref Character c = ref Entities.Get<Character>(Entities.AtSlot(players.Slots[i]));
            Vector3 v = c.State.Velocity;
            Vector3 up = Frame.UpAt(c.State.Position);
            float speed = (v - up * Vector3.Dot(v, up)).Length();
            bool crouching = c.State.Stance == Stance.Crouching;
            float noise = (c.State.Flags & MoveFlags.Swimming) != 0 ? senses.Swim
                : speed < 0.4f ? 0
                : (c.State.Flags & MoveFlags.Sprinting) != 0 || speed > 4f ? senses.Sprint
                : crouching ? senses.Crouch : senses.Walk;
            animals.Observers.Add(new AnimalObserver
            {
                Id = p.Id,
                Position = c.State.Position,
                Velocity = v,
                Noise = noise,
                Crouching = crouching,
                Lying = p.Sleeping || p.Wounded,
            });
        }
    }

    // The tick's noises: shots (silenced ones carry less), arrows loosed, blasts, chopping and blows.
    private void Listen()
    {
        AnimalWorld animals = Animals!;
        AnimalSenses senses = Content!.AnimalSenses;
        if (_heard > GameEvents.Count)
        {
            _heard = 0; // the server cleared the list after the last tick
        }
        for (int i = _heard; i < GameEvents.Count; i++)
        {
            ZoneEvent e = GameEvents[i];
            switch (e.Kind)
            {
                case ZoneEventKind.ProjectileLaunched when e.Seq != 0:
                    animals.Noises.Add(new AnimalNoise { Position = e.Position, Radius = e.Flag ? senses.Silenced : senses.Gunshot, Tick = Tick, Source = e.Player, Loud = !e.Flag });
                    break;
                case ZoneEventKind.ProjectileLaunched:
                    animals.Noises.Add(new AnimalNoise { Position = e.Position, Radius = senses.Bow, Tick = Tick, Source = e.Player });
                    break;
                case ZoneEventKind.Detonated:
                    animals.Noises.Add(new AnimalNoise { Position = e.Position, Radius = senses.Explosion, Tick = Tick, Source = e.Player, Loud = true });
                    break;
                case ZoneEventKind.HitConfirmed when e.Target.Family == TargetFamily.Natural:
                    animals.Noises.Add(new AnimalNoise { Position = e.Position, Radius = senses.Gather, Tick = Tick, Source = e.Player });
                    break;
                case ZoneEventKind.HitConfirmed when e.Target.Family is TargetFamily.Piece or TargetFamily.Door or TargetFamily.Cupboard or TargetFamily.Thing
                    or TargetFamily.Character:
                    animals.Noises.Add(new AnimalNoise { Position = e.Position, Radius = senses.Melee, Tick = Tick, Source = e.Player });
                    break;
            }
        }
        _heard = GameEvents.Count;
    }

    // ---------------------------------------------------------------- what the animals ask of the zone

    /// <summary>
    /// An animal's attack lands on a player (GROUND.md §6.9): its damage less the armour worn where it lands (against
    /// blows), bleeding in proportion; it may knock them down (the wounded state) and, on a downed player, finish them.
    /// </summary>
    public bool AnimalAttacksPlayer(Animal animal, EntityId player)
    {
        if (Content == null || animal.Species.Attack is not { } attack || !TryFindPlayer(player, out Entity entity, out Player p) || p.Dead)
        {
            return false;
        }
        // An animal's attack is a blow from its head on the region it goes for, through the characters' receiver.
        DamageOutcome bite = Damage(new DamageRequest
        {
            Attacker = EntityId.None,
            AttackerName = animal.Species.Name,
            ByAnimal = animal,
            Delivery = DamageDelivery.Bite,
            Channel = DamageType.Melee,
            Amounts = new DamageAmounts { Flesh = attack.Damage },
            Bleed = attack.Bleed,
            Cause = attack.Kind switch
            {
                AttackKind.Bite => "Bitten",
                AttackKind.Charge => "Gored",
                _ => "Mauled",
            },
            Direct = true,
            Hit = new TraceHit
            {
                Target = new TargetRef(TargetFamily.Character, Entities.IdOf(entity).Value), Region = attack.Region,
                Point = Entities.Get<Character>(entity).State.Position,
            },
            From = AnimalHitboxes.Head(animal.Species, PoseOf(animal)),
            Markers = TargetFamilies.None,
        });
        if (!bite.Landed)
        {
            return false;
        }
        AnimalAttacks++;
        return true;
    }

    /// <summary>Attacks animals landed on players, and animals killed (by anyone), for statistics.</summary>
    public long AnimalAttacks { get; private set; }

    public long AnimalsKilled { get; private set; }

    public float ObstacleAlong(Vector3 from, Vector3 direction, float length)
    {
        float best = -1;
        if (Building != null && TraceBuilding(from, direction, length, out float wall, out _))
        {
            best = wall;
        }
        _animalThings.Clear();
        DeployablesNear(from + direction * (length / 2), length / 2 + 2f, _animalThings);
        foreach (Deployable d in _animalThings)
        {
            (Vector3 right, Vector3 forward) = Geometry.Axes(d.Up, d.Yaw);
            float t = Geometry.RayBox(from, direction, length, d.Centre, d.Def.HalfExtents + new Vector3(0.35f, 0.2f, 0.35f), right, d.Up, forward);
            if (t >= 0 && (best < 0 || t < best))
            {
                best = t;
            }
        }
        return best;
    }

    public bool ObstaclesNear(Vector3 point, float radius)
    {
        if (Building != null)
        {
            Building.PiecesNear(point, point, radius + 3f, _animalPieces);
            if (_animalPieces.Count > 0)
            {
                return true;
            }
        }
        _animalThings.Clear();
        DeployablesNear(point, radius, _animalThings);
        return _animalThings.Count > 0;
    }

    private HitPose PoseOf(Animal a) => new()
    {
        Feet = a.Position,
        Up = Frame.UpAt(a.Position),
        Yaw = a.Yaw,
        Pose = a.IsDead ? Combat.Pose.Lying : Combat.Pose.Standing,
    };

    // ---------------------------------------------------------------- what the game does to animals

    /// <summary>
    /// Damage to an animal (GROUND.md §6.9) from a player's blow, arrow, bullet or blast, or another animal's bite (tests,
    /// the animals' own attacks): a request of <paramref name="amount"/> (already for the body part) through the
    /// animals' receiver; the attacker hears of the hit (a hit marker, the kill). Returns the health taken.
    /// </summary>
    public float DamageAnimal(Animal a, float amount, HitRegion region, Vector3 from, Player? attacker, EntityId attackerId = default,
        string attackerName = "", ushort weapon = 0, Animal? by = null) => Damage(new DamageRequest
        {
            By = attacker,
            Attacker = attacker?.Id ?? attackerId,
            AttackerName = attacker?.Name ?? attackerName,
            ByAnimal = by,
            Weapon = weapon,
            Delivery = by != null ? DamageDelivery.Bite : DamageDelivery.Melee,
            Channel = DamageType.Melee,
            Amounts = new DamageAmounts { Flesh = amount },
            Direct = true,
            Hit = new TraceHit { Target = a.Ref, Region = region, Point = AnimalHitboxes.Middle(a.Species, PoseOf(a)) },
            From = from,
            Markers = TargetFamilies.All,
        }).Taken;

    /// <summary>
    /// The animals' receiver (GROUND.md §6.4.2, §6.9): animals found where the attacker saw them (their hit history) by
    /// their hit shapes, corpses as they lie, and by their middles in a blast's reach. No armour. Its consequences: the
    /// health taken, a flinch, turning on the attacker or running, the kill counted; a blow of a tool on a corpse
    /// butchers it instead (the gathering rule, which confirms its own hit).
    /// </summary>
    private sealed class AnimalReceiver(Zone zone) : IDamageReceiver
    {
        public TargetFamily[] Families { get; } = { TargetFamily.Animal };

        public void Raycast(in RayQuery ray, ref TraceHit best)
        {
            if (zone.Animals != null && zone.TraceAnimal(ray.Origin, ray.Direction, ray.Length, ray.Thickness, ray.ViewTick, out float at, out Animal? animal,
                out HitRegion region, out int shape))
            {
                ray.Consider(ref best, at, new TargetRef(TargetFamily.Animal, animal!.Id.Value, (byte)(shape + 1)), region);
            }
        }

        public void InReach(in BlastQuery blast, List<BlastTarget> into)
        {
            if (zone.Animals == null)
            {
                return;
            }
            zone.Animals.Near(blast.At, blast.Radius + 3, zone._animalTrace);
            foreach (Animal a in zone._animalTrace)
            {
                if (a.IsDead || a.Removed)
                {
                    continue;
                }
                Vector3 middle = AnimalHitboxes.Middle(a.Species, zone.PoseOf(a));
                float distance = MathF.Max(0, Vector3.Distance(middle, blast.At) - a.Species.Radius);
                if (distance <= blast.Radius)
                {
                    into.Add(new BlastTarget
                    {
                        Hit = new TraceHit { Target = a.Ref, Region = HitRegion.Body, Point = middle }, Thing = a, Sight = middle,
                        Sight2 = middle, Distance = distance,
                    });
                }
            }
        }

        public DamageOutcome Receive(in DamageRequest r)
        {
            if (zone.Animals?.Find(new EntityId(r.Hit.Target.Id)) is not { } a)
            {
                return default;
            }
            if (a.IsDead)
            {
                if (r.Channel == DamageType.Melee && r.By is { } p && zone.Content!.Item(r.Weapon) is { } tool && zone.TryFindPlayer(p.Id, out Entity entity, out _))
                {
                    zone.HarvestAnimal(p, entity, a, tool, r.Hit.Point);
                }
                return default;
            }
            float amount = r.Amounts.Flesh;
            if (zone.Animals == null || a.Removed || amount <= 0)
            {
                return default;
            }
            AnimalTarget source = r.By != null ? AnimalTarget.Player(r.By.Id) : r.ByAnimal != null ? AnimalTarget.Animal(r.ByAnimal.Id) : AnimalTarget.None;
            float taken = zone.Animals.Hurt(a, amount, r.From, source, r.By?.Name ?? r.AttackerName);
            if (a.IsDead)
            {
                zone.AnimalsKilled++;
            }
            return new DamageOutcome(true, amount, taken, a.IsDead, false, a.Id);
        }
    }

    // A tool on a corpse: gathering (GROUND.md §6.1's rule: a better tool is quicker, not richer).
    private void HarvestAnimal(Player p, Entity entity, Animal corpse, ItemDef tool, Vector3 point)
    {
        float rate = Content!.HarvestRates.Rate(tool);
        if (rate <= 0)
        {
            return;
        }
        List<ItemAmount> gained = Animals!.Harvest(corpse, rate);
        EndIfEmptied(corpse); // harvested out: it goes at once
        GameEvents.Add(new ZoneEvent
        {
            Kind = ZoneEventKind.HitConfirmed, Player = p.Id, Other = corpse.Id, Target = corpse.Ref, Region = HitRegion.Body, Amount = rate,
            Position = point, Item = tool.Id,
        });
        Animals.Noises.Add(new AnimalNoise { Position = point, Radius = Content.AnimalSenses.Gather, Tick = Tick, Source = p.Id });
        if (gained.Count > 0)
        {
            Give(entity, p, gained, GainReason.Gathered);
        }
    }

    /// <summary>
    /// The nearest animal along a thickened ray, as it was at <paramref name="viewTick"/> (the attacker's view, from the
    /// animals' hit history); corpses as they lie now.
    /// </summary>
    private bool TraceAnimal(Vector3 origin, Vector3 direction, float length, float thickness, double viewTick, out float distance,
        out Animal? hit, out HitRegion region, out int shape)
    {
        distance = -1;
        hit = null;
        region = HitRegion.None;
        shape = -1;
        AnimalWorld animals = Animals!;
        Vector3 middle = origin + direction * (length / 2);
        animals.Near(middle, length / 2 + 4f, _animalTrace);
        foreach (Animal a in _animalTrace)
        {
            if (a.Removed)
            {
                continue;
            }
            HitPose pose;
            if (a.IsDead || !animals.History.Sample(a.Id, viewTick, out pose))
            {
                pose = PoseOf(a);
            }
            float t = AnimalHitboxes.Raycast(a.Species, pose, origin, direction, length, thickness, out HitRegion r, out int s);
            if (t >= 0 && (distance < 0 || t < distance))
            {
                distance = t;
                hit = a;
                region = r;
                shape = s;
            }
        }
        return hit != null;
    }
}
