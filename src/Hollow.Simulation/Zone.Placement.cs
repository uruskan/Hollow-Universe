using System;
using System.Collections.Generic;
using System.Numerics;
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
public sealed partial class Zone : IPlacementSite
{
    private readonly Dictionary<(ulong Owner, DeployableDef Def), int> _owned = new();

    /// <summary>The placement world on this zone (GROUND_SYSTEMS.md §6): the server's half of the ghost's code.</summary>
    public PlacementWorld Placement { get; private set; } = null!;

    private void InitPlacement(GameContent content)
    {
        Placement = new PlacementWorld(content, Frame, Surface, Physics, Building, new ZoneSurroundings(this), Nature, Natural, ShipCollider);
    }

    /// <summary>
    /// Places what a slot pays for (GROUND_SYSTEMS.md §6): the placeable must be what the slot's item places (or the slot
    /// holds the tool it needs, the building plan for a piece); its solver works the claimed candidate out again from the
    /// claim (only a pose within the rule's tolerance of its own is taken), every check passes, its kind makes it, and then
    /// the item or the materials are taken. Done, or why not.
    /// </summary>
    public Outcome Place(Entity entity, in PlaceRequest request)
    {
        if (!Acting(entity, out Player p))
        {
            return OutcomeCode.Dead;
        }
        if (Content!.Placement[request.Placeable] is not { SlotItem: { } needs } placeable || request.From is not (ContainerRef.Main or ContainerRef.Belt))
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
        if (Placer.Settle(Placement, ctx, request.Claim, out Outcome refused) is not { } candidate)
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
        if (PlayerOf(entity) is not { } p)
        {
            return null;
        }
        ref Character c = ref Entities.Get<Character>(entity);
        var ctx = new PlacementContext(p.Id.Value, EyeOf(c.State), placeable, new ItemWallet(Content!, p.Inventory.Carried));
        return Placer.Plan(Placement, ctx, new PlacementAim(direction, c.Input.Yaw, turns));
    }

    /// <summary>Places from a slot what a candidate (a ghost) says: the intent a client sends for it.</summary>
    public Outcome Place(Entity entity, ContainerRef from, int slot, PlacementCandidate candidate, ushort expected = 0) =>
        Place(entity, new PlaceRequest(from, slot, expected, candidate.Placeable.Index, candidate.Claim));

    /// <summary>A deployable put where a checked candidate says, its owner the player; standing on a piece, it goes with it.</summary>
    public Outcome PutDeployable(ulong player, DeployableDef def, PlacementCandidate c)
    {
        var placed = new Deployable(NextId(), def, c.Position, c.Up, c.Yaw, new EntityId(player))
        {
            Support = c.Support.Family == TargetFamily.Piece ? c.Support.Whole : TargetRef.None,
        };
        if (def.IsRespawnPoint)
        {
            placed.ReadyAtUnixMs = UnixMilliseconds + (long)(def.RespawnSeconds * 1000);
        }
        AddStatic(placed);
        return Outcome.Done;
    }

    /// <summary>A ship from its item, set down where the checked candidate says, the player's, with the tank ships.json gives it.</summary>
    public Outcome PutShip(ulong player, ShipDef def, PlacementCandidate c)
    {
        SpawnShip(def, c.Position, c.Yaw, new EntityId(player), def.PlacedFuel);
        return Outcome.Done;
    }

    /// <summary>
    /// After a restart, once the building is back: every thing that stood on something still holds to it, and one whose
    /// support didn't come back meets its consequence (a deployable breaks), as when a support goes.
    /// </summary>
    public void SettleSupports()
    {
        var gone = new List<TargetRef>();
        foreach (TargetRef support in _dependents.Keys)
        {
            if (!Exists(support))
            {
                gone.Add(support);
            }
        }
        foreach (TargetRef support in gone)
        {
            SupportGone(support);
        }
    }

    // How many of a deployable each player owns (a limit per player: RustMVP's sleeping bags), kept as things come and go.
    private void CountOwned(Deployable d, int change)
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
    private sealed class ZoneSurroundings(Zone zone) : IPlacementSurroundings
    {
        private readonly List<Deployable> _near = new();

        public void ThingsNear(Vector3 point, float radius, List<PlacedThing> into)
        {
            _near.Clear();
            zone.DeployablesNear(point, radius, _near);
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
                CharacterState s = characters.Values[i].State;
                if (!s.IsDead && Vector3.DistanceSquared(s.Position, point) <= radius * radius)
                {
                    into.Add(new Capsule(s.Position, zone.Frame.UpAt(s.Position), zone.Tuning.Radius, zone.Tuning.StandingHeight));
                }
            }
        }

        public void ShipsNear(Vector3 point, float radius, List<PlacedShip> into)
        {
            foreach (Ship ship in zone._ships)
            {
                if (!ship.Destroyed && Vector3.DistanceSquared(ship.State.Position, point) <= radius * radius)
                {
                    into.Add(new PlacedShip(ship.Def, ship.State.Position, ship.State.Rotation));
                }
            }
        }

        public int ThingsOn(in TargetRef support)
        {
            int count = 0;
            foreach (TargetRef dependent in zone.DependentsOf(support))
            {
                count += dependent.Family == TargetFamily.Thing ? 1 : 0;
            }
            return count;
        }

        public int ThingsOwned(ulong player, DeployableDef def) => zone._owned.GetValueOrDefault((player, def));
    }
}
