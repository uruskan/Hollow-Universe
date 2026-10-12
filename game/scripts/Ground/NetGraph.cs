using System;
using Godot;
using Hollow.Client;
using HollowUniverse.Common;

namespace HollowUniverse.Ground;

/// <summary>
/// The network graph (GROUND.md §4.4): the last 30 s of round trip, bandwidth down and prediction error, one line each,
/// each scaled to its own range so all three read at a glance.
/// </summary>
public partial class NetGraph : Control
{
    public static readonly Color RoundTripColour = new(0.42f, 0.80f, 1.00f);
    public static readonly Color DownColour = new(0.55f, 0.86f, 0.45f);
    public static readonly Color ErrorColour = new(1.00f, 0.62f, 0.35f);

    public ClientStats Stats { get; set; } = null!;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Ui.ScaleChanged += QueueRedraw;
    }

    public override void _ExitTree() => Ui.ScaleChanged -= QueueRedraw;

    public override void _Draw()
    {
        var area = new Rect2(Vector2.Zero, Size);
        DrawRect(area, new Color(0, 0, 0, 0.25f));
        float grid = Math.Max(1, (int)MathF.Round(Ui.Scale));
        for (int i = 1; i < 4; i++)
        {
            float y = MathF.Round(Size.Y * i / 4);
            DrawLine(new Vector2(0, y), new Vector2(Size.X, y), Ui.Divider, grid);
        }
        Line(Stats.RoundTripHistory, 100f, RoundTripColour);
        Line(Stats.KilobitsInHistory, 200f, DownColour);
        Line(Stats.PredictionErrorHistory, 0.05f, ErrorColour);
    }

    // Values are drawn against a range that starts at `floor` and grows to the largest value shown.
    private void Line(float[] history, float floor, Color colour)
    {
        int n = history.Length;
        float top = floor;
        foreach (float v in history)
        {
            top = Math.Max(top, v);
        }
        var points = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            float v = history[(Stats.HistoryStart + i) % n];
            points[i] = new Vector2(Size.X * i / (n - 1), Size.Y - 2 - (Size.Y - 4) * Math.Clamp(v / top, 0, 1));
        }
        DrawPolyline(points, colour, Math.Max(1f, Ui.Pxf(1.5f)), true);
    }

    /// <summary>What the three lines are.</summary>
    public static HBoxContainer Legend()
    {
        var row = new HBoxContainer();
        Ui.Track(row, r => r.AddThemeConstantOverride("separation", Ui.Px(12)));
        row.AddChild(Ui.Label("Round trip", TextStyle.Caption, RoundTripColour));
        row.AddChild(Ui.Label("Down", TextStyle.Caption, DownColour));
        row.AddChild(Ui.Label("Prediction error", TextStyle.Caption, ErrorColour));
        return row;
    }
}
