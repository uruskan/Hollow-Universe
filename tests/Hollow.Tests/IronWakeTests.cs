using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Hollow.Simulation;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Physics;
using Hollow.Simulation.Players;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Targets;

namespace Hollow.Tests;

/// <summary>
/// The Iron Wake on the first world's stand-in ground (IRON_WAKE.md §4, IW1): one parked near the landing site, its nose
/// along local north, and players walked about it by their inputs, or put aboard where a test needs them.
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

    /// <summary>The pads' height over the ground (the model's pivot is between them).</summary>
    public float Height => Zone.HeightAboveGround(Ship.Origin);

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
        Game.P(e).Inventory.Clear();
        Game.P(e).Vitals.Health = 100;
        ref Character c = ref Zone.Entities.Get<Character>(e);
        c.Input.View = Vector3.Normalize(Pose.DirectionToWorld(looking));
        return e;
    }

    /// <summary>A player standing aboard, on a deck at a point of the ship's frame, looking along a direction of its frame.</summary>
    public Entity Aboard(string name, Vector3 local, Vector3 looking)
    {
        CharacterState state = Zone.NewCharacter(local + Vector3.UnitY * 0.02f);
        state.Frame = Key;
        Entity e = Zone.Players().AddPlayer(Zone.NextId(), name, state);
        Game.P(e).Inventory.Clear();
        Game.P(e).Vitals.Health = 100;
        Ch(e).Input = new MoveInput { View = Vector3.Normalize(looking), Frame = Key };
        return e;
    }

    /// <summary>A pilot: aboard on the bridge's dais beside the helm, on its list, then into its seat with its verb (take the helm).</summary>
    public Entity Pilot(string name)
    {
        Entity e = Aboard(name, new Vector3(-1.75f, 9.1f, 13.9f), Vector3.UnitZ);
        Step(3);
        var helm = new TargetRef(TargetFamily.Ship, Ship.Id.Value, Def.HelmPart());
        if (!Ship.IsAuthorised(Game.P(e).Id.Value))
        {
            // The helm is the ship's tool cupboard (IRON_WAKE.md §5.2): on its list first, as at a cupboard.
            Assert.Equal(OutcomeCode.Done, Zone.Interaction().Interact(e, helm, "authorise").Code, $"{name} authorises at the helm");
        }
        Outcome boarded = Zone.Interaction().Interact(e, helm, Def.Helm.VerbKey);
        Assert.Equal(OutcomeCode.Done, boarded.Code, $"{name} takes the helm");
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
            Step();
            each?.Invoke();
        }
        Ch(e).Input.Forward = 0;
        Ch(e).Input.Buttons = MoveButtons.None;
    }

    /// <summary>The pilot's lift toward a height of the pads over the ground: up or down by how far off it is, slowing as it nears.</summary>
    public ShipInput HoldHeight(float height)
    {
        float climbing = Vector3.Dot(Ship.State.Velocity, Zone.Frame.UpAt(Ship.State.Position));
        return new ShipInput { Vertical = Math.Clamp((height - Height) * 0.12f - climbing * 0.05f, -0.6f, 0.6f) };
    }

    /// <summary>Flies the ship by its pilot's controls for some ticks (<paramref name="input"/> of the tick's index).</summary>
    public void Fly(int ticks, Func<int, ShipInput> input, Action<int>? each = null)
    {
        for (int i = 0; i < ticks; i++)
        {
            Ship.Input = input(i);
            Step();
            each?.Invoke(i);
        }
    }

    /// <summary>Steps ticks; <paramref name="seen"/> looks at each tick's events before they are cleared.</summary>
    public void Step(int ticks = 1, Action? seen = null)
    {
        for (int i = 0; i < ticks; i++)
        {
            Zone.UnixMilliseconds += 1000 / CharacterMotor.TickRate;
            Zone.Step();
            seen?.Invoke();
            Zone.Events.Clear();
            Zone.Ships().ShipEvents.Clear();
        }
    }

    public void Dispose() => Game.Dispose();
}

public static class IronWakeTests
{
    private const float Grid = 1f / 256;

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

    /// <summary>
    /// IRON_WAKE.md §4.5, §4.6: walked from the ground up the parked ship's open ramp, a player is handed over onto the
    /// deck; walked back down, onto the ground behind it, back into the zone's frame. Both hand-overs keep the feet where
    /// they were in the world.
    /// </summary>
    [Test]
    public static void WalkUpTheRampAndDownAgain()
    {
        using var rig = new IronWakeRig();
        Entity ada = rig.Add("Ada", new Vector3(0, 0, -28), Vector3.UnitZ);
        rig.Step(10);
        long aboardAt = -1;
        float jump = 0;
        Vector3 was = rig.Zone.WorldOf(rig.Ch(ada).State);
        void Watch()
        {
            CharacterState now = rig.Ch(ada).State;
            Vector3 world = rig.Zone.WorldOf(now);
            jump = MathF.Max(jump, Vector3.Distance(world, was));
            was = world;
            if (aboardAt < 0 && now.Frame == rig.Key)
            {
                aboardAt = rig.Zone.Tick;
            }
        }
        rig.Walk(ada, Vector3.UnitZ, 30 * 6, each: Watch);
        float up = jump;
        jump = 0;
        CharacterState s = rig.Ch(ada).State;
        Fixtures.Report($"aboard at tick {aboardAt}; now {s} in frame {s.Frame:X} (ship {rig.Key:X}); the most the feet moved in a tick {up:0.000} m");
        Assert.True(s.Frame == rig.Key, "aboard");
        Assert.Near(3.2f, s.Position.Y, 0.05f, "on Deck A");
        Assert.True(up < 0.2f, "the hand-over keeps the feet where they were (a walk's tick, no jump)");

        long left = rig.Zone.Frames.Left;
        rig.Walk(ada, -Vector3.UnitZ, 30 * 7, each: Watch);
        s = rig.Ch(ada).State;
        Vector3 local = rig.Pose.ToLocal(rig.Zone.WorldOf(s));
        Fixtures.Report($"walked back down: {s} in frame {s.Frame:X}, at z {local.Z:0.0} in the ship's frame, {rig.Zone.HeightAboveGround(s.Position):0.00} m over the ground; " +
            $"the most the feet moved in a tick {jump:0.000} m");
        Assert.True(s.Frame == 0 && rig.Zone.Frames.Left == left + 1, "off the ramp's foot, back in the zone's frame");
        Assert.True(local.Z < -26 && MathF.Abs(rig.Zone.HeightAboveGround(s.Position)) < 0.1f, "standing on the ground behind the ramp");
        Assert.True(jump < 0.2f, "the way back keeps the feet too");
    }

    /// <summary>
    /// IRON_WAKE.md §4.2, §14: someone walks up the ramp and stands on Deck A; a pilot takes the helm, lifts off, climbs,
    /// turns and brakes. The passenger's place on the deck never moves (the ship's flight never enters their step) while
    /// the ship carries them through the world.
    /// </summary>
    [Test]
    public static void TakeOffWithSomeoneOnTheDeck()
    {
        using var rig = new IronWakeRig();
        Entity ada = rig.Add("Ada", new Vector3(0, 0, -28), Vector3.UnitZ);
        rig.Step(10);
        rig.Walk(ada, Vector3.UnitZ, 30 * 7);
        Assert.True(rig.Ch(ada).State.Frame == rig.Key, "Ada walked aboard");
        rig.Step(30); // come to rest
        Entity bo = rig.Pilot("Bo");
        CharacterState start = rig.Ch(ada).State;
        Vector3 nose = rig.Pose.DirectionToWorld(Vector3.UnitZ), up = rig.Zone.Frame.UpAt(rig.Ship.State.Position);
        float startHeight = rig.Height, startYaw = Facing.YawOf(nose, up);
        float drift = 0, top = 0, highest = 0, carried = 0;
        Vector3 ground = rig.Zone.WorldOf(start);
        // Lift for 5 s; climb, go forward and turn for 8 s; then let flight assist brake for 15 s.
        rig.Fly(30 * 28, i => i < 150 ? new ShipInput { Vertical = 1 }
            : i < 390 ? new ShipInput { Vertical = 0.3f, Throttle = 0.5f, Yaw = 0.6f }
            : default, i =>
        {
            CharacterState s = rig.Ch(ada).State;
            Assert.True(s.Frame == rig.Key, $"Ada aboard at tick {i}");
            drift = MathF.Max(drift, Vector3.Distance(s.Position, start.Position));
            top = MathF.Max(top, rig.Ship.State.Velocity.Length());
            highest = MathF.Max(highest, rig.Height);
            carried = MathF.Max(carried, Vector3.Distance(rig.Zone.WorldOf(s), ground));
        });
        float turned = (Facing.YawOf(rig.Pose.DirectionToWorld(Vector3.UnitZ), rig.Zone.Frame.UpAt(rig.Ship.State.Position)) - startYaw) * 180 / MathF.PI;
        turned = (turned % 360 + 540) % 360 - 180;
        Fixtures.Report(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"climbed {highest - startHeight:0.0} m, turned {turned:0.0}°, top speed {top:0.0} m/s, now {rig.Ship.State.Velocity.Length():0.00} m/s; " +
            $"Ada carried {carried:0.0} m through the world, her place on the deck moved {drift * 1000:0.000} mm; Bo {rig.Ch(bo).State.Stance}"));
        Assert.True(highest - startHeight > 30 && MathF.Abs(turned) > 20 && top > 8, "the ship lifted off, climbed, went and turned");
        Assert.True(rig.Ship.State.Velocity.Length() < 1.5f, "and braked");
        Assert.True(drift <= Grid, "Ada's place on the deck didn't move (to the wire's 1/256 m)");
        Assert.True(carried > 30, "while the ship carried her");
    }

    /// <summary>IRON_WAKE.md §14: jumps on the deck of a ship climbing and turning land back on the deck, aboard, unhurt.</summary>
    [Test]
    public static void JumpOnAFlyingDeck()
    {
        using var rig = new IronWakeRig();
        Entity ada = rig.Aboard("Ada", new Vector3(1.5f, 3.2f, 2), Vector3.UnitZ);
        rig.Pilot("Bo");
        rig.Step(10);
        float deck = rig.Ch(ada).State.Position.Y, peak = 0;
        int jumps = 0, landings = 0, damage = 0;
        bool wasUp = false;
        rig.Fly(30 * 10, i =>
        {
            ref Character c = ref rig.Ch(ada);
            bool press = i is 120 or 180 or 240;
            c.Input.Buttons = press ? MoveButtons.Jump | MoveButtons.JumpPressed : MoveButtons.None;
            jumps += press ? 1 : 0;
            return i < 120 ? new ShipInput { Vertical = 1 } : new ShipInput { Vertical = 0.5f, Throttle = 0.4f, Yaw = 0.8f };
        }, i =>
        {
            CharacterState s = rig.Ch(ada).State;
            Assert.True(s.Frame == rig.Key, $"aboard at tick {i}");
            damage += rig.Ch(ada).Events.FallDamage;
            peak = MathF.Max(peak, s.Position.Y - deck);
            bool up = s.Position.Y - deck > 0.3f;
            landings += wasUp && !up ? 1 : 0;
            wasUp = up;
        });
        CharacterState end = rig.Ch(ada).State;
        Fixtures.Report($"{jumps} jumps, {landings} landings, {peak:0.00} m up at the most, back on the deck {end.Position.Y - deck:0.0000} m off; fall damage {damage}; ship {rig.Height:0.0} m up at {rig.Ship.State.Velocity.Length():0.0} m/s");
        Assert.Equal(3, landings, "three jumps, three landings on the deck");
        Assert.True(peak > 1.0f && peak < 2.0f, "a jump's height, as on the ground (the ship's climb isn't felt)");
        Assert.True(MathF.Abs(end.Position.Y - deck) <= Grid && damage == 0 && rig.Game.P(ada).Vitals.Health >= 99.9f, "back on the deck, unhurt");
    }

    /// <summary>
    /// IRON_WAKE.md §4.5, §14: from a ship hovering with its pads 10 m up, someone walks down the open ramp and off its
    /// end: back in the zone's frame with the ship's velocity, a fall from that height, landing hurt.
    /// </summary>
    [Test]
    public static void StepOffTheRampAtTenMetres()
    {
        using var rig = new IronWakeRig();
        Entity ada = rig.Aboard("Ada", new Vector3(0, 3.2f, -12), -Vector3.UnitZ);
        rig.Pilot("Bo");
        rig.Step(5);
        rig.Fly(30 * 14, _ => rig.HoldHeight(10.1f));
        float height = rig.Height;
        long left = rig.Zone.Frames.Left;
        int fall = 0;
        float landing = 0, rampEnd = 0;
        bool off = false;
        for (int t = 0; t < 30 * 10 && fall == 0; t++)
        {
            rig.Ship.Input = rig.HoldHeight(10.1f);
            if (!off)
            {
                rampEnd = rig.Zone.HeightAboveGround(rig.Zone.WorldOf(rig.Ch(ada).State));
            }
            rig.Walk(ada, -Vector3.UnitZ, 1);
            off |= rig.Ch(ada).State.Frame == 0;
            fall = Math.Max(fall, rig.Ch(ada).Events.FallDamage);
            landing = MathF.Max(landing, rig.Ch(ada).Events.LandingSpeed);
        }
        float health = rig.Game.P(ada).Vitals.Health;
        Fixtures.Report(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"hovering with the pads {height:0.00} m up; the last of the ramp under Ada {rampEnd:0.00} m over the ground; " +
            $"Ada off it into the zone's frame ({rig.Zone.Frames.Left - left} hand-over), landed at {landing:0.0} m/s: {fall} damage, health {health:0}"));
        Assert.True(height is > 9.5f and < 10.7f, "hovering 10 m up");
        Assert.True(off && rig.Zone.Frames.Left == left + 1, "walked off the ramp into the zone's frame");
        Assert.True(fall > 0 && health > 0 && health < 100, "landed hurt");
    }

    /// <summary>
    /// IRON_WAKE.md §4.7: the ramp refuses to move into someone (RAMP BLOCKED) and moves once they have stepped off; the
    /// parked ship's body in the zone follows. A turret's platform turns into someone anyway, and their next steps push
    /// them out of it.
    /// </summary>
    [Test]
    public static void MovingPartsAndPeople()
    {
        using var rig = new IronWakeRig();
        ShipInterior interior = rig.Def.Interior!;
        int main = interior.PartIndex("Ramp_Main"), turret = interior.PartIndex("Defense_A_P_Yaw");
        Entity ada = rig.Aboard("Ada", new Vector3(0, 2.2f, -20), Vector3.UnitZ);
        rig.Step(10);
        Assert.True(rig.Ch(ada).State.Frame == rig.Key && rig.Ch(ada).State.Position.Y < 3, "Ada stands on the open ramp");
        uint body = rig.Ship.ParkedBody;
        Assert.Equal(OutcomeCode.NotAllowed, rig.Zone.Ships().MovePart(rig.Ship, main, 0).Code, "the ramp's parts are its door's to move");
        Outcome blocked = rig.Zone.Ships().SetDoor(rig.Game.P(ada), rig.Ship, 0, open: false);
        Assert.Equal(OutcomeCode.RampBlocked, blocked.Code, "the ramp won't shut on Ada");
        Assert.True(rig.Ship.Parts[main] == 1 && rig.Ship.ParkedBody == body, "and nothing moved");

        rig.Walk(ada, Vector3.UnitZ, 30 * 4);
        Assert.Equal(OutcomeCode.Done, rig.Zone.Ships().SetDoor(rig.Game.P(ada), rig.Ship, 0, open: false).Code, "the ramp shuts once she's in the hold");
        rig.Step(30 * 6 + 2);
        Assert.True(rig.Ship.Parts[main] == 0 && rig.Ship.ParkedBody != 0, "shut in six seconds, the parked body made again at the new states");
        lock (JoltRuntime.Gate)
        {
            Assert.True(rig.Ship.Space!.Ray(new Vector3(0, 5, -30), Vector3.UnitZ, 20, out SpaceHit shut), "the shut ramp closes the stern");
            Fixtures.Report($"the shut ramp at z {shut.Point.Z:0.00} (part {interior.Parts[shut.Part].Name})");
        }

        // The aft port turret's platform turned a quarter round, onto where Bo stands on the sponson.
        Entity bo = rig.Aboard("Bo", new Vector3(-7f, 8.05f, -10.6f), Vector3.UnitZ);
        rig.Step(10);
        Vector3 before = rig.Ch(bo).State.Position;
        Assert.Equal(OutcomeCode.Done, rig.Zone.Ships().MovePart(rig.Ship, turret, -MathF.PI / 2).Code, "a turret turns into people");
        bool inside;
        lock (JoltRuntime.Gate)
        {
            inside = !rig.Ship.Space!.CapsuleClear(rig.Ch(bo).State.Position);
        }
        int ticks = 0;
        for (; ticks < 60; ticks++)
        {
            rig.Step();
            lock (JoltRuntime.Gate)
            {
                if (rig.Ship.Space!.CapsuleClear(rig.Ch(bo).State.Position))
                {
                    break;
                }
            }
        }
        Fixtures.Report($"the platform met Bo: {inside}; out of it after {ticks + 1} ticks, moved {Vector3.Distance(before, rig.Ch(bo).State.Position):0.00} m, still aboard {rig.Ch(bo).State.Frame == rig.Key}");
        Assert.True(inside, "the turned platform overlaps Bo");
        Assert.True(ticks < 60 && rig.Ch(bo).State.Frame == rig.Key, "his steps push him out of it, aboard");
    }

    /// <summary>
    /// IRON_WAKE.md §4.9, §14: a shot from the ground behind the parked ship goes in through the open ramp and hits a player
    /// in the hold; with the ramp shut the same shot meets the hull, and the player inside is unhurt.
    /// </summary>
    [Test]
    public static void ShotsThroughTheRamp()
    {
        using var rig = new IronWakeRig();
        ShipInterior interior = rig.Def.Interior!;
        Entity ada = rig.Aboard("Ada", new Vector3(0, 3.2f, -14), -Vector3.UnitZ);
        Entity cy = rig.Add("Cy", new Vector3(0.8f, 0, -36), Vector3.UnitZ);
        Guns.Arm(rig.Game, cy, "rifle", "rifle-ammo", 30);
        rig.Step(5);
        float health = rig.Game.P(ada).Vitals.Health;
        (TargetFamily open, float hurtOpen) = Shoot(rig, cy, ada, 1);
        Fixtures.Report($"through the open ramp: met {open}, Ada {hurtOpen:0.0} hurt");
        Assert.True(open == TargetFamily.Character && hurtOpen > 0, "the shot goes in through the open ramp and hits Ada");

        rig.Game.P(ada).Vitals.Health = health;
        Assert.Equal(OutcomeCode.Done, rig.Zone.Ships().SetDoor(rig.Game.P(ada), rig.Ship, 0, open: false).Code, "the ramp shuts");
        rig.Step(30 * 6 + 2);
        float hull = rig.Ship.State.Health;
        rig.Step(10);
        (TargetFamily shut, float hurtShut) = Shoot(rig, cy, ada, 2);
        Fixtures.Report($"with the ramp shut: met {shut}, Ada {hurtShut:0.0} hurt, the hull {hull - rig.Ship.State.Health:0.00} damaged, the ramp at {rig.Ship.Doors[0].Health:0.0}");
        Assert.True(shut == TargetFamily.Ship && hurtShut == 0, "the same shot meets the hull; Ada is unhurt");
        Assert.True(rig.Ship.State.Health == hull, "the ramp, an armoured door of its own (IW2), takes it, not the hull");
    }

    // A shot from a player on the ground at another's chest, judged now: what it ended on, and how much it hurt the other.
    private static (TargetFamily Met, float Hurt) Shoot(IronWakeRig rig, Entity shooter, Entity victim, uint seq)
    {
        Vector3 chest = rig.Zone.WorldOf(rig.Ch(victim).State) + rig.Zone.UpOf(rig.Ch(victim).State) * 1.35f;
        rig.Game.Look(shooter, chest);
        Assert.Equal(OutcomeCode.Done, Guns.Queue(rig.Game, shooter, seq, aimed: true), "the shot is taken");
        return Watch(rig, rig.Game.P(shooter).Id, rig.Game.P(victim).Id);
    }

    // Twenty ticks of a shot's flight: what it ended on, and the hurt it did the victim.
    private static (TargetFamily Met, float Hurt) Watch(IronWakeRig rig, EntityId shooter, EntityId victim)
    {
        TargetFamily met = TargetFamily.None;
        float hurt = 0;
        rig.Step(20, () =>
        {
            foreach (ProjectileEnded e in rig.Zone.Events.Of<ProjectileEnded>())
            {
                met = e.Shooter == shooter ? e.Target.Family : met;
            }
            foreach (Hurt h in rig.Zone.Events.Of<Hurt>())
            {
                hurt += h.Player == victim && h.By == shooter ? h.Amount : 0;
            }
        });
        return (met, hurt);
    }

    /// <summary>
    /// IRON_WAKE.md §4.9, §14: from the deck of a ship sliding sideways, a player shoots out through the open ramp at
    /// someone on the ground, aiming where they saw the ship and the target a fifth of a second before: the server judges
    /// the shot from there (the ship's pose history) with the ship's velocity added, and it hits.
    /// </summary>
    [Test]
    public static void AShotFromAMovingDeck()
    {
        using var rig = new IronWakeRig();
        Entity ada = rig.Aboard("Ada", new Vector3(0, 3.2f, -16), -Vector3.UnitZ);
        Guns.Arm(rig.Game, ada, "rifle", "rifle-ammo", 30);
        rig.Pilot("Bo");
        // Up a few metres, still.
        rig.Fly(30 * 6, _ => rig.HoldHeight(4));
        // Cy on the ground behind the stern; the ship slides to its side.
        Entity cy = rig.Add("Cy", new Vector3(-2, 0, -48), Vector3.UnitZ);
        rig.Fly(75, _ => rig.HoldHeight(4) with { Strafe = 1 });
        const byte viewDelay = 24; // six ticks: the client draws others 100 ms and more behind
        long shotTick = rig.Zone.Tick + 1, viewTick = shotTick - viewDelay / 4;
        Assert.True(rig.Zone.History.SampleShip(rig.Key, viewTick, out ShipPose seen), "the ship's pose then is kept");
        CharacterState a = rig.Ch(ada).State;
        Vector3 eye = seen.ToWorld(a.Position + Vector3.UnitY * rig.Zone.Tuning.EyeHeight(a.Stance));
        Vector3 chest = rig.Zone.WorldOf(rig.Ch(cy).State) + rig.Zone.UpOf(rig.Ch(cy).State) * 1.3f;
        // Aimed as the shooter does: led for the ship's velocity the bullet keeps (300 m/s, the rifle's).
        Vector3 carried = seen.VelocityAt(eye), toward = Vector3.Normalize(chest - eye);
        float speed = (float)(rig.Game.C.ItemByKey("rifle").Firearm!.Speed * rig.Game.C.ItemByKey("rifle-ammo").Bullet!.Speed);
        float along = Vector3.Dot(toward, carried);
        float k = along + MathF.Sqrt(along * along - carried.LengthSquared() + speed * speed);
        Vector3 aim = Vector3.Normalize(toward * k - carried);
        float offset = Vector3.Distance(eye, rig.Pose.ToWorld(a.Position + Vector3.UnitY * rig.Zone.Tuning.EyeHeight(a.Stance)));
        bool clear = !ShipQueries.Ray(rig.Zone.Frames.Ships, _ => seen, eye, toward, Vector3.Distance(eye, chest), out _);
        Assert.Equal(OutcomeCode.Done, rig.Zone.Guns().QueueGunAction(ada, new GunAction
        {
            Seq = 1, Tick = shotTick, Kind = GunActionKind.Fire, Slot = 0, Item = rig.Game.P(ada).Inventory.Belt[0].Item,
            View = seen.DirectionToLocal(aim), Frame = rig.Key, Aimed = true, ViewDelay = viewDelay,
        }), "Ada fires");
        (TargetFamily met, float hurt) = Watch(rig, rig.Game.P(ada).Id, rig.Game.P(cy).Id);
        Fixtures.Report(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"the ship {rig.Height:0.0} m up sliding at {carried.Length():0.0} m/s; Ada's eye {offset:0.00} m from where she saw it; " +
            $"{Vector3.Distance(eye, chest):0.0} m to Cy, clear of the ship {clear}; the shot met {met}, Cy {hurt:0.0} hurt"));
        Assert.True(offset > 0.5f, "without the ship's pose history the eye would be well off the line");
        Assert.True(met == TargetFamily.Character && hurt > 0, "the lag-compensated shot from the deck hits Cy");
    }

    /// <summary>
    /// IRON_WAKE.md §4.9, §14: a rocket bursting against Deck A's ceiling hurts the player on the deck below in its sight,
    /// and not the one standing on Deck B over it; one bursting against the hull's outside hurts the player outside and
    /// nobody inside, and the hull takes it.
    /// </summary>
    [Test]
    public static void RocketsInsideAndAgainstTheHull()
    {
        using var rig = new IronWakeRig();
        GameContent c = rig.Game.C;
        BlastStats rocket = c.Blast("rocket")!;
        ushort item = c.ItemByKey("rocket").Id;
        Entity ada = rig.Aboard("Ada", new Vector3(0.8f, 3.2f, 2), Vector3.UnitZ);
        Entity bo = rig.Aboard("Bo", new Vector3(0.5f, 7.4f, 2.5f), Vector3.UnitZ);
        rig.Step(10);
        ShipPose pose = rig.Pose;
        rig.Zone.Damage().Blast(rocket, pose.ToWorld(new Vector3(0, 7.15f, 2)), pose.DirectionToWorld(-Vector3.UnitY), rig.Ship.Ref, TargetFamily.Ship,
            EntityId.None, "", item);
        float adaHurt = 100 - rig.Game.P(ada).Vitals.Health, boHurt = 100 - rig.Game.P(bo).Vitals.Health;
        Fixtures.Report($"a rocket at Deck A's ceiling: Ada below {adaHurt:0.0} hurt, Bo above on Deck B {boHurt:0.0}");
        Assert.True(adaHurt > 0, "the player on the deck in its sight is hurt");
        Assert.True(boHurt == 0, "the one behind the deck is not");

        Entity cy = rig.Add("Cy", new Vector3(-8f, 0, -10), Vector3.UnitX);
        Entity dee = rig.Aboard("Dee", new Vector3(-4.6f, 3.2f, -10), -Vector3.UnitX);
        rig.Step(10);
        float hull = rig.Ship.State.Health;
        pose = rig.Pose;
        rig.Zone.Damage().Blast(rocket, pose.ToWorld(new Vector3(-6.36f, 3.0f, -10)), pose.DirectionToWorld(-Vector3.UnitX), rig.Ship.Ref, TargetFamily.Ship,
            EntityId.None, "", item);
        float cyHurt = 100 - rig.Game.P(cy).Vitals.Health, deeHurt = 100 - rig.Game.P(dee).Vitals.Health;
        Fixtures.Report($"a rocket against the hull's outside: Cy outside {cyHurt:0.0} hurt, Dee inside {deeHurt:0.0}, the hull {hull - rig.Ship.State.Health:0.0}");
        Assert.True(cyHurt > 0 && deeHurt == 0, "it hurts the player outside and nobody inside");
        Assert.True(rig.Ship.State.Health < hull, "and the hull takes it");
    }

    /// <summary>
    /// IRON_WAKE.md §12, measured once: what one Iron Wake flying with twelve aboard adds to the server's tick (its step,
    /// their steps in its space), against the same zone without it; and what one prediction step aboard costs a client
    /// (the same code: a character's step in the ship's frame).
    /// </summary>
    [Test]
    public static void CostsOfACrewAboard()
    {
        using var rig = new IronWakeRig();
        var crew = new List<Entity>();
        for (int i = 0; i < 12; i++)
        {
            crew.Add(rig.Aboard($"Crew{i}", new Vector3(-3 + i % 4 * 2, 3.2f, -14 + i / 4 * 8), i % 2 == 0 ? Vector3.UnitZ : -Vector3.UnitZ));
        }
        Entity bo = rig.Pilot("Bo");
        rig.Fly(60, _ => new ShipInput { Vertical = 1 });
        // The crew walks about while the ship climbs and turns; the tick timed.
        var watch = new Stopwatch();
        const int ticks = 300;
        rig.Fly(ticks, i =>
        {
            for (int n = 0; n < crew.Count; n++)
            {
                ref Character c = ref rig.Ch(crew[n]);
                c.Input.Forward = (i / 45 + n) % 2 == 0 ? 1 : -1;
                c.Input.Frame = rig.Key;
            }
            watch.Start();
            return new ShipInput { Vertical = 0.3f, Throttle = 0.3f, Yaw = 0.5f };
        }, _ => watch.Stop());
        double with = watch.Elapsed.TotalMilliseconds / ticks;
        int aboard = crew.Count(e => rig.Ch(e).State.Frame == rig.Key);

        // One character's step aboard, as a client predicts it.
        CharacterState state = rig.Ch(crew[0]).State;
        var input = new MoveInput { Forward = 1, View = Vector3.UnitZ, Frame = rig.Key };
        const int steps = 2000;
        var step = Stopwatch.StartNew();
        for (int i = 0; i < steps; i++)
        {
            CharacterState s = state;
            lock (JoltRuntime.Gate)
            {
                rig.Zone.Frames.Step(ref s, input, rig.Zone.Tick, out _);
            }
        }
        double perStep = step.Elapsed.TotalMilliseconds * 1000 / steps;

        // The same zone without the ship and its people.
        foreach (Entity e in crew.Append(bo))
        {
            rig.Zone.RemoveCharacter(e);
        }
        rig.Zone.Ships().RemoveShip(rig.Ship);
        var without = new Stopwatch();
        for (int i = 0; i < ticks; i++)
        {
            without.Start();
            rig.Step();
            without.Stop();
        }
        double baseline = without.Elapsed.TotalMilliseconds / ticks;
        Fixtures.Report(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"the tick with one Iron Wake flying and {aboard} walking aboard: {with:0.000} ms, without them {baseline:0.000} ms: it adds {with - baseline:0.000} ms (budget 1 ms); " +
            $"a prediction step aboard {perStep:0.0} µs"));
        Assert.Equal(12, aboard, "the crew stayed aboard");
        Assert.True(with - baseline < 5, "well within a tick (the budget is 1 ms; this machine's measurement above)");
    }
}
