using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using Hollow.Client;
using Hollow.Core;
using Hollow.Persistence;
using Hollow.Simulation;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Interaction;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Targets;

namespace Hollow.Tests;

/// <summary>
/// IW2 (IRON_WAKE.md §5): the Iron Wake's seats, parts, doors, its helm's list and its parts' health, engine-free and over
/// the network; and its stored form, version 2 and version 1.
/// </summary>
public static class ShipPartsTests
{
    private static ShipSeatDef SeatOf(ShipDef def, string key) => def.Seats.First(s => s.Key == key);

    private static TargetRef PartRef(Ship ship, int part) => new(TargetFamily.Ship, ship.Id.Value, (byte)part);

    // Where a seat is left to (its first exit), in the ship's frame at rest.
    private static Vector3 ExitOf(ShipDef def, ShipSeatDef seat) => def.Interior!.SocketAt(seat.Exits[0], def.Interior.Rest);

    /// <summary>
    /// IRON_WAKE.md §5.2: every way out of a seat frees it at once. Killed at the helm; an admin's teleport from a battery's
    /// seat; asleep (logged off) in the copilot's seat it stays theirs, and a lost connection stands them up at its exit,
    /// aboard; a wrecked ship kills the one at its helm. Each time the seat is empty and the character holds no seat.
    /// </summary>
    [Test]
    public static void EveryWayOutFreesTheSeat()
    {
        using var rig = new IronWakeRig();
        Zone z = rig.Zone;
        Ship ship = rig.Ship;
        ShipDef def = rig.Def;
        int helm = def.Helm.Index;
        bool Free(int seat, Entity e) => ship.Occupants[seat].IsNone && !z.Entities.Has<ShipSeat>(e) && z.Ships().SeatOf(e) == null;

        Entity ada = rig.Pilot("Ada");
        Assert.True(ship.Occupants[helm] == rig.Game.P(ada).Id && rig.Ch(ada).State.Stance == Stance.Seated, "Ada at the helm");
        z.Players().Die(ada, rig.Game.P(ada), "the F1 console's kill", EntityId.None, "");
        Assert.True(Free(helm, ada), "killed: the helm is free at once");

        ShipSeatDef mount = SeatOf(def, "forward-starboard-mount");
        Entity bo = rig.Aboard("Bo", ExitOf(def, mount), Vector3.UnitZ);
        rig.Step(5);
        TargetRef mountPart = PartRef(ship, def.PartOfSeat(mount.Index));
        Assert.Equal(OutcomeCode.NotCrew, z.Interaction().Interact(bo, mountPart, "man").Code, "a battery is its crew's");
        Assert.Equal(OutcomeCode.Done, z.Ships().Authorise(rig.Game.P(bo).Id.Value, ship).Code, "Bo on the list");
        Assert.Equal(OutcomeCode.Done, z.Interaction().Interact(bo, mountPart, "man").Code, "Bo mans the forward starboard mount");
        Assert.True(ship.Occupants[mount.Index] == rig.Game.P(bo).Id && rig.Ch(bo).State.Frame == rig.Key, "seated at the battery, aboard");
        Vector3 away = z.Settle(rig.Pose.ToWorld(new Vector3(0, 0, -45)));
        z.Teleport(bo, away + z.Frame.UpAt(away) * 0.05f);
        Assert.True(Free(mount.Index, bo) && rig.Ch(bo).State.Frame == 0 && rig.Ch(bo).State.Stance == Stance.Standing, "an admin's teleport frees it");

        ShipSeatDef copilot = SeatOf(def, "copilot");
        Entity cy = rig.Aboard("Cy", ExitOf(def, copilot), Vector3.UnitZ);
        rig.Step(5);
        Assert.Equal(OutcomeCode.Done, z.Interaction().Interact(cy, PartRef(ship, def.PartOfSeat(copilot.Index)), "sit").Code, "Cy sits in the copilot's seat");
        z.Players().Sleep(cy);
        rig.Step(30);
        Assert.True(ship.Occupants[copilot.Index] == rig.Game.P(cy).Id, "logged off, asleep in the seat, it stays Cy's");
        Assert.True(z.Ships().Release(cy, SeatRelease.Disconnected), "a lost connection stands Cy up");
        Vector3 cyAt = rig.Ch(cy).State.Position;
        Assert.True(Free(copilot.Index, cy) && rig.Ch(cy).State.Frame == rig.Key && Vector3.Distance(cyAt, ExitOf(def, copilot)) < 0.6f,
            $"at the seat's exit, aboard ({cyAt})");

        Entity dee = rig.Pilot("Dee");
        z.Ships().DamageShip(ship, 1e9f, ShipDamage.Explosive, null);
        Fixtures.Report($"wrecked: Dee dead {rig.Game.P(dee).Dead}, the ship destroyed {ship.Destroyed}");
        Assert.True(ship.Destroyed && rig.Game.P(dee).Dead && Free(helm, dee), "a wreck kills the one at its helm, and frees the seat");
    }

    /// <summary>
    /// IRON_WAKE.md §5.1: each part offers its own verbs, its own name in the prompt. The forward starboard mount says MAN
    /// FORWARD STARBOARD MOUNT, never BOARD IRON WAKE; the helm takes the helm and keeps the ship's list; the ramp opens and
    /// shuts; the hull offers no seat.
    /// </summary>
    [Test]
    public static void EachPartOffersItsOwnVerbs()
    {
        using var rig = new IronWakeRig();
        Zone z = rig.Zone;
        ShipDef def = rig.Def;
        GameContent c = rig.Game.C;
        Entity ada = rig.Aboard("Ada", ExitOf(def, SeatOf(def, "forward-starboard-mount")), Vector3.UnitZ);
        rig.Step(5);
        string Prompt(int part, out List<string> verbs)
        {
            var options = new List<InteractOption>();
            z.Interaction().Options(ada, PartRef(rig.Ship, part), options);
            verbs = options.Select(o => c.Interactions.Verb(o.Verb)!.Key).ToList();
            int primary = InteractionRules.Primary(options);
            if (primary < 0)
            {
                return "";
            }
            InteractOption o = options[primary];
            VerbDef verb = c.Interactions.Verb(o.Verb)!;
            return (verb.Named ? $"{verb.Label} {o.Name ?? (part == 0 ? def.Name : def.Parts[part].Name)}" : verb.Label).ToUpperInvariant();
        }
        string mount = Prompt(def.PartIndex("forward-starboard-mount"), out List<string> mountVerbs);
        string hull = Prompt(0, out List<string> hullVerbs);
        Fixtures.Report($"the mount: \"{mount}\" ({string.Join(", ", mountVerbs)}); the hull: \"{hull}\" ({string.Join(", ", hullVerbs)})");
        Assert.Equal("MAN FORWARD STARBOARD MOUNT", mount, "the mount's own verb and name");
        Assert.True(!mountVerbs.Contains("board") && !mountVerbs.Contains("take-helm") && !hullVerbs.Any(v => ShipSeatDef.Verbs.Contains(v)),
            "no whole-ship verbs at the mount, no seat at the hull");

        rig.Ch(ada).State.Position = new Vector3(-1.75f, 9.12f, 13.9f);
        rig.Step(5);
        Prompt(def.PartIndex("helm"), out List<string> helmVerbs);
        Assert.True(helmVerbs.Contains("take-helm") && helmVerbs.Contains("authorise") && !helmVerbs.Contains("cupboard-pick-up"),
            $"the helm: the seat and the list ({string.Join(", ", helmVerbs)})");

        rig.Ch(ada).State.Position = new Vector3(0, 2.22f, -20);
        rig.Step(5);
        string ramp = Prompt(def.PartIndex("ramp"), out _);
        Assert.Equal("CLOSE RAMP", ramp, "the open ramp shuts");
    }

    /// <summary>
    /// IRON_WAKE.md §5.3: the ramp (6 s, at rest or slow and low) and its switch. It won't shut on someone standing on it;
    /// shut from the switch inside it closes in six seconds and the parked body is made again. Locked, it stays shut to a
    /// stranger. Two timed charges stuck to it break it (an armoured door's 1000): it falls open and stays jammed; the crew
    /// mend it with the hammer (refined metal, once the damage is 30 s old) and it shuts again.
    /// </summary>
    [Test]
    public static void TheRampOpensShutsBreaksAndIsMended()
    {
        using var rig = new IronWakeRig();
        Zone z = rig.Zone;
        Ship ship = rig.Ship;
        ShipDef def = rig.Def;
        GameContent c = rig.Game.C;
        int ramp = def.DoorIndex("ramp"), main = def.Interior!.PartIndex("Ramp_Main");
        TargetRef rampPart = PartRef(ship, def.PartIndex("ramp")), rampSwitch = PartRef(ship, def.PartIndex("ramp-switch"));
        Entity ada = rig.Aboard("Ada", new Vector3(0, 2.2f, -20), Vector3.UnitZ);
        Assert.Equal(OutcomeCode.Done, z.Ships().Authorise(rig.Game.P(ada).Id.Value, ship).Code, "Ada is its crew");
        rig.Step(10);
        Assert.True(ship.DoorAt(ramp, z.Tick) == 1 && rig.Ch(ada).State.Frame == rig.Key, "the ramp open, Ada on it");
        Assert.Equal(OutcomeCode.RampBlocked, z.Interaction().Interact(ada, rampPart, "hatch-close").Code, "it won't shut on her");

        rig.Ch(ada).State.Position = new Vector3(-2.2f, 3.22f, -15.6f);
        rig.Step(10);
        Outcome shut = z.Interaction().Interact(ada, rampSwitch, "hatch-close");
        Assert.Equal(OutcomeCode.Done, shut.Code, "shut from its switch in the hold");
        uint parked = ship.ParkedBody;
        rig.Step(90);
        float half = ship.DoorAt(ramp, z.Tick);
        Assert.True(half is > 0.3f and < 0.7f && ship.ParkedBody == parked, $"half way after three seconds ({half:0.00}), the parked body kept");
        rig.Step(92);
        Assert.True(ship.DoorAt(ramp, z.Tick) == 0 && ship.Parts[main] == 0 && ship.ParkedBody != 0 && ship.Doors[ramp].Moving == 0, "shut in six seconds");
        Assert.Equal(OutcomeCode.Done, z.Interaction().Interact(ada, rampSwitch, "ship-lock").Code, "Ada locks the ship");

        Entity bo = rig.Add("Bo", new Vector3(0.5f, 0, -21f), Vector3.UnitZ);
        rig.Step(5);
        Assert.Equal(OutcomeCode.Locked, z.Ships().SetDoor(rig.Game.P(bo), ship, ramp, open: true).Code, "locked: shut to a stranger");

        // Two timed charges stuck to the shut ramp; Ada well forward, Bo away.
        rig.Ch(ada).State.Position = new Vector3(0, 3.22f, 0);
        z.Teleport(bo, z.Settle(rig.Pose.ToWorld(new Vector3(0, 0, -60))));
        rig.Step(5);
        BlastStats charge = c.Blast("timed-charge")!;
        ushort item = c.ItemByKey("timed-charge").Id;
        float full = def.Doors[ramp].Health!.Max;
        for (int i = 0; i < 2; i++)
        {
            ShipPose pose = rig.Pose;
            Vector3 at = pose.ToWorld(def.PartPoint(rampPart.Part, ship.Parts) - Vector3.UnitZ * 0.3f);
            z.Damage().Blast(charge, at, pose.DirectionToWorld(-Vector3.UnitZ), rampPart, TargetFamily.Ship, rig.Game.P(bo).Id, "Bo", item);
            Fixtures.Report($"charge {i + 1}: the ramp at {ship.Doors[ramp].Health:0} of {full:0}");
        }
        Assert.True(ship.Doors[ramp].Health <= 0, "two charges break it");
        Assert.True(ship.DoorAt(ramp, z.Tick) == 1 && ship.Parts[main] == 1, "broken, it falls open");
        rig.Ch(ada).State.Position = new Vector3(-2.2f, 3.22f, -15.6f);
        rig.Step(10);
        Assert.Equal(OutcomeCode.Jammed, z.Interaction().Interact(ada, rampSwitch, "hatch-close").Code, "and stays jammed open");

        // Mended with the hammer from the ramp.
        rig.Game.P(ada).Inventory.Belt.Set(0, ItemStack.Of(c.ItemByKey("hammer"), 1));
        rig.Game.P(ada).Inventory.Belt.Set(1, ItemStack.Of(c.ItemByKey("refined-metal"), 30));
        rig.Ch(ada).State.Position = new Vector3(0, 2.22f, -20);
        rig.Step(10);
        Assert.Equal(OutcomeCode.RecentlyDamaged, z.Building().Repair(ada, rampPart).Code, "not while the damage is fresh");
        z.UnixMilliseconds += 31_000;
        int hits = 0;
        while (z.Building().Repair(ada, rampPart).Ok && hits < 30)
        {
            hits++;
        }
        int metal = rig.Game.P(ada).Inventory.Belt[1].Count;
        Fixtures.Report($"mended in {hits} hits for {30 - metal} refined metal: the ramp at {ship.Doors[ramp].Health:0}");
        Assert.True(ship.Doors[ramp].Health == full && hits == 10 && metal == 20, "ten hits of 100, a tenth of its ten refined metal each");
        Assert.Equal(OutcomeCode.FullHealth, z.Building().Repair(ada, rampPart).Code, "whole");
        rig.Ch(ada).State.Position = new Vector3(-2.2f, 3.22f, -15.6f);
        rig.Step(10);
        Assert.Equal(OutcomeCode.Done, z.Interaction().Interact(ada, rampSwitch, "hatch-close").Code, "mended, it shuts again");
    }

    /// <summary>
    /// IRON_WAKE.md §5.2: the helm is the ship's tool cupboard. Its list governs the helm, the batteries and the locked
    /// doors: off it, Bo can't take the helm nor open a locked door; on it (authorised at the helm, as at a cupboard) he can
    /// both; a cleared list shuts him out again.
    /// </summary>
    [Test]
    public static void TheHelmsListGovernsTheHelmAndTheDoors()
    {
        using var rig = new IronWakeRig();
        Zone z = rig.Zone;
        Ship ship = rig.Ship;
        ShipDef def = rig.Def;
        TargetRef helm = PartRef(ship, def.HelmPart());
        int airlock = def.DoorIndex("airlock");
        Entity ada = rig.Aboard("Ada", new Vector3(-1.75f, 9.1f, 13.9f), Vector3.UnitZ);
        Entity bo = rig.Aboard("Bo", new Vector3(-0.6f, 9.1f, 14.2f), Vector3.UnitZ);
        rig.Step(5);
        Assert.Equal(OutcomeCode.NotCrew, z.Interaction().Interact(ada, helm, "take-helm").Code, "nobody's list: the helm waits for one");
        Assert.Equal(OutcomeCode.Done, z.Interaction().Interact(ada, helm, "authorise").Code, "Ada authorises at the helm");
        Assert.Equal(OutcomeCode.Done, z.Interaction().Interact(ada, helm, "take-helm").Code, "and takes it");
        Assert.Equal(OutcomeCode.Done, z.Ships().SeatedLock(ada, ship.Id).Code, "she locks the ship from the helm");
        Assert.Equal(OutcomeCode.Done, z.Ships().SeatedDoor(ada, ship.Id, airlock).Code, "and works the airlock from it");
        Assert.True(ship.Doors[airlock].Opening, "the airlock opening");
        rig.Step(60);
        Assert.Equal(OutcomeCode.Done, z.Ships().SetDoor(rig.Game.P(bo), ship, airlock, open: false).Code, "anyone may shut a door");
        rig.Step(60);
        Assert.Equal(OutcomeCode.Locked, z.Ships().SetDoor(rig.Game.P(bo), ship, airlock, open: true).Code, "Bo, off the list: the locked airlock stays shut to him");
        Assert.Equal(OutcomeCode.Done, z.Ships().LeaveSeat(ada).Code, "Ada stands up from the helm");
        Assert.Equal(OutcomeCode.NotCrew, z.Interaction().Interact(bo, helm, "take-helm").Code, "Bo can't take the helm");
        Assert.Equal(OutcomeCode.Done, z.Interaction().Interact(bo, helm, "authorise").Code, "Bo authorises at the helm");
        Assert.Equal(OutcomeCode.Done, z.Ships().SetDoor(rig.Game.P(bo), ship, airlock, open: true).Code, "now the airlock opens for him");
        Assert.Equal(OutcomeCode.Done, z.Interaction().Interact(bo, helm, "take-helm").Code, "and the helm is his");
        Assert.Equal(OutcomeCode.Done, z.Ships().LeaveSeat(bo).Code, "Bo stands up");
        rig.Step(60);
        Assert.Equal(OutcomeCode.Done, z.Interaction().Interact(ada, helm, "clear-list").Code, "Ada clears the list");
        Assert.True(ship.Crew.Count == 0, "nobody on it");
        Assert.Equal(OutcomeCode.Done, z.Ships().SetDoor(rig.Game.P(bo), ship, airlock, open: false).Code, "Bo shuts the airlock");
        rig.Step(60);
        Assert.Equal(OutcomeCode.Locked, z.Ships().SetDoor(rig.Game.P(bo), ship, airlock, open: true).Code, "and it stays shut to him again");
    }

    /// <summary>
    /// IRON_WAKE.md §5.2, §4.8 over the network: Ada at the helm flies the Iron Wake for ten seconds while Bo sits at the
    /// forward starboard mount; both clients predict them exactly (no correction of either character, nor of Ada's ship).
    /// Bo stands up in flight: onto the walkway by the battery, aboard.
    /// </summary>
    [Test]
    public static void TwoSeatedInAFlyingShip()
    {
        using var rig = new LoopbackRig();
        GameClient ada = rig.Join("Ada");
        GameClient bo = rig.Join("Bo");
        rig.WaitUntilPlaying();
        IronWakeNetTests.ShipFacts ship = IronWakeNetTests.Spawn(rig, "Ada");
        ShipDef def = ContentFixture.Content.Ship("iron-wake")!;
        ShipSeatDef mount = SeatOf(def, "forward-starboard-mount");
        IronWakeNetTests.Heal(rig, ada.EntityId);
        IronWakeNetTests.Heal(rig, bo.EntityId);
        IronWakeNetTests.PutAboard(rig, ada.EntityId, ship.Id, new Vector3(-1.75f, 9.1f, 13.9f));
        IronWakeNetTests.PutAboard(rig, bo.EntityId, ship.Id, ExitOf(def, mount));
        rig.Server.Inspect(sv => sv.Zone.Ships().Authorise(bo.EntityId.Value, sv.Zone.Ships().ShipOf(new EntityId(ship.Id))!));
        rig.Run(0.8, (_, _) => default);
        ada.Local.Interact(new TargetRef(TargetFamily.Ship, ship.Id, def.HelmPart()), "take-helm");
        bo.Local.Interact(new TargetRef(TargetFamily.Ship, ship.Id, (byte)def.PartOfSeat(mount.Index)), "man");
        rig.Run(1.0, (_, _) => default);
        (int ServerSeat, Stance ServerStance) boOnServer = rig.Server.Inspect(sv =>
        {
            sv.Zone.Entities.TryFind(bo.EntityId, out Entity e);
            return (sv.Zone.Ships().SeatIndexOf(e), sv.Zone.Entities.Get<Character>(e).State.Stance);
        });
        Fixtures.Report($"Bo: on the server seat {boOnServer.ServerSeat} {boOnServer.ServerStance}; his client seat {bo.Ships().SeatIndex} " +
            $"({bo.Ships().SeatShip?.Def?.Key}), {bo.Character.Stance}; the mount is seat {mount.Index}");
        Assert.True(ada.Ships().Piloting, "Ada at the helm, her client predicting the ship");
        Assert.True(bo.Ships().Seated && bo.Ships().Seat?.Key == mount.Key && bo.Character.Stance == Stance.Seated, "Bo at the battery, his client knows his seat");

        long adaCorrections = ada.Stats.Corrections, boCorrections = bo.Stats.Corrections, shipCorrections = ada.Stats.ShipCorrections;
        long boReconciled = bo.Stats.Reconciliations;
        rig.Run(10.0, (c, t) => c == ada
            ? new InputFrame { Ship = t < 4 ? new ShipInput { Vertical = 1 } : new ShipInput { Vertical = 0.2f, Throttle = 0.4f, Yaw = 0.5f } }
            : default);
        IronWakeNetTests.ShipFacts flown = IronWakeNetTests.Facts(rig);
        Fixtures.Report(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"flew {Vector3.Distance(flown.Pose.Position, ship.Pose.Position):0.0} m, {flown.Height:0.0} m up; corrections: Ada {ada.Stats.Corrections - adaCorrections}, " +
            $"her ship {ada.Stats.ShipCorrections - shipCorrections}, Bo {bo.Stats.Corrections - boCorrections} of {bo.Stats.Reconciliations - boReconciled} reconciliations"));
        Assert.True(flown.Height > 10, "the ship flew");
        Assert.Equal(0L, ada.Stats.Corrections - adaCorrections, "Ada's character never corrected");
        Assert.Equal(0L, ada.Stats.ShipCorrections - shipCorrections, "nor her ship");
        Assert.Equal(0L, bo.Stats.Corrections - boCorrections, "nor Bo at his battery");

        bo.Local.LeaveSeat();
        rig.Run(0.6, (c, _) => c == ada ? new InputFrame { Ship = new ShipInput { Vertical = 0.2f, Throttle = 0.4f } } : default);
        CharacterState stood = IronWakeNetTests.ServerState(rig, bo.EntityId);
        Fixtures.Report($"Bo stood up in flight: {stood}");
        Assert.True(stood.Frame == ship.Id && stood.Stance == Stance.Standing && !bo.Ships().Seated, "Bo stands up aboard");
        Assert.True(Vector3.Distance(stood.Position, ExitOf(def, mount)) < 1.5f, "on the walkway by the battery");
    }

    /// <summary>
    /// IRON_WAKE.md §12: <c>ShipCodec</c> version 2 keeps who sits where, each door's progress and way (a ramp half shut
    /// goes on shutting), the parts' states, the parts' and doors' health and the list; version 1's bytes (a pilot and a
    /// hatch) come back as the helm's occupant and the first door, its owner its list.
    /// </summary>
    [Test]
    public static void ShipCodecKeepsSeatsDoorsHealthAndTheList()
    {
        using var rig = new IronWakeRig();
        Zone z = rig.Zone;
        Ship ship = rig.Ship;
        ShipDef def = rig.Def;
        GameContent c = rig.Game.C;
        Entity ada = rig.Pilot("Ada");
        ShipSeatDef mount = SeatOf(def, "forward-starboard-mount");
        Entity bo = rig.Aboard("Bo", ExitOf(def, mount), Vector3.UnitZ);
        z.Ships().Authorise(rig.Game.P(bo).Id.Value, ship);
        rig.Step(5);
        Assert.Equal(OutcomeCode.Done, z.Interaction().Interact(bo, PartRef(ship, def.PartOfSeat(mount.Index)), "man").Code, "Bo at the mount");
        Assert.Equal(OutcomeCode.Done, z.Ships().SeatedDoor(ada, ship.Id, def.DoorIndex("ramp")).Code, "the ramp shutting");
        rig.Step(60);
        int mountPart = def.PartOfSeat(mount.Index), airlock = def.DoorIndex("airlock");
        ship.PartHealth[mountPart] = 700;
        ship.Doors[airlock].Health = 400;
        ship.Locked = true;
        byte[] bytes = ShipCodec.Encode(ship, z.Frame, z.Tick);
        StoredShip back = ShipCodec.Decode(ship.Id, bytes, c, z.Frame)!;
        Ship b = back.Ship;
        int ramp = def.DoorIndex("ramp");
        Fixtures.Report($"{bytes.Length} bytes; the ramp stored at {b.Doors[ramp].From:0.000} going {b.Doors[ramp].Moving}, here {ship.DoorAt(ramp, z.Tick):0.000}");
        Assert.True(back.Occupants.SequenceEqual(ship.Occupants), "who sits where");
        Assert.True(b.Doors[ramp].From == ship.DoorAt(ramp, z.Tick) && b.Doors[ramp].Moving == -1, "the ramp where it was, still shutting");
        Assert.True(b.Parts.SequenceEqual(ship.Parts), "the parts' states");
        Assert.True(b.PartHealth[mountPart] == 700 && b.Doors[airlock].Health == 400, "the battery's and the airlock's health");
        Assert.True(b.Crew.SequenceEqual(ship.Crew) && b.Locked, "the list and the lock");

        // Version 1: a Tin Finch with its pilot and its hatch half open, opening.
        ShipDef finch = c.Ship("tin-finch")!;
        Vector3 ground = z.GroundAt(40, 40);
        var owner = new EntityId(77);
        var pilot = new EntityId(78);
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            w.Write((byte)1);
            w.Write(finch.Id);
            w.Write(owner.Value);
            w.Write(false);
            Vector3D body = z.Frame.ToBody(ground + z.Frame.UpAt(ground) * 0.6f);
            w.Write(body.X);
            w.Write(body.Y);
            w.Write(body.Z);
            foreach (float q in new[] { 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 0f, 0f })
            {
                w.Write(q); // the turn, the velocity and the spin
            }
            w.Write(finch.Health);
            w.Write(10f);
            w.Write((ushort)0);
            w.Write((ushort)0);
            w.Write((ushort)0);
            w.Write((ushort)0);
            w.Write(true);
            w.Write((byte)15);
            w.Write(pilot.Value);
            w.Write(body.X);
            w.Write(body.Y);
            w.Write(body.Z);
            WorldCodec.WriteContainer(w, new Container(ContainerKind.Box, Math.Max(1, finch.CargoSlots)));
        }
        StoredShip old = ShipCodec.Decode(new EntityId(79), stream.ToArray(), c, z.Frame)!;
        Fixtures.Report($"version 1: the hatch at {old.Ship.Doors[0].From:0.00} going {old.Ship.Doors[0].Moving}, the helm's {old.Pilot}, the list {string.Join(",", old.Ship.Crew)}");
        Assert.True(old.Pilot == pilot && old.Ship.Doors[0].From == 0.5f && old.Ship.Doors[0].Moving == 1, "its pilot at the helm, its hatch half open and opening");
        Assert.True(old.Ship.Crew.SequenceEqual(new[] { owner.Value }), "its owner its list");
    }
}
