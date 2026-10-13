using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Net.Protocol;
using Hollow.Net.Replication;
using Hollow.Simulation;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Ships;
using LiteNetLib;

namespace Hollow.Server;

// Ships on the server (GROUND.md §6.7): the pilots' controls into their ships, ship intents, ships in every nearby
// player's snapshots (and the pilot's own ship exactly, for its prediction), and what happens to ships told to whom it
// concerns.
public sealed partial class GameServer
{
    /// <summary>Ships within this many metres (horizontally) of a player are in their snapshots: big, fast and seen far off.</summary>
    public const float ShipRelevanceRadius = 2000f;

    private readonly RelevanceGrid _shipGrid = new(ShipRelevanceRadius, cellSize: 256f, hysteresis: 100f);
    private readonly Dictionary<EntityId, uint> _shipNetIds = new();
    private readonly Dictionary<uint, Ship> _shipsByNet = new();
    private readonly HashSet<uint> _shipsPresent = new();
    private readonly List<uint> _shipsGone = new();
    private readonly List<ShipCandidate> _shipCandidates = new();
    private readonly List<int> _shipIndices = new();
    private readonly HashSet<uint> _shipsNow = new();
    private readonly List<uint> _shipsLeaving = new();

    /// <summary>Ship intents accepted and refused, for the load test.</summary>
    public long ShipIntentsAccepted { get; private set; }

    public long ShipIntentsRefused { get; private set; }

    // A pilot's controls for this tick go to their ship (none while on foot: hands off the stick).
    private void ApplyShipInput(PlayerSession session, in InputCommand input)
    {
        if (Zone.SeatOf(session.Entity) is { } ship && ship.Pilot == session.EntityId)
        {
            ship.Input = ShipConvert.ToShipInput(input);
        }
    }

    // ---------------------------------------------------------------- intents

    private bool ReceiveShipIntent(PlayerSession session, uint type)
    {
        if (type != ShipIntent.TypeId)
        {
            return false;
        }
        ShipIntent m = ShipIntent.Read(_reader);
        if (_reader.Failed)
        {
            return false;
        }
        Entity me = session.Entity;
        var target = new EntityId(m.Ship);
        return Intent(session, m.Seq, () =>
        {
            ActionResult result = m.Action switch
            {
                ShipAction.Hatch => Zone.ShipHatch(me, target),
                ShipAction.Board => Zone.Board(me, target),
                ShipAction.Exit => Zone.ExitShip(me),
                ShipAction.Lock => Zone.LockShip(me, target),
                ShipAction.Refuel => Zone.RefuelShip(me, target),
                ShipAction.Descend => Zone.Descend(me),
                _ => ActionResult.Invalid,
            };
            if (result == ActionResult.Accepted)
            {
                ShipIntentsAccepted++;
            }
            else
            {
                ShipIntentsRefused++;
            }
            return result;
        });
    }

    // ---------------------------------------------------------------- replication

    // Once per snapshot: every ship gets a net ID (kept while it lives) and goes into the ships' grid; those gone are noted.
    private void PrepareShips()
    {
        _shipGrid.Clear();
        _shipsPresent.Clear();
        foreach (Ship ship in Zone.Ships)
        {
            if (!_shipNetIds.TryGetValue(ship.Id, out uint netId))
            {
                netId = _nextNetId++;
                _shipNetIds.Add(ship.Id, netId);
            }
            _shipsByNet[netId] = ship;
            _shipsPresent.Add(netId);
            _shipGrid.Add(netId, ship.State.Position);
        }
        _shipsGone.Clear();
        foreach ((uint netId, Ship ship) in _shipsByNet)
        {
            if (!_shipsPresent.Contains(netId))
            {
                _shipsGone.Add(netId);
                _shipNetIds.Remove(ship.Id);
            }
        }
        foreach (uint netId in _shipsGone)
        {
            _shipsByNet.Remove(netId);
        }
    }

    // The ships near a player: spawned and despawned (reliable), offered to their snapshot by nearness.
    private void ReplicateShips(PlayerSession session, Vector3 centre)
    {
        _shipCandidates.Clear();
        foreach (uint netId in _shipsGone)
        {
            if (session.ShipsRelevant.Remove(netId))
            {
                session.Snapshots.Forget(netId);
                Despawned(session, netId);
            }
        }
        _shipGrid.Query(centre, session.ShipsRelevant, _shipIndices);
        _shipsNow.Clear();
        foreach (int index in _shipIndices)
        {
            (uint netId, Vector3 position) = _shipGrid[index];
            _shipsNow.Add(netId);
            Ship ship = _shipsByNet[netId];
            if (session.ShipsRelevant.Add(netId))
            {
                _writer.Reset();
                new Spawn { NetId = netId, EntityId = ship.Id.Value, Kind = EntityKind.Ship, Name = ship.Def.Key }.WriteMessage(_writer);
                session.Peer.Send(_writer.Data, DeliveryMethod.ReliableOrdered);
            }
            float distance = Vector3.Distance(position, centre);
            bool moving = !ship.State.Asleep;
            _shipCandidates.Add(new ShipCandidate
            {
                NetId = netId,
                State = ShipConvert.ToNet(ship, ship.Owner == session.EntityId),
                Weight = (moving ? 3f : 0.5f) + 4f * MathF.Max(0f, 1f - distance / ShipRelevanceRadius) + (ship.Pilot == session.EntityId ? 10f : 0f),
            });
        }
        _shipsLeaving.Clear();
        foreach (uint netId in session.ShipsRelevant)
        {
            if (!_shipsNow.Contains(netId))
            {
                _shipsLeaving.Add(netId);
            }
        }
        foreach (uint netId in _shipsLeaving)
        {
            session.ShipsRelevant.Remove(netId);
            session.Snapshots.Forget(netId);
            Despawned(session, netId);
        }
    }

    /// <summary>The ship this player flies, exactly, for the snapshot's header (none on foot).</summary>
    private OwnShipState[]? OwnShip(PlayerSession session)
    {
        if (Zone.SeatOf(session.Entity) is not { } ship || ship.Pilot != session.EntityId || !_shipNetIds.TryGetValue(ship.Id, out uint netId))
        {
            return null;
        }
        return new[] { ShipConvert.ToOwn(ship.State, netId, ship.Def.Id) };
    }

    // ---------------------------------------------------------------- events

    // What happened to ships this tick: the builder hears theirs rolled out; crashes and wrecks go in the log.
    private void RouteShipEvents()
    {
        foreach (ShipEvent e in Zone.ShipEvents)
        {
            switch (e.Kind)
            {
                case ShipEventKind.Built:
                    ShipsBuilt++;
                    Log($"{NameOf(e.Player)} built a {e.Ship.Def.Name} ({e.Ship.Id})");
                    Notify(e.Player, $"Your {e.Ship.Def.Name} rolled out of the vehicle workbench.");
                    break;
                case ShipEventKind.Destroyed:
                    ShipsDestroyed++;
                    Log($"a {e.Ship.Def.Name} ({e.Ship.Id}) was destroyed" + (e.Player.IsNone ? "" : $" by {NameOf(e.Player)}"));
                    break;
                case ShipEventKind.Crashed:
                    ShipCrashes++;
                    break;
                case ShipEventKind.Orbit when e.Amount > 0:
                    Log($"{NameOf(e.Player)} reached orbit in a {e.Ship.Def.Name}");
                    break;
            }
        }
        Zone.ShipEvents.Clear();
    }

    /// <summary>Ships built, destroyed and crashes, for <c>status</c> and the load test.</summary>
    public long ShipsBuilt { get; private set; }

    public long ShipsDestroyed { get; private set; }

    public long ShipCrashes { get; private set; }

    /// <summary>A server line in one player's chat.</summary>
    private void Notify(EntityId player, string text)
    {
        if (_sessionOf.GetValueOrDefault(player) is not { } to)
        {
            return;
        }
        _writer.Reset();
        new ChatLine { Kind = ChatKind.Server, From = "", Text = text }.WriteMessage(_writer);
        Send(to);
    }

    /// <summary>A Tin Finch beside a player, full, theirs, its hatch open (the admin's <c>ship</c>, and every new player's with <see cref="ServerOptions.ShipOnJoin"/>).</summary>
    public Ship? GiveShip(PlayerSession session)
    {
        if (!session.Playing || Content.Ships.Count == 0)
        {
            return null;
        }
        ShipDef def = Content.Ships[0];
        Vector3 feet = Zone.Entities.Get<Character>(session.Entity).State.Position;
        Vector3 up = Zone.Frame.UpAt(feet);
        // Beside the player, its hatch toward them: try a few spots round them.
        for (int i = 0; i < 12; i++)
        {
            float angle = i * MathF.PI / 6;
            (Vector3 north, Vector3 east) = CharacterMotor.HorizontalBasis(0, up);
            Vector3 at = feet + (north * MathF.Cos(angle) + east * MathF.Sin(angle)) * 5f;
            Vector3 ground = Zone.Settle(at);
            float yaw = MathF.Atan2(MathF.Sin(angle), MathF.Cos(angle)) + MathF.PI / 2;
            if (Zone.ShipSpaceClear(def, ground, yaw))
            {
                Ship ship = Zone.SpawnShip(def, ground, yaw, session.EntityId, def.FuelCapacity);
                ship.HatchOpen = true;
                ship.HatchProgress = Ship.HatchTicks;
                return ship;
            }
        }
        return null;
    }
}
