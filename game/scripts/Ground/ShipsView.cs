using System;
using System.Collections.Generic;
using Godot;
using Hollow.Client;
using Hollow.Net.Protocol;
using Hollow.Simulation.Ships;
using HollowUniverse.Audio;
using HollowUniverse.Fx;
using HollowUniverse.Ships;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

/// <summary>
/// Ships in the ground game (G6, GROUND.md §6.7): every ship the client knows drawn by a <see cref="ShipView"/>, others'
/// interpolated 100 ms behind (<see cref="RemoteShip"/>), the one the player flies as predicted
/// (<see cref="GameClient.OwnShipRenderPosition"/>); their engines heard at the engine socket (a hum that runs with
/// them and the thrusters' burn, both turned up with the thrust), a boost's roar, the hatch, a touchdown on the skids,
/// a crash; and a wreck's blast, with the hull burning until it goes.
/// </summary>
public partial class ShipsView : Node3D
{
    private sealed class Drawn
    {
        public ShipView View = null!;
        public Node3D? Engine;
        public SoundHandle Hum, Burn;
        public ShipNetFlags Flags;
        public float Health = 1;
        public Num.Vector3 Velocity;
        public bool Known;
        public FxHandle Fire;
    }

    private readonly Dictionary<uint, Drawn> _drawn = new();
    private readonly HashSet<uint> _seen = new();
    private readonly List<uint> _gone = new();

    public GameClient Client { get; set; } = null!;

    /// <summary>The player's own ship is seen from its cockpit: its plumes nearest the eye hide.</summary>
    public bool Cockpit { get; set; }

    /// <summary>The ship view of a ship by its net ID, if drawn (screenshots frame it).</summary>
    public ShipView? ViewOf(uint netId) => _drawn.TryGetValue(netId, out Drawn? d) ? d.View : null;

    /// <summary>Every frame, after the client's update: each ship where it is drawn, and what it sounds like.</summary>
    public void Refresh(double delta, float exposure)
    {
        ShipView.Exposure = exposure;
        _seen.Clear();
        RemoteShip? own = Client.OwnShipRemote;
        foreach (RemoteShip ship in Client.Ships)
        {
            if (ship.Def is not { } def)
            {
                continue;
            }
            _seen.Add(ship.NetId);
            if (!_drawn.TryGetValue(ship.NetId, out Drawn? d))
            {
                d = new Drawn { View = new ShipView(def) };
                AddChild(d.View);
                d.Engine = GunEffects.Socket(d.View.Model, "engine_audio");
                _drawn[ship.NetId] = d;
            }
            bool mine = own != null && own.NetId == ship.NetId;
            ShipLook look;
            if (mine)
            {
                ShipState s = Client.OwnShip;
                Num.Quaternion q = Client.OwnShipRenderRotation;
                Num.Vector3 origin = Client.OwnShipRenderPosition - Num.Vector3.Transform(def.CentreOfMass, q);
                look = new ShipLook(Pivot(origin, q), ship.Hatch, s.Thrust, s.Has(ShipFlags.EnginesOn), s.Has(ShipFlags.Boosting), true, false);
            }
            else
            {
                look = new ShipLook(Pivot(ship.Origin, ship.Rotation), ship.Hatch, ship.Thrust, ship.Has(ShipNetFlags.EnginesOn),
                    ship.Has(ShipNetFlags.Boosting), ship.Has(ShipNetFlags.Piloted), ship.Has(ShipNetFlags.Destroyed));
            }
            d.View.Visible = !ship.Has(ShipNetFlags.InOrbit);
            d.View.FromCockpit = mine && Cockpit;
            d.View.Show(look, delta);
            Hear(d, ship, def, look);
        }
        _gone.Clear();
        foreach (uint id in _drawn.Keys)
        {
            if (!_seen.Contains(id))
            {
                _gone.Add(id);
            }
        }
        foreach (uint id in _gone)
        {
            Drawn d = _drawn[id];
            Sounds.Stop(d.Hum, 0.3f);
            Sounds.Stop(d.Burn, 0.3f);
            if (d.Fire.IsValid)
            {
                FxDirector.Stop(d.Fire);
            }
            d.View.QueueFree();
            _drawn.Remove(id);
        }
    }

    private static Transform3D Pivot(Num.Vector3 origin, Num.Quaternion q) =>
        new(new Basis(new Quaternion(q.X, q.Y, q.Z, q.W)), GroundView.ToGodot(origin));

    // The engines' loops while they run, each turned up with the thrust; and what happened since the last frame.
    private void Hear(Drawn d, RemoteShip ship, ShipDef def, in ShipLook look)
    {
        Node3D at = d.Engine ?? d.View;
        bool running = look.Engines && !look.Destroyed && d.View.Visible;
        if (running)
        {
            if (!d.Hum.IsValid)
            {
                d.Hum = Sounds.Loop(Sounds.Item(def.Key, "engine") ?? "ship.engine", at);
                d.Burn = Sounds.Loop(Sounds.Item(def.Key, "thrust") ?? "ship.thrust", at);
            }
            Num.Vector3 t = look.Thrust;
            float power = Math.Clamp(Math.Max(Math.Abs(t.Z), Math.Max(Math.Abs(t.Y) * 0.8f, Math.Abs(t.X) * 0.6f)), 0, 1.8f);
            AudioDirector.Instance?.SetGain(d.Hum, -8 + 8 * Math.Min(power, 1), 0.15f);
            AudioDirector.Instance?.SetGain(d.Burn, power < 0.04f ? -40 : 20 * MathF.Log10(power) - 2 + (look.Boosting ? 3 : 0), 0.15f);
        }
        else if (d.Hum.IsValid)
        {
            Sounds.Stop(d.Hum, 0.6f);
            Sounds.Stop(d.Burn, 0.4f);
            d.Hum = d.Burn = SoundHandle.None;
        }
        ShipNetFlags was = d.Flags, now = ship.Flags;
        Vector3 hatch = GroundView.ToGodot(ship.PointOf(def.Hatch));
        Vector3 centre = GroundView.ToGodot(ship.Position);
        if (d.Known)
        {
            bool Rose(ShipNetFlags f) => (now & f) != 0 && (was & f) == 0;
            bool Fell(ShipNetFlags f) => (now & f) == 0 && (was & f) != 0;
            if (Rose(ShipNetFlags.Boosting))
            {
                Sounds.At(Sounds.Item(def.Key, "boost"), at.GlobalPosition);
            }
            if (Rose(ShipNetFlags.HatchOpen))
            {
                Sounds.At(Sounds.Item(def.Key, "hatchOpen"), hatch);
            }
            else if (Fell(ShipNetFlags.HatchOpen))
            {
                Sounds.At(Sounds.Item(def.Key, "hatchClose"), hatch);
            }
            if (Rose(ShipNetFlags.Landed) && d.Velocity.Length() > 0.6f)
            {
                Sounds.At(Sounds.Item(def.Key, "touchdown"), centre);
            }
            // A crash: the hull hurt as the ship stopped short (gunfire hurts it without the jolt).
            if (ship.Health < d.Health - 0.002f && !ship.Has(ShipNetFlags.Destroyed) && (ship.Velocity - d.Velocity).Length() > 3f)
            {
                Sounds.At(Sounds.Item(def.Key, "crash"), centre);
                FxDirector.Play("impact.bullet.metal", centre, GroundView.ToGodot(Client.Frame.UpAt(ship.Position)), new FxParams { Scale = 4 });
            }
            if (Rose(ShipNetFlags.Destroyed))
            {
                // The wreck: a blast, its debris, and the hull burning until it goes (two seconds).
                Vector3 up = GroundView.ToGodot(Client.Frame.UpAt(ship.Position));
                FxDirector.Play("explosion.rocket", centre, up, new FxParams { Scale = 1.6f });
                Sounds.At(Sounds.Item(def.Key, "destroyed") ?? "explosion", centre);
                Sounds.At("explosion.debris", centre, -2);
                d.Fire = FxDirector.Start("fire.burning", centre, up, new FxParams { Follow = d.View, Scale = 2.2f, Duration = 3f });
            }
        }
        d.Known = true;
        d.Flags = now;
        d.Health = ship.Health;
        d.Velocity = ship.Velocity;
    }
}
