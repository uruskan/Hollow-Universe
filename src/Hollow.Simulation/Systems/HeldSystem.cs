using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Players;

namespace Hollow.Simulation;

// What a held item does in the zone (GROUND_SYSTEMS.md §7): one handler per held behaviour, found by the item's
// HeldUse (its one part that gives one, decided when the data is read). The tick asks the item in hand's handler for its
// buttons; an intent a held behaviour sends is checked by that behaviour (ItemFor), never by the item's kind.
public sealed class HeldSystem : ZoneSystem
{
    /// <summary>A held behaviour's side in the zone: its per-tick buttons, and what putting it away ends.</summary>
    internal interface IHeldRules
    {
        /// <summary>One tick with the item in hand and the player's buttons (melee swings; the bow drawn and loosed).</summary>
        void Step(Entity entity, ref Character c, Player p, ItemDef item);

        /// <summary>The item was put away (another taken up, or another in the same slot): what was going on ends.</summary>
        void PutAway(Player p);
    }

    private Registry<IHeldRules> _held = null!;

    // The one place held behaviours are registered (every HeldUse but None has one; the zone won't start otherwise).
    private void InitHeld()
    {
        _held = Registry<IHeldRules>.ForEnum<HeldUse>("held behaviours", complete: true, HeldUse.None);
        _held.Add((int)HeldUse.Melee, new MeleeRules(Zone));
        _held.Add((int)HeldUse.Bow, new BowRules(Zone));
        _held.Add((int)HeldUse.Wiring, new WiringRules(Zone));
        // The rest act by their intents, each checked by the behaviour that sends it (ItemFor): the gun's shots, reloads
        // and unloads on its own timeline (GunSystem), throws, placing, the hammer's work, eating and learning.
        var byIntents = new ActsByIntents();
        foreach (HeldUse use in new[] { HeldUse.Firearm, HeldUse.Throwable, HeldUse.Placer, HeldUse.Planner, HeldUse.Hammer, HeldUse.Consumable, HeldUse.Blueprint })
        {
            _held.Add((int)use, byIntents);
        }
        _held.Seal();
    }

    // The handler of an item's held behaviour (null: it does nothing in the hands).
    internal IHeldRules? HeldRulesOf(ItemDef? item) => item == null ? null : _held.Find((int)item.Held);

    /// <summary>
    /// The item in one of the player's own slots (main or belt) when it is what the client saw and its held behaviour is
    /// <paramref name="use"/>: how an intent that a held behaviour sends is checked (GROUND_SYSTEMS.md §7).
    /// </summary>
    internal Outcome ItemFor(Player p, ContainerRef from, int slot, ushort expected, HeldUse use, out ItemDef item)
    {
        item = null!;
        if (from is not (ContainerRef.Main or ContainerRef.Belt) || !p.Inventory[from].InRange(slot))
        {
            return OutcomeCode.Invalid;
        }
        ItemStack s = p.Inventory[from][slot];
        if (s.IsEmpty || (expected != 0 && s.Item != expected))
        {
            return OutcomeCode.Stale;
        }
        if (Content.Item(s.Item) is not { } found || found.Held != use)
        {
            return OutcomeCode.NotUsable;
        }
        item = found;
        return OutcomeCode.Done;
    }

    /// <summary>Swung while the attack button is held, in the tool's rhythm, once it is ready.</summary>
    internal sealed class MeleeRules(Zone zone) : IHeldRules
    {
        public void Step(Entity entity, ref Character c, Player p, ItemDef item) => zone.Combat().StepMelee(entity, ref c, p, item, item.Melee!);

        public void PutAway(Player p) => p.StrikeTick = -1;
    }

    /// <summary>Drawn with the aim button, loosed with a press of the attack button.</summary>
    internal sealed class BowRules(Zone zone) : IHeldRules
    {
        public void Step(Entity entity, ref Character c, Player p, ItemDef item) => zone.Combat().StepBow(entity, ref c, p, item, item.Bow!);

        public void PutAway(Player p) => p.DrawStartTick = -1;
    }

    /// <summary>Nothing a tick: these act by intents.</summary>
    internal sealed class ActsByIntents : IHeldRules
    {
        public void Step(Entity entity, ref Character c, Player p, ItemDef item)
        {
        }

        public void PutAway(Player p)
        {
        }
    }

    internal sealed class WiringRules(Zone zone) : IHeldRules
    {
        public void Step(Entity entity, ref Character c, Player p, ItemDef item) { }
        public void PutAway(Player p) => zone.Wiring().Cancel(p.Id);
    }

    public override int InitOrder => 80;

    protected internal override void Init() => InitHeld();
}

/// <summary>The accessor on the zone (GROUND_SYSTEMS.md §12.2, K4).</summary>
public static class HeldSystemAccess
{
    public static HeldSystem Held(this Zone zone) => zone.Require<HeldSystem>();
}
