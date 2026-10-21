using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Hollow.Client;
using Hollow.Simulation;
using Hollow.Simulation.Building;
using Hollow.Simulation.Content;
using Hollow.Simulation.Electricity;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Nature;
using Hollow.Simulation.Physics;
using Hollow.Simulation.Targets;
using HollowUniverse.UI;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

public partial class GroundTour
{
    // A real hosted inventory/placement/interaction run. Admin is only used to grant the test inventory and travel.
    private async Task Compound()
    {
        await RepairGod(true);
        View.Interface.Open(GroundScreen.Hud);
        Me.HeldSlot = -1;
        var ground = Client.Placement().Ground!.Value;
        Num.Vector3 site = OpenSpot(Client.Character.Position).Spot;
        var plan = new (string Key, float X, float Z)[]
        {
            ("large-furnace", -14, -12), ("wooden-barricade", -7, -12), ("wood-compound-wall", 0, -12), ("wood-compound-gate", 9, -12),
            ("stone-compound-wall", -14, 0), ("stone-compound-gate", -5, 0), ("wood-spike-barricade", 4, 0), ("metal-barricade", 11, 0),
            ("windmill", -10, 13), ("sam-site", 0, 13), ("auto-turret", 10, 13),
        };
        var placed = new Dictionary<string, RemoteThing>();
        foreach (var entry in plan)
        {
            Admin("give", entry.Key + " 1");
            await Until(() => Count(entry.Key) > 0, 5);
            CompoundCheck(Count(entry.Key) > 0, entry.Key + " arrived in authoritative inventory");
            RemoteThing thing = await CompoundPlace(entry.Key, ground.Offset(site, entry.X, entry.Z));
            placed.Add(entry.Key, thing);
            await CompoundApproach(thing, picture: true);
            await Shot("compound-" + entry.Key);
        }
        foreach (var (key, lockKey, lockKind) in new[]
        {
            ("wood-compound-gate", "code-lock", LockKind.Code), ("stone-compound-gate", "key-lock", LockKind.Key),
        })
        {
            RemoteThing gate = placed[key];
            await CompoundApproach(gate);
            await Until(() => View.Aimed?.Target == Thing(gate.Id), 3);
            CompoundCheck(View.Aimed?.Target == Thing(gate.Id), key + " is targeted by E");
            await RepairKey(Key.E);
            await Until(() => gate.Utility.Open, 5);
            CompoundCheck(gate.Utility.Open, key + " opens through the actual E key");
            await Seconds(1.2);
            await Shot("compound-" + key + "-open");
            // Follow the actual open hull to an accessible leaf/post, then use E again within real reach and sight.
            DeployableDef gateDef = gate.Deployable(Client.Content)!;
            Num.Vector3 nearest = CompoundGeometry.Nearest(Client.Content, gateDef, true, gate.Position, gate.Up, gate.Yaw, Client.RenderEye);
            Num.Vector3 away = Client.Character.Position - nearest;
            away -= gate.Up * Num.Vector3.Dot(away, gate.Up);
            away = away.LengthSquared() > .01f ? Num.Vector3.Normalize(away) : Facing.Compass(gate.Up).North;
            await TeleportTo(nearest + away * 1.2f);
            Face(nearest);
            await Until(() => View.Aimed?.Target == Thing(gate.Id), 3);
            CompoundCheck(View.Aimed?.Target == Thing(gate.Id), key + " open leaf/post is within E reach");
            await RepairKey(Key.E);
            await Until(() => !gate.Utility.Open, 5);
            CompoundCheck(!gate.Utility.Open, key + " closes through the shared interaction path");
            await Seconds(1.2);
            await CompoundFitLock(gate, lockKey, lockKind);
        }

        RemoteThing wind = placed["windmill"], turret = placed["auto-turret"], sam = placed["sam-site"];
        Admin("give", "wire-tool 1");
        await Until(() => Count("wire-tool") > 0, 5);
        CompoundCheck(Count("wire-tool") > 0, "Wire Tool arrived in authoritative inventory");
        await Hold("wire-tool", 5);
        CompoundCheck(Me.HeldItem?.Wiring != null, "Wire Tool is held through the shared equipment path");
        foreach ((RemoteThing source, RemoteThing consumer) in new[] { (wind, sam), (sam, turret) })
        {
            PowerPort output = source.Deployable(Client.Content)!.Power!.Output!;
            PowerPort input = consumer.Deployable(Client.Content)!.Power!.Input!;
            Num.Vector3 outputAt = await CompoundApproachPort(source, output);
            await CompoundWireIntent(WireOperation.Begin, source, output, outputAt);
            Num.Vector3 inputAt = await CompoundApproachPort(consumer, input);
            await CompoundWireIntent(WireOperation.Complete, consumer, input, inputAt);
            await Until(() => consumer.PowerCable is { } cable && cable.OutputEntity == source.Id.Value &&
                cable.OutputPort == output.Id && cable.InputPort == input.Id && consumer.Utility.Powered, 5);
            CompoundCheck(consumer.PowerCable is { } connected && connected.OutputEntity == source.Id.Value &&
                connected.OutputPort == output.Id && connected.InputPort == input.Id && consumer.Utility.Powered,
                consumer.Deployable(Client.Content)!.Key + " receives directed power through its physical input");
            await CompoundApproach(consumer, picture: true);
            await Shot("compound-wire-" + consumer.Deployable(Client.Content)!.Key);
        }
        Me.HeldSlot = -1;
        await CompoundApproach(turret);
        Me.Interact(Thing(turret.Id), "defense-open");
        await Until(() => Me.LootTarget == turret.Id && Me.Loot != null, 4);
        CompoundCheck(Me.LootTarget == turret.Id && Me.Loot != null, "turret weapon/ammunition storage opens");
        await CompoundLoad("rifle", 1, 0);
        await CompoundLoad("rifle-ammo", 40, 1);
        await Until(() => turret.Utility.InstalledWeapon == Client.Content.ItemByKey("rifle").Id, 4);
        CompoundCheck(turret.Utility.InstalledWeapon == Client.Content.ItemByKey("rifle").Id, "installed firearm is replicated on the gunless mount");
        await Shot("compound-turret-inventory");
        Me.Close();
        View.Interface.Open(GroundScreen.Hud);
        await Seconds(.4);
        await Shot("compound-turret-armed");
        Me.Interact(Thing(turret.Id), "defense-off");
        await Until(() => !turret.Utility.DefenseEnabled && !turret.Utility.PowerEnabled, 4);
        CompoundCheck(!turret.Utility.DefenseEnabled && !turret.Utility.PowerEnabled, "one device switch disables operation while preserving its electrical connections");
        Me.Interact(Thing(turret.Id), "defense-on");
        await Until(() => turret.Utility.DefenseEnabled && turret.Utility.PowerEnabled && turret.Utility.Powered, 4);
        CompoundCheck(turret.Utility.DefenseEnabled && turret.Utility.PowerEnabled && turret.Utility.Powered, "device can be re-enabled on its existing cable");
        await CompoundApproach(sam);
        Me.Interact(Thing(sam.Id), "defense-open");
        await Until(() => Me.LootTarget == sam.Id && Me.Loot != null, 4);
        CompoundCheck(Me.LootTarget == sam.Id, "SAM ammunition storage opens");
        await CompoundLoad("rocket-hv", 3, 0);
        await Shot("compound-sam-loaded");
        Me.Close();
        View.Interface.Open(GroundScreen.Hud);

        RemoteThing furnace = placed["large-furnace"];
        await CompoundApproach(furnace);
        Me.Interact(Thing(furnace.Id), "open");
        await Until(() => Me.LootTarget == furnace.Id && Me.Loot != null, 4);
        CompoundCheck(Me.LootTarget == furnace.Id, "large furnace exposes its cooking slots");
        await CompoundLoad("wood", 50, 0);
        await CompoundLoad("metal-ore", 5, 3);
        Me.Interact(Thing(furnace.Id), "light");
        await Until(() => furnace.Lit, 4);
        CompoundCheck(furnace.Lit, "large furnace burns real inventory fuel");
        Me.Close();
        View.Interface.Open(GroundScreen.Hud);
        await Shot("compound-furnace-burning");

        Admin("give", "wooden-ladder 1");
        await Until(() => Count("wooden-ladder") > 0, 5);
        RemoteThing wall = placed["stone-compound-wall"];
        await CompoundApproach(wall);
        Num.Vector3 wallPoint = wall.Position + wall.Up * (Client.Content.ItemByKey("wooden-ladder").Deploys!.HalfExtents.Y + .05f);
        Face(wallPoint);
        var ladder = Placeable("wooden-ladder")!;
        var ladderGhost = Client.Placement().PlanAt(ladder, wallPoint, wall.Yaw);
        CompoundCheck(ladderGhost is { Valid: true }, "ladder has a valid placement against the compound wall");
        var existing = Client.Survival().Things.Select(t => t.Id).ToHashSet();
        Me.Place(ladderGhost!, ContainerRef.Main, SlotOf(Me.Inventory.Main, "wooden-ladder")!.Value);
        await Until(() => Client.Survival().Things.Any(t => !existing.Contains(t.Id) && t.Deployable(Client.Content)?.Key == "wooden-ladder"), 5);
        RemoteThing mountedLadder = Client.Survival().Things.FirstOrDefault(t => !existing.Contains(t.Id) && t.Deployable(Client.Content)?.Key == "wooden-ladder")
            ?? throw new InvalidOperationException("compound: ladder did not replicate");
        CompoundCheck(Count("wooden-ladder") == 0, "ladder placement spends its inventory item");
        placed.Add("wooden-ladder", mountedLadder);
        await Shot("compound-ladder-mounted");
        await TeleportTo(ground.Offset(site, 0, 29));
        Face(site + Client.Frame.UpAt(site) * 3);
        await Settle();
        await Shot("compound-ground-overview");
        File.WriteAllText(Path.Combine(Folder, "compound-state.json"), JsonSerializer.Serialize(placed.Select(p => new
        {
            key = p.Key, id = p.Value.Id.Value, position = new[] { p.Value.Position.X, p.Value.Position.Y, p.Value.Position.Z },
            powered = p.Value.Utility.Powered, source = p.Value.Utility.PowerSource, installed = p.Value.Utility.InstalledWeapon,
            enabled = p.Value.Utility.DefenseEnabled, open = p.Value.Utility.Open,
            lockKind = p.Value.Utility.Lock.ToString(), locked = (p.Value.Flags & ViewerFlags.Locked) != 0,
        }), new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("[compound-check] PASS 12 inventory-paid placements; both gates and paid key/code locks; wired SAM/turret; installed gun/rounds; burning furnace. Combat is covered by DefenseTests; this capture does not claim a target was fired upon.");
    }

    private async Task CompoundFitLock(RemoteThing gate, string key, LockKind kind)
    {
        int before = Count(key);
        Admin("give", key + " 1");
        await Until(() => Count(key) == before + 1, 5);
        CompoundCheck(Count(key) == before + 1, key + " arrived in authoritative inventory");
        CompoundCheck(LockMounts.TryCompoundPose(Client.Content, gate.Deployable(Client.Content)!, false,
            gate.Position, gate.Up, gate.Yaw, out var mount), "gate has its authored shared lock socket");
        Num.Vector3 outward = Num.Vector3.Transform(Num.Vector3.UnitZ, mount.Rotation);
        await TeleportTo(mount.Position + outward * 1.6f);
        Face(mount.Position);
        await Hold(key, 5);
        var ghost = Client.Placement().PlanAt(Placeable(key)!, mount.Position, gate.Yaw);
        CompoundCheck(ghost is { Valid: true } && ghost.Support.Whole == Thing(gate.Id), key + " ghost selects this gate's lock socket");
        View.UseHeld();
        await Until(() => gate.Utility.Lock == kind && Count(key) == before, 5);
        CompoundCheck(gate.Utility.Lock == kind && Count(key) == before, key + " fits through held placement and spends one item");
        Me.HeldSlot = -1;
        if (kind == LockKind.Code)
        {
            Me.Interact(Thing(gate.Id), "set-code", code: "2580");
            await Until(() => (gate.Flags & (ViewerFlags.HasCode | ViewerFlags.Locked)) == (ViewerFlags.HasCode | ViewerFlags.Locked), 4);
            CompoundCheck((gate.Flags & ViewerFlags.HasCode) != 0, "gate code is set through the ordinary interaction intent");
        }
        else Me.Interact(Thing(gate.Id), "key-lock");
        await Until(() => (gate.Flags & ViewerFlags.Locked) != 0, 4);
        CompoundCheck((gate.Flags & ViewerFlags.Locked) != 0, key + " reports locked");
        await Seconds(.4);
        await Shot("compound-" + gate.Deployable(Client.Content)!.Key + "-" + key);
        Me.Interact(Thing(gate.Id), kind == LockKind.Code ? "code-unlock" : "key-unlock");
        await Until(() => (gate.Flags & ViewerFlags.Locked) == 0, 4);
        CompoundCheck((gate.Flags & ViewerFlags.Locked) == 0, key + " unlocks through the existing lock rules");
    }

    private async Task<Num.Vector3> CompoundApproachPort(RemoteThing thing, PowerPort port)
    {
        await CompoundApproach(thing);
        Num.Vector3 at = PowerPorts.Position(thing.Position, thing.Up, thing.Yaw, port);
        float reach = Me.HeldItem?.Wiring?.Reach ?? 0;
        if (Num.Vector3.Distance(Client.RenderEye, at) > reach)
        {
            Num.Vector3 away = at - thing.Position;
            away -= thing.Up * Num.Vector3.Dot(away, thing.Up);
            away = away.LengthSquared() > .01f ? Num.Vector3.Normalize(away) : Facing.Compass(thing.Up).North;
            await TeleportTo(at + away * 1.4f);
        }
        Face(at);
        await Settle();
        CompoundCheck(Num.Vector3.Distance(Client.RenderEye, at) <= reach, "physical " + port.Key + " port is within Wire Tool reach");
        return at;
    }

    private async Task CompoundWireIntent(WireOperation operation, RemoteThing thing, PowerPort port, Num.Vector3 point)
    {
        uint sequence = 0;
        OutcomeCode? result = null;
        void Answer(IntentAnswer answer) { if (answer.Seq == sequence) result = answer.Code; }
        Me.IntentAnswered += Answer;
        try
        {
            sequence = Me.Wire(operation, Thing(thing.Id), port.Id, point);
            await Until(() => result.HasValue, 5);
            CompoundCheck(result == OutcomeCode.Done, "Wire Tool " + operation + " accepted (" + (result?.ToString() ?? "timeout") + ")");
        }
        finally { Me.IntentAnswered -= Answer; }
    }

    private async Task<RemoteThing> CompoundPlace(string key, Num.Vector3 near)
    {
        var ground = Client.Placement().Ground!.Value;
        DeployableDef def = Client.Content.ItemByKey(key).Deploys!;
        for (int attempt = 0; attempt < 9; attempt++)
        {
            float angle = attempt * 2.4f, radius = attempt == 0 ? 0 : 2 + attempt * .5f;
            Num.Vector3 at = ground.Offset(near, MathF.Cos(angle) * radius, MathF.Sin(angle) * radius);
            Num.Vector3 stand = ground.Offset(at, 0, Math.Max(def.HalfExtents.Z, def.HalfExtents.X) + 1.5f);
            await TeleportTo(stand);
            Face(at);
            await Settle();
            var ghost = Client.Placement().PlanAt(Placeable(key)!, at, 0);
            if (ghost is not { Valid: true }) { GD.Print($"[compound-check] {key} attempt {attempt}: {ghost?.Outcome}"); continue; }
            int beforeCount = Count(key);
            var before = Client.Survival().Things.Select(t => t.Id).ToHashSet();
            Me.Place(ghost, ContainerRef.Main, SlotOf(Me.Inventory.Main, key)!.Value);
            await Until(() => Client.Survival().Things.Any(t => !before.Contains(t.Id) && t.Deployable(Client.Content)?.Key == key), 4);
            if (Client.Survival().Things.FirstOrDefault(t => !before.Contains(t.Id) && t.Deployable(Client.Content)?.Key == key) is not { } placed) continue;
            CompoundCheck(Count(key) == beforeCount - 1, key + " placement spends its inventory item");
            return placed;
        }
        throw new InvalidOperationException("compound: no valid ordinary placement for " + key);
    }

    private async Task CompoundApproach(RemoteThing thing, bool picture = false)
    {
        DeployableDef def = thing.Deployable(Client.Content)!;
        float distance = picture ? Math.Max(5, def.HalfExtents.Y * 2.2f) : def.HalfExtents.Z + 1.2f;
        var ground = Client.Placement().Ground!.Value;
        await TeleportTo(ground.Offset(thing.Position, 0, distance));
        Face(thing.Position + thing.Up * Math.Min(def.HalfExtents.Y, 1.5f));
        await Settle();
    }

    private async Task CompoundLoad(string key, int amount, int slot)
    {
        int before = Count(key);
        Admin("give", key + " " + amount);
        await Until(() => Count(key) >= before + amount, 5);
        CompoundCheck(Count(key) >= before + amount && Me.Loot != null, key + " grant replicated while the intended storage is open");
        int from = SlotOf(Me.Inventory.Main, key) ?? throw new InvalidOperationException("compound: missing inventory slot " + key);
        Me.MoveItem(ContainerRef.Main, from, ContainerRef.Loot, slot, amount);
        bool Loaded() => Me.Loot is { } loot && loot[slot].Item == Client.Content.ItemByKey(key).Id && loot[slot].Count >= amount;
        await Until(Loaded, 4);
        CompoundCheck(Loaded(), key + " is inside authoritative storage");
    }

    private static void CompoundCheck(bool pass, string message)
    {
        if (!pass) throw new InvalidOperationException("compound: " + message);
        GD.Print("[compound-check] " + message);
    }
}
