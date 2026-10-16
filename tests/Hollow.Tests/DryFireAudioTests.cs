using System;
using System.Collections.Generic;
using Hollow.Client;
using Hollow.Net.Protocol;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;

namespace Hollow.Tests;

public static class DryFireAudioTests
{
    [Test]
    public static void EmptyGunsClickOncePerPressWithoutFiringOrSendingIntents()
    {
        foreach (string weapon in new[] { "pistol", "rifle", "rocket-launcher" })
        {
            using var rig = new GunAudioRig(weapon);
            rig.Ready();
            rig.Step(press: true);
            for (int i = 0; i < 45; i++) rig.Step(held: true);
            Assert.Equal(1, rig.Dry.Count, weapon + ": holding an empty trigger never repeats the click");
            Assert.Equal(rig.Weapon, rig.Dry[0], "feedback identifies the actual weapon");
            rig.Step();
            rig.Step(press: true);
            Assert.Equal(2, rig.Dry.Count, "a new press clicks again");
            Assert.Equal(0, rig.Shots, "dry fire produces no shot");
            Assert.Equal(0, rig.Sent, "dry fire sends no refused fire intent");
            Assert.Equal(0, rig.Local.Gun.Loaded, "dry fire leaves the empty magazine unchanged");
            Assert.Equal(0L, rig.Local.Gun.State.NextFire16, "dry fire does not start shot cadence or recoil");
        }
    }

    [Test]
    public static void DryFireRespectsEquipReloadCadenceAndCharacterGates()
    {
        using var empty = new GunAudioRig("rifle");
        empty.Step(press: true);
        Assert.Equal(0, empty.Dry.Count, "taking up the weapon is not dry fire");
        empty.Ready();
        foreach (MoveFlags flag in new[] { MoveFlags.Sprinting, MoveFlags.Swimming })
        {
            empty.Step(press: true, flags: flag);
            empty.Step();
        }
        empty.Step(press: true, stance: Stance.Seated);
        empty.Step(press: true, stance: Stance.Crawling);
        empty.Step(press: true, canAct: false);
        empty.Step(press: true, health: 0);
        Assert.Equal(0, empty.Dry.Count, "lowered, disabled, wounded and dead states stay silent");
        empty.Step();
        empty.Step(press: true);
        Assert.Equal(1, empty.Dry.Count, "ordinary standing input resumes feedback");

        using var loaded = new GunAudioRig("rifle", 1, 30);
        loaded.Ready();
        loaded.Step(press: true);
        Assert.Equal(1, loaded.Shots, "a valid shot still fires");
        Assert.Equal(0, loaded.Dry.Count, "a valid shot has no empty click");
        loaded.Step();
        loaded.Step(press: true);
        Assert.Equal(0, loaded.Dry.Count, "a fresh press inside shot cadence stays silent");
        for (int i = 0; i < 20; i++) loaded.Step(held: true);
        Assert.Equal(0, loaded.Dry.Count, "holding after the final round does not click later");
        loaded.Step();
        loaded.Step(press: true);
        Assert.Equal(1, loaded.Dry.Count, "a fresh ready press after the final round clicks");
        loaded.Step();
        loaded.Local.Gun.Reload();
        loaded.Step();
        Assert.True(loaded.Local.Gun.Reloading, "the real reload path started");
        loaded.Step(press: true);
        Assert.Equal(1, loaded.Dry.Count, "an empty magazine during reload is not dry fire");
    }

    private sealed class GunAudioRig : IDisposable
    {
        private readonly GameRig _rig = new();
        private readonly Predictor _predictor;
        private readonly Entity _player;
        private uint _tick;
        public LocalPlayer Local { get; }
        public ushort Weapon { get; }
        public List<ushort> Dry { get; } = new();
        public int Shots { get; private set; }
        public int Sent { get; private set; }

        public GunAudioRig(string key, ushort loaded = 0, uint reserve = 0)
        {
            _player = _rig.Add("Audio", 0, 0);
            _predictor = new Predictor(_rig.Zone.Spec, _rig.Zone.Spec.CreateSurface(), null);
            Weapon = _rig.C.ItemByKey(key).Id;
            Local = new LocalPlayer(_rig.C) { Send = _ => { Sent++; return true; } };
            Local.OnContents(new ContainerContents
            {
                Which = ContainerRef.Belt,
                Size = PlayerInventory.BeltSlots,
                Slots = new[] { new SlotNet
                {
                    Index = 0, Item = Weapon, Count = 1, Condition = ushort.MaxValue,
                    Gun = new[] { new GunNet { Loaded = loaded, LoadedAmmo = _rig.C.ItemByKey("rifle-ammo").Id } },
                } },
            });
            if (reserve > 0) Local.OnContents(new ContainerContents
            {
                Which = ContainerRef.Main, Size = 24,
                Slots = new[] { new SlotNet { Index = 0, Item = _rig.C.ItemByKey("rifle-ammo").Id, Count = reserve } },
            });
            Local.Update();
            Local.Gun.DryFired += item => Dry.Add(item.Id);
            Local.Gun.ShotFired += _ => Shots++;
        }

        public void Ready()
        {
            Step();
            for (int i = 0; !Local.Gun.Ready && i < 300; i++) Step();
            Assert.True(Local.Gun.Ready, "weapon equip finishes");
        }

        public void Step(bool press = false, bool held = false, bool canAct = true, MoveFlags flags = MoveFlags.OnGround,
            Stance stance = Stance.Standing, byte health = 100)
        {
            CharacterState state = _rig.Ch(_player).State;
            state.Flags = flags;
            state.Stance = stance;
            state.Health = health;
            (ushort u, ushort v) = NetConvert.ToView(Facing.View(0, 0, _rig.Zone.Frame.UpAt(state.Position)));
            var command = new InputCommand
            {
                Held = 0,
                ViewU = u, ViewV = v,
                Actions = (press || held ? ActionButtons.Primary : ActionButtons.None) |
                    (press ? ActionButtons.PrimaryPressed : ActionButtons.None),
            };
            Local.Gun.Tick(++_tick, command, state, _predictor.Frames, null, _predictor.Tuning, _rig.P(_player).Id, canAct);
        }

        public void Dispose()
        {
            _predictor.Dispose();
            _rig.Dispose();
        }
    }
}
