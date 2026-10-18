using System;
using System.Collections.Generic;

namespace Hollow.Surface;

/// <summary>
/// An authored, implicitly closed physical contact loop. Indices retain their supplied order; the final
/// vertex joins the first. Qualification checks actual mesh edges, not whole-module watertightness.
/// This is independent of the open CliffContactDomain chains used by the placement fitter.
/// </summary>
public sealed class FormationContactLoop
{
    public const int MaximumVertices = 512;
    public const int MaximumLoops = 128;
    private readonly int[] _vertices;

    public FormationContactLoop(string key, IEnumerable<int> vertices)
    {
        Key = FormationInput.Key(key, nameof(key));
        _vertices = CopyVertices(vertices);
    }

    public string Key { get; }
    public ReadOnlySpan<int> VertexIndices => _vertices;

    internal static int[] CopyVertices(IEnumerable<int> vertices)
    {
        int[] result = FormationInput.Copy(vertices, MaximumVertices, nameof(vertices));
        if (result.Length < 3) throw new FormationValidationException("A closed contact loop needs at least three vertices.");
        var seen = new HashSet<int>();
        foreach (int vertex in result)
            if (vertex < 0 || !seen.Add(vertex))
                throw new FormationValidationException("Closed contact vertices must be nonnegative and unique; omit the repeated closing vertex.");
        return result;
    }
}

/// <summary>Ordered visual indices corresponding exactly to a named physical contact loop's local coordinates.</summary>
public sealed class FormationContactLoopBinding
{
    private readonly int[] _vertices;
    private readonly int[] _edgeVertices;
    public FormationContactLoopBinding(string loopKey, IEnumerable<int> vertices, IEnumerable<int>? edgeVertices = null)
    {
        LoopKey = FormationInput.Key(loopKey, nameof(loopKey));
        _vertices = FormationContactLoop.CopyVertices(vertices);
        _edgeVertices = edgeVertices == null ? Array.Empty<int>() :
            FormationInput.Copy(edgeVertices, 2 * FormationContactLoop.MaximumVertices, nameof(edgeVertices));
        if (edgeVertices != null && _edgeVertices.Length != 2 * _vertices.Length)
            throw new FormationValidationException("Visual seam bindings need two actual vertex indices per closed segment.");
        foreach (int vertex in _edgeVertices)
            if (vertex < 0) throw new FormationValidationException("Visual seam edge vertices must be nonnegative.");
    }

    public string LoopKey { get; }
    public ReadOnlySpan<int> VertexIndices => _vertices;
    /// <summary>
    /// Optional actual mesh-edge endpoints, two per ordered segment. UV/normal/material seams may have
    /// distinct indices at exactly equal coordinates. Empty preserves the original indexed-loop contract.
    /// The definition validates every endpoint's exact coordinate and actual triangle-edge membership.
    /// </summary>
    public ReadOnlySpan<int> EdgeVertexIndices => _edgeVertices;
}
