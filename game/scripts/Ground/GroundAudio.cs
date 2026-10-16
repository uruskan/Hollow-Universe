using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Hollow.Client;
using Hollow.Client.Audio;
using Hollow.Core;
using Hollow.Net.Protocol;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Nature;
using Hollow.Surface;
using HollowUniverse.Audio;
using HollowUniverse.Surface;
using HollowUniverse.UI;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

/// <summary>
/// The ground game's sound (GROUND.md §4.9), through the audio package: one <see cref="AudioDirector"/> listening at
/// the camera, the <see cref="AmbienceDirector"/> fed from where the camera is (height, biome, trees around, the sea,
/// the sun), the player's own feet, and the client's events (pickups, hurt, death, cold, containers, hit markers,
/// crafting, refusals, notices) and the interface's (<see cref="GroundUi.Sound"/>). The views play what they draw
/// through <see cref="Sounds"/>: others' feet and actions, hits on trees and outcrops, fires, deployables, arrows.
/// </summary>
public partial class GroundAudio : Node
{
    public GameClient Client { get; set; } = null!;

    public Camera3D Camera { get; set; } = null!;

    public SurfaceView? World { get; set; }

    /// <summary>Prints every sound started (--sound-trace).</summary>
    public bool Trace { get; set; }

    private AudioDirector _director = null!;
    private AmbienceDirector _ambience = null!;
    private FootstepDriver _feet = null!;
    private FootSurface _surface = FootSurface.Grass;
    private Num.Vector3 _surfaceAt = new(float.MaxValue);
    private ulong _surfaceFrame = ulong.MaxValue;
    private double _surfaceIn;
    private double _ambienceIn;
    private double _coldIn = 5;
    private AmbienceInput _place = new() { Biome = Biome.TemperateForest, SeaDistance = 5000, Exposure = 0.4, Wind = 0.3 };
    private Task<AmbienceInput>? _placeJob;
    private double _placeIn;
    private Num.Vector3? _looted;
    private string? _lootedKey;
    private readonly Random _rng = new(0x50D5);

    /// <summary>Calm, constant wind until there is weather (GROUND.md §11).</summary>
    public const double Wind = 0.3;

    public override void _Ready()
    {
        _director = new AudioDirector { Name = "AudioDirector", Listener = Camera, OcclusionMask = 1 };
        AddChild(_director);
        _ambience = new AmbienceDirector { Name = "Ambience", Director = _director };
        AddChild(_ambience);
        _feet = new FootstepDriver { Name = "OwnFeet", Local = true, Director = _director };
        AddChild(_feet);
        // Everything but the ambience beds (streamed when heard), so a first play never waits on the disk.
        SoundLibrary.Preload(SoundLibrary.Data.Events.Select(e => e.Key).Where(k => !k.StartsWith("amb.", StringComparison.Ordinal)));
        GroundUi.Sound = key => _director.Play2D(key);
        if (Trace)
        {
            _director.Started += (key, layer, distance, db, pitch) => GD.Print($"[sound] {key}{(layer.Length > 0 ? "/" + layer : "")} {db:0.0} dB at {distance:0} m");
        }

        LocalPlayer me = Client.Local;
        me.Gun.DryFired += OnDryFire;
        me.ItemsChanged += OnItems;
        me.HitMarker += OnHitMarker;
        me.DamageTaken += OnDamage;
        me.Died += OnDied;
        me.CraftFinished += OnCrafted;
        me.IntentRejected += OnRefused;
        me.ContainerOpened += OnOpened;
        me.ContainerClosed += OnClosed;
        Client.ChatReceived += OnChat;
    }

    public override void _ExitTree()
    {
        if (GroundUi.Sound != null)
        {
            GroundUi.Sound = null;
        }
        LocalPlayer me = Client.Local;
        me.Gun.DryFired -= OnDryFire;
        me.ItemsChanged -= OnItems;
        me.HitMarker -= OnHitMarker;
        me.DamageTaken -= OnDamage;
        me.Died -= OnDied;
        me.CraftFinished -= OnCrafted;
        me.IntentRejected -= OnRefused;
        me.ContainerOpened -= OnOpened;
        me.ContainerClosed -= OnClosed;
        Client.ChatReceived -= OnChat;
    }

    /// <summary>Every frame: the player's feet, the ambience (four times a second) and the cold.</summary>
    public void Refresh(double delta, bool sprinting)
    {
        if (Client.HasCharacter && !Client.Local.IsDead)
        {
            UpdateOwnFeet(delta, sprinting);
        }
        else
        {
            _feet.Reset();
            _surfaceIn = 0;
        }
        _ambienceIn -= delta;
        if (_ambienceIn <= 0)
        {
            _ambienceIn = 0.25;
            UpdateAmbience(0.25);
        }
        _coldIn -= delta;
        if (_coldIn <= 0)
        {
            _coldIn = 4 + 3 * _rng.NextDouble();
            if (Client.HasCharacter && !Client.Local.IsDead && Client.Local.Vitals.Cold > 0.5f)
            {
                _director.Play2D("survival.cold");
            }
        }
    }

    // ---------------------------------------------------------------- the player's feet

    private void UpdateOwnFeet(double delta, bool sprinting)
    {
        CharacterState c = Client.Character;
        Num.Vector3 feet = Client.Prediction?.WorldPose(0).Feet ?? Client.RenderPosition;
        _surfaceIn -= delta;
        if (_surfaceIn <= 0 || c.Frame != _surfaceFrame || Num.Vector3.Distance(feet, _surfaceAt) > 0.6f)
        {
            _surfaceAt = feet;
            _surfaceFrame = c.Frame;
            _surfaceIn = 0.25;
            _surface = SurfaceAt(Client, feet, c.Frame);
        }
        FootInput input = Footsteps.Input(c, Client.Frame, sprinting, _surface, c.Frame == 0 ? WaterDepth(Client, feet) : 0);
        _feet.Update(delta, input, GroundView.ToGodot(feet));
    }

    /// <summary>What the ground sounds like underfoot at a point (zone frame): the strongest ground material there.</summary>
    public static FootSurface SurfaceAt(GameClient client, Num.Vector3 local, ulong movementFrame = 0) =>
        client.Surface is { } surface ? FootstepEnvironment.At(surface, client.Frame, client.Building().World, local, movementFrame) : FootSurface.Grass;

    /// <summary>
    /// The ground's main material at a point (zone frame): grass, dirt, sand, rock… as the terrain paints it there
    /// (<see cref="ISurface.Cover"/>, any surface); grass before the world is known.
    /// </summary>
    public static GroundMaterial MaterialAt(GameClient client, Num.Vector3 local)
    {
        if (client.Surface is not { } surface)
        {
            return GroundMaterial.Grass;
        }
        return FootstepEnvironment.MaterialAt(surface, client.Frame, local);
    }

    /// <summary>Sea or inland water over a point (zone frame), metres; 0 on land.</summary>
    public static double WaterDepth(GameClient client, Num.Vector3 local)
    {
        return client.Surface is { } surface ? FootstepEnvironment.WaterDepth(surface, client.Frame, local) : 0;
    }

    // ---------------------------------------------------------------- ambience

    private void UpdateAmbience(double delta)
    {
        if (Client.Surface is not { } surface)
        {
            return;
        }
        Vector3 ear = Camera.GlobalPosition;
        Num.Vector3 local = new(ear.X, ear.Y, ear.Z);
        // Where the listener is (biome, trees, sea, exposure) is worked out on a worker every few seconds.
        _placeIn -= delta;
        if (_placeJob is { IsCompleted: true } done)
        {
            if (done.IsCompletedSuccessfully)
            {
                _place = done.Result;
            }
            _placeJob = null;
        }
        if (_placeIn <= 0 && _placeJob == null)
        {
            _placeIn = 3;
            ZoneFrame frame = Client.Frame;
            INaturalObjects? nature = Client.Survival().Nature;
            int trees = CountTrees(nature, Client.Content, local);
            _placeJob = Task.Run(() => Place(surface, frame, local, trees));
        }
        Vector3D body = Client.Frame.ToBody(local);
        AmbienceInput input = _place;
        input.Altitude = body.Length - (surface.SeaRadius > 0 ? surface.SeaRadius : surface.Radius);
        input.SunElevation = World?.Sky?.SunElevation ?? 30;
        input.Wind = Wind;
        _ambience.Up = GroundView.ToGodot(Client.Frame.UpAt(local));
        _ambience.Update(delta, input, ear);
    }

    // Trees within 30 m (main thread: the scatter tiles there are generated already).
    private static int CountTrees(INaturalObjects? nature, GameContent content, Num.Vector3 at)
    {
        if (nature == null)
        {
            return 0;
        }
        var list = new List<NaturalObject>();
        nature.Query(at, 30, list);
        int trees = 0;
        foreach (NaturalObject o in list)
        {
            if (content.Natural(o.Kind)?.Class == NaturalClass.Tree)
            {
                trees++;
            }
        }
        return trees;
    }

    // On a worker: the biome, how open the place is (higher than the ground around), and how far the sea is.
    private static AmbienceInput Place(ISurface surface, ZoneFrame frame, Num.Vector3 at, int trees)
    {
        var input = new AmbienceInput { Wind = Wind, Forest = Math.Clamp(trees / 22.0, 0, 1) };
        Vector3D here = frame.ToBody(at).Normalized;
        var tangent = new ZoneFrame(here * surface.Radius);
        input.Biome = surface.BiomeAt(here);
        double h0 = surface.HeightAt(here);
        double around = 0;
        double sea = 5000;
        int n = 0;
        foreach (double r in new[] { 150.0, 400, 900, 1600 })
        {
            for (int i = 0; i < 8; i++)
            {
                double a = i * Math.PI / 4;
                double angle = r / surface.Radius;
                Vector3D along = tangent.East * Math.Cos(a) + tangent.South * Math.Sin(a);
                Vector3D d = here * Math.Cos(angle) + along * Math.Sin(angle);
                double h = surface.HeightAt(d);
                if (r < 200)
                {
                    around += h;
                    n++;
                }
                if (surface.SeaRadius > 0 && h < 0)
                {
                    sea = Math.Min(sea, r);
                }
            }
        }
        if (surface.SeaRadius > 0 && h0 < 1.5)
        {
            sea = 0;
        }
        input.SeaDistance = sea;
        input.Exposure = Math.Clamp(0.35 + (h0 - around / Math.Max(1, n)) / 40, 0, 1) * (1 - 0.6 * input.Forest);
        return input;
    }

    // ---------------------------------------------------------------- the client's events

    private void OnItems(ItemsChangedEvent e)
    {
        if (e.Reason != GainReason.PickedUp || e.Count <= 0)
        {
            return;
        }
        string key = e.Item.Key switch
        {
            "red-berries" or "blue-berries" => "gather.berry",
            "fibre" or "wild-tuber" => "gather.plant",
            _ => "gather.pickup",
        };
        _director.Play2D(key);
    }

    private void OnHitMarker(HitMarkerEvent e)
    {
        if (e.Target != Hollow.Simulation.Targets.TargetFamily.Character)
        {
            return;
        }
        _director.Play2D(e.Killed ? "hit.kill" : e.Region == HitRegion.Head ? "hit.headshot" : "hit.marker");
        // Confirmation can arrive after the player has changed tools; the accepted attack owns its contact sound.
        if (Client.Content.Item(e.Weapon) is { Melee: not null } melee)
        {
            _director.Play2D(Sounds.Item(melee.Key, "hitFlesh") ?? "melee.hit.flesh", -4);
        }
    }

    private void OnDryFire(ItemDef weapon) => Sounds.Flat(Sounds.Item(weapon.Key, "dryFire"));

    private void OnDamage(DamageEvent e) => _director.Play2D("survival.hurt");

    private void OnDied(LifeView life) => _director.Play2D("survival.death");

    private void OnCrafted(ItemDef item, int count) => _director.Play2D("ui.craft.done");

    private void OnRefused(IntentAnswer answer) => _director.Play2D("ui.error");

    private void OnChat(ChatLine line) => _director.Play2D("ui.notify");

    // A box, campfire or furnace opens and shuts where it stands.
    private void OnOpened(LootKind kind, EntityId id)
    {
        RemoteThing? thing = Client.Survival().Things.FirstOrDefault(t => t.Id == id);
        if (thing?.Deployable(Client.Content) is not { } def)
        {
            return;
        }
        _looted = thing.Position + Client.Frame.UpAt(thing.Position) * 0.4f;
        _lootedKey = def.Key;
        Sounds.At(Sounds.Item(def.Key, "open"), GroundView.ToGodot(_looted.Value));
    }

    private void OnClosed()
    {
        if (_looted is { } at && _lootedKey != null)
        {
            Sounds.At(Sounds.Item(_lootedKey, "close"), GroundView.ToGodot(at));
        }
        _looted = null;
        _lootedKey = null;
    }
}

/// <summary>Plays the sound catalogue's events for the ground views (GROUND.md §4.9), through the one director.</summary>
public static class Sounds
{
    /// <summary>An event at a point in render space (nothing when there is no director or no event).</summary>
    public static void At(string? key, Vector3 position, float db = 0, float pitch = 1)
    {
        if (key != null)
        {
            AudioDirector.Instance?.Play(key, position, db, pitch);
        }
    }

    /// <summary>An event flat (the player's own body, the interface).</summary>
    public static void Flat(string? key, float db = 0)
    {
        if (key != null)
        {
            AudioDirector.Instance?.Play2D(key, db);
        }
    }

    /// <summary>An item's (or a deployable's) sound for a role: "swing", "hitWood", "place", "loop", "shot"…</summary>
    public static string? Item(string itemKey, string role) => SoundLibrary.Data.ItemEvent(itemKey, role);

    /// <summary>Loops an event on a node until it is stopped or the node goes.</summary>
    public static SoundHandle Loop(string? key, Node3D node) =>
        key != null && AudioDirector.Instance is { } director ? director.Loop(key, node) : SoundHandle.None;

    public static void Stop(SoundHandle handle, float fade = 0.3f) => AudioDirector.Instance?.Stop(handle, fade);

    /// <summary>Eating, drinking or bandaging: the used item's own sound, flat.</summary>
    public static void Used(GameContent content, ushort item)
    {
        if (content.Item(item) is { } def)
        {
            Flat(Item(def.Key, "use"));
        }
    }
}
