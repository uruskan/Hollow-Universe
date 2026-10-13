using System;
using System.Collections.Generic;
using Godot;
using HollowUniverse.Items;

namespace HollowUniverse.Gibs;

/// <summary>
/// One piece of a model (GROUND.md §9.4): its mesh (the model's own materials), where it sits in the model
/// (<see cref="Rest"/>, so the pieces put together are the model), its contact shape (the box of its principal axes and up
/// to fourteen points of its hull, in its own frame) and the box's inverse inertia for a mass of one.
/// </summary>
public sealed class GibChunk
{
    public required Mesh Mesh { get; init; }

    public Transform3D Rest { get; init; }

    /// <summary>The middle of the piece's box in its mesh: the point it turns about.</summary>
    public Vector3 Centre { get; init; }

    public Vector3 Half { get; init; }

    public Vector3[] Support { get; init; } = Array.Empty<Vector3>();

    /// <summary>The farthest support point from the middle: no contact is possible farther from the floor.</summary>
    public float Radius { get; init; }

    public Vector3 InverseInertia { get; init; }

    public float Volume => 8 * Half.X * Half.Y * Half.Z;
}

/// <summary>The pieces a model (or a kit's part) breaks into, its bounds in the model's frame and its gib material.</summary>
public sealed class GibSet
{
    public required string Key { get; init; }

    public required GibChunk[] Chunks { get; init; }

    public Aabb Bounds { get; init; }

    public required GibMaterial Material { get; init; }

    /// <summary>Pieces at least this big (by box volume) are kept when the world is busy: the bigger half.</summary>
    public float MedianVolume { get; init; }
}

/// <summary>
/// The gib sets (GROUND.md §9.4) by model key and part, from the files the model catalogue names
/// (<c>models.json</c>: an entry's <c>gibs</c>): a whole model's pieces are its file's top nodes, a kit's are grouped
/// under a node per part. Files load once (threaded when asked ahead by <see cref="Request"/>) and are read on the main
/// thread the first time a set is wanted, or all at once by <see cref="Prepare"/> while a world opens.
/// </summary>
public static class GibLibrary
{
    private static readonly Dictionary<string, Dictionary<string, GibSet>> _files = new(StringComparer.Ordinal);
    private static readonly HashSet<string> _requested = new(StringComparer.Ordinal);

    /// <summary>The 14 directions the hull points are taken along: the axes and the diagonals.</summary>
    private static readonly Vector3[] Directions = BuildDirections();

    /// <summary>The set a model breaks into (<paramref name="part"/>: a kit's part, empty for a whole model), or null if it has none.</summary>
    public static GibSet? Get(string model, string part)
    {
        if (ModelCatalog.Find(model) is not { Gibs: { } gibs })
        {
            return null;
        }
        Dictionary<string, GibSet> sets = Read(gibs);
        return sets.TryGetValue(part ?? "", out GibSet? set) ? set : null;
    }

    /// <summary>A kit's parts that have gibs (empty for a whole model or a model without gibs), in the file's order.</summary>
    public static IReadOnlyList<string> Parts(string model)
    {
        if (ModelCatalog.Find(model) is not { Gibs: { } gibs })
        {
            return Array.Empty<string>();
        }
        var parts = new List<string>();
        foreach (string part in Read(gibs).Keys)
        {
            if (part.Length > 0)
            {
                parts.Add(part);
            }
        }
        return parts;
    }

    /// <summary>Starts loading every gib file the catalogue names, on Godot's loader threads.</summary>
    public static void Request()
    {
        foreach (ModelEntry e in ModelCatalog.Entries)
        {
            if (e.Gibs is { } gibs && !_files.ContainsKey(gibs.File) && _requested.Add(gibs.File) && ResourceLoader.Exists(gibs.File))
            {
                ResourceLoader.LoadThreadedRequest(gibs.File);
            }
        }
    }

    /// <summary>Reads every gib file now (while a world opens, so a first break never waits on a file). Returns the sets.</summary>
    public static IEnumerable<GibSet> Prepare()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (ModelEntry e in ModelCatalog.Entries)
        {
            if (e.Gibs is not { } gibs || !seen.Add(gibs.File))
            {
                continue;
            }
            foreach (GibSet set in Read(gibs).Values)
            {
                yield return set;
            }
        }
    }

    private static Dictionary<string, GibSet> Read(GibsInfo gibs)
    {
        if (_files.TryGetValue(gibs.File, out Dictionary<string, GibSet>? sets))
        {
            return sets;
        }
        sets = new Dictionary<string, GibSet>(StringComparer.Ordinal);
        _files[gibs.File] = sets;
        PackedScene? scene = _requested.Remove(gibs.File) ? ResourceLoader.LoadThreadedGet(gibs.File) as PackedScene
            : ResourceLoader.Exists(gibs.File) ? ResourceLoader.Load<PackedScene>(gibs.File) : null;
        if (scene == null)
        {
            GD.PushWarning($"GibLibrary: cannot load {gibs.File}");
            return sets;
        }
        GibMaterial material = GibCatalog.Material(gibs.Material);
        Node root = scene.Instantiate();
        var whole = new List<GibChunk>();
        foreach (Node child in root.GetChildren())
        {
            if (child is MeshInstance3D piece)
            {
                Add(whole, piece, Transform3D.Identity);
            }
            else if (child is Node3D group)
            {
                var parts = new List<GibChunk>();
                foreach (Node c in group.GetChildren())
                {
                    if (c is MeshInstance3D p)
                    {
                        Add(parts, p, group.Transform);
                    }
                }
                if (parts.Count > 0)
                {
                    sets[group.Name] = Make(gibs.File + "/" + group.Name, parts, material);
                }
            }
        }
        if (whole.Count > 0)
        {
            sets[""] = Make(gibs.File, whole, material);
        }
        root.Free();
        return sets;
    }

    private static void Add(List<GibChunk> into, MeshInstance3D node, Transform3D parent)
    {
        if (node.Mesh is not Mesh mesh)
        {
            return;
        }
        Aabb box = mesh.GetAabb();
        Vector3 middle = box.GetCenter();
        Vector3 half = (box.Size * 0.5f).Max(new Vector3(0.01f, 0.01f, 0.01f));
        // The extreme vertices along fourteen directions: a small hull for contacts, about the box's middle.
        var support = new List<Vector3>(14);
        Vector3[] best = new Vector3[Directions.Length];
        float[] reach = new float[Directions.Length];
        Array.Fill(reach, float.MinValue);
        for (int s = 0; s < mesh.GetSurfaceCount(); s++)
        {
            foreach (Vector3 v in mesh.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Vertex].AsVector3Array())
            {
                for (int d = 0; d < Directions.Length; d++)
                {
                    float k = v.Dot(Directions[d]);
                    if (k > reach[d])
                    {
                        reach[d] = k;
                        best[d] = v;
                    }
                }
            }
        }
        float radius = 0.02f;
        foreach (Vector3 p in best)
        {
            Vector3 q = p - middle;
            if (!support.Exists(s => s.DistanceSquaredTo(q) < 1e-6f))
            {
                support.Add(q);
                radius = MathF.Max(radius, q.Length());
            }
        }
        // A box of mass one: I = (b² + c²) / 3 about each axis.
        var inertia = new Vector3(half.Y * half.Y + half.Z * half.Z, half.X * half.X + half.Z * half.Z, half.X * half.X + half.Y * half.Y) / 3f;
        into.Add(new GibChunk
        {
            Mesh = mesh,
            // Where the piece sits: its node in the model, moved to its box's middle (the integrator turns it about that).
            Rest = parent * node.Transform * new Transform3D(Basis.Identity, middle),
            Centre = middle,
            Half = half,
            Support = support.ToArray(),
            Radius = radius,
            InverseInertia = new Vector3(1f / inertia.X, 1f / inertia.Y, 1f / inertia.Z),
        });
    }

    private static GibSet Make(string key, List<GibChunk> chunks, GibMaterial material)
    {
        Aabb bounds = default;
        for (int i = 0; i < chunks.Count; i++)
        {
            Vector3 c = chunks[i].Rest.Origin;
            var b = new Aabb(c - chunks[i].Half, chunks[i].Half * 2);
            bounds = i == 0 ? b : bounds.Merge(b);
        }
        var volumes = new List<float>(chunks.Count);
        foreach (GibChunk c in chunks)
        {
            volumes.Add(c.Volume);
        }
        volumes.Sort();
        return new GibSet { Key = key, Chunks = chunks.ToArray(), Bounds = bounds, Material = material, MedianVolume = volumes[volumes.Count / 2] };
    }

    private static Vector3[] BuildDirections()
    {
        var list = new List<Vector3> { Vector3.Right, Vector3.Left, Vector3.Up, Vector3.Down, Vector3.Back, Vector3.Forward };
        foreach (float x in new[] { -1f, 1f })
        {
            foreach (float y in new[] { -1f, 1f })
            {
                foreach (float z in new[] { -1f, 1f })
                {
                    list.Add(new Vector3(x, y, z).Normalized());
                }
            }
        }
        return list.ToArray();
    }
}
