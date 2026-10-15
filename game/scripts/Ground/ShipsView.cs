using System;
using System.Collections.Generic;
using Godot;
using Hollow.Client;
using Hollow.Client.Audio;
using Hollow.Net.Protocol;
using Hollow.Simulation;
using Hollow.Simulation.Ships;
using HollowUniverse.Audio;
using HollowUniverse.Fx;
using HollowUniverse.Ships;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

/// <summary>
/// Ships in the ground game (G6, GROUND.md §6.7): every ship the client knows drawn by a <see cref="ShipView"/>, others'
/// interpolated 100 ms behind (<see cref="RemoteShip"/>), the one the player flies as predicted
/// (<see cref="GameClient.OwnShipRenderPosition"/>); what each sounds like by its kind's roles in sounds.json
/// (<see cref="ShipSounds"/>, IRON_WAKE.md §10); the listener's space by the room the player stands in aboard (the world
/// buses' reverb, §10.1); and a wreck's blast, with the hull burning until it goes.
/// </summary>
public partial class ShipsView : Node3D
{
    private sealed class Drawn
    {
        public ShipView View = null!;
        public ShipSounds? Sounds;
        public ShipNetFlags Flags;
        public bool Known;
        public FxHandle Fire;

        // A ship people walk in: its parts' states as last shown.
        public float[]? Parts;
        public float[]? Shown;

        // Its effects (IRON_WAKE.md §11): the wash under each lift nozzle, smoke from each disabled battery and the hull.
        public FxHandle[]? Wash;
        public FxHandle[]? Smoke;
        public FxHandle HullSmoke;
        public Node3D? HullSmokeAt;
    }

    private readonly Dictionary<uint, Drawn> _drawn = new();
    private readonly HashSet<uint> _seen = new();
    private readonly List<uint> _gone = new();
    private StationsView? _stations;
    private SoundSpace _space = SoundSpace.Open;
    private double _delta;

    public GameClient Client { get; set; } = null!;

    // The crewed batteries (IW3): posed, heard and fired on every ship drawn.
    private StationsView Stations
    {
        get
        {
            if (_stations == null)
            {
                _stations = new StationsView { Client = Client };
                Client.Stations().Shot += shot =>
                {
                    if (Client.Ships().ShipByNetId(shot.Ship)?.Def is { } def)
                    {
                        _stations.Shot(shot, def);
                    }
                };
            }
            return _stations;
        }
    }

    /// <summary>The player's own ship is seen from its cockpit: its plumes nearest the eye hide.</summary>
    public bool Cockpit { get; set; }

    /// <summary>The ship view of a ship by its net ID, if drawn (screenshots frame it).</summary>
    public ShipView? ViewOf(uint netId) => _drawn.TryGetValue(netId, out Drawn? d) ? d.View : null;

    /// <summary>Every frame, after the client's update: each ship where it is drawn, and what it sounds like.</summary>
    public void Refresh(double delta, float exposure)
    {
        ShipView.Exposure = exposure;
        _delta = delta;
        _space = ListenerSpace();
        AudioBuses.SetSpace(_space, delta);
        _seen.Clear();
        RemoteShip? own = Client.Ships().OwnShipRemote;
        foreach (RemoteShip ship in Client.Ships().All)
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
                _drawn[ship.NetId] = d;
            }
            bool mine = own != null && own.NetId == ship.NetId;
            ShipLook look;
            if (mine)
            {
                ShipState s = Client.Ships().OwnShip;
                Num.Quaternion q = Client.Ships().OwnShipRenderRotation;
                Num.Vector3 origin = Client.Ships().OwnShipRenderPosition - Num.Vector3.Transform(def.CentreOfMass, q);
                look = new ShipLook(Pivot(origin, q), ship.DoorAt(0, Client.ServerTickNow), s.Thrust, s.Has(ShipFlags.EnginesOn), s.Has(ShipFlags.Boosting), true, false);
            }
            else
            {
                look = new ShipLook(Pivot(ship.Origin, ship.Rotation), ship.DoorAt(0, Client.RenderTick), ship.Thrust, ship.Has(ShipNetFlags.EnginesOn),
                    ship.Has(ShipNetFlags.Boosting), ship.Has(ShipNetFlags.Piloted), ship.Has(ShipNetFlags.Destroyed));
            }
            d.View.Visible = !ship.Has(ShipNetFlags.InOrbit);
            d.View.FromCockpit = mine && Cockpit;
            d.View.Show(look, delta);
            if (def.Interior is { } interior)
            {
                // Its ramp, airlock and turrets (IRON_WAKE.md §4.7, §5.3): as its space is posed where the player has one (the
                // doors drawn where they are met), else its details at the time drawn; shown when they change.
                d.Parts ??= new float[interior.Parts.Count];
                d.Shown ??= new float[interior.Parts.Count];
                if (Client.Ships().FrameOf(ship.NetId) is { } frame)
                {
                    frame.States.CopyTo(d.Parts, 0);
                }
                else
                {
                    ship.PartsInto(interior, d.Parts, (long)Client.RenderTick);
                }
                // Its batteries (IW3): the mounts (the player's own as predicted), the barrels' recoil and the loaders' pieces.
                IReadOnlyDictionary<int, (Num.Vector3, Num.Quaternion)>? loaders = Stations.Pose(ship, def, d.View, d.Parts, delta);
                if (loaders is { Count: > 0 } || !d.Parts.AsSpan().SequenceEqual(d.Shown))
                {
                    d.Parts.CopyTo(d.Shown, 0);
                    d.View.ShowParts(d.Shown, loaders);
                }
            }
            Num.Vector3 velocity = mine ? Client.Ships().OwnShip.Velocity : ship.Velocity;
            Hear(d, ship, def, look, velocity, mine ? Client.ServerTickNow : Client.RenderTick);
            Effects(d, ship, def, look);
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
            d.Sounds?.StopAll();
            StopEffects(d);
            if (d.Fire.IsValid)
            {
                FxDirector.Stop(d.Fire);
            }
            d.View.QueueFree();
            _drawn.Remove(id);
            _stations?.Forget(id);
        }
    }

    private static Transform3D Pivot(Num.Vector3 origin, Num.Quaternion q) =>
        new(new Basis(new Quaternion(q.X, q.Y, q.Z, q.W)), GroundView.ToGodot(origin));

    // What it sounds like (ShipSounds: every role of its kind's from the replicated state) and what happened to it: a
    // wreck's blast with the hull burning until it goes.
    private void Hear(Drawn d, RemoteShip ship, ShipDef def, in ShipLook look, Num.Vector3 velocity, double tick)
    {
        d.Sounds ??= new ShipSounds(def, d.View);
        SoundSpace? aboard = Client.RenderFrame == ship.NetId ? _space : null;
        d.Sounds.Update(look, ship, velocity, tick, d.View.Visible, aboard, _delta);
        if (d.Known && (ship.Flags & ShipNetFlags.Destroyed) != 0 && (d.Flags & ShipNetFlags.Destroyed) == 0)
        {
            // The wreck: a blast, its debris, and the hull burning until it goes (two seconds).
            Vector3 centre = GroundView.ToGodot(ship.Position);
            Vector3 up = GroundView.ToGodot(Client.Frame.UpAt(ship.Position));
            FxDirector.Play("explosion.rocket", centre, up, new FxParams { Scale = 1.6f });
            d.Fire = FxDirector.Start("fire.burning", centre, up, new FxParams { Follow = d.View, Scale = 2.2f, Duration = 3f });
        }
        d.Known = true;
        d.Flags = ship.Flags;
    }

    /// <summary>The wash ends past this height over the ground (IRON_WAKE.md §11).</summary>
    private const float WashReach = 25f;

    /// <summary>Full wash at this height and under.</summary>
    private const float WashFull = 4f;

    // The ship's own effects (IRON_WAKE.md §11): dust under each lift nozzle within 25 m of the ground, by its plume and
    // nearness; smoke pouring from each disabled battery's gun house and from the hull below a quarter, until mended.
    private void Effects(Drawn d, RemoteShip ship, ShipDef def, in ShipLook look)
    {
        bool shown = d.View.Visible && !look.Destroyed;
        d.Wash ??= new FxHandle[d.View.LiftNozzles];
        GroundProbe? ground = Client.Surface is { } world ? new GroundProbe(world, Client.Frame) : null;
        for (int i = 0; i < d.Wash.Length; i++)
        {
            (Vector3 at, float blow) = d.View.LiftNozzle(i);
            Num.Vector3 nozzle = new(at.X, at.Y, at.Z);
            float height = ground?.HeightAbove(nozzle) ?? float.PositiveInfinity;
            float near = Math.Clamp((WashReach - height) / (WashReach - WashFull), 0f, 1f);
            float k = Math.Min(blow, 1f) * near;
            if (!shown || ground == null || k < 0.02f)
            {
                if (d.Wash[i].IsValid)
                {
                    FxDirector.Stop(d.Wash[i]);
                    d.Wash[i] = default;
                }
                continue;
            }
            Num.Vector3 up = Client.Frame.UpAt(nozzle);
            Num.Vector3 under = nozzle - up * Math.Max(height, 0);
            Vector3 point = GroundView.ToGodot(under), normal = GroundView.ToGodot(ground.Value.Normal(under).Normal);
            if (!d.Wash[i].IsValid)
            {
                FxSurface surface = GroundAudio.WaterDepth(Client, under) > 0.05 ? FxSurface.Water : FxSurfaces.Of(GroundAudio.MaterialAt(Client, under));
                d.Wash[i] = FxDirector.Start("wash.lift", point, normal, new FxParams { Tint = FxSurfaces.Dust(surface) });
            }
            FxDirector.Move(d.Wash[i], point);
            FxDirector.SetIntensity(d.Wash[i], k);
        }

        // Smoke from a battery whose health ran out (its gun house: the yaw part), and from the hull under a quarter.
        if (def.Interior is { } interior && def.Stations.Count > 0)
        {
            d.Smoke ??= new FxHandle[def.Stations.Count];
            for (int i = 0; i < def.Stations.Count; i++)
            {
                ShipStationDef s = def.Stations[i];
                bool disabled = shown && s.Part < ship.PartHealth.Length && def.Parts[s.Part].Health != null && ship.PartHealth[s.Part] <= 0;
                if (disabled && !d.Smoke[i].IsValid)
                {
                    Node3D? house = s.YawPart > 0 ? d.View.NodeNamed(interior.Parts[s.YawPart].Name) : null;
                    Vector3 at = GroundView.ToGodot(ship.PointOf(def.PartPoint(s.Part)));
                    d.Smoke[i] = FxDirector.Start("smoke.damaged", at, GroundView.ToGodot(Client.Frame.UpAt(ship.Position)), new FxParams { Follow = house ?? d.View });
                }
                else if (!disabled && d.Smoke[i].IsValid)
                {
                    FxDirector.Stop(d.Smoke[i]);
                    d.Smoke[i] = default;
                }
            }
        }
        bool hurt = shown && ship.Health < 0.25f;
        if (hurt && !d.HullSmoke.IsValid)
        {
            // Over the hull's top, at its middle (the ship's bounds, ships.json and its data file).
            if (d.HullSmokeAt == null)
            {
                Num.Vector3 top = def.BoundsCentre + new Num.Vector3(0, def.BoundsHalf.Y * 0.6f, 0);
                d.HullSmokeAt = new Node3D { Name = "HullSmoke", Position = GroundView.ToGodot(top) };
                d.View.AddChild(d.HullSmokeAt);
            }
            d.HullSmoke = FxDirector.Start("smoke.damaged", d.HullSmokeAt.GlobalPosition, Vector3.Up, new FxParams { Follow = d.HullSmokeAt });
        }
        else if (!hurt && d.HullSmoke.IsValid)
        {
            FxDirector.Stop(d.HullSmoke);
            d.HullSmoke = default;
        }
    }

    private static void StopEffects(Drawn d)
    {
        foreach (FxHandle h in d.Wash ?? Array.Empty<FxHandle>())
        {
            FxDirector.Stop(h);
        }
        foreach (FxHandle h in d.Smoke ?? Array.Empty<FxHandle>())
        {
            FxDirector.Stop(h);
        }
        FxDirector.Stop(d.HullSmoke);
    }

    // The listener's space (IRON_WAKE.md §10.1): aboard a ship, the space of the room the player stands in (sounds.json's
    // spaces list their rooms); elsewhere, and on a ship's open decks, the open air.
    private SoundSpace ListenerSpace()
    {
        SoundCatalogData data = SoundLibrary.Data;
        uint frame = Client.RenderFrame;
        if (frame != 0 && Client.Ships().ShipByNetId(frame)?.Def?.Interior is { } interior)
        {
            return data.SpaceOfRoom(interior.RoomAt(Client.RenderLocal)?.Id);
        }
        return data.Outside;
    }
}
