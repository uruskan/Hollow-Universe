using System;
using System.Collections.Generic;
using Godot;
using Hollow.Client;
using Hollow.Simulation.Content;
using Hollow.Simulation.Ships;
using HollowUniverse.Audio;
using HollowUniverse.Fx;
using HollowUniverse.Ground;
using HollowUniverse.Items;
using Num = System.Numerics;

namespace HollowUniverse.Ships;

/// <summary>
/// The crewed batteries drawn and heard (IW3, IRON_WAKE.md §6, §9.5, §10, §11): every station of a ship posed each frame from
/// its record (the player's own as predicted) and batteries.json: the mount's yaw and pitch, each barrel's recoil curve
/// from its shot, every loading mechanism's piece by its phase and progress (the table's node patterns, never fixed
/// names), the rockets in each rack and cassette slot as the magazine holds them (the items' catalogue models); each
/// phase's sound and effects as it begins (at the first piece it moves), the breech's wisps while the table says; the
/// traverse's whine while a mount turns, its limit's clunk; and each shot's muzzle flash, dust, report, mechanism and the
/// motor catching a few metres out.
/// </summary>
public sealed class StationsView
{
    private sealed class LoaderSeen
    {
        public bool Active;
        public int Phase = -1;
        public bool Reverse;
        public FxHandle Loop;
        public int LoopUntil = -1;
    }

    private sealed class SlotModel
    {
        public Node3D? Socket;
        public Node3D? Model;
        public ushort Kind;
    }

    private sealed class StationSeen
    {
        public LoaderSeen[] Loaders = Array.Empty<LoaderSeen>();
        public float Yaw = float.NaN;
        public SoundHandle Traverse;
        public bool TraverseFast;
        public bool Limit;
    }

    private sealed class ShipSeen
    {
        public StationSeen[] Stations = Array.Empty<StationSeen>();
        public readonly Dictionary<string, SlotModel> Slots = new();
        public readonly Dictionary<int, (Num.Vector3 Offset, Num.Quaternion Turn)> Poses = new();
    }

    private readonly Dictionary<uint, ShipSeen> _ships = new();
    private readonly List<(double At, Vector3 Where, Vector3 Way)> _ignitions = new();

    public GameClient Client { get; init; } = null!;

    /// <summary>
    /// A ship's stations this frame: its parts' states get each mount (the player's own as predicted) and each barrel's
    /// recoil; returns each loader piece's motion by its part (for <see cref="ShipView.ShowParts"/>), and shows the rounds
    /// in the slots, the phases' sounds and effects and the traverse.
    /// </summary>
    public IReadOnlyDictionary<int, (Num.Vector3 Offset, Num.Quaternion Turn)>? Pose(RemoteShip ship, ShipDef def, ShipView view, float[] parts, double delta)
    {
        if (def.Interior is not { } interior || def.Stations.Count == 0)
        {
            return null;
        }
        if (!_ships.TryGetValue(ship.NetId, out ShipSeen? seen))
        {
            seen = new ShipSeen { Stations = new StationSeen[def.Stations.Count] };
            for (int i = 0; i < seen.Stations.Length; i++)
            {
                seen.Stations[i] = new StationSeen { Loaders = new LoaderSeen[def.Stations[i].LoaderCount] };
                for (int l = 0; l < seen.Stations[i].Loaders.Length; l++)
                {
                    seen.Stations[i].Loaders[l] = new LoaderSeen();
                }
            }
            _ships[ship.NetId] = seen;
        }
        seen.Poses.Clear();
        StationModule stations = Client.Stations();
        for (int i = 0; i < def.Stations.Count; i++)
        {
            ShipStationDef s = def.Stations[i];
            if (stations.Display(ship, i) is not { } st)
            {
                continue;
            }
            double tick = stations.DrawTick(ship.NetId, i);
            BatteryDef battery = s.Battery;
            // The mount: the player's own as predicted (the record's copy holds it), others' as the ship's details say.
            stations.OverlayMount(ship.NetId, parts);
            // Each barrel's recoil from its shot (the asset's curve; presentation).
            foreach (StationBarrel b in s.Barrels)
            {
                if (b.RecoilPart > 0 && b.RecoilPart < parts.Length)
                {
                    double seconds = (tick - st.ShotTicks[b.Index]) / Hollow.Simulation.Movement.CharacterMotor.TickRate;
                    parts[b.RecoilPart] = battery.Recoil.Travel * battery.Recoil.At(seconds);
                }
            }
            for (int l = 0; l < s.LoaderCount; l++)
            {
                LoaderState record = st.Loaders[l];
                float elapsed = record.Elapsed(tick);
                foreach (Hollow.Simulation.Ships.LoaderPoses.NodePose p in Hollow.Simulation.Ships.LoaderPoses.Pose(battery, s, l, record.Active, elapsed))
                {
                    int part = interior.PartIndex(p.Node);
                    if (part > 0)
                    {
                        seen.Poses[part] = (p.Offset, p.Turn);
                    }
                }
                ShowHolders(seen, view, battery, st, l, record.Active, elapsed);
                HearPhases(seen.Stations[i].Loaders[l], view, battery, s, l, record, elapsed);
            }
            HearTraverse(seen.Stations[i], view, interior, s, st, delta);
        }
        PlayIgnitions();
        return seen.Poses;
    }

    // Each holder shown or hidden, each slot's rocket the round it holds (the item's catalogue model, at its slot with no offset).
    private void ShowHolders(ShipSeen seen, ShipView view, BatteryDef battery, StationState st, int loader, bool active, float elapsed)
    {
        int magazine = st.Count(st.ChosenItem.Id);
        foreach (Hollow.Simulation.Ships.LoaderPoses.HolderView h in Hollow.Simulation.Ships.LoaderPoses.Holders(battery, st, loader, active, elapsed, magazine))
        {
            if (view.NodeNamed(h.Node) is { } node)
            {
                node.Visible = h.Shown;
            }
            for (int i = 0; i < h.Slots.Length; i++)
            {
                if (!seen.Slots.TryGetValue(h.Slots[i], out SlotModel? slot))
                {
                    slot = new SlotModel { Socket = view.Socket(h.Slots[i]) };
                    seen.Slots[h.Slots[i]] = slot;
                }
                if (slot.Socket == null)
                {
                    continue;
                }
                ushort kind = h.Shown ? h.Items[i] : (ushort)0;
                if (kind != slot.Kind)
                {
                    slot.Model?.QueueFree();
                    slot.Model = null;
                    slot.Kind = kind;
                    if (kind != 0 && Client.Content.Item(kind) is { } item && ModelCatalog.Instantiate(item.Key) is { } model)
                    {
                        slot.Socket.AddChild(model);
                        slot.Model = model;
                    }
                }
            }
        }
    }

    // A loader's phases heard and seen as they begin (forward): the phase's sound at the first piece it moves, the one before
    // ended with its end sound, its effects there, and its loop effect from it until the phase the table names.
    private void HearPhases(LoaderSeen seen, ShipView view, BatteryDef battery, ShipStationDef s, int loader, in LoaderState record, float elapsed)
    {
        BatteryLoader l = battery.Loader;
        int phase = record.Active ? l.PhaseAt(elapsed) : -1;
        if (!record.Active || record.Waiting && phase < l.WaitsBefore)
        {
            StopLoop(seen);
        }
        if (record.Active && seen.Active && !record.Reverse && !seen.Reverse && phase > seen.Phase)
        {
            for (int p = Math.Max(0, seen.Phase + 1); p <= phase; p++)
            {
                LoaderPhase begun = l.Phases[p];
                Vector3 at = Where(view, s, loader, begun);
                if (p > 0 && l.Phases[p - 1].EndSound is { Length: > 0 } ended)
                {
                    Sounds.At(ended, at);
                }
                if (begun.Sound.Length > 0)
                {
                    Sounds.At(begun.Sound, at);
                }
                foreach (string effect in begun.Effects)
                {
                    FxDirector.Play(effect, at, Vector3.Up);
                }
                if (begun.LoopEffect.Length > 0)
                {
                    StopLoop(seen);
                    seen.Loop = FxDirector.Start(begun.LoopEffect, at, Vector3.Up, new FxParams { Follow = PieceOf(view, s, loader, begun) });
                    seen.LoopUntil = begun.LoopUntil;
                }
                if (seen.Loop.IsValid && p == seen.LoopUntil)
                {
                    StopLoop(seen);
                }
            }
        }
        else if (record.Active && record.Reverse && phase != seen.Phase && phase >= 0)
        {
            // Drawn back: each phase's sound again as the mechanism runs it backwards.
            LoaderPhase running = l.Phases[phase];
            if (running.Sound.Length > 0)
            {
                Sounds.At(running.Sound, Where(view, s, loader, running));
            }
        }
        if (!record.Active && seen.Active && l.Phases.Count > 0 && l.Phases[^1].EndSound is { Length: > 0 } last && seen.Phase == l.Phases.Count - 1)
        {
            Sounds.At(last, Where(view, s, loader, l.Phases[^1]));
        }
        seen.Active = record.Active;
        seen.Phase = phase;
        seen.Reverse = record.Reverse;
    }

    private static void StopLoop(LoaderSeen seen)
    {
        if (seen.Loop.IsValid)
        {
            FxDirector.Stop(seen.Loop);
            seen.Loop = default;
        }
    }

    // The piece a phase moves first (its motions' first node), where its sound and effects play.
    private static Node3D? PieceOf(ShipView view, ShipStationDef s, int loader, LoaderPhase phase)
    {
        int barrel = s.Battery.Loader.Per == LoaderPer.Barrel ? loader : 0;
        foreach (LoaderMotion m in phase.Motions)
        {
            if (view.NodeNamed(Hollow.Simulation.Ships.LoaderPoses.Name(m.Node, s.Id, barrel)) is { } node)
            {
                return node;
            }
        }
        return null;
    }

    private static Vector3 Where(ShipView view, ShipStationDef s, int loader, LoaderPhase phase) =>
        PieceOf(view, s, loader, phase)?.GlobalPosition ?? view.GlobalPosition;

    // The mount's whine while it turns (slow, or fast past half its rate), and a clunk as it meets its limit.
    private static void HearTraverse(StationSeen seen, ShipView view, ShipInterior interior, ShipStationDef s, StationState st, double delta)
    {
        float rate = float.IsNaN(seen.Yaw) || delta <= 0 ? 0 : MathF.Abs(st.Yaw - seen.Yaw) / (float)delta;
        seen.Yaw = st.Yaw;
        bool turning = rate > 0.02f;
        bool fast = rate > s.Battery.Mount.YawRate * 0.5f;
        if (turning && (!seen.Traverse.IsValid || fast != seen.TraverseFast))
        {
            Sounds.Stop(seen.Traverse, 0.15f);
            Node3D at = view.NodeNamed(interior.Parts[s.YawPart].Name) ?? view;
            seen.Traverse = Sounds.Loop(fast ? "turret.traverse.fast" : "turret.traverse.slow", at);
            seen.TraverseFast = fast;
        }
        else if (!turning && seen.Traverse.IsValid)
        {
            Sounds.Stop(seen.Traverse, 0.2f);
            seen.Traverse = SoundHandle.None;
        }
        if (st.Limit && !seen.Limit)
        {
            Sounds.At("turret.limit", (view.NodeNamed(interior.Parts[s.YawPart].Name) ?? view).GlobalPosition);
        }
        seen.Limit = st.Limit;
    }

    /// <summary>
    /// A shot (IW3, §6.3, §11): the battery's muzzle effect (fx.json <c>muzzle.&lt;battery&gt;</c>, with its dust where it has
    /// one) along the barrel, its report and mechanism (sounds.json <c>&lt;battery&gt;.fire.*</c>), and the motor catching
    /// where the profile lights it.
    /// </summary>
    public void Shot(StationShotView shot, ShipDef def)
    {
        if (shot.Station >= def.Stations.Count)
        {
            return;
        }
        BatteryDef battery = def.Stations[shot.Station].Battery;
        Vector3 at = GroundView.ToGodot(shot.Origin), way = GroundView.ToGodot(shot.Direction);
        var p = new FxParams { Direction = way };
        FxDirector.Play("muzzle." + battery.Key, at, way, p);
        if (FxCatalog.Find("muzzle." + battery.Key + ".dust") != null)
        {
            FxDirector.Play("muzzle." + battery.Key + ".dust", at, way, p);
        }
        Sounds.At(battery.Key + ".fire.report", at);
        Sounds.At(battery.Key + ".fire.mechanism", at);
        // Through the structure (IRON_WAKE.md §10): the gunner's own, and anyone aboard at their space's structure-borne level.
        if (shot.Own)
        {
            Sounds.Flat(battery.Key + ".fire.interior", -4);
        }
        else if (Client.RenderFrame == shot.Ship && HollowUniverse.Audio.AudioBuses.Space is { StructureDb: > -60f } space)
        {
            Sounds.Flat(battery.Key + ".fire.interior", space.StructureDb);
        }
        if (battery.Launch.Of(shot.Kind) is { } profile && profile.Muzzle > 0)
        {
            _ignitions.Add((Time.GetTicksMsec() / 1000.0 + profile.Ignite / profile.Muzzle, at + way * profile.Ignite, way));
        }
    }

    private void PlayIgnitions()
    {
        double now = Time.GetTicksMsec() / 1000.0;
        for (int i = _ignitions.Count - 1; i >= 0; i--)
        {
            if (_ignitions[i].At <= now)
            {
                FxDirector.Play("ignite.rocket", _ignitions[i].Where, _ignitions[i].Way, new FxParams { Direction = _ignitions[i].Way });
                _ignitions.RemoveAt(i);
            }
        }
    }

    /// <summary>A ship no longer drawn: its loops stopped, its slots' rockets gone with its model.</summary>
    public void Forget(uint ship)
    {
        if (!_ships.Remove(ship, out ShipSeen? seen))
        {
            return;
        }
        foreach (StationSeen st in seen.Stations)
        {
            Sounds.Stop(st.Traverse, 0.1f);
            foreach (LoaderSeen l in st.Loaders)
            {
                StopLoop(l);
            }
        }
    }
}
