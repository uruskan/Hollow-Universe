using System;
using System.Numerics;
using Hollow.Core;
using Hollow.Persistence;
using Hollow.Simulation;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Simulation.Ships;
using Hollow.Surface;

namespace Hollow.Tests;

public static class SurvivalRepairTests
{
    [Test]
    public static void GodModeGuardsDirectDeathAndRecoversWoundedPlayers()
    {
        using var rig = new GameRig();
        Entity ada = rig.Add("Ada", 0, 0, kit: true);
        Player p = rig.P(ada);
        rig.Zone.Wounded().Wound(ada, p, "Bleeding", EntityId.None, "");
        rig.Zone.SetCheats(ada, MoveCheats.God);
        Assert.True(!p.Wounded, "enabling god mode recovers an existing wounded state");
        float health = p.Vitals.Health;
        rig.Zone.Players().Die(ada, p, "Went down with the ship", EntityId.None, "");
        rig.Zone.Players().Hurt(ada, p, 100000, "Burned", EntityId.None, "", canWound: false);
        Assert.True(!p.Dead && rig.Ch(ada).State.Health > 0, "both direct death and ordinary damage respect immunity");
        Assert.Near(health, p.Vitals.Health, 0.001, "no damage");
        Assert.True(!p.Inventory.Belt[0].IsEmpty, "death refusal does not empty inventory");
        rig.Zone.SetCheats(ada, MoveCheats.None);
        rig.Zone.Players().Die(ada, p, "Tests", EntityId.None, "");
        Assert.True(p.Dead, "ordinary death still works when god mode is disabled");
    }

    [Test]
    public static void WreckReleasesGodModePilotAlive()
    {
        using var rig = new IronWakeRig();
        Entity ada = rig.Pilot("Ada");
        rig.Zone.SetCheats(ada, MoveCheats.God);
        rig.Zone.Ships().DamageShip(rig.Ship, 1e9f, ShipDamage.Explosive, null);
        Assert.True(rig.Ship.Destroyed && !rig.Game.P(ada).Dead, "wreck destroys ship without killing immune pilot");
        Assert.True(!rig.Zone.Entities.Has<ShipSeat>(ada), "survivor has no stale seat");
        rig.Step(90);
        Assert.True(!rig.Game.P(ada).Dead && rig.Ch(ada).State.Frame == 0, "survivor continues simulation outside destroyed ship");
    }

    [Test]
    public static void BreathRunsOutRecoversAndPersists()
    {
        var s = ContentFixture.Content.Survival;
        Vitals v = new() { Health = 100 };
        Assert.Near(0, Breathing.Step(ref v, s, false, s.BreathSeconds), 0.001, "reserve protects the initial breath hold");
        Assert.Near(s.SuffocationDamagePerSecond, Breathing.Step(ref v, s, false, 1), 0.001, "then absent air causes damage");
        Assert.True(v.Suffocating, "exhausted reserve is visible");
        Breathing.Step(ref v, s, true, 1);
        Assert.True(!v.Suffocating && v.BreathUsed < s.BreathSeconds, "air immediately stops damage and refills reserve");
        var p = new Player(new EntityId(77), "Ada") { Vitals = v };
        byte[] bytes = WorldCodec.EncodePlayer(p);
        Player restored = WorldCodec.DecodePlayer(bytes, p.Id, p.Name, ContentFixture.Content);
        Assert.Near(v.BreathUsed, restored.Vitals.BreathUsed, 0.001, "reconnecting preserves used reserve");
        byte[] legacy = bytes[..^4];
        legacy[0] = 5;
        Assert.Near(0, WorldCodec.DecodePlayer(legacy, p.Id, p.Name, ContentFixture.Content).Vitals.BreathUsed, 0.001, "old saves begin with a full reserve");
    }

    private sealed class LakeSurface : ISurface
    {
        public double Radius => 1200;
        public double SeaRadius => 0;
        public double HeightAt(Vector3D direction) => 2;
        public double WaterLevelAt(Vector3D direction) => 8;
    }

    [Test]
    public static void InlandWaterUsesEyeDepthAndDrivesSwimming()
    {
        ISurface lake = new LakeSurface();
        foreach (Vector3D up in new[] { Vector3D.UnitX, Vector3D.UnitY, Vector3D.UnitZ, -Vector3D.UnitX })
        {
            Assert.True(Breathing.Underwater(lake, up * 1207), "head below inland lake surface");
            Assert.True(!Breathing.Underwater(lake, up * 1209), "head above lake can breathe");
        }
        var frame = new ZoneMoveFrame(ZoneFrame.World, 0, 9.81f, 1200, surface: lake);
        Assert.True(frame.Submersion(new Vector3(1205, 0, 0), new MovementTuning()) > 0.99f, "lake swimming works without an ocean");
    }

    [Test]
    public static void DrowningKillsAfterReserveAndGodModeSurvives()
    {
        using var rig = new GameRig(spec => new LevelSurface(spec.Radius, -8, spec.Radius));
        Entity ada = rig.Add("Ada", 0, 0);
        Entity bo = rig.Add("Bo", 3, 0);
        rig.Zone.SetCheats(bo, MoveCheats.God);
        void Dive()
        {
            rig.Ch(ada).Input.Buttons = MoveButtons.Crouch;
            rig.Ch(bo).Input.Buttons = MoveButtons.Crouch;
        }
        rig.Step(15 * 30, Dive);
        Assert.True(!rig.P(ada).Dead && rig.P(ada).Vitals.BreathUsed > 10, "submerged player uses reserve first");
        rig.Step(25 * 30, Dive);
        Assert.True(rig.P(ada).Dead && rig.P(ada).DeathCause == "Drowned", "continued submersion drowns normally");
        Assert.True(!rig.P(bo).Dead, "drowning obeys god mode too");
    }

    [Test]
    public static void FoundationProtectsPlayerFromAnimalBites()
    {
        using var rig = new GameRig();
        Entity ada = rig.Add("Ada", 0, 0);
        var wolf = AnimalRig.Herd(rig, "wolf", 0, -4, 1).Leader!;
        var foundation = BuildingNetTests.Foundation(rig.Zone, rig.Zone.GroundAt(0, 0), rig.P(ada).Id.Value);
        Vector3 basePoint = rig.Zone.GroundAt(0, 0), up = rig.Zone.Frame.UpAt(basePoint);
        rig.Zone.Teleport(ada, basePoint + up * 3);
        rig.Step(90);
        Assert.True(!rig.Zone.Animals().CanAttackPlayer(wolf, rig.P(ada).Id), "player standing on foundation is unreachable to ground animal");
        float health = rig.P(ada).Vitals.Health;
        Assert.True(!rig.Zone.Animals().AnimalAttacksPlayer(wolf, rig.P(ada).Id), "attack is rejected again at damage time");
        Assert.Near(health, rig.P(ada).Vitals.Health, 0.001, "no bite through foundation");
    }
}
