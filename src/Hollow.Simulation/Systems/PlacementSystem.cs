using System.Collections.Generic;
using System.Numerics;
using System;
using Hollow.Simulation.Building;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Placement;
using Hollow.Simulation.Players;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

namespace Hollow.Simulation;

/// <summary>
/// What a player asks to place (PlaceIntent, GROUND_SYSTEMS.md §6): the slot that pays for it (the item used up, or the
/// building plan carried), the item the client saw there, the placeable's number, and the claim (what holds it, the
/// socket, where it goes and its turn, its variant).
/// </summary>
public readonly record struct PlaceRequest(ContainerRef From, int Slot, ushort Expected, int Placeable, PlacementClaim Claim);

// Placing anything (GROUND_SYSTEMS.md §6): the zone's side of the placement world (what it has that a client is only
// told: every thing, character and ship, what stands on what, whose things are whose), the one entry point for every
// placement intent, and what the kinds make. The pipeline itself (solvers, checks, kinds) is Placement/, shared with the
// client's ghost.
public sealed class PlacementSystem : ZoneSystem, IPlacementSite
{
    private ActionSystem? _actionsSystem;
    private ActionSystem Actions => _actionsSystem ??= Zone.Require<ActionSystem>();
    private BuildingSystem? _buildingSystem;
    private BuildingSystem Building => _buildingSystem ??= Zone.Require<BuildingSystem>();
    private PlayerSystem? _playersSystem;
    private PlayerSystem Players => _playersSystem ??= Zone.Require<PlayerSystem>();
    private ShipSystem? _shipsSystem;
    private ShipSystem Ships => _shipsSystem ??= Zone.Require<ShipSystem>();
    private SupportSystem? _supportsSystem;
    private SupportSystem Supports => _supportsSystem ??= Zone.Require<SupportSystem>();
    private TargetSystem? _targetsSystem;
    private TargetSystem Targets => _targetsSystem ??= Zone.Require<TargetSystem>();
    private ThingSystem? _thingsSystem;
    private ThingSystem Things => _thingsSystem ??= Zone.Require<ThingSystem>();

    internal readonly Dictionary<(ulong Owner, DeployableDef Def), int> _owned = new();

    /// <summary>The placement world on this zone (GROUND_SYSTEMS.md §6): the server's half of the ghost's code.</summary>
    public PlacementWorld World { get; private set; } = null!;

    private void InitPlacement(GameContent content)
    {
        World = new PlacementWorld(content, Frame, Surface, Physics, Building.World, new ZoneSurroundings(Zone), Nature, Natural, Ships.ShipCollider);
    }

    /// <summary>
    /// Places what a slot pays for (GROUND_SYSTEMS.md §6): the placeable must be what the slot's item places (or the slot
    /// holds the tool it needs, the building plan for a piece); its solver works the claimed candidate out again from the
    /// claim (only a pose within the rule's tolerance of its own is taken), every check passes, its kind makes it, and then
    /// the item or the materials are taken. Done, or why not.
    /// </summary>
    public Outcome Place(Entity entity, in PlaceRequest request)
    {
        if (!Actions.Acting(entity, out Player p))
        {
            return OutcomeCode.Dead;
        }
        if (Content.Placement[request.Placeable] is not { SlotItem: { } needs } placeable || request.From is not (ContainerRef.Main or ContainerRef.Belt))
        {
            return OutcomeCode.Invalid;
        }
        Container source = p.Inventory[request.From];
        if (!source.InRange(request.Slot))
        {
            return OutcomeCode.Invalid;
        }
        ItemStack s = source[request.Slot];
        if (s.IsEmpty || (request.Expected != 0 && s.Item != request.Expected))
        {
            return OutcomeCode.Stale;
        }
        if (s.Item != needs.Id)
        {
            return placeable.Item == null ? OutcomeCode.NoTool.With(needs.Id) : OutcomeCode.NotUsable;
        }
        var wallet = new ItemWallet(Content, p.Inventory.Carried);
        var ctx = new PlacementContext(p.Id.Value, EyeOf(Entities.Get<Character>(entity).State), placeable, wallet);
        if (Placer.Settle(World, ctx, request.Claim, out Outcome refused) is not { } candidate)
        {
            return refused;
        }
        Outcome put = placeable.Kind.Put(this, p.Id.Value, candidate);
        if (!put.Ok)
        {
            return put;
        }
        if (placeable.Item != null)
        {
            InventoryRules.Remove(source, request.Slot, 1, 0, out _);
        }
        foreach (PlacementCost cost in placeable.Costs)
        {
            wallet.Take(cost.Material, cost.Amount);
        }
        return Outcome.Done;
    }

    /// <summary>
    /// What a player would see placing a placeable along a direction from their eye: the ghost, on the zone's own world
    /// (tests; a client's comes from its replicated world, GameClient.Plan). Null when the aim finds nothing.
    /// </summary>
    public PlacementCandidate? Aim(Entity entity, Placeable placeable, Vector3 direction, int turns = 0)
    {
        if (Players.PlayerOf(entity) is not { } p)
        {
            return null;
        }
        ref Character c = ref Entities.Get<Character>(entity);
        var ctx = new PlacementContext(p.Id.Value, EyeOf(c.State), placeable, new ItemWallet(Content, p.Inventory.Carried));
        return Placer.Plan(World, ctx, new PlacementAim(direction, Facing.YawOf(ViewOf(c), UpOf(c.State)), turns));
    }

    /// <summary>Places from a slot what a candidate (a ghost) says: the intent a client sends for it.</summary>
    public Outcome Place(Entity entity, ContainerRef from, int slot, PlacementCandidate candidate, ushort expected = 0) =>
        Place(entity, new PlaceRequest(from, slot, expected, candidate.Placeable.Index, candidate.Claim));

    /// <summary>
    /// A deployable put where a checked candidate says, its owner the player; standing on a piece, it goes with it; aboard
    /// a ship (IRON_WAKE.md §8), it is linked to the ship with its pose in the ship's frame, rides it, and goes with it.
    /// </summary>
    public Outcome PutDeployable(ulong player, DeployableDef def, PlacementCandidate c)
    {
        var placed = new Deployable(NextId(), def, c.Position, c.Up, c.Yaw, new EntityId(player))
        {
            Support = c.Support.Family is TargetFamily.Piece or TargetFamily.Ship ? c.Support.Whole : TargetRef.None,
            Link = c.Aboard is { } aboard ? new SupportLink(c.Support.Whole, aboard.Position, aboard.Rotation) : SupportLink.None,
        };
        if (def.IsRespawnPoint)
        {
            placed.ReadyAtUnixMs = UnixMilliseconds + (long)(def.RespawnSeconds * 1000);
        }
        Things.AddStatic(placed);
        return Outcome.Done;
    }

    /// <summary>A ship from its item, set down where the checked candidate says, the player's, with the tank ships.json gives it.</summary>
    public Outcome PutShip(ulong player, ShipDef def, PlacementCandidate c)
    {
        Ships.SpawnShip(def, c.Position, c.Yaw, new EntityId(player), def.PlacedFuel);
        return Outcome.Done;
    }

    /// <summary>
    /// After a restart, once the building is back (and again once the ships are, <paramref name="ships"/>): every thing
    /// that stood on something still holds to it, and one whose support didn't come back meets its consequence (a
    /// deployable breaks and spills), as when a support goes. Before the ships are back, what rides one waits for it.
    /// </summary>
    public void SettleSupports(bool ships = true)
    {
        var gone = new List<TargetRef>();
        foreach (TargetRef support in Supports._dependents.Keys)
        {
            if ((ships || support.Family != TargetFamily.Ship) && !Targets.Exists(support))
            {
                gone.Add(support);
            }
        }
        foreach (TargetRef support in gone)
        {
            Supports.SupportGone(support);
        }
    }

    // How many of a deployable each player owns (a limit per player: RustMVP's sleeping bags), kept as things come and go.
    internal void CountOwned(Deployable d, int change)
    {
        if (d.Owner.IsNone || d is WorldProp)
        {
            return;
        }
        (ulong, DeployableDef) key = (d.Owner.Value, d.Def);
        int count = _owned.GetValueOrDefault(key) + change;
        if (count > 0)
        {
            _owned[key] = count;
        }
        else
        {
            _owned.Remove(key);
        }
    }

    /// <summary>The zone's surroundings for placement: every thing, character and ship it has, the support links, the owners.</summary>
    internal sealed class ZoneSurroundings(Zone zone) : IPlacementSurroundings
    {
        private readonly List<Deployable> _near = new();

        public void ThingsNear(Vector3 point, float radius, List<PlacedThing> into)
        {
            _near.Clear();
            zone.Things().DeployablesNear(point, radius, _near);
            foreach (Deployable d in _near)
            {
                into.Add(new PlacedThing(d.Ref, d.Def, d.Position, d.Up, d.Yaw));
            }
        }

        public void CharactersNear(Vector3 point, float radius, List<Capsule> into)
        {
            ComponentPool<Character> characters = zone.Entities.Pool<Character>();
            for (int i = 0; i < characters.Count; i++)
            {
                // Where each stands in the world (aboard a ship, where it carries them: IRON_WAKE.md §4.2).
                CharacterState s = characters.Values[i].State;
                Vector3 feet = zone.WorldOf(s);
                if (!s.IsDead && Vector3.DistanceSquared(feet, point) <= radius * radius)
                {
                    into.Add(new Capsule(feet, zone.UpOf(s), zone.Tuning.Radius, zone.Tuning.StandingHeight));
                }
            }
        }

        public void ShipsNear(Vector3 point, float radius, List<PlacedShip> into)
        {
            foreach (Ship ship in zone.Ships()._ships)
            {
                if (!ship.Destroyed && Vector3.DistanceSquared(ship.State.Position, point) <= radius * radius)
                {
                    into.Add(new PlacedShip(ship.Def, ship.State.Position, ship.State.Rotation));
                }
            }
        }

        // The deployables standing on a support (a piece, a ship: a bag or a body lying aboard doesn't count).
        public int ThingsOn(in TargetRef support)
        {
            int count = 0;
            foreach (TargetRef dependent in zone.Supports().DependentsOf(support))
            {
                count += dependent.Family == TargetFamily.Thing && zone.Things().StaticOf(new EntityId(dependent.Id)) is Deployable ? 1 : 0;
            }
            return count;
        }

        public int ThingsOwned(ulong player, DeployableDef def) => zone.Placement()._owned.GetValueOrDefault((player, def));

        // The ships people walk in, where they are now (as the characters aboard them are carried).
        public void SpacesNear(Vector3 point, float radius, List<PlacedSpace> into)
        {
            foreach (IShipFrame frame in zone.Frames.Ships)
            {
                if (zone.Ships().ShipOf(new EntityId(frame.Key)) is { Destroyed: false, InOrbit: false } ship && frame.TryPose(zone.Tick, out ShipPose pose) &&
                    Vector3.Distance(pose.Position, point) <= radius + frame.Interior.Reach)
                {
                    into.Add(new PlacedSpace(frame, pose, ship.Ref, ship.Def));
                }
            }
        }

        public bool Crew(in TargetRef ship, ulong player) => zone.Ships().ShipOf(new EntityId(ship.Id))?.IsAuthorised(player) == true;
    }

    public override int InitOrder => 30;

    protected internal override void Init() => InitPlacement(Content);

    private ExplosiveSystem? _explosivesSystem;
    private ExplosiveSystem Explosives => _explosivesSystem ??= Zone.Require<ExplosiveSystem>();

    // IPlacementSite: what the placement rules ask of the zone (GROUND_SYSTEMS.md §6), answered by the systems that own it.
    BuildingWorld? IPlacementSite.Building => Building.World;

    double IPlacementSite.BuildingNow => Building.BuildingNow;

    Outcome IPlacementSite.PutCharge(ulong player, ThrowableStats stats, PlacementCandidate c) => Explosives.PutCharge(player, stats, c);
}

/// <summary>The accessor on the zone (GROUND_SYSTEMS.md §12.2, K4).</summary>
public static class PlacementSystemAccess
{
    public static PlacementSystem Placement(this Zone zone) => zone.Require<PlacementSystem>();
}
