using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation.Content;
using Hollow.Simulation.Movement;

namespace Hollow.Simulation.Nature;

/// <summary>
/// Where a natural object's weak spot can sit (GROUND.md §6.1): on the surface players see, at the object's current
/// stage. The server places the spot and tests hits against it here; clients draw it at the same point. A spot is two
/// numbers on the wire and in saves (<see cref="NaturalOverride.WeakSpotYaw"/>, <see cref="NaturalOverride.WeakSpotLift"/>);
/// what they mean is the surface's business. gather.json's <c>spot.surface</c> picks one per class.
/// </summary>
public interface ISpotSurface
{
    /// <summary>A spot's point on the drawn surface at a stage, and the surface's outward normal there.</summary>
    (Vector3 Point, Vector3 Normal) At(in NaturalObject o, NaturalKind kind, int stage, ushort yaw, byte lift);

    /// <summary>
    /// Where a swing from <paramref name="eye"/> along <paramref name="direction"/> meets the drawn surface.
    /// <paramref name="hit"/> is where it met the object's hit shape: the answer when the ray misses what is drawn.
    /// </summary>
    Vector3 Meet(in NaturalObject o, NaturalKind kind, int stage, Vector3 eye, Vector3 direction, Vector3 hit);

    /// <summary>The drawn surface's outward normal at (or near) a point on it.</summary>
    Vector3 NormalAt(in NaturalObject o, NaturalKind kind, int stage, Vector3 point);

    /// <summary>
    /// A new spot that faces <paramref name="facing"/> (a yaw about the object's up, toward the hitter), near where the
    /// hit met the surface (<paramref name="meet"/>).
    /// </summary>
    (ushort Yaw, byte Lift) Place(in NaturalObject o, NaturalKind kind, int stage, double facing, Vector3 meet, ref Rng rng);
}

/// <summary>Angles and frames the spot surfaces share.</summary>
public static class SpotFrames
{
    /// <summary>A yaw in 1/65536 of a turn (the wire's and the saves' form).</summary>
    public static ushort Angle16(double radians)
    {
        double turns = radians / DMath.TwoPi;
        turns -= Math.Floor(turns);
        return (ushort)((int)Math.Round(turns * 65536, MidpointRounding.ToEven) & 0xFFFF);
    }

    public static double Radians(ushort angle) => (short)angle * (Math.PI / 32768);

    /// <summary>The horizontal direction a yaw faces at an object.</summary>
    public static Vector3 Facing(in NaturalObject o, double yaw) => CharacterMotor.HorizontalBasis((float)yaw, o.Up).Forward;

    /// <summary>The yaw (about the object's up) of a direction.</summary>
    public static double YawOf(in NaturalObject o, Vector3 direction)
    {
        Vector3 flat = direction - o.Up * Vector3.Dot(direction, o.Up);
        return DMath.Atan2(-flat.X, -flat.Z);
    }

    /// <summary>
    /// The frame a model of the object is drawn in (the client's <c>NatureView.Upright</c>): X its right, Y its up, Z its
    /// back, for the object's yaw. A model's point (x, y, z) at size 1 is at <c>o.Position + o.Scale * (x X + y Y + z Z)</c>.
    /// </summary>
    public static (Vector3 X, Vector3 Y, Vector3 Z) ModelFrame(in NaturalObject o)
    {
        (Vector3 forward, Vector3 right) = CharacterMotor.HorizontalBasis(o.Yaw, o.Up);
        return (right, o.Up, -forward);
    }
}

/// <summary>
/// A tree's trunk (gather.json <c>spot.surface</c> "trunk"): the hit capsule's side (kind radius × size, which matches
/// the drawn trees, FloraLibrary), between two heights above the base. A spot's lift is its height in that band; a new
/// one goes near the height of the hit that made it (Rust: around the trunk, facing the player, near the hit height),
/// give or take <see cref="Follow"/>.
/// </summary>
public sealed class TrunkSurface(float low, float high, float follow) : ISpotSurface
{
    public float Low { get; } = low;

    public float High { get; } = Math.Max(high, low + 0.01f);

    public float Follow { get; } = follow;

    public (Vector3 Point, Vector3 Normal) At(in NaturalObject o, NaturalKind kind, int stage, ushort yaw, byte lift)
    {
        Vector3 facing = SpotFrames.Facing(o, SpotFrames.Radians(yaw));
        float height = Low + (High - Low) * lift / 255f;
        return (o.Position + o.Up * height + facing * (kind.Radius * o.Scale), facing);
    }

    public Vector3 Meet(in NaturalObject o, NaturalKind kind, int stage, Vector3 eye, Vector3 direction, Vector3 hit)
    {
        // The ray against the trunk's upright cylinder.
        Vector3 up = o.Up, p = eye - o.Position;
        Vector3 pf = p - up * Vector3.Dot(p, up), df = direction - up * Vector3.Dot(direction, up);
        float r = kind.Radius * o.Scale;
        float a = Vector3.Dot(df, df), b = 2 * Vector3.Dot(pf, df), c = Vector3.Dot(pf, pf) - r * r;
        float disc = b * b - 4 * a * c;
        if (a < 1e-8f || disc < 0)
        {
            return hit;
        }
        float t = (-b - MathF.Sqrt(disc)) / (2 * a);
        if (t < 0)
        {
            return hit;
        }
        Vector3 point = eye + direction * t;
        float height = Vector3.Dot(point - o.Position, up);
        return height >= 0 && height <= kind.Height * o.Scale ? point : hit;
    }

    public Vector3 NormalAt(in NaturalObject o, NaturalKind kind, int stage, Vector3 point)
    {
        Vector3 d = point - o.Position;
        d -= o.Up * Vector3.Dot(d, o.Up);
        return d.LengthSquared() > 1e-8f ? Vector3.Normalize(d) : SpotFrames.Facing(o, 0);
    }

    public (ushort Yaw, byte Lift) Place(in NaturalObject o, NaturalKind kind, int stage, double facing, Vector3 meet, ref Rng rng)
    {
        float height = Vector3.Dot(meet - o.Position, o.Up) + (float)rng.Range(-Follow, Follow);
        float lift = Math.Clamp((height - Low) / (High - Low), 0, 1);
        return (SpotFrames.Angle16(facing), (byte)Math.Round(lift * 255));
    }
}

/// <summary>
/// The measured top of a model drawn for the object (gather.json <c>spot.surface</c> "measured"; surfaces.json, built
/// by the client's survey from the outcrops' stage models): heights straight down at rings × directions about the base.
/// A spot sits exactly on one of those points, so it lies on the rock players see at every stage; its yaw picks the
/// direction, its lift the ring. Points too low (under <see cref="Low"/> of the stage's top: the rock's foot, a gap) are
/// never used.
/// </summary>
public sealed class MeasuredSurface : ISpotSurface
{
    private readonly float[] _rings;
    private readonly List<float[,]> _heights;
    private readonly List<bool[,]> _usable = new();
    private readonly List<Vector3[,]> _normals = new();
    private readonly List<float> _tops = new();
    private readonly float[] _cos;
    private readonly float[] _sin;
    private readonly int _directions;

    /// <summary>How far past the outer ring the measured top is taken to fall to the ground (metres at size 1).</summary>
    private const float Skirt = 0.25f;

    public MeasuredSurface(float[] rings, List<float[,]> heights, float low)
    {
        _rings = rings;
        _heights = heights;
        Low = low;
        _directions = heights.Count > 0 ? heights[0].GetLength(1) : 1;
        _cos = new float[_directions];
        _sin = new float[_directions];
        for (int d = 0; d < _directions; d++)
        {
            double angle = d * DMath.TwoPi / _directions;
            _cos[d] = (float)DMath.Cos(angle);
            _sin[d] = (float)DMath.Sin(angle);
        }
        foreach (float[,] table in heights)
        {
            float top = 0;
            foreach (float h in table)
            {
                top = float.IsNaN(h) ? top : Math.Max(top, h);
            }
            var usable = new bool[rings.Length, _directions];
            var normals = new Vector3[rings.Length, _directions];
            for (int r = 0; r < rings.Length; r++)
            {
                for (int d = 0; d < _directions; d++)
                {
                    usable[r, d] = !float.IsNaN(table[r, d]) && table[r, d] >= low * top;
                    normals[r, d] = LocalNormal(table, r, d);
                }
            }
            _usable.Add(usable);
            _normals.Add(normals);
            _tops.Add(top);
        }
    }

    /// <summary>Points lower than this share of the stage's top are not used for spots.</summary>
    public float Low { get; }

    public int Stages => _heights.Count;

    /// <summary>The rings' radii (metres at size 1) and how many directions each has.</summary>
    public IReadOnlyList<float> Rings => _rings;

    public int Directions => _directions;

    /// <summary>The highest measured point of a stage.</summary>
    public float Top(int stage) => _tops[Clamp(stage)];

    /// <summary>The measured height at a ring and direction of a stage (NaN where the model has nothing).</summary>
    public float Height(int stage, int ring, int direction) => _heights[Clamp(stage)][ring, direction];

    public (Vector3 Point, Vector3 Normal) At(in NaturalObject o, NaturalKind kind, int stage, ushort yaw, byte lift)
    {
        int s = Clamp(stage);
        (Vector3 x, Vector3 y, Vector3 z) = SpotFrames.ModelFrame(o);
        int direction = DirectionOf(SpotFrames.Facing(o, SpotFrames.Radians(yaw)), x, z);
        int ring = Math.Min(_rings.Length - 1, lift * _rings.Length / 256);
        (ring, direction) = Nearest(s, ring, direction);
        Vector3 local = Sample(s, ring, direction);
        Vector3 n = _normals[s][ring, direction];
        return (o.Position + o.Scale * (local.X * x + local.Y * y + local.Z * z), Vector3.Normalize(n.X * x + n.Y * y + n.Z * z));
    }

    public Vector3 Meet(in NaturalObject o, NaturalKind kind, int stage, Vector3 eye, Vector3 direction, Vector3 hit)
    {
        int s = Clamp(stage);
        (Vector3 x, Vector3 y, Vector3 z) = SpotFrames.ModelFrame(o);
        float scale = Math.Max(o.Scale, 0.05f);
        Vector3 rel = eye - o.Position;
        var e = new Vector3(Vector3.Dot(rel, x), Vector3.Dot(rel, y), Vector3.Dot(rel, z)) / scale;
        var d = new Vector3(Vector3.Dot(direction, x), Vector3.Dot(direction, y), Vector3.Dot(direction, z));
        // March from a little before the hit shape to a little past it, in 2 cm steps of the model, then refine.
        float along = Vector3.Dot(hit - eye, direction) / scale;
        const float Step = 0.02f;
        float start = Math.Max(0, along - 0.6f), end = along + 2.4f;
        float previous = start;
        for (float t = start; t <= end; t += Step)
        {
            Vector3 p = e + d * t;
            if (p.Y <= TopAt(s, p.X, p.Z))
            {
                float lo = previous, hi = t;
                for (int i = 0; i < 6; i++)
                {
                    float mid = (lo + hi) / 2;
                    Vector3 m = e + d * mid;
                    if (m.Y <= TopAt(s, m.X, m.Z))
                    {
                        hi = mid;
                    }
                    else
                    {
                        lo = mid;
                    }
                }
                return eye + direction * (hi * scale);
            }
            previous = t;
        }
        return hit;
    }

    public Vector3 NormalAt(in NaturalObject o, NaturalKind kind, int stage, Vector3 point)
    {
        int s = Clamp(stage);
        (Vector3 x, Vector3 y, Vector3 z) = SpotFrames.ModelFrame(o);
        float scale = Math.Max(o.Scale, 0.05f);
        Vector3 rel = (point - o.Position) / scale;
        float lx = Vector3.Dot(rel, x), lz = Vector3.Dot(rel, z);
        // The slope of the measured top around the point.
        const float H = 0.06f;
        float dx = TopAt(s, lx + H, lz) - TopAt(s, lx - H, lz), dz = TopAt(s, lx, lz + H) - TopAt(s, lx, lz - H);
        var n = new Vector3(-dx, 2 * H, -dz);
        if (n.LengthSquared() < 1e-10f)
        {
            return o.Up;
        }
        n = Vector3.Normalize(n);
        return Vector3.Normalize(n.X * x + n.Y * y + n.Z * z);
    }

    public (ushort Yaw, byte Lift) Place(in NaturalObject o, NaturalKind kind, int stage, double facing, Vector3 meet, ref Rng rng)
    {
        int s = Clamp(stage);
        (Vector3 x, _, Vector3 z) = SpotFrames.ModelFrame(o);
        int direction = DirectionOf(SpotFrames.Facing(o, facing), x, z);
        // A ring of that direction the spot can sit on, picked at random; the nearest direction that has one if none.
        int usable = 0;
        for (int r = 0; r < _rings.Length; r++)
        {
            usable += _usable[s][r, direction] ? 1 : 0;
        }
        int ring = 0;
        if (usable == 0)
        {
            (ring, direction) = Nearest(s, rng.NextInt(_rings.Length), direction);
        }
        else
        {
            int pick = rng.NextInt(usable);
            for (int r = 0; r < _rings.Length; r++)
            {
                if (_usable[s][r, direction] && pick-- == 0)
                {
                    ring = r;
                    break;
                }
            }
        }
        // The exact direction of that sample, and the middle of its ring's band of lifts.
        Vector3 along = _cos[direction] * x + _sin[direction] * z;
        return (SpotFrames.Angle16(SpotFrames.YawOf(o, along)), (byte)((ring * 256 + 128) / _rings.Length));
    }

    // ---------------------------------------------------------------- the table

    private int Clamp(int stage) => Math.Clamp(stage, 0, _heights.Count - 1);

    private Vector3 Sample(int stage, int ring, int direction)
    {
        float h = _heights[stage][ring, direction];
        return new Vector3(_rings[ring] * _cos[direction], float.IsNaN(h) ? 0 : h, _rings[ring] * _sin[direction]);
    }

    // The direction index nearest a horizontal direction, in the model's frame.
    private int DirectionOf(Vector3 facing, Vector3 x, Vector3 z)
    {
        double angle = DMath.Atan2(Vector3.Dot(facing, z), Vector3.Dot(facing, x));
        int d = (int)Math.Round(angle / DMath.TwoPi * _directions);
        return ((d % _directions) + _directions) % _directions;
    }

    // The usable sample nearest (ring, direction): the same direction first, nearer rings first, then the directions
    // either side, further and further out.
    private (int Ring, int Direction) Nearest(int stage, int ring, int direction)
    {
        bool[,] usable = _usable[stage];
        for (int offset = 0; offset <= _directions / 2; offset++)
        {
            for (int side = 0; side < (offset == 0 ? 1 : 2); side++)
            {
                int d = ((direction + (side == 0 ? offset : -offset)) % _directions + _directions) % _directions;
                for (int step = 0; step < _rings.Length * 2; step++)
                {
                    int r = ring + (step % 2 == 0 ? step / 2 : -(step / 2 + 1));
                    if (r >= 0 && r < _rings.Length && usable[r, d])
                    {
                        return (r, d);
                    }
                }
            }
        }
        return (ring, direction);
    }

    // The measured top at a point of the model's horizontal plane: between rings and directions, linearly; inside the
    // first ring, its height; past the last, falling to the ground over the skirt; nothing measured counts as ground.
    private float TopAt(int stage, float x, float z)
    {
        float rho = MathF.Sqrt(x * x + z * z);
        float last = _rings[^1];
        if (rho > last + Skirt)
        {
            return 0;
        }
        double angle = DMath.Atan2(z, x);
        if (angle < 0)
        {
            angle += DMath.TwoPi;
        }
        double at = angle / DMath.TwoPi * _directions;
        int d0 = Math.Min((int)at, _directions - 1), d1 = (d0 + 1) % _directions;
        float fd = (float)(at - d0);
        float[,] table = _heights[stage];
        float Ring(int r)
        {
            float a = table[r, d0], b = table[r, d1];
            return (float.IsNaN(a) ? 0 : a) * (1 - fd) + (float.IsNaN(b) ? 0 : b) * fd;
        }
        if (rho <= _rings[0])
        {
            return Ring(0);
        }
        if (rho >= last)
        {
            return Ring(_rings.Length - 1) * (1 - (rho - last) / Skirt);
        }
        int r0 = 0;
        while (r0 + 1 < _rings.Length - 1 && _rings[r0 + 1] < rho)
        {
            r0++;
        }
        float fr = (rho - _rings[r0]) / (_rings[r0 + 1] - _rings[r0]);
        return Ring(r0) * (1 - fr) + Ring(r0 + 1) * fr;
    }

    // A sample's outward normal in the model's frame, from its neighbours along the ring and across the rings.
    private Vector3 LocalNormal(float[,] table, int ring, int direction)
    {
        Vector3 Point(int r, int d)
        {
            float h = table[r, d];
            return new Vector3(_rings[r] * _cos[d], float.IsNaN(h) ? 0 : h, _rings[r] * _sin[d]);
        }
        int next = (direction + 1) % _directions, prev = (direction + _directions - 1) % _directions;
        Vector3 along = Point(ring, next) - Point(ring, prev);
        int outer = Math.Min(ring + 1, _rings.Length - 1), inner = Math.Max(ring - 1, 0);
        Vector3 across = Point(outer, direction) - Point(inner, direction);
        Vector3 n = Vector3.Cross(along, across);
        if (n.Y < 0)
        {
            n = -n;
        }
        // Outward from the middle too, so a steep flank's normal leans out, never into the rock.
        Vector3 out0 = new(_cos[direction], 0, _sin[direction]);
        if (n.LengthSquared() < 1e-10f || Vector3.Dot(n, out0) < -0.2f * n.Length())
        {
            n = Vector3.Normalize(Vector3.UnitY + out0 * 0.5f);
        }
        return Vector3.Normalize(n);
    }
}
