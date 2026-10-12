using Godot;
using HollowUniverse.Common;

namespace HollowUniverse.Ground;

/// <summary>A small dot in the middle of the screen.</summary>
public partial class Crosshair : Control
{
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Ui.ScaleChanged += QueueRedraw;
    }

    public override void _ExitTree() => Ui.ScaleChanged -= QueueRedraw;

    public override void _Draw()
    {
        Vector2 centre = (Size / 2).Floor();
        DrawCircle(centre, Ui.Pxf(3.2f), new Color(0, 0, 0, 0.45f), true, -1, true);
        DrawCircle(centre, Ui.Pxf(2f), new Color(1, 1, 1, 0.9f), true, -1, true);
    }
}
