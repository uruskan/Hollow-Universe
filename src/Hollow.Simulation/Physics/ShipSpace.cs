using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using Hollow.Core;
using Hollow.Simulation.Building;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Ships;
using JoltPhysicsSharp;

namespace Hollow.Simulation.Physics;

/// <summary>What a query through a ship's space met: the part (0 the hull), how far along, where and the surface's normal, in the ship's frame.</summary>
public readonly record struct SpaceHit(int Part, float Distance, Vector3 Point, Vector3 Normal);

/// <summary>
/// A ship's own small physics (IRON_WAKE.md §4.3), separate from the zone's, in the ship's frame: the hull's static
/// pieces as one compound body (its shape built once per kind and shared), and each moving part with pieces (the ramp's
/// leaves, the airlock's door, a turret's platform) as a body of its own, posed from the ship's parts' states through
/// <see cref="Pose"/>, the one entry point. Characters aboard step in it through its one <see cref="Body"/>, in turn;
/// the queries through the ship's frame (aim, reach, shots, blasts: <c>ShipQueries</c>) meet its pieces. The server keeps
/// one for every ship with an interior, a client one for each such ship near the player. Callers hold
/// <see cref="JoltRuntime.Gate"/> for the queries; making, posing and disposing take it themselves.
/// </summary>
public sealed class ShipSpace : IDisposable
{
    private const float ConvexRadius = 0.01f;

    /// <summary>The hull's body; a part's body is its index in the interior's parts.</summary>
    public const uint HullBody = 0;

    private static readonly Dictionary<ShipInterior, KindShapes> Kinds = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<(ShipInterior, ulong), Shape> ParkedShapes = new();
    private static readonly Lock KindsLock = new();

    private readonly ObjectLayerPairFilterTable _layerPairs;
    private readonly BroadPhaseLayerInterfaceTable _broadPhaseLayers;
    private readonly ObjectVsBroadPhaseLayerFilterTable _objectVsBroadPhase;
    private readonly KindShapes _shapes;
    private readonly PartPose[] _poses;
    private readonly float[] _states;
    private readonly List<CollideShapeResult> _collides = new();
    private readonly Dictionary<ulong, uint> _thingBodies = new();
    private readonly Dictionary<uint, ulong> _thingOfBody = new();
    private readonly Stack<uint> _freeThingBodies = new();
    private readonly PiecesOnly _piecesOnly;
    private uint _nextThingBody;
    private CharacterBody? _body;
    private bool _dirty = true;
    private bool _disposed;

    public ShipSpace(ShipInterior interior, ReadOnlySpan<float> states, MovementTuning tuning)
    {
        Interior = interior;
        Tuning = tuning;
        _shapes = ShapesOf(interior);
        _poses = new PartPose[interior.Parts.Count];
        _states = new float[interior.Parts.Count];
        states[..Math.Min(states.Length, _states.Length)].CopyTo(_states);
        JoltRuntime.EnsureInitialised();
        lock (JoltRuntime.Gate)
        {
            _layerPairs = new ObjectLayerPairFilterTable(2);
            _layerPairs.EnableCollision(PhysicsWorld.StaticLayer, PhysicsWorld.MovingLayer);
            _layerPairs.EnableCollision(PhysicsWorld.MovingLayer, PhysicsWorld.MovingLayer);
            _broadPhaseLayers = new BroadPhaseLayerInterfaceTable(2, 2);
            _broadPhaseLayers.MapObjectToBroadPhaseLayer(PhysicsWorld.StaticLayer, new BroadPhaseLayer(0));
            _broadPhaseLayers.MapObjectToBroadPhaseLayer(PhysicsWorld.MovingLayer, new BroadPhaseLayer(1));
            _objectVsBroadPhase = new ObjectVsBroadPhaseLayerFilterTable(_broadPhaseLayers, 2, _layerPairs, 2);
            FirstThingBody = (uint)interior.Parts.Count + 1;
            _nextThingBody = FirstThingBody;
            _piecesOnly = new PiecesOnly(FirstThingBody);
            Jolt = new PhysicsSystem(new PhysicsSystemSettings
            {
                // The hull and a body per part, then the things aboard (IW5) after them.
                MaxBodies = interior.Parts.Count + 1 + MostThings,
                MaxBodyPairs = 64,
                MaxContactConstraints = 64,
                NumBodyMutexes = 0,
                ObjectLayerPairFilter = _layerPairs,
                BroadPhaseLayerInterface = _broadPhaseLayers,
                ObjectVsBroadPhaseLayerFilter = _objectVsBroadPhase,
            });
            BodyInterface bodies = Jolt.BodyInterface;
            Add(bodies, HullBody, _shapes.Hull, PartPose.Rest);
            for (int part = 1; part < _shapes.Parts.Length; part++)
            {
                if (_shapes.Parts[part] is { } shape)
                {
                    _poses[part] = interior.PartPose(part, _states);
                    Add(bodies, (uint)part, shape, _poses[part]);
                }
            }
            OptimizeBroadPhase();
        }
    }

    public ShipInterior Interior { get; }

    public MovementTuning Tuning { get; }

    /// <summary>The most things aboard one space has bodies for (IRON_WAKE.md §8: 80 deployables a ship, with room to spare).</summary>
    public const int MostThings = 128;

    /// <summary>The first body of the things aboard: the hull's and the parts' come before.</summary>
    public uint FirstThingBody { get; }

    /// <summary>How many things aboard have a body here.</summary>
    public int ThingBodies => _thingBodies.Count;

    /// <summary>Whether a body is a thing's aboard (not a piece of the ship).</summary>
    public bool IsThing(uint body) => body >= FirstThingBody;

    /// <summary>The thing a body is (its key, as <see cref="AddThing"/> was given it), or 0.</summary>
    public ulong ThingOf(uint body) => _thingOfBody.GetValueOrDefault(body);

    /// <summary>
    /// A solid thing aboard (IRON_WAKE.md §8: a placed box, furnace or workbench) as a box of this space, standing on its
    /// base at a point of the ship's frame and turned by <paramref name="rotation"/>: people aboard walk into it, and the
    /// queries that meet things aboard (a grenade's flight, placement's room) meet it. Replaces the key's box if it has one;
    /// false when the space has no room for another.
    /// </summary>
    public bool AddThing(ulong key, Vector3 half, Vector3 basePoint, Quaternion rotation)
    {
        RemoveThing(key);
        uint id;
        if (_freeThingBodies.Count > 0)
        {
            id = _freeThingBodies.Pop();
        }
        else if (_nextThingBody < FirstThingBody + MostThings)
        {
            id = _nextThingBody++;
        }
        else
        {
            return false;
        }
        lock (JoltRuntime.Gate)
        {
            Vector3 centre = basePoint + Vector3.Transform(new Vector3(0, half.Y, 0), rotation);
            Add(Jolt.BodyInterface, id, BoxShape(half), new PartPose(rotation, centre));
            _dirty = true;
            OptimizeBroadPhase();
        }
        _thingBodies[key] = id;
        _thingOfBody[id] = key;
        return true;
    }

    /// <summary>A thing aboard gone (taken, broken, gone with its link): its box goes.</summary>
    public void RemoveThing(ulong key)
    {
        if (!_thingBodies.Remove(key, out uint id))
        {
            return;
        }
        _thingOfBody.Remove(id);
        lock (JoltRuntime.Gate)
        {
            var bodyId = new BodyID(id);
            Jolt.BodyInterface.RemoveBody(bodyId);
            Jolt.BodyInterface.DestroyBody(bodyId);
            _dirty = true;
            OptimizeBroadPhase();
        }
        _freeThingBodies.Push(id);
    }

    /// <summary>
    /// Whether a box of the ship's frame (standing on its base, turned by <paramref name="rotation"/>) meets any body here,
    /// a piece or a thing aboard, by more than <paramref name="tolerance"/>: placement's room aboard. Under the gate.
    /// </summary>
    public bool BoxMeets(Vector3 half, Vector3 basePoint, Quaternion rotation, float tolerance)
    {
        Shape shape = BoxShape(half);
        Vector3 centre = basePoint + Vector3.Transform(new Vector3(0, half.Y, 0), rotation);
        Matrix4x4 at = Matrix4x4.Transpose(Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(centre));
        Vector3 scale = Vector3.One, offset = Vector3.Zero;
        var settings = new CollideShapeSettings { BackFaceMode = BackFaceMode.CollideWithBackFaces, MaxSeparationDistance = 0 };
        _collides.Clear();
        Jolt.NarrowPhaseQuery.CollideShape(shape, scale, at, settings, offset, CollisionCollectorType.AllHit, _collides, null, null, null, null);
        foreach (CollideShapeResult r in _collides)
        {
            if (r.PenetrationDepth > tolerance)
            {
                return true;
            }
        }
        return false;
    }

    internal PhysicsSystem Jolt { get; }

    /// <summary>The parts' states the bodies are posed at.</summary>
    public ReadOnlySpan<float> States => _states;

    /// <summary>Pieces whose convex hull Jolt refused (none for good data; the content test checks).</summary>
    public int FailedPieces => _shapes.Failed;

    /// <summary>The one body the characters aboard step through, in turn (made the first time it is asked for).</summary>
    public CharacterBody Body => _body ??= new CharacterBody(this, Tuning, Vector3.Zero, Vector3.UnitY);

    private static void Add(BodyInterface bodies, uint id, Shape shape, in PartPose pose)
    {
        var bodyId = new BodyID(id);
        using var creation = new BodyCreationSettings(shape, pose.Translation, pose.Rotation, MotionType.Static, PhysicsWorld.StaticLayer);
        _ = bodies.CreateBodyWithID(bodyId, creation) ?? throw new InvalidOperationException($"Jolt refused a ship space's body {id}.");
        bodies.AddBody(bodyId, Activation.DontActivate);
    }

    /// <summary>
    /// Poses the moving parts at their states (the one entry point, IRON_WAKE.md §4.3): a part whose motion changed (its
    /// own, or a carrier's) is moved, and the broadphase is brought up to date at once (parts move seldom). Whether a part
    /// may move into a character is the ship's rules' to say first (<see cref="Sweeps"/>).
    /// </summary>
    public void Pose(ReadOnlySpan<float> states)
    {
        bool changed = false;
        for (int i = 0; i < _states.Length && i < states.Length; i++)
        {
            changed |= _states[i] != states[i];
        }
        if (!changed)
        {
            return;
        }
        states[..Math.Min(states.Length, _states.Length)].CopyTo(_states);
        lock (JoltRuntime.Gate)
        {
            BodyInterface bodies = Jolt.BodyInterface;
            for (int part = 1; part < _shapes.Parts.Length; part++)
            {
                if (_shapes.Parts[part] == null)
                {
                    continue;
                }
                PartPose pose = Interior.PartPose(part, _states);
                if (pose == _poses[part])
                {
                    continue;
                }
                _poses[part] = pose;
                var id = new BodyID((uint)part);
                bodies.SetPositionAndRotation(id, pose.Translation, pose.Rotation, Activation.DontActivate);
                _dirty = true;
            }
            OptimizeBroadPhase();
        }
    }

    /// <summary>A hinge's or a turret's travel is tried every this many radians, a slide's every <see cref="SweepMetres"/> (§4.7).</summary>
    public const float SweepRadians = 0.02f;

    public const float SweepMetres = 0.05f;

    /// <summary>
    /// Whether moving the parts from the states the bodies are posed at to <paramref name="to"/> would carry a piece into a
    /// character (IRON_WAKE.md §4.7, the building doors' rule): every part whose pose changes on the way (a leaf its main
    /// leaf carries too) is tried at poses <see cref="SweepRadians"/> or <see cref="SweepMetres"/> apart against each
    /// character's capsule (feet, up and height in the ship's frame), then put back. Under the gate.
    /// </summary>
    public bool Sweeps(ReadOnlySpan<float> to, ReadOnlySpan<Capsule> characters)
    {
        if (characters.IsEmpty)
        {
            return false;
        }
        int samples = 1;
        for (int part = 1; part < _states.Length && part < to.Length; part++)
        {
            float change = MathF.Abs(to[part] - _states[part]);
            ShipPartDef def = Interior.Parts[part];
            float travel = def.IsDoor ? change * MathF.Abs(def.Open - def.Closed) : change;
            float step = def.Kind is ShipPartKind.Slide or ShipPartKind.Recoil ? SweepMetres : SweepRadians;
            samples = Math.Max(samples, (int)MathF.Min(1024, MathF.Ceiling(travel / step)));
        }
        Span<float> at = stackalloc float[_states.Length];
        Span<bool> moved = stackalloc bool[_poses.Length];
        BodyInterface bodies = Jolt.BodyInterface;
        bool met = false;
        for (int i = 1; i <= samples && !met; i++)
        {
            float f = (float)i / samples;
            for (int p = 0; p < at.Length; p++)
            {
                at[p] = p < to.Length ? _states[p] + (to[p] - _states[p]) * f : _states[p];
            }
            for (int part = 1; part < _shapes.Parts.Length; part++)
            {
                if (_shapes.Parts[part] == null)
                {
                    continue;
                }
                PartPose pose = Interior.PartPose(part, at);
                if (pose != _poses[part] || moved[part])
                {
                    moved[part] = true;
                    bodies.SetPositionAndRotation(new BodyID((uint)part), pose.Translation, pose.Rotation, Activation.DontActivate);
                }
            }
            foreach (Capsule c in characters)
            {
                if (Meets(c, moved))
                {
                    met = true;
                    break;
                }
            }
        }
        for (int part = 1; part < moved.Length; part++)
        {
            if (moved[part])
            {
                bodies.SetPositionAndRotation(new BodyID((uint)part), _poses[part].Translation, _poses[part].Rotation, Activation.DontActivate);
            }
        }
        return met;
    }

    // Whether a character's capsule overlaps one of the moved parts' bodies (by more than a centimetre).
    private bool Meets(in Capsule c, ReadOnlySpan<bool> moved)
    {
        float half = c.Height / 2;
        using var capsule = new CapsuleShapeSettings(MathF.Max(0.01f, half - c.Radius), c.Radius);
        using Shape shape = capsule.Create();
        Vector3 up = c.Up.LengthSquared() > 1e-6f ? Vector3.Normalize(c.Up) : Vector3.UnitY;
        Quaternion turn = Facing.Turn(Vector3.UnitY, up);
        Matrix4x4 at = Matrix4x4.Transpose(Matrix4x4.CreateFromQuaternion(turn) * Matrix4x4.CreateTranslation(c.Feet + up * half));
        Vector3 scale = Vector3.One, offset = Vector3.Zero;
        var settings = new CollideShapeSettings { BackFaceMode = BackFaceMode.CollideWithBackFaces, MaxSeparationDistance = 0 };
        _collides.Clear();
        Jolt.NarrowPhaseQuery.CollideShape(shape, scale, at, settings, offset, CollisionCollectorType.AllHit, _collides, null, null, null, null);
        foreach (CollideShapeResult r in _collides)
        {
            uint body = r.BodyID2.ID;
            if (body > 0 && body < moved.Length && moved[(int)body] && r.PenetrationDepth > 0.01f)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Where a part's body is now (its motion from rest).</summary>
    public PartPose PoseOf(int part) => part > 0 && part < _poses.Length ? _poses[part] : PartPose.Rest;

    /// <summary>Keeps the broadphase in shape after parts moved (the world is never stepped: GROUND.md §4.5). Under the gate.</summary>
    public void OptimizeBroadPhase()
    {
        if (!_dirty)
        {
            return;
        }
        Jolt.Update(0f, 1, JoltRuntime.Jobs);
        _dirty = false;
    }

    /// <summary>
    /// The nearest piece along a ray of the ship's frame within <paramref name="length"/> (with <paramref name="things"/>,
    /// the things aboard too: their bodies come after the parts', <see cref="IsThing"/>). Under the gate.
    /// </summary>
    public bool Ray(Vector3 origin, Vector3 direction, float length, out SpaceHit hit, bool things = false)
    {
        hit = default;
        if (length <= 0)
        {
            return false;
        }
        var ray = new Ray(origin, direction * length);
        if (!Jolt.NarrowPhaseQuery.CastRay(ray, out RayCastResult result, null, null, things ? null : _piecesOnly))
        {
            return false;
        }
        Vector3 point = ray.GetPointOnRay(result.Fraction);
        Vector3 normal = -direction;
        BodyLockInterface locks = Jolt.BodyLockInterfaceNoLock;
        locks.LockRead(result.BodyID, out BodyLockRead read);
        if (read.Succeeded && read.Body is Body body)
        {
            normal = body.GetWorldSpaceSurfaceNormal((SubShapeID)result.subShapeID2, point);
        }
        locks.UnlockRead(read);
        if (Vector3.Dot(normal, direction) > 0)
        {
            normal = -normal;
        }
        hit = new SpaceHit((int)result.BodyID.ID, result.Fraction * length, point, normal);
        return true;
    }

    /// <summary>
    /// The point of the ship's pieces nearest a point of its frame, within <paramref name="radius"/> (a blast's reach,
    /// the nearest surface reach is measured to): its part and the distance (0 inside a piece). Under the gate.
    /// </summary>
    public bool Nearest(Vector3 point, float radius, out Vector3 nearest, out int part, out float distance)
    {
        nearest = point;
        part = 0;
        distance = float.MaxValue;
        if (radius <= 0)
        {
            return false;
        }
        using var sphere = new SphereShapeSettings(radius);
        using Shape shape = sphere.Create();
        Matrix4x4 at = Matrix4x4.Transpose(Matrix4x4.CreateTranslation(point));
        Vector3 scale = Vector3.One, offset = Vector3.Zero;
        var settings = new CollideShapeSettings { BackFaceMode = BackFaceMode.CollideWithBackFaces, MaxSeparationDistance = 0 };
        _collides.Clear();
        Jolt.NarrowPhaseQuery.CollideShape(shape, scale, at, settings, offset, CollisionCollectorType.AllHit, _collides, null, null, _piecesOnly, null);
        bool found = false;
        foreach (CollideShapeResult c in _collides)
        {
            float d = MathF.Max(0, radius - c.PenetrationDepth);
            if (d < distance || (d == distance && c.BodyID2.ID < (uint)part))
            {
                distance = d;
                nearest = d == 0 ? point : c.ContactPointOn2;
                part = (int)c.BodyID2.ID;
                found = true;
            }
        }
        return found;
    }

    /// <summary>
    /// The nearest point of each part's pieces to a point of the ship's frame within <paramref name="radius"/> (a blast's
    /// reach over a ship's parts with health, IRON_WAKE.md §5.5): one entry per part met (0 the hull), its point and
    /// distance (0 inside a piece), in the parts' order. Under the gate.
    /// </summary>
    public void NearestByPart(Vector3 point, float radius, List<(int Part, Vector3 Point, float Distance)> into)
    {
        into.Clear();
        if (radius <= 0)
        {
            return;
        }
        using var sphere = new SphereShapeSettings(radius);
        using Shape shape = sphere.Create();
        Matrix4x4 at = Matrix4x4.Transpose(Matrix4x4.CreateTranslation(point));
        Vector3 scale = Vector3.One, offset = Vector3.Zero;
        var settings = new CollideShapeSettings { BackFaceMode = BackFaceMode.CollideWithBackFaces, MaxSeparationDistance = 0 };
        _collides.Clear();
        Jolt.NarrowPhaseQuery.CollideShape(shape, scale, at, settings, offset, CollisionCollectorType.AllHit, _collides, null, null, _piecesOnly, null);
        foreach (CollideShapeResult c in _collides)
        {
            int part = (int)c.BodyID2.ID;
            float d = MathF.Max(0, radius - c.PenetrationDepth);
            Vector3 nearest = d == 0 ? point : c.ContactPointOn2;
            int index = into.FindIndex(e => e.Part == part);
            if (index < 0)
            {
                into.Add((part, nearest, d));
            }
            else if (d < into[index].Distance)
            {
                into[index] = (part, nearest, d);
            }
        }
        into.Sort((a, b) => a.Part.CompareTo(b.Part));
    }

    /// <summary>Whether a standing character's capsule with its feet at a point of the ship's frame meets no piece (an exit, a seat's way out). Under the gate.</summary>
    public bool CapsuleClear(Vector3 feet)
    {
        Shape capsule = Capsule(Tuning);
        Matrix4x4 at = Matrix4x4.Transpose(Matrix4x4.CreateTranslation(capsule.CenterOfMass) * Matrix4x4.CreateTranslation(feet));
        Vector3 scale = Vector3.One, offset = Vector3.Zero;
        var settings = new CollideShapeSettings { BackFaceMode = BackFaceMode.CollideWithBackFaces, MaxSeparationDistance = 0 };
        _collides.Clear();
        Jolt.NarrowPhaseQuery.CollideShape(capsule, scale, at, settings, offset, CollisionCollectorType.AnyHit, _collides, null, null, null, null);
        foreach (CollideShapeResult c in _collides)
        {
            if (c.PenetrationDepth > 0.01f)
            {
                return false;
            }
        }
        return true;
    }

    private static Shape? _capsule;
    private static MovementTuning? _capsuleFor;

    // A standing character's capsule, its feet at the origin (as CharacterBody's).
    private static Shape Capsule(MovementTuning tuning)
    {
        if (_capsule != null && _capsuleFor == tuning)
        {
            return _capsule;
        }
        float half = tuning.StandingHeight / 2;
        using var capsule = new CapsuleShapeSettings(half - tuning.Radius, tuning.Radius);
        using var placed = new RotatedTranslatedShapeSettings(new Vector3(0, half, 0), Quaternion.Identity, capsule);
        _capsule = placed.Create();
        _capsuleFor = tuning;
        return _capsule;
    }

    private static readonly Dictionary<Vector3, Shape> BoxShapes = new();

    // A thing's box by its half extents: made once and shared by every space (its bodies hold it; never disposed).
    private static Shape BoxShape(Vector3 half)
    {
        half = Vector3.Max(half, new Vector3(0.02f));
        lock (KindsLock)
        {
            if (!BoxShapes.TryGetValue(half, out Shape? shape))
            {
                using var box = new BoxShapeSettings(half, ConvexRadius);
                shape = box.Create();
                BoxShapes.Add(half, shape);
            }
            return shape;
        }
    }

    // The ship's own pieces (the hull's and the parts' bodies), not the things aboard: what the queries through its frame
    // mean by the ship (the aim, reach, a blast's reach, a part's health).
    private sealed class PiecesOnly(uint firstThing) : BodyFilter
    {
        protected override bool ShouldCollide(BodyID bodyID) => bodyID.ID < firstThing;

        protected override bool ShouldCollideLocked(Body body) => body != null && body.ID.ID < firstThing;
    }

    // ---------------------------------------------------------------- the shapes of a kind, built once and shared

    private sealed class KindShapes
    {
        public Shape Hull = null!;
        public Shape?[] Parts = Array.Empty<Shape?>();
        public int Failed;
    }

    private static KindShapes ShapesOf(ShipInterior interior)
    {
        lock (KindsLock)
        {
            if (Kinds.TryGetValue(interior, out KindShapes? kind))
            {
                return kind;
            }
            kind = Build(interior);
            Kinds.Add(interior, kind);
            return kind;
        }
    }

    // The hull's pieces (the static part's) as one compound, and each part's pieces as its own shape, all in the ship's
    // frame at rest: a part's body is posed by the motion that carries its pieces from rest.
    private static KindShapes Build(ShipInterior interior)
    {
        var byPart = new List<Shape>[interior.Parts.Count];
        var kind = new KindShapes { Parts = new Shape?[interior.Parts.Count] };
        lock (JoltRuntime.Gate)
        {
            JoltRuntime.EnsureInitialised();
            foreach (ShipPiece piece in interior.Pieces)
            {
                Shape? hull = null;
                try
                {
                    using var settings = new ConvexHullShapeSettings(piece.Points, ConvexRadius);
                    hull = settings.Create();
                }
                catch (Exception)
                {
                    hull = null;
                }
                if (hull == null || hull.Handle == IntPtr.Zero)
                {
                    kind.Failed++;
                    continue;
                }
                int part = interior.Parts[piece.Part].Kind is ShipPartKind.Static or ShipPartKind.Loader ? 0 : piece.Part;
                (byPart[part] ??= new List<Shape>()).Add(hull);
            }
            kind.Hull = Compound(byPart[0]) ?? throw new InvalidOperationException($"{interior.File}: the hull has no pieces");
            for (int part = 1; part < byPart.Length; part++)
            {
                kind.Parts[part] = Compound(byPart[part]);
            }
        }
        return kind;
    }

    private static Shape? Compound(List<Shape>? shapes)
    {
        if (shapes == null || shapes.Count == 0)
        {
            return null;
        }
        if (shapes.Count == 1)
        {
            return shapes[0];
        }
        using var compound = new StaticCompoundShapeSettings();
        for (int i = 0; i < shapes.Count; i++)
        {
            compound.AddShape(Vector3.Zero, Quaternion.Identity, shapes[i], (uint)i);
        }
        return compound.Create();
    }

    /// <summary>
    /// The shape of every piece of a kind at the parts' states, as one compound with its origin at the centre of mass: a
    /// parked ship's body in the zone's physics (§4.6). Made once for each kind and set of states (the ramp open, shut) and
    /// shared, so a server and a client make the same body of the same shape.
    /// </summary>
    public static Shape ParkedShape(ShipInterior interior, ReadOnlySpan<float> states)
    {
        ulong key = Hash64.Of((ulong)states.Length);
        foreach (float state in states)
        {
            key = Hash64.Combine(key, BitConverter.SingleToUInt32Bits(state));
        }
        lock (KindsLock)
        {
            if (!ParkedShapes.TryGetValue((interior, key), out Shape? shape))
            {
                shape = MakeParkedShape(interior, states);
                ParkedShapes.Add((interior, key), shape);
            }
            return shape;
        }
    }

    private static Shape MakeParkedShape(ShipInterior interior, ReadOnlySpan<float> states)
    {
        KindShapes kind = ShapesOf(interior);
        lock (JoltRuntime.Gate)
        {
            using var compound = new StaticCompoundShapeSettings();
            compound.AddShape(-interior.CentreOfMass, Quaternion.Identity, kind.Hull, 0);
            for (int part = 1; part < kind.Parts.Length; part++)
            {
                if (kind.Parts[part] is { } shape)
                {
                    PartPose pose = interior.PartPose(part, states);
                    compound.AddShape(pose.Translation - interior.CentreOfMass, pose.Rotation, shape, (uint)part);
                }
            }
            return compound.Create();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _body?.Dispose();
        lock (JoltRuntime.Gate)
        {
            Jolt.Dispose();
            _piecesOnly.Dispose();
            _objectVsBroadPhase.Dispose();
            _broadPhaseLayers.Dispose();
            _layerPairs.Dispose();
        }
    }
}
