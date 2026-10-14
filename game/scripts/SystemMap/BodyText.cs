using System;
using System.Collections.Generic;
using Hollow.Galaxy.Systems;
using HollowUniverse.Common;

namespace HollowUniverse.SystemMap;

/// <summary>Words for a body's physical state (GALAXY.md §10.3) on the body card (presentation only).</summary>
public static class BodyText
{
    /// <summary>What the surface is, with its liquids and ice, e.g. "Rock · oceans 27% · ice 7%".</summary>
    public static string Surface(PlanetState s)
    {
        if (s.Surface == SurfaceKind.None)
        {
            return "None: a deep atmosphere";
        }
        if (s.Surface == SurfaceKind.Ocean)
        {
            return s.Liquid == LiquidKind.AmmoniaWater ? "Global ammonia–water sea" : "Global ocean";
        }
        var parts = new List<string> { s.Surface == SurfaceKind.Ice ? "Ice" : "Rock" };
        if (s.OceanCover > 0.005)
        {
            string liquid = s.Liquid switch
            {
                LiquidKind.Water => "oceans",
                LiquidKind.Magma => "lava seas",
                LiquidKind.Hydrocarbons => "methane lakes",
                LiquidKind.AmmoniaWater => "ammonia–water seas",
                _ => "",
            };
            if (liquid.Length > 0)
            {
                parts.Add(liquid + " " + Percent(s.OceanCover));
            }
        }
        if (s.Surface == SurfaceKind.Rock && s.IceCover >= 0.02)
        {
            parts.Add("ice " + Percent(s.IceCover));
        }
        return string.Join(" · ", parts);
    }

    /// <summary>Bulk composition, largest share first, e.g. "Rock 78% · metal 22%".</summary>
    public static string Composition(Composition c)
    {
        var shares = new List<(string Name, float Share)>
        {
            ("metal", c.Metal), ("rock", c.Rock), ("ices", c.Ice), ("hydrogen–helium", c.Gas),
        };
        shares.Sort((x, y) => y.Share.CompareTo(x.Share));
        var parts = new List<string>();
        foreach ((string name, float share) in shares)
        {
            if (share >= 0.01f)
            {
                parts.Add(name + " " + Percent(share));
            }
        }
        string text = string.Join(" · ", parts);
        return text.Length > 0 ? char.ToUpperInvariant(text[0]) + text[1..] : text;
    }

    /// <summary>Crust and volcanism, e.g. "Plate tectonics · lava volcanoes".</summary>
    public static string Geology(PlanetState s)
    {
        string crust = s.Tectonics switch
        {
            Tectonics.Plates => "Plate tectonics",
            Tectonics.StagnantLid => "Stagnant lid",
            Tectonics.Molten => "Molten surface",
            _ => "Geologically dead",
        };
        string volcanoes = s.Volcanism switch
        {
            Volcanism.Silicate => "lava volcanoes",
            Volcanism.Sulphur => "sulphur volcanoes",
            Volcanism.Water => "water geysers",
            Volcanism.AmmoniaWater => "ammonia–water cryovolcanoes",
            Volcanism.Nitrogen => "nitrogen geysers",
            _ => "",
        };
        if (volcanoes.Length > 0 && s.VolcanicActivity > 0.5)
        {
            volcanoes = "very active " + volcanoes;
        }
        return volcanoes.Length > 0 ? crust + " · " + volcanoes : crust;
    }

    public static string Life(Biosphere b) => b switch
    {
        Biosphere.Microbial => "Microbes",
        Biosphere.Simple => "Simple life; oxygen in the air",
        Biosphere.Complex => "Plants and animals",
        _ => "None",
    };

    /// <summary>The dangers, comma separated, or "None".</summary>
    public static string HazardList(Hazards h)
    {
        if (h == Hazards.None)
        {
            return "None";
        }
        var parts = new List<string>();
        foreach (Hazards flag in Enum.GetValues<Hazards>())
        {
            if (flag != Hazards.None && (h & flag) != 0)
            {
                parts.Add(HazardName(flag));
            }
        }
        string text = string.Join(", ", parts).ToLowerInvariant();
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    public static string HazardName(Hazards h) => h switch
    {
        Hazards.Vacuum => "Vacuum",
        Hazards.ExtremeHeat => "Extreme heat",
        Hazards.ExtremeCold => "Extreme cold",
        Hazards.HighPressure => "Crushing pressure",
        Hazards.ToxicAtmosphere => "Toxic air",
        Hazards.Corrosive => "Acid",
        Hazards.HighGravity => "High gravity",
        Hazards.Volcanic => "Eruptions",
        Hazards.Radiation => "Radiation",
        Hazards.Molten => "Molten ground",
        Hazards.Storms => "Storms",
        Hazards.Quakes => "Quakes",
        _ => h.ToString(),
    };

    public static string MaterialName(Material m) => m switch
    {
        Material.Icy => "Ice",
        Material.Rocky => "Rock and dust",
        Material.MetalRich => "Metal-rich rock",
        Material.Metallic => "Metal",
        _ => "Unknown",
    };

    private static string Percent(double share) => Ui.Number(share * 100, share < 0.1 ? "0.#" : "N0") + "%";
}
