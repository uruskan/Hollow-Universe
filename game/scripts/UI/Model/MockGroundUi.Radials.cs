using System;
using System.Collections.Generic;

namespace HollowUniverse.UI.Model;

// The sample radials (GROUND_SYSTEMS.md §4): the right mouse opens the held plan's or hammer's; E held on the HUD opens
// the options of a sample door with a code lock, as if it were aimed at; R held with the pistol opens its rounds. All go
// through the interface's one radial and its one way of opening, as the game's will.
public sealed partial class MockGroundUi
{
    /// <summary>How long E and R are held before their radial opens (GROUND_SYSTEMS.md §11: RustMVP's 0.35 s).</summary>
    public const double HoldSeconds = 0.35;

    private IReadOnlyList<RadialBinding>? _radials;
    private Dictionary<(string Item, RadialInput Input), IRadialSource>? _held;
    private RadialMenu? _door;
    private int _planPiece = 1;

    /// <summary>The sample door's state, which its options change.</summary>
    public SampleDoor Door { get; } = new();

    public IReadOnlyList<RadialBinding> Radials => _radials ??= new[]
    {
        new RadialBinding(RadialInput.Aim, 0, () => Held(RadialInput.Aim)),
        new RadialBinding(RadialInput.Interact, HoldSeconds, () => DoorRadial),
        new RadialBinding(RadialInput.Reload, HoldSeconds, () => Held(RadialInput.Reload)),
    };

    /// <summary>The building plan's sample radial: its pieces in Rust's order, costs in wood.</summary>
    public IRadialSource PlanRadial => HeldRadials[("plan", RadialInput.Aim)];

    /// <summary>The hammer's sample radial, aimed at a wooden wall.</summary>
    public IRadialSource HammerRadial => HeldRadials[("hammer", RadialInput.Aim)];

    /// <summary>The pistol's rounds (R).</summary>
    public IRadialSource AmmoRadial => HeldRadials[("pistol", RadialInput.Reload)];

    /// <summary>The sample door's options (E): a fresh code lock, whose owner the player is.</summary>
    public IRadialSource DoorRadial => _door ??= new RadialMenu(DoorOptions);

    /// <summary>The plan's chosen piece (an index into its radial).</summary>
    public int PlanPiece => _planPiece;

    // What each held item opens, by the input (the held item's own radials, as P3's held behaviours will register them).
    private Dictionary<(string Item, RadialInput Input), IRadialSource> HeldRadials => _held ??= new()
    {
        [("plan", RadialInput.Aim)] = new RadialMenu(PlanOptions, () => _planPiece, splitAtTop: true),
        [("hammer", RadialInput.Aim)] = new RadialMenu(HammerOptions),
        [("pistol", RadialInput.Reload)] = new RadialMenu(AmmoOptions),
    };

    private IRadialSource? Held(RadialInput input) =>
        ActiveBelt >= 0 && HeldRadials.TryGetValue((_belt[ActiveBelt].Key, input), out IRadialSource? source) ? source : null;

    // The plan's pieces in the radial's clockwise order (RustMVP's, matched to Rust), with their glyphs' names (their
    // building.json keys); costs are in wood.
    private static readonly (string Key, string Name, string Description, int Cost)[] PlanPieces =
    {
        ("foundation", "Foundation", "A square support for your base.", 50),
        ("foundation-triangle", "Triangle Foundation", "A triangular support for your base.", 25),
        ("ramp", "Ramp", "A quarter-height sloping access ramp.", 25),
        ("steps", "Steps", "Half-height steps attached to a floor.", 25),
        ("floor", "Floor", "A square upper floor or ceiling.", 25),
        ("floor-triangle", "Triangle Floor", "A triangular upper floor or ceiling.", 13),
        ("floor-frame", "Floor Frame", "A square frame with an open centre.", 25),
        ("floor-frame-triangle", "Triangle Floor Frame", "A triangular frame with an open centre.", 13),
        ("wall", "Wall", "Enclose your base with a full wall.", 50),
        ("doorway", "Doorway", "A wall opening for a single door.", 35),
        ("window", "Window", "A wall with a window opening.", 35),
        ("wall-frame", "Wall Frame", "An opening for a double door.", 25),
        ("half-wall", "Half Wall", "A wall half the normal height.", 50),
        ("low-wall", "Low Wall", "A low protective wall.", 25),
        ("stairs-l", "L Stairs", "A full-height staircase with a corner.", 50),
        ("stairs-u", "U Stairs", "A full-height returning staircase.", 50),
        ("stairs-spiral", "Spiral Stairs", "A half-height square spiral staircase.", 50),
        ("stairs-spiral-triangle", "Triangle Spiral Stairs", "A half-height triangular spiral staircase.", 25),
        ("roof", "Roof", "A sloping square roof section.", 50),
        ("roof-triangle", "Triangle Roof", "A sloping triangular roof section.", 25),
    };

    private void PlanOptions(RadialMenu menu)
    {
        int wood = Have("wood");
        for (int i = 0; i < PlanPieces.Length; i++)
        {
            (string key, string name, string description, int cost) = PlanPieces[i];
            int piece = i;
            menu.Add(new RadialOption(RadialIcon.Glyph(key), name, description, FormattableString.Invariant($"{cost} x WOOD ({wood})"),
                Warning: wood < cost ? "Not enough wood" : null), () => _planPiece = piece);
        }
    }

    // The hammer aims at a wooden wall: wood is already done, stone and metal are affordable.
    private void HammerOptions(RadialMenu menu)
    {
        const string strength = "Improve this piece's strength";
        void Upgrade(string grade, int cost, string material, string key, string? reason = null) =>
            menu.Add(new RadialOption(RadialIcon.Glyph("upgrade"), $"UPGRADE TO {grade.ToUpperInvariant()}", strength,
                FormattableString.Invariant($"{cost} x {material} ({Have(key)})"), reason ?? (Have(key) < cost ? "Not enough materials" : null)),
                () => Notice?.Invoke($"Upgraded to {grade.ToLowerInvariant()}."));
        Upgrade("Wood", 140, "WOOD", "wood", "Already at this grade or stronger");
        Upgrade("Stone", 210, "STONES", "stones");
        Upgrade("Metal", 140, "METAL FRAGMENTS", "metal.fragments");
        Upgrade("Refined Metal", 18, "REFINED METAL", "metal.refined");
        menu.Add(new RadialOption(RadialIcon.Glyph("rotate"), "ROTATE", "Turn this piece around"), () => Notice?.Invoke("Turned round."));
        menu.Add(new RadialOption(RadialIcon.Glyph("demolish"), "DEMOLISH", "Remove this piece and unsupported pieces"),
            () => Notice?.Invoke("Demolished."));
    }

    // Rust's options on a door with a fresh code lock, for its owner: the door, the code, the lock (once it has a code),
    // and taking the lock off (while unlocked).
    private void DoorOptions(RadialMenu menu)
    {
        SampleDoor door = Door;
        menu.Add(new RadialOption(RadialIcon.Glyph("door"), door.Open ? "CLOSE DOOR" : "OPEN DOOR", "Use this door"),
            () => door.Open = !door.Open);
        menu.Add(new RadialOption(RadialIcon.Glyph("keypad"), door.CodeSet ? "CHANGE CODE" : "SET CODE", "Choose a four-digit code"),
            () =>
            {
                door.CodeSet = true;
                Notice?.Invoke("Code set.");
            });
        menu.Add(new RadialOption(RadialIcon.Glyph(door.Locked ? "unlock" : "lock"), door.Locked ? "UNLOCK" : "LOCK",
                door.Locked ? "Let anyone use this door" : "Only those who know the code may use it",
                Reason: door.CodeSet ? null : "Set a code first"),
            () => door.Locked = !door.Locked);
        menu.Add(new RadialOption(RadialIcon.Glyph("pick-up"), "PICK UP", "Take the code lock off the door",
                Reason: door.Locked ? "Unlock it first" : null),
            () => Notice?.Invoke("You take the code lock."));
    }

    // The pistol's rounds the player carries, the loaded kind first chosen by a tap, and UNLOAD (GROUND_SYSTEMS.md §7).
    private void AmmoOptions(RadialMenu menu)
    {
        foreach (string key in new[] { "ammo.pistol", "ammo.pistol.hv" })
        {
            int have = Have(key);
            if (have > 0)
            {
                ItemInfo rounds = Item(key);
                menu.Add(new RadialOption(RadialIcon.Item(key), rounds.Name.ToUpperInvariant(), rounds.Description, FormattableString.Invariant($"{have} CARRIED")),
                    () => Notice?.Invoke($"Reloading with {rounds.Name.ToLowerInvariant()}."));
            }
        }
        int loaded = ActiveBelt >= 0 ? Math.Max(0, _belt[ActiveBelt].Ammo) : 0;
        menu.Add(new RadialOption(RadialIcon.Glyph("pick-up"), "UNLOAD", "Take the rounds out of the magazine",
                Reason: loaded > 0 ? null : "The magazine is empty"),
            () => Notice?.Invoke(FormattableString.Invariant($"{loaded} rounds unloaded.")));
    }

    /// <summary>The sample door's state.</summary>
    public sealed class SampleDoor
    {
        public bool Open { get; set; }

        public bool CodeSet { get; set; }

        public bool Locked { get; set; }
    }
}
