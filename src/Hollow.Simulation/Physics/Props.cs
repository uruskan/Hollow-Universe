using System.Numerics;
using System.Collections.Generic;
using Hollow.Simulation.Building;
using JoltPhysicsSharp;

namespace Hollow.Simulation.Physics;

/// <summary>Who a prop belongs to: its key is only unique within its layer.</summary>
public enum PropLayer : byte
{
    /// <summary>Trees and outcrops; keyed by natural object ID.</summary>
    Nature,

    /// <summary>Placed objects; keyed by entity ID.</summary>
    Deployable,
}

/// <summary>Told when ground tiles are added to or removed from a <see cref="PhysicsWorld"/>, on its owning thread.</summary>
public interface ITileListener
{
    void TileAdded(Hollow.Surface.TileKey tile);

    void TileRemoved(Hollow.Surface.TileKey tile);

    /// <summary>
    /// A tile is being built ahead on a worker thread: prepare what <see cref="TileAdded"/> will need (pure work
    /// only, such as generating its natural objects), so the tick that adds it doesn't wait.
    /// </summary>
    void TilePreparing(Hollow.Surface.TileKey tile)
    {
    }
}

public enum PropKind : byte
{
    /// <summary>A capsule along the rotation's +Y: trunks.</summary>
    Capsule,

    Sphere,

    Box,
    Compound,
}

/// <summary>A static collision shape for a prop (GROUND.md §4.5: deployables and natural objects use simple shapes).</summary>
public readonly record struct PropShape
{
    public PropKind Kind { get; init; }

    /// <summary>The shape's centre, and its rotation (+Y of the shape along the given up).</summary>
    public Vector3 Centre { get; init; }

    public Quaternion Rotation { get; init; }

    public float Radius { get; init; }

    /// <summary>A capsule's half height of its straight part.</summary>
    public float HalfHeight { get; init; }

    public Vector3 HalfExtents { get; init; }
    public ConvexPart[]? Parts { get; init; }

    public static PropShape Compound(Vector3 at, Quaternion rotation, ConvexPart[] parts) => new()
    {
        Kind = PropKind.Compound, Centre = at, Rotation = rotation, Parts = parts,
    };

    public static PropShape Capsule(Vector3 a, Vector3 b, float radius, Vector3 up) => new()
    {
        Kind = PropKind.Capsule,
        Centre = (a + b) / 2,
        Rotation = CharacterBody.UpRotation(up),
        Radius = radius,
        HalfHeight = Vector3.Distance(a, b) / 2,
    };

    public static PropShape Sphere(Vector3 centre, float radius) => new()
    {
        Kind = PropKind.Sphere,
        Centre = centre,
        Rotation = Quaternion.Identity,
        Radius = radius,
    };

    /// <summary>A box standing on <paramref name="up"/>, turned by <paramref name="yaw"/> from local north (<see cref="Movement.Facing"/>).</summary>
    public static PropShape Box(Vector3 centre, Vector3 halfExtents, Vector3 up, float yaw)
    {
        // The box's local −Z is its forward.
        (Vector3 forward, Vector3 right) = Movement.Facing.Basis(yaw, up);
        var basis = new Matrix4x4(
            right.X, right.Y, right.Z, 0,
            up.X, up.Y, up.Z, 0,
            -forward.X, -forward.Y, -forward.Z, 0,
            0, 0, 0, 1);
        return new PropShape
        {
            Kind = PropKind.Box,
            Centre = centre,
            Rotation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(basis)),
            HalfExtents = halfExtents,
        };
    }

    internal Shape CreateShape()
    {
        switch (Kind)
        {
            case PropKind.Compound:
            {
                using var compound = new StaticCompoundShapeSettings();
                var settings = new List<ConvexHullShapeSettings>();
                try
                {
                    foreach (ConvexPart part in Parts!)
                    {
                        var hull = new ConvexHullShapeSettings(part.Points, 0.005f);
                        settings.Add(hull);
                        compound.AddShape(Vector3.Zero, Quaternion.Identity, hull, 0);
                    }
                    return compound.Create();
                }
                finally { foreach (var hull in settings) hull.Dispose(); }
            }
            case PropKind.Capsule:
            {
                using var settings = new CapsuleShapeSettings(System.Math.Max(0.01f, HalfHeight), Radius);
                return settings.Create();
            }
            case PropKind.Sphere:
            {
                using var settings = new SphereShapeSettings(Radius);
                return settings.Create();
            }
            default:
            {
                Vector3 half = Vector3.Max(HalfExtents, new Vector3(0.06f));
                using var settings = new BoxShapeSettings(half, 0.05f);
                return settings.Create();
            }
        }
    }
}
