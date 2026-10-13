using System;
using System.Collections.Generic;
using Godot;
using Hollow.Core;
using Hollow.Simulation.Building;
using Num = System.Numerics;

namespace HollowUniverse.Build;

/// <summary>
/// The posts that close the notch walls leave at an outer corner (GROUND.md §6.3, Fit; decision J5). Walls stand centred
/// on their edges with square ends, so where two of them meet at an angle the outside of the corner misses a column
/// (15 × 15 cm at 90°, a kite reaching 26 cm out at 60°). Wherever wall ends meet at a point and an empty angle round it
/// is wider than half a turn, a post shaped for the corner's interior angle fills that notch, as tall as the lower of the
/// two walls, in the grade of the higher. Drawn only: no collision, no health; it goes when either wall does. The posts'
/// meshes come with the grade's art (<c>corner-&lt;angle&gt;-&lt;height in cm&gt;</c>, art/import/building_kit.py); a
/// grade without them (twig, whose round end posts close its corners) gets none.
/// </summary>
public sealed class CornerPosts
{
    private readonly Node3D _parent;
    private readonly BuildingAssets _assets;
    // One batch a post and grade for the whole zone: posts are a dozen triangles, so drawing every one is cheaper than a
    // draw call a chunk.
    private readonly Dictionary<(string Mesh, int Grade), MultiMeshInstance3D> _batches = new();
    private readonly Dictionary<(string Mesh, int Grade), List<Transform3D>> _wanted = new();

    public CornerPosts(Node3D parent, BuildingAssets assets)
    {
        _parent = parent;
        _assets = assets;
    }

    /// <summary>Posts drawn.</summary>
    public int Count { get; private set; }

    /// <summary>Multi-meshes in use.</summary>
    public int Batches => _batches.Count;

    /// <summary>Hides them all (the stability overlay colours pieces only).</summary>
    public bool Hidden
    {
        set
        {
            foreach (MultiMeshInstance3D batch in _batches.Values)
            {
                batch.Visible = !value && batch.Multimesh.InstanceCount > 0;
            }
        }
    }

    // A wall's end: the wall, which way it runs from there, its base and top.
    private readonly record struct End(Piece Wall, Vector3D Into, double Height);

    /// <summary>Works out every post again from the walls standing now, and redraws them.</summary>
    public void Update(IEnumerable<Piece> pieces, double width, bool hidden)
    {
        var ends = new Dictionary<(BuildingFrame Frame, long X, long Y, long Z), List<End>>();
        foreach (Piece piece in pieces)
        {
            if (piece.Gone || piece.Def.Family != PieceFamily.Wall)
            {
                continue;
            }
            double half = width / 2;
            foreach (int side in new[] { -1, 1 })
            {
                Vector3D point = PieceGeometry.ToFrame(piece.Pose, new Vector3D(side * half, 0, 0));
                var key = (piece.Structure.Frame, Mm(point.X), Mm(point.Y), Mm(point.Z));
                if (!ends.TryGetValue(key, out List<End>? list))
                {
                    ends[key] = list = new List<End>();
                }
                list.Add(new End(piece, PieceGeometry.Rotate(new Vector3D(-side, 0, 0), piece.Pose.Yaw), piece.Def.Height));
            }
        }
        foreach (List<Transform3D> list in _wanted.Values)
        {
            list.Clear();
        }
        Count = 0;
        foreach (((BuildingFrame frame, long x, long y, long z), List<End> here) in ends)
        {
            if (here.Count < 2)
            {
                continue;
            }
            here.Sort((a, b) => Angle(a.Into).CompareTo(Angle(b.Into)));
            for (int i = 0; i < here.Count; i++)
            {
                End a = here[i], b = here[(i + 1) % here.Count];
                double from = Angle(a.Into), to = Angle(b.Into);
                double gap = to - from;
                if (gap <= 0)
                {
                    gap += 2 * Math.PI;
                }
                // An empty angle wider than half a turn is a corner's outside; the walls' interior angle is the rest.
                int interior = (int)Math.Round(360 - gap * 180 / Math.PI);
                if (gap <= Math.PI + 1e-3 || interior % 30 != 0)
                {
                    continue;
                }
                double height = Math.Min(a.Height, b.Height);
                int grade = Math.Max(a.Wall.Grade, b.Wall.Grade);
                string mesh = $"corner-{interior}-{(int)Math.Round(height * 100)}";
                if (_assets.Corner(mesh, grade) == null)
                {
                    continue;
                }
                double outward = from + gap / 2;
                var point = new Vector3D(x / 1000.0, y / 1000.0, z / 1000.0);
                Transform3D transform = Place(frame, point, new Vector3D(Math.Cos(outward), 0, Math.Sin(outward)));
                var key = (mesh, grade);
                if (!_wanted.TryGetValue(key, out List<Transform3D>? list))
                {
                    _wanted[key] = list = new List<Transform3D>();
                }
                list.Add(transform);
                Count++;
            }
        }
        foreach (((string mesh, int grade) key, List<Transform3D> transforms) in _wanted)
        {
            if (!_batches.TryGetValue(key, out MultiMeshInstance3D? batch))
            {
                batch = new MultiMeshInstance3D
                {
                    Name = $"{key.mesh} g{key.grade}",
                    Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = _assets.Corner(key.mesh, key.grade) },
                    MaterialOverride = _assets.Grade(key.grade),
                };
                _parent.AddChild(batch);
                _batches[key] = batch;
            }
            batch.Multimesh.InstanceCount = transforms.Count;
            for (int i = 0; i < transforms.Count; i++)
            {
                batch.Multimesh.SetInstanceTransform(i, transforms[i]);
            }
            batch.Visible = !hidden && transforms.Count > 0;
        }
    }

    // A wall's direction as an angle about the frame's up (x toward z).
    private static double Angle(Vector3D d) => Math.Atan2(d.Z, d.X);

    private static long Mm(double v) => (long)Math.Round(v * 1000);

    // The post's frame: the corner's point, the notch's middle along +Z, the structure's up along +Y.
    private static Transform3D Place(in BuildingFrame frame, Vector3D point, Vector3D outward)
    {
        Vector3D origin = frame.ToZone(point);
        Vector3D z = frame.DirectionToZone(outward), y = frame.DirectionToZone(Vector3D.UnitY);
        Vector3D x = Vector3D.Cross(y, z);
        var basis = new Basis(new Vector3((float)x.X, (float)x.Y, (float)x.Z), new Vector3((float)y.X, (float)y.Y, (float)y.Z),
            new Vector3((float)z.X, (float)z.Y, (float)z.Z));
        return new Transform3D(basis, new Vector3((float)origin.X, (float)origin.Y, (float)origin.Z));
    }
}
