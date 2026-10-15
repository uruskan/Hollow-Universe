using System.Collections.Generic;
using System.Numerics;
using System;
using Hollow.Simulation.Building;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Physics;
using Hollow.Simulation.Placement;
using Hollow.Simulation.Players;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

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

// Ships in the zone (GROUND.md §6.7, IRON_WAKE.md §5): built at a vehicle workbench, flown by whoever sits at their helm,
// parked on their skids (a static body characters walk into), fuelled, looted, hurt by weapons and crashes, wrecked when
// the hull goes, parked in orbit above the ceiling. Their seats (ShipSystem.Seats.cs), doors (ShipSystem.Doors.cs), crew
// and parts' health (ShipSystem.Parts.cs) and the spaces people walk in (ShipSystem.Spaces.cs) are this system's too.
public sealed partial class ShipSystem : ZoneSystem
{
    private DamageSystem? _damageSystem;
    private DamageSystem Damage => _damageSystem ??= Zone.Require<DamageSystem>();
    private PlacementSystem? _placementSystem;
    private PlacementSystem Placement => _placementSystem ??= Zone.Require<PlacementSystem>();
    private PlayerSystem? _playersSystem;
    private PlayerSystem Players => _playersSystem ??= Zone.Require<PlayerSystem>();
    private TargetSystem? _targetsSystem;
    private TargetSystem Targets => _targetsSystem ??= Zone.Require<TargetSystem>();
    private ThingSystem? _thingsSystem;
    private ThingSystem Things => _thingsSystem ??= Zone.Require<ThingSystem>();

    /// <summary>A pilot may step out (and others open the hatch) only this slow, and this near the ground (or on the sea).</summary>
    public const float ExitSpeed = 2.5f;

    public const float ExitHeight = 3.5f;

    internal readonly List<Ship> _ships = new();
    private readonly List<Ship> _shipsGone = new();
    private readonly Dictionary<EntityId, long> _shipNotices = new();
    private ShipCollider? _shipCollider;

    /// <summary>The ships' side of the physics: queries and parked ships' bodies.</summary>
    public ShipCollider ShipCollider => _shipCollider ??= new ShipCollider(Physics);

    /// <summary>Every ship in the zone, in the order they are stepped (by entity ID).</summary>
    public IReadOnlyList<Ship> All => _ships;

    /// <summary>What happened to ships this tick (the server sends and clears it).</summary>
    public List<ShipEvent> ShipEvents { get; } = new();

    /// <summary>The ships' world: the zone's frame and ground, the planet's gravity and air.</summary>
    public ShipContext ShipContext => new(Frame, Surface, ShipContext.GravityOf(Spec.SurfaceGravity), (float)Spec.AirDensity,
        (float)Spec.AirScaleHeight, ShipCollider);

    /// <summary>The ship an entity is, or null.</summary>
    public Ship? ShipOf(Entity entity) => Entities.Has<ShipLink>(entity) ? Entities.Get<ShipLink>(entity).Ship : null;

    public Ship? ShipOf(EntityId id) => Entities.TryFind(id, out Entity e) ? ShipOf(e) : null;

    /// <summary>
    /// Adds a ship (built, or restored from storage), its doors' motion counted from this tick (a stored half-shut ramp goes
    /// on shutting from where it was). One people walk in (an <c>interior</c> in ships.json, IRON_WAKE.md §4.3) gets its
    /// space, posed from its parts' states (its doors' as their progress has them), and its frame among the zone's.
    /// </summary>
    public Ship AddShip(Ship ship)
    {
        Entity entity = Entities.Create(ship.Id);
        Entities.Add(entity, new ShipLink { Ship = ship });
        int at = _ships.FindIndex(s => s.Id.Value > ship.Id.Value);
        _ships.Insert(at < 0 ? _ships.Count : at, ship);
        ship.Revision++;
        for (int i = 0; i < ship.Doors.Length; i++)
        {
            // A door's record counts from the zone's tick it arrives at (a stored one's ticks were another run's).
            ship.Doors[i].Since = Tick;
        }
        if (ship.Def.Interior is { } interior)
        {
            DoorParts(ship, Tick, ship.Parts);
            ship.Space = new ShipSpace(interior, ship.Parts, Tuning);
            ship.Frame = new ShipFrame(ship, ship.Space, Movement.Gravity);
            Zone.Frames.Add(ship.Frame);
            // What rides it (restored before it, IRON_WAKE.md §8) goes into its space and where it carries it.
            Zone.Get<ThingSystem>()?.Boarded(ship);
        }
        return ship;
    }

    /// <summary>
    /// The deck of a ship people walk in under a point of the zone's frame (IRON_WAKE.md §4.10): the point inside its
    /// boarding volume, and a floor of its space (its pieces or a thing aboard) facing up from <paramref name="above"/> over
    /// it to <paramref name="below"/> under it. A wreck or a ship in orbit has none. Where on the deck, in the zone's frame.
    /// </summary>
    public bool DeckUnder(Vector3 point, float above, float below, out Vector3 at, out Ship? ship)
    {
        at = point;
        ship = null;
        foreach (Ship s in _ships)
        {
            if (s.Frame == null || s.Space == null || s.Destroyed || s.InOrbit)
            {
                continue;
            }
            ShipPose pose = ShipFrame.PoseOf(s.State, s.Def.CentreOfMass);
            ShipInterior interior = s.Space.Interior;
            if (Vector3.DistanceSquared(pose.Position, point) > interior.Reach * interior.Reach)
            {
                continue;
            }
            Vector3 local = pose.ToLocal(point);
            if (interior.OutsideBoarding(local, s.Parts) > interior.Margin)
            {
                continue;
            }
            bool met;
            SpaceHit hit;
            lock (JoltRuntime.Gate)
            {
                met = s.Space.Ray(local + Vector3.UnitY * above, -Vector3.UnitY, above + below, out hit, things: true);
            }
            if (met && hit.Normal.Y > 0.5f)
            {
                at = pose.ToWorld(hit.Point);
                ship = s;
                return true;
            }
        }
        return false;
    }

    /// <summary>A new ship standing level on the ground at a point, its nose toward <paramref name="yaw"/>.</summary>
    public Ship SpawnShip(ShipDef def, Vector3 ground, float yaw, EntityId owner, float fuel)
    {
        Vector3 up = Frame.UpAt(ground);
        ShipState state = ShipMotor.Standing(def, ground + up * 0.02f, up, yaw);
        // Level where its centre of mass is, the point the motor flies: on a small world the pivot's up is a hair off
        // (the Tin Finch's 0.28 m is 0.0064° on a 2.41 km world), which flight assist would hold as a slow sink.
        state.Rotation = Quaternion.Normalize(Facing.Turn(up, Frame.UpAt(state.Position)) * state.Rotation);
        var ship = new Ship(NextId(), def, owner)
        {
            State = state,
        };
        ship.State.Fuel = Math.Clamp(fuel, 0, def.FuelCapacity);
        Physics.EnsureGround(ground);
        return AddShip(ship);
    }

    /// <summary>
    /// Takes a ship out of the zone: whoever sits in it stands up where their seat is, everyone else aboard is put ashore
    /// where the ship carried them (with its velocity there), its body and its space go.
    /// </summary>
    public void RemoveShip(Ship ship)
    {
        for (int seat = 0; seat < ship.Occupants.Length; seat++)
        {
            if (Entities.TryFind(ship.Occupants[seat], out Entity occupant))
            {
                Release(occupant, SeatRelease.Moved);
                if (ship.Frame == null)
                {
                    Entities.Get<Character>(occupant).State.Position = Settle(ship.Origin) + Frame.UpAt(ship.Origin) * 0.05f;
                }
            }
        }
        Ashore(ship);
        if (ship.ParkedBody != 0)
        {
            lock (JoltRuntime.Gate)
            {
                ShipCollider.RemoveParked(ship.ParkedBody);
            }
            ship.ParkedBody = 0;
        }
        if (ship.Frame != null)
        {
            Zone.Frames.Remove(ship.Frame.Key);
            ship.Frame = null;
        }
        ship.Space?.Dispose();
        ship.Space = null;
        History.ForgetShip(ship.Id.Value);
        if (Entities.TryFind(ship.Id, out Entity entity))
        {
            Entities.Destroy(entity);
        }
        _ships.Remove(ship);
        // What rode it goes with it (IRON_WAKE.md §8; a wreck's went when it was wrecked): built things break and spill,
        // loose ones fall.
        Zone.Get<SupportSystem>()?.SupportGone(ship.Ref);
    }

    // ---------------------------------------------------------------- each tick

    // After the characters move, inside the movement pass's Jolt lock: every ship's seats are checked (an occupant gone,
    // dead or no longer seated frees theirs), every ship steps with its helm's input (or none), its doors move, it carries
    // whoever sits in it, parks or wakes, goes to orbit, crashes or burns as its step says.
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
            CheckSeats(ship);
            Player? pilot = null;
            Entity pilotEntity = Entity.None;
            if (ship.Piloted && Entities.TryFind(ship.Pilot, out pilotEntity))
            {
                pilot = PlayerOf(pilotEntity);
            }
            // The helm's controls; asleep (logged off) the engines hold the ship where it is (flight assist, no input).
            ShipInput input = pilot != null ? (pilot.Sleeping ? new ShipInput() : ship.Input) : ship.Autopilot ? ship.Input : default;
            input.Engines = pilot != null || ship.Autopilot;
            if (ship.ParkedBody != 0 && input.Engines)
            {
                ShipCollider.RemoveParked(ship.ParkedBody);
                ship.ParkedBody = 0;
            }
            ship.Events = ShipMotor.Step(ref ship.State, input, ship.Def, context, ship.ParkedBody == 0 ? ShipCollider.NoBody : ship.ParkedBody);
            ShipSteps++;
            StepDoors(ship);
            if (ship.Events.CrashDamage > 0)
            {
                ShipEvents.Add(new ShipEvent(ShipEventKind.Crashed, ship, ship.Events.CrashDamage));
                if (pilot != null && ship.Def.CrashPilotShare > 0)
                {
                    Players.Hurt(pilotEntity, pilot, ship.Events.CrashDamage * ship.Def.CrashPilotShare, "Crashed", EntityId.None, "", canWound: false);
                }
            }
            if (ship.Events.ReachedCeiling && (ship.Piloted || ship.Autopilot) && !ship.Destroyed)
            {
                EnterOrbit(ship);
            }
            CarrySeated(ship);
            bool park = ship.State.Asleep && !ship.Piloted && !ship.Autopilot && !ship.Destroyed && !ship.InOrbit;
            if (park && ship.ParkedBody == 0)
            {
                Park(ship);
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
        // What rides the ships goes where they carried it, before anything this tick looks at it (IRON_WAKE.md §4.10).
        Zone.Get<ThingSystem>()?.FollowSupports();
        ShipTime += System.Diagnostics.Stopwatch.GetElapsedTime(started);
    }

    /// <summary>Time spent stepping ships, and ship steps taken (sleeping ones included), for the load test.</summary>
    public TimeSpan ShipTime { get; private set; }

    public long ShipSteps { get; private set; }

    // ---------------------------------------------------------------- building a ship

    /// <summary>
    /// Rolls a ship out of the station it was built at, onto its bay (or, if the station is gone, in front of its
    /// builder), when the space there is clear. False when it must wait.
    /// </summary>
    internal bool DeliverShip(Entity builder, Player p, Recipe recipe, ulong stationId)
    {
        ShipDef def = recipe.Output.Ship!;
        Vector3 at;
        float yaw;
        float maxSlope;
        if (Things.StaticOf(new EntityId(stationId)) is Deployable { Def.Bay: { } bay } station)
        {
            (Vector3 right, Vector3 forward) = Geometry.Axes(station.Up, station.Yaw);
            at = station.Position + right * bay.At.X + forward * bay.At.Z;
            yaw = station.Yaw;
            maxSlope = bay.MaxSlopeDegrees * MathF.PI / 180;
        }
        else
        {
            ref Character c = ref Entities.Get<Character>(builder);
            Vector3 standing = Frame.UpAt(WorldOf(c.State));
            (Vector3 forward, _) = Facing.Basis(ViewOf(c), standing);
            at = WorldOf(c.State) + forward * 6f;
            yaw = Facing.YawOf(ViewOf(c), standing);
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
                Events.Add(new Notice(Player: p.Id, Text: steep                         ? $"Your {def.Name} is ready, but the ground in front of the bench is too steep for it."                         : $"Your {def.Name} is ready: clear the space in front of the vehicle workbench."));
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
        Things._near.Clear();
        Things.DeployablesNear(eye, reach + 6f, Things._near);
        Deployable? best = null;
        float bestDistance = float.MaxValue;
        foreach (Deployable d in Things._near)
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

    private readonly List<Capsule> _hullCharacters = new();
    private readonly List<PlacedShip> _hullShips = new();

    /// <summary>
    /// Whether a ship would stand at a point without overlapping anything but the ground, a character or another ship: the
    /// placement system's hull footprint (GROUND_SYSTEMS.md §6), as when one is placed from its item.
    /// </summary>
    public bool ShipSpaceClear(ShipDef def, Vector3 ground, float yaw) =>
        HullFootprint.Clear(Placement.World, def, ground, yaw, _hullCharacters, _hullShips).Ok;

    // ---------------------------------------------------------------- at rest, the fuel, the cargo

    /// <summary>
    /// A ship is still enough to open its hatch, step out or reach in: landed, asleep, or barely moving near the ground or
    /// on the sea. The one rule: clients are told it (<c>ShipNetFlags.Still</c>), so they offer what it allows.
    /// </summary>
    public bool ShipStill(Ship ship)
    {
        if (ship.State.Has(ShipFlags.Landed) || ship.State.Asleep)
        {
            return true;
        }
        return ship.State.Velocity.Length() <= ExitSpeed && (HeightAboveGround(ship.Origin) <= ExitHeight || ship.State.Has(ShipFlags.Wet));
    }

    /// <summary>
    /// A ship slow and low enough for a door that opens "slow" (IRON_WAKE.md §5.3, the ramp): at rest, or slower than
    /// <see cref="ShipDoorDef.SlowSpeed"/> within <see cref="ShipDoorDef.SlowHeight"/> of the ground. Clients are told it
    /// (<c>ShipNetFlags.Slow</c>).
    /// </summary>
    public bool ShipSlow(Ship ship) =>
        ShipStill(ship) || (ship.State.Velocity.Length() < ShipDoorDef.SlowSpeed && HeightAboveGround(ship.Origin) < ShipDoorDef.SlowHeight);

    /// <summary>Fills a ship's tank from the player's lamp oil (main slots, then belt), as much as fits.</summary>
    internal Outcome Refuel(Player p, Ship ship)
    {
        int room = (int)MathF.Floor(ship.Def.FuelCapacity - ship.State.Fuel);
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
        Events.Add(new ItemsGained(Player: p.Id, Item: ship.Def.Fuel.Id, Count: -taken, Reason: GainReason.Used));
        return OutcomeCode.Done;
    }

    /// <summary>The ship whose cargo a player has open, or null.</summary>
    public Ship? LootShip(Player p) => p.Looting.IsNone ? null : ShipOf(p.Looting) is { Destroyed: false } ship ? ship : null;

    // Whether a player looting a ship's cargo may go on (CheckLoot, once a second): from one of its seats, or beside it while
    // it is at rest, its cargo's part within the reach that keeps a container open (GROUND_SYSTEMS.md §3.4).
    internal bool StillAtShip(Entity entity, Ship ship)
    {
        if (SeatOf(entity) == ship)
        {
            return true;
        }
        int cargo = ship.Def.CargoPart;
        return cargo > 0 && Content != null && !ship.InOrbit && ShipStill(ship) &&
            Targets.Reach(EyeOf(Entities.Get<Character>(entity).State), new TargetRef(TargetFamily.Ship, ship.Id.Value, (byte)cargo), Content.Survival.KeepOpenReach,
                sight: false).Ok;
    }

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

    /// <summary>From orbit, back down over the spot it left: level, nose where it was, falling at 40 m/s with flight assist on (the helm's key).</summary>
    public Outcome Descend(Entity entity)
    {
        if (PlayerOf(entity) is not { Dead: false } || SeatOf(entity) is not { InOrbit: true } ship || ship.Pilot != Entities.IdOf(entity))
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

    // ---------------------------------------------------------------- the hull and wrecks

    /// <summary>The point of a ship's hull nearest to a point: its pieces for a ship people walk in, else its boxes.</summary>
    public static Vector3 ShipClosestPoint(Ship ship, Vector3 at)
    {
        if (ship.Frame != null)
        {
            ShipPose pose = ShipFrame.PoseOf(ship.State, ship.Def.CentreOfMass);
            if (ShipQueries.Nearest(ship.Frame, pose, at, Vector3.Distance(at, pose.Position) + 1, out Vector3 point, out _, out _))
            {
                return point;
            }
        }
        return ShipShapes.NearestOnHull(ship.Def, ship.State.Position, ship.State.Rotation, at);
    }

    /// <summary>
    /// Damage to a ship's hull from a weapon of a kind (tests): a request through the ships' receiver, with no hit marker.
    /// The hull takes its share for the kind; at 0 it is wrecked. Returns the hull lost.
    /// </summary>
    public float DamageShip(Ship ship, float damage, ShipDamage kind, Player? attacker) => Damage.Damage(new DamageRequest
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
    internal float HurtShip(Ship ship, float damage, DamageDelivery delivery, Player? attacker)
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

    /// <summary>Wakes a resting ship (something touched it: a hit, a refuel, a pilot).</summary>
    public static void WakeShip(Ship ship)
    {
        if (ship.State.RestTicks >= ShipMotor.SleepTicks)
        {
            ship.State.RestTicks = 0;
        }
    }

    // The hull is gone: whoever sits in it dies in it (strapped in); everyone else aboard is put ashore; its cargo, half its
    // fuel and some of its metal go into a wreck anyone may loot.
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
        // Its parked body goes first: what was aboard spills to the ground under it, not onto a deck that is gone.
        if (ship.ParkedBody != 0)
        {
            lock (JoltRuntime.Gate)
            {
                ShipCollider.RemoveParked(ship.ParkedBody);
            }
            ship.ParkedBody = 0;
        }
        // Destroyed by the blow being applied, or (none) by its crash; what was built aboard breaks and spills, what lay
        // aboard falls (IRON_WAKE.md §8, the support rule).
        Damage.Destroyed(ship.Ref, ship.Def.Key, 0, ship.Origin, ship.State.Rotation, Damage._blow.Active ? null : DestroyCause.Crash);
        Vector3 at = Settle(ship.Origin);
        Vector3 up = Frame.UpAt(at);
        for (int seat = 0; seat < ship.Occupants.Length; seat++)
        {
            if (Entities.TryFind(ship.Occupants[seat], out Entity occupant) && PlayerOf(occupant) is { } p)
            {
                // Out of the seat into the zone's frame where it was, then down with the ship.
                Release(occupant, SeatRelease.Moved);
                if (ship.Frame == null)
                {
                    Entities.Get<Character>(occupant).State.Position = at + up * 0.05f;
                }
                Players.Die(occupant, p, "Went down with the ship", ship.LastAttacker, ship.LastAttackerName);
            }
        }
        Ashore(ship);
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
            LifetimeRule body = Content!.Survival.BodyLifetime;
            var wreck = new Corpse(NextId(), $"{ship.Def.Name} wreck", ship.Id, stacks.Count, at, up, ShipMotor.YawOf(ship.State.Rotation, up), body,
                Tick + body.Ticks);
            for (int i = 0; i < stacks.Count; i++)
            {
                wreck.Loot.Set(i, stacks[i]);
            }
            Things.AddStatic(wreck);
        }
        ShipEvents.Add(new ShipEvent(ShipEventKind.Destroyed, ship, 0, ship.LastAttacker));
    }

    /// <summary>
    /// The ship people only sit in that a ray (thickened) meets first, by its hull boxes where the attacker saw it (the
    /// pose history at <paramref name="viewTick"/>): the distance along it (−1 for none) and the ship. One people walk in
    /// is met by its space (<see cref="ShipQueries"/>).
    /// </summary>
    internal float TraceShips(Vector3 origin, Vector3 direction, float length, float thickness, out Ship? hit, double viewTick = double.MaxValue)
    {
        hit = null;
        float best = -1;
        foreach (Ship ship in _ships)
        {
            if (ship.Frame != null || ship.Destroyed || ship.InOrbit)
            {
                continue;
            }
            ShipPose pose = viewTick < Tick && History.SampleShip(ship.Id.Value, viewTick, out ShipPose then) ? then : ShipFrame.PoseOf(ship.State, ship.Def.CentreOfMass);
            if (Geometry.RaySphere(origin, direction, length, pose.Position, ship.Def.BoundingRadius + thickness) < 0)
            {
                continue;
            }
            Vector3 x = Vector3.Transform(Vector3.UnitX, pose.Rotation);
            Vector3 y = Vector3.Transform(Vector3.UnitY, pose.Rotation);
            Vector3 z = Vector3.Transform(Vector3.UnitZ, pose.Rotation);
            foreach (HullBox box in ship.Def.Hull)
            {
                float t = Geometry.RayBox(origin, direction, length, pose.ToWorld(box.Centre), box.HalfExtents + new Vector3(thickness), x, y, z);
                if (t >= 0 && (best < 0 || t < best))
                {
                    best = t;
                    hit = ship;
                }
            }
        }
        return best;
    }

    public override bool InsidePhysicsGate => true;

    protected internal override void Init() => InitSeats();

    protected internal override void Step()
    {
        StepShips();
    }

    public override void Dispose()
    {
        foreach (Ship ship in _ships)
        {
            ship.Space?.Dispose();
            ship.Space = null;
        }
        _shipCollider?.Dispose();
    }

    /// <summary>The survival state of a character, or null: not a player, or G1's movement-only zone, where ships fly without players.</summary>
    private Player? PlayerOf(Entity entity) => Zone.Get<PlayerSystem>()?.PlayerOf(entity);
}

/// <summary>The accessor on the zone (GROUND_SYSTEMS.md §12.2, K4).</summary>
public static class ShipSystemAccess
{
    public static ShipSystem Ships(this Zone zone) => zone.Require<ShipSystem>();
}
