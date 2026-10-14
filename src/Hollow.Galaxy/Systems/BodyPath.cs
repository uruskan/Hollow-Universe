namespace Hollow.Galaxy.Systems;

/// <summary>
/// A body's permanent 32-bit path inside its system (ARCHITECTURE.md §7, GALAXY.md §10.1). With the system address
/// it identifies a body for ever (and, from milestone M4, every surface tile on it), so it never depends on
/// numbering that could shift: a planet is named by its host and orbit slot, not by its position in a list.
/// <code>
/// bits 28–31  node: the star or barycentre the body is or orbits (1–8 stars A–H, 9–15 barycentres)
/// bits 20–27  orbit slot + 1 around that node (0 = the node itself)
/// bits 12–19  moon + 1 (0 = the planet itself)
/// bits  4–11  reserved for moons of moons (0)
/// bits  0–3   feature: 0 = the body, 1 = a belt in that slot (2–15 reserved)
/// </code>
/// </summary>
public static class BodyPath
{
    /// <summary>Stars are nodes 1–8 (A–H), barycentres 9–15.</summary>
    public const int MaxStars = 8;
    public const int FirstBarycentre = 9;
    public const int MaxNodes = 15;
    public const int MaxSlots = 255;
    public const int MaxMoons = 255;

    private const uint BeltFeature = 1;

    /// <summary>The path of a star (node 1–8) or barycentre (node 9–15).</summary>
    public static uint Node(int node) => (uint)node << 28;

    /// <summary>The path of the planet in orbit slot <paramref name="slot"/> (0-based) around a node.</summary>
    public static uint Planet(int node, int slot) => (uint)node << 28 | (uint)(slot + 1) << 20;

    /// <summary>The path of moon <paramref name="moon"/> (0-based) of a planet.</summary>
    public static uint Moon(uint planet, int moon) => planet | (uint)(moon + 1) << 12;

    /// <summary>The path of the belt in orbit slot <paramref name="slot"/> (0-based) around a node.</summary>
    public static uint Belt(int node, int slot) => (uint)node << 28 | (uint)(slot + 1) << 20 | BeltFeature;

    /// <summary>The star or barycentre a path belongs to (1–15).</summary>
    public static int NodeOf(uint path) => (int)(path >> 28);

    /// <summary>The orbit slot (0-based), or −1 for a star or barycentre.</summary>
    public static int SlotOf(uint path) => (int)((path >> 20) & 0xFF) - 1;

    /// <summary>The moon (0-based), or −1 for anything else.</summary>
    public static int MoonOf(uint path) => (int)((path >> 12) & 0xFF) - 1;

    public static bool IsBelt(uint path) => (path & 0xF) == BeltFeature;
}
