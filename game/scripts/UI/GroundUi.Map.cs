using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Godot;
using HollowUniverse.Common;
using HollowUniverse.UI.Model;
using G = HollowUniverse.Common.Ui.Ground;

namespace HollowUniverse.UI;

// The map (G7, GROUND.md §6.8), on G as in Rust: the zone's parchment map as large as the window allows, its grid
// lettered from the west and numbered from the north, the player and their facing, teammates with their names, the
// last death, sleeping bags and beds and the player's own markers (names placed by the Labeller, so they never overlap);
// beside it the grid cell the player stands in, the markers with their colours, and the team (members, invites, create,
// leave, promote, kick). A click on the map puts a marker down, a right-click on one takes it off; dragging pans,
// the wheel zooms. The death screen draws its map with the same painter (PaintMapPicture).
public partial class GroundUi
{
    /// <summary>The six marker colours (MapMarkerView.Colour), RustMVP's palette in kind.</summary>
    public static readonly Color[] MarkerColours =
    {
        new(0.86f, 0.30f, 0.22f), new(0.93f, 0.68f, 0.20f), new(0.55f, 0.75f, 0.30f), new(0.30f, 0.62f, 0.85f),
        new(0.70f, 0.45f, 0.85f), new(0.92f, 0.90f, 0.85f),
    };

    private static readonly Color MapBackdrop = new(0.095f, 0.10f, 0.10f);
    private static readonly Color GridInk = new(0.16f, 0.13f, 0.09f, 0.38f);
    private static readonly Color LabelInk = new(0.16f, 0.13f, 0.09f, 0.72f);
    private static readonly Dictionary<string, Texture2D> MapTextures = new();
    private static readonly Dictionary<string, Task<Image?>> MapRenders = new();

    private Vector2 _worldMapCentre = new(0.5f, 0.5f);
    private float _worldMapZoom = 2.5f;
    private bool _worldMapFollow = true;
    private Vector2? _worldMapPress;
    private Vector2 _worldMapPressCentre;
    private Rect2 _worldMapArea;
    private int _markerColour;

    /// <summary>Whether the model's map picture is ready (or there is none): screenshots of the map wait for it.</summary>
    public bool MapReady => Model.Map == null || MapTexture(Model.Map) != null;

    /// <summary>Opens the map (G), centred on the player.</summary>
    public void ToggleMap() => Open(Screen == GroundScreen.Map ? GroundScreen.Hud : GroundScreen.Map);

    /// <summary>The map picture for a view, once rendered (on a worker thread the first time, then from the disk cache).</summary>
    private static Texture2D? MapTexture(MapView? view)
    {
        if (view == null)
        {
            return null;
        }
        string key = $"{view.CacheKey}-{view.Pixels}";
        if (MapTextures.TryGetValue(key, out Texture2D? texture))
        {
            return texture;
        }
        string path = $"user://map-cache/{key}.png";
        if (!MapRenders.TryGetValue(key, out Task<Image?>? render))
        {
            if (FileAccess.FileExists(path) && Image.LoadFromFile(ProjectSettings.GlobalizePath(path)) is { } cached && !cached.IsEmpty())
            {
                return MapTextures[key] = ImageTexture.CreateFromImage(cached);
            }
            render = Task.Run(() =>
            {
                byte[] rgba = view.Render();
                return rgba.Length == view.Pixels * view.Pixels * 4 ? Image.CreateFromData(view.Pixels, view.Pixels, false, Image.Format.Rgba8, rgba) : null;
            });
            MapRenders[key] = render;
        }
        if (!render.IsCompleted)
        {
            return null;
        }
        MapRenders.Remove(key);
        if (render.IsCompletedSuccessfully && render.Result is { } image)
        {
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("user://map-cache"));
            image.SavePng(path);
            return MapTextures[key] = ImageTexture.CreateFromImage(image);
        }
        return null;
    }

    private void PaintMap()
    {
        _p.DrawRect(new Rect2(Vector2.Zero, Size), MapBackdrop);
        MapView? view = Model.Map;
        Texture2D? picture = MapTexture(view);
        float margin = Ui.Px(24), panel = Ui.Px(420);
        float side = MathF.Floor(MathF.Min(Size.Y - 2 * margin - Ui.Px(36), Size.X - panel - 3 * margin));
        var area = new Rect2(MathF.Floor((Size.X - panel - margin - side) / 2), margin, side, side);
        _worldMapArea = area;
        IReadOnlyList<MapMarkerView> markers = Model.MapMarkers;
        MapMarkerView? me = null;
        foreach (MapMarkerView m in markers)
        {
            if (m.Kind == MapMarkerKind.Me)
            {
                me = m;
            }
        }
        if (_worldMapFollow && me != null)
        {
            _worldMapCentre = new Vector2(me.X, me.Y);
        }
        _worldMapZoom = Math.Clamp(_worldMapZoom, 1, 8);
        float half = 0.5f / _worldMapZoom;
        _worldMapCentre = new Vector2(Math.Clamp(_worldMapCentre.X, half, 1 - half), Math.Clamp(_worldMapCentre.Y, half, 1 - half));
        Vector2 corner = _worldMapCentre - new Vector2(half, half);
        Vector2 ToScreen(float u, float v) => (area.Position + (new Vector2(u, v) - corner) * _worldMapZoom * area.Size).Round();

        // The picture, its frame and grid.
        _p.DrawRect(area.Grow(Ui.Px(3)), new Color(0.32f, 0.27f, 0.20f));
        if (picture != null)
        {
            var source = new Vector2(picture.GetWidth(), picture.GetHeight());
            _p.DrawTextureRectRegion(picture, area, new Rect2(corner * source, source / _worldMapZoom));
        }
        else
        {
            _p.DrawRect(area, new Color(0.78f, 0.72f, 0.58f));
            _o = area.Position;
            TextIn(view == null ? "NO MAP HERE" : "DRAWING THE MAP…", 0, side / Ui.Scale / 2 - 20, side / Ui.Scale, 30, LabelInk,
                HorizontalAlignment.Center, bold: true, condensed: true);
        }
        _hits.Add(new Hit(area, HitKind.Map));
        int cells = view?.Cells ?? 26;
        float cell = area.Size.X * _worldMapZoom / cells;
        int c0 = (int)MathF.Floor(corner.X * cells), r0 = (int)MathF.Floor(corner.Y * cells);
        for (int i = c0; i <= c0 + cells / _worldMapZoom + 1 && i <= cells; i++)
        {
            float x = ToScreen(i / (float)cells, 0).X;
            if (x > area.Position.X && x < area.End.X)
            {
                _p.DrawRect(new Rect2(x, area.Position.Y, 1, area.Size.Y), GridInk);
            }
        }
        for (int j = r0; j <= r0 + cells / _worldMapZoom + 1 && j <= cells; j++)
        {
            float y = ToScreen(0, j / (float)cells).Y;
            if (y > area.Position.Y && y < area.End.Y)
            {
                _p.DrawRect(new Rect2(area.Position.X, y, area.Size.X, 1), GridInk);
            }
        }
        // Cell names in each cell's corner when the cells are big enough, else along the edges.
        bool inCells = cell >= Ui.Px(44);
        for (int i = Math.Max(0, c0); i < cells && i <= c0 + cells / _worldMapZoom + 1; i++)
        {
            for (int j = Math.Max(0, r0); j < cells && j <= r0 + cells / _worldMapZoom + 1; j++)
            {
                if (!inCells && i != Math.Max(0, c0) && j != Math.Max(0, r0))
                {
                    continue;
                }
                Vector2 at = ToScreen(i / (float)cells, j / (float)cells) + new Vector2(Ui.Px(4), Ui.Px(2));
                if (!area.Grow(-Ui.Px(2)).HasPoint(at) || !area.HasPoint(at + new Vector2(Ui.Px(26), Ui.Px(14))))
                {
                    continue;
                }
                _o = at;
                string name = !inCells ? i == Math.Max(0, c0) && j == Math.Max(0, r0) ? Cell(i, j) : j == Math.Max(0, r0) ? Column(i) : Row(j) : Cell(i, j);
                Text(name, 0, 0, 13, LabelInk, bold: true);
            }
        }

        // Markers, nearest last: bags and beds, the death, the player's markers, teammates, the player.
        var labeller = new Labeller(Size);
        labeller.Block(new Rect2(0, 0, Size.X, area.Position.Y));
        labeller.Block(new Rect2(area.End.X, 0, Size.X - area.End.X, Size.Y));
        labeller.Block(new Rect2(0, area.End.Y, Size.X, Size.Y - area.End.Y));
        labeller.Block(new Rect2(0, 0, area.Position.X, Size.Y));
        float r = Ui.Pxf(11);
        foreach (MapMarkerView m in markers)
        {
            labeller.Block(ToScreen(m.X, m.Y), r + Ui.Pxf(2));
        }
        Rect2 inside = area.Grow(-Ui.Px(6));
        foreach (MapMarkerKind kind in new[] { MapMarkerKind.Bag, MapMarkerKind.Bed, MapMarkerKind.Death, MapMarkerKind.Marker, MapMarkerKind.Teammate, MapMarkerKind.Me })
        {
            foreach (MapMarkerView m in markers)
            {
                if (m.Kind != kind)
                {
                    continue;
                }
                Vector2 at = ToScreen(m.X, m.Y);
                if (!inside.HasPoint(at))
                {
                    continue;
                }
                PaintMapMarker(m, at, r, labeller);
            }
        }

        // Beside the map: where the player is, the markers, the team.
        var info = new Rect2(area.End.X + margin, margin, Size.X - area.End.X - 2 * margin, Size.Y - 2 * margin);
        PaintMapPanel(info, view, me);
        // The keys at the foot of the panel (the toasts keep the bottom left).
        _o = new Vector2(info.Position.X, info.End.Y - Ui.Px(40));
        Text("CLICK  PLACE A MARKER     RIGHT-CLICK  REMOVE IT", 0, 0, 14, G.Muted, bold: true);
        Text("DRAG  PAN     WHEEL  ZOOM     G  CLOSE", 0, 22, 14, G.Muted, bold: true);
    }

    private static string Column(int i) => Hollow.Client.Map.ZoneMap.Column(i);

    private static string Row(int j) => j.ToString(CultureInfo.InvariantCulture);

    private static string Cell(int i, int j) => Column(i) + Row(j);

    /// <summary>A grid cell's name for a map point (0–1 across and down).</summary>
    private static string CellAt(float u, float v, int cells) =>
        Cell(Math.Clamp((int)MathF.Floor(u * cells), 0, cells - 1), Math.Clamp((int)MathF.Floor(v * cells), 0, cells - 1));

    // One marker as Rust draws them: a disc with a glyph or a number, the player an arrow, names by the Labeller.
    private void PaintMapMarker(MapMarkerView m, Vector2 at, float r, Labeller labeller)
    {
        ReadOnlySpan<LabelSide> sides = stackalloc[] { LabelSide.Right, LabelSide.Below, LabelSide.Above, LabelSide.Left };
        Color outline = new(0.08f, 0.07f, 0.06f, 0.9f);
        string label = m.Label;
        switch (m.Kind)
        {
            case MapMarkerKind.Me:
            {
                float a = m.Facing * MathF.PI / 180;
                Vector2 f = new(MathF.Sin(a), -MathF.Cos(a)), s = new(-f.Y, f.X);
                float size = Ui.Pxf(15);
                Vector2[] tip = { at + f * size, at - f * size * 0.6f + s * size * 0.7f, at - f * size * 0.3f, at - f * size * 0.6f - s * size * 0.7f };
                Vector2[] shadow = Array.ConvertAll(tip, p => p + (p - at).Normalized() * Ui.Pxf(2.5f));
                _p.DrawColoredPolygon(shadow, outline);
                _p.DrawColoredPolygon(tip, G.Ivory);
                label = "";
                break;
            }
            case MapMarkerKind.Teammate:
                _p.DrawCircle(at, r * 0.8f + Ui.Pxf(2), outline, true, -1, true);
                _p.DrawCircle(at, r * 0.8f, m.Dim ? new Color(G.Green, 0.45f) : G.Green, true, -1, true);
                break;
            case MapMarkerKind.Death:
                _p.DrawCircle(at, r + Ui.Pxf(2), outline, true, -1, true);
                _p.DrawCircle(at, r, G.Red, true, -1, true);
                _o = at;
                Glyph("skull", -8, -8, 16, G.Ink);
                break;
            case MapMarkerKind.Bag or MapMarkerKind.Bed:
                _p.DrawCircle(at, r + Ui.Pxf(2), new Color(G.Ink, 0.9f), true, -1, true);
                _p.DrawCircle(at, r, new Color(0.20f, 0.30f, 0.14f), true, -1, true);
                _o = at;
                Glyph(m.Kind == MapMarkerKind.Bed ? "bed" : "bag", -8, -8, 16, G.Ink);
                break;
            case MapMarkerKind.Marker:
            {
                Color colour = MarkerColours[Math.Clamp(m.Colour, 0, MarkerColours.Length - 1)];
                _p.DrawLine(at + new Vector2(0, r), at + new Vector2(0, r + Ui.Pxf(8)), outline, Ui.Pxf(3), true);
                _p.DrawCircle(at, r + Ui.Pxf(2), outline, true, -1, true);
                _p.DrawCircle(at, r, colour, true, -1, true);
                _o = at;
                TextIn((m.Index + 1).ToString(CultureInfo.InvariantCulture), -11, -10, 22, 15, outline, HorizontalAlignment.Center, bold: true);
                if (label.Length == 0)
                {
                    label = $"Marker {m.Index + 1}";
                }
                break;
            }
        }
        if (label.Length > 0 && labeller.TryPlaceAround(at, r + Ui.Pxf(2), label, G.Ink, 15, MapLabel.Strong, sides, out MapOverlay.Text placed))
        {
            FontFile font = G.SemiBold;
            int px = Ui.Px(15);
            float w = font.GetStringSize(label, HorizontalAlignment.Left, -1, px).X;
            float left = MathF.Round(placed.Centred ? placed.Position.X - w / 2 : placed.Position.X);
            float baseline = MathF.Round(placed.Position.Y);
            float pad = Ui.Px(5);
            _p.DrawRect(new Rect2(left - pad, baseline - font.GetAscent(px) - Ui.Px(2), w + 2 * pad, font.GetHeight(px) + Ui.Px(4)),
                new Color(0.08f, 0.08f, 0.07f, 0.8f));
            _p.DrawString(font, new Vector2(left, baseline), label, HorizontalAlignment.Left, -1, px, m.Dim ? G.Muted : G.Ink);
        }
    }

    // The column beside the map: the cell the player is in, the markers, the team and its invites.
    private void PaintMapPanel(Rect2 panel, MapView? view, MapMarkerView? me)
    {
        _o = panel.Position;
        float w = panel.Size.X / Ui.Scale;
        int cells = view?.Cells ?? 26;
        Text("MAP", 0, 0, 40, G.Ink, bold: true, condensed: true);
        if (me != null)
        {
            TextIn("YOU ARE IN " + CellAt(me.X, me.Y, cells), 0, 12, w, 20, G.Ink, HorizontalAlignment.Right, bold: true);
        }
        Button(_worldMapFollow ? "FOLLOWING YOU" : "CENTRE ON ME", 0, 56, w, 34, () =>
        {
            _worldMapFollow = true;
        }, 16, _worldMapFollow ? new Color(G.Selected, 0.5f) : G.Panel);

        // Markers.
        float y = 104;
        IReadOnlyList<MapMarkerView> all = Model.MapMarkers;
        var mine = new List<MapMarkerView>();
        foreach (MapMarkerView m in all)
        {
            if (m.Kind == MapMarkerKind.Marker)
            {
                mine.Add(m);
            }
        }
        Section($"MARKERS  {mine.Count}/{Model.MaxMarkers}", 0, y);
        y += 36;
        for (int i = 0; i < MarkerColours.Length; i++)
        {
            int colour = i;
            float x = i * 34;
            Button("", x, y, 30, 26, () => _markerColour = colour, fill: colour == _markerColour ? new Color(G.Ink, 0.85f) : G.Panel);
            Rect(x + 4, y + 4, 22, 18, MarkerColours[i]);
        }
        Button("CLEAR ALL", w - 110, y, 110, 26, mine.Count > 0 ? () => Model.RemoveMarker(-1) : null, 14, enabled: mine.Count > 0);
        y += 34;
        foreach (MapMarkerView m in mine)
        {
            Rect(0, y, w, 30, G.Panel);
            Rect(6, y + 6, 18, 18, MarkerColours[Math.Clamp(m.Colour, 0, MarkerColours.Length - 1)]);
            Text(m.Label.Length > 0 ? m.Label : $"Marker {m.Index + 1}", 34, y + 4, 17, G.Ink, bold: true);
            TextIn(CellAt(m.X, m.Y, cells), w - 110, y + 5, 64, 16, G.Muted, HorizontalAlignment.Right, bold: true);
            int index = m.Index;
            Button("×", w - 34, y + 2, 30, 26, () => Model.RemoveMarker(index), 20);
            y += 34;
        }
        if (mine.Count == 0)
        {
            Paragraph("Click the map to put a marker down; right-click one to take it off.", 0, y + 2, w, 16, G.Muted, 2);
            y += 44;
        }

        // The team.
        y += 14;
        TeamInfo? team = Model.Team;
        Section(team == null ? "NO TEAM" : $"TEAM  {team.Members.Count}/{team.MaxMembers}", 0, y);
        y += 38;
        if (team == null)
        {
            Button("CREATE TEAM", 0, y, w, 40, () => Model.CreateTeam(), 20, new Color(G.Green, 0.55f));
            y += 46;
            Paragraph("Then invite players near you: look at them and press E.", 0, y, w, 16, G.Muted, 2);
            y += 42;
        }
        else
        {
            foreach (TeamMemberInfo member in team.Members)
            {
                Rect(0, y, w, 32, member.Me ? new Color(G.Selected, 0.35f) : G.Panel);
                _p.DrawCircle(At(14, y + 16), Ui.Pxf(5), member.Online ? G.Green : G.Muted, true, -1, true);
                Text(member.Name, 28, y + 5, 18, member.Online ? G.Ink : G.Muted, bold: true);
                if (member.Leader)
                {
                    TextIn("LEADER", w - 200, y + 8, 80, 14, G.Star, HorizontalAlignment.Right, bold: true);
                }
                if (team.Leading && !member.Me)
                {
                    string id = member.Id;
                    Button("LEAD", w - 112, y + 3, 54, 26, () => Model.PromoteInTeam(id), 13);
                    Button("KICK", w - 54, y + 3, 54, 26, () => Model.KickFromTeam(id), 13, new Color(G.Red, 0.55f));
                }
                y += 36;
            }
            Button("LEAVE TEAM", 0, y + 4, w, 36, () => Model.LeaveTeam(), 18, new Color(G.Red, 0.5f));
            y += 48;
        }
        foreach (TeamInviteInfo invite in Model.TeamInvites)
        {
            Rect(0, y, w, 64, new Color(G.Selected, 0.35f));
            Text($"{invite.From} invites you to a team", 10, y + 6, 17, G.Ink, bold: true);
            Text($"{(int)MathF.Ceiling(invite.Seconds)} s left", 10, y + 30, 14, G.Muted, bold: true);
            string id = invite.Team;
            Button("ACCEPT", w - 196, y + 26, 94, 30, () => Model.AcceptInvite(id), 15, new Color(G.Green, 0.6f));
            Button("DECLINE", w - 98, y + 26, 94, 30, () => Model.DeclineInvite(id), 15);
            y += 70;
        }
        PanelArea(0, 0, w, Math.Max(y, 40));
    }

    // ---------------------------------------------------------------- input (from GroundUi's Press, Release and wheel)

    private void PressMap(MouseButton button)
    {
        if (button == MouseButton.Left)
        {
            _worldMapPress = Pointer;
            _worldMapPressCentre = _worldMapCentre;
        }
        else if (button == MouseButton.Right)
        {
            // The marker nearest the pointer, within its disc, comes off.
            float best = Ui.Pxf(16);
            int index = -1;
            foreach (MapMarkerView m in Model.MapMarkers)
            {
                float d = MapToScreen(m.X, m.Y).DistanceTo(Pointer);
                if (m.Kind == MapMarkerKind.Marker && d < best)
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

    private void ReleaseMap(MouseButton button)
    {
        if (button != MouseButton.Left || _worldMapPress is not Vector2 pressed)
        {
            return;
        }
        _worldMapPress = null;
        if (pressed.DistanceTo(Pointer) < Ui.Pxf(5) && _worldMapArea.HasPoint(Pointer))
        {
            Vector2 at = ScreenToMap(Pointer);
            int count = 0;
            foreach (MapMarkerView m in Model.MapMarkers)
            {
                count += m.Kind == MapMarkerKind.Marker ? 1 : 0;
            }
            if (count < Model.MaxMarkers)
            {
                Model.PlaceMarker(at.X, at.Y, _markerColour, "");
            }
        }
    }

    private void DragMap()
    {
        if (_worldMapPress is Vector2 pressed && pressed.DistanceTo(Pointer) >= Ui.Pxf(5))
        {
            _worldMapFollow = false;
            _worldMapCentre = _worldMapPressCentre - (Pointer - pressed) / (_worldMapArea.Size * _worldMapZoom);
        }
    }

    private bool ZoomMap(int steps)
    {
        if (Screen != GroundScreen.Map || !_worldMapArea.HasPoint(Pointer))
        {
            return false;
        }
        Vector2 before = ScreenToMap(Pointer);
        _worldMapZoom = Math.Clamp(_worldMapZoom * (steps < 0 ? 1.25f : 0.8f), 1, 8);
        // Keep the point under the pointer where it was.
        Vector2 after = ScreenToMap(Pointer);
        _worldMapCentre += before - after;
        _worldMapFollow = false;
        return true;
    }

    private Vector2 MapToScreen(float u, float v)
    {
        Vector2 corner = _worldMapCentre - new Vector2(0.5f, 0.5f) / _worldMapZoom;
        return _worldMapArea.Position + (new Vector2(u, v) - corner) * _worldMapZoom * _worldMapArea.Size;
    }

    private Vector2 ScreenToMap(Vector2 screen)
    {
        Vector2 corner = _worldMapCentre - new Vector2(0.5f, 0.5f) / _worldMapZoom;
        return corner + (screen - _worldMapArea.Position) / (_worldMapZoom * _worldMapArea.Size);
    }
}
