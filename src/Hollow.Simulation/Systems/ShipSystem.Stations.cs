using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Physics;
using Hollow.Simulation.Players;
using Hollow.Simulation.Ships;

namespace Hollow.Simulation;

// Crewed batteries (IRON_WAKE.md §6, IW3): each tick, every station of a ship takes its gunner's controls (the asked
// direction in the world, the trigger, the ammo choice and the cycle tap), slews its mount there through the ship's pose
// within its arcs and rates (its yaw and pitch are the ship's parts, moved through MoveParts), steps its loaders through
// batteries.json's phases (taking rounds from its magazine at the phase the table says, waiting there with nothing to
// load, drawing rounds of another kind back by the same phases in reverse), and fires: one barrel a press (alternate) or
// rippled while held, each shot the projectile system's own rocket from the barrel's muzzle. A press that can't fire is
// refused with its reason (§6.7) and never kept. Everything comes from the battery's and the ship's data.
public sealed partial class ShipSystem
{
    private GunSystem? _gunsForStations;
    private GunSystem StationGuns => _gunsForStations ??= Zone.Require<GunSystem>();

    /// <summary>A station's refusal is said again after this many ticks of the trigger held (a ripple waiting on its loader).</summary>
    private const int RefusalRepeatTicks = 15;

    private readonly int[] _mountParts = new int[2];
    private readonly float[] _mountStates = new float[2];

    /// <summary>The station a seat works on a ship, or null.</summary>
    public static StationState? StationAt(Ship ship, int seat)
    {
        foreach (StationState st in ship.Stations)
        {
            if (st.Def.Seat == seat)
            {
                return st;
            }
        }
        return null;
    }

    /// <summary>The station a character works (seated at it), with its ship, or null.</summary>
    public StationState? StationOf(Entity character, out Ship ship)
    {
        if (TrySeat(character, out ship, out ShipSeatDef seat) && seat.Role == SeatRole.Station)
        {
            return StationAt(ship, seat.Index);
        }
        ship = null!;
        return null;
    }

    /// <summary>
    /// A gunner's controls for this tick (the server's input of the tick, or a test's): the asked direction (the zone's
    /// frame), the trigger, the zoom, the ammo choice and the cycle counter. False when the character works no station.
    /// </summary>
    public bool SetStationInput(Entity gunner, in StationInput input)
    {
        if (StationOf(gunner, out _) is not { } st)
        {
            return false;
        }
        StationInput given = input;
        float length = given.View.Length();
        given.View = length > 1e-3f && float.IsFinite(length) ? given.View / length : Vector3.Zero;
        st.Input = given;
        return true;
    }

    // ---------------------------------------------------------------- each tick

    // After the ship's own step and its doors: every station's controls, mount, loaders and trigger, before the seated are
    // carried (a gunner turns with the yaw).
    private void StepStations(Ship ship)
    {
        if (ship.Stations.Length == 0 || ship.Def.Interior is not { } interior || ship.Space == null)
        {
            return;
        }
        ShipPose pose = ShipFrame.PoseOf(ship.State, ship.Def.CentreOfMass);
        foreach (StationState st in ship.Stations)
        {
            ShipStationDef def = st.Def;
            bool disabled = SeatDisabled(ship, def.Seat);
            Entity gunner = Entity.None;
            Player? p = null;
            if (!ship.Occupants[def.Seat].IsNone && Entities.TryFind(ship.Occupants[def.Seat], out Entity found) && PlayerOf(found) is { CanAct: true } who)
            {
                (gunner, p) = (found, who);
            }
            if (p == null)
            {
                // Nobody at the guns (or asleep, down): the mount holds, the trigger is let go, the next gunner's taps start afresh.
                st.Input = default;
                st.CycleSeenSet = false;
            }
            else
            {
                ApplyChoice(ship, st);
            }
            // The mount, unless the station is disabled (it can't turn).
            if (!disabled && StationRules.Slew(interior, st, pose.Rotation))
            {
                (_mountParts[0], _mountParts[1], _mountStates[0], _mountStates[1]) = (def.YawPart, def.PitchPart, st.Yaw, st.Pitch);
                MoveParts(ship, _mountParts, _mountStates);
            }
            StepLoaders(ship, st);
            StepTrigger(ship, st, gunner, p, disabled, pose);
        }
    }

    // The gunner's choice of kind, and a tap of R (its counter changed): rounds of another kind already chambered, or carried
    // by a loader, go back to the magazine by the same mechanism in reverse (§6.5).
    private void ApplyChoice(Ship ship, StationState st)
    {
        BatteryDef battery = st.Def.Battery;
        if (st.Input.Choice is > 0 and var choice && choice <= battery.Ammo.Count && choice - 1 != st.Chosen)
        {
            st.Chosen = (byte)(choice - 1);
            ship.StationsRevision++;
        }
        if (!st.CycleSeenSet)
        {
            st.CycleSeen = st.Input.Cycle;
            st.CycleSeenSet = true;
        }
        else if (st.Input.Cycle != st.CycleSeen)
        {
            st.CycleSeen = st.Input.Cycle;
            DrawBack(ship, st);
        }
    }

    /// <summary>
    /// Draws chambered rounds of another kind than the chosen back into the magazine (a tap of R, §6.5): a loaded barrel's,
    /// or a loader's carried round, runs its phases in reverse to where rounds are taken, and goes back there; then the
    /// chosen kind loads. True when anything went back.
    /// </summary>
    public bool DrawBack(Ship ship, StationState st)
    {
        ushort chosen = st.ChosenItem.Id;
        BatteryLoader loader = st.Def.Battery.Loader;
        bool any = false;
        for (int li = 0; li < st.Loaders.Length; li++)
        {
            ref LoaderState l = ref st.Loaders[li];
            (int first, int count) = st.Def.LoaderBarrels(li);
            if (!l.Active)
            {
                ushort kind = 0;
                int rounds = 0;
                for (int b = first; b < first + count; b++)
                {
                    if (st.Chambers[b] != 0 && st.Chambers[b] != chosen)
                    {
                        kind = st.Chambers[b];
                    }
                    rounds += st.Chambers[b] != 0 ? 1 : 0;
                }
                if (kind == 0)
                {
                    continue;
                }
                for (int b = first; b < first + count; b++)
                {
                    st.Chambers[b] = 0;
                }
                l = new LoaderState { Active = true, Reverse = true, Since = Tick, At = loader.Seconds, Kind = kind, Count = (byte)rounds };
                any = true;
            }
            else if (!l.Reverse && !l.Waiting && l.Kind != 0 && l.Kind != chosen)
            {
                l.At = l.Elapsed(Tick);
                l.Since = Tick;
                l.Reverse = true;
                any = true;
            }
        }
        if (any)
        {
            ship.StationsRevision++;
        }
        return any;
    }

    // Each loader through its phases (§6.6): an idle one whose barrels are all empty starts (from the end of its first
    // phase: no shot to recover from); one in reverse gives its rounds back where rounds are taken and goes on forward
    // from there; one waiting takes the chosen kind once the magazine has it; one at work takes its round(s) at the start
    // of the table's phase (or waits there, the breech open), and at its last phase's end its barrels are loaded.
    private void StepLoaders(Ship ship, StationState st)
    {
        BatteryLoader loader = st.Def.Battery.Loader;
        if (loader.Phases.Count == 0)
        {
            return;
        }
        for (int li = 0; li < st.Loaders.Length; li++)
        {
            ref LoaderState l = ref st.Loaders[li];
            (int first, int count) = st.Def.LoaderBarrels(li);
            if (!l.Active)
            {
                bool empty = true;
                for (int b = first; b < first + count; b++)
                {
                    empty &= st.Chambers[b] == 0;
                }
                if (!empty)
                {
                    continue;
                }
                l = new LoaderState { Active = true, Since = Tick, At = loader.Phases[0].End };
                ship.StationsRevision++;
            }
            if (l.Reverse)
            {
                if (l.Elapsed(Tick) > loader.TakeAt)
                {
                    continue;
                }
                GiveBack(ship, st, l.Kind, l.Count);
                l = new LoaderState { Active = true, Since = Tick, At = loader.TakeAt };
                ship.StationsRevision++;
            }
            if (l.Waiting)
            {
                if (TakeRounds(st, out ushort kind, out int taken))
                {
                    l.Waiting = false;
                    l.Since = Tick;
                    l.Kind = kind;
                    l.Count = (byte)taken;
                    ship.StationsRevision++;
                }
                continue;
            }
            float elapsed = l.Elapsed(Tick);
            if (l.Kind == 0 && elapsed >= loader.TakeAt)
            {
                if (!TakeRounds(st, out ushort kind, out int taken))
                {
                    l.Waiting = true;
                    l.At = loader.TakeAt;
                    l.Since = Tick;
                    ship.StationsRevision++;
                    continue;
                }
                l.Kind = kind;
                l.Count = (byte)taken;
                ship.StationsRevision++;
            }
            if (elapsed >= loader.Seconds)
            {
                for (int i = 0; i < count && i < l.Count; i++)
                {
                    st.Chambers[first + i] = l.Kind;
                }
                l = default;
                ship.StationsRevision++;
            }
        }
    }

    // A load of the chosen kind from the magazine: a round, or a cassette's worth (as many as it holds, up to the load).
    private static bool TakeRounds(StationState st, out ushort kind, out int taken)
    {
        kind = st.ChosenItem.Id;
        int have = st.Count(kind);
        taken = Math.Min(have, Math.Max(1, st.Def.Battery.RoundsPerLoad));
        if (taken <= 0)
        {
            kind = 0;
            return false;
        }
        InventoryRules.Take(new[] { st.Magazine }, kind, taken);
        return true;
    }

    // Rounds drawn back into the magazine; what doesn't fit (someone filled it meanwhile) falls in a bag at the station.
    private void GiveBack(Ship ship, StationState st, ushort kind, int count)
    {
        if (kind == 0 || count <= 0 || Content.Item(kind) is not { } item)
        {
            return;
        }
        int left = InventoryRules.AddTo(Content, new[] { st.Magazine }, ItemStack.Of(item, count), byPlayer: false);
        if (left > 0)
        {
            Vector3 at = ship.PointOf(ship.Def.PartPoint(st.Def.Part, ship.Parts));
            Zone.Spill().Spill(at, ItemStack.Of(item, left));
        }
    }

    // The trigger (§6.3): the siege battery fires a barrel at each press; the defense mount ripples while it is held, a barrel
    // at a time an interval apart. A press it can't fire is refused, said, and forgotten.
    private void StepTrigger(Ship ship, StationState st, Entity gunner, Player? p, bool disabled, in ShipPose pose)
    {
        ShipStationDef def = st.Def;
        BatteryDef battery = def.Battery;
        bool held = p != null && st.Input.Fire;
        bool press = held && !st.FireWas;
        st.FireWas = held;
        bool wants = battery.Fire == BatteryFire.Alternate ? press : held && Tick >= st.NextRippleTick;
        if (!wants || p == null)
        {
            return;
        }
        Outcome why = CanFire(ship, st, disabled, Tick, out int barrel);
        if (!why.Ok)
        {
            if (press || why.Code != st.Refused || Tick - st.RefusedTick >= RefusalRepeatTicks)
            {
                st.Refused = why.Code;
                st.RefusedDetail = why.Detail;
                st.RefusedTick = Tick;
                ship.StationsRevision++;
            }
            return;
        }
        Fire(ship, st, barrel, gunner, p, pose);
    }

    /// <summary>
    /// Whether a station can fire now (§6.7, in its order: DISABLED, EMPTY, LOADING, LIMIT, TRAVERSING, HULL BLOCKED), and
    /// the barrel that would.
    /// </summary>
    public Outcome CanFire(Ship ship, StationState st, bool disabled, long tick, out int barrel)
    {
        ShipInterior interior = ship.Def.Interior!;
        ShipStationDef def = st.Def;
        BatteryLoader loader = def.Battery.Loader;
        Span<bool> loaded = stackalloc bool[def.Barrels.Count];
        bool carried = false;
        float progress = 0;
        for (int b = 0; b < loaded.Length; b++)
        {
            loaded[b] = st.Loaded(b);
        }
        foreach (LoaderState l in st.Loaders)
        {
            carried |= l.Active && !l.Reverse && !l.Waiting;
            progress = MathF.Max(progress, l.Active ? StationRules.Progress(loader, l, tick) : 0);
        }
        bool hasRounds = carried || st.Count(st.ChosenItem.Id) > 0;
        float misalignment = StationRules.Misalignment(interior, st, ship.Parts, ship.State.Rotation);
        Outcome why = StationRules.Check(st, disabled, loaded, progress, hasRounds, misalignment, out barrel);
        if (why.Ok && HullBlocked(ship, st, barrel))
        {
            return OutcomeCode.HullBlocked;
        }
        return why;
    }

    /// <summary>
    /// Whether a barrel's line, from its breech to <see cref="StationRules.HullLinePast"/> metres past its muzzle, meets the
    /// ship's own space (anything but the station's own parts: §6.7's HULL BLOCKED).
    /// </summary>
    public bool HullBlocked(Ship ship, StationState st, int barrel)
    {
        if (ship.Space is not { } space || ship.Def.Interior is not { } interior)
        {
            return false;
        }
        (Vector3 muzzle, Vector3 breech) = StationRules.BarrelLine(interior, st.Def, barrel, ship.Parts);
        Vector3 line = muzzle - breech;
        float length = line.Length();
        if (length < 1e-3f)
        {
            return false;
        }
        Vector3 along = line / length;
        int yaw = st.Def.YawPart;
        lock (JoltRuntime.Gate)
        {
            return space.RayPast(breech, along, length + StationRules.HullLinePast, part => Carries(interior, yaw, part), out _);
        }
    }

    // Whether a part is carried by another (or is it): a station's own parts ride its yaw.
    private static bool Carries(ShipInterior interior, int carrier, int part)
    {
        for (int p = part; p > 0 && p < interior.Parts.Count; p = interior.Parts[p].Parent)
        {
            if (p == carrier)
            {
                return true;
            }
        }
        return false;
    }

    // A shot (§6.3): from the barrel's muzzle (the ship's pose × the mount × the barrel), along the barrel at the launch
    // profile's muzzle speed with the ship's velocity there, the chambered round as the projectile system's rocket; the
    // barrel recoils and its loader starts (a mount's once its last barrel is empty).
    private void Fire(Ship ship, StationState st, int barrel, Entity gunner, Player p, in ShipPose pose)
    {
        ShipInterior interior = ship.Def.Interior!;
        ShipStationDef def = st.Def;
        BatteryDef battery = def.Battery;
        ushort kind = st.Chambers[barrel];
        if (Content.Item(kind) is not { Bullet: not null } round || battery.Launch.Of(kind) is not { } profile)
        {
            st.Chambers[barrel] = 0;
            return;
        }
        (Vector3 muzzle, _) = StationRules.BarrelLine(interior, def, barrel, ship.Parts);
        Vector3 direction = StationRules.BarrelDirection(interior, def, ship.Parts);
        Vector3 origin = pose.ToWorld(muzzle);
        ref Character c = ref Entities.Get<Character>(gunner);
        float rewind = c.Actions.ViewDelay / 4f;
        st.Shots++;
        StationGuns.LaunchBattery(p.Id, p.Name, round, battery, profile, origin, pose.DirectionToWorld(direction), pose.VelocityAt(origin), st.Shots, def.Index,
            barrel, rewind);
        st.Chambers[barrel] = 0;
        st.ShotTicks[barrel] = Tick;
        st.NextBarrel = (barrel + 1) % def.Barrels.Count;
        st.NextRippleTick = Tick + battery.IntervalTicks;
        st.Refused = OutcomeCode.Done;
        int li = def.LoaderOf(barrel);
        (int first, int count) = def.LoaderBarrels(li);
        bool empty = true;
        for (int b = first; b < first + count; b++)
        {
            empty &= st.Chambers[b] == 0;
        }
        if (empty && !st.Loaders[li].Active)
        {
            st.Loaders[li] = new LoaderState { Active = true, Since = Tick, At = 0 };
        }
        ship.StationsRevision++;
        Events.Add(new TurretFired(Ship: ship.Id, Station: def.Index, Barrel: barrel, Number: st.Shots, Tick: Tick, Kind: kind, Gunner: p.Id));
    }

    // ---------------------------------------------------------------- the magazine (§6.5)

    /// <summary>
    /// Opens a station's magazine (at its part, on foot: E, OPEN MAGAZINE; or from its seat: Tab) for the ship's crew and
    /// its gunner: it is looted as a box that takes only the battery's rockets.
    /// </summary>
    internal Outcome OpenMagazine(Entity entity, Player p, Ship ship, int part)
    {
        if (part <= 0 || part >= ship.Def.Parts.Count || ship.Def.Parts[part].Station is not (>= 0 and var station) || station >= ship.Stations.Length)
        {
            return OutcomeCode.NotFound;
        }
        bool gunner = StationOf(entity, out Ship seated) is { } st && seated == ship && st.Def.Index == station;
        if (!gunner && !ship.IsAuthorised(p.Id.Value))
        {
            return OutcomeCode.NotCrew;
        }
        return Zone.Require<InteractionSystem>().OpenLoot(p, ship.Id, part);
    }

    /// <summary>A gunner's Tab: their station's magazine.</summary>
    public Outcome SeatedMagazine(Entity entity, EntityId target)
    {
        Outcome check = Seated(entity, target, out Player p, out Ship ship, out ShipSeatDef seat);
        if (!check.Ok)
        {
            return check;
        }
        return StationAt(ship, seat.Index) is { } st ? OpenMagazine(entity, p, ship, st.Def.Part) : OutcomeCode.NotAllowed;
    }
}
