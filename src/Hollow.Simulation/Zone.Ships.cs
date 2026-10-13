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
using Hollow.Simulation.World;
using Hollow.Simulation.Targets;

namespace Hollow.Simulation;

/// <summary>What happened to a ship that players should hear about (G6).</summary>
public enum ShipEventKind : byte
{
    /// <summary>A ship rolled out of a vehicle workbench (to its builder).</summary>
    Built,

    /// <summary>A ship was destroyed (to everyone near: the explosion).</summary>
    Destroyed,

    /// <summary>A ship crashed into something hard enough to hurt it (to everyone near).</summary>
    Crashed,

    /// <summary>A ship reached orbit (amount 1) or came back down from it (0), to its pilot.</summary>
    Orbit,
}

/// <summary>What kind of weapon hurt a ship (its hull takes a share per kind, ships.json).</summary>
public enum ShipDamage : byte
{
    Melee,
    Arrow,
    Bullet,
    Explosive,
}

/// <summary>Something that happened to a ship (<see cref="Zone.ShipEvents"/>): the ship, an amount, who it concerns.</summary>
public readonly record struct ShipEvent(ShipEventKind Kind, Ship Ship, float Amount, EntityId Player = default);

// Ships in the zone (GROUND.md §6.7): built at a vehicle workbench, boarded through the hatch, flown by their pilot's
// inputs, parked on their skids (a static body characters walk into), fuelled, locked, looted, hurt by weapons and
// crashes, wrecked when the hull goes, parked in orbit above the ceiling.
public sealed partial class Zone
{
    /// <summary>A ship's hatch and fuel cap are reached within its reach of the eye (ships.json) plus this.</summary>
    private const float ShipReachSlack = 0.5f;

    /// <summary>A pilot may step out (and others open the hatch) only this slow, and this near the ground (or on the sea).</summary>
    public const float ExitSpeed = 2.5f;

    public const float ExitHeight = 3.5f;

    private readonly List<Ship> _ships = new();
    private readonly List<Ship> _shipsGone = new();
    private readonly Dictionary<EntityId, long> _shipNotices = new();
    private ShipCollider? _shipCollider;

    /// <summary>The ships' side of the physics: queries and parked ships' bodies.</summary>
    public ShipCollider ShipCollider => _shipCollider ??= new ShipCollider(Physics);

    /// <summary>Every ship in the zone, in the order they are stepped (by entity ID).</summary>
    public IReadOnlyList<Ship> Ships => _ships;

    /// <summary>What happened to ships this tick (the server sends and clears it).</summary>
    public List<ShipEvent> ShipEvents { get; } = new();

    /// <summary>The ships' world: the zone's frame and ground, the planet's gravity and air.</summary>
    public ShipContext ShipContext => new(Frame, Surface, ShipContext.GravityOf(Spec.SurfaceGravity), (float)Spec.AirDensity,
        (float)Spec.AirScaleHeight, ShipCollider);

    /// <summary>The ship an entity is, or null.</summary>
    public Ship? ShipOf(Entity entity) => Entities.Has<ShipLink>(entity) ? Entities.Get<ShipLink>(entity).Ship : null;

    public Ship? ShipOf(EntityId id) => Entities.TryFind(id, out Entity e) ? ShipOf(e) : null;

    /// <summary>The ship a character sits in, or null.</summary>
    public Ship? SeatOf(Entity character) => Entities.Has<ShipSeat>(character) ? Entities.Get<ShipSeat>(character).Ship : null;

    /// <summary>Adds a ship (built, or restored from storage).</summary>
    public Ship AddShip(Ship ship)
    {
        Entity entity = Entities.Create(ship.Id);
        Entities.Add(entity, new ShipLink { Ship = ship });
        int at = _ships.FindIndex(s => s.Id.Value > ship.Id.Value);
        _ships.Insert(at < 0 ? _ships.Count : at, ship);
        ship.Revision++;
        return ship;
    }

    /// <summary>A new ship standing level on the ground at a point, its nose toward <paramref name="yaw"/>.</summary>
    public Ship SpawnShip(ShipDef def, Vector3 ground, float yaw, EntityId owner, float fuel)
    {
        Vector3 up = Frame.UpAt(ground);
        var ship = new Ship(NextId(), def, owner)
        {
            State = ShipMotor.Standing(def, ground + up * 0.02f, up, yaw),
        };
        ship.State.Fuel = Math.Clamp(fuel, 0, def.FuelCapacity);
        Physics.EnsureGround(ground);
        return AddShip(ship);
    }

    /// <summary>Takes a ship out of the zone: its pilot steps out where the seat is, its body goes.</summary>
    public void RemoveShip(Ship ship)
    {
        if (ship.Piloted && Entities.TryFind(ship.Pilot, out Entity pilot))
        {
            Unseat(pilot, ship, Settle(ship.Origin) + Frame.UpAt(ship.Origin) * 0.05f);
        }
        if (ship.ParkedBody != 0)
        {
            lock (JoltRuntime.Gate)
            {
                ShipCollider.RemoveParked(ship.ParkedBody);
            }
            ship.ParkedBody = 0;
        }
        if (Entities.TryFind(ship.Id, out Entity entity))
        {
            Entities.Destroy(entity);
        }
        _ships.Remove(ship);
    }

    // ---------------------------------------------------------------- each tick

    // After the characters move, inside the movement pass's Jolt lock: every ship steps with its pilot's input (or none),
    // then carries its pilot, parks or wakes, goes to orbit, crashes or burns as its step says.
    private void StepShips()
    {
        if (_ships.Count == 0)
        {
            return;
        }
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        ShipContext context = ShipContext;
        _shipsGone.Clear();
        foreach (Ship ship in _ships)
        {
            if (ship.Destroyed)
            {
                if (++ship.WreckAge >= Ship.WreckTicks)
                {
                    _shipsGone.Add(ship);
                }
                continue;
            }
            Player? pilot = null;
            Entity pilotEntity = Entity.None;
            if (ship.Piloted)
            {
                if (Entities.TryFind(ship.Pilot, out pilotEntity) && PlayerOf(pilotEntity) is { } p && !p.Dead && SeatOf(pilotEntity) == ship)
                {
                    pilot = p;
                }
                else
                {
                    // Gone or dead: the seat is free (a corpse stays on the ground below).
                    if (pilotEntity != Entity.None && Entities.Has<ShipSeat>(pilotEntity))
                    {
                        Entities.Remove<ShipSeat>(pilotEntity);
                        Entities.Get<Character>(pilotEntity).State.Stance = Stance.Standing;
                    }
                    ship.Pilot = EntityId.None;
                    ship.Input = default;
                }
            }
            // The pilot's controls; asleep (logged off) the engines hold the ship where it is (flight assist, no input).
            ShipInput input = pilot != null ? (pilot.Sleeping ? new ShipInput() : ship.Input) : ship.Autopilot ? ship.Input : default;
            input.Engines = pilot != null || ship.Autopilot;
            if (ship.ParkedBody != 0 && input.Engines)
            {
                ShipCollider.RemoveParked(ship.ParkedBody);
                ship.ParkedBody = 0;
            }
            ship.Events = ShipMotor.Step(ref ship.State, input, ship.Def, context, ship.ParkedBody == 0 ? ShipCollider.NoBody : ship.ParkedBody);
            ShipSteps++;
            StepHatch(ship);
            if (ship.Events.CrashDamage > 0)
            {
                ShipEvents.Add(new ShipEvent(ShipEventKind.Crashed, ship, ship.Events.CrashDamage));
                if (pilot != null && ship.Def.CrashPilotShare > 0)
                {
                    Hurt(pilotEntity, pilot, ship.Events.CrashDamage * ship.Def.CrashPilotShare, "Crashed", EntityId.None, "", canWound: false);
                }
            }
            if (ship.Events.ReachedCeiling && (ship.Piloted || ship.Autopilot) && !ship.Destroyed)
            {
                EnterOrbit(ship);
            }
            if (ship.Piloted && Entities.TryFind(ship.Pilot, out pilotEntity) && SeatOf(pilotEntity) == ship)
            {
                CarryPilot(pilotEntity, ship);
            }
            bool park = ship.State.Asleep && !ship.Piloted && !ship.Autopilot && !ship.Destroyed && !ship.InOrbit;
            if (park && ship.ParkedBody == 0)
            {
                ship.ParkedBody = ShipCollider.AddParked(ship.Def, ship.State.Position, ship.State.Rotation);
            }
            else if (!park && ship.ParkedBody != 0)
            {
                ShipCollider.RemoveParked(ship.ParkedBody);
                ship.ParkedBody = 0;
            }
            if (ship.Destroyed)
            {
                Wreck(ship);
            }
        }
        foreach (Ship gone in _shipsGone)
        {
            RemoveShip(gone);
        }
        ShipTime += System.Diagnostics.Stopwatch.GetElapsedTime(started);
    }

    /// <summary>Time spent stepping ships, and ship steps taken (sleeping ones included), for the load test.</summary>
    public TimeSpan ShipTime { get; private set; }

    public long ShipSteps { get; private set; }

    private static void StepHatch(Ship ship)
    {
        if (ship.HatchOpen && ship.HatchProgress < Ship.HatchTicks)
        {
            ship.HatchProgress++;
        }
        else if (!ship.HatchOpen && ship.HatchProgress > 0)
        {
            ship.HatchProgress--;
        }
    }

    // The seated character goes where the seat is.
    private void CarryPilot(Entity pilot, Ship ship)
    {
        ref Character c = ref Entities.Get<Character>(pilot);
        c.State.Position = ship.PointOf(ship.Def.Seat);
        c.State.Velocity = ship.State.Velocity;
        c.State.Stance = Stance.Seated;
        c.State.Flags = MoveFlags.None;
    }

    // ---------------------------------------------------------------- building a ship

    /// <summary>
    /// Rolls a ship out of the station it was built at, onto its bay (or, if the station is gone, in front of its
    /// builder), when the space there is clear. False when it must wait.
    /// </summary>
    private bool DeliverShip(Entity builder, Player p, Recipe recipe, ulong stationId)
    {
        ShipDef def = recipe.Output.Ship!;
        Vector3 at;
        float yaw;
        float maxSlope;
        if (StaticOf(new EntityId(stationId)) is Deployable { Def.Bay: { } bay } station)
        {
            (Vector3 right, Vector3 forward) = Geometry.Axes(station.Up, station.Yaw);
            at = station.Position + right * bay.At.X + forward * bay.At.Z;
            yaw = station.Yaw;
            maxSlope = bay.MaxSlopeDegrees * MathF.PI / 180;
        }
        else
        {
            ref Character c = ref Entities.Get<Character>(builder);
            (Vector3 forward, _) = CharacterMotor.HorizontalBasis(c.Input.Yaw, Frame.UpAt(c.State.Position));
            at = c.State.Position + forward * 6f;
            yaw = c.Input.Yaw;
            maxSlope = 20 * MathF.PI / 180;
        }
        Vector3 ground = Settle(at);
        (_, float slope) = GroundNormal(ground);
        bool steep = slope > maxSlope;
        if (steep || !ShipSpaceClear(def, ground, yaw))
        {
            if (_shipNotices.GetValueOrDefault(p.Id, long.MinValue / 2) + 30 * CharacterMotor.TickRate <= Tick)
            {
                _shipNotices[p.Id] = Tick;
                GameEvents.Add(new ZoneEvent
                {
                    Kind = ZoneEventKind.Notice,
                    Player = p.Id,
                    Text = steep
                        ? $"Your {def.Name} is ready, but the ground in front of the bench is too steep for it."
                        : $"Your {def.Name} is ready: clear the space in front of the vehicle workbench.",
                });
            }
            return false;
        }
        float fuel = 0;
        foreach (ItemAmount a in recipe.Ingredients)
        {
            if (a.Item == def.Fuel)
            {
                fuel += a.Count;
            }
        }
        Ship ship = SpawnShip(def, ground, yaw, p.Id, fuel);
        ShipEvents.Add(new ShipEvent(ShipEventKind.Built, ship, 0, p.Id));
        return true;
    }

    /// <summary>The nearest station of a kind within crafting reach of an eye (its nearest surface, as a workbench's), or null.</summary>
    public Deployable? StationNear(Vector3 eye, DeployableDef def)
    {
        float reach = Content!.Survival.StationReach;
        _near.Clear();
        DeployablesNear(eye, reach + 6f, _near);
        Deployable? best = null;
        float bestDistance = float.MaxValue;
        foreach (Deployable d in _near)
        {
            float distance = Vector3.Distance(d.Nearest(eye), eye);
            if (d.Def == def && distance <= reach && distance < bestDistance)
            {
                best = d;
                bestDistance = distance;
            }
        }
        return best;
    }

    /// <summary>Whether a ship would stand at a point without overlapping anything but the ground, a character or another ship.</summary>
    public bool ShipSpaceClear(ShipDef def, Vector3 ground, float yaw)
    {
        Vector3 up = Frame.UpAt(ground);
        ShipState probe = ShipMotor.Standing(def, ground + up * 0.05f, up, yaw);
        Span<ShipContact> contacts = stackalloc ShipContact[4];
        int found;
        lock (JoltRuntime.Gate)
        {
            Physics.EnsureGround(ground);
            found = ShipCollider.Overlaps(def, probe.Position, probe.Rotation, ShipCollider.NoBody, contacts);
        }
        for (int i = 0; i < found; i++)
        {
            // The ground may poke into the hull a little on bumpy ground; anything else (a box, a tree, a wall) blocks.
            if (!PhysicsWorld.IsGroundBody(contacts[i].Body) || contacts[i].Depth > 0.25f)
            {
                return false;
            }
        }
        float reach = def.BoundingRadius + 0.5f;
        ComponentPool<Character> characters = Entities.Pool<Character>();
        foreach (Character c in characters.Values)
        {
            if (!c.State.IsDead && Vector3.DistanceSquared(c.State.Position, probe.Position) < reach * reach &&
                InsideHull(def, probe, c.State.Position + up * 0.9f, 0.4f))
            {
                return false;
            }
        }
        foreach (Ship other in _ships)
        {
            if (!other.Destroyed && Vector3.Distance(other.State.Position, probe.Position) < other.Def.BoundingRadius + def.BoundingRadius - 0.5f)
            {
                return false;
            }
        }
        return true;
    }

    // Whether a point (with a margin) is inside one of a ship's hull boxes.
    private static bool InsideHull(ShipDef def, in ShipState state, Vector3 point, float margin)
    {
        Vector3 local = Vector3.Transform(point - state.Position, Quaternion.Conjugate(state.Rotation)) + def.CentreOfMass;
        foreach (HullBox box in def.Hull)
        {
            Vector3 d = Vector3.Abs(local - box.Centre) - box.HalfExtents - new Vector3(margin);
            if (d.X <= 0 && d.Y <= 0 && d.Z <= 0)
            {
                return true;
            }
        }
        return false;
    }

    // ---------------------------------------------------------------- the hatch, the seat, the lock, the fuel

    // Within reach of a point of a ship: its hatch (boarding, the hatch, the lock, the cargo) or its fuel cap.
    private bool InShipReach(Entity entity, Ship ship, Vector3 local) =>
        !ship.InOrbit && Vector3.Distance(EyeOf(Entities.Get<Character>(entity).State), ship.PointOf(local)) <= ship.Def.Reach + ShipReachSlack;

    // A ship is still enough to open its hatch, step out or reach in: barely moving, near the ground or on the sea.
    private bool ShipStill(Ship ship)
    {
        if (ship.State.Has(ShipFlags.Landed) || ship.State.Asleep)
        {
            return true;
        }
        return ship.State.Velocity.Length() <= ExitSpeed && (HeightAboveGround(ship.Origin) <= ExitHeight || ship.State.Has(ShipFlags.Wet));
    }

    // The ship a player acts on from beside it (at its hatch) or from its seat; why not otherwise.
    private Outcome ReachShip(Entity entity, EntityId target, out Player p, out Ship ship, out bool inside)
    {
        ship = null!;
        inside = false;
        if (!Acting(entity, out p))
        {
            return OutcomeCode.Dead;
        }
        if (ShipOf(target) is not { Destroyed: false } found)
        {
            return OutcomeCode.NotFound;
        }
        ship = found;
        Ship? seat = SeatOf(entity);
        inside = seat == ship;
        if (inside)
        {
            return OutcomeCode.Done;
        }
        return seat == null && InShipReach(entity, ship, ship.Def.Hatch) ? OutcomeCode.Done : OutcomeCode.OutOfReach;
    }

    /// <summary>Opens or shuts a ship's hatch (the interact key at it, or the pilot's): opened only when still, and a locked one only by its owner.</summary>
    public Outcome ShipHatch(Entity entity, EntityId target)
    {
        Outcome check = ReachShip(entity, target, out Player p, out Ship ship, out bool inside);
        if (check != OutcomeCode.Done)
        {
            return check;
        }
        if (!ship.HatchOpen)
        {
            if (!ShipStill(ship))
            {
                return OutcomeCode.NotLanded;
            }
            if (ship.Locked && p.Id != ship.Owner && !inside)
            {
                return OutcomeCode.Locked;
            }
        }
        ship.HatchOpen = !ship.HatchOpen;
        WakeShip(ship);
        return OutcomeCode.Done;
    }

    /// <summary>Climbs into the seat through the open hatch; the hatch shuts behind the pilot.</summary>
    public Outcome Board(Entity entity, EntityId target)
    {
        Outcome check = ReachShip(entity, target, out Player p, out Ship ship, out bool inside);
        if (check != OutcomeCode.Done)
        {
            return check;
        }
        if (inside)
        {
            return OutcomeCode.NotAllowed;
        }
        if (ship.Locked && p.Id != ship.Owner)
        {
            return OutcomeCode.Locked;
        }
        if (ship.Piloted)
        {
            return OutcomeCode.SeatTaken;
        }
        if (!ship.HatchOpen || ship.HatchProgress < Ship.HatchTicks / 2)
        {
            return OutcomeCode.HatchShut;
        }
        if (!ShipStill(ship))
        {
            return OutcomeCode.NotLanded;
        }
        Seat(entity, p, ship);
        ship.HatchOpen = false;
        return OutcomeCode.Done;
    }

    // Puts a character in a ship's seat (boarding, or a sleeper restored where it was).
    private void Seat(Entity entity, Player p, Ship ship)
    {
        ship.Pilot = p.Id;
        if (Entities.Has<ShipSeat>(entity))
        {
            Entities.Get<ShipSeat>(entity).Ship = ship;
        }
        else
        {
            Entities.Add(entity, new ShipSeat { Ship = ship });
        }
        p.Looting = EntityId.None;
        p.LootRevision++;
        p.StrikeTick = -1;
        p.DrawStartTick = -1;
        ship.Input = default;
        CarryPilot(entity, ship);
        WakeShip(ship);
    }

    /// <summary>Puts a character restored from storage back in the seat of the ship it was flying (the server, at start).</summary>
    public bool Reseat(Entity entity, Ship ship)
    {
        if (PlayerOf(entity) is not { Dead: false } p || ship.Destroyed || (ship.Piloted && ship.Pilot != p.Id))
        {
            return false;
        }
        Seat(entity, p, ship);
        return true;
    }

    /// <summary>
    /// Steps out of the seat to the first safe point beside the ship (the hatch's side first), when it is still; the
    /// hatch opens. From orbit there is no stepping out.
    /// </summary>
    public Outcome ExitShip(Entity entity)
    {
        if (PlayerOf(entity) is not { Dead: false } || SeatOf(entity) is not { } ship)
        {
            return OutcomeCode.Invalid;
        }
        if (ship.InOrbit || !ShipStill(ship))
        {
            return OutcomeCode.NotLanded;
        }
        if (!ExitPoint(ship, out Vector3 at))
        {
            return OutcomeCode.Blocked;
        }
        Unseat(entity, ship, at);
        ship.HatchOpen = true;
        return OutcomeCode.Done;
    }

    // Takes a character out of a ship's seat, standing at a point.
    private void Unseat(Entity entity, Ship ship, Vector3 at)
    {
        if (ship.Pilot == Entities.IdOf(entity))
        {
            ship.Pilot = EntityId.None;
        }
        ship.Input = default;
        Entities.Remove<ShipSeat>(entity);
        ref Character c = ref Entities.Get<Character>(entity);
        Vector3 up = Frame.UpAt(at);
        c.State.Position = at;
        c.State.Velocity = Vector3.Zero;
        c.State.Stance = Stance.Standing;
        c.State.Flags = MoveFlags.OnGround;
        c.Input.Yaw = ShipMotor.YawOf(ship.State.Rotation, up);
        Physics.EnsureGround(at);
        WakeShip(ship);
    }

    // The first exit (ships.json, in order) whose spot on the ground is clear of the hull, deployables, trees and walls.
    private bool ExitPoint(Ship ship, out Vector3 at)
    {
        Vector3 up = Frame.UpAt(ship.State.Position);
        lock (JoltRuntime.Gate)
        {
            foreach (Vector3 exit in ship.Def.Exits)
            {
                Vector3 point = ship.PointOf(exit);
                Vector3 ground = Settle(point);
                if (Vector3.Dot(point - ground, up) > ExitHeight + 1 || Vector3.Dot(ground - point, up) > 1.5f)
                {
                    continue; // a drop or a bank there
                }
                Physics.EnsureGround(ground);
                Vector3 feet = ground + up * 0.05f;
                if (InsideHull(ship.Def, ship.State, feet + up * 0.9f, 0.35f) || !CapsuleClear(feet, up))
                {
                    continue;
                }
                at = feet;
                return true;
            }
        }
        at = default;
        return false;
    }

    // Whether a standing character's capsule is clear of every static body but the ground (rays up and down its sides).
    private bool CapsuleClear(Vector3 feet, Vector3 up)
    {
        (Vector3 forward, Vector3 right) = CharacterMotor.HorizontalBasis(0, up);
        Vector3 top = feet + up * 1.7f;
        Span<Vector3> sides = stackalloc Vector3[] { Vector3.Zero, forward * 0.3f, -forward * 0.3f, right * 0.3f, -right * 0.3f };
        foreach (Vector3 side in sides)
        {
            if (ShipCollider.Ray(feet + up * 0.4f + side, up, 1.3f, ShipCollider.NoBody, out ShipRayHit hit) && !PhysicsWorld.IsGroundBody(hit.Body))
            {
                return false;
            }
            if (ShipCollider.Ray(top + side, -up, 1.3f, ShipCollider.NoBody, out hit) && !PhysicsWorld.IsGroundBody(hit.Body))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Locks or unlocks a ship (its owner, beside it or in the seat).</summary>
    public Outcome LockShip(Entity entity, EntityId target)
    {
        Outcome check = ReachShip(entity, target, out Player p, out Ship ship, out _);
        if (check != OutcomeCode.Done)
        {
            return check;
        }
        if (p.Id != ship.Owner)
        {
            return OutcomeCode.NotOwner;
        }
        ship.Locked = !ship.Locked;
        ship.Revision++;
        return OutcomeCode.Done;
    }

    /// <summary>Fills a ship's tank from the player's lamp oil (main slots, then belt), as much as fits: at the fuel cap, the hatch or in the seat.</summary>
    public Outcome RefuelShip(Entity entity, EntityId target)
    {
        Outcome check = ReachShip(entity, target, out Player p, out Ship ship, out _);
        if (check == OutcomeCode.OutOfReach && ship != null && SeatOf(entity) == null && InShipReach(entity, ship, ship.Def.FuelCap))
        {
            check = OutcomeCode.Done;
        }
        if (check != OutcomeCode.Done)
        {
            return check;
        }
        int room = (int)MathF.Floor(ship!.Def.FuelCapacity - ship.State.Fuel);
        if (room <= 0)
        {
            return OutcomeCode.NoSpace;
        }
        int have = p.Inventory.Count(ship.Def.Fuel);
        if (have <= 0)
        {
            return OutcomeCode.NotEnough;
        }
        int taken = Math.Min(room, have);
        p.Inventory.Take(new[] { new ItemAmount(ship.Def.Fuel, taken) });
        ship.State.Fuel += taken;
        WakeShip(ship);
        GameEvents.Add(new ZoneEvent { Kind = ZoneEventKind.ItemsGained, Player = p.Id, Item = ship.Def.Fuel.Id, Count = -taken, Reason = GainReason.Used });
        return OutcomeCode.Done;
    }

    /// <summary>The ship whose cargo a player has open, or null.</summary>
    public Ship? LootShip(Player p) => p.Looting.IsNone ? null : ShipOf(p.Looting) is { Destroyed: false } ship ? ship : null;

    // Opening a ship's cargo (Open on its ID): at the hatch while it is still, or from the seat; not if locked to them.
    private Outcome OpenShip(Entity entity, Player p, Ship ship)
    {
        bool inside = SeatOf(entity) == ship;
        if (!inside && (SeatOf(entity) != null || !InShipReach(entity, ship, ship.Def.Hatch) || !ShipStill(ship)))
        {
            return OutcomeCode.OutOfReach;
        }
        if (ship.Locked && p.Id != ship.Owner && !inside)
        {
            return OutcomeCode.Locked;
        }
        p.Looting = ship.Id;
        p.LootRevision++;
        return OutcomeCode.Done;
    }

    // Whether a player looting a ship's cargo may go on (CheckLoot, once a second).
    private bool StillAtShip(Entity entity, Ship ship) =>
        SeatOf(entity) == ship || (InShipReach(entity, ship, ship.Def.Hatch) && ShipStill(ship));

    // ---------------------------------------------------------------- orbit (GROUND.md §6.7: the seam for G8)

    /// <summary>
    /// Asked when a ship climbs above its ceiling: a handler that takes it to the space layer (G8, ARCHITECTURE.md §12,
    /// "local space ↔ surface") returns true. Until that exists the zone parks it at its orbit marker above where it left.
    /// </summary>
    public Func<Ship, bool>? OrbitTransfer { get; set; }

    private void EnterOrbit(Ship ship)
    {
        if (OrbitTransfer?.Invoke(ship) == true)
        {
            return;
        }
        ship.OrbitAnchor = ship.State.Position;
        ShipMotor.ParkInOrbit(ref ship.State);
        ShipEvents.Add(new ShipEvent(ShipEventKind.Orbit, ship, 1, ship.Pilot));
    }

    /// <summary>From orbit, back down over the spot it left: level, nose where it was, falling at 40 m/s with flight assist on.</summary>
    public Outcome Descend(Entity entity)
    {
        if (PlayerOf(entity) is not { Dead: false } || SeatOf(entity) is not { InOrbit: true } ship)
        {
            return OutcomeCode.NotAllowed;
        }
        Vector3 up = Frame.UpAt(ship.OrbitAnchor);
        float yaw = ShipMotor.YawOf(ship.State.Rotation, up);
        Vector3 start = ship.OrbitAnchor - up * 250f;
        Vector3 startUp = Frame.UpAt(start);
        ship.State.Rotation = ShipMotor.Orientation(startUp, yaw);
        ship.State.Position = start;
        ship.State.Velocity = -startUp * 40f;
        ship.State.AngularVelocity = Vector3.Zero;
        ship.State.Flags &= ~ShipFlags.InOrbit;
        ship.State.RestTicks = 0;
        ShipEvents.Add(new ShipEvent(ShipEventKind.Orbit, ship, 0, ship.Pilot));
        return OutcomeCode.Done;
    }

    // ---------------------------------------------------------------- damage and wrecks

    /// <summary>
    /// Ships' hulls (GROUND.md §6.4.2, §6.7): found by their hull boxes along a ray and by their nearest points in a
    /// blast's reach (seen from a point just off the hull toward the blast: the hull never hides its own side); a ship in
    /// orbit or wrecked is out of reach. Its defence: the hull's share by how the blow came (ships.json). Its consequences:
    /// the hull's health and last attacker; at 0 the ship is wrecked (the pilot dies in it, a wreck to loot) and reported
    /// destroyed. The pilot inside is the cabin's to protect.
    /// </summary>
    private sealed class ShipReceiver(Zone zone) : IDamageReceiver
    {
        public TargetFamily[] Families { get; } = { TargetFamily.Ship };

        /// <summary>An arrow on a hull is lost.</summary>
        public bool Keeps => true;

        public void Raycast(in RayQuery ray, ref TraceHit best)
        {
            if (zone._ships.Count > 0)
            {
                float t = zone.TraceShips(ray.Origin, ray.Direction, ray.Length, ray.Thickness, out Ship? ship);
                if (ship != null)
                {
                    ray.Consider(ref best, t, ship.Ref);
                }
            }
        }

        public void InReach(in BlastQuery blast, List<BlastTarget> into)
        {
            foreach (Ship ship in zone._ships)
            {
                if (ship.Destroyed || ship.InOrbit)
                {
                    continue;
                }
                Vector3 point = ShipClosestPoint(ship, blast.At);
                bool stuck = blast.IsStuckTo(ship.Ref);
                float distance = stuck ? 0 : Vector3.Distance(point, blast.At);
                if (distance > blast.Radius)
                {
                    continue;
                }
                Vector3 off = blast.At - point;
                Vector3 seen = off.LengthSquared() > 1e-6f ? point + Vector3.Normalize(off) * 0.05f : point;
                into.Add(new BlastTarget
                {
                    Hit = new TraceHit { Target = ship.Ref, Point = point }, Thing = ship, Sight = seen, Sight2 = seen, Distance = distance,
                    Stuck = stuck,
                });
            }
        }

        public DamageOutcome Receive(in DamageRequest r)
        {
            if (zone.ShipOf(new EntityId(r.Hit.Target.Id)) is not { } ship || ship.Destroyed || ship.InOrbit)
            {
                return default;
            }
            float lost = zone.HurtShip(ship, r.Amounts.Hull, r.Delivery, r.By);
            return new DamageOutcome(true, lost, lost, ship.Destroyed, false, ship.Id);
        }
    }

    /// <summary>The point of a ship's hull (its boxes) nearest to a point.</summary>
    public static Vector3 ShipClosestPoint(Ship ship, Vector3 at)
    {
        ShipDef def = ship.Def;
        Quaternion q = ship.State.Rotation;
        Vector3 local = Vector3.Transform(at - ship.State.Position, Quaternion.Conjugate(q)) + def.CentreOfMass;
        Vector3 best = local;
        float nearest = float.MaxValue;
        foreach (HullBox box in def.Hull)
        {
            Vector3 p = Vector3.Clamp(local, box.Centre - box.HalfExtents, box.Centre + box.HalfExtents);
            float d = Vector3.DistanceSquared(p, local);
            if (d < nearest)
            {
                nearest = d;
                best = p;
            }
        }
        return ship.PointOf(best);
    }

    /// <summary>
    /// Damage to a ship's hull from a weapon of a kind (tests): a request through the ships' receiver, with no hit marker.
    /// The hull takes its share for the kind; at 0 it is wrecked. Returns the hull lost.
    /// </summary>
    public float DamageShip(Ship ship, float damage, ShipDamage kind, Player? attacker) => Damage(new DamageRequest
    {
        By = attacker,
        Attacker = attacker?.Id ?? EntityId.None,
        AttackerName = attacker?.Name ?? "",
        Delivery = kind switch
        {
            ShipDamage.Melee => DamageDelivery.Melee,
            ShipDamage.Arrow => DamageDelivery.Arrow,
            ShipDamage.Bullet => DamageDelivery.Bullet,
            _ => DamageDelivery.Blast,
        },
        Channel = kind switch
        {
            ShipDamage.Melee => DamageType.Melee,
            ShipDamage.Explosive => DamageType.Explosive,
            _ => DamageType.Bullet,
        },
        Amounts = new DamageAmounts { Hull = damage },
        Direct = true,
        Hit = new TraceHit { Target = ship.Ref, Point = ship.Origin },
        Markers = TargetFamilies.None,
    }).Taken;

    // The ships' consequences: the hull's share for how the blow came; at 0 the ship is wrecked. Returns the hull lost.
    private float HurtShip(Ship ship, float damage, DamageDelivery delivery, Player? attacker)
    {
        if (ship.Destroyed || ship.InOrbit || damage <= 0)
        {
            return 0;
        }
        float lost = MathF.Min(ship.State.Health, damage * ship.Def.Share(delivery));
        if (lost <= 0)
        {
            return 0;
        }
        ship.State.Health -= lost;
        if (attacker != null)
        {
            ship.LastAttacker = attacker.Id;
            ship.LastAttackerName = attacker.Name;
        }
        WakeShip(ship);
        if (ship.State.Health <= 0)
        {
            ship.State.Health = 0;
            ship.State.Flags |= ShipFlags.Destroyed;
            Wreck(ship);
        }
        return lost;
    }

    /// <summary>Wakes a resting ship (something touched it: a hit, a hatch, a pilot).</summary>
    public static void WakeShip(Ship ship)
    {
        if (ship.State.RestTicks >= ShipMotor.SleepTicks)
        {
            ship.State.RestTicks = 0;
        }
    }

    // The hull is gone: the pilot dies in it; its cargo, half its fuel and some of its metal go into a wreck anyone may loot.
    private void Wreck(Ship ship)
    {
        if (ship.WreckAge >= 0)
        {
            return;
        }
        ship.WreckAge = 0;
        ship.State.Flags |= ShipFlags.Destroyed;
        ship.State.Velocity = Vector3.Zero;
        ship.State.AngularVelocity = Vector3.Zero;
        // Destroyed by the blow being applied, or (none) by its crash.
        Destroyed(ship.Ref, ship.Def.Key, 0, ship.Origin, ship.State.Rotation, _blow.Active ? null : DestroyCause.Crash);
        if (ship.ParkedBody != 0)
        {
            lock (JoltRuntime.Gate)
            {
                ShipCollider.RemoveParked(ship.ParkedBody);
            }
            ship.ParkedBody = 0;
        }
        Vector3 at = Settle(ship.Origin);
        Vector3 up = Frame.UpAt(at);
        if (ship.Piloted && Entities.TryFind(ship.Pilot, out Entity pilot) && PlayerOf(pilot) is { } p)
        {
            Unseat(pilot, ship, at + up * 0.05f);
            Die(pilot, p, "Went down with the ship", ship.LastAttacker, ship.LastAttackerName);
        }
        var stacks = new List<ItemStack>();
        for (int i = 0; i < ship.Cargo.Size; i++)
        {
            if (!ship.Cargo[i].IsEmpty)
            {
                stacks.Add(ship.Cargo[i]);
            }
        }
        int fuel = (int)MathF.Floor(ship.State.Fuel * 0.5f);
        if (fuel > 0)
        {
            stacks.Add(ItemStack.Of(ship.Def.Fuel, Math.Min(fuel, ship.Def.Fuel.StackSize)));
        }
        foreach (ItemAmount a in ship.Def.Wreck)
        {
            stacks.Add(ItemStack.Of(a.Item, Math.Min(a.Count, a.Item.StackSize)));
        }
        if (stacks.Count > 0 && Content != null)
        {
            var wreck = new Corpse(NextId(), $"{ship.Def.Name} wreck", ship.Id, stacks.Count, at, up, ShipMotor.YawOf(ship.State.Rotation, up),
                Tick + (long)(Content!.Survival.CorpseSeconds * CharacterMotor.TickRate));
            for (int i = 0; i < stacks.Count; i++)
            {
                wreck.Loot.Set(i, stacks[i]);
            }
            AddStatic(wreck);
        }
        ShipEvents.Add(new ShipEvent(ShipEventKind.Destroyed, ship, 0, ship.LastAttacker));
    }

    /// <summary>The ship a ray (thickened) meets first: the distance along it (−1 for none) and the ship.</summary>
    private float TraceShips(Vector3 origin, Vector3 direction, float length, float thickness, out Ship? hit)
    {
        hit = null;
        float best = -1;
        foreach (Ship ship in _ships)
        {
            if (ship.Destroyed || ship.InOrbit ||
                Geometry.RaySphere(origin, direction, length, ship.State.Position, ship.Def.BoundingRadius + thickness) < 0)
            {
                continue;
            }
            Vector3 x = Vector3.Transform(Vector3.UnitX, ship.State.Rotation);
            Vector3 y = Vector3.Transform(Vector3.UnitY, ship.State.Rotation);
            Vector3 z = Vector3.Transform(Vector3.UnitZ, ship.State.Rotation);
            foreach (HullBox box in ship.Def.Hull)
            {
                float t = Geometry.RayBox(origin, direction, length, ship.PointOf(box.Centre), box.HalfExtents + new Vector3(thickness), x, y, z);
                if (t >= 0 && (best < 0 || t < best))
                {
                    best = t;
                    hit = ship;
                }
            }
        }
        return best;
    }
}
