using System;
using Godot;

namespace HollowUniverse.UI;

/// <summary>A full-window layer whose drawing is done by its owner (the ground interface paints in immediate mode).</summary>
public partial class Painter : Control
{
    public Action<Painter>? Paint { get; set; }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }

    public override void _Draw() => Paint?.Invoke(this);
}
