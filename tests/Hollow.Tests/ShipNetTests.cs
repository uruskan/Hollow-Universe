using System;
using System.Linq;
using System.Numerics;
using Hollow.Client;
using Hollow.Net.Protocol;
using Hollow.Simulation;
using Hollow.Simulation.Targets;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Nature;
using Hollow.Simulation.Physics;
using Hollow.Simulation.Players;
using Hollow.Simulation.Ships;
using Hollow.Surface;

namespace Hollow.Tests;

public static class ShipNetTests
{
    // A pilot's controls over a flight: lift off, climb, turn, cruise, boost, flight assist off and on, strafe, roll,
    // then down and a landing with the stick still moving.
    private static ShipInput Script(int t)
    {
        var input = new ShipInput();
        if (t < 60)
        {
            input.Vertical = 1;
        }
        else if (t < 480)
        {
            // Low and fast: a turn, a boost, a moment with flight assist off, a little pitch, a roll and back, strafing.
            input.Throttle = Math.Clamp((t - 60) / 120f, 0, 0.6f);
            input.Yaw = t is > 150 and < 260 ? 0.6f : 0;
            input.Pitch = t is > 300 and < 315 ? 0.3f : t is > 330 and < 345 ? -0.3f : 0;
            input.Roll = t is > 360 and < 390 ? 0.5f : t is > 395 and < 425 ? -0.5f : 0;
            input.Strafe = t is > 430 and < 470 ? 0.5f : 0;
            input.Vertical = t is > 270 and < 300 ? 0.4f : 0;
            input.Buttons = (t is > 200 and < 205 ? ShipButtons.Boost : 0) | (t is > 280 and < 295 ? ShipButtons.AssistOff : 0);
        }
        else
        {
            // Down and a landing, the stick still moving.
            input.Throttle = 0;
            input.Vertical = -1;
            input.Yaw = t % 90 < 30 ? 0.2f : 0;
        }
        return input;
    }

    /// <summary>
    /// GROUND.md §6.7: the pilot's client predicts its ship (and its seated character) to the bit, for a recorded flight
    /// through the ship's controls as the wire carries them: the client side is the real predictor on its own copy of the
    /// ground and trees; the server side is the zone.
    /// </summary>
    [Test]
    public static void PredictionMatchesTheServer()
    {
        using var rig = new ShipRig(content: true);
        Zone zone = rig.Zone;
        GameContent c = ContentFixture.Content;
        Ship ship = rig.Add(x: 30, z: -20, yaw: 0.4f);
        rig.Step(40);
        Vector3 hatch = ship.PointOf(ShipRig.Finch.HatchPoint());
        Vector3 side = Vector3.Normalize(Vector3.Transform(-Vector3.UnitX, ship.State.Rotation));
        Vector3 standAt = zone.Settle(hatch + side * 1.6f);
        Entity ada = zone.Players().AddPlayer(zone.NextId(), "Ada", zone.NewCharacter(standAt + zone.Frame.UpAt(standAt) * 0.02f));
        zone.Players().PlayerOf(ada)!.Vitals.Health = 100;
        zone.Ships().SetDoorAtOnce(ship, 0, 1);
        Assert.Equal(OutcomeCode.Done, zone.Interaction().Interact(ada, new TargetRef(TargetFamily.Ship, ship.Id.Value, ShipRig.Finch.HelmPart()), "board"), "boards");
        rig.Step(2);

        // The client: its own ground and trees, the character's predictor (which records the inputs) and the ship's.
        ISurface surface = new LevelSurface(ShipRig.Earthlike);
        using var predictor = new Predictor(zone.Spec, surface, null);
        ZoneFrame frame = predictor.Frame;
        _ = new NaturalBodies(predictor.World, new NaturalState(c, zone.Spec.CreateNature(surface, frame, c)));
        using var collider = new ShipCollider(predictor.World);
        var context = new ShipContext(frame, surface, ShipContext.GravityOf(zone.Spec.SurfaceGravity), (float)zone.Spec.AirDensity,
            (float)zone.Spec.AirScaleHeight, collider);
        var ships = new ShipPredictor(predictor, () => context);
        predictor.Seat = tick => ships.TryGetState(tick, out ShipState s) ? (0UL, s.PointOf(ShipRig.Finch.SeatPoint(), ShipRig.Finch.CentreOfMass), s.Velocity) : null;
        uint start = (uint)zone.Tick;
        zone.Entities.Get<Character>(ada).State.Health = 100; // as the vitals have it (set above)
        ships.Reset(ShipRig.Finch, 1, start, ship.State, start);
        predictor.Reset(start, zone.Entities.Get<Character>(ada).State, start);

        int differ = 0, characterDiffer = 0;
        float worst = 0, top = 0, highest = 0;
        for (uint tick = start + 1; tick <= start + 900; tick++)
        {
            int t = (int)(tick - start);
            ShipInput input = Script(t);
            InputCommand command = NetConvert.ToCommand(new MoveInput { View = Vector3.UnitZ }, new ActionInput { HeldSlot = ActionInput.NoSlot });
            command.Ship = new[] { ShipConvert.ToControls(input) };
            predictor.RecordInput(tick, command);
            ships.Predict(tick);
            predictor.Predict(tick);

            // The server's tick, with what the wire brought.
            ship.Input = ShipConvert.ToShipInput(command);
            rig.Step();
            Assert.Equal(tick, (uint)zone.Tick, "the zone's tick");
            ships.TryGetState(tick, out ShipState predicted);
            if (!predicted.Equals(ship.State))
            {
                differ++;
                worst = MathF.Max(worst, Vector3.Distance(predicted.Position, ship.State.Position));
                ships.Reconcile(ShipRig.Finch, 1, tick, ship.State, tick);
            }
            predictor.TryGetState(tick, out CharacterState seated);
            if (!seated.Equals(zone.Entities.Get<Character>(ada).State))
            {
                characterDiffer++;
                predictor.Reconcile(tick, zone.Entities.Get<Character>(ada).State);
            }
            top = MathF.Max(top, ship.State.Velocity.Length());
            highest = MathF.Max(highest, rig.Height(ship));
        }
        Fixtures.Report(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"900 ticks of flight: {differ} ship ticks and {characterDiffer} character ticks differed (worst {worst * 1000:0.000} mm); " +
            $"top speed {top:0} m/s, {highest:0} m up, landed {ship.State.Has(ShipFlags.Landed)}, fuel {ship.State.Fuel:0.0}"));
        Assert.Equal(0, differ, "the ship's prediction never differs");
        Assert.Equal(0, characterDiffer, "the seated pilot's prediction never differs");
        Assert.True(top > 60 && highest > 10, "it really flew");
        Assert.True(ship.State.Has(ShipFlags.Landed), "and landed");
    }

    /// <summary>
    /// GROUND.md §6.7: over the real network, a player boards a ship given by the server, takes off and flies; another
    /// player sees it fly (interpolated, its flags); on a clean line the pilot's prediction is never corrected.
    /// </summary>
    [Test]
    public static void OthersSeeItFly()
    {
        using var rig = new LoopbackRig();
        GameClient ada = rig.Join("Ada");
        GameClient bo = rig.Join("Bo");
        rig.WaitUntilPlaying();
        rig.Server.Execute("ship Ada");
        RemoteShip? seen = null;
        rig.Run(1.0, (_, _) => default);
        seen = ada.Ships().All.FirstOrDefault();
        Assert.True(seen?.Def != null, "Ada sees her ship");
        RemoteShip ship = seen!;
        // Ada walks to the hatch (the admin's teleport puts her beside it) and boards.
        Vector3 hatch = ship.PointOf(ship.Def!.HatchPoint());
        Vector3 out_ = Vector3.Normalize(Vector3.Transform(-Vector3.UnitX, ship.Rotation));
        Vector3 at = hatch + out_ * 1.6f;
        rig.Teleport("Ada", at);
        rig.Run(0.5, (_, _) => default);
        ada.Local.Interact(new TargetRef(TargetFamily.Ship, ship.Id.Value, ship.Def!.HelmPart()), "board");
        rig.Run(0.5, (_, _) => default);
        Assert.True(ada.Ships().Piloting, "Ada flies the ship (her client predicts it)");
        long corrections = ada.Stats.ShipCorrections;

        // Up, then forward and round.
        float startHeight = ship.Position.Length();
        rig.Run(4.0, (client, time) => client == ada
            ? new InputFrame { Ship = new ShipInput { Vertical = time < 2 ? 1 : 0, Throttle = time < 2 ? 0 : 0.4f, Yaw = 0.3f } }
            : default);
        RemoteShip? bosView = bo.Ships().All.FirstOrDefault(s => s.Id == ship.Id);
        Assert.True(bosView != null, "Bo sees the ship");
        Vector3 aboveGround = bosView!.Position;
        Fixtures.Report(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"Bo sees it {bosView.Position.Length() - startHeight:0.0} m up, going {bosView.Velocity.Length():0.0} m/s, engines {(bosView.Has(ShipNetFlags.EnginesOn) ? "on" : "off")}; " +
            $"Ada's ship corrections {ada.Stats.ShipCorrections - corrections}, predicted at {Vector3.Distance(ada.Ships().OwnShip.Position, bosView.Position):0.0} m from Bo's view"));
        Assert.True(bosView.Has(ShipNetFlags.Piloted) && bosView.Has(ShipNetFlags.EnginesOn), "Bo sees it piloted, engines on");
        Assert.True(bosView.Position.Length() - startHeight > 10, "Bo sees it climb");
        Assert.True(bosView.Velocity.Length() > 5, "and fly");
        Assert.Equal(0L, ada.Stats.ShipCorrections - corrections, "Ada's prediction held on a clean line");
        _ = aboveGround;
    }
}
