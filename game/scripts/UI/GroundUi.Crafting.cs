using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using HollowUniverse.Common;
using HollowUniverse.UI.Model;
using G = HollowUniverse.Common.Ui.Ground;

namespace HollowUniverse.UI;

// The crafting screen (RustMVP's, from SBareRoot.cpp and its Godot predecessor): categories with counts, the
// recipe grid with search, the details with the ingredients table and amount, and the queue at the bottom left.
public partial class GroundUi
{
    private const int RecipeRows = 5;
    private string _category = "COMMON";
    private string? _recipe;
    private int _amount = 1;
    private int _recipeScroll;
    private readonly HashSet<string> _favourites = new();

    private Recipe? CurrentRecipe()
    {
        foreach (Recipe recipe in Model.Recipes)
        {
            if (recipe.Key == _recipe)
            {
                return recipe;
            }
        }
        return Model.Recipes.Count > 0 ? Model.Recipes[0] : null;
    }

    private bool CanCraft(Recipe recipe, int amount) =>
        Model.Death == null && Model.Workbench >= recipe.Workbench && (recipe.Station == null || Model.StationNear(recipe.Station)) &&
        recipe.Ingredients.All(i => Model.Have(i.Key) >= i.Amount * amount);

    private List<Recipe> VisibleRecipes()
    {
        string search = _search.Text.Trim();
        return Model.Recipes.Where(r =>
                (_category == "COMMON" || (_category == "FAVOURITE" ? _favourites.Contains(r.Key) : r.Category == _category))
                && (search.Length == 0 || Model.Item(r.Key).Name.Contains(search, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private void SetAmount(int amount)
    {
        _amount = Math.Clamp(amount, 1, 999);
        _quantity.Text = _amount.ToString(CultureInfo.InvariantCulture);
    }

    private void PaintCrafting()
    {
        _o = TopFrame;
        Button("", 802, 0, 250, 60, () => Open(GroundScreen.Inventory), 30);
        Arrow(833, 33, -1);
        Text("INVENTORY", 869, 13, 30, G.Ink, bold: true, condensed: true);
        if (Model.TechTree != null)
        {
            // G7: the tech tree of the workbench in reach.
            Button("", 1060, 0, 250, 60, OpenTechTree, 30);
            Text("TECH TREE", 1092, 13, 30, G.Ink, bold: true, condensed: true);
            Arrow(1268, 33);
        }

        _o = CanvasFrame;
        Rect(87, 65, 175, 597, G.Panel);
        Rect(266, 65, 521, 553, G.Panel);
        PanelArea(79, 65, 1414, 690);
        IReadOnlyList<CraftCategory> categories = Model.Categories;
        for (int i = 0; i < categories.Count; i++)
        {
            CraftCategory category = categories[i];
            string label = category.Name + (category.Count > 0 && i > 1 ? "  " + category.Count.ToString(CultureInfo.InvariantCulture) : "");
            string name = category.Name;
            Button(label, 79, 68 + i * 41, 191, 40, () =>
            {
                _category = name;
                _recipeScroll = 0;
            }, 18, _category == name ? G.Category : Transparent, left: true);
        }

        List<Recipe> recipes = VisibleRecipes();
        int rows = (recipes.Count + 4) / 5;
        _recipeScroll = Math.Clamp(_recipeScroll, 0, Math.Max(0, rows - RecipeRows));
        for (int i = _recipeScroll * 5; i < recipes.Count && i < (_recipeScroll + RecipeRows) * 5; i++)
        {
            int index = i - _recipeScroll * 5;
            string key = recipes[i].Key;
            float x = 273 + index % 5 * 101, y = 72 + index / 5 * 101;
            Button("", x, y, 96, 96, () => _recipe = key, fill: Transparent, tip: key);
            Icon(key, x + 5, y + 5, 85, 85);
        }

        Recipe? recipe = CurrentRecipe();
        if (recipe == null)
        {
            Rect(791, 65, 702, 690, G.Panel);
            Text("No crafting recipes are available yet.", 820, 100, 24, G.Ink, bold: true);
        }
        else
        {
            PaintRecipe(recipe);
        }
        PaintQueue();
    }

    private void PaintRecipe(Recipe recipe)
    {
        ItemInfo info = Model.Item(recipe.Key);
        float here = Model.SecondsHere(recipe);
        Rect(791, 65, 702, 489, G.Panel);
        TextIn(info.Name.ToUpperInvariant(), 899, 84, 502, 31, G.Ink, HorizontalAlignment.Center, bold: true);
        Icon(recipe.Key, 809, 75, 65, 65);
        bool favourite = _favourites.Contains(recipe.Key);
        Button(favourite ? "   FAVOURITED" : "   FAVOURITE", 799, 143, 110, 21, () =>
        {
            if (!_favourites.Remove(recipe.Key))
            {
                _favourites.Add(recipe.Key);
            }
            SavePreferences();
        }, 11);
        StarShape(811, 153, 5, favourite ? G.Green : G.Ink);
        if (recipe.Station != null)
        {
            // Made at a station (a ship at the vehicle workbench, G6): it rolls out onto the bench's bay.
            bool near = Model.StationNear(recipe.Station);
            Rect(991, 133, 345, 22, near ? new Color(0.31f, 0.44f, 0.18f, 0.72f) : new Color(G.Red, 0.72f));
            TextIn($"{Model.Item(recipe.Station).Name.ToUpperInvariant()} REQUIRED", 991, 133, 345, 16, G.Ink, HorizontalAlignment.Center, bold: true);
        }
        else if (recipe.Workbench > 0 || here < recipe.Seconds)
        {
            // The workbench it needs, and how much faster the one in reach makes it (GROUND_SYSTEMS.md §7).
            bool near = Model.Workbench >= recipe.Workbench;
            string faster = here < recipe.Seconds ? $"WORKBENCH {Model.Workbench}: {Ui.Number(recipe.Seconds / here, "0.#")}× FASTER" : "";
            string needs = recipe.Workbench > 0 ? $"WORKBENCH LEVEL {recipe.Workbench} REQUIRED" : "";
            Rect(991, 133, 345, 22, near ? new Color(0.31f, 0.44f, 0.18f, 0.72f) : new Color(G.Red, 0.72f));
            TextIn(needs.Length > 0 && faster.Length > 0 ? $"LEVEL {recipe.Workbench} · {faster}" : needs + faster, 991, 133, 345, 16, G.Ink,
                HorizontalAlignment.Center, bold: true);
        }
        // The time one takes here, green when a workbench shortens it.
        Rect(1408, 70, 80, 34, G.Panel);
        Glyph("clock", 1413, 76, 22, here < recipe.Seconds ? G.Green : G.Ink);
        Text(Ui.Number(here, "0.0"), 1438, 70, 25, here < recipe.Seconds ? G.Green : G.Ink, bold: true);
        Rect(1425, 108, 63, 32, G.Panel);
        Glyph("yield", 1430, 113, 22, G.Ink);
        Text(recipe.Yield.ToString(CultureInfo.InvariantCulture), 1453, 108, 24, G.Ink, bold: true);
        Paragraph(info.Description, 812, 174, 660, 21, G.Ink, 6);

        // Ingredients: what one craft takes, what this many take, and what the player has (short in amber).
        Rect(791, 558, 702, 197, G.Panel);
        (string Name, float X, float W)[] columns = { ("AMOUNT", 799, 129), ("ITEM TYPE", 935, 281), ("TOTAL", 1219, 132), ("HAVE", 1355, 130) };
        foreach ((string name, float x, float w) in columns)
        {
            TextIn(name, x, 560, w, 16, G.Ink, HorizontalAlignment.Center, bold: true);
        }
        // Four rows as RustMVP draws them; a recipe with more (the Tin Finch's five) shares the same height.
        int rows = Math.Max(4, recipe.Ingredients.Count);
        float step = 116f / rows;
        for (int i = 0; i < rows; i++)
        {
            Rect(798, 584 + i * step, 688, step - 2, new Color(0.13f, 0.12f, 0.10f, 0.15f));
        }
        for (int i = 0; i < recipe.Ingredients.Count; i++)
        {
            Ingredient ingredient = recipe.Ingredients[i];
            int have = Model.Have(ingredient.Key);
            Color colour = have >= ingredient.Amount * _amount ? G.Ink : G.Short;
            float y = 584 + i * step + (step - 29) / 2;
            TextIn(ingredient.Amount.ToString(CultureInfo.InvariantCulture), 799, y, 129, 19, colour, HorizontalAlignment.Right);
            Text(Model.Item(ingredient.Key).Name, 937, y, 19, colour);
            Text((ingredient.Amount * _amount).ToString(CultureInfo.InvariantCulture), 1222, y, 19, colour);
            Text(have.ToString(CultureInfo.InvariantCulture), 1357, y, 19, colour);
        }

        Button("−", 798, 710, 49, 40, () => SetAmount(_amount - 1), 35);
        Button("+", 938, 710, 49, 40, () => SetAmount(_amount + 1), 35);
        Button("", 992, 710, 49, 40, () => SetAmount(MaxCraft(recipe)), 28);
        // "The most": a play triangle against a bar, as in the reference.
        _p.DrawColoredPolygon(new[] { Pt(1015, 726.5f), Pt(1023.5f, 731), Pt(1015, 735.5f) }, G.Ink);
        Rect(1029, 721, 4, 21, G.Ink);
        Button("CRAFT", 1368, 710, 118, 40, () => Model.Craft(recipe.Key, _amount), 22, enabled: CanCraft(recipe, _amount));
    }

    private int MaxCraft(Recipe recipe)
    {
        int most = 999;
        foreach (Ingredient ingredient in recipe.Ingredients)
        {
            most = Math.Min(most, Model.Have(ingredient.Key) / Math.Max(1, ingredient.Amount));
        }
        return Math.Max(1, most);
    }

    // The queue: each job's icon, seconds left and count; the first one's progress; a click cancels.
    private void PaintQueue()
    {
        Rect(87, 665, 700, 90, G.Panel);
        IReadOnlyList<CraftJob> queue = Model.Queue;
        if (queue.Count == 0)
        {
            Text("CRAFTING QUEUE", 100, 672, 57, new Color(0.79f, 0.76f, 0.70f, 0.26f), bold: true);
            return;
        }
        for (int i = 0; i < queue.Count && i < 9; i++)
        {
            CraftJob job = queue[i];
            float x = 88 + i * 77;
            int ticket = job.Ticket;
            Button("", x, 670, 71, 70, () => Model.CancelCraft(ticket), tip: job.Key);
            Icon(job.Key, x + 5, 672, 60, 60);
            Text($"{(int)MathF.Ceiling(job.Remaining)}s  ×{job.Count}", x + 3, 715, 16, G.Ink, bold: true);
            if (i == 0 && job.Seconds > 0)
            {
                Rect(x, 740, 71 * Math.Clamp(1 - job.Remaining / job.Seconds, 0, 1), 3, G.Green);
            }
        }
    }

    /// <summary>The mouse wheel scrolls the recipe grid by rows.</summary>
    private bool ScrollRecipes(int rows)
    {
        if (Screen != GroundScreen.Crafting)
        {
            return false;
        }
        _o = CanvasFrame;
        if (!Area(266, 65, 521, 553).HasPoint(Pointer))
        {
            return false;
        }
        _recipeScroll = Math.Max(0, _recipeScroll + rows);
        return true;
    }
}
