using System;
using System.Collections.Generic;
using System.Linq;
using Hollow.Simulation.Building;
using HollowUniverse.UI.Model;
using UiPrivilege = HollowUniverse.UI.Model.Privilege;

namespace HollowUniverse.Build;

/// <summary>
/// The ground interface's view model for the build sandbox (GROUND.md §6.3, §8.1): a belt of building tools, the plan's
/// radial with the pieces in RustMVP's order and their costs, the hammer's radial for the piece aimed at (both the
/// game's own, <see cref="BuildingRadials"/>), building rights, the prompt and notices. The sandbox fills in what it
/// knows each frame. Everything the sandbox doesn't have (crafting, loot, death) is empty.
/// </summary>
public sealed class BuildSandboxUi : IGroundUi
{
    /// <summary>The belt: item key (its icon), name, what it does.</summary>
    public static readonly (string Key, string Name, string Description)[] Tools =
    {
        ("plan", "Building Plan", "Lays out foundations, walls, floors and roofs."),
        ("hammer", "Hammer", "Repairs, upgrades, rotates and demolishes building pieces."),
        ("door.wooden", "Wooden Door", "A single door for a doorway."),
        ("door.armoured", "Armoured Door", "A heavy single door for a doorway."),
        ("door.double", "Sheet Metal Double Door", "A double door for a wall frame."),
        ("claim", "Claim Device", "Claims a building: only those it lists may build there."),
    };

    private readonly IBuildingWallet _wallet;
    private readonly ItemStack[] _belt;
    private readonly ItemStack[] _main = new ItemStack[24];
    private readonly ItemStack[] _wear = new ItemStack[7];
    private readonly Dictionary<string, IRadialSource> _radials;

    /// <param name="hammer">The hammer's radial on what the sandbox aims at.</param>
    public BuildSandboxUi(BuildingData data, IBuildingWallet wallet, IRadialSource hammer)
    {
        _wallet = wallet;
        Plan = new PlanRadial(data, wallet);
        // The tools' radials by the tool's key (the right mouse opens the held one's).
        _radials = new Dictionary<string, IRadialSource> { ["plan"] = Plan, ["hammer"] = hammer };
        Radials = new[] { new RadialBinding(RadialInput.Aim, 0, () => HeldRadial) };
        _belt = Tools.Select(t => new ItemStack(t.Key, 1)).ToArray();
        string[] materials = { "wood", "stones", "metal.fragments", "metal.refined" };
        for (int i = 0; i < materials.Length; i++)
        {
            _main[i] = new ItemStack(materials[i], 1000);
        }
    }

    /// <summary>The plan's radial and its chosen piece (an index into <see cref="BuildingData.Wheel"/>).</summary>
    public PlanRadial Plan { get; }

    /// <summary>The radial the held tool opens with the right mouse (the plan's, the hammer's), or null.</summary>
    public IRadialSource? HeldRadial => _radials.GetValueOrDefault(Tools[ActiveBelt].Key);

    public IReadOnlyList<RadialBinding> Radials { get; }

    public event Action<int>? BeltChosen;

    /// <summary>Raises a notice line (a refused action).</summary>
    public void Say(string notice) => Notice?.Invoke(notice);

    // ---------------------------------------------------------------- reads

    public string PlayerName => "Builder";

    public ItemInfo Item(string key)
    {
        foreach ((string k, string name, string description) in Tools)
        {
            if (k == key)
            {
                return new ItemInfo(key, name, description, "CONSTRUCTION");
            }
        }
        return key switch
        {
            "wood" => new ItemInfo(key, "Wood", "Builds twig and wooden pieces.", "RESOURCES", 1000),
            "stones" => new ItemInfo(key, "Stone", "Builds stone pieces.", "RESOURCES", 1000),
            "metal.fragments" => new ItemInfo(key, "Metal Fragments", "Builds metal pieces.", "RESOURCES", 1000),
            "metal.refined" => new ItemInfo(key, "Refined Metal", "Builds armoured pieces.", "RESOURCES", 100),
            _ => new ItemInfo(key, key.ToUpperInvariant(), "", "OTHER"),
        };
    }

    public Vitals Vitals { get; set; } = new(100, 400, 200, Temperature: 19);

    public IReadOnlyList<ItemStack> Slots(ContainerId container) => container switch
    {
        ContainerId.Belt => _belt,
        ContainerId.Main => _main,
        ContainerId.Wear => _wear,
        _ => Array.Empty<ItemStack>(),
    };

    public int ActiveBelt { get; private set; }

    public HeldAmmo? Ammo => null;

    public LootView? Loot => null;

    public IReadOnlyList<CraftCategory> Categories { get; } = Array.Empty<CraftCategory>();

    public IReadOnlyList<Recipe> Recipes { get; } = Array.Empty<Recipe>();

    public int Workbench => 0;

    public int Have(string key) => key switch
    {
        "wood" => _wallet.Count("wood"),
        "stones" => _wallet.Count("stone"),
        "metal.fragments" => _wallet.Count("metal-fragments"),
        "metal.refined" => _wallet.Count("refined-metal"),
        _ => 0,
    };

    public IReadOnlyList<CraftJob> Queue { get; } = Array.Empty<CraftJob>();

    public DeathInfo? Death => null;

    public IReadOnlyList<RespawnBag> Bags { get; } = Array.Empty<RespawnBag>();

    public InteractionPrompt? Prompt { get; set; }

    /// <summary>Building rights where the player stands, from the rules' answer.</summary>
    public void SetPrivilege(Hollow.Simulation.Building.Privilege privilege) => Vitals = Vitals with
    {
        Building = privilege switch
        {
            Hollow.Simulation.Building.Privilege.Allowed => UiPrivilege.Allowed,
            Hollow.Simulation.Building.Privilege.Blocked => UiPrivilege.Blocked,
            _ => UiPrivilege.None,
        },
    };

    // ---------------------------------------------------------------- events

    // The sandbox has no fights, pickups, crafting or chat.
    public event Action<bool>? HitConfirmed { add { } remove { } }

    public event Action<float>? Damaged { add { } remove { } }

    public event Action<string, int>? Gathered { add { } remove { } }

    public event Action<string, int>? Crafted { add { } remove { } }

    public event Action<string>? Notice;

    public event Action<ChatLine>? ChatReceived { add { } remove { } }

    // ---------------------------------------------------------------- actions

    public void Move(SlotRef from, SlotRef to, int amount)
    {
    }

    public void Split(SlotRef slot, int amount)
    {
    }

    public void Drop(SlotRef slot, int amount)
    {
    }

    public void QuickMove(SlotRef slot)
    {
    }

    public void Craft(string key, int amount)
    {
    }

    public void CancelCraft(int ticket)
    {
    }

    public void Respawn(string? bagId)
    {
    }

    public void SelectBelt(int index)
    {
        if (index >= 0 && index < _belt.Length)
        {
            ActiveBelt = index;
            BeltChosen?.Invoke(index);
        }
    }

    public void SetBurning(bool on)
    {
    }

    public void CloseLoot()
    {
    }

    void IGroundUi.Say(string text)
    {
    }
}
