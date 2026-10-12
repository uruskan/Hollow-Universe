using System;
using System.Collections.Generic;
using Godot;
using Hollow.Core;

namespace HollowUniverse.Surface.Flora;

/// <summary>
/// Builds plant and rock meshes in code (presentation only), after hollow-orbit's flora: trunks and limbs as tubes
/// with parallel-transport frames, conifer whorls of drooping branch cards, broadleaf canopies of leaf-cluster cards,
/// bushes with berries, ferns, flowers and boulders. Every vertex carries an ambient-occlusion grey in its colour and
/// a wind weight pair (bend, flutter) in CUSTOM0; foliage normals point out from the crown, so cards light like a
/// rounded volume. Unit scale, the foot at the origin, Y up. Deterministic per seed.
/// </summary>
public sealed class GeometryBuilder
{
    public const int Bark = 0;
    public const int Leaves = 1;
    public const int Berries = 2;

    private sealed class Part
    {
        public readonly List<Vector3> P = new();
        public readonly List<Vector3> N = new();
        public readonly List<Vector2> T = new();
        public readonly List<Color> C = new();
        public readonly List<float> W = new();
        public readonly List<int> I = new();
    }

    private readonly Part[] _parts = { new(), new(), new() };
    private Part _cur;

    public GeometryBuilder()
    {
        _cur = _parts[0];
    }

    public void Mat(int m) => _cur = _parts[m];

    public int V(Vector3 p, Vector3 n, float u, float v, Vector3 c, Vector2 w)
    {
        _cur.P.Add(p);
        _cur.N.Add(n);
        _cur.T.Add(new Vector2(u, v));
        _cur.C.Add(new Color(c.X, c.Y, c.Z));
        _cur.W.Add(w.X);
        _cur.W.Add(w.Y);
        return _cur.P.Count - 1;
    }

    public void Tri(int a, int b, int c)
    {
        _cur.I.Add(a);
        _cur.I.Add(b);
        _cur.I.Add(c);
    }

    public void Quad(int a, int b, int c, int d)
    {
        Tri(a, b, c);
        Tri(a, c, d);
    }

    public int Triangles(int part) => _parts[part].I.Count / 3;

    /// <summary>Height of the top above the foot, and the side of a square (centred at half that height) that holds it all.</summary>
    public (float Height, float Extent) Size()
    {
        float top = 0, radius = 0, bottom = 0;
        foreach (Part part in _parts)
        {
            foreach (Vector3 p in part.P)
            {
                top = Math.Max(top, p.Y);
                bottom = Math.Min(bottom, p.Y);
                radius = Math.Max(radius, new Vector2(p.X, p.Z).Length());
            }
        }
        float extent = Math.Max(Math.Max(top, 2 * radius), 2 * Math.Max(top / 2 - bottom, top / 2));
        return (top, extent * 1.04f);
    }

    /// <summary>The mesh: a surface per part that has triangles, with the given materials (null for parts not used).</summary>
    public ArrayMesh Build(Material?[] materials)
    {
        var mesh = new ArrayMesh();
        for (int m = 0; m < _parts.Length; m++)
        {
            Part part = _parts[m];
            if (part.I.Count == 0)
            {
                continue;
            }
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = part.P.ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = part.N.ToArray();
            arrays[(int)Mesh.ArrayType.TexUV] = part.T.ToArray();
            arrays[(int)Mesh.ArrayType.Color] = part.C.ToArray();
            arrays[(int)Mesh.ArrayType.Custom0] = part.W.ToArray();
            // Godot draws clockwise faces as fronts; these were built counter-clockwise.
            var indices = new int[part.I.Count];
            for (int t = 0; t < indices.Length; t += 3)
            {
                indices[t] = part.I[t];
                indices[t + 1] = part.I[t + 2];
                indices[t + 2] = part.I[t + 1];
            }
            arrays[(int)Mesh.ArrayType.Index] = indices;
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, null, null,
                (Mesh.ArrayFormat)((int)Mesh.ArrayCustomFormat.RgFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift));
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, materials.Length > m ? materials[m] : null);
        }
        return mesh;
    }

    // ---------------------------------------------------------------- primitives

    /// <summary>A tube along a polyline with parallel-transport frames; returns its length.</summary>
    public float Tube(IReadOnlyList<Vector3> pts, IReadOnlyList<float> radii, int sides, float vScale,
        Func<float, Vector3> colour, Func<float, Vector3, Vector2> wind, bool cap = false, Func<float, float, int, float>? rough = null)
    {
        int n = pts.Count;
        var tangents = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            Vector3 t = (i < n - 1 ? pts[i + 1] - pts[i] : pts[i] - pts[i - 1]).Normalized();
            if (i > 0 && i < n - 1)
            {
                t = (t + (pts[i] - pts[i - 1]).Normalized()).Normalized();
            }
            tangents[i] = t;
        }
        Vector3 nrm = Math.Abs(tangents[0].Y) < 0.9f ? Vector3.Up.Cross(tangents[0]).Normalized() : Vector3.Right.Cross(tangents[0]).Normalized();
        var frames = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            if (i > 0)
            {
                var q = new Quaternion(tangents[i - 1], tangents[i]);
                nrm = (q * nrm).Normalized();
            }
            frames[i] = nrm;
        }
        float len = 0;
        var rows = new List<int[]>();
        for (int i = 0; i < n; i++)
        {
            if (i > 0)
            {
                len += pts[i].DistanceTo(pts[i - 1]);
            }
            float t = (float)i / (n - 1);
            Vector3 b = tangents[i].Cross(frames[i]).Normalized();
            var row = new int[sides + 1];
            for (int s = 0; s <= sides; s++)
            {
                float a = (float)s / sides * MathF.Tau;
                Vector3 dir = frames[i] * MathF.Cos(a) + b * MathF.Sin(a);
                float rm = rough?.Invoke(t, a, s) ?? 1;
                Vector3 p = pts[i] + dir * (radii[i] * rm);
                row[s] = V(p, dir, (float)s / sides, len / vScale, colour(t), wind(t, p));
            }
            rows.Add(row);
        }
        for (int i = 0; i < n - 1; i++)
        {
            for (int s = 0; s < sides; s++)
            {
                Quad(rows[i][s], rows[i][s + 1], rows[i + 1][s + 1], rows[i + 1][s]);
            }
        }
        if (cap)
        {
            int i = n - 1;
            Vector3 tip = pts[i] + tangents[i] * (radii[i] * 0.8f);
            int ti = V(tip, tangents[i], 0.5f, (len + radii[i]) / vScale, colour(1), wind(1, tip));
            for (int s = 0; s < sides; s++)
            {
                Tri(rows[i][s], rows[i][s + 1], ti);
            }
        }
        return len;
    }

    /// <summary>
    /// A bent card from <paramref name="o"/> along <paramref name="u"/> (texture u), half-width <paramref name="w"/>
    /// (texture v), drooping by <paramref name="droop"/> metres at the tip.
    /// </summary>
    public void Card(Vector3 o, Vector3 u, Vector3 w, int segs, float droop, Vector4 uv, Func<Vector3, float, Vector3> normal,
        Func<float, Vector3> colour, Func<float, Vector3, Vector2> wind, Vector3? curl = null)
    {
        var rows = new int[segs + 1, 2];
        for (int i = 0; i <= segs; i++)
        {
            float t = (float)i / segs;
            Vector3 c = o + u * t;
            c.Y -= droop * t * t;
            if (curl.HasValue)
            {
                c += curl.Value * (t * t);
            }
            for (int k = 0; k < 2; k++)
            {
                int side = k == 0 ? -1 : 1;
                Vector3 p = c + w * side;
                float uu = uv.X + (uv.Z - uv.X) * t, vv = uv.Y + (uv.W - uv.Y) * k;
                rows[i, k] = V(p, normal(p, t), uu, vv, colour(t), wind(t, p));
            }
        }
        for (int i = 0; i < segs; i++)
        {
            Quad(rows[i, 0], rows[i + 1, 0], rows[i + 1, 1], rows[i, 1]);
        }
    }

    /// <summary>A card whose texture runs bottom to top (grass, fern, flower): t along <paramref name="up"/> maps to v.</summary>
    public void VCard(Vector3 o, Vector3 up, Vector3 w, int segs, float droop, Vector4 uv, Func<Vector3, float, Vector3> normal,
        Func<float, Vector3> colour, Func<float, Vector3, Vector2> wind, Vector3? curl = null)
    {
        var rows = new int[segs + 1, 2];
        for (int i = 0; i <= segs; i++)
        {
            float t = (float)i / segs;
            Vector3 c = o + up * t;
            c.Y -= droop * t * t;
            if (curl.HasValue)
            {
                c += curl.Value * (t * t);
            }
            for (int k = 0; k < 2; k++)
            {
                int side = k == 0 ? -1 : 1;
                Vector3 p = c + w * side;
                rows[i, k] = V(p, normal(p, t), uv.X + (uv.Z - uv.X) * k, uv.Y + (uv.W - uv.Y) * t, colour(t), wind(t, p));
            }
        }
        for (int i = 0; i < segs; i++)
        {
            Quad(rows[i, 0], rows[i, 1], rows[i + 1, 1], rows[i + 1, 0]);
        }
    }

    /// <summary>Appends triangles (positions, normals) transformed, with colour and wind per vertex.</summary>
    public void Add(Vector3[] positions, Vector3[] normals, int[] indices, Transform3D m, Func<Vector3, Vector3, Vector3> colour, Vector2 wind)
    {
        int b = _cur.P.Count;
        Basis nm = m.Basis.Inverse().Transposed();
        for (int i = 0; i < positions.Length; i++)
        {
            Vector3 p = m * positions[i];
            V(p, (nm * normals[i]).Normalized(), 0, 0, colour(p, positions[i]), wind);
        }
        foreach (int i in indices)
        {
            _cur.I.Add(b + i);
        }
    }
}

/// <summary>The plant and rock generators.</summary>
public static class FloraGeometry
{
    private static Vector3 V(float x, float y, float z) => new(x, y, z);
    private static Vector3 Grey(float g) => new(g, g, g);
    private static float Rad(float deg) => deg * MathF.PI / 180;

    public sealed class Conifer
    {
        public int Seed;
        public float H = 13, RBase = 0.3f, CrownStart = 0.1f, LenMax = 3, LenMin = 0.35f, Droop = 0.32f, Pitch = -14, WidthK = 0.5f, Shape = 1;
        public int Whorls = 14, PerWhorl = 7;
        public (int X, int Y) Cell = FloraTextures.Spruce;
        public Vector3 BarkLow = V(0.44f, 0.36f, 0.3f), BarkHigh = V(0.55f, 0.42f, 0.33f);
    }

    /// <summary>Conifer: a tapering trunk, whorls of drooping branch cards (a flat one and a near-vertical one each), a top tuft.</summary>
    public static GeometryBuilder ConiferTree(Conifer o, bool lod)
    {
        var gb = new GeometryBuilder();
        var r = new Rng(Hash64.Of((ulong)o.Seed, 0UL)); // both levels share the stream: same lean, same first whorls
        float R() => (float)r.NextDouble();
        float h = o.H;
        gb.Mat(GeometryBuilder.Bark);
        int segs = lod ? 3 : 8;
        var bend = V((R() - 0.5f) * 0.4f, 0, (R() - 0.5f) * 0.4f);
        var pts = new List<Vector3>();
        var rad = new List<float>();
        for (int i = 0; i <= segs; i++)
        {
            float t = (float)i / segs;
            pts.Add(V(bend.X * t * t, -0.35f + (h + 0.35f) * t, bend.Z * t * t));
            float rr = o.RBase * MathF.Pow(1 - t, 0.85f) + 0.03f;
            if (t < 0.08f)
            {
                rr *= 1 + (0.08f - t) * 6;
            }
            rad.Add(rr);
        }
        gb.Tube(pts, rad, lod ? 5 : 9, 2.2f, t =>
        {
            Vector3 c = o.BarkLow.Lerp(o.BarkHigh, t);
            float ao = 0.65f + 0.35f * Math.Min(1, t * 3);
            return c * ao;
        }, (t, _) => new Vector2(t * t, 0));
        gb.Mat(GeometryBuilder.Leaves);
        Vector4 uv = FloraTextures.CellUv(lod && o.Cell == FloraTextures.Pine ? FloraTextures.PineFar : o.Cell);
        float crown0 = h * o.CrownStart;
        int whorls = lod ? Math.Max(6, (int)(o.Whorls * 0.7f)) : o.Whorls;
        int per = lod ? Math.Max(4, o.PerWhorl - 1) : o.PerWhorl;
        float az = R() * MathF.Tau;
        for (int w = 0; w < whorls; w++)
        {
            float tw = (float)w / (whorls - 1);
            float y = crown0 + (h * 0.985f - crown0) * MathF.Pow(tw, 0.92f);
            float len = (o.LenMin + (o.LenMax - o.LenMin) * MathF.Pow(1 - tw, o.Shape)) * (0.85f + 0.3f * R());
            int k = per + (R() < 0.5f ? 1 : 0) - (tw > 0.92f ? 1 : 0);
            for (int b = 0; b < k; b++)
            {
                az += MathF.Tau / k + (R() - 0.5f) * 0.5f;
                float ty = bend.X * MathF.Pow(y / h, 2), tz = bend.Z * MathF.Pow(y / h, 2);
                var basePt = V(ty, y, tz);
                float pitch = Rad(o.Pitch + (R() - 0.5f) * 14) * (1 - tw * 0.5f) + (tw > 0.8f ? Rad(25) : 0);
                var dir = V(MathF.Cos(az) * MathF.Cos(pitch), MathF.Sin(pitch), MathF.Sin(az) * MathF.Cos(pitch));
                Vector3 u = dir * len;
                var side = V(-MathF.Sin(az), 0, MathF.Cos(az));
                float width = len * o.WidthK + 0.25f;
                float roll = (R() - 0.5f) * 0.5f;
                Vector3 wv = side * (width * MathF.Cos(roll)) + V(0, width * MathF.Sin(roll), 0);
                float droop = len * o.Droop * (0.6f + 0.8f * R());
                var radial = V(MathF.Cos(az), 0, MathF.Sin(az));
                float yy = y;
                Vector3 Normal(Vector3 p, float t)
                {
                    Vector3 outward = V(p.X - ty, (p.Y - (crown0 + h) * 0.5f) * 0.35f, p.Z - tz).Normalized();
                    return (outward * 0.75f + V(0, 0.55f, 0)).Normalized();
                }
                Vector3 Colour(float t)
                {
                    float ao = (0.42f + 0.58f * MathF.Pow(t, 0.7f)) * (0.72f + 0.28f * tw);
                    return Grey(ao);
                }
                Vector2 Wind(float t, Vector3 p) => new(MathF.Pow(p.Y / h, 2) + 0.15f * t, 0.35f + 0.65f * t);
                int segsC = lod ? 1 : 2;
                gb.Card(basePt - radial * 0.05f, u, wv, segsC, droop, uv, Normal, Colour, Wind);
                // A second, near-vertical card for volume: the crown reads from the side too.
                Vector3 wv2 = V(0, width * 0.75f, 0) + side * (width * 0.25f);
                gb.Card(basePt - radial * 0.05f, u * 0.88f, wv2, segsC, droop * 0.8f, uv, Normal, Colour, Wind);
                _ = yy;
            }
        }
        // The top: a few small whorls of short branches turned upward, then a short leader shoot covered in needles,
        // so the crown closes into a full point instead of a bare spike.
        int tops = lod ? 2 : 4;
        for (int w = 0; w < tops; w++)
        {
            float tw = (w + 1f) / (tops + 1);
            float y = h * (0.9f + 0.075f * tw);
            float len = (0.95f - 0.55f * tw) * (0.85f + 0.3f * R()) * Math.Max(o.LenMax / 3f, 0.7f);
            int k = lod ? 3 : 5;
            for (int b = 0; b < k; b++)
            {
                az += MathF.Tau / k + (R() - 0.5f) * 0.6f;
                float ty = bend.X * MathF.Pow(y / h, 2), tz = bend.Z * MathF.Pow(y / h, 2);
                float pitch = Rad(28 + R() * 18);
                var dir = V(MathF.Cos(az) * MathF.Cos(pitch), MathF.Sin(pitch), MathF.Sin(az) * MathF.Cos(pitch));
                var side = V(-MathF.Sin(az), 0, MathF.Cos(az));
                float width = len * o.WidthK + 0.2f;
                Vector3 TopNormal(Vector3 p, float t) => (V(p.X - ty, 0.4f, p.Z - tz).Normalized() * 0.7f + V(0, 0.6f, 0)).Normalized();
                gb.Card(V(ty, y, tz), dir * len, side * width, 1, len * 0.12f, uv, TopNormal, t => Grey(0.7f + 0.3f * t),
                    (t, p) => new Vector2(MathF.Pow(p.Y / h, 2) + 0.15f * t, 0.4f + 0.6f * t));
            }
        }
        int leader = lod ? 2 : 3;
        for (int k = 0; k < leader; k++)
        {
            float a = (float)k / leader * MathF.PI + R() * 0.3f;
            var basePt = V(bend.X, h * 0.955f, bend.Z);
            var u = V(MathF.Cos(a) * 0.04f, 0.55f + 0.04f * h, MathF.Sin(a) * 0.04f);
            var wv = V(-MathF.Sin(a) * 0.2f, 0, MathF.Cos(a) * 0.2f);
            gb.Card(basePt, u, wv, 1, 0, uv, (_, _) => V(MathF.Cos(a), 0.8f, MathF.Sin(a)).Normalized(), t => Grey(0.75f + 0.25f * t),
                (_, _) => new Vector2(1, 0.6f));
        }
        return gb;
    }

    public sealed class Broadleaf
    {
        public int Seed;
        public float H = 11, RBase = 0.42f, TrunkH = 0.4f, ClusterSize = 3, CanopyR = 4.4f, CanopyH = 3.1f, CanopyY = 0.7f;
        public int Limbs = 5, Clusters = 30;
        public (int X, int Y) CellA = FloraTextures.Broad, CellB = FloraTextures.OakDark;
        public Vector3 BarkColour = V(0.55f, 0.47f, 0.4f);
        public bool Birch;
    }

    /// <summary>Broadleaf tree: a trunk, limbs to the canopy with sub-branches, leaf-cluster cards over the canopy's shell.</summary>
    public static GeometryBuilder BroadleafTree(Broadleaf o, bool lod)
    {
        var gb = new GeometryBuilder();
        var r = new Rng(Hash64.Of((ulong)o.Seed, lod ? 1UL : 0UL));
        float R() => (float)r.NextDouble();
        float h = o.H;
        gb.Mat(GeometryBuilder.Bark);
        var lean = V((R() - 0.5f) * 0.5f, 0, (R() - 0.5f) * 0.5f);
        float th = h * o.TrunkH;
        var tp = new List<Vector3>();
        var tr = new List<float>();
        int ts = lod ? 2 : 5;
        for (int i = 0; i <= ts; i++)
        {
            float t = (float)i / ts;
            tp.Add(V(lean.X * t, -0.3f + (th + 0.3f) * t, lean.Z * t));
            float rr = o.RBase * (1 - 0.35f * t);
            if (t < 0.12f)
            {
                rr *= 1 + (0.12f - t) * 4;
            }
            tr.Add(rr);
        }
        Vector3 BarkCol(float t)
        {
            float ao = 0.6f + 0.4f * Math.Min(1, t * 2.5f);
            return o.BarkColour * ao;
        }
        gb.Tube(tp, tr, lod ? 5 : 9, 2.0f, BarkCol, (t, _) => new Vector2(0.05f * t, 0));
        Vector3 top = tp[^1];
        var cc = V(lean.X, h * o.CanopyY, lean.Z);
        var ends = new List<Vector3>();
        int limbs = lod ? Math.Min(3, o.Limbs) : o.Limbs;
        for (int l = 0; l < limbs; l++)
        {
            float a = (float)l / limbs * MathF.Tau + R() * 0.8f;
            float outR = o.CanopyR * (0.45f + 0.35f * R());
            var end = V(cc.X + MathF.Cos(a) * outR, cc.Y + o.CanopyH * (0.1f + 0.4f * R()), cc.Z + MathF.Sin(a) * outR);
            Vector3 mid = top.Lerp(end, 0.45f) + V(0, 0.6f, 0);
            var pts = new List<Vector3> { top, mid, end };
            var rr = new List<float> { o.RBase * 0.62f, o.RBase * 0.38f, o.RBase * 0.12f };
            if (!lod)
            {
                pts.Insert(2, mid.Lerp(end, 0.55f) + V((R() - 0.5f) * 0.4f, 0.3f, (R() - 0.5f) * 0.4f));
                rr.Insert(2, o.RBase * 0.24f);
            }
            gb.Tube(pts, rr, lod ? 4 : 6, 2.0f, t => BarkCol(0.6f + 0.4f * t), (_, p) => new Vector2(MathF.Pow(Math.Clamp(p.Y / h, 0, 1), 2) * 0.8f, 0));
            ends.Add(end);
            if (!lod)
            {
                for (int s = 0; s < 2; s++)
                {
                    Vector3 st = mid.Lerp(end, 0.3f + 0.4f * R());
                    float sa = a + (R() - 0.5f) * 2.0f;
                    Vector3 se = st + V(MathF.Cos(sa) * 1.6f, 0.8f + R() * 0.8f, MathF.Sin(sa) * 1.6f);
                    gb.Tube(new[] { st, se }, new[] { o.RBase * 0.14f, o.RBase * 0.04f }, 4, 2.0f, _ => BarkCol(0.9f),
                        (_, p) => new Vector2(MathF.Pow(Math.Clamp(p.Y / h, 0, 1), 2) * 0.9f, 0));
                    ends.Add(se);
                }
            }
        }
        gb.Mat(GeometryBuilder.Leaves);
        int clusters = lod ? Math.Max(8, (int)MathF.Round(o.Clusters * 0.55f)) : o.Clusters;
        Vector4 uvA = FloraTextures.CellUv(o.CellA), uvB = FloraTextures.CellUv(o.CellB);
        for (int k = 0; k < clusters; k++)
        {
            Vector3 p;
            if (k < ends.Count && !lod)
            {
                p = ends[k];
            }
            else
            {
                float u = R() * 2 - 1, a = R() * MathF.Tau, s = MathF.Sqrt(1 - u * u), rr = 0.55f + 0.45f * MathF.Cbrt(R());
                p = V(cc.X + MathF.Cos(a) * s * o.CanopyR * rr, cc.Y + u * o.CanopyH * rr, cc.Z + MathF.Sin(a) * s * o.CanopyR * rr);
            }
            float size = (lod ? 1.22f : 1.0f) * o.ClusterSize * (0.8f + 0.4f * R());
            int nq = lod ? 2 : 3;
            Vector4 uvr = R() < 0.5f ? uvA : uvB;
            for (int q = 0; q < nq; q++)
            {
                Vector3 ax = V(R() - 0.5f, (R() - 0.5f) * 0.6f, R() - 0.5f).Normalized();
                Vector3 bx = V(R() - 0.5f, R() - 0.5f, R() - 0.5f).Cross(ax).Normalized();
                if (q == 0)
                {
                    bx = V(R() - 0.5f, 0, R() - 0.5f).Normalized();
                    ax = V(-bx.Z, (R() - 0.5f) * 0.4f, bx.X).Normalized();
                }
                Vector3 o0 = p - ax * (size / 2);
                Vector3 Normal(Vector3 pp, float t)
                {
                    var d = V((pp.X - cc.X) / o.CanopyR, (pp.Y - cc.Y) / o.CanopyH, (pp.Z - cc.Z) / o.CanopyR);
                    return (d.Normalized() * 0.85f + V(0, 0.35f, 0)).Normalized();
                }
                Vector3 pc = p;
                Vector3 Colour(float t)
                {
                    float d = V((pc.X - cc.X) / o.CanopyR, (pc.Y - cc.Y) / o.CanopyH, (pc.Z - cc.Z) / o.CanopyR).Length();
                    float ao = Math.Clamp(0.38f + 0.62f * d * (0.75f + 0.25f * ((pc.Y - cc.Y) / o.CanopyH + 1) / 2), 0.3f, 1.05f);
                    return Grey(ao);
                }
                float flutter = 0.6f + 0.4f * R();
                gb.Card(o0, ax * size, bx * (size / 2), 1, o.Birch ? size * 0.12f : 0, uvr, Normal, Colour,
                    (_, pp) => new Vector2(MathF.Pow(Math.Clamp(pp.Y / h, 0, 1), 2) * 0.9f, flutter));
            }
        }
        return gb;
    }

    public sealed class Shrub
    {
        public int Seed;
        public float R = 1, H = 1.25f, Size = 1.1f;
        public int Cards = 13;
        public (int X, int Y) Cell = FloraTextures.Bush;
        public Vector3? Berries;
    }

    /// <summary>Bush: a few stems, leaf cards over a dome, and (for berry bushes) clusters of round berries.</summary>
    public static GeometryBuilder Bush(Shrub o, bool lod)
    {
        var gb = new GeometryBuilder();
        var r = new Rng(Hash64.Of((ulong)o.Seed, lod ? 1UL : 0UL));
        float R() => (float)r.NextDouble();
        gb.Mat(GeometryBuilder.Bark);
        if (!lod)
        {
            for (int k = 0; k < 4; k++)
            {
                float a = R() * 6.28f;
                gb.Tube(new[] { V(0, -0.1f, 0), V(MathF.Cos(a) * 0.3f * o.R, o.H * 0.5f, MathF.Sin(a) * 0.3f * o.R) }, new[] { 0.04f, 0.015f }, 3, 1,
                    _ => V(0.25f, 0.2f, 0.15f), (_, _) => Vector2.Zero);
            }
        }
        gb.Mat(GeometryBuilder.Leaves);
        Vector4 uv = FloraTextures.CellUv(o.Cell);
        int n = lod ? Math.Max(4, o.Cards / 3) : o.Cards;
        for (int k = 0; k < n; k++)
        {
            float u = R() * 0.85f + 0.1f, a = R() * MathF.Tau;
            var c = V(MathF.Cos(a) * MathF.Sqrt(1 - u * u) * o.R * 0.65f, o.H * (0.25f + 0.55f * u), MathF.Sin(a) * MathF.Sqrt(1 - u * u) * o.R * 0.65f);
            float size = o.Size * (0.75f + 0.5f * R()) * (lod ? 1.4f : 1);
            Vector3 ax = V(R() - 0.5f, (R() - 0.5f) * 0.7f, R() - 0.5f).Normalized();
            Vector3 bx = V(R() - 0.5f, R() - 0.5f, R() - 0.5f).Cross(ax).Normalized();
            float uu = u;
            gb.Card(c - ax * (size / 2), ax * size, bx * (size / 2), 1, 0, uv,
                (p, _) => (V(p.X / o.R, (p.Y - o.H * 0.3f) / o.H, p.Z / o.R).Normalized() * 0.8f + V(0, 0.4f, 0)).Normalized(),
                _ => Grey(Math.Clamp(0.45f + 0.6f * uu, 0.4f, 1.05f)), (_, p) => new Vector2(Math.Clamp(p.Y / o.H, 0, 1) * 0.5f, 0.8f));
        }
        if (o.Berries.HasValue)
        {
            gb.Mat(GeometryBuilder.Berries);
            (Vector3[] sp, Vector3[] sn, int[] si) = Icosphere(lod ? 0 : 1);
            int nb = lod ? 10 : 34;
            for (int k = 0; k < nb; k++)
            {
                float u = R() * 0.8f + 0.15f, a = R() * MathF.Tau;
                var c = V(MathF.Cos(a) * MathF.Sqrt(1 - u * u) * o.R * 0.82f, o.H * (0.25f + 0.62f * u), MathF.Sin(a) * MathF.Sqrt(1 - u * u) * o.R * 0.82f);
                int cl = 3 + (int)(R() * 3);
                for (int j = 0; j < cl; j++)
                {
                    float s = (lod ? 0.05f : 0.032f) + 0.012f * R();
                    Vector3 p = c + V((R() - 0.5f) * 0.12f, (R() - 0.5f) * 0.12f, (R() - 0.5f) * 0.12f);
                    float shade = 0.8f + 0.3f * R();
                    Vector3 col = o.Berries.Value * shade;
                    gb.Add(sp, sn, si, new Transform3D(Basis.Identity.Scaled(V(s, s, s)), p), (_, _) => col, new Vector2(0.3f, 0.3f));
                }
            }
        }
        return gb;
    }

    /// <summary>Fern: fronds radiating from the centre, arching over and drooping at the tips.</summary>
    public static GeometryBuilder Fern(int seed, bool lod)
    {
        var gb = new GeometryBuilder();
        var r = new Rng(Hash64.Of((ulong)seed, lod ? 1UL : 0UL));
        float R() => (float)r.NextDouble();
        gb.Mat(GeometryBuilder.Leaves);
        Vector4 uv = FloraTextures.CellUv(FloraTextures.Fern, 6);
        int n = lod ? 4 : 7;
        for (int k = 0; k < n; k++)
        {
            float a = (float)k / n * MathF.Tau + R() * 0.5f;
            float len = 0.7f + 0.35f * R();
            var outward = V(MathF.Cos(a), 0, MathF.Sin(a));
            Vector3 up = outward * (len * 0.75f) + V(0, len * 0.75f, 0);
            Vector3 side = V(-MathF.Sin(a), 0, MathF.Cos(a)) * (0.22f * len);
            gb.VCard(Vector3.Zero, up, side, 3, len * 0.55f, uv, (_, _) => (outward * 0.3f + Vector3.Up).Normalized(),
                t => Grey(0.5f + 0.55f * t), (t, _) => new Vector2(t * t * 0.6f, 0.6f * t));
        }
        return gb;
    }

    /// <summary>Flower: two crossed cards of the flower cell (white petals, tinted per instance).</summary>
    public static GeometryBuilder Flower(int seed)
    {
        var gb = new GeometryBuilder();
        gb.Mat(GeometryBuilder.Leaves);
        Vector4 uv = FloraTextures.CellUv(FloraTextures.Flower, 6);
        float turn = seed * 0.7f;
        for (int k = 0; k < 2; k++)
        {
            float a = turn + k * MathF.PI / 2;
            gb.VCard(Vector3.Zero, V(0, 0.42f, 0), V(MathF.Cos(a) * 0.14f, 0, MathF.Sin(a) * 0.14f), 1, 0, uv, (_, _) => Vector3.Up,
                t => Grey(0.7f + 0.3f * t), (t, _) => new Vector2(t * t, 0.3f));
        }
        return gb;
    }

    /// <summary>Boulder: an icosphere pushed out by noise, cut by planes into facets, flattened where it sits.</summary>
    public static GeometryBuilder Boulder(int seed, int detail, float sx = 1, float sy = 0.72f, float sz = 1, float noise = 0.28f)
    {
        var gb = new GeometryBuilder();
        var r = new Rng(Hash64.Of((ulong)seed, 0x524F434B)); // "ROCK"
        float R() => (float)r.NextDouble();
        (Vector3[] p, _, int[] idx) = Icosphere(detail);
        var planes = new List<(Vector3, float)>();
        for (int i = 0; i < 6; i++)
        {
            planes.Add((V(R() - 0.5f, (R() - 0.3f) * 0.8f, R() - 0.5f).Normalized(), 0.55f + R() * 0.3f));
        }
        for (int i = 0; i < p.Length; i++)
        {
            Vector3 v = p[i];
            float n = (float)NoiseTextures.Fbm(v.X * 0.35 + 0.5 + (seed % 997) * 0.13, v.Y * 0.35 + v.Z * 0.21 + 0.5, 4, 3, 0.5, seed) * noise;
            v *= 1 + n;
            foreach ((Vector3 pn, float d) in planes)
            {
                float dd = v.Dot(pn);
                if (dd > d)
                {
                    v -= pn * ((dd - d) * 0.9f);
                }
            }
            v = V(v.X * sx, v.Y * sy, v.Z * sz);
            if (v.Y < -0.25f * sy)
            {
                v.Y = -0.25f * sy + (v.Y + 0.25f * sy) * 0.25f;
            }
            p[i] = v + V(0, 0.25f * sy, 0);
        }
        // Flat-ish normals from the faces, averaged per vertex.
        var normals = new Vector3[p.Length];
        for (int t = 0; t < idx.Length; t += 3)
        {
            Vector3 fn = (p[idx[t + 1]] - p[idx[t]]).Cross(p[idx[t + 2]] - p[idx[t]]);
            normals[idx[t]] += fn;
            normals[idx[t + 1]] += fn;
            normals[idx[t + 2]] += fn;
        }
        for (int i = 0; i < normals.Length; i++)
        {
            normals[i] = normals[i].Normalized();
        }
        gb.Mat(GeometryBuilder.Bark);
        gb.Add(p, normals, idx, Transform3D.Identity, (pp, _) => Grey(Math.Clamp(0.45f + pp.Y * 0.5f, 0.3f, 1.0f)), Vector2.Zero);
        return gb;
    }

    /// <summary>A unit icosphere: each icosahedron face split into (detail + 1)² triangles, vertices shared.</summary>
    public static (Vector3[] Positions, Vector3[] Normals, int[] Indices) Icosphere(int detail)
    {
        float t = (1 + MathF.Sqrt(5)) / 2;
        Vector3[] v =
        {
            V(-1, t, 0), V(1, t, 0), V(-1, -t, 0), V(1, -t, 0), V(0, -1, t), V(0, 1, t),
            V(0, -1, -t), V(0, 1, -t), V(t, 0, -1), V(t, 0, 1), V(-t, 0, -1), V(-t, 0, 1),
        };
        int[] f =
        {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
        };
        var positions = new List<Vector3>();
        var lookup = new Dictionary<(int, int, int), int>();
        var indices = new List<int>();
        int n = detail + 1;
        int Vertex(Vector3 p)
        {
            Vector3 q = p.Normalized();
            var key = ((int)MathF.Round(q.X * 1e5f), (int)MathF.Round(q.Y * 1e5f), (int)MathF.Round(q.Z * 1e5f));
            if (!lookup.TryGetValue(key, out int i))
            {
                i = positions.Count;
                positions.Add(q);
                lookup[key] = i;
            }
            return i;
        }
        for (int k = 0; k < f.Length; k += 3)
        {
            Vector3 a = v[f[k]], b = v[f[k + 1]], c = v[f[k + 2]];
            var grid = new int[n + 1][];
            for (int i = 0; i <= n; i++)
            {
                grid[i] = new int[n - i + 1];
                for (int j = 0; j <= n - i; j++)
                {
                    grid[i][j] = Vertex(a + (b - a) * ((float)i / n) + (c - a) * ((float)j / n));
                }
            }
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n - i; j++)
                {
                    indices.Add(grid[i][j]);
                    indices.Add(grid[i + 1][j]);
                    indices.Add(grid[i][j + 1]);
                    if (j < n - i - 1)
                    {
                        indices.Add(grid[i + 1][j]);
                        indices.Add(grid[i + 1][j + 1]);
                        indices.Add(grid[i][j + 1]);
                    }
                }
            }
        }
        Vector3[] pos = positions.ToArray();
        return (pos, (Vector3[])pos.Clone(), indices.ToArray());
    }
}
