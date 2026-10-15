using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Core;
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

    /// <summary>The ships (net IDs) this player hears about (G6), and the revisions of each one's details they were told (IW1, IW2).</summary>
    private sealed class ShipView
    {
        public readonly HashSet<uint> Relevant = new();
        public readonly Dictionary<uint, (uint Parts, uint Details)> DetailsSent = new();
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
                    ShipAction.Door => Zone.Ships().SeatedDoor(me, target, m.Arg),
                    ShipAction.Leave => Zone.Ships().LeaveSeat(me),
                    ShipAction.Lock => Zone.Ships().SeatedLock(me, target),
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

    /// <summary>
    /// A player leaving while seated (IRON_WAKE.md §5.2): logging off keeps the seat, asleep in it; a lost connection stands
    /// them up at its first clear exit, where they sleep (none clear: they sleep in the seat).
    /// </summary>
    protected internal override void Leaving(PlayerSession session, bool loggedOff)
    {
        if (!loggedOff)
        {
            Zone.Ships().Release(session.Entity, SeatRelease.Disconnected);
        }
    }

    /// <summary>Every new character gets a Tin Finch beside them, full and theirs (<see cref="ServerOptions.ShipOnJoin"/>, the G6 load test's pilots).</summary>
    protected internal override void Joined(PlayerSession session, bool newCharacter)
    {
        if (newCharacter && (Options.ShipOnJoin || Options.ShipOnJoinFor?.Invoke(session.Name) == true))
        {
            GiveShip(session);
        }
    }

    // A pilot's controls for this tick go to their ship (none while on foot: hands off the stick). The view's frame comes
    // as a ship's net ID: the zone knows its ships by their IDs (IRON_WAKE.md §4.11); a frame it doesn't know is taken
    // for the one the character is in.
    protected internal override void Input(PlayerSession session, in InputCommand input)
    {
        if (Zone.Ships().SeatOf(session.Entity) is { } ship && ship.Pilot == session.EntityId)
        {
            ship.Input = ShipConvert.ToShipInput(input);
        }
        ref Character c = ref Zone.Entities.Get<Character>(session.Entity);
        c.Input.Frame = input.Frame == 0 ? 0 : _shipsByNet.TryGetValue(input.Frame, out Ship? frame) ? frame.Id.Value : c.State.Frame;
    }

    /// <summary>A frame's key in the zone (a ship's ID) from the wire's (its net ID); 0 for the zone's, or one not known.</summary>
    public ulong FrameKey(uint netId) => netId != 0 && _shipsByNet.TryGetValue(netId, out Ship? ship) ? ship.Id.Value : 0;

    /// <summary>A frame's net ID (the wire's) from the zone's key (a ship's ID); 0 for the zone's.</summary>
    public uint NetIdOfFrame(ulong key) => key != 0 && Zone.Ships().ShipOf(new EntityId(key)) is { } ship ? NetIdOf(ship) : 0;

    // A ship's net ID, given the first time it is asked for (kept while it lives).
    private uint NetIdOf(Ship ship)
    {
        if (!_shipNetIds.TryGetValue(ship.Id, out uint netId))
        {
            netId = NextNetId();
            _shipNetIds.Add(ship.Id, netId);
            _shipsByNet[netId] = ship;
        }
        return netId;
    }

    // After the step: every character's frame as the wire names it, and its view as others see it turned into that frame
    // when the input gave it in another (the tick of a hand-over).
    protected internal override void AfterStep()
    {
        AnnounceShips();
        ComponentPool<NetPresence> presences = Zone.Entities.Pool<NetPresence>();
        for (int i = 0; i < presences.Count; i++)
        {
            ref NetPresence presence = ref presences.Values[i];
            ref Character c = ref Zone.Entities.Get<Character>(Zone.Entities.AtSlot(presences.Slots[i]));
            presence.Frame = NetIdOfFrame(c.State.Frame);
            if (c.Input.Frame != c.State.Frame && presence.Owner != null)
            {
                Vector3 view = Zone.Frames.ViewIn(Quantize.FromOctahedral(presence.ViewU, presence.ViewV), c.Input.Frame, c.State.Frame, Zone.Tick);
                (presence.ViewU, presence.ViewV) = Quantize.Octahedral(view);
            }
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
            uint netId = NetIdOf(ship);
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

    /// <summary>
    /// The ships for this player's snapshot: their spawns and despawns first (reliable), then the pilot's own ship in the
    /// header and the ships offered by nearness; and the frame of the player's own state, as the wire names it.
    /// </summary>
    protected internal override void Snapshot(PlayerSession session, Vector3 centre, ref SnapshotHeader header, List<ShipCandidate> ships)
    {
        ReplicateShips(session, centre, ships);
        header.OwnShip = OwnShip(session)!;
        header.Own.Frame = NetIdOfFrame(Zone.Entities.Get<Character>(session.Entity).State.Frame);
        header.Own.Seat = (byte)(Zone.Ships().SeatIndexOf(session.Entity) + 1);
    }

    // The ships near a player: spawned and despawned (reliable), offered to their snapshot by nearness.
    private void ReplicateShips(PlayerSession session, Vector3 centre, List<ShipCandidate> ships)
    {
        ShipView view = session.State<ShipView>();
        HashSet<uint> relevant = view.Relevant;
        foreach (uint netId in _shipsGone)
        {
            view.DetailsSent.Remove(netId);
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
            SendDetails(session, view, netId, ship);
            float distance = Vector3.Distance(position, centre);
            bool moving = !ship.State.Asleep;
            ships.Add(new ShipCandidate
            {
                NetId = netId,
                State = ShipConvert.ToNet(ship, Zone.Targets().FlagsFor(ship.Ref, session.Viewer), Zone.Ships().ShipStill(ship), Zone.Ships().ShipSlow(ship)),
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
            view.DetailsSent.Remove(netId);
            session.Snapshots.Forget(netId);
            Despawned(session, netId);
        }
    }

    // A ship's details (IRON_WAKE.md §5: its parts away from rest, its doors' motion, its seats, its parts' health), to a
    // player who hasn't been told their newest: when it comes into range, and whenever they change.
    private void SendDetails(PlayerSession session, ShipView view, uint netId, Ship ship)
    {
        (uint Parts, uint Details) now = (ship.PartsRevision, ship.DetailsRevision);
        if (view.DetailsSent.TryGetValue(netId, out (uint Parts, uint Details) sent) && sent == now)
        {
            return;
        }
        view.DetailsSent[netId] = now;
        Writer.Reset();
        ShipConvert.Details(ship, netId, id => id.Value).WriteMessage(Writer);
        session.Peer.Send(Writer.Data, DeliveryMethod.ReliableOrdered);
    }

    /// <summary>The ship this player flies (at its helm), exactly, for the snapshot's header (none otherwise).</summary>
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

    // The body's ships as they were saved: parked, flying (they fall or hover on), in orbit; whoever sat in their seats,
    // loaded as sleepers, goes back into them.
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
                for (int seat = 0; seat < restored.Occupants.Length; seat++)
                {
                    EntityId who = restored.Occupants[seat];
                    if (!who.IsNone && Zone.Entities.TryFind(who, out Entity sitter) && Zone.Players().PlayerOf(sitter) != null)
                    {
                        Zone.Ships().Reseat(sitter, ship, seat);
                    }
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
        // Sleepers stored aboard a ship that didn't come back (IRON_WAKE.md §4.11) wake ashore, round the landing site.
        ComponentPool<Character> characters = Zone.Entities.Pool<Character>();
        for (int i = 0; i < characters.Count; i++)
        {
            ref Character c = ref characters.Values[i];
            Entity character = Zone.Entities.AtSlot(characters.Slots[i]);
            if (c.State.Stance == Stance.Seated && Zone.Ships().SeatOf(character) == null)
            {
                c.State.Stance = Stance.Standing; // stored seated in a seat it didn't get back: it stands where it was
            }
            if (c.State.Frame != 0 && Zone.Frames.Ship(c.State.Frame) == null)
            {
                EntityId id = Zone.Entities.IdOf(Zone.Entities.AtSlot(characters.Slots[i]));
                Log($"{NameOf(id)} slept aboard a ship that is gone; ashore at the landing site");
                byte health = c.State.Health;
                c.State = Zone.NewCharacter(Zone.SpawnPoint(id.Value));
                c.State.Health = health;
                c.Input = new MoveInput { View = Facing.View(0, 0, Zone.Frame.UpAt(c.State.Position)) };
            }
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
            save.Ships.Add(new ShipRecord(ship.Id, ShipCodec.Encode(ship, Zone.Frame, Zone.Tick)));
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
    private void AnnounceShips()
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

    /// <summary>
    /// A ship beside a player, full, theirs, its first door open: the kind asked for (IRON_WAKE.md §5.6: the admin's
    /// <c>ship [player] [kind]</c>, the F1 panel's spawn), else the first (every new player's Tin Finch with
    /// <see cref="ServerOptions.ShipOnJoin"/>). It stands on the nearest clear spot of rings round them, as far out as its
    /// size needs, with its way in toward them (<see cref="WayIn"/>); in a wood with no room for it, at the nearest spot
    /// whose only obstacles are trees and rocks, they go (<see cref="ShipSystem.ClearNatureFor"/>).
    /// </summary>
    public Ship? GiveShip(PlayerSession session, ShipDef? kind = null)
    {
        if (!session.Playing || Content.Ships.Count == 0)
        {
            return null;
        }
        ShipDef def = kind ?? Content.Ships[0];
        Vector3 feet = Zone.WorldOf(Zone.Entities.Get<Character>(session.Entity).State);
        Vector3 up = Zone.Frame.UpAt(feet);
        (Vector3 north, Vector3 east) = CharacterMotor.HorizontalBasis(0, up);
        // The way in's heading on a ship facing north here: the ship is turned by the rest.
        Vector3 way = WayIn(def);
        Vector3 side = Vector3.Normalize(Vector3.Cross(up, north));
        float wayYaw = way.X * way.X + way.Z * way.Z > 1e-6f ? Facing.YawOf(side * way.X + north * way.Z, up) : 0;
        float beyond = MathF.Max(0, MathF.Max(def.BoundsHalf.X, def.BoundsHalf.Z) - 4);
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (float ring in new[] { 6f, 9f, 12f, 16f, 22f })
            {
                float distance = ring + beyond;
                for (int i = 0; i < 12; i++)
                {
                    float angle = i * MathF.PI / 6 + ring;
                    Vector3 at = feet + (north * MathF.Cos(angle) + east * MathF.Sin(angle)) * distance;
                    Vector3 ground = Zone.Settle(at);
                    float yaw = Facing.YawOf(feet - at, up) - wayYaw;
                    int cleared = pass == 1 ? Zone.Ships().ClearNatureFor(def, ground, yaw) : 0;
                    if (Zone.Ships().ShipSpaceClear(def, ground, yaw))
                    {
                        if (cleared > 0)
                        {
                            Log($"cleared {cleared} trees and rocks for a {def.Name} beside {session.Name}");
                        }
                        Ship ship = Zone.Ships().SpawnShip(def, ground, yaw, session.EntityId, def.FuelCapacity);
                        if (def.Doors.Count > 0)
                        {
                            Zone.Ships().SetDoorAtOnce(ship, 0, 1);
                        }
                        return ship;
                    }
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Where a ship is entered, in its frame: its helm's part (the hatch) of one people sit in; the lowest box of the
    /// boarding volume of one they walk in (the Iron Wake's open ramp, down to the ground).
    /// </summary>
    public static Vector3 WayIn(ShipDef def)
    {
        if (def.Interior is not { Boarding.Count: > 0 } interior)
        {
            return def.PartPoint(def.PartOfSeat(def.Helm.Index));
        }
        BoardingBox lowest = interior.Boarding[0];
        foreach (BoardingBox box in interior.Boarding)
        {
            if (box.Min.Y < lowest.Min.Y)
            {
                lowest = box;
            }
        }
        return (lowest.Min + lowest.Max) / 2;
    }
}

/// <summary>The accessor on the server (GROUND_SYSTEMS.md §12.5): ships' intents, replication and storage.</summary>
public static class ShipModuleAccess
{
    public static ShipModule Ships(this GameServer server) => server.Require<ShipModule>();
}
