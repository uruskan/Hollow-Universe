using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Players;

namespace Hollow.Simulation;

// What a held item does in the zone (GROUND_SYSTEMS.md §7): one handler per held behaviour, found by the item's
// HeldUse (its one part that gives one, decided when the data is read). The tick asks the item in hand's handler for its
// buttons; an intent a held behaviour sends is checked by that behaviour (ItemFor), never by the item's kind.
public sealed partial class Zone
{
    /// <summary>A held behaviour's side in the zone: its per-tick buttons, and what putting it away ends.</summary>
    private interface IHeldRules
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
        _held.Add((int)HeldUse.Melee, new MeleeRules(this));
        _held.Add((int)HeldUse.Bow, new BowRules(this));
        // The rest act by their intents, each checked by the behaviour that sends it (ItemFor): the gun's shots, reloads
        // and unloads on its own timeline (Zone.Guns.cs), throws, placing, the hammer's work, eating and learning.
        var byIntents = new ActsByIntents();
        foreach (HeldUse use in new[] { HeldUse.Firearm, HeldUse.Throwable, HeldUse.Placer, HeldUse.Planner, HeldUse.Hammer, HeldUse.Consumable, HeldUse.Blueprint })
        {
            _held.Add((int)use, byIntents);
        }
        _held.Seal();
    }

    // The handler of an item's held behaviour (null: it does nothing in the hands).
    private IHeldRules? HeldRulesOf(ItemDef? item) => item == null ? null : _held.Find((int)item.Held);

    /// <summary>
    /// The item in one of the player's own slots (main or belt) when it is what the client saw and its held behaviour is
    /// <paramref name="use"/>: how an intent that a held behaviour sends is checked (GROUND_SYSTEMS.md §7).
    /// </summary>
    private Outcome ItemFor(Player p, ContainerRef from, int slot, ushort expected, HeldUse use, out ItemDef item)
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
        if (Content!.Item(s.Item) is not { } found || found.Held != use)
        {
            return OutcomeCode.NotUsable;
        }
        item = found;
        return OutcomeCode.Done;
    }

    /// <summary>Swung while the attack button is held, in the tool's rhythm, once it is ready.</summary>
    private sealed class MeleeRules(Zone zone) : IHeldRules
    {
        public void Step(Entity entity, ref Character c, Player p, ItemDef item) => zone.StepMelee(entity, ref c, p, item, item.Melee!);

        public void PutAway(Player p) => p.StrikeTick = -1;
    }

    /// <summary>Drawn with the aim button, loosed with a press of the attack button.</summary>
    private sealed class BowRules(Zone zone) : IHeldRules
    {
        public void Step(Entity entity, ref Character c, Player p, ItemDef item) => zone.StepBow(entity, ref c, p, item, item.Bow!);

        public void PutAway(Player p) => p.DrawStartTick = -1;
    }

    /// <summary>Nothing a tick: these act by intents.</summary>
    private sealed class ActsByIntents : IHeldRules
    {
        public void Step(Entity entity, ref Character c, Player p, ItemDef item)
        {
        }

        public void PutAway(Player p)
        {
        }
    }
}
