using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hollow.Simulation.Content;
using Hollow.Simulation.Content.Readers;

namespace Hollow.Tests;

/// <summary>The ground game's data (GROUND.md §7), loaded once for every test that needs it.</summary>
public static class ContentFixture
{
    private static readonly Lazy<GameContent> Loaded = new(() => GameContent.Load(Folder));

    public static string Folder => Path.Combine(Fixtures.Root, "game", "data", "ground");

    public static GameContent Content => Loaded.Value;

    public static ItemDef Item(string key) => Content.ItemByKey(key);
}

public static class ContentTests
{
    /// <summary>
    /// GROUND.md §7: the data in game/data/ground loads with no problem: IDs and keys are unique, every reference
    /// resolves, and every recipe can be made from what natural objects give (through workbenches, campfires and
    /// furnaces, which are made too).
    /// </summary>
    [Test]
    public static void GroundDataIsComplete()
    {
        GameContent content = GameContent.Read(ContentFixture.Folder);
        Assert.True(content.Problems.Count == 0, "problems:\n        " + string.Join("\n        ", content.Problems));
        Fixtures.Report($"{content.Items.Count} items, {content.Recipes.Count} recipes, {content.NaturalKinds.Count} natural kinds, " +
            $"{content.Deployables.Count} deployables, {content.LootTables.Count} loot tables; hash {content.Hash:X16}");
        Assert.True(content.Items.Count >= 40 && content.Recipes.Count >= 25, "the G2 catalogue is there");
        foreach (string key in new[] { "wood", "stone", "metal-ore", "sulphur-ore", "metal-fragments", "sulphur", "charcoal", "fibre",
                     "rock", "torch", "stone-hatchet", "stone-pickaxe", "metal-hatchet", "metal-pickaxe", "wooden-spear", "stone-spear",
                     "machete", "bow", "arrow", "bandage", "medical-injector", "woven-shirt", "woven-trousers", "work-boots", "woven-hood",
                     "woven-gloves", "chest-plate", "sleeping-bag", "campfire", "furnace", "small-box", "large-box", "workbench-1",
                     "workbench-2", "workbench-3", "red-berries", "blue-berries", "bed" })
        {
            Assert.True(content.Item(key) != null, $"item {key}");
        }
        Assert.True(content.Natural("red-berry-bush")?.Class == NaturalClass.Bush && content.Natural("blue-berry-bush") != null, "berry bushes");
        // G4: the guns, every kind of round, the attachments; the rifle's recoil is RustMVP's AK pattern, whose 29 steps
        // add up to 29.3677° up and 0.6145° right (weapons_character_ui.md §1.1).
        FirearmStats rifle = content.ItemByKey("rifle").Firearm!, pistol = content.ItemByKey("pistol").Firearm!;
        Assert.True(rifle.Mode == FireMode.Auto && rifle.Magazine == 30 && pistol.Mode == FireMode.Semi && pistol.Magazine == 15, "the two guns");
        Assert.Equal(64, rifle.IntervalSixteenths, "450 rounds a minute: a shot every 4 ticks");
        Assert.Equal(72, pistol.IntervalSixteenths, "400 rounds a minute: a shot every 4.5 ticks");
        Assert.Equal("rifle-ammo,rifle-ammo-hv,rifle-ammo-incendiary,rifle-ammo-explosive", string.Join(",", rifle.Ammo.Select(a => a.Key)),
            "the rifle's rounds (G5 adds the explosive ones)");
        Assert.Equal("pistol-ammo,pistol-ammo-hv,pistol-ammo-incendiary", string.Join(",", pistol.Ammo.Select(a => a.Key)), "the pistol's rounds");
        Assert.Equal("holo-sight,scope,silencer,weapon-light,muzzle-brake", string.Join(",", rifle.Attachments.Select(a => a.Key)), "the rifle's attachments");
        Assert.Equal(30, rifle.Recoil.Pattern.Count, "30 steps");
        Assert.Near(29.3677, rifle.Recoil.Pattern.Take(29).Sum(s => s.Pitch), 1e-3, "the pattern's pitch");
        Assert.Near(0.6145, rifle.Recoil.Pattern.Take(29).Sum(s => s.Yaw), 1e-3, "the pattern's yaw");
        Assert.True(content.Deployable("furnace")?.Device?.Cooks == "furnace", "the furnace smelts");
        Assert.Equal(1, content.Recipe(content.ItemByKey("stone-spear").Id)!.Ingredients.Count(i => i.Item.Key == "wooden-spear"),
            "recipes may need other crafted items");
        // The same files always give the same hash; a byte more gives another.
        Assert.Equal(content.Hash, GameContent.Read(ContentFixture.Folder).Hash, "hash is stable");
    }

    /// <summary>The checks catch what they are for: a reused ID, a reference to nothing, a recipe nothing can feed.</summary>
    [Test]
    public static void BrokenDataIsReported()
    {
        Dictionary<string, string> texts = GameContent.ReadTexts(ContentFixture.Folder);
        texts["items.json"] = texts["items.json"]
            .Replace("\"id\": 2, \"key\": \"stone\"", "\"id\": 1, \"key\": \"stone\"", StringComparison.Ordinal)
            .Replace("\"into\": \"sulphur\"", "\"into\": \"brimstone\"", StringComparison.Ordinal)
            .Replace("\"stack\": 64,", "\"stak\": 64,", StringComparison.Ordinal);
        texts["recipes.json"] = texts["recipes.json"].Replace("[\"stone\", 10]", "[\"unobtainium\", 10]", StringComparison.Ordinal)
            .Replace("[[\"wood\", 300]]", "[[\"refined-metal\", 1]], \"workbench\": 3", StringComparison.Ordinal);
        texts["gather.json"] = texts["gather.json"].Replace("[[\"metal-ore\", 250], [\"high-grade-ore\", 20]]", "[[\"metal-ore\", 250]]", StringComparison.Ordinal);
        // G7: the recycler turns loot into refined metal too; take that away as well.
        texts["world.json"] = texts["world.json"].Replace(", [\"refined-metal\", 1]]", "]", StringComparison.Ordinal)
            .Replace(", [\"refined-metal\", 2]]", "]", StringComparison.Ordinal);
        GameContent content = GameContent.FromTexts(texts);
        string all = string.Join("\n", content.Problems);
        Fixtures.Report($"{content.Problems.Count} problems found, e.g. {content.Problems.FirstOrDefault()}");
        Assert.True(all.Contains("id 1 is used by wood, stone", StringComparison.Ordinal), "a reused id");
        Assert.True(all.Contains("no item \"brimstone\"", StringComparison.Ordinal), "an unresolved reference");
        Assert.True(all.Contains("unknown field \"stak\"", StringComparison.Ordinal), "a typo");
        Assert.True(all.Contains("no item \"unobtainium\"", StringComparison.Ordinal), "an unknown ingredient");
        Assert.True(all.Contains("\"workbench-2\": can't be made from gatherable resources (nothing gives refined-metal)", StringComparison.Ordinal),
            "a recipe nothing can feed once metal outcrops give no rich ore: " + all);
        Assert.True(all.Contains("\"wooden-spear\"", StringComparison.Ordinal), "a recipe needing a workbench that can't be made");
        bool threw = false;
        try
        {
            string folder = Path.Combine(Path.GetTempPath(), $"hollow-content-{Guid.NewGuid():N}");
            Directory.CreateDirectory(folder);
            try
            {
                GameContent.Load(folder);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }
        catch (ContentException e)
        {
            threw = e.Message.Contains("items.json: missing or empty", StringComparison.Ordinal);
        }
        Assert.True(threw, "a missing file is an error, never a different game");
    }
    /// <summary>
    /// GROUND_SYSTEMS.md §12.6: one reader per data file. Every file in <see cref="GameContent.Files"/> (and the texts
    /// file) has exactly one reader in <see cref="ContentReaders.All"/>, and every reader reads one of those files: the
    /// registry is complete (§3.5).
    /// </summary>
    [Test]
    public static void EveryDataFileHasOneReader()
    {
        string[] files = GameContent.Files.Concat(GameContent.Unhashed).ToArray();
        foreach (string file in files)
        {
            Assert.Equal(1, ContentReaders.All.Count(r => r.File == file), $"readers of {file}");
        }
        foreach (IContentReader reader in ContentReaders.All.Where(r => r.File.Length > 0))
        {
            Assert.True(files.Contains(reader.File), $"{reader.GetType().Name} reads {reader.File}, which GameContent.Files doesn't list");
        }
        Fixtures.Report($"{ContentReaders.All.Count} readers for {files.Length} files, and the checks across them");
    }
}
