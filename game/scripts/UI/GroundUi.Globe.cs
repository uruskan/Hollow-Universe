using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;
using Hollow.Client.Map;
using Hollow.Core;
using Hollow.Surface;
using HollowUniverse.Common;
using HollowUniverse.UI.Model;
using Num = System.Numerics;
using G = HollowUniverse.Common.Ui.Ground;

namespace HollowUniverse.UI;

// The map control (docs/WHOLE_WORLD.md §11), one for the G screen and the death screen: the whole world as a globe seen
// from afar that becomes a flat chart as it zooms in (GlobeProjection), from 4.8 km across down to 150 m. A canvas
// shader draws the picture from its six cube faces (rendered on a worker from the surface, then kept on disk); over it
// the overlay draws, crisp at any scale, the grid (faces A to F, 25 × 25 cells a face, "C K12"), the screen's markers,
// the cells' names (placed by the Labeller after the markers' names), a compass whose click puts north up, and a scale.
// Each screen keeps its own Globe: where it looks (GlobeCamera) and its drag. Dragging turns the globe, the wheel zooms
// about the pointer, following keeps the player in the middle with local north up.
public partial class GroundUi
{
    private static readonly Color MapBackdrop = new(0.095f, 0.10f, 0.10f);
    private static readonly Color MapBlank = new(0.78f, 0.72f, 0.58f);
    private static readonly Color GridInk = new(0.16f, 0.13f, 0.09f, 0.38f);
    private static readonly Color FaceInk = new(0.16f, 0.13f, 0.09f, 0.75f);
    private static readonly Color LabelInk = new(0.16f, 0.13f, 0.09f, 0.72f);
    private static readonly Dictionary<string, Texture2DArray> GlobeTextures = new();
    private static readonly Dictionary<string, Task<Image[]?>> GlobeRenders = new();
    private static readonly HashSet<string> GlobeFailed = new();

    /// <summary>One screen's map: where it looks, where it is drawn, its drag, and whether a click puts a marker down.</summary>
    private sealed class Globe
    {
        public Globe(bool marks, double across)
        {
            Marks = marks;
            Across = across;
        }

        public bool Marks { get; }

        /// <summary>How close it starts, in metres across its shorter side.</summary>
        public double Across { get; }

        public GlobeCamera? Camera;
        public Rect2 Area;
        public Vector2? Press;
        public Vector2 Last;
        public bool Dragging;

        /// <summary>What the view was last put on (the death screen's death).</summary>
        public object? Focus;

        public float ShortSide => Math.Max(1, Math.Min(Area.Size.X, Area.Size.Y));

        public GlobeProjection Projection => Camera!.Projection(ShortSide);

        public Num.Vector2 Offset(Vector2 screen) => new(screen.X - Area.GetCenter().X, screen.Y - Area.GetCenter().Y);
    }

    private readonly Globe _mapGlobe = new(marks: true, across: 1200);
    private readonly Globe _deathGlobe = new(marks: false, across: 900);
    private Painter _globePicture = null!;
    private ShaderMaterial _globeMaterial = null!;

    /// <summary>Whether the map's picture is ready (or there is none): screenshots of the map wait for it.</summary>
    public bool MapReady => Model.Map == null || GlobePicture(Model.Map) != null || GlobeFailed.Contains(PictureKey(Model.Map));

    /// <summary>For checks: the G screen's map on screen, whether it follows the player, and the grid cell under a point of it.</summary>
    public Rect2 MapScreenArea => MapArea();

    public bool MapFollowing => _mapGlobe.Camera?.Following ?? true;

    public string? MapCellAt(Vector2 screen) =>
        Model.Map is { } view && _mapGlobe.Camera != null && _mapGlobe.Projection.ToDirection(_mapGlobe.Offset(screen), out Vector3D d)
            ? WorldGrid.Name(d, view.Cells) : null;

    private double? _zoomTo;

    /// <summary>Zooms the shown map to a width, in metres across its shorter side (the viewer's and the tour's pictures).</summary>
    public void ZoomMapTo(double metresAcross) => _zoomTo = metresAcross;

    /// <summary>The map shown now: the G screen's or the death screen's.</summary>
    private Globe? ActiveGlobe => Screen switch
    {
        GroundScreen.Map => _mapGlobe,
        GroundScreen.Death => _deathGlobe,
        _ => null,
    };

    // The picture's layer: under the overlay, over the canvas (which paints the screen's backdrop and panels).
    private void CreateGlobe()
    {
        _globeMaterial = new ShaderMaterial { Shader = new Shader { Code = GlobeShader } };
        _globePicture = new Painter { Name = "Globe", Material = _globeMaterial, Paint = PaintGlobePicture, Visible = false };
        AddChild(_globePicture);
    }

    /// <summary>Every frame: the shown map follows, settles and hands its view to the shader.</summary>
    private void UpdateGlobe(double delta)
    {
        Globe? g = ActiveGlobe;
        MapView? view = Model.Map;
        _globePicture.Visible = g != null && view != null;
        if (g == null || view == null)
        {
            return;
        }
        g.Area = Screen == GroundScreen.Map ? MapArea() : DeathMapArea();
        if (g.Camera == null || Math.Abs(g.Camera.Radius - view.Radius) > 1e-6)
        {
            g.Camera = new GlobeCamera(view.Radius, Vector3D.UnitY, g.Across);
        }
        if (g == _mapGlobe)
        {
            foreach (MapMarkerView m in Model.MapMarkers)
            {
                if (m.Kind == MapMarkerKind.Me)
                {
                    g.Camera.Follow(Direction(m.At));
                }
            }
        }
        else if (Model.Death is { } death && !ReferenceEquals(death, g.Focus))
        {
            // A new death: the map on the body, north up, close in.
            g.Focus = death;
            g.Camera.LookAt(Direction(death.At));
            g.Camera.Across = g.Across;
            g.Camera.Following = false;
        }
        if (_zoomTo is { } across)
        {
            g.Camera.Across = across;
            _zoomTo = null;
        }
        g.Camera.Settle(delta);

        GlobeProjection p = g.Projection;
        Texture2DArray? picture = GlobePicture(view);
        _globeMaterial.SetShaderParameter("ready", picture != null);
        if (picture != null)
        {
            _globeMaterial.SetShaderParameter("faces", picture);
        }
        _globeMaterial.SetShaderParameter("centre_px", g.Area.GetCenter());
        _globeMaterial.SetShaderParameter("view_centre", ToGodot(p.Centre));
        _globeMaterial.SetShaderParameter("view_right", ToGodot(p.Right));
        _globeMaterial.SetShaderParameter("view_up", ToGodot(p.Up));
        _globeMaterial.SetShaderParameter("radius_px", (float)(p.Radius * p.PixelsPerMetre));
        _globeMaterial.SetShaderParameter("bend", (float)p.Bend);
        _globeMaterial.SetShaderParameter("reach", (float)p.Reach);
        _globeMaterial.SetShaderParameter("texels_per_radian", (float)(view.Pixels / (Math.PI / 2)));
        _globeMaterial.SetShaderParameter("backdrop", MapBackdrop);
        _globeMaterial.SetShaderParameter("blank", MapBlank);
    }

    private void PaintGlobePicture(Painter painter)
    {
        if (ActiveGlobe is { Camera: not null } g)
        {
            painter.DrawRect(g.Area, Colors.White);
        }
    }

    /// <summary>The G screen's map: a square as large as the window allows beside the panel.</summary>
    private Rect2 MapArea()
    {
        float margin = Ui.Px(24), panel = Ui.Px(420);
        float side = MathF.Floor(MathF.Min(Size.Y - 2 * margin - Ui.Px(36), Size.X - panel - 3 * margin));
        return new Rect2(MathF.Floor((Size.X - panel - margin - side) / 2), margin, side, side);
    }

    /// <summary>The death screen's map: the whole width between its top and bottom bars.</summary>
    private Rect2 DeathMapArea()
    {
        float top = Ui.Px(124), bottom = Size.Y - Ui.Px(106);
        return new Rect2(0, top, Size.X, bottom - top);
    }

    // ---------------------------------------------------------------- the picture: rendered, kept on disk, loaded

    private static string PictureKey(MapView view) => $"{view.CacheKey}-{view.Pixels}";

    /// <summary>The map's six faces as a texture array, once ready (rendered on a worker the first time, then read from disk).</summary>
    private static Texture2DArray? GlobePicture(MapView? view)
    {
        if (view == null)
        {
            return null;
        }
        string key = PictureKey(view);
        if (GlobeTextures.TryGetValue(key, out Texture2DArray? texture))
        {
            return texture;
        }
        if (GlobeFailed.Contains(key))
        {
            return null;
        }
        if (!GlobeRenders.TryGetValue(key, out Task<Image[]?>? render))
        {
            string path = ProjectSettings.GlobalizePath($"user://map-cache/{key}.png");
            render = Task.Run(() => LoadOrRender(view, path));
            GlobeRenders[key] = render;
        }
        if (!render.IsCompleted)
        {
            return null;
        }
        GlobeRenders.Remove(key);
        if (render.IsCompletedSuccessfully && render.Result is { } faces)
        {
            var array = new Texture2DArray();
            if (array.CreateFromImages(new Godot.Collections.Array<Image>(faces)) == Error.Ok)
            {
                return GlobeTextures[key] = array;
            }
        }
        GD.PushWarning($"[map] the picture {key} could not be made");
        GlobeFailed.Add(key);
        return null;
    }

    // On a worker: the strip of six faces from the disk, or rendered from the surface and saved; then each face with its
    // mipmaps (the shader picks the level from the projection's scale, so seams between faces don't flicker).
    private static Image[]? LoadOrRender(MapView view, string path)
    {
        int size = view.Pixels;
        Image? strip = null;
        if (File.Exists(path))
        {
            strip = Image.LoadFromFile(path);
            if (strip == null || strip.IsEmpty() || strip.GetWidth() != size || strip.GetHeight() != CubeSphere.FaceCount * size)
            {
                strip = null;
            }
            else
            {
                strip.Convert(Image.Format.Rgba8);
            }
        }
        if (strip == null)
        {
            byte[] rgba = view.Render();
            if (rgba.Length != size * size * CubeSphere.FaceCount * 4)
            {
                return null;
            }
            strip = Image.CreateFromData(size, CubeSphere.FaceCount * size, false, Image.Format.Rgba8, rgba);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            strip.SavePng(path);
        }
        var faces = new Image[CubeSphere.FaceCount];
        for (int f = 0; f < faces.Length; f++)
        {
            faces[f] = strip.GetRegion(new Rect2I(0, f * size, size, size));
            faces[f].GenerateMipmaps();
        }
        return faces;
    }

    // ---------------------------------------------------------------- the overlay

    /// <summary>The overlay's frame: the projection this frame, the labeller with everything outside the map taken.</summary>
    private readonly record struct GlobeFrame(Globe Globe, GlobeProjection Projection, Labeller Labeller, int Cells);

    // Starts the overlay: the grid's lines under everything else.
    private GlobeFrame BeginGlobe(Globe g, MapView view)
    {
        var labeller = new Labeller(Size);
        Rect2 a = g.Area;
        labeller.Block(new Rect2(0, 0, Size.X, a.Position.Y));
        labeller.Block(new Rect2(a.End.X, 0, Size.X - a.End.X, Size.Y));
        labeller.Block(new Rect2(0, a.End.Y, Size.X, Size.Y - a.End.Y));
        labeller.Block(new Rect2(0, 0, a.Position.X, Size.Y));
        var frame = new GlobeFrame(g, g.Projection, labeller, view.Cells);
        ProjectGrid(frame);
        PaintGridLines(frame);
        return frame;
    }

    // Ends the overlay: the cells' names where they fit, the faces' letters from afar, the compass and the scale.
    private void EndGlobe(in GlobeFrame f, MapView view)
    {
        PaintGridNames(f);
        PaintCompass(f);
        PaintScale(f);
        if (GlobePicture(view) == null)
        {
            _o = f.Globe.Area.Position;
            float w = f.Globe.Area.Size.X / Ui.Scale;
            TextIn(GlobeFailed.Contains(PictureKey(view)) ? "NO MAP PICTURE" : "DRAWING THE MAP…", 0, f.Globe.Area.Size.Y / Ui.Scale / 2 - 20, w, 30,
                LabelInk, HorizontalAlignment.Center, bold: true, condensed: true);
        }
    }

    /// <summary>A point on the map's screen, or null when it is hidden or outside the map's area.</summary>
    private static Vector2? OnGlobe(in GlobeFrame f, Num.Vector3 at, float inset = 0)
    {
        if (!f.Projection.ToScreen(Direction(at), out Num.Vector2 o))
        {
            return null;
        }
        Vector2 screen = f.Globe.Area.GetCenter() + new Vector2(o.X, o.Y);
        return f.Globe.Area.Grow(-inset).HasPoint(screen) ? screen.Round() : null;
    }

    /// <summary>Local north on screen at a point (unit, y down); straight up where north has no meaning.</summary>
    private static Vector2 NorthOnGlobe(in GlobeFrame f, Num.Vector3 at) =>
        f.Projection.NorthAt(Direction(at)) is { } n ? new Vector2(n.X, n.Y) : new Vector2(0, -1);

    // The grid's points (half a cell apart along every face) on screen this frame, for the faces that can show.
    private Num.Vector3[]?[] _gridDirections = Array.Empty<Num.Vector3[]?>();
    private int _gridCells;
    private readonly Vector2[][] _gridScreen = new Vector2[CubeSphere.FaceCount][];
    private readonly bool[][] _gridShown = new bool[CubeSphere.FaceCount][];
    private readonly bool[] _faceNear = new bool[CubeSphere.FaceCount];

    private int GridSide => 2 * _gridCells + 1;

    private void ProjectGrid(in GlobeFrame f)
    {
        int cells = f.Cells;
        if (_gridCells != cells || _gridDirections.Length != CubeSphere.FaceCount)
        {
            _gridCells = cells;
            _gridDirections = new Num.Vector3[]?[CubeSphere.FaceCount];
            for (int face = 0; face < CubeSphere.FaceCount; face++)
            {
                var points = new Num.Vector3[GridSide * GridSide];
                for (int a = 0; a < GridSide; a++)
                {
                    for (int b = 0; b < GridSide; b++)
                    {
                        Vector3D d = WorldGrid.At(face, a * 0.5, b * 0.5, cells);
                        points[a * GridSide + b] = new Num.Vector3((float)d.X, (float)d.Y, (float)d.Z);
                    }
                }
                _gridDirections[face] = points;
                _gridScreen[face] = new Vector2[points.Length];
                _gridShown[face] = new bool[points.Length];
            }
        }
        // A face shows when some of it can reach the area: its nearest point (the face spans 55° from its middle) is
        // within the reach and, on screen, within the area's half-diagonal (the projection draws at least 2/π of the arc).
        GlobeProjection p = f.Projection;
        Vector2 centre = f.Globe.Area.GetCenter();
        double halfDiagonal = f.Globe.Area.Size.Length() / 2;
        for (int face = 0; face < CubeSphere.FaceCount; face++)
        {
            double nearest = Math.Max(0, DMath.Acos(Math.Clamp(Vector3D.Dot(CubeSphere.Normal(face), p.Centre), -1, 1)) - 0.96);
            _faceNear[face] = nearest <= p.Reach && p.Radius * p.PixelsPerMetre * nearest * (2 / Math.PI) <= halfDiagonal;
            if (!_faceNear[face])
            {
                continue;
            }
            Num.Vector3[] points = _gridDirections[face]!;
            Vector2[] screen = _gridScreen[face];
            bool[] shown = _gridShown[face];
            for (int i = 0; i < points.Length; i++)
            {
                shown[i] = p.ToScreen(Direction(points[i]), out Num.Vector2 o);
                screen[i] = centre + new Vector2(o.X, o.Y);
            }
        }
    }

    // Every column and row line of the faces that show, as polylines clipped to the map's area: the faces' edges darker,
    // the cells' lines fading out when cells are too small on screen to read.
    private void PaintGridLines(in GlobeFrame f)
    {
        float cell = CellPixels(f);
        float fade = Math.Clamp((cell - Ui.Pxf(14)) / Ui.Pxf(26), 0, 1);
        float width = Ui.Pxf(1.2f);
        var run = new List<Vector2>(GridSide);
        for (int face = 0; face < CubeSphere.FaceCount; face++)
        {
            if (!_faceNear[face])
            {
                continue;
            }
            for (int line = 0; line <= _gridCells; line++)
            {
                bool edge = line == 0 || line == _gridCells;
                Color ink = edge ? FaceInk : new Color(GridInk, GridInk.A * fade);
                if (ink.A <= 0.01f)
                {
                    continue;
                }
                for (int across = 0; across < 2; across++)
                {
                    run.Clear();
                    for (int t = 0; t < GridSide; t++)
                    {
                        // A column line runs down its rows; a row line across its columns.
                        int k = across == 0 ? 2 * line * GridSide + t : t * GridSide + 2 * line;
                        int next = t + 1 < GridSide ? (across == 0 ? k + 1 : k + GridSide) : -1;
                        if (next < 0 || !_gridShown[face][k] || !_gridShown[face][next])
                        {
                            Flush(run, ink, width);
                            continue;
                        }
                        Vector2 a = _gridScreen[face][k], b = _gridScreen[face][next];
                        if (!Clip(f.Globe.Area, ref a, ref b))
                        {
                            Flush(run, ink, width);
                            continue;
                        }
                        if (run.Count == 0 || run[^1].DistanceSquaredTo(a) > 0.01f)
                        {
                            Flush(run, ink, width);
                            run.Add(a);
                        }
                        run.Add(b);
                    }
                    Flush(run, ink, width);
                }
            }
        }
    }

    private void Flush(List<Vector2> run, Color ink, float width)
    {
        if (run.Count >= 2)
        {
            _p.DrawPolyline(run.ToArray(), ink, width, true);
        }
        run.Clear();
    }

    // A cell's side on screen at the map's middle, in pixels.
    private static float CellPixels(in GlobeFrame f) => (float)(f.Projection.PixelsPerMetre * TileKey.EdgeLength(0, f.Projection.Radius) / f.Cells);

    // The cells' names, Rust's way, in each cell's top-left corner on screen ("K12"; "C K12" once there is room), where
    // the Labeller finds room after the markers; from afar, each face's letter in its middle.
    private void PaintGridNames(in GlobeFrame f)
    {
        float cell = CellPixels(f);
        Rect2 inside = f.Globe.Area.Grow(-Ui.Px(4));
        if (cell < Ui.Pxf(46))
        {
            FontFile big = G.Font(40, true, true);
            int px = Ui.Px(40);
            for (int face = 0; face < CubeSphere.FaceCount; face++)
            {
                // A face's letter in its middle, unless that is near the globe's edge, where it would be squeezed or cut.
                int middle = _gridCells * GridSide + _gridCells;
                double fromCentre = DMath.Acos(Math.Clamp(Vector3D.Dot(CubeSphere.Normal(face), f.Projection.Centre), -1, 1));
                if (!_faceNear[face] || !_gridShown[face][middle] || !inside.HasPoint(_gridScreen[face][middle]) ||
                    fromCentre > f.Projection.Reach - 0.25)
                {
                    continue;
                }
                string letter = WorldGrid.FaceLetter(face).ToString();
                Vector2 size = big.GetStringSize(letter, HorizontalAlignment.Left, -1, px);
                Vector2 at = (_gridScreen[face][middle] - new Vector2(size.X / 2, -big.GetAscent(px) / 2 + Ui.Px(4))).Round();
                if (f.Labeller.TryPlace(new Rect2(at - new Vector2(0, big.GetAscent(px)), size)))
                {
                    _p.DrawString(big, at, letter, HorizontalAlignment.Left, -1, px, new Color(LabelInk, 0.55f));
                }
            }
            return;
        }
        bool full = cell >= Ui.Pxf(80);
        FontFile font = G.Font(13, true, false);
        int size13 = Ui.Px(13);
        float ascent = font.GetAscent(size13);
        for (int face = 0; face < CubeSphere.FaceCount; face++)
        {
            if (!_faceNear[face])
            {
                continue;
            }
            for (int column = 0; column < _gridCells; column++)
            {
                for (int row = 0; row < _gridCells; row++)
                {
                    int middle = (2 * column + 1) * GridSide + 2 * row + 1;
                    if (!_gridShown[face][middle] || !f.Globe.Area.HasPoint(_gridScreen[face][middle]))
                    {
                        continue;
                    }
                    // The corner nearest the screen's top left.
                    Vector2 corner = new(float.MaxValue, float.MaxValue);
                    for (int c = 0; c < 4; c++)
                    {
                        int k = 2 * (column + (c & 1)) * GridSide + 2 * (row + (c >> 1));
                        if (_gridShown[face][k] && _gridScreen[face][k].X + _gridScreen[face][k].Y < corner.X + corner.Y)
                        {
                            corner = _gridScreen[face][k];
                        }
                    }
                    if (corner.X == float.MaxValue)
                    {
                        continue;
                    }
                    string name = full ? WorldGrid.Name(new WorldGrid.Cell(face, column, row)) : WorldGrid.CellName(column, row);
                    // Inward from the corner, towards the cell's middle.
                    Vector2 inward = (_gridScreen[face][middle] - corner).Normalized();
                    Vector2 at = (corner + inward * Ui.Pxf(6) + new Vector2(Ui.Px(1), ascent - Ui.Px(2))).Round();
                    float w = font.GetStringSize(name, HorizontalAlignment.Left, -1, size13).X;
                    var bounds = new Rect2(at - new Vector2(0, ascent), new Vector2(w, font.GetHeight(size13)));
                    if (inside.Encloses(bounds) && f.Labeller.TryPlace(bounds))
                    {
                        _p.DrawString(font, at, name, HorizontalAlignment.Left, -1, size13, LabelInk);
                    }
                }
            }
        }
    }

    // The compass in the map's top-right corner: its needle points to local north at the middle; a click puts north up.
    private void PaintCompass(in GlobeFrame f)
    {
        float r = Ui.Pxf(17);
        Vector2 at = new Vector2(f.Globe.Area.End.X - Ui.Px(18) - r, f.Globe.Area.Position.Y + Ui.Px(32) + r).Round();
        Vector2 north = f.Projection.NorthAt(f.Projection.Centre) is { } n ? new Vector2(n.X, n.Y) : new Vector2(0, -1);
        Vector2 side = new(-north.Y, north.X);
        _p.DrawCircle(at, r + Ui.Pxf(1.5f), new Color(0.08f, 0.07f, 0.06f, 0.85f), true, -1, true);
        _p.DrawCircle(at, r, new Color(0.18f, 0.17f, 0.15f, 0.9f), true, -1, true);
        _p.DrawColoredPolygon(new[] { at + north * r * 0.8f, at + side * r * 0.28f, at - side * r * 0.28f }, G.Red);
        _p.DrawColoredPolygon(new[] { at - north * r * 0.8f, at - side * r * 0.28f, at + side * r * 0.28f }, G.Ink);
        FontFile font = G.Font(12, true, false);
        int px = Ui.Px(12);
        Vector2 letter = at + north * (r + Ui.Pxf(9));
        float w = font.GetStringSize("N", HorizontalAlignment.Left, -1, px).X;
        Vector2 baseline = (letter + new Vector2(-w / 2, font.GetAscent(px) * 0.36f)).Round();
        // Outlined, so it reads on pale land as on the sea.
        _p.DrawStringOutline(font, baseline, "N", HorizontalAlignment.Left, -1, px, Ui.Px(4), new Color(0.08f, 0.07f, 0.06f, 0.9f));
        _p.DrawString(font, baseline, "N", HorizontalAlignment.Left, -1, px, G.Ink);
        f.Labeller.Block(at, r + Ui.Pxf(14));
        Globe g = f.Globe;
        _hits.Add(new Hit(new Rect2(at - new Vector2(r, r), new Vector2(2 * r, 2 * r)), HitKind.Button, () => g.Camera?.NorthUp()));
    }

    // A scale bar in the map's bottom-left corner, true at the middle: a round length between 70 and 180 pixels.
    private void PaintScale(in GlobeFrame f)
    {
        double perMetre = f.Projection.PixelsPerMetre;
        double metres = 10;
        foreach (double m in new double[] { 10, 20, 50, 100, 200, 500, 1000, 2000 })
        {
            metres = m;
            if (m * perMetre >= Ui.Pxf(70))
            {
                break;
            }
        }
        float length = (float)(metres * perMetre);
        if (length > Ui.Pxf(240))
        {
            return;
        }
        Vector2 left = new Vector2(f.Globe.Area.Position.X + Ui.Px(16), f.Globe.Area.End.Y - Ui.Px(18)).Round();
        Color shade = new(0.08f, 0.07f, 0.06f, 0.75f);
        _p.DrawRect(new Rect2(left.X - Ui.Px(6), left.Y - Ui.Px(22), length + Ui.Px(12) + Ui.Px(58), Ui.Px(30)), shade);
        _p.DrawRect(new Rect2(left.X, left.Y - Ui.Px(3), MathF.Round(length), Ui.Px(3)), G.Ink);
        _p.DrawRect(new Rect2(left.X, left.Y - Ui.Px(9), Ui.Px(2), Ui.Px(9)), G.Ink);
        _p.DrawRect(new Rect2(left.X + MathF.Round(length) - Ui.Px(2), left.Y - Ui.Px(9), Ui.Px(2), Ui.Px(9)), G.Ink);
        FontFile font = G.Font(13, true, false);
        string label = metres >= 1000 ? $"{metres / 1000:0} km" : $"{metres:0} m";
        _p.DrawString(font, new Vector2(left.X + MathF.Round(length) + Ui.Px(8), left.Y), label, HorizontalAlignment.Left, -1, Ui.Px(13), G.Ink);
        f.Labeller.Block(new Rect2(left.X - Ui.Px(6), left.Y - Ui.Px(22), length + Ui.Px(70), Ui.Px(30)));
    }

    // Clips a segment to a rectangle (Liang–Barsky); false when none of it is inside.
    private static bool Clip(Rect2 r, ref Vector2 a, ref Vector2 b)
    {
        float t0 = 0, t1 = 1;
        Vector2 d = b - a;
        ReadOnlySpan<float> p = stackalloc[] { -d.X, d.X, -d.Y, d.Y };
        ReadOnlySpan<float> q = stackalloc[] { a.X - r.Position.X, r.End.X - a.X, a.Y - r.Position.Y, r.End.Y - a.Y };
        for (int i = 0; i < 4; i++)
        {
            if (Math.Abs(p[i]) < 1e-9f)
            {
                if (q[i] < 0)
                {
                    return false;
                }
                continue;
            }
            float t = q[i] / p[i];
            if (p[i] < 0)
            {
                t0 = Math.Max(t0, t);
            }
            else
            {
                t1 = Math.Min(t1, t);
            }
            if (t0 > t1)
            {
                return false;
            }
        }
        Vector2 start = a;
        a = start + d * t0;
        b = start + d * t1;
        return true;
    }

    // ---------------------------------------------------------------- input (from GroundUi's Press, Release, motion and wheel)

    private void PressGlobe(MouseButton button)
    {
        if (ActiveGlobe is not { Camera: not null } g)
        {
            return;
        }
        if (button == MouseButton.Left)
        {
            g.Press = Pointer;
            g.Last = Pointer;
            g.Dragging = false;
            return;
        }
        if (button == MouseButton.Right && g.Marks && Model.Map is { } view)
        {
            // The marker nearest the pointer, within its disc, comes off.
            GlobeProjection p = g.Projection;
            float best = Ui.Pxf(16);
            int index = -1;
            foreach (MapMarkerView m in Model.MapMarkers)
            {
                if (m.Kind != MapMarkerKind.Marker || !p.ToScreen(Direction(m.At), out Num.Vector2 o))
                {
                    continue;
                }
                float d = (g.Area.GetCenter() + new Vector2(o.X, o.Y)).DistanceTo(Pointer);
                if (d < best)
                {
                    best = d;
                    index = m.Index;
                }
            }
            if (index >= 0)
            {
                Model.RemoveMarker(index);
            }
        }
    }

    private void DragGlobe()
    {
        if (ActiveGlobe is not { Camera: not null, Press: Vector2 pressed } g)
        {
            return;
        }
        if (!g.Dragging && pressed.DistanceTo(Pointer) < Ui.Pxf(5))
        {
            return;
        }
        g.Dragging = true;
        g.Camera.Drag(g.Offset(g.Last), g.Offset(Pointer), g.ShortSide);
        g.Last = Pointer;
    }

    private void ReleaseGlobe(MouseButton button)
    {
        if (button != MouseButton.Left || ActiveGlobe is not { Camera: not null, Press: Vector2 } g)
        {
            return;
        }
        bool click = !g.Dragging;
        g.Press = null;
        g.Dragging = false;
        if (!click || !g.Marks || !g.Area.HasPoint(Pointer) || !g.Projection.ToDirection(g.Offset(Pointer), out Vector3D at))
        {
            return;
        }
        int count = 0;
        foreach (MapMarkerView m in Model.MapMarkers)
        {
            count += m.Kind == MapMarkerKind.Marker ? 1 : 0;
        }
        if (count < Model.MaxMarkers)
        {
            Model.PlaceMarker(new Num.Vector3((float)at.X, (float)at.Y, (float)at.Z), _markerColour, "");
        }
    }

    private bool ZoomGlobe(int steps)
    {
        if (ActiveGlobe is not { Camera: not null } g || !g.Area.HasPoint(Pointer))
        {
            return false;
        }
        g.Camera.Zoom(steps < 0 ? 0.8 : 1.25, g.Offset(Pointer), g.ShortSide);
        return true;
    }

    private static Vector3D Direction(Num.Vector3 at) => at.LengthSquared() > 1e-12f ? new Vector3D(at.X, at.Y, at.Z).Normalized : Vector3D.UnitY;

    private static Vector3 ToGodot(Vector3D v) => new((float)v.X, (float)v.Y, (float)v.Z);

    // The picture (docs/WHOLE_WORLD.md §11): each pixel read back through GlobeProjection's inverse, the same formula as
    // the overlay's: θ = asin(k·r)/k from the middle along its bearing, then the cube face and its warped (u, v) as
    // CubeSphere.FromDirection finds them. The mip level comes from the projection's own scale there, so the faces' seams
    // don't pick blurry levels. Beyond the world, the backdrop; a globe darkens a little towards its edge.
    private const string GlobeShader = @"
shader_type canvas_item;
render_mode unshaded;

uniform sampler2DArray faces : filter_linear_mipmap, repeat_disable;
uniform bool ready = false;
uniform vec2 centre_px;
uniform vec3 view_centre = vec3(1.0, 0.0, 0.0);
uniform vec3 view_right = vec3(0.0, 0.0, -1.0);
uniform vec3 view_up = vec3(0.0, 1.0, 0.0);
uniform float radius_px = 300.0;
uniform float bend = 1.0;
uniform float reach = 1.5707963;
uniform float texels_per_radian = 652.0;
uniform vec4 backdrop = vec4(0.095, 0.1, 0.1, 1.0);
uniform vec4 blank = vec4(0.78, 0.72, 0.58, 1.0);

varying vec2 local;

void vertex() {
    local = VERTEX;
}

// B-spline weights of a texel's four neighbours, for bicubic sampling from four bilinear taps.
vec4 cubic(float t) {
    vec4 n = vec4(1.0, 2.0, 3.0, 4.0) - t;
    vec4 s = n * n * n;
    float x = s.x;
    float y = s.y - 4.0 * s.x;
    float z = s.z - 4.0 * s.y + 6.0 * s.x;
    return vec4(x, y, z, 6.0 - x - y - z) * (1.0 / 6.0);
}

// The picture magnified: bicubic, so the texels' steps (a shore's inked line) don't show as stairs.
vec3 smooth_sample(vec3 uvw) {
    vec2 size = vec2(textureSize(faces, 0).xy);
    vec2 tc = uvw.xy * size - 0.5;
    vec2 f = fract(tc);
    tc -= f;
    vec4 xc = cubic(f.x);
    vec4 yc = cubic(f.y);
    vec4 c = tc.xxyy + vec2(-0.5, 1.5).xyxy;
    vec4 s = vec4(xc.xz + xc.yw, yc.xz + yc.yw);
    vec4 o = (c + vec4(xc.yw, yc.yw) / s) / size.xxyy;
    vec3 s0 = textureLod(faces, vec3(o.x, o.z, uvw.z), 0.0).rgb;
    vec3 s1 = textureLod(faces, vec3(o.y, o.z, uvw.z), 0.0).rgb;
    vec3 s2 = textureLod(faces, vec3(o.x, o.w, uvw.z), 0.0).rgb;
    vec3 s3 = textureLod(faces, vec3(o.y, o.w, uvw.z), 0.0).rgb;
    float sx = s.x / (s.x + s.y);
    float sy = s.z / (s.z + s.w);
    return mix(mix(s3, s2, sx), mix(s1, s0, sx), sy);
}

// CubeSphere.FromDirection: the face's (u, v) as texture coordinates, and the face.
vec3 cube(vec3 d) {
    vec3 a = abs(d);
    float face; float n; float x; float y;
    if (a.x >= a.y && a.x >= a.z) {
        if (d.x >= 0.0) { face = 0.0; n = d.x; x = -d.z; y = d.y; } else { face = 1.0; n = -d.x; x = d.z; y = d.y; }
    } else if (a.y >= a.z) {
        if (d.y >= 0.0) { face = 2.0; n = d.y; x = d.x; y = -d.z; } else { face = 3.0; n = -d.y; x = d.x; y = d.z; }
    } else {
        if (d.z >= 0.0) { face = 4.0; n = d.z; x = d.x; y = d.y; } else { face = 5.0; n = -d.z; x = -d.x; y = d.y; }
    }
    float u = atan(x / n) * (4.0 / PI);
    float v = atan(y / n) * (4.0 / PI);
    return vec3((u + 1.0) * 0.5, (1.0 - v) * 0.5, face);
}

void fragment() {
    vec2 o = local - centre_px;
    vec2 m = vec2(o.x, -o.y) / radius_px;
    float r = length(m);
    float s = r * bend;
    float theta = bend > 1e-6 ? asin(min(s, 1.0)) / bend : r;
    float edge = (bend > 1e-6 ? sin(bend * reach) / bend : reach) * radius_px - r * radius_px;
    if (s > 1.0 || theta > reach + 1e-4) {
        COLOR = backdrop;
    } else {
        vec3 along = r > 1e-7 ? (view_right * m.x + view_up * m.y) / r : vec3(0.0);
        vec3 d = normalize(view_centre * cos(theta) + along * sin(theta));
        float kt = bend * theta;
        float radial = max(cos(kt), 0.02);
        float across = theta > 1e-4 ? (bend > 1e-6 ? sin(kt) / bend : theta) / max(sin(theta), 1e-4) : 1.0;
        float lod = log2(max(texels_per_radian / (radius_px * sqrt(radial * max(across, 0.02))), 1.0));
        vec3 t = cube(d);
        vec3 colour = !ready ? blank.rgb : lod > 0.5 ? textureLod(faces, t, lod).rgb : smooth_sample(t);
        float globe = smoothstep(0.5, 1.0, bend);
        colour *= mix(1.0, 0.8 + 0.2 * cos(min(theta, 1.5707963)), globe);
        COLOR = vec4(mix(backdrop.rgb, colour, clamp(edge + 0.5, 0.0, 1.0)), 1.0);
    }
}";
}
