using System.Collections.Generic;
using Godot;
using HollowUniverse.Common;
using HollowUniverse.UI;

namespace HollowUniverse.Ground;

/// <summary>
/// The ground game's item icons until the assets package renders them from our models (GROUND.md §8): items.json keys
/// mapped onto the interface's placeholder drawings (which use RustMVP's keys), and a few more drawings in the same flat
/// style for items it has none of (berries, tubers, gloves, the chest plate, lamp oil). The seam: set
/// <see cref="ItemIcons.Provider"/> to the catalogue's icons when they land; keys this doesn't know fall through to
/// <see cref="Inner"/>.
/// </summary>
public sealed class ItemIconAliases : IItemIconProvider
{
    private static readonly Dictionary<string, string> Aliases = new()
    {
        ["stone"] = "stones",
        ["metal-ore"] = "metal.ore",
        ["sulphur-ore"] = "sulfur.ore",
        ["high-grade-ore"] = "metal.ore",
        ["metal-fragments"] = "metal.fragments",
        ["refined-metal"] = "metal.refined",
        ["fibre"] = "cloth",
        ["stone-hatchet"] = "hatchet.stone",
        ["metal-hatchet"] = "hatchet.stone",
        ["stone-pickaxe"] = "pickaxe.stone",
        ["metal-pickaxe"] = "pickaxe.stone",
        ["wooden-spear"] = "spear.wooden",
        ["stone-spear"] = "spear.wooden",
        ["arrow"] = "arrow.wooden",
        ["medical-injector"] = "syringe",
        ["woven-hood"] = "hood.cloth",
        ["woven-shirt"] = "shirt.cloth",
        ["woven-trousers"] = "trousers.cloth",
        ["work-boots"] = "boots.hide",
        ["sleeping-bag"] = "bag.sleeping",
        ["bed"] = "bag.sleeping",
        ["small-box"] = "box.wooden",
        ["large-box"] = "box.large",
        ["workbench-1"] = "workbench.1",
        ["workbench-2"] = "workbench.1",
        ["workbench-3"] = "workbench.1",
    };

    private const string Berries = "<g fill=\"{0}\"><circle cx=\"52\" cy=\"70\" r=\"15\"/><circle cx=\"76\" cy=\"64\" r=\"15\"/><circle cx=\"64\" cy=\"88\" r=\"15\"/>" +
        "<circle cx=\"40\" cy=\"92\" r=\"13\"/><circle cx=\"88\" cy=\"88\" r=\"13\"/></g><g fill=\"{1}\"><circle cx=\"48\" cy=\"65\" r=\"4\"/>" +
        "<circle cx=\"72\" cy=\"59\" r=\"4\"/><circle cx=\"60\" cy=\"83\" r=\"4\"/></g><path d=\"M64 50c2-14 10-22 24-26-6 10-14 18-24 26Z\" fill=\"#5f7a3a\"/>";

    private static readonly Dictionary<string, string> Drawings = new()
    {
        ["red-berries"] = string.Format(Berries, "#b3262a", "#e7797a"),
        ["blue-berries"] = string.Format(Berries, "#2c3a7a", "#7f8fcf"),
        ["wild-tuber"] = "<path d=\"M30 74c-6-22 14-38 36-34 24 4 38 22 30 42-8 18-58 20-66-8Z\" fill=\"#8a6a44\"/>" +
            "<path d=\"M46 58c6-2 10 2 8 6M70 76c6 0 8 4 6 8M84 56c4 0 6 4 4 6\" fill=\"none\" stroke=\"#5a4430\" stroke-width=\"4\"/>" +
            "<path d=\"M58 40c-4-12 0-22 10-28-2 10 0 18 4 26Z\" fill=\"#6a8a3a\"/>",
        ["roasted-tuber"] = "<path d=\"M30 74c-6-22 14-38 36-34 24 4 38 22 30 42-8 18-58 20-66-8Z\" fill=\"#5a3a22\"/>" +
            "<path d=\"M40 70c10-8 30-10 44-2\" fill=\"none\" stroke=\"#c8925a\" stroke-width=\"6\"/>",
        ["lamp-oil"] = "<path d=\"M46 34h36v12l10 10v52H36V56l10-10Z\" fill=\"#5f6a5a\"/><path d=\"M40 70h48v30H40Z\" fill=\"#c9a24a\"/>" +
            "<path d=\"M52 22h24v12H52Z\" fill=\"#3e3a34\"/>",
        ["sulphur"] = "<path d=\"m18 76 8-35 39-20 38 20 8 33-30 23-37-4Z\" fill=\"#d8c64a\"/><path d=\"m26 41 40 8 37-8-27-16Z\" fill=\"#efe28a\"/>" +
            "<path d=\"m66 49 15 48 30-23-8-33Z\" fill=\"#a89632\"/>",
        ["woven-gloves"] = "<path d=\"M40 110V64l-10-24 8-4 12 20V24h8v30-36h8v36-30h8v32l8-16 8 4-12 30v56Z\" fill=\"#aa9474\"/>" +
            "<path d=\"M40 96h48v14H40Z\" fill=\"#807159\"/>",
        ["chest-plate"] = "<path d=\"M30 30l20-10h28l20 10 4 40-10 36H36L26 70Z\" fill=\"#8d949a\"/>" +
            "<path d=\"M50 20c4 10 24 10 28 0M64 34v66\" fill=\"none\" stroke=\"#5f666c\" stroke-width=\"5\"/>" +
            "<g fill=\"#c9cdd0\"><circle cx=\"40\" cy=\"44\" r=\"4\"/><circle cx=\"88\" cy=\"44\" r=\"4\"/><circle cx=\"44\" cy=\"90\" r=\"4\"/><circle cx=\"84\" cy=\"90\" r=\"4\"/></g>",
        ["machete"] = "<path d=\"m22 106 18-18 8 8-18 18Z\" fill=\"#5a4430\"/><path d=\"m40 88 60-62c6 6 6 16 0 22L48 96Z\" fill=\"#a6acae\"/>",
    };

    private readonly Dictionary<(string, int), ImageTexture> _cache = new();

    public ItemIconAliases(IItemIconProvider inner)
    {
        Inner = inner;
        Ui.ScaleChanged += _cache.Clear;
    }

    /// <summary>The provider asked for keys this doesn't draw itself.</summary>
    public IItemIconProvider Inner { get; }

    public Texture2D? Icon(string key, int pixels)
    {
        if (Drawings.TryGetValue(key, out string? svg))
        {
            if (!_cache.TryGetValue((key, pixels), out ImageTexture? texture))
            {
                var image = new Image();
                if (image.LoadSvgFromString("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"128\" height=\"128\" viewBox=\"0 0 128 128\">" + svg + "</svg>",
                        pixels / 128f) != Error.Ok)
                {
                    return null;
                }
                texture = ImageTexture.CreateFromImage(image);
                _cache[(key, pixels)] = texture;
            }
            return texture;
        }
        return Inner.Icon(Aliases.TryGetValue(key, out string? alias) ? alias : key, pixels);
    }
}
