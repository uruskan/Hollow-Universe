using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Hollow.Surface;
using HollowUniverse.Common;
using HollowUniverse.UI.Model;
using G = HollowUniverse.Common.Ui.Ground;

namespace HollowUniverse.UI;

// The map (G7, GROUND.md §6.8; the whole world since S3d, docs/WHOLE_WORLD.md §11), on G as in Rust: the map control
// (GroundUi.Globe.cs) as large as the window allows, following the player with local north up; on it the player and
// their facing, teammates with their names, the last death, sleeping bags and beds and the player's own markers (names
// placed by the Labeller, so they never overlap); beside it the grid cell the player stands in ("C K12"), the markers
// with their colours and cells, and the team (members, invites, create, leave, promote, kick). A click on the map puts a
// marker down, a right-click on one takes it off; dragging turns the globe, the wheel zooms.
public partial class GroundUi
{
    /// <summary>The six marker colours (MapMarkerView.Colour), RustMVP's palette in kind.</summary>
    public static readonly Color[] MarkerColours =
    {
        new(0.86f, 0.30f, 0.22f), new(0.93f, 0.68f, 0.20f), new(0.55f, 0.75f, 0.30f), new(0.30f, 0.62f, 0.85f),
        new(0.70f, 0.45f, 0.85f), new(0.92f, 0.90f, 0.85f),
    };

    private int _markerColour;

    /// <summary>Opens the map (G), centred on the player.</summary>
    public void ToggleMap() => Open(Screen == GroundScreen.Map ? GroundScreen.Hud : GroundScreen.Map);

    // The canvas's part: the backdrop, the frame round the map, its hit area and the panel beside it. The picture is the
    // globe's layer above; the grid and markers are the overlay's (PaintMapOverlay).
    private void PaintMap()
    {
        _p.DrawRect(new Rect2(Vector2.Zero, Size), MapBackdrop);
        MapView? view = Model.Map;
        Rect2 area = MapArea();
        float margin = Ui.Px(24);
        _p.DrawRect(area.Grow(Ui.Px(3)), new Color(0.32f, 0.27f, 0.20f));
        _p.DrawRect(area, MapBackdrop);
        if (view == null)
        {
            _p.DrawRect(area, MapBlank);
            _o = area.Position;
            TextIn("NO MAP HERE", 0, area.Size.Y / Ui.Scale / 2 - 20, area.Size.X / Ui.Scale, 30, LabelInk, HorizontalAlignment.Center, bold: true, condensed: true);
        }
        _hits.Add(new Hit(area, HitKind.Map));

        MapMarkerView? me = null;
        foreach (MapMarkerView m in Model.MapMarkers)
        {
            if (m.Kind == MapMarkerKind.Me)
            {
                me = m;
            }
        }
        // Beside the map: where the player is, the markers, the team.
        var info = new Rect2(area.End.X + margin, margin, Size.X - area.End.X - 2 * margin, Size.Y - 2 * margin);
        PaintMapPanel(info, view, me);
        // The keys at the foot of the panel (the toasts keep the bottom left).
        _o = new Vector2(info.Position.X, info.End.Y - Ui.Px(40));
        Text("CLICK  PLACE A MARKER     RIGHT-CLICK  REMOVE IT", 0, 0, 14, G.Muted, bold: true);
        Text("DRAG  TURN     WHEEL  ZOOM     G  CLOSE", 0, 22, 14, G.Muted, bold: true);
    }

    // The overlay's part: the grid, then the markers nearest last (bags and beds, the death, the player's markers,
    // teammates, the player), then the cells' names where they fit.
    private void PaintMapOverlay()
    {
        if (Model.Map is not { } view || _mapGlobe.Camera == null)
        {
            return;
        }
        GlobeFrame f = BeginGlobe(_mapGlobe, view);
        IReadOnlyList<MapMarkerView> markers = Model.MapMarkers;
        float r = Ui.Pxf(11);
        foreach (MapMarkerView m in markers)
        {
            if (OnGlobe(f, m.At) is Vector2 at)
            {
                f.Labeller.Block(at, r + Ui.Pxf(2));
            }
        }
        foreach (MapMarkerKind kind in new[] { MapMarkerKind.Bag, MapMarkerKind.Bed, MapMarkerKind.Death, MapMarkerKind.Marker, MapMarkerKind.Teammate, MapMarkerKind.Me })
        {
            foreach (MapMarkerView m in markers)
            {
                if (m.Kind == kind && OnGlobe(f, m.At, Ui.Px(6)) is Vector2 at)
                {
                    PaintMapMarker(m, at, NorthOnGlobe(f, m.At), r, f.Labeller);
                }
            }
        }
        EndGlobe(f, view);
    }

    /// <summary>A grid cell's name for a point of the map ("C K12").</summary>
    private static string CellOf(System.Numerics.Vector3 at, MapView? view) =>
        view == null ? "" : WorldGrid.Name(Direction(at), view.Cells);

    // One marker as Rust draws them: a disc with a glyph or a number, the player an arrow turned from local north as the
    // map shows it there, names by the Labeller.
    private void PaintMapMarker(MapMarkerView m, Vector2 at, Vector2 north, float r, Labeller labeller)
    {
        ReadOnlySpan<LabelSide> sides = stackalloc[] { LabelSide.Right, LabelSide.Below, LabelSide.Above, LabelSide.Left };
        Color outline = new(0.08f, 0.07f, 0.06f, 0.9f);
        string label = m.Label;
        switch (m.Kind)
        {
            case MapMarkerKind.Me:
            {
                // The facing is clockwise from north; on screen (y down) that turn is the usual rotation.
                float a = m.Facing * MathF.PI / 180;
                Vector2 f = new Vector2(north.X * MathF.Cos(a) - north.Y * MathF.Sin(a), north.X * MathF.Sin(a) + north.Y * MathF.Cos(a)).Normalized();
                Vector2 s = new(-f.Y, f.X);
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
        Text("MAP", 0, 0, 40, G.Ink, bold: true, condensed: true);
        if (me != null && view != null)
        {
            TextIn("YOU ARE IN " + CellOf(me.At, view), 0, 12, w, 20, G.Ink, HorizontalAlignment.Right, bold: true);
        }
        bool following = _mapGlobe.Camera?.Following ?? true;
        Button(following ? "FOLLOWING YOU" : "CENTRE ON ME", 0, 56, w, 34, () =>
        {
            if (_mapGlobe.Camera != null)
            {
                _mapGlobe.Camera.Following = true;
            }
        }, 16, following ? new Color(G.Selected, 0.5f) : G.Panel);

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
            TextIn(CellOf(m.At, view), w - 120, y + 5, 76, 16, G.Muted, HorizontalAlignment.Right, bold: true);
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
}
