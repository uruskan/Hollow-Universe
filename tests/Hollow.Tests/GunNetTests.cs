using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Hollow.Client;
using Hollow.Core;
using Hollow.Net.Bits;
using Hollow.Net.Protocol;
using Hollow.Persistence;
using Hollow.Simulation;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Simulation.World;
using Hollow.Simulation.Targets;

namespace Hollow.Tests;

public static class GunNetTests
{
    /// <summary>
    /// GROUND.md §6.3–6.4: the client and the server compute the same recoil and the same bullet directions, to the bit,
    /// for a recorded stream of input: walking, strafing, crouching, aiming, bursts, a pause, a reload, and sprinting
    /// with the trigger held (refused on both sides alike). The client side is the real one (the predictor and
    /// <see cref="LocalGun"/>); its fire and reload intents go into the zone's queue as the server's network code puts them.
    /// </summary>
    [Test]
    public static void ClientAndServerAgreeOnRecoilAndDirections()
    {
        using var rig = new GameRig();
        GameContent c = rig.C;
        Entity ada = rig.Add("Ada", 0, 0);
        Player p = rig.P(ada);
        rig.Ch(ada).State.Health = 100; // as the vitals will have it (the rig set them)
        ItemStack rifle = ItemStack.Of(c.ItemByKey("rifle"), 1);
        rifle.Loaded = 30;
        rifle.LoadedAmmo = c.ItemByKey("rifle-ammo").Id;
        rifle.Sight = c.ItemByKey("holo-sight").Id;
        p.Inventory.Belt.Set(0, rifle);
        rig.Give(ada, "rifle-ammo", 60);

        // The client: its own ground and predictor, and the local player with the inventory the server would send.
        using var predictor = new Predictor(rig.Zone.Spec, rig.Zone.Spec.CreateSurface(), null);
        uint start = (uint)rig.Zone.Tick;
        predictor.Reset(start, rig.Ch(ada).State, start);
        var sent = new List<byte[]>();
        var local = new LocalPlayer(c)
        {
            Send = write =>
            {
                var w = new BitWriter(256);
                write(w);
                sent.Add(w.ToArray());
                return true;
            },
        };
        local.OnContents(Contents(ContainerRef.Belt, p.Inventory.Belt));
        local.OnContents(Contents(ContainerRef.Main, p.Inventory.Main));
        local.Update();
        var shots = new List<ShotEvent>();
        local.Gun.ShotFired += shots.Add;

        int launches = 0, reloads = 0, sprintTicks = 0;
        for (uint tick = start + 1; tick <= start + 240; tick++)
        {
            int t = (int)(tick - start);
            var move = new MoveInput
            {
                Forward = t is > 40 and < 120 ? 0.6f : t is >= 200 and < 220 ? 1 : 0,
                Right = t is > 60 and < 100 ? -0.8f : 0,
                Yaw = 0.3f + 0.002f * t,
                Pitch = 0.05f - 0.004f * (t % 50),
                Buttons = (t is > 80 and < 110 ? MoveButtons.Crouch : MoveButtons.None) | (t is >= 200 and < 220 ? MoveButtons.Sprint : MoveButtons.None),
            };
            bool burst = (t % 40) is > 5 and < 30;
            var actions = new ActionInput
            {
                HeldSlot = 0,
                Buttons = (burst ? ActionButtons.Primary : ActionButtons.None) | ((t % 40) == 6 ? ActionButtons.PrimaryPressed : ActionButtons.None) |
                    (t is > 100 and < 140 ? ActionButtons.Secondary : ActionButtons.None),
                ViewDelay = (byte)(t % 9),
            };
            if (t == 100)
            {
                local.Reload();
            }
            InputCommand command = NetConvert.ToCommand(move, actions);
            predictor.RecordInput(tick, command);
            predictor.Predict(tick);
            int before = shots.Count;
            local.Gun.Tick(tick, command, predictor.Latest, predictor.Frame, predictor.Tuning, p.Id, canAct: true);
            foreach (byte[] packet in sent)
            {
                var r = new BitReader(packet);
                uint type = r.ReadBits(8);
                if (type == FireIntent.TypeId)
                {
                    FireIntent m = FireIntent.Read(r);
                    rig.Zone.QueueGunAction(ada, new GunAction
                    {
                        Seq = m.Seq, Tick = m.Tick, Kind = GunActionKind.Fire, Slot = m.Slot, Item = (ushort)m.Item,
                        Yaw = Quantize.FromAngle16(m.Yaw), Pitch = Quantize.FromAngle16(m.Pitch), Aimed = m.Aimed, ViewDelay = m.ViewDelay,
                    });
                }
                else if (type == ReloadIntent.TypeId)
                {
                    ReloadIntent m = ReloadIntent.Read(r);
                    rig.Zone.QueueGunAction(ada, new GunAction
                    {
                        Seq = m.Seq, Tick = m.Tick, Kind = GunActionKind.Reload, Slot = m.Slot, Item = (ushort)m.Item, Ammo = (ushort)m.Ammo,
                    });
                    reloads++;
                }
            }
            sent.Clear();

            // The server's tick, with the same input.
            ref Character ch = ref rig.Ch(ada);
            ch.Input = NetConvert.ToMoveInput(command);
            ch.Actions = NetConvert.ToActions(command);
            rig.Zone.GameEvents.Clear();
            rig.Step();
            Assert.True(predictor.Latest.Equals(rig.Ch(ada).State), $"tick {t}: the movement agrees");
            if ((rig.Ch(ada).State.Flags & MoveFlags.Sprinting) != 0 && burst)
            {
                sprintTicks++;
            }
            List<ZoneEvent> fired = Guns.Launches(rig);
            Assert.Equal(fired.Count, shots.Count - before, $"tick {t}: shots on both sides");
            for (int i = 0; i < fired.Count; i++)
            {
                ShotEvent shot = shots[before + i];
                Assert.True(shot.Direction * shot.Speed == fired[i].Velocity, $"tick {t}: the same direction to the bit ({shot.Direction * shot.Speed} / {fired[i].Velocity})");
                Assert.True(shot.Origin == fired[i].Position, $"tick {t}: from the same eye");
                launches++;
            }
            Assert.True(local.Gun.State.Recoil.Equals(p.Gun.Recoil), $"tick {t}: the same recoil ({local.Gun.State.Recoil.Pitch} / {p.Gun.Recoil.Pitch})");
            Assert.Equal(p.Gun.NextFire16, local.Gun.State.NextFire16, $"tick {t}: the same cadence");
            Assert.Equal((int)p.Inventory.Belt[0].Loaded, local.Gun.Loaded, $"tick {t}: the same magazine");
        }
        Fixtures.Report($"{launches} shots and {reloads} reload in 8 s of recorded input, every one the same on both sides; " +
            $"{sprintTicks} ticks of sprinting with the trigger held fired nothing on either side");
        Assert.True(launches >= 20 && reloads == 1 && sprintTicks >= 10, "bursts, a reload, sprinting");
        Assert.True(p.GunRefusedSeq == 0, $"the server refused nothing ({p.GunRefused})");
    }

    private static ContainerContents Contents(ContainerRef which, Container container) => new()
    {
        Which = which,
        Size = (byte)container.Size,
        Slots = Enumerable.Range(0, container.Size).Where(i => !container[i].IsEmpty).Select(i =>
        {
            ItemStack s = container[i];
            return new SlotNet
            {
                Index = (byte)i, Item = s.Item, Count = (uint)s.Count, Condition = s.Condition,
                Gun = s.HasWeaponState ? new[] { new GunNet { Loaded = s.Loaded, LoadedAmmo = s.LoadedAmmo, Sight = s.Sight, Muzzle = s.Muzzle, Rail = s.Rail } } : null!,
            };
        }).ToArray(),
    };

    // RustMVP's AK table (its docs/evidence/ak-playtest-2026-10-01/old-rust-ak-angular-reference.json, the dated community
    // table its pattern's steps were taken from): where the view is before each of the 30 shots, in degrees; x to the
    // right, y down (pitch = −y).
    private static readonly double[] AkTableX =
    {
        0.0, 1.3907062632285214, 1.1764334502408484, 3.3871707461173646, 5.0870491851652755, 5.094189086829459, 4.426012801154849,
        3.2504552119818904, 1.7354512031510199, 0.04893565850270232, -1.6411565381226527, -3.166890502884598, -4.360331351942659,
        -5.053544201456452, -5.090650616437088, -4.489915538556545, -3.382553537114461, -1.8995856377872116, -0.17203286625115766,
        1.6690837518171975, 3.4927431907415496, 5.1679244248454665, 6.56360642845253, 7.548768175886437, 7.992388641470555,
        7.5122256391478, 6.06379234579299, 4.11736515892494, 2.1439320548176966, 0.6144810097516711,
    };

    private static readonly double[] AkTableY =
    {
        -0.0, -2.0039412840665776, -3.8441756774048494, -5.516686177315278, -7.017455781098323, -8.342467486054444, -9.487704289484107,
        -10.449149188687768, -11.222785180965893, -11.804595263618937, -12.190562433947365, -12.587131622128602, -13.320773253380537,
        -14.321275417643292, -15.511026930930969, -16.81241660925766, -18.147833268637438, -19.439665725084474, -20.61030279461278,
        -21.582133293236506, -22.27754603696978, -22.618929841826656, -22.817776859076133, -23.373894638142545, -24.211385718746534,
        -25.237342069507864, -26.358855659045446, -27.483018455978456, -28.51692242892666, -29.36765954650875,
    };

    /// <summary>
    /// GROUND.md §6.4, the AK exactly RustMVP's: two 30-round sprays, standing at the hip and then aimed and crouched, put
    /// the view where RustMVP's table does before every shot (its 30 cumulative points), nothing random and nothing scaled,
    /// on the client's prediction (its recoil, and the camera's kick at the shot) and on the server alike, to the bit;
    /// 4 ticks apart (450 a minute); after the 30th shot the view holds the table's last point (the pattern's zero tail).
    /// </summary>
    [Test]
    public static void AkSprayMatchesRustMvpTable()
    {
        using var rig = new GameRig();
        GameContent c = rig.C;
        Entity ada = rig.Add("Ada", 0, 0);
        Player p = rig.P(ada);
        rig.Ch(ada).State.Health = 100;
        ItemStack rifle = ItemStack.Of(c.ItemByKey("rifle"), 1);
        rifle.Loaded = 30;
        rifle.LoadedAmmo = c.ItemByKey("rifle-ammo").Id;
        p.Inventory.Belt.Set(0, rifle);
        rig.Give(ada, "rifle-ammo", 30);

        using var predictor = new Predictor(rig.Zone.Spec, rig.Zone.Spec.CreateSurface(), null);
        uint start = (uint)rig.Zone.Tick;
        predictor.Reset(start, rig.Ch(ada).State, start);
        var sent = new List<byte[]>();
        var local = new LocalPlayer(c)
        {
            Send = write =>
            {
                var w = new BitWriter(256);
                write(w);
                sent.Add(w.ToArray());
                return true;
            },
        };
        local.OnContents(Contents(ContainerRef.Belt, p.Inventory.Belt));
        local.OnContents(Contents(ContainerRef.Main, p.Inventory.Main));
        local.Update();
        var shots = new List<ShotEvent>();
        local.Gun.ShotFired += shots.Add;

        // The first spray as soon as the rifle is up; then a reload; the second spray aimed and crouched, once crouched.
        var sprays = new[] { new List<uint>(), new List<uint>() };
        int spray = 0;
        uint secondFrom = uint.MaxValue;
        double worst = 0;
        for (uint tick = start + 1; tick <= start + 400 && sprays[1].Count < 30; tick++)
        {
            int t = (int)(tick - start);
            if (spray == 0 && sprays[0].Count == 30)
            {
                if (local.Gun.Loaded == 0 && !local.Gun.Reloading)
                {
                    local.Reload();
                }
                else if (local.Gun.Loaded == 30 && !local.Gun.Reloading)
                {
                    spray = 1;
                    secondFrom = tick + 10;
                }
            }
            bool fire = spray == 0 ? sprays[0].Count < 30 : tick >= secondFrom;
            var move = new MoveInput { Yaw = 0.4f, Pitch = 0.05f, Buttons = spray == 1 ? MoveButtons.Crouch : MoveButtons.None };
            var actions = new ActionInput
            {
                HeldSlot = 0,
                Buttons = (fire ? ActionButtons.Primary : ActionButtons.None) | (spray == 1 ? ActionButtons.Secondary : ActionButtons.None),
            };
            InputCommand command = NetConvert.ToCommand(move, actions);
            predictor.RecordInput(tick, command);
            predictor.Predict(tick);
            int before = shots.Count;
            local.Gun.Tick(tick, command, predictor.Latest, predictor.Frame, predictor.Tuning, p.Id, canAct: true);
            foreach (byte[] packet in sent)
            {
                var r = new BitReader(packet);
                uint type = r.ReadBits(8);
                if (type == FireIntent.TypeId)
                {
                    FireIntent m = FireIntent.Read(r);
                    rig.Zone.QueueGunAction(ada, new GunAction
                    {
                        Seq = m.Seq, Tick = m.Tick, Kind = GunActionKind.Fire, Slot = m.Slot, Item = (ushort)m.Item,
                        Yaw = Quantize.FromAngle16(m.Yaw), Pitch = Quantize.FromAngle16(m.Pitch), Aimed = m.Aimed, ViewDelay = m.ViewDelay,
                    });
                }
                else if (type == ReloadIntent.TypeId)
                {
                    ReloadIntent m = ReloadIntent.Read(r);
                    rig.Zone.QueueGunAction(ada, new GunAction
                    {
                        Seq = m.Seq, Tick = m.Tick, Kind = GunActionKind.Reload, Slot = m.Slot, Item = (ushort)m.Item, Ammo = (ushort)m.Ammo,
                    });
                }
            }
            sent.Clear();
            ref Character ch = ref rig.Ch(ada);
            ch.Input = NetConvert.ToMoveInput(command);
            ch.Actions = NetConvert.ToActions(command);
            rig.Zone.GameEvents.Clear();
            rig.Step();
            Assert.True(local.Gun.State.Recoil.Equals(p.Gun.Recoil), $"tick {t}: the client's recoil is the server's, to the bit");
            List<ZoneEvent> fired = Guns.Launches(rig);
            Assert.Equal(fired.Count, shots.Count - before, $"tick {t}: shots on both sides");
            if (fired.Count == 0)
            {
                continue;
            }
            int k = sprays[spray].Count;
            sprays[spray].Add(tick);
            Assert.True(shots[^1].Aimed == (spray == 1) && (spray == 0) == (rig.Ch(ada).State.Stance == Stance.Standing),
                $"spray {spray + 1}, shot {k + 1}: at the hip standing, then aimed and crouched");
            // Before the shot's own kick the view was at the table's point k: the recoil's start, the same on both sides
            // (checked above), and the camera at the shot's tick.
            RecoilState after = p.Gun.Recoil;
            double pitch = -AkTableY[k], yaw = AkTableX[k];
            worst = Math.Max(worst, Math.Max(Math.Abs(after.StartPitch - pitch), Math.Abs(after.StartYaw - yaw)));
            Assert.Near(pitch, after.StartPitch, 1e-9, $"spray {spray + 1}, shot {k + 1}: pitch before the kick");
            Assert.Near(yaw, after.StartYaw, 1e-9, $"spray {spray + 1}, shot {k + 1}: yaw before the kick");
            local.Gun.Render(tick);
            (float kickYaw, float kickPitch) = local.Gun.ViewKick;
            Assert.Near(pitch * DMath.DegToRad, kickPitch, 1e-6, $"spray {spray + 1}, shot {k + 1}: the camera's pitch");
            Assert.Near(-yaw * DMath.DegToRad, kickYaw, 1e-6, $"spray {spray + 1}, shot {k + 1}: the camera's yaw (+ is left)");
            if (k > 0)
            {
                Assert.Equal(4L, (long)(tick - sprays[spray][k - 1]), $"spray {spray + 1}, shot {k + 1}: 450 a minute, 4 ticks apart");
            }
            if (k == 29)
            {
                Assert.Near(-AkTableY[29], after.Pitch, 1e-9, $"spray {spray + 1}: the view ends on the table's last point, pitch");
                Assert.Near(AkTableX[29], after.Yaw, 1e-9, $"spray {spray + 1}: and yaw (the pattern's last step is zero)");
            }
        }
        Assert.Equal(30, sprays[0].Count, "the first spray: 30 shots");
        Assert.Equal(30, sprays[1].Count, "the second spray, aimed and crouched: 30 shots");
        Assert.True(p.GunRefusedSeq == 0, $"the server refused nothing ({p.GunRefused})");
        Fixtures.Report($"two 30-round sprays (hip; aimed and crouched): every view before a shot on RustMVP's table within {worst:0.0e0}°, " +
            $"client and server to the bit; it ends {-AkTableY[29]:0.000}° up, {AkTableX[29]:0.000}° right");
    }

    private static void RunUntil(LoopbackRig rig, Func<bool> done, string what, Func<GameClient, double, InputFrame> input, double seconds = 8)
    {
        var watch = Stopwatch.StartNew();
        while (!done())
        {
            Assert.True(watch.Elapsed.TotalSeconds < seconds, $"timed out waiting for {what}");
            rig.Run(0.05, input);
        }
    }

    /// <summary>
    /// GROUND.md §6.4, a gunfight over the network: Ada takes up a rifle and reloads (predicted at once, the server's
    /// magazine follows), then fires at Bo; her shots show at once, the server accepts every one (nothing refused, nothing
    /// left pending), her magazine agrees with the server's, she gets hit markers and Bo the damage; the body shot that
    /// would kill him knocks him down (both hear of it); Ada helps him up with the interact key.
    /// </summary>
    [Test]
    public static void GunfightOverTheNetwork()
    {
        using var rig = new LoopbackRig();
        GameClient ada = rig.Join("Ada");
        GameClient bo = rig.Join("Bo");
        rig.WaitUntilPlaying();
        GameContent c = ada.Content;
        rig.Server.Execute("teleport Ada -3 0");
        rig.Server.Execute("teleport Bo 3 0");
        rig.Server.Execute("give Ada rifle 1");
        rig.Server.Execute("give Ada rifle-ammo 64");
        InputFrame Still(GameClient who, double _) => who == bo ? new InputFrame { Yaw = MathF.PI / 2 } : new InputFrame { Yaw = -MathF.PI / 2 };
        RunUntil(rig, () => ada.Local.Inventory.Count(c.ItemByKey("rifle-ammo")) == 64 && ada.Local.Inventory.Count(c.ItemByKey("rifle")) == 1,
            "the rifle and rounds", Still);
        int rifleSlot = Enumerable.Range(0, 24).First(i => ada.Local.Inventory.Main[i].Item == c.ItemByKey("rifle").Id);
        ada.Local.MoveItem(ContainerRef.Main, rifleSlot, ContainerRef.Belt, 2);
        ada.Local.HeldSlot = 2;
        RunUntil(rig, () => ada.Local.Gun.Ready, "the rifle taken up", Still);

        var events = new List<string>();
        ada.Local.Gun.ShotFired += _ => events.Add("shot");
        ada.Local.Gun.ReloadStarted += _ => events.Add("reload");
        ada.Local.Gun.ReloadFinished += _ => events.Add("reloaded");
        ada.Local.Gun.Refused += r => events.Add($"refused {r.Result}");
        var markers = new List<HitMarkerEvent>();
        ada.Local.HitMarker += markers.Add;
        var damage = new List<DamageEvent>();
        bo.Local.DamageTaken += damage.Add;
        bool boDown = false, boUp = false;
        bo.Local.Downed += _ => boDown = true;
        bo.Local.GotUp += () => boUp = true;

        // Reload: shown at once, done 2.4 s later on both sides.
        ada.Local.Reload();
        rig.Run(0.1, Still);
        Assert.True(ada.Local.Gun.Reloading && events.Contains("reload"), "reloading at once");
        RunUntil(rig, () => ada.Local.Gun.Loaded == 30 && !ada.Local.Gun.Reloading && ada.Local.Gun.PendingCount == 0, "the magazine full", Still);
        Assert.True(events.Contains("reloaded"), "the reload's end");
        RunUntil(rig, () => ada.Local.Inventory.Count(c.ItemByKey("rifle-ammo")) == 34, "the rounds out of the inventory", Still);

        // Fire at Bo's chest, a few rounds at a time, until he is down.
        Vector3 chest = rig.Server.Inspect(sv =>
        {
            sv.Zone.TryFindPlayer(bo.EntityId, out Entity e, out _);
            Vector3 at = sv.Zone.Entities.Get<Character>(e).State.Position;
            return at + sv.Zone.Frame.UpAt(at) * 1.15f;
        });
        double shotsFrom = 0;
        InputFrame Shoot(GameClient who, double time)
        {
            if (who == bo)
            {
                return new InputFrame { Yaw = MathF.PI / 2 };
            }
            Vector3 eye = ada.RenderPosition + ada.Frame.UpAt(ada.RenderPosition) * ada.EyeHeight;
            Vector3 d = chest - eye, up = ada.Frame.UpAt(eye), flat = d - up * Vector3.Dot(d, up);
            // One shot a burst (a tap): the pattern starts again each time, so every round goes where she aims.
            bool firing = time - shotsFrom < 0.05;
            return new InputFrame
            {
                Yaw = MathF.Atan2(-flat.X, -flat.Z), Pitch = MathF.Atan2(Vector3.Dot(d, up), flat.Length()), Primary = firing, Secondary = true,
                PrimaryPressed = firing,
            };
        }
        for (int burst = 0; burst < 8 && !boDown; burst++)
        {
            shotsFrom = 0;
            rig.Run(0.6, Shoot);
        }
        RunUntil(rig, () => boDown, "Bo down", Still);
        RunUntil(rig, () => ada.Local.Gun.PendingCount == 0, "every shot answered", Still);
        int fired = events.Count(e => e == "shot");
        int serverLoaded = rig.Server.Inspect(sv =>
        {
            sv.Zone.TryFindPlayer(ada.EntityId, out _, out Player p);
            return (int)p.Inventory.Belt[2].Loaded;
        });
        Fixtures.Report($"Ada fired {fired}, {markers.Count(m => m.Target == TargetFamily.Character)} hit Bo ({string.Join(", ", markers.Select(m => $"{m.Region} {m.Damage:0.#}"))}); " +
            $"Bo took {damage.Sum(d => d.Damage):0.#}; magazine {ada.Local.Gun.Loaded} here, {serverLoaded} on the server; {string.Join(", ", events.Where(e => e.StartsWith("refused", StringComparison.Ordinal)))}");
        Assert.True(!events.Any(e => e.StartsWith("refused", StringComparison.Ordinal)), "nothing refused");
        Assert.Equal(serverLoaded, ada.Local.Gun.Loaded, "the predicted magazine is the server's");
        Assert.Equal(30 - fired, serverLoaded, "every predicted shot was a shot on the server");
        // Bo joined with a new life's 60 health: the second body shot knocks him down.
        Assert.True(markers.Count(m => m.Target == TargetFamily.Character) >= 2 && markers.Last().Downed, "hit markers, the last one 'downed'");
        Assert.True(damage.Count >= 2 && bo.Local.IsWounded, "Bo took the damage and is down");

        // Ada helps him up.
        rig.Server.Execute("teleport Ada Bo");
        rig.Run(0.3, Still);
        ada.Local.Interact(new Hollow.Simulation.Targets.TargetRef(Hollow.Simulation.Targets.TargetFamily.Character, bo.EntityId.Value), "help-up");
        RunUntil(rig, () => boUp && !bo.Local.IsWounded, "Bo up again", Still);
    }

    /// <summary>
    /// GROUND.md §4.7, persistence of weapons: a character's rifle keeps its rounds and attachments, a pistol in a corpse
    /// keeps its own; stored bytes are untrusted, so rounds of the wrong calibre and attachments the gun doesn't take are
    /// dropped, and a magazine can't hold more than it does. A character saved while down comes back dead, in a corpse.
    /// </summary>
    [Test]
    public static void WeaponsKeepTheirMagazinesAndAttachments()
    {
        using var rig = new GameRig();
        GameContent c = rig.C;
        Entity ada = rig.Add("Ada", 4, 4);
        Player a = rig.P(ada);
        ItemStack rifle = ItemStack.Of(c.ItemByKey("rifle"), 1);
        rifle.Loaded = 17;
        rifle.LoadedAmmo = c.ItemByKey("rifle-ammo-hv").Id;
        rifle.Sight = c.ItemByKey("scope").Id;
        rifle.Muzzle = c.ItemByKey("silencer").Id;
        rifle.Rail = c.ItemByKey("weapon-light").Id;
        rifle.Condition = 123;
        a.Inventory.Belt.Set(1, rifle);
        ItemStack odd = ItemStack.Of(c.ItemByKey("pistol"), 1);
        odd.Loaded = 99;
        odd.LoadedAmmo = c.ItemByKey("rifle-ammo").Id;
        odd.Sight = c.ItemByKey("scope").Id;
        odd.Muzzle = c.ItemByKey("muzzle-brake").Id;
        a.Inventory.Main.Set(3, odd);
        a.Vitals.Burning = 4;
        a.Body = 1;

        Player back = WorldCodec.DecodePlayer(WorldCodec.EncodePlayer(a), a.Id, a.Name, c);
        Assert.Equal(1, back.Body, "the body chosen");
        Assert.True(back.Inventory.Belt[1] == rifle, $"the rifle as it was: {back.Inventory.Belt[1]}");
        ItemStack pistol = back.Inventory.Main[3];
        Assert.True(pistol.Loaded == 0 && pistol.LoadedAmmo == 0, "rifle rounds don't stay in a pistol");
        Assert.True(pistol.Sight == 0 && pistol.Muzzle == c.ItemByKey("muzzle-brake").Id, "nor a scope; the brake does");
        Assert.Equal(4f, back.Vitals.Burning, "burning");

        // In a corpse (a static thing), through the thing's own record.
        var corpse = new Corpse(new EntityId(77), "Ada", a.Id, 37, rig.Zone.GroundAt(4, 4), Vector3.UnitY, 0, c.Survival.BodyLifetime, rig.Zone.Tick + 900);
        corpse.Loot.Set(5, rifle);
        WorldObject? thing = WorldCodec.DecodeThing(corpse.Id, StaticKind.Corpse,
            WorldCodec.EncodeThing(corpse, rig.Zone.Frame.ToBody(corpse.Position), rig.Zone.Tick), c, rig.Zone.Frame.ToLocal, rig.Zone.Frame.UpAt, rig.Zone.Tick);
        Assert.True(thing is Corpse { } k && k.Loot[5] == rifle, "the rifle in the corpse, as it was");

        // Down when saved: back as a corpse with everything.
        a.Vitals.Health = 9;
        a.Wounded = true;
        a.WoundedCause = "Shot";
        a.WoundedByName = "Bo";
        Player saved = WorldCodec.DecodePlayer(WorldCodec.EncodePlayer(a), new EntityId(0xADA2), "Ada2", c);
        Assert.True(saved.Wounded, "stored down");
        Entity again = rig.Zone.AddPlayer(saved.Id, saved.Name, rig.Zone.NewCharacter(rig.Zone.GroundAt(6, 6)), saved);
        Assert.True(saved.Dead && saved.KilledBy == "Bo", "added down: dead, killed by who downed them");
        Assert.True(rig.Zone.Statics().OfType<Corpse>().Any(k => k.Of == saved.Id && k.Loot.Slots.ToArray().Contains(rifle)), "their corpse has the rifle");
        Assert.True(rig.Zone.Entities.Get<Character>(again).State.IsDead, "and the character is dead");
    }
}
