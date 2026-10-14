using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Net.Protocol;
using Hollow.Net.Replication;
using Hollow.Persistence;
using Hollow.Simulation;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Ships;
using LiteNetLib;

namespace Hollow.Server;

/// <summary>
/// Ships on the server (GROUND.md §6.7): the pilots' controls into their ships, ship intents, ships in every nearby
/// player's snapshots (and the pilot's own ship exactly, for its prediction), and what happens to ships told to whom it
/// concerns.
/// </summary>
public sealed class ShipModule : ServerModule
{
    /// <summary>Ships within this many metres (horizontally) of a player are in their snapshots: big, fast and seen far off.</summary>
    public const float ShipRelevanceRadius = 2000f;

    private readonly RelevanceGrid _shipGrid = new(ShipRelevanceRadius, cellSize: 256f, hysteresis: 100f);
    private readonly Dictionary<EntityId, uint> _shipNetIds = new();
    private readonly Dictionary<uint, Ship> _shipsByNet = new();
    private readonly HashSet<uint> _shipsPresent = new();
    private readonly List<uint> _shipsGone = new();
    private readonly List<int> _shipIndices = new();
    private readonly HashSet<uint> _shipsNow = new();
    private readonly List<uint> _shipsLeaving = new();

    /// <summary>Ship intents accepted and refused, for the load test.</summary>
    public long ShipIntentsAccepted { get; private set; }

    public long ShipIntentsRefused { get; private set; }

    /// <summary>The ships (net IDs) this player hears about (G6).</summary>
    private sealed class ShipView
    {
        public readonly HashSet<uint> Relevant = new();
    }

    protected internal override void Init(MessageHandlers<PlayerSession> handlers)
    {
        handlers.On<ShipIntent>((PlayerSession session, in ShipIntent message) =>
        {
            ShipIntent m = message;
            Entity me = session.Entity;
            var target = new EntityId(m.Ship);
            Intent(session, m.Seq, () =>
            {
                Outcome result = m.Action switch
                {
                    ShipAction.Hatch => Zone.Ships().ShipHatch(me, target),
                    ShipAction.Exit => Zone.Ships().ExitShip(me),
                    ShipAction.Lock => Zone.Ships().LockShip(me, target),
                    ShipAction.Descend => Zone.Ships().Descend(me),
                    _ => OutcomeCode.Invalid,
                };
                if (result == OutcomeCode.Done)
                {
                    ShipIntentsAccepted++;
                }
                else
                {
                    ShipIntentsRefused++;
                }
                return result;
            });
        });
    }

    /// <summary>Every new character gets a Tin Finch beside them, full and theirs (<see cref="ServerOptions.ShipOnJoin"/>, the G6 load test's pilots).</summary>
    protected internal override void Joined(PlayerSession session, bool newCharacter)
    {
        if (newCharacter && (Options.ShipOnJoin || Options.ShipOnJoinFor?.Invoke(session.Name) == true))
        {
            GiveShip(session);
        }
    }

    // A pilot's controls for this tick go to their ship (none while on foot: hands off the stick).
    protected internal override void Input(PlayerSession session, in InputCommand input)
    {
        if (Zone.Ships().SeatOf(session.Entity) is { } ship && ship.Pilot == session.EntityId)
        {
            ship.Input = ShipConvert.ToShipInput(input);
        }
    }

    // ---------------------------------------------------------------- replication

    protected internal override void Prepare() => PrepareShips();

    // Once per snapshot: every ship gets a net ID (kept while it lives) and goes into the ships' grid; those gone are noted.
    private void PrepareShips()
    {
        _shipGrid.Clear();
        _shipsPresent.Clear();
        foreach (Ship ship in Zone.Ships().All)
        {
            if (!_shipNetIds.TryGetValue(ship.Id, out uint netId))
            {
                netId = NextNetId();
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

    /// <summary>The ships for this player's snapshot: their spawns and despawns first (reliable), then the pilot's own ship in the header and the ships offered by nearness.</summary>
    protected internal override void Snapshot(PlayerSession session, Vector3 centre, ref SnapshotHeader header, List<ShipCandidate> ships)
    {
        ReplicateShips(session, centre, ships);
        header.OwnShip = OwnShip(session)!;
    }

    // The ships near a player: spawned and despawned (reliable), offered to their snapshot by nearness.
    private void ReplicateShips(PlayerSession session, Vector3 centre, List<ShipCandidate> ships)
    {
        HashSet<uint> relevant = session.State<ShipView>().Relevant;
        foreach (uint netId in _shipsGone)
        {
            if (relevant.Remove(netId))
            {
                session.Snapshots.Forget(netId);
                Despawned(session, netId);
            }
        }
        _shipGrid.Query(centre, relevant, _shipIndices);
        _shipsNow.Clear();
        foreach (int index in _shipIndices)
        {
            (uint netId, Vector3 position) = _shipGrid[index];
            _shipsNow.Add(netId);
            Ship ship = _shipsByNet[netId];
            if (relevant.Add(netId))
            {
                Writer.Reset();
                new Spawn { NetId = netId, EntityId = ship.Id.Value, Kind = EntityKind.Ship, Name = ship.Def.Key }.WriteMessage(Writer);
                session.Peer.Send(Writer.Data, DeliveryMethod.ReliableOrdered);
            }
            float distance = Vector3.Distance(position, centre);
            bool moving = !ship.State.Asleep;
            ships.Add(new ShipCandidate
            {
                NetId = netId,
                State = ShipConvert.ToNet(ship, Zone.Targets().FlagsFor(ship.Ref, session.Viewer)),
                Weight = (moving ? 3f : 0.5f) + 4f * MathF.Max(0f, 1f - distance / ShipRelevanceRadius) + (ship.Pilot == session.EntityId ? 10f : 0f),
            });
        }
        _shipsLeaving.Clear();
        foreach (uint netId in relevant)
        {
            if (!_shipsNow.Contains(netId))
            {
                _shipsLeaving.Add(netId);
            }
        }
        foreach (uint netId in _shipsLeaving)
        {
            relevant.Remove(netId);
            session.Snapshots.Forget(netId);
            Despawned(session, netId);
        }
    }

    /// <summary>The ship this player flies, exactly, for the snapshot's header (none on foot).</summary>
    private OwnShipState[]? OwnShip(PlayerSession session)
    {
        if (Zone.Ships().SeatOf(session.Entity) is not { } ship || ship.Pilot != session.EntityId || !_shipNetIds.TryGetValue(ship.Id, out uint netId))
        {
            return null;
        }
        return new[] { ShipConvert.ToOwn(ship.State, netId, ship.Def.Id) };
    }

    // ---------------------------------------------------------------- storage

    private readonly HashSet<EntityId> _savedShips = new();

    // The body's ships as they were saved: parked, flying (they fall or hover on), in orbit; their pilots, loaded as
    // sleepers, go back into their seats.
    protected internal override void Restore(ZoneRecord stored, HashSet<EntityId> saved)
    {
        int loaded = 0;
        foreach (ShipRecord record in stored.Ships)
        {
            _savedShips.Add(record.Id);
            try
            {
                if (ShipCodec.Decode(record.Id, record.Data, Content, Zone.Frame) is not { } restored || Zone.Entities.TryFind(record.Id, out _))
                {
                    continue;
                }
                Ship ship = Zone.Ships().AddShip(restored.Ship);
                Zone.Physics.EnsureGround(ship.State.Position);
                loaded++;
                if (!restored.Pilot.IsNone && Zone.Entities.TryFind(restored.Pilot, out Entity pilot) && Zone.Players().PlayerOf(pilot) != null)
                {
                    Zone.Ships().Reseat(pilot, ship);
                }
            }
            catch (Exception e) when (e is System.IO.EndOfStreamException or System.IO.InvalidDataException or ArgumentException)
            {
                Log($"ship {record.Id} doesn't load ({e.Message}); left out");
            }
        }
        if (loaded > 0)
        {
            Log($"loaded {loaded} ships");
        }
    }

    // Every ship now (they move), and those gone since the last save.
    protected internal override void Save(ZoneSave save, HashSet<EntityId> current)
    {
        var ships = new HashSet<EntityId>();
        foreach (Ship ship in Zone.Ships().All)
        {
            if (ship.Destroyed)
            {
                continue;
            }
            save.Ships.Add(new ShipRecord(ship.Id, ShipCodec.Encode(ship, Zone.Frame)));
            ships.Add(ship.Id);
        }
        foreach (EntityId id in _savedShips)
        {
            if (!ships.Contains(id))
            {
                save.RemovedShips.Add(id);
            }
        }
        _savedShips.Clear();
        _savedShips.UnionWith(ships);
    }

    // ---------------------------------------------------------------- events

    // What happened to ships this tick: the builder hears theirs rolled out; crashes and wrecks go in the log.
    protected internal override void AfterStep()
    {
        foreach (ShipEvent e in Zone.Ships().ShipEvents)
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
        Zone.Ships().ShipEvents.Clear();
    }

    /// <summary>Ships built, destroyed and crashes, for <c>status</c> and the load test.</summary>
    public long ShipsBuilt { get; private set; }

    public long ShipsDestroyed { get; private set; }

    public long ShipCrashes { get; private set; }

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
        // Beside the player, its hatch (its right side) toward them: rings of spots round them, nearest first.
        (Vector3 north, Vector3 east) = CharacterMotor.HorizontalBasis(0, up);
        foreach (float distance in new[] { 6f, 9f, 12f, 16f, 22f })
        {
            for (int i = 0; i < 12; i++)
            {
                float angle = i * MathF.PI / 6 + distance;
                Vector3 at = feet + (north * MathF.Cos(angle) + east * MathF.Sin(angle)) * distance;
                Vector3 ground = Zone.Settle(at);
                // The ship's right is (cos yaw, −sin yaw) east and north... in the characters' yaw: point it back at the player.
                float yaw = MathF.Atan2(-MathF.Cos(angle), -MathF.Sin(angle));
                if (Zone.Ships().ShipSpaceClear(def, ground, yaw))
                {
                    Ship ship = Zone.Ships().SpawnShip(def, ground, yaw, session.EntityId, def.FuelCapacity);
                    ship.HatchOpen = true;
                    ship.HatchProgress = Ship.HatchTicks;
                    return ship;
                }
            }
        }
        return null;
    }
}

/// <summary>The accessor on the server (GROUND_SYSTEMS.md §12.5): ships' intents, replication and storage.</summary>
public static class ShipModuleAccess
{
    public static ShipModule Ships(this GameServer server) => server.Require<ShipModule>();
}
