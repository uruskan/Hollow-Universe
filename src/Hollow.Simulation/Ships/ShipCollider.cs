using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using Hollow.Simulation.Physics;
using JoltPhysicsSharp;

namespace Hollow.Simulation.Ships;

/// <summary>Where a ray from a ship met the world: how far, the point and the surface's normal there.</summary>
public readonly record struct ShipRayHit(uint Body, float Distance, Vector3 Point, Vector3 Normal);

/// <summary>A contact of the hull with the world: the point, the normal pointing out of the world, how deep, and the body.</summary>
public readonly record struct ShipContact(Vector3 Point, Vector3 Normal, float Depth, uint Body = 0);

/// <summary>
/// A ship's side of a zone's Jolt world (GROUND.md §6.6): the queries the flight model asks (rays for the skids, the
/// hull swept along a step and its overlaps) against everything static (ground tiles, trees, outcrops, deployables,
/// building pieces, parked ships), and the static bodies of parked ships, under body IDs from
/// <see cref="PhysicsWorld.FirstShipBody"/>, so characters walk into them. Hull shapes are built once per kind and shared
/// by every world. Callers hold <see cref="JoltRuntime.Gate"/>.
/// </summary>
public sealed class ShipCollider : IDisposable
{
    private const float ConvexRadius = 0.02f;

    /// <summary>"Ignore no body" for the queries.</summary>
    public const uint NoBody = uint.MaxValue;

    private static readonly Dictionary<(ShipDef Def, bool Swept), Shape> Shapes = new();
    private static readonly Lock ShapesLock = new();

    private readonly PhysicsWorld _world;
    private readonly ExceptBody _filter = new();
    private readonly List<ShapeCastResult> _casts = new();
    private readonly List<CollideShapeResult> _collides = new();
    private readonly HashSet<uint> _bodies = new();
    private readonly Stack<uint> _free = new();
    private uint _next = PhysicsWorld.FirstShipBody;
    private bool _disposed;

    public ShipCollider(PhysicsWorld world)
    {
        _world = world;
    }

    public PhysicsWorld World => _world;

    /// <summary>Parked ships' bodies in the world.</summary>
    public int BodyCount => _bodies.Count;

    /// <summary>Whether a body is a parked ship's.</summary>
    public static bool IsShipBody(uint id) => id >= PhysicsWorld.FirstShipBody && id < PhysicsWorld.FirstShipBody + PhysicsWorld.MaxShipBodies;

    /// <summary>
    /// The hull as one compound shape with its origin at the centre of mass. <paramref name="swept"/>: the shape the step
    /// sweeps, whose skids stop the skid travel short of the pads, so the springs take a landing and the hull a crash.
    /// </summary>
    public static Shape HullShape(ShipDef def, bool swept)
    {
        lock (ShapesLock)
        {
            if (Shapes.TryGetValue((def, swept), out Shape? shape))
            {
                return shape;
            }
            lock (JoltRuntime.Gate)
            {
                using var compound = new StaticCompoundShapeSettings().Owned();
                var boxes = new List<BoxShapeSettings>();
                foreach (HullBox box in def.Hull)
                {
                    if (box.Fragile && !swept)
                    {
                        // A parked rotor's blades are two thin sticks, not its disc (docs/MINICOPTER.md §5.5).
                        continue;
                    }
                    Vector3 centre = box.Centre, half = box.HalfExtents;
                    if (swept && box.Skid)
                    {
                        // Raise the skid's bottom to the travel's end.
                        float bottom = MathF.Max(centre.Y - half.Y, def.SkidTravel);
                        float top = MathF.Max(centre.Y + half.Y, bottom + 0.04f);
                        centre.Y = (bottom + top) / 2;
                        half.Y = (top - bottom) / 2;
                    }
                    var settings = new BoxShapeSettings(Vector3.Max(half, new Vector3(ConvexRadius * 1.5f)), ConvexRadius).Owned();
                    boxes.Add(settings);
                    compound.AddShape(centre - def.CentreOfMass, Quaternion.Identity, settings, 0);
                }
                shape = compound.Create();
                foreach (BoxShapeSettings box in boxes)
                {
                    box.Dispose();
                }
            }
            Shapes.Add((def, swept), shape);
            return shape;
        }
    }

    // JoltPhysicsSharp reads matrices column-major (Jolt's own layout): the transpose of System.Numerics'. A shape whose
    // origin (the ship's centre of mass) is at a pose; Jolt wants the transform of the shape's own centre of mass.
    private static Matrix4x4 ComTransform(Shape shape, Vector3 position, Quaternion rotation) =>
        Matrix4x4.Transpose(Matrix4x4.CreateTranslation(shape.CenterOfMass) * Matrix4x4.CreateFromQuaternion(rotation) *
            Matrix4x4.CreateTranslation(position));

    /// <summary>The nearest static body along a ray within <paramref name="length"/> (not the body <paramref name="ignore"/>).</summary>
    public bool Ray(Vector3 origin, Vector3 direction, float length, uint ignore, out ShipRayHit hit)
    {
        hit = default;
        var ray = new Ray(origin, direction * length);
        _filter.Ignore = ignore;
        if (!_world.Jolt.NarrowPhaseQuery.CastRay(ray, out RayCastResult result, null, null, _filter))
        {
            return false;
        }
        Vector3 point = ray.GetPointOnRay(result.Fraction);
        Vector3 normal = -direction;
        BodyLockInterface locks = _world.Jolt.BodyLockInterfaceNoLock;
        locks.LockRead(result.BodyID, out BodyLockRead read);
        if (read.Succeeded && read.Body is Body body)
        {
            normal = body.GetWorldSpaceSurfaceNormal((SubShapeID)result.subShapeID2, point);
        }
        locks.UnlockRead(read);
        if (Vector3.Dot(normal, direction) > 0)
        {
            normal = -normal; // a back face: the normal faces the ray all the same
        }
        hit = new ShipRayHit(result.BodyID.ID, result.Fraction * length, point, normal);
        return true;
    }

    /// <summary>
    /// Sweeps the hull (the swept shape) from a pose along <paramref name="displacement"/>: the first static body met, as
    /// the fraction of the way and the contact (normal out of the world). False when the way is clear.
    /// </summary>
    public bool Cast(ShipDef def, Vector3 position, Quaternion rotation, Vector3 displacement, uint ignore, out float fraction, out ShipContact contact)
    {
        fraction = 1;
        contact = default;
        Shape shape = HullShape(def, swept: true);
        Matrix4x4 com = ComTransform(shape, position, rotation);
        Vector3 offset = Vector3.Zero;
        var settings = new ShapeCastSettings
        {
            BackFaceModeTriangles = BackFaceMode.IgnoreBackFaces,
            BackFaceModeConvex = BackFaceMode.IgnoreBackFaces,
            UseShrunkenShapeAndConvexRadius = true,
            ReturnDeepestPoint = true,
            CollisionTolerance = 1e-4f,
            PenetrationTolerance = 1e-4f,
        };
        _filter.Ignore = ignore;
        _casts.Clear();
        _world.Jolt.NarrowPhaseQuery.CastShape(shape, com, displacement, settings, offset, CollisionCollectorType.ClosestHit, _casts, null, null, _filter, null);
        if (_casts.Count == 0)
        {
            return false;
        }
        ShapeCastResult best = _casts[0];
        for (int i = 1; i < _casts.Count; i++)
        {
            if (_casts[i].Fraction < best.Fraction)
            {
                best = _casts[i];
            }
        }
        Vector3 axis = best.PenetrationAxis;
        float length = axis.Length();
        Vector3 normal = length > 1e-9f ? -axis / length : -Vector3.Normalize(displacement);
        fraction = Math.Clamp(best.Fraction, 0, 1);
        contact = new ShipContact(best.ContactPointOn2, normal, best.PenetrationDepth, best.BodyID2.ID);
        return true;
    }

    /// <summary>
    /// Sweeps a tyre (VEHICLES.md §6.2: a cylinder of its radius and width, rounded at its edges, its axle along the
    /// rotation's +X) from its hub along <paramref name="displacement"/>: of the static bodies it meets, the first whose
    /// surface faces up the body's way at least <paramref name="leastUp"/> (a kerb's edge, a rock's shoulder; not a
    /// trunk's side, which the hull meets), as the fraction of the way and the contact, with the depth when it starts
    /// inside. A tyre rolls up an edge in its way, where a ray from its hub would pass it by and the hull would hit it.
    /// </summary>
    public bool CastTyre(float radius, float width, Vector3 hub, Quaternion rotation, Vector3 displacement, float leastUp, uint ignore,
        out float fraction, out ShipContact contact)
    {
        fraction = 1;
        contact = default;
        Shape shape = TyreShape(radius, width);
        // The cylinder's own axis is its +Y: turned onto the axle.
        Quaternion pose = Quaternion.Concatenate(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2), rotation);
        Matrix4x4 com = ComTransform(shape, hub, pose);
        var settings = new ShapeCastSettings
        {
            BackFaceModeTriangles = BackFaceMode.IgnoreBackFaces,
            BackFaceModeConvex = BackFaceMode.IgnoreBackFaces,
            UseShrunkenShapeAndConvexRadius = true,
            ReturnDeepestPoint = true,
            CollisionTolerance = 1e-4f,
            PenetrationTolerance = 1e-4f,
        };
        _filter.Ignore = ignore;
        _casts.Clear();
        _world.Jolt.NarrowPhaseQuery.CastShape(shape, com, displacement, settings, Vector3.Zero, CollisionCollectorType.AllHit, _casts, null, null, _filter, null);
        Vector3 up = displacement.LengthSquared() > 1e-12f ? -Vector3.Normalize(displacement) : Vector3.UnitY;
        bool found = false;
        foreach (ShapeCastResult r in _casts)
        {
            Vector3 axis = r.PenetrationAxis;
            float length = axis.Length();
            if (length < 1e-9f)
            {
                continue;
            }
            Vector3 normal = -axis / length;
            if (Vector3.Dot(normal, up) < leastUp)
            {
                continue;
            }
            // The first along the way; at the start, the deepest; ties by body and place, as the server and a prediction agree.
            float f = Math.Clamp(r.Fraction, 0, 1);
            bool better = !found || f < fraction - 1e-6f ||
                (MathF.Abs(f - fraction) <= 1e-6f && (r.PenetrationDepth > contact.Depth + 1e-6f ||
                    (MathF.Abs(r.PenetrationDepth - contact.Depth) <= 1e-6f && (r.BodyID2.ID < contact.Body ||
                        (r.BodyID2.ID == contact.Body && r.ContactPointOn2.X < contact.Point.X)))));
            if (better)
            {
                found = true;
                fraction = f;
                contact = new ShipContact(r.ContactPointOn2, normal, r.PenetrationDepth, r.BodyID2.ID);
            }
        }
        return found;
    }

    private static readonly Dictionary<(float Radius, float Width), Shape> Tyres = new();

    // A tyre's cylinder, made once for each size and shared by every world.
    private static Shape TyreShape(float radius, float width)
    {
        lock (ShapesLock)
        {
            if (Tyres.TryGetValue((radius, width), out Shape? shape))
            {
                return shape;
            }
            lock (JoltRuntime.Gate)
            {
                float half = MathF.Max(width / 2, 0.02f);
                // The tread's shoulders rounded by a few centimetres, as a knobbed tyre's are.
                float round = MathF.Min(0.04f, MathF.Min(half, radius) * 0.8f);
                using var settings = new CylinderShapeSettings(half, radius, round).Owned();
                shape = settings.Create();
            }
            Tyres.Add((radius, width), shape);
            return shape;
        }
    }

    /// <summary>Where the hull (the full shape) overlaps static bodies by more than a millimetre at a pose: up to <paramref name="into"/>'s length.</summary>
    public int Overlaps(ShipDef def, Vector3 position, Quaternion rotation, uint ignore, Span<ShipContact> into)
    {
        Shape shape = HullShape(def, swept: true);
        Matrix4x4 com = ComTransform(shape, position, rotation);
        Vector3 scale = Vector3.One, offset = Vector3.Zero;
        var settings = new CollideShapeSettings { BackFaceMode = BackFaceMode.CollideWithBackFaces, MaxSeparationDistance = 0 };
        _filter.Ignore = ignore;
        _collides.Clear();
        _world.Jolt.NarrowPhaseQuery.CollideShape(shape, scale, com, settings, offset, CollisionCollectorType.AllHit, _collides, null, null, _filter, null);
        int count = 0;
        // The deepest first, so a full list keeps what matters; ties in a fixed order (by body, then place), so the
        // server and a pilot's prediction resolve them alike whatever order Jolt met them in.
        _collides.Sort(static (a, b) =>
        {
            int c = b.PenetrationDepth.CompareTo(a.PenetrationDepth);
            if (c != 0)
            {
                return c;
            }
            c = a.BodyID2.ID.CompareTo(b.BodyID2.ID);
            if (c != 0)
            {
                return c;
            }
            c = a.ContactPointOn2.X.CompareTo(b.ContactPointOn2.X);
            return c != 0 ? c : a.ContactPointOn2.Z.CompareTo(b.ContactPointOn2.Z) is var z && z != 0 ? z : a.ContactPointOn2.Y.CompareTo(b.ContactPointOn2.Y);
        });
        foreach (CollideShapeResult c in _collides)
        {
            if (count >= into.Length)
            {
                break;
            }
            if (c.PenetrationDepth <= 1e-3f)
            {
                continue;
            }
            Vector3 axis = c.PenetrationAxis;
            float length = axis.Length();
            if (length < 1e-9f)
            {
                continue;
            }
            into[count++] = new ShipContact(c.ContactPointOn2, -axis / length, c.PenetrationDepth, c.BodyID2.ID);
        }
        return count;
    }

    // ---------------------------------------------------------------- parked ships

    /// <summary>Adds a parked ship's static body at its pose (centre of mass and rotation); returns its ID, or 0 when the range is full.</summary>
    public uint AddParked(ShipDef def, Vector3 position, Quaternion rotation) => AddParked(HullShape(def, swept: false), position, rotation);

    /// <summary>
    /// Adds a parked ship's static body of a shape whose origin is the ship's centre of mass (a ship people walk in: every
    /// piece, IRON_WAKE.md §4.6); returns its ID, or 0 when the range is full.
    /// </summary>
    public uint AddParked(Shape shape, Vector3 position, Quaternion rotation)
    {
        uint id;
        if (_free.Count > 0)
        {
            id = _free.Pop();
        }
        else if (_next < PhysicsWorld.FirstShipBody + PhysicsWorld.MaxShipBodies)
        {
            id = _next++;
        }
        else
        {
            return 0;
        }
        _world.AddStaticBody(id, shape, position, rotation);
        _bodies.Add(id);
        return id;
    }

    /// <summary>
    /// Adds a parked ship's static body with the ID the server gave its own (a client's copy, IRON_WAKE.md §4.6: the same
    /// contacts in the same order); false when the ID is outside the ships' range or taken.
    /// </summary>
    public bool AddParkedAt(uint id, Shape shape, Vector3 position, Quaternion rotation)
    {
        if (!IsShipBody(id) || _bodies.Contains(id))
        {
            return false;
        }
        _world.AddStaticBody(id, shape, position, rotation);
        _bodies.Add(id);
        return true;
    }

    /// <summary>Whether a parked ship's body has this ID now.</summary>
    public bool HasParked(uint id) => _bodies.Contains(id);

    /// <summary>
    /// Moves a ship's body to a pose (centre of mass and rotation): a vehicle on wheels keeps its body while it moves
    /// (VEHICLES.md §6.4), as a door's leaf does (DOORS.md §5). Characters meet it where it is now; their next step's
    /// penetration recovery takes them out of where it came to.
    /// </summary>
    public void MoveParked(uint id, Vector3 position, Quaternion rotation)
    {
        if (id == 0 || !_bodies.Contains(id) || _disposed)
        {
            return;
        }
        _world.Jolt.BodyInterface.SetPositionAndRotation(new BodyID(id), position, rotation, Activation.DontActivate);
        _world.NoteBodyChanged();
    }

    public void RemoveParked(uint id)
    {
        if (id == 0 || !_bodies.Remove(id) || _disposed)
        {
            return;
        }
        _world.RemoveStaticBody(id);
        _free.Push(id);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        lock (JoltRuntime.Gate)
        {
            foreach (uint id in _bodies)
            {
                _world.RemoveStaticBody(id);
            }
            _bodies.Clear();
            _filter.Dispose();
        }
        _disposed = true;
    }

    private sealed class ExceptBody : BodyFilter
    {
        public uint Ignore;

        protected override bool ShouldCollide(BodyID bodyID) => bodyID.ID != Ignore;

        protected override bool ShouldCollideLocked(Body body) => body != null && body.ID.ID != Ignore;
    }
}
