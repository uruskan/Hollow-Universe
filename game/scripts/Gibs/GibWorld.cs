using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using HollowUniverse.Fx;

namespace HollowUniverse.Gibs;

/// <summary>
/// Where the floor is under a point: a ray from <paramref name="from"/> along <paramref name="down"/> for
/// <paramref name="length"/> metres; the hit and the surface's normal there. The ground game asks the client's own
/// collision (ground, building, deployables); the viewer a plane.
/// </summary>
public delegate bool GibFloor(Vector3 from, Vector3 down, float length, out Vector3 point, out Vector3 normal);

/// <summary>
/// The pieces of broken things (GROUND.md §9.4): every live piece in one preallocated array, moved by a cheap integrator
/// (gravity, drag, contact with a floor plane sampled under it, bounce, friction, spin, sleep), then resting a few seconds
/// and sinking away; drawn by one <see cref="MultiMeshInstance3D"/> per piece mesh (a whole wall's pieces of one shape in
/// one draw call, ten barrels' alike), whose buffer is written in place and handed over in one call when something moved.
/// A cap on live pieces recycles the oldest first; nothing is allocated per frame once the meshes it draws have batches.
/// </summary>
public partial class GibWorld : Node3D
{
    private enum PieceState : byte
    {
        Free,
        Flying,
        Resting,
        Sinking,
    }

    // A piece's turn as a 3 x 3 matrix (columns are its axes), times its scale.
    private struct Turn
    {
        public float M00, M01, M02, M10, M11, M12, M20, M21, M22;

        public Turn(in Quaternion q, float s)
        {
            float x = q.X, y = q.Y, z = q.Z, w = q.W;
            float xx = x * x, yy = y * y, zz = z * z, xy = x * y, xz = x * z, yz = y * z, wx = w * x, wy = w * y, wz = w * z;
            M00 = (1 - 2 * (yy + zz)) * s;
            M01 = 2 * (xy - wz) * s;
            M02 = 2 * (xz + wy) * s;
            M10 = 2 * (xy + wz) * s;
            M11 = (1 - 2 * (xx + zz)) * s;
            M12 = 2 * (yz - wx) * s;
            M20 = 2 * (xz - wy) * s;
            M21 = 2 * (yz + wx) * s;
            M22 = (1 - 2 * (xx + yy)) * s;
        }

        public readonly Vector3 Apply(in Vector3 v) =>
            new(M00 * v.X + M01 * v.Y + M02 * v.Z, M10 * v.X + M11 * v.Y + M12 * v.Z, M20 * v.X + M21 * v.Y + M22 * v.Z);

        public readonly Vector3 ApplyTransposed(in Vector3 v) =>
            new(M00 * v.X + M10 * v.Y + M20 * v.Z, M01 * v.X + M11 * v.Y + M21 * v.Z, M02 * v.X + M12 * v.Y + M22 * v.Z);
    }

    // The catalogue's numbers for this frame (read once, not per piece).
    private float _airDrag, _spinDrag, _contactSpinDrag, _bounceStop, _sleepSpeed2, _sleepSpin2, _sleepTime, _maxLife, _sink, _sinkDepth, _shrink;

    private struct Piece
    {
        public PieceState State;
        public int Batch;
        public int Slot;
        public int Break;
        public GibChunk Chunk;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Velocity;
        public Vector3 Spin;
        public Vector3 Up;
        public float Gravity;
        public float Scale;
        public float Size;
        public float Bounce;
        public float Friction;
        public float Age;
        public float Timer;
        public float RestFor;
        public float Sleep;
        public float Touching;
        public bool HasFloor;
        public Vector3 FloorPoint;
        public Vector3 FloorNormal;
        public Vector3 FloorAt;
    }

    // A break's floor, sampled once for all its pieces a couple of frames after it (by then what broke has left the
    // collision), then each piece's own as it travels.
    private struct BreakFloor
    {
        public Vector3 Centre;
        public Vector3 Up;
        public Vector3 Base;
        public int Wait;
        public int Tries;
        public bool Known;
        public Vector3 Point;
        public Vector3 Normal;
    }

    private sealed class Batch
    {
        public required MultiMeshInstance3D Node;
        public required MultiMesh Multimesh;
        public required Rid Rid;
        public int Capacity;
        public int Count;
        public int[] Owners = Array.Empty<int>();
        public float[] Buffer = Array.Empty<float>();
        public bool Dirty;
        public bool Listed;

        // What the renderer was last told: how many it draws and the box they stay in (grown as pieces leave it).
        public int Shown;
        public bool Bounded;
        public Aabb Bounds;
    }

    private const int FloorWaitFrames = 2;
    private const int FloorRetries = 6;
    private const float ReSampleDistance = 1.0f;
    private const int ReSamplesPerFrame = 16;

    /// <summary>How far a batch's box reaches past its pieces, so it is rarely told again.</summary>
    private const float BoundsMargin = 3f;

    private Piece[] _pieces = Array.Empty<Piece>();
    private readonly BreakFloor[] _breaks = new BreakFloor[128];
    private int _nextBreak;
    private int _next;
    private readonly Dictionary<Mesh, int> _batchOf = new();
    private readonly List<Batch> _batches = new();
    private readonly List<Batch> _active = new();
    private readonly FxRandom _random = new(0x61B5UL);

    /// <summary>The floor under a point; null: a flat plane through each break's base.</summary>
    public GibFloor? Floor { get; set; }

    /// <summary>The camera distances are measured from (the viewport's when null).</summary>
    public Camera3D? CameraOverride { get; set; }

    /// <summary>Freezes every piece where it is (screenshots).</summary>
    public bool Paused { get; set; }

    /// <summary>Pieces alive now.</summary>
    public int Live { get; private set; }

    /// <summary>Of <see cref="Live"/>, those still moving in the last frame (in flight or settling).</summary>
    public int Moving { get; private set; }

    /// <summary>Pieces recycled while still alive because the cap was reached.</summary>
    public long Recycled { get; private set; }

    /// <summary>Batches drawn now (one draw call each per pass).</summary>
    public int ActiveBatches => _active.Count;

    /// <summary>Batches made so far (one per piece mesh broken once).</summary>
    public int Batches => _batches.Count;

    /// <summary>Microseconds of the last frame: integration, floors and the buffers handed over.</summary>
    public double LastMicroseconds { get; private set; }

    /// <summary>Microseconds of the breaks since the last frame (pieces placed, dust played).</summary>
    public double LastBreakMicroseconds { get; private set; }

    /// <summary>Of <see cref="LastMicroseconds"/>, the part spent handing buffers over.</summary>
    public double LastUploadMicroseconds { get; private set; }

    /// <summary>Batches handed over in the last frame.</summary>
    public int LastUploads { get; private set; }

    private long _breakTicks;

    public override void _Ready()
    {
        if (_pieces.Length == 0)
        {
            _pieces = new Piece[GibCatalog.MaxPieces];
        }
        for (int b = 0; b < _breaks.Length; b++)
        {
            _breaks[b].Wait = -1;
        }
    }

    // ---------------------------------------------------------------- breaking

    /// <summary>
    /// Breaks a gib set standing at <paramref name="at"/> (the model's transform, a uniform scale allowed): its pieces fly
    /// from the <paramref name="blow"/> (a unit direction, or zero) as the <paramref name="cause"/> throws them, under
    /// <paramref name="gravity"/> along −<paramref name="up"/>, with the material's dust. Returns whether pieces (not only
    /// dust) were placed; nothing beyond <see cref="GibCatalog.Far"/>.
    /// </summary>
    public bool Break(GibSet set, Transform3D at, Vector3 blow, GibCause cause, Vector3 up, float gravity, ulong seed, out Vector3 centre)
    {
        long started = Stopwatch.GetTimestamp();
        try
        {
            return Place(set, at, blow, cause, up, gravity, seed, out centre);
        }
        finally
        {
            _breakTicks += Stopwatch.GetTimestamp() - started;
        }
    }

    private bool Place(GibSet set, Transform3D at, Vector3 blow, GibCause cause, Vector3 up, float gravity, ulong seed, out Vector3 centre)
    {
        if (_pieces.Length == 0)
        {
            _Ready();
        }
        float scale = MathF.Max(0.01f, at.Basis.Scale.X);
        Basis turn = at.Basis.Orthonormalized();
        centre = at * set.Bounds.GetCenter();
        Vector3 half = set.Bounds.Size * 0.5f * scale;
        up = up.LengthSquared() > 1e-8f ? up.Normalized() : Vector3.Up;
        float distance = CameraDistance(centre);
        if (distance > GibCatalog.Far)
        {
            return false;
        }
        GibMaterial material = set.Material;
        // Dust in the material's colour over the whole thing, as big as the cause makes it.
        float size = Math.Clamp(half.Length() / 1.2f, 0.5f, 2.2f);
        Vector3 flat = new Vector3(MathF.Max(half.X, half.Z), half.Y, MathF.Max(half.X, half.Z));
        FxDirector.Play(material.Dust, centre, up, new FxParams
        {
            Area = flat * 0.8f, Tint = material.Colour, Direction = blow, Scale = size * cause.Dust, Floor = at.Origin - up * 0.02f,
        });
        if (distance > GibCatalog.Near)
        {
            return false;
        }
        _random.Reseed(seed != 0 ? seed : (ulong)Stopwatch.GetTimestamp());
        bool busy = Live > GibCatalog.Busy * _pieces.Length;
        int b = _nextBreak;
        _nextBreak = (_nextBreak + 1) % _breaks.Length;
        _breaks[b] = new BreakFloor { Centre = centre, Up = up, Base = at.Origin, Wait = FloorWaitFrames };
        float g = gravity > 0 ? gravity : GibCatalog.Gravity;
        float extent = MathF.Max(0.1f, half.Length());
        foreach (GibChunk chunk in set.Chunks)
        {
            if (busy && chunk.Volume < set.MedianVolume)
            {
                continue;
            }
            Transform3D rest = at * chunk.Rest;
            Vector3 position = rest.Origin;
            Vector3 offset = position - centre;
            // Pushed along the blow, harder on the side it came in; spread from the middle; lifted; spun.
            float along = blow == Vector3.Zero ? 0 : Math.Clamp(-offset.Dot(blow) / extent, -1f, 1f);
            Vector3 outward = offset - up * offset.Dot(up) * 0.5f;
            outward = outward.LengthSquared() > 1e-6f ? outward.Normalized() : _random.UnitVector();
            Vector3 velocity = blow * (_random.Range(cause.Push) * (1f + 0.5f * along))
                + outward * _random.Range(cause.Spread) + up * _random.Range(cause.Lift);
            float radius = MathF.Max(0.05f, chunk.Radius * scale);
            Vector3 spin = _random.UnitVector() * (_random.Range(cause.Spin) * material.Spin * Math.Clamp(0.35f / radius, 0.3f, 2.5f));
            int index = Claim();
            _pieces[index] = new Piece
            {
                State = PieceState.Flying,
                Break = b,
                Chunk = chunk,
                Position = position,
                Rotation = new Quaternion((turn * chunk.Rest.Basis).Orthonormalized()),
                Velocity = velocity,
                Spin = spin,
                Up = up,
                Gravity = g,
                Scale = scale,
                Size = scale,
                Bounce = material.Bounce,
                Friction = material.Friction,
                RestFor = _random.Range(GibCatalog.Rest),
            };
            Attach(index);
            Live++;
        }
        return true;
    }

    // A free slot: the next in the ring; a live piece there (the oldest, near enough) goes.
    private int Claim()
    {
        for (int tries = 0; tries < _pieces.Length; tries++)
        {
            int i = _next;
            _next = (_next + 1) % _pieces.Length;
            if (_pieces[i].State == PieceState.Free)
            {
                return i;
            }
            if (tries == _pieces.Length - 1 || _pieces[i].State != PieceState.Flying)
            {
                Recycled++;
                Release(i);
                return i;
            }
        }
        Release(_next);
        return _next;
    }

    private void Release(int index)
    {
        ref Piece p = ref _pieces[index];
        if (p.State == PieceState.Free)
        {
            return;
        }
        Batch batch = _batches[p.Batch];
        int last = batch.Count - 1;
        if (p.Slot != last)
        {
            int moved = batch.Owners[last];
            batch.Owners[p.Slot] = moved;
            Array.Copy(batch.Buffer, last * 12, batch.Buffer, p.Slot * 12, 12);
            _pieces[moved].Slot = p.Slot;
        }
        batch.Count = last;
        batch.Dirty = true;
        p.State = PieceState.Free;
        p.Chunk = null!;
        Live--;
    }

    private void Attach(int index)
    {
        ref Piece p = ref _pieces[index];
        Batch batch = BatchFor(p.Chunk.Mesh);
        if (batch.Count == batch.Capacity)
        {
            Grow(batch, batch.Capacity * 2);
        }
        p.Batch = _batchOf[p.Chunk.Mesh];
        p.Slot = batch.Count;
        batch.Owners[batch.Count++] = index;
        Write(batch, p);
        batch.Dirty = true;
        if (!batch.Listed)
        {
            batch.Listed = true;
            _active.Add(batch);
        }
    }

    private Batch BatchFor(Mesh mesh)
    {
        if (_batchOf.TryGetValue(mesh, out int i))
        {
            return _batches[i];
        }
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh };
        var node = new MultiMeshInstance3D { Name = $"Gibs{_batches.Count}", Multimesh = mm };
        AddChild(node);
        var batch = new Batch { Node = node, Multimesh = mm, Rid = mm.GetRid() };
        Grow(batch, 8);
        _batchOf[mesh] = _batches.Count;
        _batches.Add(batch);
        return batch;
    }

    private static void Grow(Batch batch, int capacity)
    {
        int[] owners = new int[capacity];
        float[] buffer = new float[capacity * 12];
        Array.Copy(batch.Owners, owners, batch.Count);
        Array.Copy(batch.Buffer, buffer, batch.Count * 12);
        batch.Owners = owners;
        batch.Buffer = buffer;
        batch.Capacity = capacity;
        batch.Multimesh.InstanceCount = capacity;
        batch.Dirty = true;
    }

    // The piece's mesh transform (its box's middle turned about, the mesh's own origin placed) into its batch's buffer:
    // Godot's 3 x 4 rows.
    private static void Write(Batch batch, in Piece p) => Write(batch, p, new Turn(p.Rotation, p.Scale));

    private static void Write(Batch batch, in Piece p, in Turn m)
    {
        Vector3 c = p.Chunk.Centre;
        float[] b = batch.Buffer;
        int o = p.Slot * 12;
        b[o] = m.M00;
        b[o + 1] = m.M01;
        b[o + 2] = m.M02;
        b[o + 3] = p.Position.X - (m.M00 * c.X + m.M01 * c.Y + m.M02 * c.Z);
        b[o + 4] = m.M10;
        b[o + 5] = m.M11;
        b[o + 6] = m.M12;
        b[o + 7] = p.Position.Y - (m.M10 * c.X + m.M11 * c.Y + m.M12 * c.Z);
        b[o + 8] = m.M20;
        b[o + 9] = m.M21;
        b[o + 10] = m.M22;
        b[o + 11] = p.Position.Z - (m.M20 * c.X + m.M21 * c.Y + m.M22 * c.Z);
    }

    /// <summary>Removes every piece (the viewer between scenes).</summary>
    public void Clear()
    {
        for (int i = 0; i < _pieces.Length; i++)
        {
            Release(i);
        }
        Upload();
    }

    // ---------------------------------------------------------------- the frame

    /// <summary>Collections the runtime made during the last frame's work (they land in its times; the work allocates nothing).</summary>
    public int LastCollections { get; private set; }

    public override void _Process(double delta)
    {
        int collections = GC.CollectionCount(0);
        long started = Stopwatch.GetTimestamp();
        float dt = Paused ? 0f : MathF.Min((float)delta, 1f / 30f);
        if (dt > 0)
        {
            _airDrag = 1f - GibCatalog.AirDrag * dt;
            _spinDrag = 1f - GibCatalog.SpinDrag * dt;
            _contactSpinDrag = 1f - MathF.Min(1f, GibCatalog.ContactSpinDrag * dt);
            _bounceStop = GibCatalog.BounceStop;
            _sleepSpeed2 = GibCatalog.SleepSpeed * GibCatalog.SleepSpeed;
            _sleepSpin2 = GibCatalog.SleepSpin * GibCatalog.SleepSpin;
            _sleepTime = GibCatalog.SleepTime;
            _maxLife = GibCatalog.MaxLife;
            _sink = GibCatalog.Sink;
            _sinkDepth = GibCatalog.SinkDepth;
            _shrink = GibCatalog.Shrink;
            Floors();
            int resamples = ReSamplesPerFrame;
            int moving = 0;
            for (int i = 0; i < _pieces.Length; i++)
            {
                if (_pieces[i].State != PieceState.Free)
                {
                    moving += _pieces[i].State == PieceState.Flying ? 1 : 0;
                    Step(i, dt, ref resamples);
                }
            }
            Moving = moving;
        }
        long uploading = Stopwatch.GetTimestamp();
        Upload();
        LastUploadMicroseconds = (Stopwatch.GetTimestamp() - uploading) * 1e6 / Stopwatch.Frequency;
        LastBreakMicroseconds = _breakTicks * 1e6 / Stopwatch.Frequency;
        _breakTicks = 0;
        LastMicroseconds = (Stopwatch.GetTimestamp() - started) * 1e6 / Stopwatch.Frequency;
        LastCollections = GC.CollectionCount(0) - collections;
    }

    // Breaks waiting for their floor take it now.
    private void Floors()
    {
        for (int b = 0; b < _breaks.Length; b++)
        {
            ref BreakFloor f = ref _breaks[b];
            if (f.Wait < 0)
            {
                continue;
            }
            if (f.Wait > 0)
            {
                f.Wait--;
                continue;
            }
            if (Sample(f.Centre, f.Up, out f.Point, out f.Normal))
            {
                f.Known = true;
                f.Wait = -1;
            }
            else if (++f.Tries >= FloorRetries)
            {
                // Nothing found under it (or always inside something): its base, flat.
                f.Point = f.Base;
                f.Normal = f.Up;
                f.Known = true;
                f.Wait = -1;
            }
        }
    }

    // The floor under a point: none when the ray starts inside something (what broke may still be there for a frame).
    private bool Sample(Vector3 from, Vector3 up, out Vector3 point, out Vector3 normal)
    {
        if (Floor == null)
        {
            point = default;
            normal = up;
            return false;
        }
        if (!Floor(from, -up, GibCatalog.FloorDepth, out point, out normal))
        {
            return false;
        }
        if ((point - from).Dot(-up) < 0.002f)
        {
            return false;
        }
        normal = normal.LengthSquared() > 1e-6f ? normal.Normalized() : up;
        if (normal.Dot(up) < 0.2f)
        {
            normal = up;
        }
        return true;
    }

    private void Step(int index, float dt, ref int resamples)
    {
        ref Piece p = ref _pieces[index];
        p.Age += dt;
        switch (p.State)
        {
            case PieceState.Flying:
            {
                Turn turn = Fly(ref p, dt, ref resamples);
                if (p.Age > _maxLife)
                {
                    StartSinking(ref p);
                }
                Batch moved = _batches[p.Batch];
                Write(moved, p, turn);
                moved.Dirty = true;
                return;
            }
            case PieceState.Resting:
                p.Timer += dt;
                if (p.Timer >= p.RestFor || p.Age > _maxLife)
                {
                    StartSinking(ref p);
                }
                return;
            case PieceState.Sinking:
            {
                p.Timer += dt;
                float t = p.Timer / _sink;
                if (t >= 1f)
                {
                    Release(index);
                    return;
                }
                float depth = _sinkDepth * MathF.Max(0.05f, p.Chunk.Radius * p.Size);
                p.Position -= p.Up * (depth / _sink * dt);
                p.Scale = p.Size * (1f - _shrink * t);
                Batch batch = _batches[p.Batch];
                Write(batch, p);
                batch.Dirty = true;
                return;
            }
        }
    }

    private static void StartSinking(ref Piece p)
    {
        p.State = PieceState.Sinking;
        p.Timer = 0;
        p.Velocity = Vector3.Zero;
        p.Spin = Vector3.Zero;
    }

    // Free flight (gravity, a little drag, spin), then the floor. Returns the piece's turn, for drawing it.
    private Turn Fly(ref Piece p, float dt, ref int resamples)
    {
        p.Velocity = (p.Velocity - p.Up * (p.Gravity * dt)) * _airDrag;
        p.Spin *= _spinDrag;
        p.Position += p.Velocity * dt;
        // q' = q + dt/2 (0, w) q, normalised: a small rotation about the spin.
        Quaternion q = p.Rotation;
        float hx = p.Spin.X * dt * 0.5f, hy = p.Spin.Y * dt * 0.5f, hz = p.Spin.Z * dt * 0.5f;
        float qx = q.X + (hx * q.W + hy * q.Z - hz * q.Y);
        float qy = q.Y + (hy * q.W + hz * q.X - hx * q.Z);
        float qz = q.Z + (hz * q.W + hx * q.Y - hy * q.X);
        float qw = q.W - (hx * q.X + hy * q.Y + hz * q.Z);
        float inv = 1f / MathF.Sqrt(qx * qx + qy * qy + qz * qz + qw * qw);
        p.Rotation = new Quaternion(qx * inv, qy * inv, qz * inv, qw * inv);
        var turn = new Turn(p.Rotation, p.Scale);
        // The floor: the break's at first, then its own once it has travelled.
        if (!p.HasFloor)
        {
            ref BreakFloor f = ref _breaks[p.Break];
            if (f.Known)
            {
                p.HasFloor = true;
                p.FloorPoint = f.Point;
                p.FloorNormal = f.Normal;
                p.FloorAt = p.Position;
                if ((p.Position - f.Point).Dot(f.Normal) < -0.2f * p.Chunk.Radius * p.Scale && p.Age < 0.5f)
                {
                    // Buried (a foundation's lower pieces): it stays where it is and sinks in its turn.
                    Settle(ref p);
                    return turn;
                }
            }
        }
        else if (resamples > 0)
        {
            Vector3 moved = p.Position - p.FloorAt;
            moved -= p.Up * moved.Dot(p.Up);
            if (moved.LengthSquared() > ReSampleDistance * ReSampleDistance)
            {
                resamples--;
                p.FloorAt = p.Position;
                if (Sample(p.Position + p.Up * 0.05f, p.Up, out Vector3 point, out Vector3 normal))
                {
                    p.FloorPoint = point;
                    p.FloorNormal = normal;
                }
            }
        }
        if (p.HasFloor)
        {
            Contact(ref p, dt, turn);
        }
        return turn;
    }

    // One contact with the floor plane at the lowest point of the piece's hull: pushed out, then an impulse with bounce
    // and friction (a box's inertia), which tumbles it; asleep once slow for a moment, or after two seconds on the floor.
    private void Contact(ref Piece p, float dt, in Turn m)
    {
        Vector3 n = p.FloorNormal;
        float height = (p.Position - p.FloorPoint).Dot(n);
        if (height > p.Chunk.Radius * p.Scale + 0.02f)
        {
            p.Sleep = 0;
            return;
        }
        // The hull point lowest along the floor's normal (the turn carries the scale).
        Vector3 local = m.ApplyTransposed(n);
        float lowest = float.MaxValue;
        Vector3 deepest = Vector3.Zero;
        Vector3[] support = p.Chunk.Support;
        for (int i = 0; i < support.Length; i++)
        {
            Vector3 s = support[i];
            float d = s.X * local.X + s.Y * local.Y + s.Z * local.Z;
            if (d < lowest)
            {
                lowest = d;
                deepest = s;
            }
        }
        lowest += height;
        if (lowest > 0.002f)
        {
            p.Sleep = 0;
            return;
        }
        Vector3 arm = m.Apply(deepest);
        p.Position += n * (-lowest);
        p.Touching += dt;
        float scale2 = p.Scale * p.Scale;
        Vector3 inv = p.Chunk.InverseInertia / scale2;
        Vector3 vc = p.Velocity + p.Spin.Cross(arm);
        float vn = vc.Dot(n);
        if (vn < 0)
        {
            // The turn's columns are scaled: R^T a = M^T a / s², R b = M b / s... folded into the inertia's units.
            Vector3 rn = arm.Cross(n);
            Vector3 iRn = m.Apply(m.ApplyTransposed(rn) * inv) / scale2;
            float k = 1f + rn.Dot(iRn);
            float bounce = -vn > _bounceStop ? p.Bounce : 0f;
            float j = -(1f + bounce) * vn / k;
            p.Velocity += n * j;
            p.Spin += iRn * j;
            // Friction along the floor, at most μ of the push.
            vc = p.Velocity + p.Spin.Cross(arm);
            Vector3 vt = vc - n * vc.Dot(n);
            float speed = vt.Length();
            if (speed > 1e-5f)
            {
                Vector3 t = vt / speed;
                Vector3 rt = arm.Cross(t);
                Vector3 iRt = m.Apply(m.ApplyTransposed(rt) * inv) / scale2;
                float kt = 1f + rt.Dot(iRt);
                float jt = MathF.Min(speed / kt, p.Friction * j);
                p.Velocity -= t * jt;
                p.Spin -= iRt * jt;
            }
        }
        p.Spin *= _contactSpinDrag;
        if (p.Velocity.LengthSquared() < _sleepSpeed2 && p.Spin.LengthSquared() < _sleepSpin2)
        {
            p.Sleep += dt;
            if (p.Sleep >= _sleepTime)
            {
                Settle(ref p);
            }
        }
        else
        {
            p.Sleep = 0;
        }
        if (p.Touching > 2f && p.Velocity.LengthSquared() < 4 * _sleepSpeed2)
        {
            // Rocking on an edge for long: it lies still now.
            Settle(ref p);
        }
    }

    private static void Settle(ref Piece p)
    {
        p.State = PieceState.Resting;
        p.Timer = 0;
        p.Velocity = Vector3.Zero;
        p.Spin = Vector3.Zero;
    }

    // Every batch whose pieces moved hands its buffer over in one call. How many it draws and its box are told only when
    // they change: the box grows by a margin when a piece leaves it, so a flying cloud of pieces costs one call a frame.
    private void Upload()
    {
        LastUploads = 0;
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            Batch batch = _active[i];
            if (!batch.Dirty)
            {
                continue;
            }
            batch.Dirty = false;
            if (batch.Count == 0)
            {
                RenderingServer.MultimeshSetVisibleInstances(batch.Rid, 0);
                batch.Shown = 0;
                batch.Bounded = false;
                batch.Listed = false;
                _active.RemoveAt(i);
                continue;
            }
            bool grow = !batch.Bounded;
            Aabb box = batch.Bounds;
            for (int s = 0; s < batch.Count; s++)
            {
                ref Piece p = ref _pieces[batch.Owners[s]];
                float r = p.Chunk.Radius * p.Scale;
                var sphere = new Aabb(p.Position - new Vector3(r, r, r), new Vector3(2 * r, 2 * r, 2 * r));
                if (grow || !box.Encloses(sphere))
                {
                    box = grow ? sphere : box.Merge(sphere);
                    grow = false;
                    batch.Bounded = false;
                }
            }
            RenderingServer.MultimeshSetBuffer(batch.Rid, new ReadOnlySpan<float>(batch.Buffer, 0, batch.Capacity * 12));
            LastUploads++;
            if (batch.Count != batch.Shown)
            {
                RenderingServer.MultimeshSetVisibleInstances(batch.Rid, batch.Count);
                batch.Shown = batch.Count;
            }
            if (!batch.Bounded)
            {
                batch.Bounds = box.Grow(BoundsMargin);
                batch.Bounded = true;
                RenderingServer.MultimeshSetCustomAabb(batch.Rid, batch.Bounds);
            }
        }
    }

    private float CameraDistance(Vector3 at)
    {
        Camera3D? camera = CameraOverride ?? GetViewport()?.GetCamera3D();
        return camera == null ? 0f : camera.GlobalPosition.DistanceTo(at);
    }

    /// <summary>
    /// Draws one piece of every set once, tiny, before the camera for a moment: each material's pipeline for drawing many
    /// is built while the world opens, not at the first break.
    /// </summary>
    public void Warm(Camera3D camera, IEnumerable<GibSet> sets)
    {
        if (!camera.IsInsideTree())
        {
            throw new InvalidOperationException("Gib warm-up needs a camera in the scene tree.");
        }
        Node3D holder = CreateWarmup(sets);
        holder.Position = new Vector3(0, 0, -2);
        holder.Scale = Vector3.One * 0.004f;
        camera.AddChild(holder);
        // World can still be awaiting GibDirector's deferred AddChild. The camera already owns this preview.
        camera.GetTree().CreateTimer(1.5).Timeout += () =>
        {
            if (GodotObject.IsInstanceValid(holder) && !holder.IsQueuedForDeletion())
            {
                holder.QueueFree();
            }
        };
    }

    /// <summary>
    /// Creates an unscaled preview of one piece from each set. Its caller owns the node and its lifetime; no live
    /// gib world or scene tree is needed, so the loading stage can own and free all its previews together.
    /// </summary>
    public static Node3D CreateWarmup(IEnumerable<GibSet> sets)
    {
        var holder = new Node3D { Name = "GibWarmup" };
        float x = 0;
        var seen = new HashSet<Mesh>();
        foreach (GibSet set in sets)
        {
            GibChunk chunk = set.Chunks[0];
            if (!seen.Add(chunk.Mesh))
            {
                continue;
            }
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = chunk.Mesh, InstanceCount = 1 };
            mm.SetInstanceTransform(0, Transform3D.Identity);
            holder.AddChild(new MultiMeshInstance3D { Multimesh = mm, Position = new Vector3(x += 1.5f, 0, 0) });
        }
        return holder;
    }
}
