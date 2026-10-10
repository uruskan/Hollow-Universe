using System;
using System.Globalization;

namespace Hollow.Galaxy;

/// <summary>What kind of object a star is. Stored as a byte; append only, never reorder.</summary>
public enum StarKind : byte
{
    MainSequence = 0,
    Subgiant = 1,
    Giant = 2,
    BrightGiant = 3,
    Supergiant = 4,
    WhiteDwarf = 5,
    BrownDwarf = 6,
    NeutronStar = 7,
    BlackHole = 8,
    WolfRayet = 9,
    CarbonStar = 10,
    TTauri = 11,
    HerbigAeBe = 12,
}

/// <summary>Stellar populations with their own mix of star classes (GALAXY.md §4).</summary>
public enum Population : byte
{
    Young = 0,
    Disk = 1,
    Old = 2,
}

/// <summary>Density-model components (GALAXY.md §3), plus the real-star catalogue.</summary>
public enum GalaxyComponent : byte
{
    YoungDisk = 0,
    ThinDisk = 1,
    ThickDisk = 2,
    Bulge = 3,
    NuclearDisk = 4,
    Halo = 5,
    Catalogue = 255,
}

/// <summary>
/// A star's classification: kind plus a numeric spectral code. Code = 10 × letter index + subclass, with
/// letters O B A F G K M L T Y → 0…9 (so G2 = 42, M4.5 = 64.5, T6 = 86). White dwarfs use the code for the
/// temperature index (DA2 → 2); neutron stars and black holes use 0.
/// </summary>
public readonly struct StarClass : IEquatable<StarClass>
{
    private const string Letters = "OBAFGKMLTY";

    public readonly StarKind Kind;
    public readonly float Code;

    public StarClass(StarKind kind, float code)
    {
        Kind = kind;
        Code = code;
    }

    /// <summary>The spectral letter (O … Y), or '?' if the code is out of range.</summary>
    public char Letter
    {
        get
        {
            int i = (int)MathF.Floor(Code / 10f);
            return i >= 0 && i < Letters.Length ? Letters[i] : '?';
        }
    }

    public float Subclass => Code - 10f * MathF.Floor(Code / 10f);

    public static int LetterIndex(char letter) => Letters.IndexOf(char.ToUpperInvariant(letter));

    /// <summary>Short spectral notation, e.g. "G2 V", "K1 III", "DA2", "L5", "Neutron star".</summary>
    public override string ToString()
    {
        string sub = FormatSubclass(Subclass);
        return Kind switch
        {
            StarKind.MainSequence when Letter is 'L' or 'T' or 'Y' => $"{Letter}{sub}",
            StarKind.MainSequence => $"{Letter}{sub} V",
            StarKind.Subgiant => $"{Letter}{sub} IV",
            StarKind.Giant => $"{Letter}{sub} III",
            StarKind.BrightGiant => $"{Letter}{sub} II",
            StarKind.Supergiant => $"{Letter}{sub} I",
            StarKind.BrownDwarf => $"{Letter}{sub}",
            StarKind.WhiteDwarf => $"DA{FormatSubclass(Code)}",
            StarKind.NeutronStar => "Neutron star",
            StarKind.BlackHole => "Black hole",
            StarKind.WolfRayet => "Wolf–Rayet",
            StarKind.CarbonStar => "Carbon star",
            StarKind.TTauri => $"T Tauri ({Letter}{sub})",
            StarKind.HerbigAeBe => $"Herbig Ae/Be ({Letter}{sub})",
            _ => "Unknown",
        };
    }

    /// <summary>Human-readable description of the kind.</summary>
    public string KindName => Kind switch
    {
        StarKind.MainSequence when Letter is 'L' or 'T' or 'Y' => "Brown dwarf",
        StarKind.MainSequence => $"{Letter}-type main-sequence star",
        StarKind.Subgiant => $"{Letter}-type subgiant",
        StarKind.Giant => $"{Letter}-type giant",
        StarKind.BrightGiant => $"{Letter}-type bright giant",
        StarKind.Supergiant => $"{Letter}-type supergiant",
        StarKind.WhiteDwarf => "White dwarf",
        StarKind.BrownDwarf => "Brown dwarf",
        StarKind.NeutronStar => "Neutron star",
        StarKind.BlackHole => "Black hole",
        StarKind.WolfRayet => "Wolf–Rayet star",
        StarKind.CarbonStar => "Carbon star",
        StarKind.TTauri => "T Tauri star",
        StarKind.HerbigAeBe => "Herbig Ae/Be star",
        _ => "Unknown",
    };

    private static string FormatSubclass(float sub)
    {
        float rounded = MathF.Round(sub * 2f) / 2f;
        return rounded.ToString(rounded == MathF.Floor(rounded) ? "0" : "0.0", CultureInfo.InvariantCulture);
    }

    public bool Equals(StarClass other) => Kind == other.Kind && Code.Equals(other.Code);

    public override bool Equals(object? obj) => obj is StarClass other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Kind, Code);

    public static bool operator ==(StarClass a, StarClass b) => a.Equals(b);

    public static bool operator !=(StarClass a, StarClass b) => !a.Equals(b);
}
