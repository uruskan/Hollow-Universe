using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;

namespace Hollow.Tests;

/// <summary>Guns in a <see cref="GameRig"/>: a loaded gun in hand, shots queued for the coming tick as the server would.</summary>
public static class Guns
{
    /// <summary>A gun on belt slot 0 with rounds in it (and attachments), taken up and ready (its equip time stepped).</summary>
    public static void Arm(GameRig rig, Entity e, string gun, string ammo, int loaded, params string[] attachments)
    {
        GameContent c = rig.C;
        ItemStack stack = ItemStack.Of(c.ItemByKey(gun), 1);
        stack.Loaded = (ushort)loaded;
        stack.LoadedAmmo = c.ItemByKey(ammo).Id;
        foreach (string key in attachments)
        {
            ItemDef part = c.ItemByKey(key);
            stack.SetMod(part.Attachment!.Slot, part.Id);
        }
        rig.P(e).Inventory.Belt.Set(0, stack);
        rig.Step(16, () => rig.Ch(e).Actions = new ActionInput { HeldSlot = 0 });
    }

    /// <summary>Queues a shot for the coming tick with the character's view as it is (or a reload); returns the queue's answer.</summary>
    public static GunResult Queue(GameRig rig, Entity e, uint seq, GunActionKind kind = GunActionKind.Fire, bool aimed = false, byte viewDelay = 0,
        long? tick = null, byte slot = 0, ushort ammo = 0)
    {
        ref Character c = ref rig.Ch(e);
        return rig.Zone.QueueGunAction(e, new GunAction
        {
            Seq = seq,
            Tick = tick ?? rig.Zone.Tick + 1,
            Kind = kind,
            Slot = slot,
            Item = rig.P(e).Inventory.Belt[slot].Item,
            Yaw = c.Input.Yaw,
            Pitch = c.Input.Pitch,
            Aimed = aimed,
            ViewDelay = viewDelay,
            Ammo = ammo,
        });
    }

    /// <summary>The launch events of a step (bullets leaving guns).</summary>
    public static List<ZoneEvent> Launches(GameRig rig) => rig.Zone.GameEvents.Where(x => x.Kind == ZoneEventKind.ProjectileLaunched).ToList();

    /// <summary>A bullet of a gun and round, as a shot would launch it (for damage tests).</summary>
    public static Bullet Round(GameContent c, string gun, string ammo, EntityId shooter = default) => new()
    {
        Gun = c.ItemByKey(gun).Firearm!,
        Round = c.ItemByKey(ammo).Bullet!,
        Ammo = c.ItemByKey(ammo),
        Damage = (float)(c.ItemByKey(gun).Firearm!.Damage * c.ItemByKey(ammo).Bullet!.Damage),
        Shooter = shooter,
    };
}

public static class GunTests
{
    /// <summary>
    /// GROUND.md §6.3, ballistics: a bullet falls with the planet's gravity and slows with drag, integrated exactly, so two
    /// steps a tick give the path a hundred would; in the zone, a shot's bullet is where that integral puts it. Prints the
    /// drop of each round flat over 50–200 m (the table in §6.3).
    /// </summary>
    [Test]
    public static void BulletDropFollowsGravityAndDrag()
    {
        using var rig = new GameRig();
        GameContent c = rig.C;
        FirearmStats rifle = c.ItemByKey("rifle").Firearm!;
        float g = (float)(rig.Zone.Spec.SurfaceGravity * 9.80665);
        var gravity = new Vector3(0, -g, 0);

        // Exact: 2 steps of 1/60 s or 100 of 1/3000 s land in the same place; Euler in tiny steps agrees.
        var v0 = new Vector3(300, 0, 0);
        (Vector3 coarse, _) = Fly(v0, gravity, rifle.Drag, 1.0 / 60, 60);
        (Vector3 fine, _) = Fly(v0, gravity, rifle.Drag, 1.0 / 3000, 3000);
        Vector3 euler = Vector3.Zero, ve = v0;
        for (int i = 0; i < 100_000; i++)
        {
            float h = 1e-5f;
            ve += (gravity - ve * (float)rifle.Drag) * h;
            euler += ve * h;
        }
        Assert.True(Vector3.Distance(coarse, fine) < 0.02f, $"exact integration (to float precision over 260 m): {coarse} vs {fine}");
        Assert.True(Vector3.Distance(coarse, euler) < 0.05f, $"agrees with small steps: {coarse} vs {euler}");

        // The drop table: flat from the muzzle, how far below the line each round is at a distance.
        foreach ((string gun, string[] rounds) in new[] { ("pistol", new[] { "pistol-ammo", "pistol-ammo-hv", "pistol-ammo-incendiary" }),
                     ("rifle", new[] { "rifle-ammo", "rifle-ammo-hv", "rifle-ammo-incendiary" }) })
        {
            FirearmStats f = c.ItemByKey(gun).Firearm!;
            foreach (string round in rounds)
            {
                double speed = f.Speed * c.ItemByKey(round).Bullet!.Speed;
                var drops = new List<string>();
                foreach (float at in new[] { 50f, 100f, 150f, 200f })
                {
                    Vector3 p = Vector3.Zero, v = new((float)speed, 0, 0);
                    double decay = Ballistics.Decay(f.Drag, 1.0 / 60);
                    int steps = 0;
                    while (p.X < at && steps++ < 600)
                    {
                        (p, v) = Ballistics.Step(p, v, gravity, f.Drag, decay, 1.0 / 60);
                    }
                    drops.Add($"{at:0} m {-p.Y:0.00} m ({steps / 60.0:0.00} s)");
                }
                Fixtures.Report($"{round} at {speed:0} m/s: drop " + string.Join(", ", drops));
            }
        }

        // In the zone: a rifle shot steeply up (over any tree beyond the clearing round the origin); twelve ticks on, the
        // bullet is where the integral says.
        Entity ada = rig.Add("Ada", 0, 0);
        Guns.Arm(rig, ada, "rifle", "rifle-ammo", 30);
        rig.Ch(ada).Input.Pitch = 70 * MathF.PI / 180;
        rig.Zone.GameEvents.Clear();
        Assert.Equal(GunResult.Accepted, Guns.Queue(rig, ada, 1), "queued");
        rig.Step();
        ZoneEvent launch = Guns.Launches(rig).Single();
        rig.Step(11);
        Bullet bullet = rig.Zone.Bullets.Single();
        Vector3 up = rig.Zone.Frame.UpAt(launch.Position);
        // The bullet's first steps were in the tick it was fired: 12 ticks, 24 steps.
        (Vector3 expected, _) = Fly(launch.Velocity, -up * g, rifle.Drag, Zone.BulletStepSeconds, 24, launch.Position);
        Fixtures.Report($"zone bullet after 0.4 s: {Vector3.Distance(launch.Position, bullet.Position):0.0} m out, " +
            $"{Vector3.Distance(bullet.Position, expected) * 1000:0.000} mm from the integral");
        Assert.True(Vector3.Distance(bullet.Position, expected) < 0.01f, "the zone flies bullets by the same integral (up varies a hair)");
        Assert.True(bullet.Velocity.Length() < launch.Velocity.Length() - 25, "drag slowed it");
    }

    private static (Vector3 Position, Vector3 Velocity) Fly(Vector3 v, Vector3 gravity, double drag, double step, int steps, Vector3 p = default)
    {
        double decay = Ballistics.Decay(drag, step);
        for (int i = 0; i < steps; i++)
        {
            (p, v) = Ballistics.Step(p, v, gravity, drag, decay, step);
        }
        return (p, v);
    }

    /// <summary>
    /// ARCHITECTURE.md §11.5, a gun under lag compensation: the target stepped aside after the shooter saw it. A shot at
    /// where the shooter saw it hits when the action says how far back that was, and misses without; the hit marker and
    /// the damage come from the server alone.
    /// </summary>
    [Test]
    public static void LagCompensatedHitOnATargetThatMoved()
    {
        foreach (byte viewDelay in new byte[] { 16, 0 })
        {
            using var rig = new GameRig();
            Entity ada = rig.Add("Ada", -4, 0);
            Entity bo = rig.Add("Bo", 4, 0);
            rig.Ch(bo).Input.Yaw = MathF.PI / 2; // facing Ada (west), so his chest is towards her and his arms aside
            Guns.Arm(rig, ada, "rifle", "rifle-ammo", 30);
            Player b = rig.P(bo);
            Vector3 seen = rig.Ch(bo).State.Position + rig.Zone.Frame.UpAt(rig.Ch(bo).State.Position) * 1.2f;
            // Bo steps 1.2 m aside two ticks before the shot; Ada, 4 ticks behind, still sees him where he was.
            rig.Zone.Teleport(bo, rig.Zone.GroundAt(4, 1.2f) + rig.Zone.Frame.UpAt(seen) * 0.02f);
            rig.Step(2);
            rig.Look(ada, seen);
            rig.Zone.GameEvents.Clear();
            Assert.Equal(GunResult.Accepted, Guns.Queue(rig, ada, 1, aimed: true, viewDelay: viewDelay), "queued");
            rig.Step(3);
            float taken = rig.Zone.GameEvents.Where(x => x.Kind == ZoneEventKind.DamageTaken && x.Player == b.Id).Sum(x => x.Amount);
            bool marker = rig.Zone.GameEvents.Any(x => x.Kind == ZoneEventKind.HitConfirmed && x.Player == rig.P(ada).Id && x.Target == HitTarget.Character);
            Fixtures.Report($"view delay {viewDelay / 4.0:0} ticks: Bo took {taken:0.0}; hit marker {marker}");
            if (viewDelay > 0)
            {
                Assert.Near(35, taken, 0.01, "hit where Ada saw Bo: a body shot, full damage at 8 m");
                Assert.True(marker, "Ada gets a hit marker");
            }
            else
            {
                Assert.Equal(0f, taken, "without compensation it misses");
            }
        }
    }

    /// <summary>
    /// GROUND.md §6.3, recoil: the rifle's 30 shots follow RustMVP's AK pattern step by step (its pitch exactly, its yaw
    /// within the random part); aimed and crouched the step is smaller (×0.78, ×0.85); a pause of 0.4 s starts the pattern
    /// again; 0.2 s after the last shot the view comes back over 0.08 s. Every bullet leaves along the view plus the recoil
    /// before its own kick, within the spread.
    /// </summary>
    [Test]
    public static void RecoilFollowsThePattern()
    {
        using var rig = new GameRig();
        GameContent c = rig.C;
        FirearmStats rifle = c.ItemByKey("rifle").Firearm!;
        Entity ada = rig.Add("Ada", 0, 0);
        Guns.Arm(rig, ada, "rifle", "rifle-ammo", 30);
        Player a = rig.P(ada);
        rig.Ch(ada).Input.Pitch = 0.1f;
        double pitchSum = 0, yawSum = 0;
        uint seq = 0;
        for (int shot = 0; shot < 30; shot++)
        {
            rig.Zone.GameEvents.Clear();
            Assert.Equal(GunResult.Accepted, Guns.Queue(rig, ada, ++seq), $"shot {shot + 1} queued");
            RecoilSample before = Recoil.Sample(a.Gun.Recoil, rifle, rig.Zone.Tick + 1);
            rig.Step();
            Assert.Equal(GunResult.Accepted, a.GunRefusedSeq == seq ? a.GunRefused : GunResult.Accepted, $"shot {shot + 1}");
            ZoneEvent launch = Guns.Launches(rig).Single();
            pitchSum += rifle.Recoil.Pattern[shot].Pitch;
            yawSum += rifle.Recoil.Pattern[shot].Yaw;
            Assert.Near(pitchSum, a.Gun.Recoil.Pitch, 1e-9, $"pitch after shot {shot + 1}");
            Assert.Near(yawSum, a.Gun.Recoil.Yaw, 0.06 * (shot + 1) + 1e-9, $"yaw after shot {shot + 1}");
            // The bullet: the view plus the recoil before this shot's kick, within the hip spread and bloom.
            Vector3 up = rig.Zone.Frame.UpAt(launch.Position);
            Vector3 aim = CharacterMotor.ViewDirection(
                rig.Ch(ada).Input.Yaw - (float)(before.Yaw * DMath.DegToRad), rig.Ch(ada).Input.Pitch + (float)(before.Pitch * DMath.DegToRad), up);
            double off = Math.Acos(Math.Clamp(Vector3.Dot(aim, Vector3.Normalize(launch.Velocity)), -1, 1)) * DMath.RadToDeg;
            Assert.True(off <= rifle.Spread.Hip + before.Bloom + 1e-3, $"shot {shot + 1} within the cone: {off:0.000}°");
            rig.Step(3);
        }
        Assert.Equal(0, rig.P(ada).Inventory.Belt[0].Loaded, "the magazine is empty");
        Fixtures.Report($"30 shots: recoil {a.Gun.Recoil.Pitch:0.000}° up, {a.Gun.Recoil.Yaw:0.000}° right (pattern sum 29.368°, 0.615°)");

        // Recovery: held 0.2 s after the last shot, then back to nothing over 0.08 s.
        long last = a.Gun.Recoil.ShotTick;
        Assert.Near(a.Gun.Recoil.Pitch, Recoil.Sample(a.Gun.Recoil, rifle, last + 5).Pitch, 1e-9, "held for 0.2 s");
        Assert.True(Recoil.Sample(a.Gun.Recoil, rifle, last + 7.5).Pitch is > 0 and < 20, "on its way back");
        Assert.Equal(0.0, Recoil.Sample(a.Gun.Recoil, rifle, last + 9).Pitch, "all of it back after 0.28 s");

        // Aimed and crouched: the first step is smaller; after a pause the pattern starts again.
        rig.P(ada).Inventory.Belt.Set(0, rig.P(ada).Inventory.Belt[0] with { Loaded = 30 });
        rig.Step(20, () => rig.Ch(ada).Input.Buttons = MoveButtons.Crouch);
        Assert.Equal(GunResult.Accepted, Guns.Queue(rig, ada, ++seq, aimed: true), "queued");
        rig.Step(1, () => rig.Ch(ada).Input.Buttons = MoveButtons.Crouch);
        Assert.Near(rifle.Recoil.Pattern[0].Pitch * 0.78 * 0.85, a.Gun.Recoil.Pitch, 1e-9, "first step, aimed and crouched");
        Assert.Equal(1, (int)a.Gun.Recoil.Index, "the pattern started again");
    }

    /// <summary>GROUND.md §6.3, damage by body part: the rifle's 35 to the body, 70 to the head, 17.5 to a limb; less with distance.</summary>
    [Test]
    public static void HeadChestAndLimbDamage()
    {
        using var rig = new GameRig();
        GameContent c = rig.C;
        Entity bo = rig.Add("Bo", 10, 10);
        Player b = rig.P(bo);
        foreach ((HitRegion region, float distance, float expected) in new[]
                 {
                     (HitRegion.Body, 10f, 35f), (HitRegion.Head, 10f, 70f), (HitRegion.Limb, 10f, 17.5f),
                     (HitRegion.Body, 135f, 35 * (1 - 0.5f * 0.55f)), (HitRegion.Body, 200f, 35 * 0.45f),
                 })
        {
            b.Vitals.Health = 100;
            b.Vitals.Bleeding = 0;
            rig.Zone.ShootCharacter(bo, b, Guns.Round(c, "rifle", "rifle-ammo"), region, distance, Vector3.Zero, null);
            Assert.Near(expected, 100 - b.Vitals.Health, 1e-3, $"{region} at {distance} m");
            Assert.True(b.Vitals.Bleeding > 0, "bullets start bleeding");
        }
        // The kind of round: high-velocity hits a little softer; incendiary sets the victim burning.
        b.Vitals.Health = 100;
        rig.Zone.ShootCharacter(bo, b, Guns.Round(c, "rifle", "rifle-ammo-incendiary"), HitRegion.Body, 10, Vector3.Zero, null);
        Assert.Near(35 * 0.85, 100 - b.Vitals.Health, 1e-3, "incendiary damage");
        Assert.True(b.Vitals.Burning > 0, "and burning");
        float burning = b.Vitals.Burning, health = b.Vitals.Health;
        rig.Step(31);
        Assert.True(b.Vitals.Burning < burning && b.Vitals.Health < health, "burning hurts over time");
    }

    /// <summary>
    /// GROUND.md §6.3, armour: what is worn over the part hit takes its share of a bullet (the chest plate 25%, the metal
    /// mask 50%, a woven shirt 4%, together at most 80%); limbs it doesn't cover take it all.
    /// </summary>
    [Test]
    public static void ArmourReducesBulletDamage()
    {
        using var rig = new GameRig();
        GameContent c = rig.C;
        Entity bo = rig.Add("Bo", 10, 12);
        Player b = rig.P(bo);
        float Hit(HitRegion region)
        {
            b.Vitals.Health = 100;
            rig.Zone.ShootCharacter(bo, b, Guns.Round(c, "rifle", "rifle-ammo"), region, 10, Vector3.Zero, null);
            return 100 - b.Vitals.Health;
        }
        float bareBody = Hit(HitRegion.Body), bareHead = Hit(HitRegion.Head);
        foreach (string key in new[] { "chest-plate", "woven-shirt", "metal-mask" })
        {
            rig.Give(bo, key, 1);
            int slot = Enumerable.Range(0, 24).First(i => b.Inventory.Main[i].Item == c.ItemByKey(key).Id);
            Assert.Equal(ActionResult.Accepted, rig.Zone.QuickMove(bo, ContainerRef.Main, slot, 0), $"wear the {key}");
        }
        float body = Hit(HitRegion.Body), head = Hit(HitRegion.Head), limb = Hit(HitRegion.Limb);
        Fixtures.Report($"body {bareBody:0.0} → {body:0.00} (chest plate and shirt), head {bareHead:0.0} → {head:0.0} (metal mask), limb {limb:0.00} (shirt sleeves)");
        Assert.Near(35 * (1 - 0.25 - 0.04), body, 1e-3, "chest plate and shirt");
        Assert.Near(70 * 0.5, head, 1e-3, "the mask halves a head shot");
        Assert.Near(17.5 * (1 - 0.04), limb, 1e-3, "the shirt's sleeves cover the arms");
    }

    /// <summary>
    /// GROUND.md §6.3, what the server refuses: a shot faster than the cadence, from an empty magazine, while reloading,
    /// before the gun is up, while sprinting, for a gun not in hand, for a tick too long ago; a reload of a full magazine or
    /// without rounds. Only accepted shots take a round. A reload takes 2.4 s and fills the magazine from the inventory.
    /// </summary>
    [Test]
    public static void FireRateAmmoAndReloadAreChecked()
    {
        using var rig = new GameRig();
        GameContent c = rig.C;
        Entity ada = rig.Add("Ada", -10, 5);
        Player a = rig.P(ada);
        Guns.Arm(rig, ada, "pistol", "pistol-ammo", 2);
        uint seq = 0;
        GunResult Result(uint s) => a.Gun.LastSeq >= s && a.GunRefusedSeq == s ? a.GunRefused : a.Gun.LastSeq >= s ? GunResult.Accepted : (GunResult)255;

        // The pistol fires every 4.5 ticks: a shot the next tick is too soon.
        Guns.Queue(rig, ada, ++seq);
        rig.Step();
        Assert.Equal(GunResult.Accepted, Result(seq), "the first shot");
        Guns.Queue(rig, ada, ++seq);
        rig.Step();
        Assert.Equal(GunResult.Cadence, Result(seq), "too soon");
        Assert.Equal(1, (int)a.Inventory.Belt[0].Loaded, "a refused shot takes no round");
        rig.Step(4);
        Guns.Queue(rig, ada, ++seq);
        rig.Step();
        Assert.Equal(GunResult.Accepted, Result(seq), "the second shot");
        rig.Step(5);
        Guns.Queue(rig, ada, ++seq);
        rig.Step();
        Assert.Equal(GunResult.Empty, Result(seq), "the magazine is empty");

        // Reloading: none to reload with, then rounds; nothing fires meanwhile; full afterwards.
        Guns.Queue(rig, ada, ++seq, GunActionKind.Reload);
        rig.Step();
        Assert.Equal(GunResult.NoReserve, Result(seq), "no rounds");
        rig.Give(ada, "pistol-ammo", 40);
        Guns.Queue(rig, ada, ++seq, GunActionKind.Reload);
        rig.Step();
        Assert.Equal(GunResult.Accepted, Result(seq), "a reload");
        Assert.True(a.Gun.Reloading, "reloading");
        Guns.Queue(rig, ada, ++seq);
        rig.Step();
        Assert.Equal(GunResult.Reloading, Result(seq), "can't fire while reloading");
        rig.Step(71);
        Assert.Equal(15, (int)a.Inventory.Belt[0].Loaded, "full after 2.4 s");
        Assert.Equal(25, a.Inventory.Count(c.ItemByKey("pistol-ammo")), "the rounds came from the inventory");
        Guns.Queue(rig, ada, ++seq, GunActionKind.Reload);
        rig.Step();
        Assert.Equal(GunResult.Full, Result(seq), "already full");

        // Another kind of round: the loaded ones come back out.
        rig.Give(ada, "pistol-ammo-hv", 20);
        Guns.Queue(rig, ada, ++seq, GunActionKind.Reload, ammo: c.ItemByKey("pistol-ammo-hv").Id);
        rig.Step(73);
        Assert.Equal(c.ItemByKey("pistol-ammo-hv").Id, a.Inventory.Belt[0].LoadedAmmo, "high-velocity rounds loaded");
        Assert.Equal(40, a.Inventory.Count(c.ItemByKey("pistol-ammo")), "the plain ones back in the inventory");

        // Sprinting lowers the gun; an action for a gun not in hand, or a tick long gone, is refused.
        rig.Step(5, () => rig.Ch(ada).Input = rig.Ch(ada).Input with { Forward = 1, Buttons = MoveButtons.Sprint });
        Guns.Queue(rig, ada, ++seq);
        rig.Step(1, () => rig.Ch(ada).Input = rig.Ch(ada).Input with { Forward = 1, Buttons = MoveButtons.Sprint });
        Assert.Equal(GunResult.NotReady, Result(seq), "not while sprinting");
        rig.Ch(ada).Input = rig.Ch(ada).Input with { Forward = 0, Buttons = MoveButtons.None };
        rig.Step(10);
        Guns.Queue(rig, ada, ++seq, slot: 1);
        rig.Step();
        Assert.Equal(GunResult.Stale, Result(seq), "not the gun in hand");
        Assert.Equal(GunResult.TooLate, Guns.Queue(rig, ada, ++seq, tick: rig.Zone.Tick - 20), "a tick long gone");
        rig.Step();
        Assert.Equal(GunResult.TooLate, Result(seq), "answered in order");

        // Taking the gun up again takes its 0.5 s.
        rig.Step(2, () => rig.Ch(ada).Actions = new ActionInput { HeldSlot = 1 });
        rig.Step(2, () => rig.Ch(ada).Actions = new ActionInput { HeldSlot = 0 });
        Guns.Queue(rig, ada, ++seq);
        rig.Step();
        Assert.Equal(GunResult.Equipping, Result(seq), "still taking it up");
        Assert.Equal(GunResult.Stale, Guns.Queue(rig, ada, seq), "a repeated sequence number is dropped");
    }

    /// <summary>
    /// Attachments go on a gun that takes them (the scope not on the pistol), swap with what was there, come off into the
    /// inventory, and change the gun: a muzzle brake halves the kick, a silencer softens and slows the round.
    /// </summary>
    [Test]
    public static void AttachmentsGoOnAndChangeTheGun()
    {
        using var rig = new GameRig();
        GameContent c = rig.C;
        Entity ada = rig.Add("Ada", -12, -6);
        Player a = rig.P(ada);
        Guns.Arm(rig, ada, "pistol", "pistol-ammo", 15);
        foreach (string key in new[] { "scope", "muzzle-brake", "silencer" })
        {
            rig.Give(ada, key, 1);
        }
        int Slot(string key) => Enumerable.Range(0, 24).First(i => a.Inventory.Main[i].Item == c.ItemByKey(key).Id);
        Assert.Equal(ActionResult.NotAllowed, rig.Zone.Attach(ada, ContainerRef.Main, Slot("scope"), ContainerRef.Belt, 0, 0), "the pistol takes no scope");
        Assert.Equal(ActionResult.Accepted, rig.Zone.Attach(ada, ContainerRef.Main, Slot("muzzle-brake"), ContainerRef.Belt, 0, 0), "a muzzle brake on");
        Assert.Equal(c.ItemByKey("muzzle-brake").Id, a.Inventory.Belt[0].Muzzle, "on the muzzle");
        Guns.Queue(rig, ada, 1);
        rig.Step();
        Assert.Near(c.ItemByKey("pistol").Firearm!.Recoil.PitchPerShot * 0.5, a.Gun.Recoil.Pitch, 0.5 * 3 * 0.12 + 1e-9, "half the kick");

        int silencer = Slot("silencer");
        Assert.Equal(ActionResult.Accepted, rig.Zone.Attach(ada, ContainerRef.Main, silencer, ContainerRef.Belt, 0, 0), "a silencer swaps it");
        Assert.Equal(c.ItemByKey("muzzle-brake").Id, a.Inventory.Main[silencer].Item, "the brake back where the silencer was");
        rig.Zone.GameEvents.Clear();
        rig.Step(5);
        Guns.Queue(rig, ada, 2);
        rig.Step();
        ZoneEvent launch = Guns.Launches(rig).Single();
        Assert.True(launch.Flag, "silenced");
        Assert.Near(300 * 0.75, launch.Velocity.Length(), 0.01, "slower");
        Assert.Equal(ActionResult.Accepted, rig.Zone.Detach(ada, ContainerRef.Belt, 0, AttachmentSlot.Muzzle), "off again");
        Assert.True(a.Inventory.Belt[0].Muzzle == 0 && a.Inventory.Count(c.ItemByKey("silencer")) == 1, "into the inventory");
        Assert.Equal(13, (int)a.Inventory.Belt[0].Loaded, "the magazine stayed with the gun throughout");
    }
}
