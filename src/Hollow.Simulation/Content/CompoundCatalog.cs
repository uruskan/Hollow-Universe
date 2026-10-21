using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hollow.Simulation.Building;

namespace Hollow.Simulation.Content;

public sealed record CompoundPart(string Name, string Mesh, string? Parent, Vector3 Pivot);
public sealed record CompoundSocket(string Name, string Part, Vector3 At);
public sealed record CompoundHull(string Name, string Part, Vector3[] Points)
{
    public ConvexPart Shape { get; } = new(Points);
}

/// <summary>Authored geometry in assembly-local metres. Parts retain their named articulation pivots.</summary>
public sealed class CompoundAssembly
{
    public string Key { get; init; } = "";
    /// <summary>Measured half extents of the delivered model, matching DeployableDef.HalfExtents.</summary>
    public Vector3 Size { get; init; }
    public IReadOnlyList<CompoundPart> Parts { get; init; } = Array.Empty<CompoundPart>();
    public IReadOnlyList<CompoundHull> Hulls { get; init; } = Array.Empty<CompoundHull>();
    public IReadOnlyList<CompoundSocket> Sockets { get; init; } = Array.Empty<CompoundSocket>();
    public CompoundPart? Part(string name) => Parts.FirstOrDefault(p => p.Name == name);
}

/// <summary>Shared visual and collision assembly definitions, hashed with the gameplay content.</summary>
public sealed class CompoundCatalog
{
    public static readonly CompoundCatalog Empty = new(Array.Empty<CompoundAssembly>());
    private readonly Dictionary<string, CompoundAssembly> _byKey;
    public CompoundCatalog(IReadOnlyList<CompoundAssembly> assemblies)
    {
        Assemblies = assemblies;
        _byKey = new Dictionary<string, CompoundAssembly>(StringComparer.Ordinal);
        foreach (CompoundAssembly assembly in assemblies) _byKey.TryAdd(assembly.Key, assembly);
    }
    public IReadOnlyList<CompoundAssembly> Assemblies { get; }
    public CompoundAssembly? Find(string? key) => key == null ? null : _byKey.GetValueOrDefault(key);
}
