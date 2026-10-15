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

    /// <summary>The biome at a unit direction (stand-in surfaces: grassland).</summary>
    Biome BiomeAt(Vector3D direction) => Biome.Grassland;

    /// <summary>
    /// Natural objects (trees, bushes, ferns, rocks) on a scatter tile of <see cref="ScatterLevel"/>, in a fixed
    /// order; each one's ID is the tile plus its <see cref="ScatterItem.Index"/> (stand-in surfaces: none).
    /// </summary>
    IReadOnlyList<ScatterItem> ScatterFor(TileKey tile) => Array.Empty<ScatterItem>();

    /// <summary>The cube-sphere level of the scatter's tiles (<see cref="ScatterFor"/>).</summary>
    int ScatterLevel => Scatter.Level;

    /// <summary>
    /// <see cref="ScatterFor"/>, or with <paramref name="treesOnly"/> its trees alone (what far impostors need; a surface
    /// may find them faster than the whole tile).
    /// </summary>
    IReadOnlyList<ScatterItem> ScatterFor(TileKey tile, bool treesOnly)
    {
        IReadOnlyList<ScatterItem> all = ScatterFor(tile);
        if (!treesOnly || all.Count == 0)
        {
            return all;
        }
        var trees = new List<ScatterItem>();
        foreach (ScatterItem item in all)
        {
            if (item.Kind <= ScatterKind.Broadleaf)
            {
                trees.Add(item);
            }
        }
        return trees;
    }

    // ---------------------------------------------------------------- drawing (docs/WHOLE_WORLD.md §10.1)

    /// <summary>
    /// The lowest and highest ground in metres above <see cref="Radius"/>, sea floor included: a drawn tile's bounds
    /// before its own heights are known. By default half a percent of the radius either way.
    /// </summary>
    (double Low, double High) HeightBounds => (-0.005 * Radius, 0.005 * Radius);

    /// <summary>
    /// A point of the ground for drawing, in one evaluation of the surface: its height above <see cref="Radius"/> and
    /// whatever else the surface knows there (by default the height alone). <see cref="Cover"/> takes it back, so a
    /// surface that works out both at once (<see cref="PlanetSurface"/>) does it once.
    /// </summary>
    SurfaceSample Sample(Vector3D direction) => new(HeightAt(direction), 0, 0, 0, 0);

    /// <summary>
    /// What the ground at a point looks like, for drawing (GROUND.md §4.6): the biomes' soft weights
    /// (<see cref="BiomeTable.Count"/>), the ground materials' weights (<see cref="BiomeTable.MaterialCount"/>, in
    /// <see cref="GroundMaterial"/> order, adding up to about 1), and the climate and plant cover the shaders tint the
    /// ground and the grass with. <paramref name="sample"/> is <see cref="Sample"/>'s at <paramref name="direction"/>,
    /// <paramref name="slope"/> the sine of the ground's tilt there. By default from <see cref="BiomeAt"/> through the
    /// biome table (<see cref="BiomeTable.Cover"/>); a surface with a climate of its own blends its borders.
    /// </summary>
    GroundCover Cover(Vector3D direction, in SurfaceSample sample, double slope, Span<double> biomes, Span<double> materials) =>
        BiomeTable.Cover(BiomeAt(direction), sample.Height - (SeaRadius > 0 ? SeaRadius - Radius : 0), slope, biomes, materials);
}

/// <summary>
/// The climate and plant cover at a point, as drawing reads it (<see cref="ISurface.Cover"/>): what tints the ground,
/// thickens the grass and darkens the forest floor. Presentation only: no rule of the game reads it.
/// </summary>
public readonly struct GroundCover
{
    public GroundCover(double temperature, double moisture, double grass, double trees)
    {
        Temperature = temperature;
        Moisture = moisture;
        Grass = grass;
        Trees = trees;
    }

    /// <summary>Annual mean temperature, °C.</summary>
    public double Temperature { get; }

    /// <summary>How wet the climate is, 0 (desert) to 1 (rainforest).</summary>
    public double Moisture { get; }

    /// <summary>How thick the grass grows, 0 to 1.</summary>
    public double Grass { get; }

    /// <summary>Trees per square metre.</summary>
    public double Trees { get; }

    /// <summary>The tree cover the ground is painted with, 0 (open) to 1 (closed forest).</summary>
    public double TreeCover => Math.Clamp(Trees / 0.022, 0, 1);
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
