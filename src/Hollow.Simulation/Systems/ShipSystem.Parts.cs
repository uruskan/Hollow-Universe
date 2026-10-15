using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Simulation.Building;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Physics;
using Hollow.Simulation.Players;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Targets;

namespace Hollow.Simulation;

// A ship's crew, lock and parts (IRON_WAKE.md §5.2, §5.5): its list of authorised players (the tool cupboard's verbs at its
// helm: the ship is its own cupboard), its lock (its doors stay shut to anyone not on its list), and the health of its
// parts: a door's (as an armoured door: broken, it jams open) and a battery's (disabled at 0, its gunner put out), taken
// through the damage pipeline's receiver for ships and mended with the hammer, as the building's pieces are.
public sealed partial class ShipSystem
{
    // ---------------------------------------------------------------- the crew and the lock

    /// <summary>Puts a player on a ship's list (anyone who reaches its helm may, as at a tool cupboard; the list has a limit).</summary>
    internal Outcome Authorise(ulong player, Ship ship)
    {
        if (!ship.Authorise(player))
        {
            return OutcomeCode.ListFull;
        }
        return Outcome.Done;
    }

    /// <summary>Takes a player off a ship's list.</summary>
    internal Outcome Deauthorise(ulong player, Ship ship)
    {
        ship.Deauthorise(player);
        return Outcome.Done;
    }

    /// <summary>Clears a ship's list; only someone on it may.</summary>
    internal Outcome ClearCrew(ulong player, Ship ship)
    {
        if (!ship.IsAuthorised(player))
        {
            return OutcomeCode.NotCrew;
        }
        ship.ClearCrew();
        return Outcome.Done;
    }

    /// <summary>Puts a player and their team on a ship's list (as many as fit).</summary>
    internal Outcome AuthoriseTeam(ulong player, Ship ship, List<ulong> members)
    {
        if (!ship.Authorise(player))
        {
            return OutcomeCode.ListFull;
        }
        foreach (ulong m in members)
        {
            if (m != player && !ship.Authorise(m))
            {
                break;
            }
        }
        return Outcome.Done;
    }

    /// <summary>Locks or unlocks a ship's doors: its crew alone.</summary>
    internal Outcome SetShipLock(Player p, Ship ship, bool locked)
    {
        if (!ship.IsAuthorised(p.Id.Value))
        {
            return OutcomeCode.NotCrew;
        }
        if (ship.Locked != locked)
        {
            ship.Locked = locked;
            ship.Revision++;
        }
        return OutcomeCode.Done;
    }

    // ---------------------------------------------------------------- parts' health

    /// <summary>Whether a seat's station is disabled: the part it sits in has health, and none left (IRON_WAKE.md §5.5).</summary>
    public bool SeatDisabled(Ship ship, int seat)
    {
        int part = ship.Def.PartOfSeat(seat);
        return part > 0 && ship.Def.Parts[part].Health != null && ship.PartHealth[part] <= 0;
    }

    /// <summary>
    /// What a target part's health is (its own, or its door's), its most, and whether it has any: the hull's for part 0
    /// (and a part without health).
    /// </summary>
    public (float Health, float Max, ShipPartHealth? Def) PartHealthOf(Ship ship, int part)
    {
        if (part > 0 && part < ship.Def.Parts.Count)
        {
            ShipTargetPart p = ship.Def.Parts[part];
            if (p.Door >= 0 && ship.Def.Doors[p.Door].Health is { } door)
            {
                return (ship.Doors[p.Door].Health, door.Max, door);
            }
            if (p.Health is { } own)
            {
                return (ship.PartHealth[part], own.Max, own);
            }
        }
        return (ship.State.Health, ship.Def.Health, null);
    }

    // A blow on a part with health (a door, a battery): its protection takes what reaches it, as a building's (a blast by
    // the explosive's table for its grade, whole on the part it hit and a splash share on the rest); at 0 a door jams open
    // and a battery is disabled, its gunner put out. Returns the health lost.
    internal float HurtPart(Ship ship, int part, ShipPartHealth health, in DamageRequest r)
    {
        ShipTargetPart p = ship.Def.Parts[part];
        float current = p.Door >= 0 ? ship.Doors[p.Door].Health : ship.PartHealth[part];
        if (current <= 0 || ship.Destroyed || ship.InOrbit)
        {
            return 0;
        }
        DamageHit hit;
        if (r.Blast is { } table)
        {
            double share = r.Falloff * (r.Direct ? 1 : table.Splash);
            double amount = (health.DoorTable ? table.Door(health.Protection.Key) : table.Piece(health.Protection.Key, false)) * share;
            hit = new DamageHit(r.Direction, Explosive: amount);
        }
        else
        {
            hit = r.Channel == DamageType.Melee ? new DamageHit(r.Direction, Melee: r.Amounts.Building) : new DamageHit(r.Direction, Bullet: r.Amounts.Building);
        }
        float lost = (float)BuildingWorld.Resolve(health.Protection.Hard, hit, current);
        if (lost <= 0)
        {
            return 0;
        }
        current -= lost;
        ship.LastDamagedAt = Zone.Get<BuildingSystem>()?.BuildingNow ?? Tick / (double)CharacterMotor.TickRate;
        if (r.By != null)
        {
            ship.LastAttacker = r.By.Id;
            ship.LastAttackerName = r.By.Name;
        }
        if (p.Door >= 0)
        {
            ship.Doors[p.Door].Health = current;
            if (current <= 0)
            {
                // Broken: it falls open, and stays so until mended (IRON_WAKE.md §5.3).
                SetDoorAtOnce(ship, p.Door, 1);
            }
        }
        else
        {
            ship.PartHealth[part] = current;
            if (current <= 0 && p.Seat >= 0 && Entities.TryFind(ship.Occupants[p.Seat], out Entity gunner))
            {
                // Disabled: its gunner is put out (onto the walkway), or stays put while every way out is blocked.
                Release(gunner, SeatRelease.Disabled);
            }
        }
        ship.DetailsRevision++;
        return lost;
    }

    /// <summary>
    /// One hammer hit of repair on a ship's part with health (a door, a battery; IRON_WAKE.md §5.5), as the building's: up to
    /// building.json's <c>repairPerHit</c>, not within its <c>repairDelay</c> of damage, for max(1, ⌈full cost × restored /
    /// most⌉) of the part's material; its crew alone (or anyone, on a ship with no crew). A broken door, mended to full, works
    /// again (it stays open until shut).
    /// </summary>
    internal Outcome RepairPart(Entity entity, Player p, Ship ship, int part, IBuildingWallet wallet)
    {
        (float health, float max, ShipPartHealth? def) = PartHealthOf(ship, part);
        if (def == null || ship.Destroyed)
        {
            return OutcomeCode.NotUsable;
        }
        if (ship.Crew.Count > 0 && !ship.IsAuthorised(p.Id.Value))
        {
            return OutcomeCode.NotCrew;
        }
        if (health >= max)
        {
            return OutcomeCode.FullHealth;
        }
        BuildingData data = Content.Building;
        double now = Zone.Get<BuildingSystem>()?.BuildingNow ?? Tick / (double)CharacterMotor.TickRate;
        double wait = ship.LastDamagedAt + data.RepairDelay - now;
        if (wait > 0)
        {
            return OutcomeCode.RecentlyDamaged.With((int)Math.Ceiling(wait));
        }
        float restored = MathF.Min((float)data.RepairPerHit, max - health);
        int cost = (int)Math.Max(1, Math.Ceiling(def.RepairFull * (double)restored / max - 1e-9));
        string material = def.RepairMaterial.Key;
        if (wallet.Count(material) < cost || !wallet.Take(material, cost))
        {
            return OutcomeCode.NotEnough.With(def.RepairMaterial.Id);
        }
        ShipTargetPart target = ship.Def.Parts[part];
        if (target.Door >= 0)
        {
            ship.Doors[target.Door].Health = MathF.Min(max, health + restored);
        }
        else
        {
            ship.PartHealth[part] = MathF.Min(max, health + restored);
        }
        ship.DetailsRevision++;
        Events.Add(new ItemsGained(Player: p.Id, Item: def.RepairMaterial.Id, Count: -cost, Reason: GainReason.Used));
        return Outcome.Done;
    }

    // ---------------------------------------------------------------- the damage pipeline's receiver for ships

    /// <summary>
    /// Ships (GROUND.md §6.4.2, §6.7; IRON_WAKE.md §5.5): a ship people walk in is met by its space's pieces along a ray
    /// (IRON_WAKE.md §4.9: through its open ramp a shot goes on to whoever stands inside; through the hull it meets the hull),
    /// each piece naming its target part (the ramp's leaf the ramp, a turret its battery, the rest the hull); any other by
    /// its hull boxes; each where the attacker saw it (the pose history). In a blast's reach: the hull by its nearest point,
    /// and each part with health by its own pieces' (a door the explosive's doors' table, whole when it is stuck to it). A
    /// ship in orbit or wrecked is out of reach. Its defences: a part's protection (an armoured door's, a battery's metal),
    /// else the hull's share by how the blow came (ships.json). Its consequences: a part jammed or disabled; the hull's
    /// health and last attacker, and at 0 the wreck.
    /// </summary>
    internal sealed class ShipReceiver(Zone zone) : DamageSystem.IDamageReceiver
    {
        private readonly List<(int Part, Vector3 Point, float Distance)> _near = new();

        public TargetFamily[] Families { get; } = { TargetFamily.Ship };

        public void Raycast(in RayQuery ray, ref TraceHit best)
        {
            ShipSystem ships = zone.Ships();
            if (ships._ships.Count == 0)
            {
                return;
            }
            if (zone.Frames.Ships.Count > 0 &&
                ShipQueries.Ray(zone.Frames.Ships, ray.Origin, ray.Direction, ray.Upto(best), zone.Tick, out ShipQueryHit hit, zone.History, ray.ViewTick) &&
                ships.ShipOf(new EntityId(hit.Key)) is { Destroyed: false, InOrbit: false } walked)
            {
                ray.Consider(ref best, hit.Distance, walked.Ref with { Part = (byte)walked.Def.PartOfPieces(hit.Hit.Part) });
            }
            float t = ships.TraceShips(ray.Origin, ray.Direction, ray.Length, ray.Thickness, out Ship? ship, ray.ViewTick);
            if (ship != null)
            {
                ray.Consider(ref best, t, ship.Ref);
            }
        }

        public void InReach(in BlastQuery blast, List<BlastTarget> into)
        {
            foreach (Ship ship in zone.Ships()._ships)
            {
                if (ship.Destroyed || ship.InOrbit)
                {
                    continue;
                }
                bool stuck = blast.IsStuckTo(ship.Ref);
                Vector3 point;
                if (ship.Frame != null)
                {
                    ShipPose pose = ShipFrame.PoseOf(ship.State, ship.Def.CentreOfMass);
                    // Its parts with health, each by its own pieces; the hull by every piece.
                    _near.Clear();
                    ShipQueries.NearestByPart(ship.Frame, pose, blast.At, blast.Radius, _near);
                    foreach ((int dataPart, Vector3 at, float distance) in _near)
                    {
                        int part = ship.Def.PartOfPieces(dataPart);
                        if (part <= 0 || zone.Ships().PartHealthOf(ship, part).Def is null || into.Exists(t => t.Hit.Target == ship.Ref with { Part = (byte)part }))
                        {
                            continue;
                        }
                        bool onPart = stuck && blast.StuckTo.Part == part;
                        into.Add(Target(ship, (byte)part, at, onPart ? 0 : distance, onPart, blast.At, table: true));
                    }
                    if (!ShipQueries.Nearest(ship.Frame, pose, blast.At, blast.Radius, out point, out _, out _) && !stuck)
                    {
                        continue;
                    }
                }
                else
                {
                    point = ShipClosestPoint(ship, blast.At);
                }
                bool onHull = stuck && (blast.StuckTo.Part == 0 || zone.Ships().PartHealthOf(ship, blast.StuckTo.Part).Def is null);
                float distanceToHull = onHull ? 0 : Vector3.Distance(point, blast.At);
                if (distanceToHull > blast.Radius)
                {
                    continue;
                }
                into.Add(Target(ship, 0, point, distanceToHull, onHull, blast.At, table: false));
            }
        }

        private static BlastTarget Target(Ship ship, byte part, Vector3 point, float distance, bool stuck, Vector3 from, bool table)
        {
            Vector3 off = from - point;
            Vector3 seen = off.LengthSquared() > 1e-6f ? point + Vector3.Normalize(off) * 0.05f : point;
            return new BlastTarget
            {
                Hit = new TraceHit { Target = ship.Ref with { Part = part }, Point = point }, Thing = part == 0 ? ship : (object)(ship, part), Sight = seen,
                Sight2 = seen, Distance = distance, Stuck = stuck, Table = table,
            };
        }

        public DamageOutcome Receive(in DamageRequest r)
        {
            if (zone.Ships().ShipOf(new EntityId(r.Hit.Target.Id)) is not { } ship || ship.Destroyed || ship.InOrbit)
            {
                return default;
            }
            int part = r.Hit.Target.Part;
            if (zone.Ships().PartHealthOf(ship, part) is { Def: { } health })
            {
                float taken = zone.Ships().HurtPart(ship, part, health, r);
                return new DamageOutcome(true, taken, taken, false, false, ship.Id);
            }
            float lost = zone.Ships().HurtShip(ship, r.Amounts.Hull, r.Delivery, r.By);
            return new DamageOutcome(true, lost, lost, ship.Destroyed, false, ship.Id);
        }
    }
}
