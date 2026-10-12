using System;
using System.Collections.Generic;
using Godot;

namespace HollowUniverse.Surface.Flora;

/// <summary>
/// A small 2D vector painter for the foliage atlas (presentation only): paths of lines and quadratic curves, filled
/// (even-odd, solid or with a colour function) or stroked with round joins, and circles; with a transform stack and
/// a clip rectangle, like the 2D canvas hollow-orbit painted its atlas with. Straight-alpha "source over" on float
/// pixels; draw at twice the final size and <see cref="Downsample"/> for anti-aliasing.
/// </summary>
public sealed class Painter
{
    private readonly float[] _px; // r, g, b, a
    private readonly Stack<Transform2D> _stack = new();
    private Transform2D _t = Transform2D.Identity;
    private Rect2I _clip;
    private readonly List<List<Vector2>> _path = new();

    public Painter(int width, int height)
    {
        Width = width;
        Height = height;
        _px = new float[width * height * 4];
        _clip = new Rect2I(0, 0, width, height);
    }

    public int Width { get; }
    public int Height { get; }
    public float[] Pixels => _px;

    // ---------------------------------------------------------------- state

    public void Save() => _stack.Push(_t);

    public void Restore()
    {
        _t = _stack.Pop();
    }

    public void Translate(float x, float y) => _t = _t * Transform2D.Identity.Translated(new Vector2(x, y));

    public void Rotate(float a) => _t = _t * Transform2D.Identity.Rotated(a);

    public void Scale(float s) => _t = _t * Transform2D.Identity.Scaled(new Vector2(s, s));

    public void ClipRect(int x, int y, int w, int h) => _clip = new Rect2I(x, y, w, h).Intersection(new Rect2I(0, 0, Width, Height));

    public void ResetClip() => _clip = new Rect2I(0, 0, Width, Height);

    private Vector2 Map(float x, float y) => _t * new Vector2(x, y);

    // ---------------------------------------------------------------- paths

    public void BeginPath() => _path.Clear();

    public void MoveTo(float x, float y) => _path.Add(new List<Vector2> { Map(x, y) });

    public void LineTo(float x, float y)
    {
        if (_path.Count == 0)
        {
            MoveTo(x, y);
            return;
        }
        _path[^1].Add(Map(x, y));
    }

    public void QuadraticCurveTo(float cx, float cy, float x, float y)
    {
        if (_path.Count == 0)
        {
            MoveTo(cx, cy);
        }
        List<Vector2> sub = _path[^1];
        Vector2 p0 = sub[^1], c = Map(cx, cy), p1 = Map(x, y);
        float length = p0.DistanceTo(c) + c.DistanceTo(p1);
        int steps = Math.Clamp((int)(length / 3), 2, 24);
        for (int i = 1; i <= steps; i++)
        {
            float t = (float)i / steps, u = 1 - t;
            sub.Add(p0 * (u * u) + c * (2 * u * t) + p1 * (t * t));
        }
    }

    public void ClosePath()
    {
        if (_path.Count > 0 && _path[^1].Count > 0)
        {
            _path[^1].Add(_path[^1][0]);
        }
    }

    /// <summary>Fills the current path (even-odd) with a colour.</summary>
    public void Fill(Color colour) => Fill((_, _) => colour);

    /// <summary>Fills the current path with a colour that depends on the device position.</summary>
    public void Fill(Func<float, float, Color> shade)
    {
        var edges = new List<(Vector2 A, Vector2 B)>();
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (List<Vector2> sub in _path)
        {
            for (int i = 0; i < sub.Count; i++)
            {
                Vector2 a = sub[i], b = sub[(i + 1) % sub.Count];
                if (a.Y != b.Y)
                {
                    edges.Add((a, b));
                }
                minY = Math.Min(minY, a.Y);
                maxY = Math.Max(maxY, a.Y);
            }
        }
        if (edges.Count == 0)
        {
            return;
        }
        int y0 = Math.Max(_clip.Position.Y, (int)MathF.Floor(minY)), y1 = Math.Min(_clip.End.Y - 1, (int)MathF.Ceiling(maxY));
        var xs = new List<float>(16);
        for (int y = y0; y <= y1; y++)
        {
            float sy = y + 0.5f;
            xs.Clear();
            foreach ((Vector2 a, Vector2 b) in edges)
            {
                if ((a.Y <= sy && b.Y > sy) || (b.Y <= sy && a.Y > sy))
                {
                    xs.Add(a.X + (sy - a.Y) / (b.Y - a.Y) * (b.X - a.X));
                }
            }
            xs.Sort();
            for (int k = 0; k + 1 < xs.Count; k += 2)
            {
                int x0 = Math.Max(_clip.Position.X, (int)MathF.Ceiling(xs[k] - 0.5f));
                int x1 = Math.Min(_clip.End.X - 1, (int)MathF.Floor(xs[k + 1] - 0.5f));
                for (int x = x0; x <= x1; x++)
                {
                    Blend(x, y, shade(x + 0.5f, sy));
                }
            }
        }
    }

    /// <summary>Strokes the current path with round joins and caps.</summary>
    public void Stroke(Color colour, float width)
    {
        float w = width * _t.X.Length();
        var saved = new List<List<Vector2>>(_path);
        foreach (List<Vector2> sub in saved)
        {
            for (int i = 0; i + 1 < sub.Count; i++)
            {
                Segment(sub[i], sub[i + 1], w, colour);
            }
        }
        _path.Clear();
        _path.AddRange(saved);
    }

    // A thick segment as a quad plus a round cap at its end, in device coordinates.
    private void Segment(Vector2 a, Vector2 b, float width, Color colour)
    {
        Vector2 d = b - a;
        float len = d.Length();
        if (len < 1e-4f)
        {
            return;
        }
        Vector2 n = new Vector2(-d.Y, d.X) / len * (width * 0.5f);
        Transform2D saved = _t;
        _t = Transform2D.Identity;
        var path = new List<List<Vector2>>(_path);
        _path.Clear();
        _path.Add(new List<Vector2> { a + n, b + n, b - n, a - n });
        Fill(colour);
        if (width > 2.5f)
        {
            DeviceCircle(b, width * 0.5f, (_, _) => colour);
        }
        _path.Clear();
        _path.AddRange(path);
        _t = saved;
    }

    /// <summary>A straight stroke from one point to another.</summary>
    public void Line(float x0, float y0, float x1, float y1, float width, Color colour)
    {
        BeginPath();
        MoveTo(x0, y0);
        LineTo(x1, y1);
        Stroke(colour, width);
        BeginPath();
    }

    /// <summary>A filled circle; <paramref name="shade"/> gets the distance from the centre over the radius (0–1).</summary>
    public void Circle(float cx, float cy, float r, Func<float, Color> shade)
    {
        Vector2 c = Map(cx, cy);
        float rd = r * _t.X.Length();
        DeviceCircle(c, rd, (x, y) => shade(Math.Min(1, new Vector2(x, y).DistanceTo(c) / rd)));
    }

    private void DeviceCircle(Vector2 c, float r, Func<float, float, Color> shade)
    {
        int x0 = Math.Max(_clip.Position.X, (int)MathF.Floor(c.X - r)), x1 = Math.Min(_clip.End.X - 1, (int)MathF.Ceiling(c.X + r));
        int y0 = Math.Max(_clip.Position.Y, (int)MathF.Floor(c.Y - r)), y1 = Math.Min(_clip.End.Y - 1, (int)MathF.Ceiling(c.Y + r));
        float r2 = r * r;
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                float dx = x + 0.5f - c.X, dy = y + 0.5f - c.Y;
                if (dx * dx + dy * dy <= r2)
                {
                    Blend(x, y, shade(x + 0.5f, y + 0.5f));
                }
            }
        }
    }

    /// <summary>Fills an ellipse (device-aligned axes after the transform's translation).</summary>
    public void Ellipse(float cx, float cy, float rx, float ry, Color colour)
    {
        BeginPath();
        const int Steps = 20;
        for (int i = 0; i <= Steps; i++)
        {
            float a = i * MathF.Tau / Steps;
            if (i == 0)
            {
                MoveTo(cx + rx * MathF.Cos(a), cy + ry * MathF.Sin(a));
            }
            else
            {
                LineTo(cx + rx * MathF.Cos(a), cy + ry * MathF.Sin(a));
            }
        }
        Fill(colour);
        BeginPath();
    }

    private void Blend(int x, int y, Color c)
    {
        int k = (y * Width + x) * 4;
        float a = Math.Clamp(c.A, 0, 1);
        if (a <= 0)
        {
            return;
        }
        float da = _px[k + 3];
        float oa = a + da * (1 - a);
        if (oa <= 0)
        {
            return;
        }
        _px[k] = (c.R * a + _px[k] * da * (1 - a)) / oa;
        _px[k + 1] = (c.G * a + _px[k + 1] * da * (1 - a)) / oa;
        _px[k + 2] = (c.B * a + _px[k + 2] * da * (1 - a)) / oa;
        _px[k + 3] = oa;
    }

    /// <summary>Halves the resolution with a box filter (colours weighted by alpha, so edges don't darken).</summary>
    public static float[] Downsample(float[] src, int width, int height)
    {
        int w = width / 2, h = height / 2;
        var dst = new float[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float r = 0, g = 0, b = 0, a = 0;
                for (int j = 0; j < 2; j++)
                {
                    for (int i = 0; i < 2; i++)
                    {
                        int k = ((2 * y + j) * width + 2 * x + i) * 4;
                        float sa = src[k + 3];
                        r += src[k] * sa;
                        g += src[k + 1] * sa;
                        b += src[k + 2] * sa;
                        a += sa;
                    }
                }
                int o = (y * w + x) * 4;
                if (a > 0)
                {
                    dst[o] = r / a;
                    dst[o + 1] = g / a;
                    dst[o + 2] = b / a;
                }
                dst[o + 3] = a / 4;
            }
        }
        return dst;
    }
}
