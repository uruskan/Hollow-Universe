using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Hollow.Simulation.Ships.Modules;

/// <summary>Why a build can't be (VEHICLES.md §3.3), or none.</summary>
public enum BuildProblem : byte
{
    None,

    /// <summary>A socket the kind hasn't.</summary>
    NoSocket,

    /// <summary>An item that is no module.</summary>
    UnknownModule,

    /// <summary>The socket (or a neighbour a long module takes) doesn't take that module.</summary>
    DoesNotFit,

    /// <summary>The module doesn't stand at that level, or the socket has no such level.</summary>
    NoLevel,

    /// <summary>A long module runs past the end of its row.</summary>
    NoRoom,

    /// <summary>Another module is there already.</summary>
    Taken,

    /// <summary>A level-2 module over a socket with nothing under it, or under it a roof that carries no load.</summary>
    NoRoof,

    /// <summary>A level-2 module across roofs of different heights.</summary>
    UnevenRoof,

    /// <summary>Taller than the kind allows.</summary>
    TooTall,

    /// <summary>More entries than a build holds.</summary>
    TooMany,
}

/// <summary>
/// Makes a ship's layout from its kind and its build (VEHICLES.md §9.1): the kind's own, then each module's seats, doors,
/// target parts, hull boxes and hold where its socket puts it, the mass, centre of mass and inertia of the whole, the fuel
/// the modules add. The same build of the same kind always makes the same layout (the server's, every client's, a save's),
/// and it is made once and kept. Everything downstream (seats, doors, damage, the motor, the views) reads the result.
/// </summary>
public static class ShipAssembler
{
    private static readonly ConditionalWeakTable<ShipDef, Dictionary<ShipBuild, ShipDef>> Made = new();

    /// <summary>Why a build can't be on a kind, or <see cref="BuildProblem.None"/>.</summary>
    public static BuildProblem Check(ShipDef kind, ShipBuild build, Func<int, ModuleDef?> modules) =>
        Arrange(kind.Kind, build, modules, out _, out _);

    /// <summary>The layout of a kind with a build (the kind itself for none). The build must pass <see cref="Check"/>.</summary>
    public static ShipDef Compose(ShipDef kind, ShipBuild build, Func<int, ModuleDef?> modules)
    {
        kind = kind.Kind;
        if (build.IsEmpty)
        {
            return kind;
        }
        Dictionary<ShipBuild, ShipDef> made = Made.GetOrCreateValue(kind);
        lock (made)
        {
            if (made.TryGetValue(build, out ShipDef? known))
            {
                return known;
            }
            BuildProblem problem = Arrange(kind, build, modules, out ModuleDef[] defs, out Vector3[] origins);
            if (problem != BuildProblem.None)
            {
                throw new ArgumentException($"{kind.Key}: build {build} can't be ({problem})", nameof(build));
            }
            ShipDef composed = Assemble(kind, build, defs, origins);
            made[build] = composed;
            return composed;
        }
    }

    /// <summary>
    /// A build read from untrusted bytes made sane on a kind: its entries in order, each kept only if the build so far stays
    /// possible with it (a module the game lost, a socket the kind no longer has, a roof gone from under it are left out).
    /// </summary>
    public static ShipBuild Sane(ShipDef kind, IEnumerable<Installed> entries, Func<int, ModuleDef?> modules)
    {
        kind = kind.Kind;
        ShipBuild build = ShipBuild.Empty;
        foreach (Installed entry in new ShipBuild(entries).Entries)
        {
            ShipBuild with = build.With(entry);
            if (Check(kind, with, modules) == BuildProblem.None)
            {
                build = with;
            }
        }
        return build;
    }

    // Each entry's module and origin, and the first rule it breaks. Level 1 is placed before level 2, so a level-2 module meets
    // every level-1 module under it already placed (a long one starting at the socket before, one at the socket after).
    private static BuildProblem Arrange(ShipDef kind, ShipBuild build, Func<int, ModuleDef?> modules, out ModuleDef[] defs, out Vector3[] origins)
    {
        IReadOnlyList<Installed> entries = build.Entries;
        defs = new ModuleDef[entries.Count];
        origins = new Vector3[entries.Count];
        if (entries.Count > ShipBuild.MostEntries)
        {
            return BuildProblem.TooMany;
        }
        IReadOnlyList<ShipSocketDef> sockets = kind.Sockets;
        // Who stands on each socket at each level: the entry's index, or −1.
        var taken = new int[sockets.Count, 2];
        for (int s = 0; s < sockets.Count; s++)
        {
            taken[s, 0] = taken[s, 1] = -1;
        }
        var order = new List<int>(entries.Count);
        for (int level = 1; level <= 2; level++)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Level == level)
                {
                    order.Add(i);
                }
            }
        }
        if (order.Count != entries.Count)
        {
            return BuildProblem.NoLevel;
        }
        foreach (int i in order)
        {
            Installed e = entries[i];
            if (e.Socket >= sockets.Count)
            {
                return BuildProblem.NoSocket;
            }
            if (modules(e.Module) is not { } module)
            {
                return BuildProblem.UnknownModule;
            }
            defs[i] = module;
            if (e.Level is not (1 or 2) || (e.Level == 1 ? !module.Lower : !module.Upper))
            {
                return BuildProblem.NoLevel;
            }
            ShipSocketDef first = sockets[e.Socket];
            float roof = float.NaN;
            for (int k = 0; k < module.Span; k++)
            {
                int s = e.Socket + k;
                if (k > 0 && (s >= sockets.Count || first.Row.Length == 0 || sockets[s].Row != first.Row))
                {
                    return BuildProblem.NoRoom;
                }
                ShipSocketDef socket = sockets[s];
                if (!socket.Takes(module))
                {
                    return BuildProblem.DoesNotFit;
                }
                if (socket.Levels < e.Level)
                {
                    return BuildProblem.NoLevel;
                }
                if (taken[s, e.Level - 1] >= 0)
                {
                    return BuildProblem.Taken;
                }
                if (e.Level == 2)
                {
                    int under = taken[s, 0];
                    if (under < 0 || !defs[under].Carries)
                    {
                        return BuildProblem.NoRoof;
                    }
                    float top = origins[under].Y + defs[under].Height;
                    if (!float.IsNaN(roof) && MathF.Abs(top - roof) > 0.01f)
                    {
                        return BuildProblem.UnevenRoof;
                    }
                    roof = top;
                }
                taken[s, e.Level - 1] = i;
            }
            origins[i] = e.Level == 2 ? first.At with { Y = roof } : first.At;
            if (kind.BuildHeight > 0 && origins[i].Y + module.Height > kind.BuildHeight + 1e-3f)
            {
                return BuildProblem.TooTall;
            }
        }
        return BuildProblem.None;
    }

    private static ShipDef Assemble(ShipDef kind, ShipBuild build, ModuleDef[] defs, Vector3[] origins)
    {
        var seats = new List<ShipSeatDef>(kind.Seats);
        var doors = new List<ShipDoorDef>(kind.Doors);
        var parts = new List<ShipTargetPart>(kind.Parts);
        var hull = new List<HullBox>(kind.Hull);
        var hullParts = new List<byte>();
        for (int b = 0; b < kind.Hull.Count; b++)
        {
            hullParts.Add(kind.HullPart(b));
        }
        var holds = new List<ShipHold>(kind.Holds);
        var placed = new List<PlacedModule>();
        // One seat flies the ship: the kind's own helm, else the front-most helm seat of a level-1 module (VEHICLES.md §3.3:
        // the front cab drives); every other helm seat rides as a passenger.
        int helm = kind.Seats.Exists(s => s.Flies) ? -1 : FrontHelm(defs, origins, build);
        int moduleSeats = 0;
        var wheels = new List<WheelDef>(kind.Wheels);
        float power = kind.EnginePower;
        var sums = new SortedDictionary<string, float>(StringComparer.Ordinal);
        float mass = kind.Mass, water = kind.WaterVolume;
        Vector3 moment = kind.Mass * kind.CentreOfMass;
        float fuel = kind.FuelCapacity;
        for (int i = 0; i < defs.Length; i++)
        {
            ModuleDef m = defs[i];
            Installed entry = build.Entries[i];
            Vector3 o = origins[i];
            string prefix = $"{kind.Sockets[entry.Socket].Key}.{entry.Level}.";
            int seat0 = seats.Count, door0 = doors.Count, part0 = parts.Count, box0 = hull.Count;
            placed.Add(new PlacedModule(entry, m, o, seat0, door0, part0, box0));
            foreach (ShipDoorDef d in m.Doors)
            {
                doors.Add(new ShipDoorDef
                {
                    Index = door0 + d.Index, Key = prefix + d.Key, Name = d.Name, Parts = d.Parts, Clip = d.Clip, Seconds = d.Seconds, Opens = d.Opens,
                    Ease = d.Ease, Health = d.Health,
                });
            }
            foreach (ShipSeatDef s in m.Seats)
            {
                bool flies = s.Flies && moduleSeats++ == helm;
                seats.Add(new ShipSeatDef
                {
                    Index = seat0 + s.Index, Key = prefix + s.Key, Name = s.Name, Role = s.Flies && !flies ? SeatRole.Passenger : s.Role,
                    Station = s.Station, At = Moved(s.At, o), Eye = Moved(s.Eye, o), Exits = s.Exits.ConvertAll(x => Moved(x, o)),
                    Exposed = s.Exposed, Sitters = s.Sitters, Works = s.Works, Verb = s.Verb, VerbKey = s.VerbKey, Door = Offset(s.Door, door0),
                });
            }
            foreach (ShipTargetPart p in m.Parts)
            {
                parts.Add(new ShipTargetPart
                {
                    Index = part0 + p.Index, Key = prefix + p.Key, Name = p.Name, At = p.HasBall ? Moved(p.At, o) : p.At, Radius = p.Radius,
                    Pieces = p.Pieces, Capabilities = p.Capabilities, Seat = Offset(p.Seat, seat0), Door = Offset(p.Door, door0),
                    Through = Offset(p.Through, door0), Health = p.Health,
                });
            }
            byte body = m.Parts.Count > 0 ? (byte)part0 : ShipParts.Hull;
            foreach (HullBox box in m.Hull)
            {
                hull.Add(box with { Centre = box.Centre + o });
                hullParts.Add(body);
                if (!box.Skid && !box.Fragile)
                {
                    water += 8 * box.HalfExtents.X * box.HalfExtents.Y * box.HalfExtents.Z;
                }
            }
            if (m.Hold > 0 && m.HoldPart >= 0)
            {
                holds.Add(new ShipHold(part0 + m.HoldPart, m.Hold, placed.Count - 1));
            }
            foreach (WheelDef w in m.Wheels)
            {
                wheels.Add(w.Moved(o, prefix));
            }
            power += m.Power;
            if (m.Power != 0)
            {
                sums["power"] = sums.GetValueOrDefault("power") + m.Power;
            }
            foreach ((string name, float value) in m.Drive)
            {
                sums[name] = sums.GetValueOrDefault(name) + value;
            }
            mass += m.Mass;
            moment += m.Mass * (o + m.CentreOfMass);
            fuel += m.FuelCapacity;
        }
        Vector3 centre = moment / mass;
        // The kind's inertia moved to the new centre of mass, and each module's as a solid box of its hull's bounds at its own.
        Vector3 inertia = kind.Inertia + Shift(kind.Mass, kind.CentreOfMass - centre);
        for (int i = 0; i < defs.Length; i++)
        {
            ModuleDef m = defs[i];
            inertia += BoxInertia(m) + Shift(m.Mass, origins[i] + m.CentreOfMass - centre);
        }
        ShipDef composed = kind.CopyFor(build, placed, hullParts, holds, sums);
        composed.Seats = seats;
        composed.Doors = doors;
        composed.Parts = parts;
        composed.Hull = hull;
        composed.Mass = mass;
        composed.CentreOfMass = centre;
        composed.Inertia = inertia;
        composed.FuelCapacity = fuel;
        composed.WaterVolume = water;
        composed.Wheels = wheels;
        composed.EnginePower = power;
        composed.ComputeBounds();
        return composed;
    }

    // The module seat (counted over the modules' seats in the build's order) that drives: the helm seat of a level-1 module
    // furthest forward, the first of equals; −1 when none.
    private static int FrontHelm(ModuleDef[] defs, Vector3[] origins, ShipBuild build)
    {
        int best = -1, n = 0;
        float front = float.MinValue;
        for (int i = 0; i < defs.Length; i++)
        {
            foreach (ShipSeatDef s in defs[i].Seats)
            {
                float z = origins[i].Z + s.At.Position.Z;
                if (s.Flies && build.Entries[i].Level == 1 && z > front)
                {
                    front = z;
                    best = n;
                }
                n++;
            }
        }
        return best;
    }

    private static ShipSocket Moved(ShipSocket socket, Vector3 by) => socket with { Position = socket.Position + by };

    private static List<ShipSocket> ConvertAll(this IReadOnlyList<ShipSocket> list, Func<ShipSocket, ShipSocket> f)
    {
        var result = new List<ShipSocket>(list.Count);
        foreach (ShipSocket s in list)
        {
            result.Add(f(s));
        }
        return result;
    }

    private static bool Exists(this IReadOnlyList<ShipSeatDef> seats, Predicate<ShipSeatDef> match)
    {
        foreach (ShipSeatDef s in seats)
        {
            if (match(s))
            {
                return true;
            }
        }
        return false;
    }

    private static int Offset(int index, int by) => index < 0 ? index : index + by;

    // The parallel axis: a mass at an offset adds m(y² + z²), m(x² + z²), m(x² + y²) about the x, y and z axes.
    private static Vector3 Shift(float mass, Vector3 d) => mass * new Vector3(d.Y * d.Y + d.Z * d.Z, d.X * d.X + d.Z * d.Z, d.X * d.X + d.Y * d.Y);

    private static Vector3 BoxInertia(ModuleDef m)
    {
        if (m.Hull.Count == 0)
        {
            return Vector3.Zero;
        }
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        foreach (HullBox box in m.Hull)
        {
            min = Vector3.Min(min, box.Centre - box.HalfExtents);
            max = Vector3.Max(max, box.Centre + box.HalfExtents);
        }
        Vector3 s = max - min;
        return m.Mass / 12 * new Vector3(s.Y * s.Y + s.Z * s.Z, s.X * s.X + s.Z * s.Z, s.X * s.X + s.Y * s.Y);
    }
}
