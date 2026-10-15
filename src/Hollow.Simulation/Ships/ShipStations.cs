using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation.Content;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;

namespace Hollow.Simulation.Ships;

/// <summary>One barrel of a station (the data file's <c>stations[].barrels</c> and <c>receiver.barrels</c>).</summary>
public sealed class StationBarrel
{
    public int Index { get; init; }

    /// <summary>The data file's recoil part (presentation: the rules never move it).</summary>
    public int RecoilPart { get; init; } = -1;

    /// <summary>Its muzzle: where a shot leaves, carried by the barrel's parts.</summary>
    public ShipSocket Muzzle { get; init; }

    /// <summary>Its breech end in the ship's frame at rest (where the hull-blocked line starts).</summary>
    public Vector3 Breech { get; init; }

    /// <summary>+1 on the mount's port side, −1 starboard, 0 the middle (its loader's outboard, IRON_WAKE.md §9.5).</summary>
    public float Side { get; init; }

    public float Length { get; init; }
}

/// <summary>
/// A station of a ship (IRON_WAKE.md §6.1): its id in the data file, its battery's kind (batteries.json), the seat that works
/// it and the target part its health and magazine are at, its mount's yaw and pitch parts and how far the yaw turns, its
/// barrels and its gunner's camera. Its index is its number on the wire and in saves.
/// </summary>
public sealed class ShipStationDef
{
    public int Index { get; init; }

    public string Id { get; init; } = "";

    public BatteryDef Battery { get; init; } = null!;

    public int Seat { get; init; } = -1;

    /// <summary>The target part (ships.json <c>parts</c>) whose health is the station's and where its magazine is opened.</summary>
    public int Part { get; init; }

    public int YawPart { get; init; } = -1;

    public int PitchPart { get; init; } = -1;

    /// <summary>+1 port, −1 starboard, 0 on the centre line (a mount loader's outboard).</summary>
    public float Side { get; init; }

    /// <summary>The yaw's limits (radians from the bow, + to port): <see cref="YawMax"/> may pass π (an arc across the stern).</summary>
    public float YawMin { get; init; }

    public float YawMax { get; init; }

    /// <summary>The yaw turns all the way round.</summary>
    public bool FullCircle => YawMax - YawMin >= 2 * MathF.PI - 1e-4f;

    public IReadOnlyList<StationBarrel> Barrels { get; init; } = Array.Empty<StationBarrel>();

    public ShipSocket Camera { get; init; }

    /// <summary>Its loaders: one per barrel, or one for the mount.</summary>
    public int LoaderCount => Battery.Loader.Per == LoaderPer.Barrel ? Barrels.Count : 1;

    /// <summary>The barrels a loader serves (a barrel's own, or every barrel of the mount).</summary>
    public (int First, int Count) LoaderBarrels(int loader) => Battery.Loader.Per == LoaderPer.Barrel ? (loader, 1) : (0, Barrels.Count);

    /// <summary>The loader of a barrel.</summary>
    public int LoaderOf(int barrel) => Battery.Loader.Per == LoaderPer.Barrel ? barrel : 0;

    /// <summary>A loader's outboard side (+1 port, −1 starboard): its barrel's side, or the station's for a mount loader.</summary>
    public float LoaderSide(int loader)
    {
        float side = Battery.Loader.Per == LoaderPer.Barrel && loader < Barrels.Count ? Barrels[loader].Side : Side;
        return side < 0 ? -1 : 1;
    }

    public override string ToString() => Id;
}

/// <summary>A gunner's controls for a tick (IRON_WAKE.md §6.8): the world direction asked for, the trigger, the zoom, the ammo choice and the cycle tap.</summary>
public struct StationInput
{
    /// <summary>The asked direction in the zone's frame (unit), or zero: none yet.</summary>
    public Vector3 View;

    public bool Fire;

    public bool Zoom;

    /// <summary>The kind asked for, its index in the battery's ammo + 1 (0: no change).</summary>
    public byte Choice;

    /// <summary>A counter a tap of R raises (two bits): a change draws chambered rounds of another kind back (§6.5).</summary>
    public byte Cycle;
}

/// <summary>
/// A loader's cycle as the rules have it (IRON_WAKE.md §6.6): at work or idle, waiting with nothing to load, running in
/// reverse (drawing a round back), seconds into the cycle at a tick and the round(s) it carries once taken. Its time at
/// any tick is worked out alike on the server and every client, from the record alone (sent once a change, as a door's).
/// </summary>
public struct LoaderState
{
    public bool Active;

    public bool Waiting;

    public bool Reverse;

    /// <summary>The tick <see cref="At"/> was true at.</summary>
    public long Since;

    /// <summary>Seconds into the cycle at <see cref="Since"/>.</summary>
    public float At;

    /// <summary>The round it carries once taken from the magazine (0: none yet), and how many (a cassette's).</summary>
    public ushort Kind;

    public byte Count;

    /// <summary>Seconds into the cycle at a tick (held while waiting; going down in reverse).</summary>
    public readonly float Elapsed(long tick) => Elapsed((double)tick);

    public readonly float Elapsed(double tick)
    {
        if (!Active || Waiting)
        {
            return At;
        }
        double dt = Math.Max(0, tick - Since) / CharacterMotor.TickRate;
        return (float)(Reverse ? At - dt : At + dt);
    }

    public override readonly string ToString() => Active ? $"{(Reverse ? "reverse " : Waiting ? "waiting " : "")}{At:0.00}s@{Since} {Kind}x{Count}" : "idle";
}

/// <summary>
/// A station's state on a ship (IRON_WAKE.md §6): the mount (where it points, how fast it turns, what it was last asked),
/// the chosen kind, the magazine, each barrel's chamber and last shot, the loaders, the shot count (the shot's number on
/// the wire) and the gunner's controls.
/// </summary>
public sealed class StationState
{
    public StationState(ShipStationDef def)
    {
        Def = def;
        BatteryDef b = def.Battery;
        var takes = new HashSet<ushort>();
        foreach (ItemDef item in b.Ammo)
        {
            takes.Add(item.Id);
        }
        Magazine = new Container(ContainerKind.Magazine, Math.Max(1, b.MagazineSlots)) { Takes = takes };
        Chambers = new ushort[def.Barrels.Count];
        ShotTicks = new long[def.Barrels.Count];
        Array.Fill(ShotTicks, long.MinValue / 4);
        Loaders = new LoaderState[def.LoaderCount];
    }

    public ShipStationDef Def { get; }

    /// <summary>The mount: yaw and pitch (radians, the parts' states), their rates, and the targets it slewed to last.</summary>
    public float Yaw, Pitch, YawRate, PitchRate, YawTarget, PitchTarget;

    public bool HasTarget;

    /// <summary>The asked direction was beyond the mount's arcs last tick.</summary>
    public bool Limit;

    /// <summary>The chosen kind: its index in the battery's ammo.</summary>
    public byte Chosen;

    public Container Magazine { get; }

    /// <summary>Each barrel's round, or 0 (empty).</summary>
    public ushort[] Chambers { get; }

    /// <summary>Each barrel's last shot's tick (its recoil is drawn from it).</summary>
    public long[] ShotTicks { get; }

    public LoaderState[] Loaders { get; }

    /// <summary>Shots fired: the next shot's number is one more.</summary>
    public uint Shots;

    /// <summary>The siege battery's turn: the barrel tried first at the next press.</summary>
    public int NextBarrel;

    /// <summary>A ripple's next barrel may fire at this tick.</summary>
    public long NextRippleTick;

    public StationInput Input;

    /// <summary>The trigger as it was last tick (a press is its edge), and the cycle counter last seen.</summary>
    public bool FireWas;

    public byte CycleSeen;

    public bool CycleSeenSet;

    /// <summary>The last refusal and its tick (the sight shows it).</summary>
    public OutcomeCode Refused;

    public int RefusedDetail;

    public long RefusedTick = long.MinValue / 4;

    /// <summary>The chosen kind's item.</summary>
    public ItemDef ChosenItem => Def.Battery.Ammo[Math.Min(Chosen, Def.Battery.Ammo.Count - 1)];

    /// <summary>How many of a kind the magazine holds.</summary>
    public int Count(ushort item)
    {
        int n = 0;
        foreach (ItemStack s in Magazine.Slots)
        {
            if (s.Item == item)
            {
                n += s.Count;
            }
        }
        return n;
    }

    /// <summary>Whether a barrel is loaded (its round chambered; its loader done).</summary>
    public bool Loaded(int barrel) => Chambers[barrel] != 0 && !Loaders[Def.LoaderOf(barrel)].Active;
}

/// <summary>
/// The station rules both the server and the gunner's client run (IRON_WAKE.md §6.2–§6.7): the mount's aim held in the world
/// (the asked direction through the ship's pose, clamped to the arcs, slewed at the motor's rates with its acceleration and
/// braking, following a turning ship), where the barrels point, where a shot leaves, and why a press is refused.
/// </summary>
public static class StationRules
{
    private const float Dt = CharacterMotor.DeltaTime;

    /// <summary>The yaw and pitch that point the mount along a direction of the ship's frame (its parts' axes' conventions).</summary>
    public static (float Yaw, float Pitch) Angles(ShipInterior interior, ShipStationDef s, Vector3 local)
    {
        float ys = interior.Parts[s.YawPart].Axis.Y < 0 ? -1 : 1;
        float ps = interior.Parts[s.PitchPart].Axis.X > 0 ? -1 : 1;
        float horizontal = MathF.Sqrt(local.X * local.X + local.Z * local.Z);
        float yaw = ys * (float)DMath.Atan2(local.X, local.Z);
        float pitch = ps * (float)DMath.Atan2(local.Y, horizontal);
        return (yaw, pitch);
    }

    /// <summary>
    /// A yaw within the mount's limits: unwrapped from <see cref="ShipStationDef.YawMin"/>, or the nearer limit (beyond: true).
    /// A full circle wraps to −π … π.
    /// </summary>
    public static (float Yaw, bool Beyond) ClampYaw(ShipStationDef s, float yaw)
    {
        if (s.FullCircle)
        {
            return ((float)DMath.WrapAngle(yaw), false);
        }
        float twoPi = 2 * MathF.PI;
        float off = (yaw - s.YawMin) % twoPi;
        if (off < 0)
        {
            off += twoPi;
        }
        float unwrapped = s.YawMin + off;
        if (unwrapped <= s.YawMax)
        {
            return (unwrapped, false);
        }
        float pastMax = unwrapped - s.YawMax, beforeMin = s.YawMin + twoPi - unwrapped;
        return (pastMax <= beforeMin ? s.YawMax : s.YawMin, true);
    }

    /// <summary>
    /// One tick of the mount (§6.2): the asked direction in the zone's frame, turned into the ship's frame through its turn,
    /// gives the yaw and pitch it wants, clamped to its arcs; each axis slews toward them at its rate, accelerating and
    /// braking, and follows a target that moves (the ship turning under it) without lagging behind it. Beyond the arcs it
    /// stops at the limit (<see cref="StationState.Limit"/>). Returns whether it moved.
    /// </summary>
    public static bool Slew(ShipInterior interior, StationState st, Quaternion shipRotation)
    {
        ShipStationDef s = st.Def;
        BatteryMount m = s.Battery.Mount;
        Vector3 view = st.Input.View;
        if (view.LengthSquared() < 0.25f)
        {
            // Nothing asked yet: it holds where it is.
            bool moving = st.YawRate != 0 || st.PitchRate != 0;
            st.YawRate = st.PitchRate = 0;
            st.HasTarget = false;
            st.Limit = false;
            return moving;
        }
        Vector3 local = Vector3.Normalize(Vector3.Transform(view, Quaternion.Conjugate(shipRotation)));
        (float yawWanted, float pitchWanted) = Angles(interior, s, local);
        (float yaw, bool yawBeyond) = ClampYaw(s, yawWanted);
        if (s.FullCircle)
        {
            // Turning all the way round: the shorter way from where it points.
            yaw = st.Yaw + (float)DMath.WrapAngle(yaw - st.Yaw);
        }
        float pitch = Math.Clamp(pitchWanted, m.PitchMin, m.PitchMax);
        bool pitchBeyond = MathF.Abs(pitch - pitchWanted) > 1e-4f;
        st.Limit = yawBeyond || pitchBeyond;
        float yawChange = s.FullCircle ? (float)DMath.WrapAngle(yaw - st.YawTarget) : yaw - st.YawTarget;
        float yawFollow = st.HasTarget ? Math.Clamp(yawChange / Dt, -m.YawRate, m.YawRate) : 0;
        float pitchFollow = st.HasTarget ? Math.Clamp((pitch - st.PitchTarget) / Dt, -m.PitchRate, m.PitchRate) : 0;
        st.YawTarget = yaw;
        st.PitchTarget = pitch;
        st.HasTarget = true;
        float yawBefore = st.Yaw, pitchBefore = st.Pitch;
        Axis(ref st.Yaw, ref st.YawRate, yaw, yawFollow, m.YawRate, m.YawAccel);
        Axis(ref st.Pitch, ref st.PitchRate, pitch, pitchFollow, m.PitchRate, m.PitchAccel);
        if (s.FullCircle)
        {
            st.Yaw = (float)DMath.WrapAngle(st.Yaw);
        }
        else
        {
            st.Yaw = Math.Clamp(st.Yaw, s.YawMin, s.YawMax);
        }
        st.Pitch = Math.Clamp(st.Pitch, m.PitchMin, m.PitchMax);
        return st.Yaw != yawBefore || st.Pitch != pitchBefore;
    }

    // One axis: the speed it wants is the target's own (feed-forward, so a turning ship doesn't leave it behind) plus what
    // closes the gap no faster than it can brake in the distance left; its speed changes by at most its acceleration a
    // tick; reaching the target this tick, it lands on it and moves with it.
    private static void Axis(ref float position, ref float rate, float target, float follow, float most, float accel)
    {
        float gap = target - position;
        float closing = MathF.Min(most, MathF.Sqrt(2 * accel * MathF.Abs(gap)));
        float wanted = Math.Clamp(follow + MathF.Sign(gap) * closing, -most, most);
        rate += Math.Clamp(wanted - rate, -accel * Dt, accel * Dt);
        float next = position + rate * Dt;
        // Slew runs after the hull's step (and against the client's pose for this tick). The target is already current:
        // adding a further follow*Dt aimed one tick ahead, causing a permanent error proportional to hull turn speed.
        if ((position <= target && target <= next) || (next <= target && target <= position) || MathF.Abs(target - next) < 1e-6f)
        {
            next = target;
            rate = follow;
        }
        position = next;
    }

    /// <summary>Where the barrels point, in the ship's frame, at the parts' states.</summary>
    public static Vector3 BarrelDirection(ShipInterior interior, ShipStationDef s, ReadOnlySpan<float> states) =>
        Vector3.Normalize(Vector3.Transform(Vector3.UnitZ, interior.PartPose(s.PitchPart, states).Rotation));

    /// <summary>A barrel's muzzle and breech in the ship's frame at the parts' states (the recoil at rest).</summary>
    public static (Vector3 Muzzle, Vector3 Breech) BarrelLine(ShipInterior interior, ShipStationDef s, int barrel, ReadOnlySpan<float> states)
    {
        StationBarrel b = s.Barrels[barrel];
        PartPose pitch = interior.PartPose(s.PitchPart, states);
        return (pitch.Apply(b.Muzzle.Position), pitch.Apply(b.Breech));
    }

    /// <summary>The angle between where the barrels point and the asked direction (radians; π with nothing asked).</summary>
    public static float Misalignment(ShipInterior interior, StationState st, ReadOnlySpan<float> states, Quaternion shipRotation)
    {
        if (st.Input.View.LengthSquared() < 0.25f)
        {
            return MathF.PI;
        }
        Vector3 barrel = Vector3.Transform(BarrelDirection(interior, st.Def, states), shipRotation);
        float dot = Math.Clamp(Vector3.Dot(barrel, Vector3.Normalize(st.Input.View)), -1f, 1f);
        return (float)DMath.Acos(dot);
    }

    /// <summary>A station's states with its own yaw and pitch put in (the parts' copy the shot and the line are worked out on).</summary>
    public static void Into(StationState st, Span<float> states)
    {
        if (st.Def.YawPart >= 0 && st.Def.YawPart < states.Length)
        {
            states[st.Def.YawPart] = st.Yaw;
        }
        if (st.Def.PitchPart >= 0 && st.Def.PitchPart < states.Length)
        {
            states[st.Def.PitchPart] = st.Pitch;
        }
    }

    /// <summary>
    /// Why a press can't fire now, in the sight's order (§6.7), or Done with the barrel that fires: DISABLED, EMPTY, LOADING
    /// (its progress), LIMIT, TRAVERSING; the hull's line is the caller's (it needs the ship's space). <paramref name="loaded"/>
    /// says which barrels are loaded (the server's chambers, or a client's prediction of them); <paramref name="progress"/> is
    /// the busiest loader's share done; <paramref name="hasRounds"/> whether any loader carries a round or the magazine holds
    /// the chosen kind.
    /// </summary>
    public static Outcome Check(StationState st, bool disabled, ReadOnlySpan<bool> loaded, float progress, bool hasRounds, float misalignment, out int barrel)
    {
        barrel = -1;
        if (disabled)
        {
            return OutcomeCode.Disabled;
        }
        ShipStationDef s = st.Def;
        int n = s.Barrels.Count;
        // The barrel that fires: the next loaded in turn (alternate), or in the ripple's order.
        for (int k = 0; k < n; k++)
        {
            int b = s.Battery.Fire == BatteryFire.Alternate ? (st.NextBarrel + k) % n : s.Battery.Order.Count == n ? s.Battery.Order[k] : k;
            if (b < loaded.Length && loaded[b])
            {
                barrel = b;
                break;
            }
        }
        if (barrel < 0)
        {
            return hasRounds ? OutcomeCode.Loading.With((int)MathF.Round(Math.Clamp(progress, 0, 1) * 100)) : OutcomeCode.Empty;
        }
        if (st.Limit)
        {
            return OutcomeCode.Limit;
        }
        if (misalignment > s.Battery.Mount.Alignment)
        {
            return OutcomeCode.Traversing;
        }
        return Outcome.Done;
    }

    /// <summary>How far a loader is through its cycle (0 … 1), at a tick.</summary>
    public static float Progress(BatteryLoader loader, in LoaderState l, double tick) =>
        !l.Active ? 1 : loader.Seconds > 0 ? Math.Clamp(l.Elapsed(tick) / loader.Seconds, 0, 1) : 1;

    /// <summary>The hull-blocked line's reach past the muzzle (§6.7).</summary>
    public const float HullLinePast = 6f;
}

/// <summary>
/// The loading mechanism as it looks at a moment (IRON_WAKE.md §9.5, batteries.json's conventions), engine-free so the view
/// and the tests read the same: each node's offset (metres, its own axes) and turn from its rest, and each holder's
/// visibility and what its slots show, from a loader's record, its side and what the magazine holds.
/// </summary>
public static class LoaderPoses
{
    /// <summary>A node's pose from rest: a translation along its own axes and a turn.</summary>
    public readonly record struct NodePose(string Node, Vector3 Offset, Quaternion Turn);

    /// <summary>A holder now: its node, shown or not, and each slot's socket and rocket (0: none).</summary>
    public readonly record struct HolderView(string Node, bool Shown, string[] Slots, ushort[] Items);

    /// <summary>A pattern's node name for a station and a barrel.</summary>
    public static string Name(string pattern, string station, int barrel) =>
        pattern.Replace("{station}", station, StringComparison.Ordinal).Replace("{barrel}", barrel.ToString("00"), StringComparison.Ordinal);

    /// <summary>
    /// Every moved node of a loader at <paramref name="elapsed"/> seconds into its cycle (none idle): for each phase begun,
    /// its motions' values (the last one per node and axis), summed per node on top of rest. The recoil curve's motions
    /// are left to the barrels' own shots (<see cref="RecoilCurve"/>).
    /// </summary>
    public static List<NodePose> Pose(BatteryDef battery, ShipStationDef station, int loader, bool active, float elapsed)
    {
        var poses = new List<NodePose>();
        if (!active)
        {
            return poses;
        }
        BatteryLoader l = battery.Loader;
        float side = station.LoaderSide(loader);
        int barrel = l.Per == LoaderPer.Barrel ? loader : 0;
        var values = new Dictionary<(string Node, Vector3 Axis, bool Rotate), float>();
        foreach (LoaderPhase phase in l.Phases)
        {
            if (elapsed < phase.Start)
            {
                break;
            }
            float t = phase.Seconds > 0 ? Math.Clamp((elapsed - phase.Start) / phase.Seconds, 0, 1) : 1;
            foreach (LoaderMotion m in phase.Motions)
            {
                if (m.Curve)
                {
                    continue;
                }
                Vector3 axis = m.Outboard ? new Vector3(side, 0, 0) : m.Axis;
                float value = m.At(t, battery.Recoil, phase.Seconds);
                if (m.Mirror && side < 0)
                {
                    if (m.Rotate)
                    {
                        axis = new Vector3(axis.X, -axis.Y, -axis.Z);
                    }
                    else
                    {
                        axis = new Vector3(-axis.X, axis.Y, axis.Z);
                    }
                }
                values[(Name(m.Node, station.Id, barrel), axis, m.Rotate)] = value;
            }
        }
        var byNode = new Dictionary<string, (Vector3 Offset, Quaternion Turn)>();
        foreach (((string node, Vector3 axis, bool rotate), float value) in values)
        {
            (Vector3 offset, Quaternion turn) = byNode.GetValueOrDefault(node, (Vector3.Zero, Quaternion.Identity));
            if (rotate)
            {
                float radians = value * MathF.PI / 180;
                double half = radians * 0.5;
                Vector3 unit = axis.LengthSquared() > 1e-8f ? Vector3.Normalize(axis) : Vector3.UnitX;
                turn = Quaternion.Normalize(turn * new Quaternion(unit * (float)DMath.Sin(half), (float)DMath.Cos(half)));
            }
            else
            {
                offset += axis * value;
            }
            byNode[node] = (offset, turn);
        }
        foreach ((string node, (Vector3 offset, Quaternion turn)) in byNode)
        {
            poses.Add(new NodePose(node, offset, turn));
        }
        return poses;
    }

    /// <summary>
    /// What a loader's holders show at <paramref name="elapsed"/> (batteries.json conventions.holders): the loads counted from
    /// the cycle's own (the round it carries once taken, else the next in the magazine), the magazine's rounds of the chosen
    /// kind dealt in turn between the loaders that share it; <c>current</c>, the rounds in the breech now.
    /// </summary>
    public static List<HolderView> Holders(BatteryDef battery, StationState st, int loader, bool active, float elapsed, int magazineCount)
    {
        var views = new List<HolderView>();
        BatteryLoader l = battery.Loader;
        ShipStationDef s = st.Def;
        int barrel = l.Per == LoaderPer.Barrel ? loader : 0;
        int loaders = Math.Max(1, s.LoaderCount);
        LoaderState record = st.Loaders[loader];
        bool taken = active && record.Kind != 0;
        ushort chosen = st.ChosenItem.Id;
        int per = Math.Max(1, battery.RoundsPerLoad);
        for (int hi = 0; hi < l.Holders.Count; hi++)
        {
            LoaderHolder h = l.Holders[hi];
            string node = Name(h.Node, s.Id, barrel);
            var slots = new string[h.Slots.Count];
            var items = new ushort[h.Slots.Count];
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i] = Name(h.Slots[i], s.Id, barrel);
            }
            // A load's rounds are numbered across the holders that show it in the same window: the defense clip's three
            // rockets are three holders of a slot each (batteries.json conventions.holders); a holder without slots (the
            // clip) holds the load while any of it is there.
            int offset = 0;
            for (int earlier = 0; earlier < hi; earlier++)
            {
                LoaderHolder e = l.Holders[earlier];
                if (e.What == h.What && e.From == h.From && e.Until == h.Until)
                {
                    offset += e.Slots.Count;
                }
            }
            bool inWindow = !active || ((h.From < 0 || elapsed >= l.Phases[h.From].Start) && (h.Until < 0 || elapsed < l.Phases[h.Until].End));
            if (!active && h.From >= 0)
            {
                inWindow = false; // shown only from a phase of a cycle (the rammer's round)
            }
            bool holds = false;
            if (h.What == LoaderHolder.Current)
            {
                // The rounds in the breech now (fired ones gone), the cassette shown empty until it is stowed.
                (int first, int count) = s.LoaderBarrels(loader);
                for (int i = 0; i < items.Length && i < count; i++)
                {
                    items[i] = st.Chambers[first + i];
                }
                holds = true;
            }
            else
            {
                int n = h.What;
                int rounds;
                ushort kind;
                if (n == 0 && taken)
                {
                    (rounds, kind) = (record.Count, record.Kind);
                }
                else
                {
                    int fromMagazine = n - (taken ? 1 : 0);
                    int first = (fromMagazine * loaders + loader) * per;
                    (rounds, kind) = (Math.Clamp(magazineCount - first, 0, per), chosen);
                }
                for (int i = 0; i < items.Length; i++)
                {
                    items[i] = offset + i < rounds ? kind : (ushort)0;
                    holds |= items[i] != 0;
                }
                if (items.Length == 0)
                {
                    holds = rounds > 0;
                }
            }
            views.Add(new HolderView(node, inWindow && holds, slots, items));
        }
        return views;
    }
}
