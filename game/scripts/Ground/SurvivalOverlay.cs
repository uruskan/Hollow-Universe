using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Godot;
using Hollow.Client;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Items;
using HollowUniverse.Common;

namespace HollowUniverse.Ground;

/// <summary>
/// A debug text overlay of the survival game, for testing until the RustMVP interface lands from the UI package
/// (GROUND.md §8): vitals, the belt with the item in hand, the inventory, clothing, crafting queue, what is looted,
/// the last events, the interact prompt and the death screen's choices. Built with <see cref="Ui"/>.
/// </summary>
public partial class SurvivalOverlay : Control
{
    private const int EventLines = 7;

    private readonly List<(string Text, double Time)> _events = new();
    private PanelContainer _card = null!;
    private Label _vitals = null!;
    private Label _belt = null!;
    private Label _items = null!;
    private Label _crafting = null!;
    private Label _loot = null!;
    private Label _log = null!;
    private Label _prompt = null!;
    private Label _death = null!;
    private double _time;

    public GameClient Client { get; set; } = null!;

    /// <summary>The prompt under the crosshair ("E  Pick up Wood pile"), set by the view each frame.</summary>
    public string Prompt { get; set; } = "";

    public Control Card => _card;

    public override void _Ready()
    {
        Theme = Ui.Theme;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        (PanelContainer card, VBoxContainer content) = Ui.Card(4);
        _card = card;
        content.AddChild(Ui.Label("Survival (debug)", TextStyle.Overline));
        _vitals = Line(content);
        content.AddChild(Ui.Separator());
        _belt = Line(content);
        _items = Line(content, TextStyle.Secondary);
        content.AddChild(Ui.Separator());
        _crafting = Line(content);
        _loot = Line(content, TextStyle.Secondary);
        content.AddChild(Ui.Separator());
        _log = Line(content, TextStyle.Secondary);
        Ui.Size(card, 400);
        Ui.Track(card, c =>
        {
            c.AnchorLeft = 0;
            c.AnchorRight = 0;
            c.AnchorTop = 0;
            c.AnchorBottom = 0;
            c.OffsetLeft = Ui.Px(20);
            c.OffsetTop = Ui.Px(196);
        });
        AddChild(card);

        _prompt = Ui.Label("", TextStyle.Value);
        _prompt.HorizontalAlignment = HorizontalAlignment.Center;
        _prompt.AddThemeConstantOverride("outline_size", Ui.Px(5));
        _prompt.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.7f));
        Ui.Track(_prompt, c =>
        {
            c.AnchorLeft = 0.5f;
            c.AnchorRight = 0.5f;
            c.AnchorTop = 0.5f;
            c.AnchorBottom = 0.5f;
            c.OffsetTop = Ui.Px(36);
            c.GrowHorizontal = GrowDirection.Both;
        });
        AddChild(_prompt);

        _death = Ui.Label("", TextStyle.Title, Ui.Warm);
        _death.HorizontalAlignment = HorizontalAlignment.Center;
        Ui.Place(_death, LayoutPreset.CenterTop, 120);
        AddChild(_death);

        LocalPlayer me = Client.Local;
        me.ItemsChanged += e => Add($"{(e.Count >= 0 ? "+" : "−")}{Math.Abs(e.Count)} {e.Item.Name}" +
            (e.Reason == GainReason.Broke ? " broke" : e.WeakSpot ? " (weak spot)" : ""));
        me.HitMarker += e => Add(e.Target switch
        {
            HitTarget.Character => $"Hit {e.Region.ToString().ToLowerInvariant()} for {e.Damage:0}" + (e.Killed ? ", killed" : ""),
            HitTarget.Deployable => $"Hit a deployable for {e.Damage:0}",
            _ => e.WeakSpot ? "Weak spot!" : "Hit",
        });
        me.DamageTaken += e => Add($"Took {e.Damage:0} ({e.Region.ToString().ToLowerInvariant()}" + (e.Weapon != null ? $", {e.Weapon.Name})" : ")"));
        me.CraftFinished += (item, count) => Add($"Crafted {item.Name} ×{count}");
        me.IntentRejected += a => Add($"Refused: {Words(a.Result)}");
        me.ContainerOpened += (kind, _) => Add($"Opened a {kind.ToString().ToLowerInvariant()}");
        me.Died += life => Add($"Died: {life.Cause}" + (life.KilledBy.Length > 0 ? $" by {life.KilledBy}" : ""));
    }

    private static Label Line(VBoxContainer parent, TextStyle style = TextStyle.Body)
    {
        Label label = Ui.Label("", style);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        parent.AddChild(label);
        return label;
    }

    private void Add(string text)
    {
        _events.Add((text, _time));
        while (_events.Count > EventLines)
        {
            _events.RemoveAt(0);
        }
    }

    private static string Words(ActionResult result) => result switch
    {
        ActionResult.NotEnough => "not enough materials",
        ActionResult.NeedsWorkbench => "needs a workbench",
        ActionResult.NoSpace => "no room",
        ActionResult.OutOfReach => "out of reach",
        ActionResult.TooSteep => "too steep",
        ActionResult.Blocked => "something is in the way",
        ActionResult.Cooldown => "still cooling down",
        ActionResult.RateLimited => "too fast",
        _ => result.ToString(),
    };

    /// <summary>Updates the text every frame.</summary>
    public void Refresh(double delta)
    {
        _time += delta;
        LocalPlayer me = Client.Local;
        GameContent c = Client.Content;
        VitalsView v = me.Vitals;
        var flags = new List<string>();
        if (v.IsBleeding)
        {
            flags.Add($"bleeding {v.Bleeding:0}");
        }
        if (v.Cold > 0)
        {
            flags.Add($"cold {v.Cold * 100:0} %");
        }
        if (v.Comfort)
        {
            flags.Add("by a fire");
        }
        if (v.Wet)
        {
            flags.Add("wet");
        }
        if (v.Healing > 0)
        {
            flags.Add($"healing {v.Healing:0}");
        }
        _vitals.Text = string.Create(CultureInfo.InvariantCulture,
            $"Health {v.Health:0}/{v.MaxHealth:0}   Food {v.Calories:0}   Water {v.Hydration:0}   {v.Temperature:0} °C") +
            (flags.Count > 0 ? "\n" + string.Join(", ", flags) : "");

        var belt = new StringBuilder();
        for (int i = 0; i < PlayerInventory.BeltSlots; i++)
        {
            ItemStack s = me.Inventory.Belt[i];
            string name = s.IsEmpty ? "—" : Short(c, s);
            belt.Append(i == me.HeldSlot ? $"[{i + 1} {name}]  " : $"{i + 1} {name}  ");
        }
        _belt.Text = belt.ToString().TrimEnd() + (me.WorkbenchLevel > 0 ? $"\nWorkbench {me.WorkbenchLevel} in reach" : "");
        string main = string.Join(", ", Enumerable.Range(0, me.Inventory.Main.Size).Where(i => !me.Inventory.Main[i].IsEmpty).Select(i => Short(c, me.Inventory.Main[i])));
        string worn = string.Join(", ", Enumerable.Range(0, me.Inventory.Wear.Size).Where(i => !me.Inventory.Wear[i].IsEmpty).Select(i => Short(c, me.Inventory.Wear[i])));
        _items.Text = $"Carried: {(main.Length > 0 ? main : "nothing")}\nWorn: {(worn.Length > 0 ? worn : "nothing")}";
        _crafting.Text = me.Crafting.Count == 0 ? "Crafting: nothing (type /craft <item> in chat)"
            : "Crafting: " + string.Join("; ", me.Crafting.Select(j => string.Create(CultureInfo.InvariantCulture,
                $"{j.Recipe.Output.Name} ×{j.Count} {j.UnitProgress * 100:0} % ({j.SecondsLeft:0} s)")));
        if (me.Loot is { } loot)
        {
            string inside = string.Join(", ", Enumerable.Range(0, loot.Size).Where(i => !loot[i].IsEmpty).Select(i => Short(c, loot[i])));
            _loot.Text = $"Looting a {me.LootKind.ToString().ToLowerInvariant()}: {(inside.Length > 0 ? inside : "empty")}\n" +
                (me.LootTakesDeposits ? "/put <slot> moves a main slot in, /take <slot> takes one out" : "/take <slot> takes a slot");
        }
        else
        {
            _loot.Text = "";
        }
        _log.Text = string.Join("\n", _events.Where(e => _time - e.Time < 20).Select(e => e.Text));
        _prompt.Text = Prompt;
        if (me.IsDead)
        {
            var options = me.Life.Options.Select((o, i) => string.Create(CultureInfo.InvariantCulture,
                $"{(o.IsBeach ? "B: the beach" : $"{i}: a sleeping bag")}{(me.SecondsUntilReady(o) > 0 ? $" (in {me.SecondsUntilReady(o):0} s)" : "")}"));
            _death.Text = $"You died: {me.Life.Cause}" + (me.Life.KilledBy.Length > 0 ? $" ({me.Life.KilledBy})" : "") + "\n" + string.Join("   ", options);
        }
        else
        {
            _death.Text = "";
        }
    }

    private static string Short(GameContent c, ItemStack s)
    {
        ItemDef? item = c.Item(s.Item);
        if (item == null)
        {
            return "?";
        }
        return item.HasCondition ? $"{item.Name} ({100 * s.Condition / Math.Max(1, item.MaxCondition)} %)" : s.Count > 1 ? $"{item.Name} ×{s.Count}" : item.Name;
    }
}
