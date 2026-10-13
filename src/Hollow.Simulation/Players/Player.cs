using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Targets;

namespace Hollow.Simulation.Players;

/// <summary>The survival state of a character (GROUND.md §6): health and the needs, all in their own units.</summary>
public struct Vitals
{
    public float Health;
    public float Calories;
    public float Hydration;

    /// <summary>Health still to be lost to bleeding.</summary>
    public float Bleeding;

    /// <summary>Health still to be lost to burning (incendiary rounds, G4); water puts it out.</summary>
    public float Burning;

    /// <summary>Health still to be gained over time (bandages, injectors, food).</summary>
    public float PendingHeal;

    /// <summary>The temperature felt, in °C (the air, a fire, wetness), and how cold that is with clothing on (0 … 1).</summary>
    public float Temperature;
    public float Cold;

    /// <summary>Seconds of wetness left after swimming.</summary>
    public float WetSeconds;

    /// <summary>Near a lit fire.</summary>
    public bool Comfort;

    public readonly bool Starving => Calories <= 0;

    public readonly bool Dehydrated => Hydration <= 0;
}

/// <summary>Buttons of one tick for actions, besides movement (they ride in the same input command).</summary>
[Flags]
public enum ActionButtons : byte
{
    None = 0,

    /// <summary>Attack, swing, loose (left mouse) is held.</summary>
    Primary = 1,

    /// <summary>Aim, draw a bow (right mouse) is held.</summary>
    Secondary = 2,

    /// <summary>The primary button went down since the previous tick (kept until a tick uses it).</summary>
    PrimaryPressed = 4,
}

/// <summary>One tick of a player's actions: what they hold, the buttons, and how far in the past they saw others.</summary>
public struct ActionInput
{
    /// <summary>The belt slot in hand (0 … 5), or <see cref="NoSlot"/>.</summary>
    public byte HeldSlot;

    public ActionButtons Buttons;

    /// <summary>
    /// How many quarter ticks behind this input's tick the player saw everyone else (interpolation and latency): hits
    /// are tested against where others were then (ARCHITECTURE.md §11.5).
    /// </summary>
    public byte ViewDelay;

    public const byte NoSlot = 7;

    public readonly bool Has(ActionButtons b) => (Buttons & b) != 0;
}

/// <summary>What a swing or shot does visibly, for others to animate (it rides in the character's snapshot state).</summary>
public enum ActionKind : byte
{
    None,
    Swing,
    Draw,
    Shoot,

    /// <summary>A gun reload began (G4).</summary>
    Reload,

    /// <summary>An explosive thrown or placed (G5).</summary>
    Throw,
}

/// <summary>
/// One character's survival and items: inventory, crafting queue, vitals, what it holds and is doing, and its life
/// (alive or dead, awake or asleep). Lives in the zone alongside its <see cref="Character"/>; owned by the zone's
/// thread.
/// </summary>
public sealed class Player : ITargetable
{
    public Player(EntityId id, string name)
    {
        Id = id;
        Name = name;
    }

    /// <summary>The character's permanent ID (the same across lives).</summary>
    public EntityId Id { get; }

    /// <summary>How the world names it (GROUND_SYSTEMS.md §3.1).</summary>
    public TargetRef Ref => new(TargetFamily.Character, Id.Value);

    public string Name { get; }

    public PlayerInventory Inventory { get; } = new();

    public CraftQueue Crafting { get; } = new();

    public Vitals Vitals;

    /// <summary>Dead: the body became a corpse; waits for a respawn.</summary>
    public bool Dead { get; set; }

    /// <summary>The body the player chose (0 male, 1 female; GROUND.md §4.8): how others see them, nothing more.</summary>
    public byte Body { get; set; }

    /// <summary>Logged off but still in the world: lootable and killable, wakes when the player logs in.</summary>
    public bool Sleeping { get; set; }

    /// <summary>How the character died last (for the death screen).</summary>
    public string DeathCause { get; set; } = "";

    public string KilledBy { get; set; } = "";

    /// <summary>When a beach respawn is allowed again (Unix ms).</summary>
    public long BeachReadyAtUnixMs { get; set; }

    /// <summary>The object being looted (a box, corpse, device or sleeper), or none.</summary>
    public EntityId Looting { get; set; }

    /// <summary>Raised whenever <see cref="Looting"/> changes, so the server knows to send contents.</summary>
    public uint LootRevision { get; set; }

    // ---------------------------------------------------------------- held item and its actions

    /// <summary>The belt slot in hand and the item that was in it when taken up (a change re-equips).</summary>
    public byte HeldSlot = ActionInput.NoSlot;
    public ushort HeldItem;

    /// <summary>Tick when the held item is ready (an equip takes a moment).</summary>
    public long EquipReadyTick;

    /// <summary>Next tick a swing may start, and the tick the swing in progress strikes (−1: none).</summary>
    public long NextSwingTick;
    public long StrikeTick = -1;

    /// <summary>Tick the bow started being drawn (−1: not drawing).</summary>
    public long DrawStartTick = -1;

    /// <summary>Counts swings and shots (wrapping), so others see each one; and what the last was.</summary>
    public byte ActionCount;
    public ActionKind LastAction;

    /// <summary>Raised when the vitals change in a way the owner should see (whole units).</summary>
    public uint VitalsRevision { get; set; }

    // ---------------------------------------------------------------- G4: the gun in hand

    /// <summary>The gun in hand: cadence, recoil, reload, the last gun action applied (its magazine is in the item).</summary>
    public GunState Gun = GunState.None;

    /// <summary>Shots and reloads that arrived, in order, waiting for their tick.</summary>
    public List<GunAction> GunQueue { get; } = new();

    /// <summary>Raised whenever <see cref="Gun"/> changes in a way the owner should hear (the server sends it then).</summary>
    public uint GunRevision { get; set; }

    /// <summary>The last gun action refused, and why (sent with the gun's state).</summary>
    public uint GunRefusedSeq { get; set; }

    public OutcomeCode GunRefused { get; set; }

    // ---------------------------------------------------------------- G4: the wounded state (Rust's)

    /// <summary>Down: crawling with a little health, until helped up, up by themselves, finished off or dead.</summary>
    public bool Wounded { get; set; }

    /// <summary>When a wounded player's time runs out (a tick), and what downed them (the death screen's if they die).</summary>
    public long WoundedUntilTick { get; set; }

    public string WoundedCause { get; set; } = "";

    public EntityId WoundedBy { get; set; }

    public string WoundedByName { get; set; } = "";

    // ---------------------------------------------------------------- G7: progression and the map (GROUND.md §6.8)

    /// <summary>Blueprints learnt (tech trees, research, blueprint items); they last through death, as in Rust.</summary>
    public BlueprintBook Blueprints { get; } = new();

    /// <summary>Markers the player put on the map (at most world.json's <c>map.markers</c>), zone frame.</summary>
    public List<MapMarker> Markers { get; } = new();

    /// <summary>Where the player died last (zone frame), for the map; null before any death.</summary>
    public Vector3? DeathPoint { get; set; }

    /// <summary>Raised when the markers, the death point or the player's sleeping bags and beds change (the server sends them).</summary>
    public uint MapRevision { get; set; }

    public bool IsAwakeAndAlive => !Dead && !Sleeping;

    /// <summary>Awake, alive and not down: can act (use items, swing, shoot, open things).</summary>
    public bool CanAct => !Dead && !Sleeping && !Wounded;

    /// <summary>The item in the hand, if the held slot has one.</summary>
    public ItemDef? Held(GameContent content) =>
        HeldSlot < PlayerInventory.BeltSlots ? content.Item(Inventory.Belt[HeldSlot].Item) : null;
}
