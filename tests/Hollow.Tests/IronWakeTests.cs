using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hollow.Simulation;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Physics;
using Hollow.Simulation.Players;
using Hollow.Simulation.Ships;

namespace Hollow.Tests;

/// <summary>
/// The Iron Wake on the first world's stand-in ground (IRON_WAKE.md §4, IW1): one parked at the landing site, its nose
/// along local north, and players walked about it by their inputs.
/// </summary>
public sealed class IronWakeRig : IDisposable
{
    public IronWakeRig(bool settle = true)
    {
        Game = new GameRig();
        Def = ContentFixture.Content.Ship("iron-wake")!;
        Ground = Site(Game.Zone, Def);
        // A clearing: nothing of the world's within its footprint and the walk round it.
        Game.Clear(Ground, Ground, 48);
        Ship = Zone.Ships().SpawnShip(Def, Ground, 0, EntityId.None, 2000);
        // Down onto its pads, then asleep: parked, a body in the zone's physics.
        for (int i = 0; settle && i < 20 * 30 && Ship.ParkedBody == 0; i++)
        {
            Step();
        }
    }

    public GameRig Game { get; }

    public Zone Zone => Game.Zone;

    public ShipDef Def { get; }

    public Ship Ship { get; private set; }

    public Vector3 Ground { get; }

    /// <summary>The ship's frame's key (its entity ID): what a character aboard has as its frame.</summary>
    public ulong Key => Ship.Id.Value;

    public ShipPose Pose => ShipFrame.PoseOf(Ship.State, Def.CentreOfMass);

    // The flattest spot near the landing site the ship's footprint finds clear.
    private static Vector3 Site(Zone zone, ShipDef def)
    {
        Vector3 best = zone.GroundAt(0, 0);
        float spread = float.MaxValue;
        for (int i = -6; i <= 6; i++)
        {
            for (int j = -6; j <= 6; j++)
            {
                Vector3 g = zone.GroundAt(i * 20, j * 20);
                float lo = float.MaxValue, hi = float.MinValue;
                for (int k = 0; k < 12; k++)
                {
                    float a = k * MathF.PI / 6;
                    foreach (float r in new[] { 10f, 20f, 30f })
                    {
                        Vector3 p = zone.GroundAt(i * 20 + r * MathF.Cos(a), j * 20 + r * MathF.Sin(a));
                        float height = Vector3.Dot(p - g, zone.Frame.UpAt(g));
                        lo = MathF.Min(lo, height);
                        hi = MathF.Max(hi, height);
                    }
                }
                if (zone.Surface.SeaRadius > 0 && zone.Frame.ToBody(g).Length < zone.Surface.SeaRadius + 2)
                {
                    continue;
                }
                if (hi - lo < spread)
                {
                    spread = hi - lo;
                    best = g;
                }
            }
        }
        return best;
    }

    /// <summary>A player standing on the ground at a point of the ship's frame, looking along a direction of the ship's frame.</summary>
    public Entity Add(string name, Vector3 local, Vector3 looking)
    {
        Vector3 world = Pose.ToWorld(local);
        Vector3 ground = Zone.Settle(world) + Zone.Frame.UpAt(world) * 0.02f;
        Entity e = Zone.Players().AddPlayer(Zone.NextId(), name, Zone.NewCharacter(ground));
        Zone.Players().PlayerOf(e)!.Vitals.Health = 100;
        ref Character c = ref Zone.Entities.Get<Character>(e);
        c.Input.View = Vector3.Normalize(Pose.DirectionToWorld(looking));
        return e;
    }

    public ref Character Ch(Entity e) => ref Zone.Entities.Get<Character>(e);

    /// <summary>Walks a character along a direction of the ship's frame for some ticks (its view given in the frame it is in).</summary>
    public void Walk(Entity e, Vector3 along, int ticks, bool sprint = false, Action? each = null)
    {
        for (int i = 0; i < ticks; i++)
        {
            ref Character c = ref Ch(e);
            c.Input.Forward = 1;
            c.Input.Buttons = sprint ? MoveButtons.Sprint : MoveButtons.None;
            c.Input.Frame = c.State.Frame;
            c.Input.View = c.State.Frame == 0 ? Vector3.Normalize(Pose.DirectionToWorld(along)) : along;
            each?.Invoke();
            Step();
        }
        Ch(e).Input.Forward = 0;
    }

    public void Step(int ticks = 1)
    {
        for (int i = 0; i < ticks; i++)
        {
            Zone.UnixMilliseconds += 1000 / CharacterMotor.TickRate;
            Zone.Step();
            Zone.Events.Clear();
            Zone.Ships().ShipEvents.Clear();
        }
    }

    public void Dispose() => Game.Dispose();
}

public static class IronWakeTests
{
    /// <summary>IRON_WAKE.md §4.3: the Iron Wake's data makes a space: every piece a convex hull, its decks where the import put them.</summary>
    [Test]
    public static void TheSpaceIsTheShip()
    {
        using var rig = new IronWakeRig();
        ShipSpace space = rig.Ship.Space!;
        Fixtures.Report($"{space.Interior.Pieces.Count} pieces, {space.FailedPieces} refused; {space.Interior.Parts.Count} parts, " +
            $"{space.Interior.Boarding.Count} boarding boxes, reach {space.Interior.Reach:0.0} m; parked body {rig.Ship.ParkedBody}, asleep {rig.Ship.State.Asleep}");
        Assert.Equal(0, space.FailedPieces, "every piece is a convex hull");
        Assert.True(rig.Ship.ParkedBody != 0, "parked: a body in the zone's physics");
        lock (JoltRuntime.Gate)
        {
            Assert.True(space.Ray(new Vector3(0, 5, 0), -Vector3.UnitY, 4, out SpaceHit deck), "Deck A under the hold");
            Fixtures.Report($"Deck A at {deck.Point.Y:0.000} m (part {deck.Part})");
            Assert.Near(3.2f, deck.Point.Y, 0.02f, "Deck A's floor");
            Assert.True(space.Ray(new Vector3(0, 4, -22), -Vector3.UnitY, 5, out SpaceHit ramp), "the open ramp behind the stern");
            Fixtures.Report($"the ramp at z -22 is {ramp.Point.Y:0.000} m up (part {space.Interior.Parts[ramp.Part].Name})");
        }
    }

    /// <summary>IRON_WAKE.md §4.5: walked from the ground up the parked ship's open ramp, a player is handed over onto the deck.</summary>
    [Test]
    public static void WalkUpTheRamp()
    {
        using var rig = new IronWakeRig();
        Entity ada = rig.Add("Ada", new Vector3(0, 0, -28), Vector3.UnitZ);
        rig.Step(10);
        long aboardAt = -1;
        rig.Walk(ada, Vector3.UnitZ, 30 * 6, each: () =>
        {
            if (aboardAt < 0 && rig.Ch(ada).State.Frame == rig.Key)
            {
                aboardAt = rig.Zone.Tick;
            }
        });
        CharacterState s = rig.Ch(ada).State;
        Fixtures.Report($"aboard at tick {aboardAt}; now {s} in frame {s.Frame:X} (ship {rig.Key:X})");
        Assert.True(s.Frame == rig.Key, "aboard");
        Assert.Near(3.2f, s.Position.Y, 0.05f, "on Deck A");
    }
}
