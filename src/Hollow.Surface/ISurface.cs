using System;
using System.Collections.Generic;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>
/// A body's ground as a pure function (GROUND.md §4.6): the server builds collision from it and the client draws it,
/// and both get the same result to the bit (ARCHITECTURE.md §8).
/// </summary>
/// <remarks>
/// Directions and points are in the <b>body frame</b>: metres from the body's centre, fixed to the surface and turning
/// with it. +Y is the north pole (the rotation axis); the prime meridian is +X; east at the prime meridian is −Z
/// (<see cref="ZoneFrame"/> gives the exact axes).
/// </remarks>
public interface ISurface
{
    /// <summary>Mean radius in metres; heights are measured from it.</summary>
    double Radius { get; }

    /// <summary>Radius of the sea surface in metres, or 0 when the body has no surface liquid.</summary>
    double SeaRadius { get; }

    /// <summary>Height of the ground above <see cref="Radius"/> at a unit direction from the body's centre, in metres.</summary>
    double HeightAt(Vector3D direction);

    /// <summary>
    /// Ground triangles in a zone's frame covering a cube-sphere tile and <paramref name="margin"/> metres around it
    /// (neighbouring tiles overlap by that much). Only the ground: natural objects and water are separate. By default
    /// a grid of about a metre on the surface's heights (<see cref="SurfaceGrid"/>).
    /// </summary>
    TriangleMesh GroundCollision(in ZoneFrame frame, TileKey tile, double margin) =>
        SurfaceGrid.Collision(this, frame, tile, margin, SurfaceGrid.DefaultSpacing);

    /// <summary>
    /// <see cref="GroundCollision"/>'s triangles, the same to the bit, perhaps in pooled arrays (the physics builds
    /// tiles all the time); dispose the result. Surfaces that don't override it hand over their mesh's own arrays.
    /// </summary>
    PooledTriangles GroundCollisionPooled(in ZoneFrame frame, TileKey tile, double margin) =>
        PooledTriangles.Of(GroundCollision(frame, tile, margin));

    /// <summary>
    /// The height above <see cref="Radius"/> of the water's surface over a unit direction, or −∞ where none lies: a point
    /// below it is in water. By default the sea's level wherever the body has a sea (the sea covers everything below
    /// it); the walking-scale ground adds its lakes and streams (docs/WALKING_GROUND.md §4).
    /// </summary>
    double WaterLevelAt(Vector3D direction) => SeaRadius > 0 ? SeaRadius - Radius : double.NegativeInfinity;

    /// <summary>The biome at a unit direction (stand-in surfaces: grassland).</summary>
    Biome BiomeAt(Vector3D direction) => Biome.Grassland;

    /// <summary>
    /// Natural objects (trees, bushes, ferns, rocks) on a scatter tile of <see cref="Scatter.Level"/>, in a fixed
    /// order; each one's ID is the tile plus its <see cref="ScatterItem.Index"/> (stand-in surfaces: none).
    /// </summary>
    IReadOnlyList<ScatterItem> ScatterFor(TileKey tile) => Array.Empty<ScatterItem>();
}

/// <summary>
/// Triangles in a zone's frame (metres, float). Each triangle is wound counter-clockwise seen from above, so
/// (b − a) × (c − a) points away from the ground.
/// </summary>
public sealed class TriangleMesh
{
    public TriangleMesh(float[] positions, int[] indices)
    {
        if (positions.Length % 3 != 0 || indices.Length % 3 != 0)
        {
            throw new ArgumentException("Positions and indices come in threes.");
        }
        Positions = positions;
        Indices = indices;
    }

    /// <summary>x, y, z of each vertex in turn.</summary>
    public float[] Positions { get; }

    /// <summary>Three vertex indices per triangle.</summary>
    public int[] Indices { get; }

    public int VertexCount => Positions.Length / 3;
    public int TriangleCount => Indices.Length / 3;
}

/// <summary>
/// Triangles as <see cref="TriangleMesh"/>, in arrays that may be rented from the shared pools and be longer than
/// the counts (<see cref="ISurface.GroundCollisionPooled"/>): dispose to give them back.
/// </summary>
public sealed class PooledTriangles : IDisposable
{
    private readonly bool _rented;

    public PooledTriangles(float[] positions, int[] indices, int vertexCount, int triangleCount, bool rented)
    {
        Positions = positions;
        Indices = indices;
        VertexCount = vertexCount;
        TriangleCount = triangleCount;
        _rented = rented;
    }

    /// <summary>A mesh's own arrays (nothing to give back).</summary>
    public static PooledTriangles Of(TriangleMesh mesh) => new(mesh.Positions, mesh.Indices, mesh.VertexCount, mesh.TriangleCount, false);

    /// <summary>x, y, z of each vertex in turn; the first <see cref="VertexCount"/> count.</summary>
    public float[] Positions { get; }

    /// <summary>Three vertex indices per triangle; the first <see cref="TriangleCount"/> count.</summary>
    public int[] Indices { get; }

    public int VertexCount { get; }

    public int TriangleCount { get; }

    public void Dispose()
    {
        if (_rented)
        {
            System.Buffers.ArrayPool<float>.Shared.Return(Positions);
            System.Buffers.ArrayPool<int>.Shared.Return(Indices);
        }
    }
}
