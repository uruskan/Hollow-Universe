using System;
using System.Collections.Generic;
using HollowUniverse.UI.Model;

namespace HollowUniverse.Ground;

/// <summary>The interface's view model before a game exists (the main menu): nothing carried, nothing to show.</summary>
public sealed class MenuGroundUi : IGroundUi
{
    private static readonly ItemStack[] Main = new ItemStack[24], Belt = new ItemStack[6], Wear = new ItemStack[7];

    public string PlayerName => "";
    public ItemInfo Item(string key) => new(key, key, "", "OTHER");
    public Vitals Vitals { get; } = new(100, 500, 250);
    public IReadOnlyList<ItemStack> Slots(ContainerId container) => container switch
    {
        ContainerId.Main => Main,
        ContainerId.Belt => Belt,
        ContainerId.Wear => Wear,
        _ => Array.Empty<ItemStack>(),
    };
    public int ActiveBelt => -1;
    public HeldAmmo? Ammo => null;
    public LootView? Loot => null;
    public IReadOnlyList<CraftCategory> Categories { get; } = Array.Empty<CraftCategory>();
    public IReadOnlyList<Recipe> Recipes { get; } = Array.Empty<Recipe>();
    public int Workbench => 0;
    public int Have(string key) => 0;
    public IReadOnlyList<CraftJob> Queue { get; } = Array.Empty<CraftJob>();
    public DeathInfo? Death => null;
    public IReadOnlyList<RespawnBag> Bags { get; } = Array.Empty<RespawnBag>();
    public InteractionPrompt? Prompt => null;
    public WheelKind? HeldWheel => null;
    public IReadOnlyList<WheelChoice> Wheel(WheelKind kind) => Array.Empty<WheelChoice>();
    public int WheelCurrent(WheelKind kind) => -1;

    public event Action<bool>? HitConfirmed { add { } remove { } }
    public event Action<float>? Damaged { add { } remove { } }
    public event Action<string, int>? Gathered { add { } remove { } }
    public event Action<string, int>? Crafted { add { } remove { } }
    public event Action<string>? Notice { add { } remove { } }
    public event Action<ChatLine>? ChatReceived { add { } remove { } }

    public void Move(SlotRef from, SlotRef to, int amount) { }
    public void Split(SlotRef slot, int amount) { }
    public void Drop(SlotRef slot, int amount) { }
    public void QuickMove(SlotRef slot) { }
    public void Use(SlotRef slot) { }
    public void Craft(string key, int amount) { }
    public void CancelCraft(int ticket) { }
    public void Respawn(string? bagId) { }
    public void SelectBelt(int index) { }
    public void SetBurning(bool on) { }
    public void ChooseWheel(WheelKind kind, int index) { }
    public void CloseLoot() { }
    public void Say(string text) { }
}
