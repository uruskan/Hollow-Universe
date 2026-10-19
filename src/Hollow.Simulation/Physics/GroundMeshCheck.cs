using System;
using Hollow.Surface;

namespace Hollow.Simulation.Physics;

/// <summary>
/// A ground tile whose triangles Jolt must not be given: no triangles, a vertex that is not a finite number, or an
/// index outside its vertices. Jolt does not check these; it reads past its arrays or builds a tree on NaN, and the
/// process dies with a native access violation that names nothing (docs/KNOWN_PROBLEMS.md).
/// </summary>
public sealed class MalformedGroundTileException(TileKey tile, string problem)
    : InvalidOperationException($"Ground tile face {tile.Face}, level {tile.Level}, ({tile.X}, {tile.Y}) is malformed: {problem}.")
{
    public TileKey Tile { get; } = tile;

    public string Problem { get; } = problem;
}

/// <summary>
/// The one check every ground tile's triangles pass before they become a Jolt mesh shape: the legacy tiles
/// (<see cref="PhysicsWorld"/>'s BuildShape) and the selected ground's alike.
/// </summary>
public static class GroundMeshCheck
{
    /// <summary>
    /// Throws <see cref="MalformedGroundTileException"/> unless the first <paramref name="vertexCount"/> vertices
    /// (x, y, z in turn) are finite and the first <paramref name="triangleCount"/> triangles index them, at least one
    /// with three different vertices (Jolt drops the others and refuses a mesh with none).
    /// </summary>
    public static void Require(TileKey tile, ReadOnlySpan<float> positions, ReadOnlySpan<int> indices, int vertexCount, int triangleCount)
    {
        if (vertexCount < 3 || triangleCount < 1)
        {
            throw new MalformedGroundTileException(tile, $"{vertexCount} vertices and {triangleCount} triangles");
        }
        if (positions.Length < 3 * vertexCount || indices.Length < 3 * triangleCount)
        {
            throw new MalformedGroundTileException(tile,
                $"arrays of {positions.Length} coordinates and {indices.Length} indices for {vertexCount} vertices and {triangleCount} triangles");
        }
        for (int i = 0; i < 3 * vertexCount; i++)
        {
            if (!float.IsFinite(positions[i]))
            {
                throw new MalformedGroundTileException(tile, $"vertex {i / 3} has coordinate {positions[i]}");
            }
        }
        bool anyWhole = false;
        for (int t = 0; t < triangleCount; t++)
        {
            int a = indices[3 * t], b = indices[3 * t + 1], c = indices[3 * t + 2];
            if ((uint)a >= (uint)vertexCount || (uint)b >= (uint)vertexCount || (uint)c >= (uint)vertexCount)
            {
                throw new MalformedGroundTileException(tile, $"triangle {t} indexes vertices {a}, {b}, {c} of {vertexCount}");
            }
            anyWhole |= a != b && b != c && a != c;
        }
        if (!anyWhole)
        {
            throw new MalformedGroundTileException(tile, $"all {triangleCount} triangles repeat a vertex");
        }
    }
}
