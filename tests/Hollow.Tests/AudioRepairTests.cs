using System;
using System.Numerics;
using System.IO;
using System.Linq;
using Hollow.Client.Audio;
using Hollow.Core;
using Hollow.Simulation.Building;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Physics;
using Hollow.Surface;

namespace Hollow.Tests;

public static class AudioRepairTests
{
    [Test]
    public static void DistinctActionsUseDedicatedNonemptyVariantSets()
    {
        var sounds = SoundCatalogData.Load(Path.Combine(Fixtures.Root, "game", "data", "ground", SoundCatalogData.FileName));
        foreach ((string item, string role, string expected) in new[]
        {
            ("medical-injector", "use", "survival.inject"), ("rocket-launcher", "shot", "launcher.shot"),
            ("rocket-launcher", "magOut", "launcher.open"), ("rocket-launcher", "magIn", "launcher.load"),
            ("rocket-launcher", "rack", "launcher.lock"), ("recycler", "start", "recycler.start"), ("recycler", "stop", "recycler.stop"),
        })
        {
            Assert.Equal(expected, sounds.ItemEvent(item, role), "dedicated action routing");
            SoundEventData sound = sounds.Events.Single(e => e.Key == expected);
            Assert.True(sound.Files.Count >= 3 && sound.Files.Distinct().Count() == sound.Files.Count, "actual recorded/mixed variants, not aliases");
        }
        SoundEventData loop = sounds.Events.Single(e => e.Key == sounds.ItemEvent("recycler", "loop"));
        Assert.True(loop.Loop && loop.Key == "recycler.loop" && loop.Files.Count > 0, "recycler uses a real continuous machinery loop");
    }

    [Test]
    public static void FootstepsUseRelativeVelocityOnPlanetsAndShipDecks()
    {
        foreach (Vector3 up in new[] { Vector3.UnitX, Vector3.UnitY, -Vector3.UnitZ })
        {
            Vector3 along = Vector3.Normalize(Vector3.Cross(up, up == Vector3.UnitY ? Vector3.UnitX : Vector3.UnitY));
            var state = new CharacterState { Position = up * 1200, Velocity = along * 3 - up * 2, Flags = MoveFlags.OnGround };
            FootInput input = Footsteps.Input(state, ZoneFrame.World, false, FootSurface.Grass, 0);
            Assert.Near(3, input.Speed, 0.0001, "horizontal speed at any latitude");
            Assert.Near(-2, input.VerticalSpeed, 0.0001, "radial descent");
        }
        var deck = new CharacterState { Frame = 17, Position = new Vector3(7, 2, -30), Velocity = new Vector3(3, -2, 0), Flags = MoveFlags.OnGround };
        FootInput aboard = Footsteps.Input(deck, ZoneFrame.World, false, FootSurface.Metal, 1000);
        Assert.Near(3, aboard.Speed, 0.0001, "ship-local movement uses deck up");
        Assert.Near(-2, aboard.VerticalSpeed, 0.0001, "ship-local vertical speed");
        Assert.Near(0, aboard.WaterDepth, 0, "interior coordinates are never planet water");
        deck.Stance = Stance.Seated;
        Assert.True(Footsteps.Input(deck, ZoneFrame.World, true, FootSurface.Metal, 0).Suppressed, "seated feet are silent");
        deck.Stance = Stance.Standing;
        deck.Cheats = MoveCheats.Noclip;
        Assert.True(Footsteps.Input(deck, ZoneFrame.World, true, FootSurface.Metal, 0).Suppressed, "noclip feet are silent");
    }

    [Test]
    public static void FootstepsDoNotPlayInAirOrReplaySeatLandings()
    {
        var cadence = new FootstepCadence();
        var input = new FootInput { Speed = 5, VerticalSpeed = -8, Surface = FootSurface.Rock };
        for (int i = 0; i < 60; i++) Assert.True(cadence.Update(1.0 / 60, input) == null, "airborne motion has no steps");
        input.Grounded = true;
        Assert.Equal(Footsteps.LandKey(FootSurface.Rock), cadence.Update(1.0 / 60, input)!.Value.Key, "a genuine fall lands");
        input.Grounded = false;
        cadence.Update(1, input);
        input.Suppressed = true;
        for (int i = 0; i < 60; i++) Assert.True(cadence.Update(1.0 / 60, input) == null, "seat/noclip suppressed");
        input.Suppressed = false;
        input.Grounded = true;
        input.Speed = 0;
        Assert.True(cadence.Update(1.0 / 60, input) == null, "standing from a seat has no old landing");
        input.Speed = 2;
        input.WaterDepth = 0.2;
        bool heard = false;
        for (int i = 0; i < 60; i++)
            if (cadence.Update(1.0 / 60, input) is { } step)
            {
                Assert.Equal("footstep.walk.water", step.Key, "shallow water splashes while walking");
                heard = true;
            }
        Assert.True(heard, "walking resumes");
    }

    [Test]
    public static void FootContactsResolveActualFloorGradesAndInlandWater()
    {
        ISurface surface = new LakeSurface();
        Assert.Near(0.3, FootstepEnvironment.WaterDepth(surface, ZoneFrame.World, new Vector3(1203.7f, 0, 0)), 0.001, "inland lake without a sea");
        Assert.Near(0, FootstepEnvironment.WaterDepth(surface, ZoneFrame.World, new Vector3(1205, 0, 0)), 0, "above lake is dry");
        var data = ContentFixture.Content.Building;
        foreach (Vector3 up in new[] { Vector3.UnitX, -Vector3.UnitY, Vector3.UnitZ })
        {
            using var physics = new PhysicsWorld(surface, ZoneFrame.World);
            ulong id = 0;
            using var building = new BuildingWorld(data, physics, () => new EntityId(++id));
            Vector3 origin = up * 1206;
            for (int grade = 0; grade < data.Grades.Count; grade++)
            {
                Vector3 axis = Vector3.Cross(up, up.X != 0 ? Vector3.UnitY : Vector3.UnitX);
                Vector3 at = origin + axis * (grade * 8);
                building.AddFirst(BuildingFrame.At(new Vector3D(at.X, at.Y, at.Z), new Vector3D(up.X, up.Y, up.Z), 0), data.Piece("foundation")!, grade, 1, 0);
                Assert.True(building.Physics!.Ray(at + up * 3, -up, 6, out RayHit hit), "real foundation collision");
                Assert.Equal(Footsteps.FromGrade(data.Grades[grade].Key), FootstepEnvironment.At(surface, ZoneFrame.World, building, hit.Point), "footsteps use the floor grade at any latitude");
            }
        }
    }

    [Test]
    public static void RealMovementHasNoLandingChatterAndOneSoundPerJump()
    {
        // Drive Jolt on a small planet, including gentle slopes and collision tile seams. Synthetic input alone
        // cannot establish whether real ground contact briefly drops and produces repeated landing sounds.
        foreach (ISurface surface in new ISurface[] { new LevelSurface(1200), new RollingAudioSurface() })
        {
            using var rig = new MovementRig(surface);
            rig.Run(new MoveInput(), 15);
            var cadence = new FootstepCadence();
            Vector3 view = Facing.View(0.65f, 0, rig.Zone.Frame.UpAt(rig.State.Position));
            foreach ((MoveButtons buttons, string gait) in new[]
            {
                (MoveButtons.None, "walk"), (MoveButtons.Sprint, "run"), (MoveButtons.Crouch, "crouch"),
            })
            {
                int steps = 0;
                for (int i = 0; i < 10 * CharacterMotor.TickRate; i++)
                {
                    FootEvent? heard = Tick(new MoveInput { Forward = 1, Buttons = buttons, View = view });
                    if (heard is not { } foot) continue;
                    Assert.True(foot.Key.StartsWith("footstep." + gait + ".", StringComparison.Ordinal),
                        $"{surface.GetType().Name}: ordinary {gait} must not emit a landing ({foot.Key})");
                    Assert.True((rig.State.Flags & MoveFlags.OnGround) != 0, "real movement steps require contact");
                    steps++;
                }
                Assert.True(steps >= 10, $"{surface.GetType().Name}: actual {gait} emits a sustained cadence");
            }
            for (int i = 0; i < CharacterMotor.TickRate; i++) Tick(new MoveInput { View = view });
            for (int jump = 0; jump < 3; jump++)
            {
                int landings = 0;
                for (int i = 0; i < 3 * CharacterMotor.TickRate; i++)
                {
                    FootEvent? heard = Tick(new MoveInput { View = view,
                        Buttons = i == 0 ? MoveButtons.Jump | MoveButtons.JumpPressed : MoveButtons.None });
                    if (heard is not { } foot) continue;
                    Assert.True(foot.Key.StartsWith("footstep.land.", StringComparison.Ordinal), "stationary jump has no walking footfalls");
                    landings++;
                }
                Assert.Equal(1, landings, $"{surface.GetType().Name}: a real jump produces exactly one landing");
            }

            FootEvent? Tick(MoveInput input)
            {
                Vector3 before = rig.Zone.Frame.UpAt(rig.State.Position);
                rig.Character.Input = input;
                rig.Zone.Step();
                view = Facing.Carry(view, before, rig.Zone.Frame.UpAt(rig.State.Position));
                FootSurface material = FootstepEnvironment.At(surface, rig.Zone.Frame, null, rig.State.Position);
                Assert.True(material != FootSurface.Metal && material != FootSurface.Wood, "natural ground is not a constructed deck");
                FootInput feet = Footsteps.Input(rig.State, rig.Zone.Frame, (rig.State.Flags & MoveFlags.Sprinting) != 0, material, 0);
                return cadence.Update(CharacterMotor.DeltaTime, feet);
            }
        }
    }

    private sealed class RollingAudioSurface : ISurface
    {
        public double Radius => 1200;
        public double SeaRadius => 0;
        public double HeightAt(Vector3D direction) => 3 + 0.6 * DMath.Sin(direction.X * Radius / 8) + 0.2 * DMath.Sin(direction.Z * Radius / 3);
    }

    private sealed class LakeSurface : ISurface
    {
        public double Radius => 1200;
        public double SeaRadius => 0;
        public double HeightAt(Vector3D direction) => 0;
        public double WaterLevelAt(Vector3D direction) => 4;
    }
}
