using System;
using System.Collections.Generic;
using System.Linq;
using Hollow.Client;
using Hollow.Net.Bits;
using Hollow.Net.Protocol;
using Hollow.Persistence;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Interaction;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Targets;
using Hollow.Surface;

namespace Hollow.Tests;

public static class ShipFuelTests
{
    [Test]
    public static void ShipFuelOnlyTakesFuelAndHonoursBothTankAndCarriedCapacity()
    {
        GameContent c = ContentFixture.Content;
        foreach (string key in new[] { "tin-finch", "iron-wake" })
        {
            ShipDef def = c.Ship(key)!;
            var fuel = new ShipFuel(def);
            var carried = new Container(ContainerKind.Main, 24);
            int capacity = (int)def.FuelCapacity;
            fuel.Load(capacity - 20);
            carried.Set(0, ItemStack.Of(def.Fuel, 50));
            Assert.Equal(OutcomeCode.Done, InventoryRules.Move(c, carried, 0, fuel.Tank, 0, 0).Code, key + " fills only remaining space");
            Assert.Equal(capacity, fuel.Units, key + " authored capacity");
            Assert.Equal(30, carried[0].Count, "excess stays with the player");
            Assert.Equal(OutcomeCode.NoSpace, InventoryRules.QuickMove(c, carried, 0, new[] { fuel.Tank }).Code, "full tank refuses more");
            carried.Set(1, ItemStack.Of(c.ItemByKey("wood"), 1));
            Assert.Equal(OutcomeCode.NotAllowed, InventoryRules.Move(c, carried, 1, fuel.Tank, 0, 0).Code, "cannot swap wood into a fuel tank");
            Assert.Equal(capacity, fuel.Units, "a refused swap changes nothing");
            Assert.Equal(OutcomeCode.Done, InventoryRules.Move(c, fuel.Tank, 0, carried, 2, 0).Code, "can drag a large tank stack into an ordinary slot");
            Assert.Equal(def.Fuel.StackSize, carried[2].Count, "carried stack never exceeds normal size");
            Assert.Equal(capacity - def.Fuel.StackSize, fuel.Units, "unmoved fuel remains in the tank");
            if (fuel.Units > 0)
                Assert.Equal(OutcomeCode.Done, InventoryRules.QuickMove(c, fuel.Tank, 0, new[] { carried }).Code, "quick withdraw splits across ordinary slots");
            Assert.Equal(capacity + 30, InventoryRules.Count(new[] { carried }, def.Fuel.Id), "all whole fuel units conserved");
            Assert.True(carried.Slots.ToArray().All(s => s.Item != def.Fuel.Id || s.Count <= def.Fuel.StackSize), "all resulting stacks valid");
        }
    }

    [Test]
    public static void ShipFuelPartialUnitSurvivesWithdrawalAndCannotBeDuplicated()
    {
        GameContent c = ContentFixture.Content;
        ShipDef def = c.Ship("iron-wake")!;
        var fuel = new ShipFuel(def);
        var carried = new Container(ContainerKind.Main, 24);
        fuel.Load(6000);
        fuel.Consume(0.25f);
        Assert.Equal(5999, fuel.Units, "opened exactly one item");
        Assert.Near(0.75, fuel.Reserve, 1e-7, "partly used unit stays in the engine");
        Assert.Equal(5999, fuel.Tank.StackLimit, "reserve occupies space in the tank capacity");
        InventoryRules.QuickMove(c, fuel.Tank, 0, new[] { carried });
        Assert.Equal(5999, InventoryRules.Count(new[] { carried }, def.Fuel.Id), "only unopened units are withdrawn");
        Assert.Near(0.75, fuel.Amount, 1e-7, "withdrawing everything does not discard reserve");
        for (int i = 0; i < carried.Size; i++)
            if (!carried[i].IsEmpty) InventoryRules.QuickMove(c, carried, i, new[] { fuel.Tank });
        carried.Set(0, ItemStack.Of(def.Fuel, 1));
        Assert.Equal(OutcomeCode.NoSpace, InventoryRules.QuickMove(c, carried, 0, new[] { fuel.Tank }).Code, "cannot overfill around the partial unit");
        fuel.Consume(5999.75f);
        Assert.True(fuel.Tank.IsEmpty && fuel.Reserve == 0 && fuel.Amount == 0, "all and only remaining fuel burns");
        Assert.Equal(6000, fuel.Tank.StackLimit, "fully empty tank regains its whole-unit capacity");
        fuel.Consume(100);
        Assert.Near(0, fuel.Amount, 0, "empty engine cannot produce negative fuel");
        var tiny = new ShipFuel(def);
        tiny.Load(6000);
        for (int i = 0; i < 30000; i++) tiny.Consume(0.00001f);
        Assert.Near(5999.7, tiny.Amount, 0.002, "small burns are retained even below the full-tank float resolution");
    }

    [Test]
    public static void ShipFuelV5AndLegacyV4RestoreExactlyOneAuthority()
    {
        GameContent c = ContentFixture.Content;
        ShipDef def = c.Ship("iron-wake")!;
        ZoneSpec spec = ZoneSpec.StartingWorld(10, 20);
        ZoneFrame frame = ZoneFrame.World;
        var ship = new Ship(new EntityId(700), def, new EntityId(701))
        {
            State = ShipMotor.Standing(def, frame.ToLocal(new Hollow.Core.Vector3D(spec.Radius + 100, 0, 0)), System.Numerics.Vector3.UnitX, 0),
        };
        ship.LoadFuel(5999.75f);
        byte[] bytes = ShipCodec.Encode(ship, frame)[..^ShipCodec.AttitudeTrailerBytes];
        bytes[0] = 5; // Genuine v5 bytes, including its tank fields but not the v6 attitude trailer.
        Ship restored = ShipCodec.Decode(ship.Id, bytes, c, frame)!.Ship;
        Assert.Equal(5999, restored.Fuel.Units, "oversized tank item survives v5 save");
        Assert.Near(0.75, restored.Fuel.Reserve, 1e-7, "fraction survives v5 save");
        Assert.Near(5999.75, restored.State.Fuel, 1e-7, "cached total is not added twice");
        byte[] legacy = bytes[..^8]; // v4 has the same prefix, before the two new tank fields.
        legacy[0] = 4;
        Ship migrated = ShipCodec.Decode(ship.Id, legacy, c, frame)!.Ship;
        Assert.Equal(5999, migrated.Fuel.Units, "v4 float becomes unopened units once");
        Assert.Near(0.75, migrated.Fuel.Reserve, 1e-7, "v4 fraction becomes the reserve");
        migrated.Fuel.Tank.Clear();
        Ship withdrawn = ShipCodec.Decode(ship.Id, ShipCodec.Encode(migrated, frame), c, frame)!.Ship;
        Assert.True(withdrawn.Fuel.Tank.IsEmpty, "withdrawal survives restart, even if cached State.Fuel was stale");
        Assert.Near(0.75, withdrawn.State.Fuel, 1e-7, "restart preserves only the remaining reserve");
    }

    [Test]
    public static void ShipFuelContainerMetadataKeepsClientMovesIdentical()
    {
        GameContent c = ContentFixture.Content;
        ShipDef def = c.Ship("iron-wake")!;
        var local = new LocalPlayer(c) { Send = _ => true };
        local.OnLoot(new LootState { Target = 42, Kind = LootKind.Box }, null);
        var sent = new ContainerContents
        {
            Which = ContainerRef.Loot, Size = 1, AcceptedItem = def.Fuel.Id, StackLimit = 5999,
            Slots = new[] { new SlotNet { Index = 0, Item = def.Fuel.Id, Count = 5999 } },
        };
        var writer = new BitWriter();
        sent.Write(writer);
        var read = ContainerContents.Read(new BitReader(writer.ToArray()));
        local.OnContents(read);
        local.Update();
        local.MoveItem(ContainerRef.Loot, 0, ContainerRef.Main, 0);
        local.Update();
        Assert.Equal(500, local.Inventory.Main[0].Count, "optimistic drag obeys carried stack size");
        Assert.Equal(5499, local.Loot![0].Count, "optimistic tank keeps the remainder");
        Assert.Equal((ushort)def.Fuel.Id, local.Loot.AcceptedItem, "accepted item survives protocol and optimistic copy");
        writer.Reset();
        new ContainerSlots { LastSeq = uint.MaxValue, Which = ContainerRef.Loot, AcceptedItem = def.Fuel.Id, StackLimit = 6000,
            Slots = new[] { new SlotNet { Index = 0 } } }.Write(writer);
        local.OnSlots(ContainerSlots.Read(new BitReader(writer.ToArray())));
        local.Update();
        Assert.True(local.Loot.IsEmpty && local.Loot.StackLimit == 6000, "empty incremental update restores full capacity");
        Assert.True(!local.Loot.Accepts(c, 0, ItemStack.Of(c.ItemByKey("wood"), 1)), "client refuses a wrong item in the empty tank");
    }

    [Test]
    public static void ShipFuelCapAndHullUseTheSameInventoryWithCrewAndReachGates()
    {
        using var rig = new ShipRig(content: true);
        Ship ship = rig.Add(fuel: 12);
        Zone z = rig.Zone;
        Entity owner = z.Players().AddPlayer(z.NextId(), "Owner", z.NewCharacter(ship.Origin));
        Entity other = z.Players().AddPlayer(z.NextId(), "Other", z.NewCharacter(ship.Origin));
        var player = z.Players().PlayerOf(owner)!;
        ship.Owner = player.Id;
        var cap = new TargetRef(TargetFamily.Ship, ship.Id.Value, ship.Def.FuelPart());
        var hull = cap with { Part = 0 };
        var options = new List<InteractOption>();
        z.Interaction().Options(owner, cap, options);
        int capVerb = ContentFixture.Content.Interactions.Index("refuel");
        Assert.True(options.Any(o => o.Verb == capVerb && o.Enabled), "fuel cap offers its Open action");
        Assert.Equal("Open", ContentFixture.Content.Interactions.Verb(capVerb)!.Label, "cap prompt does not say refuel");
        z.Interaction().Options(owner, hull, options);
        int tankVerb = ContentFixture.Content.Interactions.Index("open-fuel");
        Assert.True(options.Any(o => o.Verb == tankVerb && o.Enabled), "general ship radial offers its tank");
        Assert.Equal(OutcomeCode.Done, z.Ships().OpenFuel(player, ship, cap.Part).Code, "crew opens one slot");
        Assert.True(ReferenceEquals(ship.Fuel.Tank, z.Actions().ResolveContainer(player, ContainerRef.Loot)), "normal inventory routes to tank");
        Assert.Near(12, ship.Fuel.Amount, 0, "opening never transfers fuel");
        Assert.Equal(OutcomeCode.NotCrew, z.Ships().OpenFuel(z.Players().PlayerOf(other)!, ship, 0).Code, "outsider cannot bypass crew permission");
        z.Entities.Get<Character>(owner).State.Position = z.GroundAt(300, 300);
        Assert.True(!z.Ships().StillAtShip(owner, ship, player.LootPart), "tank closes when the reached ship part is out of range");
    }

    [Test]
    public static void ShipFuelFinalFractionPowersOnlyPartOfATickAndThenStops()
    {
        using var rig = new ShipRig();
        Ship ship = rig.Add(height: 200, fuel: 0.0001f);
        ShipState empty = ship.State, full = ship.State;
        empty.Fuel = 0;
        full.Fuel = 1;
        ShipInput input = new() { Engines = true, Throttle = 1, Buttons = ShipButtons.AssistOff };
        ShipMotor.Step(ref empty, input, ship.Def, rig.Zone.Ships().ShipContext);
        ShipMotor.Step(ref full, input, ship.Def, rig.Zone.Ships().ShipContext);
        rig.Fly(ship, input, 1);
        Assert.Near(0, ship.Fuel.Amount, 0, "final reserve exhausted");
        Assert.True(!ship.State.Has(ShipFlags.EnginesOn), "engine flag clears on the exhaustion tick");
        float tinyImpulse = System.Numerics.Vector3.Distance(ship.State.Velocity, empty.Velocity);
        float wholeImpulse = System.Numerics.Vector3.Distance(full.Velocity, empty.Velocity);
        Assert.True(tinyImpulse > 0 && tinyImpulse < wholeImpulse * 0.1f, "tiny reserve cannot purchase a whole free thrust tick");
        rig.Fly(ship, input, 1);
        Assert.True(ship.Events.FuelUsed == 0 && ship.State.Thrust == System.Numerics.Vector3.Zero, "no powered tick after depletion");
    }
}
