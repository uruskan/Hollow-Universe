using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Simulation.Physics;
using JoltPhysicsSharp;

namespace Hollow.Simulation.Building;

/// <summary>What a ray hit: the body, how far along, where and the surface's normal there.</summary>
public readonly record struct RayHit(uint Body, float Distance, Vector3 Point, Vector3 Normal)
{
    public bool IsGround => PhysicsWorld.IsGroundBody(Body);
}

/// <summary>A body a shape overlaps, and how deep.</summary>
public readonly record struct OverlapHit(uint Body, float Depth);

/// <summary>
/// The building's side of a zone's Jolt world (GROUND.md §6.3): one static body per piece (a convex hull, or a compound
/// of hulls), per door leaf and per claim device, under body IDs from <see cref="PhysicsWorld.FirstBuildingBody"/>;
/// and the queries placement asks: overlaps, rays and the ground under a point. Shapes are built once per piece kind
/// and shared. Every call holds <see cref="JoltRuntime.Gate"/>.
/// </summary>
public sealed class BuildingPhysics : IDisposable
{
    private const float ConvexRadius = 0.01f;

    private readonly PhysicsWorld _world;
    private readonly Dictionary<object, Shape> _shapes = new();
    private readonly Dictionary<uint, object> _owners = new();
    private readonly Stack<uint> _free = new();
    private readonly List<CollideShapeResult> _collide = new();
    private readonly OnlyGround _onlyGround = new();
    private readonly OnlyBuilding _onlyBuilding = new();
    private readonly NotGround _notGround = new();
    private uint _next = PhysicsWorld.FirstBuildingBody;
    private bool _disposed;

    public BuildingPhysics(PhysicsWorld world)
    {
        _world = world;
    }

    public PhysicsWorld World => _world;

    /// <summary>A mirror's bodies all carry the server's IDs: without one, a body isn't made.</summary>
    public bool OnlyRequested { get; set; }

    /// <summary>Building bodies in the world.</summary>
    public int BodyCount => _owners.Count;

    /// <summary>The piece, door or claim device a building body belongs to, or null for any other body.</summary>
    public object? OwnerOf(uint body) => _owners.TryGetValue(body, out object? owner) ? owner : null;

    /// <summary>The shared Jolt shape of some convex parts, built the first time it's asked for under that key.</summary>
    public Shape ShapeFor(object key, ConvexPart[] parts)
    {
        if (_shapes.TryGetValue(key, out Shape? shape))
        {
            return shape;
        }
        lock (JoltRuntime.Gate)
        {
            if (parts.Length == 1)
            {
                using var hull = new ConvexHullShapeSettings(parts[0].Points, ConvexRadius).Owned();
                shape = hull.Create();
            }
            else
            {
                using var compound = new StaticCompoundShapeSettings().Owned();
                var hulls = new List<ConvexHullShapeSettings>();
                foreach (ConvexPart part in parts)
                {
                    var hull = new ConvexHullShapeSettings(part.Points, ConvexRadius).Owned();
                    hulls.Add(hull);
                    compound.AddShape(Vector3.Zero, Quaternion.Identity, hull, 0);
                }
                shape = compound.Create();
                foreach (ConvexHullShapeSettings hull in hulls)
                {
                    hull.Dispose();
                }
            }
        }
        _shapes.Add(key, shape);
        return shape;
    }

    /// <summary>
    /// Adds a static body for an owner; returns its ID, or 0 when the building range is full. A client's mirror asks
    /// for the server's ID (<paramref name="requested"/>), so its prediction meets the same contacts in the same order.
    /// </summary>
    public uint Add(object owner, Shape shape, Vector3 position, Quaternion rotation, uint requested = 0)
    {
        uint id;
        if (requested != 0)
        {
            if (requested < PhysicsWorld.FirstBuildingBody || requested >= PhysicsWorld.FirstBuildingBody + PhysicsWorld.MaxBuildingBodies)
            {
                return 0;
            }
            if (_owners.ContainsKey(requested))
            {
                Remove(requested);
            }
            id = requested;
        }
        else if (OnlyRequested)
        {
            return 0;
        }
        else if (_free.Count > 0)
        {
            id = _free.Pop();
        }
        else if (_next < PhysicsWorld.FirstBuildingBody + PhysicsWorld.MaxBuildingBodies)
        {
            id = _next++;
        }
        else
        {
            return 0;
        }
        _world.AddStaticBody(id, shape, position, rotation);
        _owners.Add(id, owner);
        return id;
    }

    public void Move(uint id, Vector3 position, Quaternion rotation)
    {
        if (id == 0 || !_owners.ContainsKey(id))
        {
            return;
        }
        lock (JoltRuntime.Gate)
        {
            _world.Jolt.BodyInterface.SetPositionAndRotation(new BodyID(id), position, rotation, Activation.DontActivate);
            _world.NoteBodyChanged();
        }
    }

    /// <summary>Removes a body, but only while it still belongs to <paramref name="owner"/> (a mirror may have given its ID to a newer piece).</summary>
    public void Remove(uint id, object owner)
    {
        if (id != 0 && _owners.TryGetValue(id, out object? current) && Equals(current, owner))
        {
            Remove(id);
        }
    }

    public void Remove(uint id)
    {
        if (id == 0 || !_owners.Remove(id))
        {
            return;
        }
        _world.RemoveStaticBody(id);
        _free.Push(id);
    }

    /// <summary>
    /// Every body a shape at a pose overlaps by more than <paramref name="tolerance"/> metres; without the ground tiles
    /// when <paramref name="ground"/> is false (a foundation sits in the ground: colliding a box with the tile's mesh
    /// under it took 3–90 ms the first time a spot was asked about, S3b's load test on beaches).
    /// </summary>
    public void Overlaps(Shape shape, Vector3 position, Quaternion rotation, float tolerance, List<OverlapHit> into, bool ground = true)
    {
        into.Clear();
        // JoltPhysicsSharp reads the matrix column-major (Jolt's own layout): the transpose of System.Numerics'.
        Matrix4x4 com = Matrix4x4.Transpose(Matrix4x4.CreateTranslation(shape.CenterOfMass) * Matrix4x4.CreateFromQuaternion(rotation) *
            Matrix4x4.CreateTranslation(position));
        lock (JoltRuntime.Gate)
        {
            _collide.Clear();
            Vector3 scale = Vector3.One, offset = Vector3.Zero;
            // Back faces too: a slab whose middle is below the ground still cuts into it.
            var settings = new CollideShapeSettings { BackFaceMode = BackFaceMode.CollideWithBackFaces };
            _world.Jolt.NarrowPhaseQuery.CollideShape(shape, scale, com, settings, offset, CollisionCollectorType.AllHit, _collide,
                null, null, ground ? null : _notGround, null);
        }
        foreach (CollideShapeResult hit in _collide)
        {
            if (hit.PenetrationDepth <= tolerance)
            {
                continue;
            }
            uint id = hit.BodyID2.ID;
            bool known = false;
            for (int i = 0; i < into.Count; i++)
            {
                if (into[i].Body == id)
                {
                    known = true;
                    if (hit.PenetrationDepth > into[i].Depth)
                    {
                        into[i] = new OverlapHit(id, hit.PenetrationDepth);
                    }
                }
            }
            if (!known)
            {
                into.Add(new OverlapHit(id, hit.PenetrationDepth));
            }
        }
    }

    /// <summary>The nearest body along a ray, within <paramref name="length"/> metres; ground only, or building only, when asked.</summary>
    public bool Ray(Vector3 origin, Vector3 direction, float length, out RayHit hit, bool groundOnly = false, bool buildingOnly = false)
    {
        hit = default;
        var ray = new Ray(origin, Vector3.Normalize(direction) * length);
        lock (JoltRuntime.Gate)
        {
            BodyFilter? filter = groundOnly ? _onlyGround : buildingOnly ? _onlyBuilding : null;
            if (!_world.Jolt.NarrowPhaseQuery.CastRay(ray, out RayCastResult result, null, null, filter))
            {
                return false;
            }
            Vector3 point = ray.GetPointOnRay(result.Fraction);
            Vector3 normal = -Vector3.Normalize(direction);
            BodyLockInterface locks = _world.Jolt.BodyLockInterfaceNoLock;
            locks.LockRead(result.BodyID, out BodyLockRead read);
            if (read.Succeeded && read.Body is Body body)
            {
                normal = body.GetWorldSpaceSurfaceNormal((SubShapeID)result.subShapeID2, point);
            }
            locks.UnlockRead(read);
            hit = new RayHit(result.BodyID.ID, result.Fraction * length, point, normal);
            return true;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        lock (JoltRuntime.Gate)
        {
            foreach (uint id in _owners.Keys)
            {
                _world.RemoveStaticBody(id);
            }
            _owners.Clear();
            foreach (Shape shape in _shapes.Values)
            {
                shape.Dispose();
            }
            _shapes.Clear();
            _onlyGround.Dispose();
            _onlyBuilding.Dispose();
            _notGround.Dispose();
        }
    }

    private sealed class OnlyGround : BodyFilter
    {
        protected override bool ShouldCollide(BodyID bodyID) => PhysicsWorld.IsGroundBody(bodyID.ID);

        protected override bool ShouldCollideLocked(Body body) => body != null && PhysicsWorld.IsGroundBody(body.ID.ID);
    }

    private sealed class NotGround : BodyFilter
    {
        protected override bool ShouldCollide(BodyID bodyID) => !PhysicsWorld.IsGroundBody(bodyID.ID);

        protected override bool ShouldCollideLocked(Body body) => body != null && !PhysicsWorld.IsGroundBody(body.ID.ID);
    }

    private sealed class OnlyBuilding : BodyFilter
    {
        private static bool IsBuilding(uint id) => id >= PhysicsWorld.FirstBuildingBody && id < PhysicsWorld.FirstBuildingBody + PhysicsWorld.MaxBuildingBodies;

        protected override bool ShouldCollide(BodyID bodyID) => IsBuilding(bodyID.ID);

        protected override bool ShouldCollideLocked(Body body) => body != null && IsBuilding(body.ID.ID);
    }
}
