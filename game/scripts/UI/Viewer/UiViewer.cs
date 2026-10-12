using System.Collections.Generic;
using System.Globalization;
using Godot;
using HollowUniverse.UI.Model;

namespace HollowUniverse.UI.Viewer;

/// <summary>
/// The interface viewer (<c>--view ui</c>, GROUND.md §8): the ground interface on <see cref="MockGroundUi"/> over a
/// plain backdrop, opened on one screen for a look or a screenshot. Options (after "--"):
/// <c>--screen hud|inventory|loot|corpse|furnace|campfire|crafting|death|wheel|hammer|menu</c>, and
/// <c>--pointer x,y</c> to hold the pointer at a design-canvas point (hover states, wheel sectors).
/// Screenshots hold the HUD's moments and the sample's clock still; without <c>--shot</c> the mock runs and every
/// screen is interactive.
/// </summary>
public partial class UiViewer : Node
{
    public Dictionary<string, string> Options { get; set; } = new();

    /// <summary>For automated screenshots: fonts, icons and the character have had a few frames to settle.</summary>
    public bool ReadyForShot => _frames > 12;

    private MockGroundUi _model = null!;
    private GroundUi _ui = null!;
    private bool _shot;
    private int _frames;

    public override void _Ready()
    {
        _shot = Options.ContainsKey("shot");
        AddChild(new UiBackdrop { Name = "Backdrop" });
        var layer = new CanvasLayer { Name = "Interface" };
        AddChild(layer);
        _model = new MockGroundUi();
        _ui = new GroundUi { Name = "GroundUi", Model = _model, HoldEffects = _shot };
        layer.AddChild(_ui);
        Show(Options.TryGetValue("screen", out string? screen) ? screen : "hud");
        if (Options.TryGetValue("pointer", out string? pointer))
        {
            string[] xy = pointer.Split(',');
            _ui.HoldPointer(new Vector2(float.Parse(xy[0], CultureInfo.InvariantCulture), float.Parse(xy[1], CultureInfo.InvariantCulture)));
        }
    }

    private void Show(string screen)
    {
        switch (screen)
        {
            case "inventory":
                _ui.Open(GroundScreen.Inventory);
                _ui.Select(new SlotRef(ContainerId.Belt, 5));
                break;
            case "loot":
                Loot(ContainerKind.Box, new SlotRef(ContainerId.Belt, 5));
                break;
            case "corpse":
                Loot(ContainerKind.Corpse, new SlotRef(ContainerId.Main, 7));
                break;
            case "furnace":
                Loot(ContainerKind.Furnace, new SlotRef(ContainerId.Main, 4));
                break;
            case "campfire":
                Loot(ContainerKind.Campfire, new SlotRef(ContainerId.Main, 15));
                break;
            case "crafting":
                _ui.SelectRecipe("bandage");
                _ui.Open(GroundScreen.Crafting);
                break;
            case "death":
                _model.Die();
                break;
            case "wheel" or "plan":
                _ui.OpenWheel(WheelKind.Plan);
                // Pointing at the second sector, the triangle foundation, as in the reference.
                _ui.HoldPointer(new Vector2(913, 227));
                break;
            case "hammer":
                _ui.OpenWheel(WheelKind.Hammer);
                _ui.HoldPointer(new Vector2(800, 190));
                break;
            case "menu":
                _ui.ShowMenu(inGame: false);
                break;
            default:
                _model.HudSample();
                _model.RaiseSamples();
                break;
        }
    }

    private void Loot(ContainerKind kind, SlotRef selected)
    {
        _model.OpenLoot(kind);
        _ui.Open(GroundScreen.Inventory);
        _ui.Select(selected);
    }

    public override void _Process(double delta)
    {
        _frames++;
        if (!_shot)
        {
            _model.Tick(delta);
        }
    }
}
