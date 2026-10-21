using System;
using System.Runtime.CompilerServices;
using Hollow.Surface;
using JoltPhysicsSharp;

namespace Hollow.Simulation.Physics;

/// <summary>Explicit versioned estimates, never measured native allocations. There are no production defaults.</summary>
public sealed record GroundNativeEstimates
{
    public required string Contract { get; init; }
    public required long ShapeFixed { get; init; }
    public required long ShapePerVertex { get; init; }
    public required long ShapePerTriangle { get; init; }
    public required long BuildFixed { get; init; }
    public required long BuildPerVertex { get; init; }
    public required long BuildPerTriangle { get; init; }
    public required long BodyBytes { get; init; }
    public required long SetupBytes { get; init; }
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Contract) || Contract.Length > 128 || ShapeFixed < 1 || BuildFixed < 1 ||
            BodyBytes < 1 || SetupBytes < 1 || ShapeFixed > 1L << 30 || BuildFixed > 1L << 30 ||
            BodyBytes > 1L << 20 || SetupBytes > 1L << 30 || ShapePerVertex < 0 || ShapePerVertex > 1 << 20 ||
            ShapePerTriangle < 0 || ShapePerTriangle > 1 << 20 || BuildPerVertex < 0 || BuildPerVertex > 1 << 20 ||
            BuildPerTriangle < 0 || BuildPerTriangle > 1 << 20)
            throw new ArgumentOutOfRangeException(nameof(GroundNativeEstimates));
    }
}

/// <summary>Inactive adapter cost contract. Existing PhysicsWorld queue/Ensure consumers do not use this yet.</summary>
public static class GroundStreamingEstimates
{
    public static int IndexedTriangleBytes => Unsafe.SizeOf<IndexedTriangle>();
    internal static GroundBuildCost For(SelectedGroundCollisionPlan plan, GroundNativeEstimates native) => new(
        plan.EstimatedRetainedBytes,
        checked(plan.EstimatedBuildScratchBytes + 12L * plan.VertexCount + IndexedTriangleBytes * (long)plan.TriangleCount + 8192),
        checked(native.BuildFixed + native.BuildPerVertex * plan.VertexCount + native.BuildPerTriangle * plan.TriangleCount),
        checked(native.ShapeFixed + native.ShapePerVertex * plan.VertexCount + native.ShapePerTriangle * plan.TriangleCount));
}

internal readonly record struct GroundBuildCost(long Plan, long ManagedBuild, long NativeBuild, long NativeShape);
