using System.Collections.Generic;
using Godot;
using HollowUniverse.Common;

namespace HollowUniverse.UI;

/// <summary>Where item icons come from. The icon package will render them from our models; until then, placeholders.</summary>
public interface IItemIconProvider
{
    /// <summary>A square icon for an item key, exactly <paramref name="pixels"/> wide, or null to draw nothing.</summary>
    Texture2D? Icon(string key, int pixels);
}

/// <summary>The interface's item icons (GROUND.md §8). Every screen asks here; swap <see cref="Provider"/> for real art.</summary>
public static class ItemIcons
{
    public static IItemIconProvider Provider { get; set; } = new PlaceholderItemIcons();

    public static Texture2D? Get(string key, int pixels) => pixels <= 0 ? null : Provider.Icon(key, pixels);
}

/// <summary>
/// Placeholder item art: flat vector drawings on a 128-unit square (RustMVP's own placeholder shapes, and new ones in
/// the same style), rasterised at the exact pixel size they are drawn at so they stay crisp at any scale. Nothing here
/// comes from another game's files.
/// </summary>
public sealed class PlaceholderItemIcons : IItemIconProvider
{
    private readonly Dictionary<(string Key, int Pixels), ImageTexture> _cache = new();

    public PlaceholderItemIcons() => Ui.ScaleChanged += _cache.Clear;

    public Texture2D? Icon(string key, int pixels)
    {
        if (_cache.TryGetValue((key, pixels), out ImageTexture? texture))
        {
            return texture;
        }
        var image = new Image();
        string svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"128\" height=\"128\" viewBox=\"0 0 128 128\">" + Shape(key) + "</svg>";
        if (image.LoadSvgFromString(svg, pixels / 128f) != Error.Ok)
        {
            return null;
        }
        texture = ImageTexture.CreateFromImage(image);
        _cache[(key, pixels)] = texture;
        return texture;
    }

    private const string Rock = "<path d=\"m17 69 27-36 53 9 14 28-30 22-54-4Z\" fill=\"{0}\"/><path d=\"m44 33 7 24 60 13-14-28Z\" fill=\"{1}\"/>" +
        "<path d=\"m17 69 34-12 30 35-54-4Z\" fill=\"{2}\"/>";

    private const string Pile = "<path d=\"m18 76 8-35 39-20 38 20 8 33-30 23-37-4Z\" fill=\"{0}\"/><path d=\"m26 41 40 8 37-8-27-16Z\" fill=\"{1}\"/>" +
        "<path d=\"m66 49 15 48 30-23-8-33Z\" fill=\"{2}\"/>";

    private const string Box = "<path d=\"m18 51 41-23 53 21-39 26Z\" fill=\"#a48d61\"/><path d=\"m18 51 55 24v33l-52-23Z\" fill=\"#7e6142\"/>" +
        "<path d=\"m73 75 39-26-3 35-36 24Z\" fill=\"#5a4935\"/><path d=\"m32 45 55 25v25M46 35l53 22v32M23 70l45 20\" fill=\"none\" stroke=\"#423f34\" stroke-width=\"5\"/>";

    private const string Gun = "<g transform=\"rotate(-24 64 64)\"><path d=\"M12 51h87v14H42l-6 29-14-5 7-24H12Z\" fill=\"#747b73\"/>" +
        "<path d=\"M51 65h14l-2 22-12-2Z\" fill=\"#404740\"/><path d=\"M99 53h21v7H99ZM26 84l10 3-2 10-14-5Z\" fill=\"#342f29\"/>" +
        "<path d=\"m12 52-8 24 22-11m46-1h18v22H70Z\" fill=\"#90714c\"/></g>";

    // Three rounds standing: brass cases, the tips in {0} (plain grey, high velocity blue).
    private const string Rounds = "<g fill=\"#c08a3a\"><path d=\"M34 56h14v44H34Z\"/><path d=\"M57 56h14v44H57Z\"/><path d=\"M80 56h14v44H80Z\"/></g>" +
        "<g fill=\"{0}\"><path d=\"M34 56c0-14 14-14 14 0Z\"/><path d=\"M57 56c0-14 14-14 14 0Z\"/><path d=\"M80 56c0-14 14-14 14 0Z\"/></g>";

    private const string Meat = "<path d=\"M24 70c0-24 30-40 54-34 22 6 30 30 18 46-14 18-60 18-72-12Z\" fill=\"{0}\"/>" +
        "<path d=\"M40 66c10-10 30-14 42-6\" fill=\"none\" stroke=\"{1}\" stroke-width=\"5\"/>" +
        "<path d=\"m92 80 14 14\" stroke=\"#e8e0d0\" stroke-width=\"8\" stroke-linecap=\"round\"/>";

    private static string Shape(string key) => key switch
    {
        "wood" => "<path d=\"M21 72 63 23 110 37 68 94Z\" fill=\"#8e7150\"/><path d=\"m26 73 44-48 8 2-42 51m15 4 43-48\" stroke=\"#493f30\" stroke-width=\"5\"/>" +
            "<ellipse cx=\"87\" cy=\"68\" rx=\"16\" ry=\"37\" transform=\"rotate(42 87 68)\" fill=\"#b1946a\"/>" +
            "<ellipse cx=\"88\" cy=\"69\" rx=\"8\" ry=\"28\" transform=\"rotate(42 88 69)\" fill=\"none\" stroke=\"#705839\" stroke-width=\"3\"/>",
        "stones" => string.Format(Pile, "#85867f", "#b9b9a9", "#5c625b"),
        "charcoal" => string.Format(Pile, "#3b3a37", "#5a5853", "#262523"),
        "metal.ore" => string.Format(Rock, "#8a7464", "#b19a86", "#5f5248"),
        "sulfur.ore" => string.Format(Rock, "#b8aa58", "#d8cd84", "#7d743c"),
        "metal.fragments" => string.Format(Rock, "#8c9a9b", "#bec4bd", "#737d7b"),
        "metal.refined" => string.Format(Rock, "#bac6c6", "#dfe5e1", "#8a9696"),
        "cloth" => "<path d=\"m36 22 46 5 23 68-69 4-19-14Z\" fill=\"#bcb69a\"/>" +
            "<path d=\"m36 22 16 58-16 19m16-19 37 5M48 43l34-16\" stroke=\"#767867\" fill=\"none\" stroke-width=\"4\"/>",
        "fat" => "<path d=\"M26 82c0-30 26-50 50-46 24 4 30 30 22 46-10 20-62 26-72 0Z\" fill=\"#e2d3a0\"/>" +
            "<path d=\"M40 70c12-12 30-14 44-8\" fill=\"none\" stroke=\"#c2ad70\" stroke-width=\"5\"/>",
        "meat.raw" => string.Format(Meat, "#b5524a", "#e8c8b8"),
        "meat.cooked" => string.Format(Meat, "#7a4a2a", "#b8865a"),
        "plan" => "<path d=\"M23 30h78v63H23Z\" fill=\"#557e93\"/><path d=\"M31 22h11v78H27V35m59-13h14v78H85V35\" fill=\"#d3c9a7\"/>" +
            "<path d=\"m46 69 16-21 16 21v14H46Zm17-21v35\" stroke=\"#dce6dc\" fill=\"none\" stroke-width=\"3\"/>",
        "hammer" => "<path d=\"m28 107 43-64 13 7-42 66Z\" fill=\"#927248\"/><path d=\"m45 34 15-18 42 27-8 30-49-22Z\" fill=\"#77664c\"/>" +
            "<path d=\"m45 34 49 33 8-24-42-27Z\" fill=\"#b2976b\"/>",
        "rock" => "<path d=\"m24 78 10-30 30-16 32 10 12 28-14 22-40 6Z\" fill=\"#8a8a82\"/><path d=\"m34 48 30-16 32 10-28 10Z\" fill=\"#b3b3a8\"/>" +
            "<path d=\"m68 52 28-10 12 28-14 22Z\" fill=\"#62645e\"/>",
        "hatchet.stone" => "<path d=\"m34 108 36-78 10 4-36 78Z\" fill=\"#8a6a44\"/><path d=\"m58 26 30-6 14 16-8 22-26-10Z\" fill=\"#8d8f88\"/>" +
            "<path d=\"m58 26 30-6-6 14Z\" fill=\"#b8b9ae\"/>",
        "pickaxe.stone" => "<path d=\"m42 110 28-78 10 4-28 78Z\" fill=\"#8a6a44\"/><path d=\"m20 42 32-18h30l26 18-30-8H50Z\" fill=\"#8d8f88\"/>",
        "torch" => "<path d=\"m52 112 14-60 10 2-14 60Z\" fill=\"#7a5a38\"/><path d=\"m60 52 18 4 2-10-18-4Z\" fill=\"#5a4a3a\"/>" +
            "<path d=\"M72 46c-14-6-10-22 2-34 0 10 10 12 8 22-1 6-4 10-10 12Z\" fill=\"#e0a040\"/>" +
            "<path d=\"M73 42c-6-4-4-12 2-18 0 6 5 8 3 13Z\" fill=\"#f6d27a\"/>",
        "bandage" => "<path d=\"m31 31 50-3 22 52-48 23-31-46Z\" fill=\"#c2b699\"/>" +
            "<path d=\"m43 44 25-4 7 20 19-5 6 17-21 6 6 17-22 8-6-21-18 3-5-17 18-5Z\" fill=\"#954b41\"/>",
        "syringe" => "<g transform=\"rotate(-40 64 64)\"><path d=\"M28 56h56v16H28Z\" fill=\"#cfd6d6\"/><path d=\"M36 58h30v12H36Z\" fill=\"#c04a3a\"/>" +
            "<path d=\"M84 60h22v8H84ZM18 54h10v20H18ZM106 63h12v2h-12Z\" fill=\"#6c7476\"/></g>",
        "shirt.cloth" => "<path d=\"m45 24 11 8 15-2 12-6 29 18-18 24-12-10 4 51H39l3-51-13 9-15-23Z\" fill=\"#aa9474\"/>" +
            "<path d=\"m45 24 12 22 14-16m-29 26 40 1M48 76l25 2\" fill=\"none\" stroke=\"#756854\" stroke-width=\"4\"/>",
        "trousers.cloth" => "<path d=\"m38 20 50 3-2 84-21 2-4-52-7 50-23-2Z\" fill=\"#807159\"/>" +
            "<path d=\"m38 32 48 3M61 38v19M35 79l18 2m14 1 18 1\" stroke=\"#b19c75\" stroke-width=\"4\"/>",
        "hood.cloth" => "<path d=\"M30 96c-4-40 6-70 34-74 28 4 38 34 34 74l-14-4c2-24-6-40-20-40s-22 16-20 40Z\" fill=\"#8e8063\"/>" +
            "<path d=\"M44 92c-2-24 6-40 20-40s22 16 20 40\" fill=\"none\" stroke=\"#5e5440\" stroke-width=\"4\"/>",
        "boots.hide" => "<path d=\"M30 22h30v52l40 8v22H30Z\" fill=\"#7a5a3c\"/><path d=\"M30 92h70v12H30Z\" fill=\"#4a3a2a\"/>" +
            "<path d=\"M30 34h30\" stroke=\"#a07a52\" stroke-width=\"5\"/>",
        "spear.wooden" => "<path d=\"m22 112 74-90 6 5-74 90Z\" fill=\"#9a7a4c\"/><path d=\"m92 18 16-8-6 18Z\" fill=\"#c9b48a\"/>",
        "bow" => "<path d=\"M38 16c40 20 56 60 42 98\" fill=\"none\" stroke=\"#8a6a44\" stroke-width=\"9\" stroke-linecap=\"round\"/>" +
            "<path d=\"M40 18 78 112\" stroke=\"#dcd6c4\" stroke-width=\"2\"/>",
        "arrow.wooden" => "<path d=\"m24 104 74-74\" stroke=\"#a0804c\" stroke-width=\"5\"/><path d=\"m98 30 12-12-4 16Z\" fill=\"#8d8f88\"/>" +
            "<path d=\"m24 104-8 2 6-14 10 4Z\" fill=\"#c9c2ae\"/>",
        "pistol" => "<g transform=\"rotate(-20 64 64)\"><path d=\"M24 50h64v14H46l-4 26-14-3 4-23H24Z\" fill=\"#6a706a\"/>" +
            "<path d=\"M30 64h12l-4 26-14-3Z\" fill=\"#5a4636\"/><path d=\"M88 52h8v8h-8ZM26 46h10v4H26Z\" fill=\"#342f29\"/></g>",
        "rifle" => Gun,
        "ammo.pistol" => string.Format(Rounds, "#a8adb0"),
        "ammo.pistol.hv" => string.Format(Rounds, "#4f86c6"),
        "box.wooden" => "<g transform=\"translate(14 18) scale(0.8)\">" + Box + "</g>",
        "box.large" => Box,
        "bag.sleeping" => "<path d=\"m20 50 70-16 20 40-70 18Z\" fill=\"#6f7a54\"/><path d=\"m20 50 70-16 6 12-70 16Z\" fill=\"#8c9868\"/>" +
            "<path d=\"m42 66 8 22\" stroke=\"#4e5638\" stroke-width=\"4\"/>",
        "campfire" => "<path d=\"m22 96 80-26 4 10-80 26Z\" fill=\"#6e5236\"/><path d=\"m26 70 80 26-4 10-80-26Z\" fill=\"#8a6a44\"/>" +
            "<path d=\"M64 84c-20-6-20-30-2-50 0 14 16 18 14 32-1 10-5 16-12 18Z\" fill=\"#e0902e\"/>" +
            "<path d=\"M64 80c-8-4-8-16 0-24 0 8 7 10 6 16Z\" fill=\"#f6d27a\"/>",
        "furnace" => "<path d=\"M30 30h68v80H30Z\" fill=\"#6f6a62\"/><path d=\"M30 30h68v12H30Z\" fill=\"#8d877d\"/>" +
            "<path d=\"M48 66h32v28H48Z\" fill=\"#2c2622\"/><path d=\"M52 82h24v10H52Z\" fill=\"#e0802e\"/>",
        "workbench.1" => "<path d=\"m18 45 67-17 30 25-67 20Z\" fill=\"#8c8066\"/><path d=\"M23 53v50m26-29v38m58-50v41M29 30v18m44-31v23\" stroke=\"#5b7777\" stroke-width=\"9\"/>" +
            "<path d=\"M30 28h47v15H30Z\" fill=\"#9da397\"/>",
        "claim" => "<path d=\"m35 17 55 5v90l-55-5Z\" fill=\"#775b40\"/><path d=\"m35 17-10 9v79l10 2m55-85 13 8v76l-13 6\" fill=\"#443f34\"/>" +
            "<path d=\"M63 24v80M39 63l18 1m12 0 16 1\" stroke=\"#b29f77\" stroke-width=\"3\"/>",
        "door.wooden" => "<path d=\"M38 14h52v100H38Z\" fill=\"#8a6a44\" stroke=\"#5a4430\" stroke-width=\"4\"/>" +
            "<path d=\"M50 32h28M50 52h28M50 72h28M50 92h28\" stroke=\"#6e5236\" stroke-width=\"4\"/><path d=\"M78 62h6v8h-6Z\" fill=\"#c9c2ae\"/>",
        "lock.key" => "<path d=\"M42 58h44v42H42Z\" fill=\"#9a9a90\"/><path d=\"M50 58V44c0-18 28-18 28 0v14\" fill=\"none\" stroke=\"#6c6c64\" stroke-width=\"7\"/>" +
            "<path d=\"M60 72h8v14h-8Z\" fill=\"#3c3c38\"/>",
        // G7: salvage, components, schematics, the blueprint and the research table (our own shapes, until rendered).
        "salvage" => "<path d=\"m18 82 30-30 24 10-8 34-34 4Z\" fill=\"#7d8078\"/><path d=\"m56 40 34 6 14 28-26 18-16-20Z\" fill=\"#9c7a52\"/><path d=\"m48 52 24 10\" stroke=\"#5a5c56\" stroke-width=\"5\"/><circle cx=\"86\" cy=\"94\" r=\"12\" fill=\"#a8aaa2\"/><circle cx=\"86\" cy=\"94\" r=\"5\" fill=\"#5f625b\"/><path d=\"M30 40c6-8 16-8 22 0s16 8 22 0\" fill=\"none\" stroke=\"#c49a5c\" stroke-width=\"5\"/>",
        "gears" => "<path d=\"M64 22l8 0 3 12 10 4 10-7 6 6-7 10 4 10 12 3v8l-12 3-4 10 7 10-6 6-10-7-10 4-3 12h-8l-3-12-10-4-10 7-6-6 7-10-4-10-12-3v-8l12-3 4-10-7-10 6-6 10 7 10-4Z\" fill=\"#8f938c\"/><circle cx=\"68\" cy=\"64\" r=\"16\" fill=\"#4f534d\"/><circle cx=\"68\" cy=\"64\" r=\"8\" fill=\"#b8bbb2\"/>",
        "springs" => "<path d=\"M40 30h48M40 98h48\" stroke=\"#6c706a\" stroke-width=\"7\" stroke-linecap=\"round\"/><path d=\"M42 38l44 8-44 8 44 8-44 8 44 8-44 8 44 8\" fill=\"none\" stroke=\"#b5b8ae\" stroke-width=\"6\" stroke-linejoin=\"round\"/>",
        "metal-pipe" => "<path d=\"m26 88 62-62 14 14-62 62Z\" fill=\"#8d918a\"/><path d=\"m26 88 62-62 4 4-62 62Z\" fill=\"#b9bcb4\"/><ellipse cx=\"95\" cy=\"33\" rx=\"10\" ry=\"5\" transform=\"rotate(-45 95 33)\" fill=\"#3c3f3a\"/>",
        "rope" => "<ellipse cx=\"64\" cy=\"66\" rx=\"40\" ry=\"30\" fill=\"none\" stroke=\"#a88d5c\" stroke-width=\"12\"/><ellipse cx=\"64\" cy=\"66\" rx=\"26\" ry=\"18\" fill=\"none\" stroke=\"#c4aa76\" stroke-width=\"10\"/><path d=\"M98 80c8 10 10 22 4 30\" fill=\"none\" stroke=\"#a88d5c\" stroke-width=\"9\"/>",
        "sewing-kit" => "<path d=\"M24 46h80v54H24Z\" fill=\"#8d6a4a\"/><path d=\"M24 46h80v12H24Z\" fill=\"#a8835e\"/><path d=\"M40 70h22v18H40Z\" fill=\"#c9c2ae\"/><path d=\"m74 66 22 26\" stroke=\"#d6d8d0\" stroke-width=\"4\"/><path d=\"M80 64c10 4 12 14 4 20\" fill=\"none\" stroke=\"#c24a3a\" stroke-width=\"3\"/>",
        "tech-scrap" => "<path d=\"m22 40 76-12 10 56-76 14Z\" fill=\"#3f6a4a\"/><path d=\"M40 52h20v16H40Zm34-6h16v12H74Z\" fill=\"#2c2f2c\"/><path d=\"M34 80h56M60 60v22M52 42l4 12\" stroke=\"#c7a54a\" stroke-width=\"3\"/><circle cx=\"86\" cy=\"74\" r=\"5\" fill=\"#b8b8b0\"/>",
        "metal-blade" => "<path d=\"M22 92 96 26l10 10-62 70Z\" fill=\"#c2c6be\"/><path d=\"M22 92 96 26l3 3-74 66Z\" fill=\"#e2e5de\"/><path d=\"M30 84l8 8M44 72l8 8M58 58l8 8M72 44l8 8\" stroke=\"#7d817a\" stroke-width=\"3\"/>",
        "sheet-metal" => "<path d=\"m18 54 70-26 24 40-70 28Z\" fill=\"#9a9e96\"/><path d=\"m18 54 70-26 4 6-70 26Z\" fill=\"#c4c7bf\"/><path d=\"m26 74 64-24M34 86l64-24\" stroke=\"#7a7e77\" stroke-width=\"2\"/>",
        "sign-plate" => "<path d=\"M24 34h80v56H24Z\" fill=\"#d7c25a\"/><path d=\"M24 34h80v56H24Z\" fill=\"none\" stroke=\"#5c5644\" stroke-width=\"5\"/><path d=\"m36 82 16-36 16 36M74 46l18 36\" stroke=\"#3a3830\" stroke-width=\"7\"/><path d=\"M60 90v26\" stroke=\"#6c6c64\" stroke-width=\"7\"/>",
        "receiver" => "<g transform=\"rotate(-18 64 64)\"><path d=\"M20 52h78v18H54l-6 24-14-3 5-21H20Z\" fill=\"#6b716a\"/><path d=\"M28 56h64v6H28Z\" fill=\"#8c938b\"/><path d=\"M98 54h12v10H98Z\" fill=\"#3c403b\"/></g>",
        "rifle-frame" => "<g transform=\"rotate(-20 64 64)\"><path d=\"M14 52h70v16H50l-4 22-12-3 3-19H14Z\" fill=\"#6b716a\"/><path d=\"M84 50h28l4 22-32-4Z\" fill=\"#8a6a44\"/><path d=\"M58 68h12l-2 22-12-2Z\" fill=\"#3c403b\"/></g>",
        "torn-schematic" => "<path d=\"m24 28 72-4 8 74-28 8-6-14-16 12-28-4Z\" fill=\"#cfd8de\"/><path d=\"M36 44h48M36 58h40M36 72h30\" stroke=\"#4b6f96\" stroke-width=\"4\"/><path d=\"m60 88 8-18 10 10\" fill=\"none\" stroke=\"#4b6f96\" stroke-width=\"3\"/>",
        "sealed-schematic" => "<path d=\"M26 26h76v80H26Z\" fill=\"#7a6a4c\"/><path d=\"M34 34h60v64H34Z\" fill=\"#cfd8de\"/><path d=\"M42 48h44M42 62h36M42 76h28\" stroke=\"#4b6f96\" stroke-width=\"4\"/><circle cx=\"82\" cy=\"88\" r=\"10\" fill=\"#b8402e\"/>",
        "blueprint" => "<path d=\"M22 26h84v78H22Z\" fill=\"#2f5f96\"/><path d=\"M30 34h68v62H30Z\" fill=\"none\" stroke=\"#9fc2e8\" stroke-width=\"3\"/><path d=\"M42 50h44M42 64h36M42 78h28\" stroke=\"#d8e8f8\" stroke-width=\"4\"/><circle cx=\"84\" cy=\"82\" r=\"8\" fill=\"none\" stroke=\"#d8e8f8\" stroke-width=\"3\"/>",
        "research-table" => "<path d=\"m16 54 72-20 26 22-72 22Z\" fill=\"#7d6a52\"/><path d=\"M22 60v42m24-26v38m58-46v36\" stroke=\"#5a5c58\" stroke-width=\"8\"/><path d=\"M58 30v18M50 30h16\" stroke=\"#9a9d96\" stroke-width=\"5\"/><circle cx=\"58\" cy=\"26\" r=\"7\" fill=\"#e8d27a\"/><path d=\"M70 44h22v10H70Z\" fill=\"#2f5f96\"/>",
        _ => "<path d=\"m28 37 28-15 48 27-10 46-46 8-27-36Z\" fill=\"#7f8273\"/>" +
            "<path d=\"m36 45 27-13 29 20-11 28-27 5-22-18Z\" fill=\"none\" stroke=\"#b0ad91\" stroke-width=\"9\"/>",
    };
}

/// <summary>The HUD's white glyphs on a 32-unit square (vitals and statuses), tinted when drawn; rasterised at their final size.</summary>
public static class GroundGlyphs
{
    private static readonly Dictionary<(string Name, int Pixels), ImageTexture> Cache = new();

    static GroundGlyphs() => Ui.ScaleChanged += Cache.Clear;

    public static Texture2D? Get(string name, int pixels)
    {
        if (pixels <= 0)
        {
            return null;
        }
        if (Cache.TryGetValue((name, pixels), out ImageTexture? texture))
        {
            return texture;
        }
        var image = new Image();
        string svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"32\" height=\"32\" viewBox=\"0 0 32 32\" fill=\"#ffffff\">" + Shape(name) + "</svg>";
        if (image.LoadSvgFromString(svg, pixels / 32f) != Error.Ok)
        {
            return null;
        }
        texture = ImageTexture.CreateFromImage(image);
        Cache[(name, pixels)] = texture;
        return texture;
    }

    private const string Line = "fill=\"none\" stroke=\"#ffffff\" stroke-width=\"2.6\" stroke-linecap=\"round\" stroke-linejoin=\"round\"";

    private static string Shape(string name) => name switch
    {
        // RustMVP's vitals symbols.
        "health" => "<path d=\"M13 5h6v8h8v6h-8v8h-6v-8H5v-6h8Z\"/>",
        "water" => "<path d=\"m7 6 18 0-3 22H10Zm3 3 2 16h8l2-16Z\" fill-rule=\"evenodd\"/>",
        "food" => "<path d=\"M7 4h2v7h2V4h2v7h2V4h2v9c0 3-2 5-4 5v10h-4V18c-2 0-4-2-4-5V4Zm17 0h2v24h-4V18h-3V9c0-3 2-5 5-5Z\"/>",
        // Statuses.
        "bleeding" => "<path d=\"M16 3C12 10 7 15 7 20a9 9 0 0 0 18 0c0-5-5-10-9-17Z\"/>",
        "wet" => "<path d=\"M11 4C8 9 5 12 5 16a6 6 0 0 0 12 0c0-4-3-7-6-12Zm11 10c-2 4-4 6-4 9a4 4 0 0 0 8 0c0-3-2-5-4-9Z\"/>",
        "cold" => $"<path d=\"M16 3v26M4.7 9.5l22.6 13M4.7 22.5l22.6-13M12 5l4 3 4-3M12 27l4-3 4 3\" {Line}/>",
        "hot" => $"<circle cx=\"16\" cy=\"16\" r=\"6\"/><path d=\"M16 2v4M16 26v4M2 16h4M26 16h4M6 6l3 3M23 23l3 3M26 6l-3 3M9 23l-3 3\" {Line}/>",
        "comfort" => "<path d=\"M16 28 5 17a6.5 6.5 0 0 1 11-9 6.5 6.5 0 0 1 11 9Z\"/>",
        "privilege" => "<path d=\"m5 25 13-13 3 3L8 28Z\"/><path d=\"m14 9 6-5 9 9-5 6Z\"/>",
        "blocked" => $"<circle cx=\"16\" cy=\"16\" r=\"11\" {Line}/><path d=\"M8 8l16 16\" {Line}/>",
        "clock" => $"<circle cx=\"16\" cy=\"16\" r=\"11\" {Line}/><path d=\"M16 9v7h5\" {Line}/>",
        "yield" => $"<circle cx=\"16\" cy=\"16\" r=\"11\" {Line}/><path d=\"M11 16h10M16 11v10\" {Line}/>",
        "skull" => "<path d=\"M16 4C9 4 5 9 5 15c0 4 2 6 4 7v4h14v-4c2-1 4-3 4-7 0-6-4-11-11-11Zm-5 10a3 3 0 1 1 0 6 3 3 0 0 1 0-6Zm10 0a3 3 0 1 1 0 6 3 3 0 0 1 0-6Z\" fill-rule=\"evenodd\"/>",
        "bag" => "<path d=\"M6 10h20l2 16H4Z\"/><path d=\"M11 10V8a5 5 0 0 1 10 0v2\" fill=\"none\" stroke=\"#ffffff\" stroke-width=\"2.6\"/>",
        "person" => "<circle cx=\"16\" cy=\"10\" r=\"6\"/><path d=\"M4 30c0-8 5-12 12-12s12 4 12 12Z\"/>",
        // G7: the map's bed, a known tech tree node.
        "bed" => "<path d=\"M3 14h4v4h20a3 3 0 0 1 3 3v6h-4v-3H7v3H3Z\"/><path d=\"M8 12h8a2 2 0 0 1 2 2v3H8Z\"/>",
        "check" => $"<path d=\"M6 17l7 7 13-15\" {Line}/>",
        _ => "<circle cx=\"16\" cy=\"16\" r=\"6\"/>",
    };
}
