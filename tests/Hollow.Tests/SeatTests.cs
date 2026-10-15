using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hollow.Client;
using Hollow.Net.Protocol;
using Hollow.Simulation;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Interaction;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Targets;

namespace Hollow.Tests;

/// <summary>Seats, parts and doors (IRON_WAKE.md §5, IW2): who sits where, every way a seat is freed, and ships at rest.</summary>
public static class SeatTests
{
    /// <summary>
    /// The F1-kill finding (KNOWN_PROBLEMS.md): a ship with nobody at its helm comes to rest and sleeps wherever it is left,
    /// on its pads, on its hull beside them on uneven ground, on its side or afloat. An Iron Wake given to a player on
    /// each spot of a grid round the landing site (the admin's spawn: its footprint clear, trees and rocks in the way gone)
    /// parks within 25 s; so does a Tin Finch dropped on its side. Before, 13 of these 49 Iron Wakes jittered on their
    /// contacts for ever, never slept, never parked, and a client (which knew a ship at rest only by its sleep) offered
    /// nothing at them but "land first".
    /// </summary>
    [Test]
    public static void AShipLeftAloneComesToRest()
    {
        using var game = new GameRig();
        Zone zone = game.Zone;
        ShipDef wake = ContentFixture.Content.Ship("iron-wake")!, finch = ContentFixture.Content.Ship("tin-finch")!;
        int tried = 0, parked = 0, slowest = 0;
        var restless = new List<string>();
        for (int i = -3; i <= 3; i++)
        {
            for (int j = -3; j <= 3; j++)
            {
                Vector3 ground = zone.GroundAt(i * 90, j * 90);
                float yaw = (i * 7 + j * 3) * 0.37f;
                zone.Ships().ClearNatureFor(wake, ground, yaw);
                if (!zone.Ships().ShipSpaceClear(wake, ground, yaw))
                {
                    continue;
                }
                tried++;
                Ship ship = zone.Ships().SpawnShip(wake, ground, yaw, EntityId.None, 100);
                int t = 0;
                for (; t < 30 * 25 && ship.ParkedBody == 0; t++)
                {
                    game.Step();
                }
                if (ship.ParkedBody != 0)
                {
                    parked++;
                    slowest = Math.Max(slowest, t);
                }
                else
                {
                    restless.Add($"({i * 90}, {j * 90}): {ship.State}");
                }
                zone.Ships().RemoveShip(ship);
            }
        }

        // A Tin Finch on its side: rolled a quarter turn, let go a metre over the ground.
        Vector3 at = zone.GroundAt(15, 25);
        zone.Ships().ClearNatureFor(finch, at, 0);
        Ship side = zone.Ships().SpawnShip(finch, at, 0, EntityId.None, 100);
        Vector3 up = zone.Frame.UpAt(at);
        side.State.Rotation = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.Normalize(side.State.Forward), MathF.PI / 2) * side.State.Rotation);
        side.State.Position += up * 1.5f;
        int fell = 0;
        for (; fell < 30 * 25 && side.ParkedBody == 0; fell++)
        {
            game.Step();
        }
        float lean = MathF.Acos(Math.Clamp(Vector3.Dot(side.State.Up, up), -1, 1)) * 180 / MathF.PI;
        Fixtures.Report($"{parked} of {tried} Iron Wakes parked, the slowest after {slowest / 30.0:0.0} s; the Tin Finch on its side ({lean:0}° from upright) " +
            $"{(side.ParkedBody != 0 ? $"parked after {fell / 30.0:0.0} s" : "never parked")}" + string.Concat(restless.Select(r => "\n  restless at " + r)));
        Assert.True(tried > 30, "the grid has room for the ship");
        Assert.Equal(tried, parked, "every Iron Wake left alone comes to rest and parks");
        Assert.True(side.ParkedBody != 0 && lean > 45, "a Tin Finch left on its side rests on its side, and parks");
    }

    /// <summary>
    /// The F1-kill finding, over the network as the owner played it: Ada flies her Tin Finch, kills herself with the F1
    /// console's <c>kill</c> in mid-air, and respawns. On the server and in her client nothing of the seat is left (the
    /// seat component, the ship's pilot, the seated stance, her client's piloting and its seat). An Iron Wake given beside
    /// her where she arrived comes to rest; at every part of it her client offers what the server would, for the same
    /// reasons (the server's word on a ship at rest, <c>ShipNetFlags.Still</c>): never "land first".
    /// </summary>
    [Test]
    public static void KilledInFlightAndRespawned()
    {
        using var rig = new LoopbackRig();
        GameClient ada = rig.Join("Ada");
        rig.WaitUntilPlaying();
        rig.Server.Execute("ship Ada");
        rig.Run(1.0, (_, _) => default);
        RemoteShip finch = ada.Ships().All.First();
        Vector3 hatch = finch.PointOf(finch.Def!.Hatch);
        rig.Teleport("Ada", hatch + Vector3.Normalize(Vector3.Transform(-Vector3.UnitX, finch.Rotation)) * 1.6f);
        rig.Run(0.5, (_, _) => default);
        ada.Local.Interact(new TargetRef(TargetFamily.Ship, finch.Id.Value, ShipParts.Hatch), "board");
        rig.Run(0.5, (_, _) => default);
        Assert.True(ada.Ships().Piloting, "Ada flies her Tin Finch");
        rig.Run(2.5, (_, _) => new InputFrame { Ship = new ShipInput { Vertical = 1 } });

        ada.Console().SendConsole("kill");
        rig.Run(1.0, (_, _) => new InputFrame { Ship = new ShipInput { Vertical = 1 } });
        Assert.True(ada.Local.IsDead && !ada.Ships().Piloting, "dead, and her client no longer flies");
        ada.Local.Respawn();
        rig.Run(1.5, (_, _) => default);
        CharacterState server = rig.Server.Inspect(sv =>
        {
            sv.Zone.Entities.TryFind(ada.EntityId, out Entity e);
            Assert.True(sv.Zone.Ships().SeatOf(e) == null, "no seat component left on her character");
            Assert.True(sv.Zone.Ships().All.All(s => s.Pilot != ada.EntityId), "no ship has her as its pilot");
            return sv.Zone.Entities.Get<Character>(e).State;
        });
        Fixtures.Report($"respawned: on the server {server}; her client {ada.Character}, piloting {ada.Ships().Piloting}, doer {ada.Interaction().Doer.State}");
        Assert.True(server.Stance != Stance.Seated && server.Frame == 0 && !server.IsDead, "on the server: alive, standing, in the zone's frame");
        Assert.True(!ada.Ships().Piloting && ada.Character.Stance != Stance.Seated && ada.Interaction().Doer.State == DoerState.None,
            "in her client: on foot, not seated, nothing held against her");

        // An Iron Wake beside her: it comes to rest, and her client offers what the server would.
        rig.Server.Execute("ship Ada iron-wake");
        var wait = System.Diagnostics.Stopwatch.StartNew();
        RemoteShip? wake = null;
        do
        {
            rig.Run(0.25, (_, _) => default);
            wake = ada.Ships().All.FirstOrDefault(s => s.Def?.Interior != null);
        }
        while ((wake == null || !wake.Has(ShipNetFlags.Asleep)) && wait.Elapsed.TotalSeconds < 25);
        Assert.True(wake != null && wake.Has(ShipNetFlags.Asleep) && wake.Has(ShipNetFlags.Still), "the Iron Wake came to rest, and her client is told so");
        int compared = 0;
        for (byte part = 0; part < ShipParts.Count; part++)
        {
            var target = new TargetRef(TargetFamily.Ship, wake!.Id.Value, part);
            var mine = new List<InteractOption>();
            if (!ada.Interaction().Options(target, wake.PointOf(wake.Def!.PartPoint(part)), mine, out _))
            {
                continue;
            }
            List<InteractOption> theirs = rig.Server.Inspect(sv =>
            {
                sv.Zone.Entities.TryFind(ada.EntityId, out Entity e);
                var list = new List<InteractOption>();
                sv.Zone.Interaction().Options(e, target, list);
                return list;
            });
            foreach (InteractOption option in mine)
            {
                InteractOption? server_ = InteractionRules.Find(theirs, option.Verb);
                Assert.True(server_ != null && server_.Value.Reason.Code == option.Reason.Code,
                    $"part {part}, {ada.Content.Interactions.Verb(option.Verb)?.Key}: her client says {option.Reason}, the server {server_?.Reason}");
                Assert.True(option.Reason.Code is not (OutcomeCode.NotLanded or OutcomeCode.ShipMoving), "nothing says it isn't at rest");
                compared++;
            }
        }
        Fixtures.Report($"the Iron Wake asleep after {wait.Elapsed.TotalSeconds:0.0} s; {compared} options compared with the server's: the same");
        Assert.True(compared > 0, "options were compared");
    }
}
