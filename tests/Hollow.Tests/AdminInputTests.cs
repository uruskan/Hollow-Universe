using System;
using System.Numerics;
using Hollow.Client;
using Hollow.Net.Commands;
using Hollow.Net.Protocol;
using Hollow.Simulation;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;

namespace Hollow.Tests;

public static class AdminInputTests
{
    [Test]
    public static void NoclipSpeedChangesPredictExactlyAndStayBounded()
    {
        ZoneSpec spec = ZoneSpec.StartingWorld(10, 20);
        var surface = new LevelSurface(spec.Radius);
        using var zone = new Zone(surface, spec);
        Entity entity = zone.AddCharacter(new EntityId(7), "admin", zone.NewCharacter(zone.GroundAt(0, 0)));
        zone.SetCheats(entity, MoveCheats.Noclip | MoveCheats.God);
        using var predictor = new Predictor(spec, surface, null);
        predictor.World.EnsureGround(zone.Entities.Get<Character>(entity).State.Position);
        predictor.Reset(0, zone.Entities.Get<Character>(entity).State, 0);
        float[] requests = [0, 1, 12.5f, 40, 200, 999, float.NaN, float.PositiveInfinity, -10];
        for (uint tick = 1; tick <= 180; tick++)
        {
            float requested = requests[(tick / 20) % requests.Length];
            bool fast = tick % 2 == 0;
            var input = new MoveInput
            {
                Forward = 1, View = Vector3.Normalize(new Vector3(0.6f, 0.4f, -0.3f)), NoclipSpeed = requested,
                Buttons = fast ? MoveButtons.Sprint : MoveButtons.None,
            };
            InputCommand command = NetConvert.ToCommand(input);
            Assert.True(command.FlySpeed <= 200, "wire speed clamped before prediction");
            zone.Entities.Get<Character>(entity).Input = NetConvert.ToMoveInput(command);
            zone.Step();
            predictor.RecordInput(tick, command);
            predictor.Predict(tick);
            CharacterState authoritative = zone.Entities.Get<Character>(entity).State;
            float speed = command.FlySpeed == 0 ? CharacterMotor.NoclipSpeed : command.FlySpeed;
            Assert.Near(speed * (fast ? 4 : 1), authoritative.Velocity.Length(), 0.001, "requested speed with sprint multiplier");
            Assert.True(predictor.Latest.Equals(authoritative), $"changing speed predicts identically at tick {tick}");
        }
        Assert.Equal(200f, NetConvert.ToMoveInput(new InputCommand { FlySpeed = 255 }).NoclipSpeed, "malicious wire byte is bounded too");
    }

    [Test]
    public static void FlightSpeedInputDoesNotGrantNoclipOrAccelerateWalking()
    {
        ZoneSpec spec = ZoneSpec.StartingWorld(10, 20);
        var surface = new LevelSurface(spec.Radius);
        using var zone = new Zone(surface, spec);
        Entity entity = zone.AddCharacter(new EntityId(8), "ordinary player", zone.NewCharacter(zone.GroundAt(0, 0)));
        using var predictor = new Predictor(spec, surface, null);
        predictor.World.EnsureGround(zone.Entities.Get<Character>(entity).State.Position);
        predictor.Reset(0, zone.Entities.Get<Character>(entity).State, 0);
        for (uint tick = 1; tick <= 120; tick++)
        {
            Vector3 up = zone.Frame.UpAt(zone.Entities.Get<Character>(entity).State.Position);
            InputCommand normal = NetConvert.ToCommand(new MoveInput { Forward = 1, View = Facing.View(0.3f, 0, up) });
            InputCommand requested = normal;
            requested.FlySpeed = 255;
            zone.Entities.Get<Character>(entity).Input = NetConvert.ToMoveInput(requested);
            zone.Step();
            predictor.RecordInput(tick, normal);
            predictor.Predict(tick);
            CharacterState authoritative = zone.Entities.Get<Character>(entity).State;
            Assert.True(!authoritative.Has(MoveCheats.Noclip), "client input cannot grant the server permission");
            Assert.True(predictor.Latest.Equals(authoritative), "for an ordinary player requested flight speed has no effect on walking");
        }
    }

    [Test]
    public static void ConsoleBindingsNormalizeChordsAndKeepServerCommandsOnTheServer()
    {
        var binds = new ConsoleBindings();
        Assert.True(binds.Set("shift+control+f6", "noclip", out _), "a command binds to a normalized modifier chord");
        Assert.Equal("noclip", binds.Find("CTRL+SHIFT+F6"), "event modifier order is canonical");
        Assert.True(binds.Find("F6") == null, "modifiers match exactly");
        Assert.True(binds.Set("wheelup", "noclipspeed 40", out _), "wheel alias accepted");
        Assert.Equal("noclipspeed 40", binds.Find("MWHEELUP"), "wheel dispatch uses the stored command");
        Assert.True(binds.Set("f6", "global.noclip on", out _), "admin commands can be assigned without locally granting permission");
        var command = ConsoleCatalog.Parse(binds.Find("F6")!).Command!;
        Assert.True(command.Admin && command.Side == CommandSide.Server, "binding preserves the server's permission path");
        Assert.True(!binds.Set("notakey", "noclip", out _) && !binds.Set("F7", "notacommand", out _), "invalid shortcuts report errors");
        Assert.True(binds.Remove("control+shift+f6") && binds.Find("CTRL+SHIFT+F6") == null, "unbind uses the same key normalization");
    }
}
