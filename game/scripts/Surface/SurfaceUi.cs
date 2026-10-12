using System;
using Godot;
using Hollow.Core;
using Hollow.Galaxy.Systems;
using Hollow.Surface;
using HollowUniverse.Common;

namespace HollowUniverse.Surface;

/// <summary>The surface view's overlay: where you are, the local time, the sun and moon, and the frame time.</summary>
public partial class SurfaceUi : Control
{
    private Label _title = null!;
    private Label _time = null!;
    private Label _position = null!;
    private Label _altitude = null!;
    private Label _biome = null!;
    private Label _sun = null!;
    private Label _moon = null!;
    private Label _frame = null!;
    private double _lastUpdate;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Theme = Ui.Theme;
        (PanelContainer card, VBoxContainer content) = Ui.Card();
        Ui.Place(card, LayoutPreset.TopLeft);
        Ui.Size(card, 300);
        _title = Ui.Label("", TextStyle.Heading);
        content.AddChild(_title);
        GridContainer rows = Ui.Rows();
        content.AddChild(rows);
        _time = Ui.AddRow(rows, "Local time");
        _position = Ui.AddRow(rows, "Position");
        _altitude = Ui.AddRow(rows, "Altitude");
        _biome = Ui.AddRow(rows, "Ground", "", numeric: false);
        _sun = Ui.AddRow(rows, "Sun");
        _moon = Ui.AddRow(rows, "Moon");
        _frame = Ui.AddRow(rows, "Frame");
        AddChild(card);
        HFlowContainer hints = Ui.KeyHints(("W|A|S|D", "move"), ("Q|E", "down / up"), ("Right mouse", "look"),
            ("Shift", "fast"), ("G", "walk / fly"), ("[|]", "time ∓ 30 min"), ("T", "run time"));
        Ui.Place(hints, LayoutPreset.BottomLeft);
        AddChild(hints);
    }

    public void Refresh(Body planet, PlanetSurface surface, RenderFrame frame, Vector3D camera, SurfaceSky sky, double time,
        float frameMs, TerrainLod terrain, bool walking)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        if (now - _lastUpdate < 0.25)
        {
            return;
        }
        _lastUpdate = now;
        _title.Text = planet.Name;
        double hours = sky.LocalHours;
        int h = (int)Math.Floor(hours), m = (int)Math.Floor((hours - h) * 60);
        _time.Text = $"{h:00}:{m:00}  ({UniverseTime.ToUtc(time):yyyy-MM-dd HH:mm} UTC)";
        Vector3D d = camera.Normalized;
        double lat = Math.Asin(Math.Clamp(d.Y, -1, 1)) * 180 / Math.PI;
        double lon = Math.Atan2(-d.Z, d.X) * 180 / Math.PI;
        _position.Text = $"{Ui.Number(Math.Abs(lat), "0.0000")}° {(lat >= 0 ? "N" : "S")}  {Ui.Number(Math.Abs(lon), "0.0000")}° {(lon >= 0 ? "E" : "W")}";
        double ground = surface.HeightAt(d);
        _altitude.Text = $"{Ui.Number(camera.Length - surface.Radius - Math.Max(ground, 0), "N0")} m above ground ({Ui.Number(ground, "N0")} m){(walking ? ", on foot" : "")}";
        _biome.Text = surface.BiomeAt(d).ToString();
        _sun.Text = $"{Ui.Number(sky.SunElevation, "0.0")}° above the horizon";
        _moon.Text = sky.Moon == null ? "none" : $"{Ui.Number(sky.MoonElevation, "0.0")}°, {Ui.Number(sky.MoonPhase * 100, "N0")}% lit";
        _frame.Text = $"{Ui.Number(frameMs, "0.0")} ms ({Ui.Number(1000 / Math.Max(frameMs, 0.01), "N0")} fps), {terrain.DrawnTiles} tiles";
    }
}
