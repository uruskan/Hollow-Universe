using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Hollow.Client;
using HollowUniverse.Common;
using HollowUniverse.Ground;
using HollowUniverse.UI.Model;

namespace HollowUniverse.UI.Viewer;

public partial class UiViewer
{
    private static void PrepareCraftingHud(MockGroundUi model)
    {
        if (model.Death != null) model.Respawn(null);
        model.CloseLoot();
        foreach (CraftJob job in model.Queue.ToArray()) model.CancelCraft(job.Ticket);
        model.FreeCrafting = true;
        model.Craft("bandage", 4);
        model.Craft("arrow.wooden", 2);
        model.FreeCrafting = false;
    }

    private async void RunCraftingHudCheck()
    {
        int checks = 0, failures = 0;
        await CheckCraftingHud((ok, what) =>
        {
            checks++;
            if (!ok) failures++;
            GD.Print($"crafting-hud-check: {(ok ? "ok  " : "FAIL")} {what}");
        });
        GD.Print($"crafting-hud-check: {checks - failures}/{checks} passed");
        GetTree().Quit(failures == 0 ? 0 : 1);
    }

    private async Task CheckCraftingHud(Action<bool, string> expect)
    {
        // Test the production adapter with a separate recipe key and a multi-item yield.
        var output = new Hollow.Simulation.Content.ItemDef { Key = "arrow.wooden" };
        var recipe = new Hollow.Simulation.Content.Recipe { Key = "arrows-batch", Output = output, Amount = 2, Seconds = 5 };
        CraftJob mapped = ClientGroundUi.ToUiCraftJob(new CraftJobView(7, recipe, 3, .4f, 13, false, 5));
        expect(mapped.Key == "arrows-batch" && mapped.OutputKey == "arrow.wooden" && mapped.Count == 6 && mapped.UnitProgress == .4f,
            "client adapter keeps recipe identity, produced item, output units and next-craft progress separate");

        // Use the rendered HUD and an independently advanced clock; test duration cannot finish a sample craft.
        var model = _model;
        var ui = _ui;
        PrepareCraftingHud(model);
        ui.Open(GroundScreen.Hud);
        ui.Show();
        await Frames(3);
        int owned = model.Have("bandage");
        expect(ui.HudCrafting is { Job.Count: 4, Job.OutputKey: "bandage" } && ui.HudCrafting.Owned == owned,
            "HUD shows four remaining bandages and the combined main/belt inventory count");
        model.Tick(2);
        await Frames(3);
        expect(ui.HudCrafting is { Job.Count: 4 } status && Math.Abs(status.Job.UnitProgress - .4f) < .001f,
            "next-craft progress advances without changing the remaining count early");
        model.Tick(3);
        await Frames(3);
        expect(ui.HudCrafting is { Job.Count: 3, Job.UnitProgress: 0 } && ui.HudCrafting.Owned == owned + 1,
            "one completion changes four to three remaining and increases the owned count by one");
        SlotRef carriedBandage = new[] { ContainerId.Main, ContainerId.Belt }
            .SelectMany(container => Enumerable.Range(0, model.Slots(container).Count).Select(i => new SlotRef(container, i)))
            .First(slot => model.Slots(slot.Container)[slot.Index].Key == "bandage");
        model.Drop(carriedBandage, 1);
        await Frames(3);
        expect(ui.HudCrafting is { Job.Count: 3 } changed && changed.Owned == owned,
            "owned count follows inventory changes independently of crafting");
        model.CancelCraft(model.Queue[0].Ticket);
        await Frames(3);
        int arrows = model.Have("arrow.wooden");
        expect(ui.HudCrafting is { Job.OutputKey: "arrow.wooden", Job.Count: 4 } && ui.HudCrafting.Owned == arrows,
            "cancelling the active job reveals the next item with yield already included once");
        model.Tick(5);
        await Frames(3);
        expect(ui.HudCrafting is { Job.Count: 2 } && ui.HudCrafting.Owned == arrows + 2,
            "a two-arrow recipe completion removes two remaining output units and adds two owned");
        model.Tick(5);
        await Frames(3);
        expect(ui.HudCrafting == null && model.Have("arrow.wooden") == arrows + 4,
            "finishing the queue hides the card without losing the final output");

        model.FreeCrafting = true;
        model.Craft("bandage", 1);
        ui.Open(GroundScreen.Inventory);
        await Frames(3);
        expect(ui.HudCrafting == null, "inventory screen hides the HUD crafting card");
        ui.Open(GroundScreen.Hud);
        await Frames(3);
        expect(ui.HudCrafting is { Job.Count: 1 }, "returning to the HUD restores the active craft");
        model.CancelCraft(model.Queue[0].Ticket);
        await Frames(3);
        expect(ui.HudCrafting == null, "cancelling the final queued job hides the card");
        model.Craft("bandage", 1);
        model.Die();
        await Frames(3);
        expect(ui.HudCrafting == null, "death hides the crafting card");

        // Exercise the same pixel layout used to paint at common/narrow sizes and a larger UI preference.
        float originalScale = Ui.Scale;
        foreach ((Vector2 size, float scale) in new[]
        {
            (new Vector2(1600, 900), 1f), (new Vector2(1280, 720), .8125f),
            (new Vector2(1024, 768), .75f), (new Vector2(1920, 1080), 1.25f),
        })
        {
            Ui.SetScale(scale);
            Rect2 card = GroundUi.CraftingHudLayout(size);
            Vector2 canvas = new(MathF.Floor((size.X - Ui.Px(1600)) / 2), size.Y - Ui.Px(900));
            Rect2 belt = new(canvas + new Vector2(Ui.Px(550), Ui.Px(802)), new Vector2(Ui.Px(475), Ui.Px(75)));
            Rect2 vitals = new(new Vector2(size.X - Ui.Px(260), size.Y - Ui.Px(122)), new Vector2(Ui.Px(240), Ui.Px(102)));
            expect(card.Position.X >= 0 && card.Position.Y >= 0 && card.End.X <= size.X && card.End.Y <= size.Y &&
                !card.Intersects(belt) && !card.Intersects(vitals) && card.End.X <= vitals.Position.X - Ui.Px(12),
                $"crafting card stays visible left of vitals and clear of the hotbar at {size.X}×{size.Y}, scale {scale}");
        }
        Ui.SetScale(originalScale);
    }
}
