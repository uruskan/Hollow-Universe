using System;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Surface;

namespace Hollow.Tests;

/// <summary>A test surface: a smooth sphere whose ground sits at a fixed height, with an optional sea.</summary>
public sealed class LevelSurface : ISurface
{
    private readonly double _height;

    public LevelSurface(double radius, double groundHeight = 0, double seaRadius = 0)
    {
        Radius = radius;
        _height = groundHeight;
        SeaRadius = seaRadius;
    }

    public double Radius { get; }

    public double SeaRadius { get; }

    public double HeightAt(Vector3D direction) => _height;

    public TriangleMesh GroundCollision(in ZoneFrame frame, double centreX, double centreZ, double size) =>
        SphereSurface.GridCollision(this, frame, centreX, centreZ, size, spacing: 2.0);
}

/// <summary>A zone with one character, stepped by hand.</summary>
public sealed class MovementRig : IDisposable
{
    public MovementRig(ISurface surface, double gravityG = 0.92)
    {
        var spec = new ZoneSpec { Radius = surface.Radius, SurfaceGravity = gravityG, LatitudeDegrees = 10, LongitudeDegrees = 20 };
        Zone = new Zone(surface, spec);
        Entity = Zone.AddCharacter(new EntityId(1), "test", Zone.NewCharacter(Zone.GroundAt(0, 0)));
    }

    public Zone Zone { get; }

    public Entity Entity { get; }

    public ref Character Character => ref Zone.Entities.Get<Character>(Entity);

    public ref CharacterState State => ref Character.State;

    /// <summary>Steps with the same input for a number of ticks; returns the largest fall damage seen.</summary>
    public int Run(MoveInput input, int ticks)
    {
        int damage = 0;
        for (int i = 0; i < ticks; i++)
        {
            Character.Input = input;
            Zone.Step();
            damage = Math.Max(damage, Character.Events.FallDamage);
            input.Buttons &= ~MoveButtons.JumpPressed;
        }
        return damage;
    }

    public float HorizontalSpeed
    {
        get
        {
            Vector3 up = Zone.Frame.UpAt(State.Position);
            Vector3 v = State.Velocity;
            return (v - up * Vector3.Dot(v, up)).Length();
        }
    }

    public void Dispose() => Zone.Dispose();
}

public static class MovementTests
{
    private const double Earthlike = 6_435_000;

    /// <summary>GROUND.md §4.5: walking, sprinting and crouching reach RustMVP's speeds; sprint needs straight forward.</summary>
    [Test]
    public static void SpeedsFollowTheTuning()
    {
        using var rig = new MovementRig(new LevelSurface(Earthlike));
        MovementTuning t = MovementTuning.Default;
        rig.Run(new MoveInput { Forward = 1 }, 30);
        Assert.Near(t.WalkSpeed, rig.HorizontalSpeed, 0.01, "walk");
        rig.Run(new MoveInput { Forward = 1, Buttons = MoveButtons.Sprint }, 30);
        Assert.Near(t.SprintSpeed, rig.HorizontalSpeed, 0.01, "sprint");
        Assert.True((rig.State.Flags & MoveFlags.Sprinting) != 0, "flagged as sprinting");
        rig.Run(new MoveInput { Forward = 1, Right = 1, Buttons = MoveButtons.Sprint }, 30);
        Assert.Near(t.WalkSpeed, rig.HorizontalSpeed, 0.01, "no sprint on a diagonal");
        rig.Run(new MoveInput { Forward = 1, Buttons = MoveButtons.Crouch }, 30);
        Assert.Near(t.CrouchSpeed, rig.HorizontalSpeed, 0.01, "crouch");
        Assert.Equal(Stance.Crouching, rig.State.Stance, "stance");
        rig.Run(new MoveInput(), 10);
        Assert.Near(0, rig.HorizontalSpeed, 1e-6, "stops without input");
        Assert.Equal(Stance.Standing, rig.State.Stance, "stands up again");
    }

    /// <summary>A jump rises v²/2g with the planet's gravity times the feel scale: 1.51 m on the first world.</summary>
    [Test]
    public static void JumpHeightFollowsGravity()
    {
        using var rig = new MovementRig(new LevelSurface(Earthlike));
        rig.Run(new MoveInput(), 5);
        float start = rig.State.Position.Y, top = start;
        rig.Character.Input = new MoveInput { Buttons = MoveButtons.Jump | MoveButtons.JumpPressed };
        for (int i = 0; i < 60; i++)
        {
            rig.Zone.Step();
            rig.Character.Input = new MoveInput { Buttons = MoveButtons.Jump };
            top = Math.Max(top, rig.State.Position.Y);
        }
        float g = rig.Zone.Movement.Gravity;
        double expected = MovementTuning.Default.JumpSpeed * MovementTuning.Default.JumpSpeed / (2 * g);
        Fixtures.Report($"gravity {g:0.00} m/s², jump {top - start:0.000} m (v²/2g = {expected:0.000} m)");
        Assert.Near(expected, top - start, 0.02, "jump height");
        Assert.True((rig.State.Flags & MoveFlags.OnGround) != 0, "landed again");
        Assert.Equal(100, (int)rig.State.Health, "a jump doesn't hurt");
    }

    /// <summary>Fall damage follows Rust's rule: nothing from 5 m, most of the health from 30 m, death from 40 m.</summary>
    [Test]
    public static void FallDamageFollowsLandingSpeed()
    {
        MovementTuning t = MovementTuning.Default;
        foreach ((float height, int min, int max) in new[] { (5f, 0, 0), (30f, 80, 99), (40f, 100, 255) })
        {
            using var rig = new MovementRig(new LevelSurface(Earthlike));
            Vector3 up = rig.Zone.Frame.UpAt(rig.State.Position);
            rig.Zone.Teleport(rig.Entity, rig.State.Position + up * height);
            int damage = rig.Run(new MoveInput(), 120);
            double speed = Math.Sqrt(2 * rig.Zone.Movement.Gravity * height);
            double rule = speed <= t.SafeLandingSpeed ? 0 : Math.Ceiling(t.FallDamageScale * (speed - t.SafeLandingSpeed) / (t.LethalScaleSpeed - t.SafeLandingSpeed));
            Fixtures.Report($"{height} m: landed at {speed:0.0} m/s, damage {damage} (rule {rule}), health {rig.State.Health}");
            Assert.InRange(min, max, damage, $"damage from {height} m");
            Assert.Near(rule, damage, 2, "matches the rule");
        }
    }

    /// <summary>
    /// Swimming where the surface has a sea: the stand-in has none, so this uses a sea 6 m deep. A swimmer floats with
    /// the head out, swims at swimming speed, dives with crouch and never lands hard.
    /// </summary>
    [Test]
    public static void SwimmingInTheSea()
    {
        using var rig = new MovementRig(new LevelSurface(Earthlike, groundHeight: -6, seaRadius: Earthlike));
        MovementTuning t = rig.Zone.Tuning;
        Vector3 up = rig.Zone.Frame.UpAt(rig.State.Position);
        // Drop in from just above the water.
        rig.Zone.Teleport(rig.Entity, rig.State.Position + up * 6.5f);
        int damage = rig.Run(new MoveInput(), 300);
        float floating = CharacterMotor.Submersion(rig.State.Position, rig.Zone.Movement);
        Fixtures.Report($"floats at {floating:0.00} of its height under water");
        Assert.True((rig.State.Flags & MoveFlags.Swimming) != 0, "swimming");
        Assert.Near(t.FloatDepth, floating, 0.05, "floats at the float depth");
        Assert.Equal(0, damage, "falling into water doesn't hurt");
        rig.Run(new MoveInput { Forward = 1 }, 60);
        Assert.Near(t.SwimSpeed, rig.HorizontalSpeed, 0.05, "swim speed");
        rig.Run(new MoveInput { Buttons = MoveButtons.Crouch }, 20);
        Vector3 v = rig.State.Velocity;
        Assert.True(Vector3.Dot(v, rig.Zone.Frame.UpAt(rig.State.Position)) < -1.5f, "crouch dives");
    }

    /// <summary>
    /// "Up" always comes from the planet's centre: on a planet 300 m across, a minute's walk goes a fifth of the way
    /// round, and the character stays on the ground the whole way.
    /// </summary>
    [Test]
    public static void UpFollowsTheCurvedGround()
    {
        var surface = new LevelSurface(150);
        using var rig = new MovementRig(surface, gravityG: 0.5);
        Vector3D start = rig.Zone.Frame.ToBody(rig.State.Position).Normalized;
        double worst = 0;
        for (int i = 0; i < 20; i++)
        {
            rig.Run(new MoveInput { Forward = 1 }, 30);
            double height = rig.Zone.Frame.ToBody(rig.State.Position).Length - surface.Radius;
            worst = Math.Max(worst, Math.Abs(height));
            Assert.True((rig.State.Flags & MoveFlags.OnGround) != 0, "on the ground");
        }
        Vector3D end = rig.Zone.Frame.ToBody(rig.State.Position).Normalized;
        double arc = surface.Radius * DMath.Acos(Math.Clamp(Vector3D.Dot(start, end), -1, 1));
        Fixtures.Report($"walked {arc:0.0} m round the planet, height off the ground at most {worst * 100:0.0} cm");
        Assert.Near(20 * MovementTuning.Default.WalkSpeed, arc, 2.0, "distance round the curve");
        Assert.True(worst < 0.1, "stays on the ground");
    }

    /// <summary>The entity store: IDs, slot reuse with generations, components added, read and removed.</summary>
    [Test]
    public static void EntityStoreKeepsHandlesSafe()
    {
        var store = new EntityStore();
        Entity a = store.Create(new EntityId(10)), b = store.Create(new EntityId(11));
        store.Add(a, new Named { Name = "a" });
        store.Add(b, new Named { Name = "b" });
        store.Destroy(a);
        Entity c = store.Create(new EntityId(12));
        Assert.Equal(a.Index, c.Index, "slot reused");
        Assert.True(!store.IsAlive(a) && store.IsAlive(c), "old handle is dead, new one alive");
        Assert.True(!store.Has<Named>(c), "no component carried over");
        Assert.Equal("b", store.Get<Named>(b).Name, "other entity untouched");
        Assert.True(store.TryFind(new EntityId(11), out Entity found) && found == b, "find by ID");
        var ids = new EntityIdAllocator(5, () => EntityIdAllocator.EpochUnixMilliseconds + 1000);
        EntityId first = ids.Next(), second = ids.Next();
        Assert.True(second.Value == first.Value + 1, "sequence within a millisecond");
        Assert.Equal(1000UL, first.Value >> 23, "time part");
        Assert.Equal(5UL, (first.Value >> 14) & 511, "allocator part");
    }
}
