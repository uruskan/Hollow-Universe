using Hollow.Simulation.Building;
using Hollow.Simulation.Content;
using Hollow.Simulation.Items;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;
using Hollow.Simulation.Raid;

namespace Hollow.Simulation.Capabilities;

/// <summary>A thing with an owner: the player who placed or built it (0: none).</summary>
public interface IOwned
{
    ulong OwnerId { get; }
}

/// <summary>A thing that holds items.</summary>
public interface IContainerHolder
{
    Container? Container { get; }
}

/// <summary>
/// A thing with a list of authorised players (the cupboard capability's, GROUND_SYSTEMS.md §5): the tool cupboard's
/// building rights, a ship's crew at its helm (IRON_WAKE.md §5.2).
/// </summary>
public interface IAuthorisations
{
    bool IsAuthorised(ulong player);
}

/// <summary>
/// A loose thing with a lifetime (GROUND_SYSTEMS.md §3.7), of any family: its kind's rule (survival.json's lifetimes, a
/// species' carcass), the world tick it goes at (restarted as its rule says), and whether it was emptied (looted or
/// harvested out: it goes at once, if its rule says so).
/// </summary>
public interface ILifetime : ITargetable
{
    LifetimeRule Lifetime { get; }

    long EndsAt { get; set; }

    bool Emptied { get; }
}

/// <summary>
/// Every capability's handler (GROUND_SYSTEMS.md §2.1, §3.5), in a registry checked against capabilities.json: an entry
/// without its handler, or a handler without its entry, stops the start. One class each; a new kind of behaviour is one
/// class here and one line in the data.
/// </summary>
public static partial class CapabilityHandlers
{
    /// <summary>
    /// The handlers by capabilities.json's index, sealed (throws listing what is missing or extra). Those with verbs
    /// (GROUND_SYSTEMS.md §5) find theirs in the content's interactions.json.
    /// </summary>
    public static Registry<ICapability> Create(GameContent content)
    {
        CapabilityData data = content.Capabilities;
        InteractionData verbs = content.Interactions;
        var registry = new Registry<ICapability>("capabilities", data.Keys);
        foreach (ICapability handler in new ICapability[]
                 {
                     new Owned(), new Holds(verbs), new Burner(verbs), new Machine(verbs), new DoorCapability(verbs), new KeyLockCapability(verbs),
                     new CodeLockCapability(verbs), new CupboardCapability(verbs), new ShipCapability(), new Lifetime(), new ExplosiveCapability(),
                     new Pickup(verbs), new Portable(verbs, data), new Station(verbs), new WoundedCapability(verbs), new CharacterCapability(verbs),
                     new DudCapability(verbs), new ArrowCapability(), new Standing(), new FireCapability(), new SeatCapability(verbs),
                     new ShipDoorCapability(verbs), new FuelCapability(verbs), new MagazineCapability(verbs),
                 })
        {
            registry.Add(handler.Key, handler);
        }
        return registry.Seal();
    }

    /// <summary>What a thing with these capabilities tells a viewer (GROUND_SYSTEMS.md §3.3): every capability's share.</summary>
    public static ViewerFlags Flags(this Registry<ICapability> registry, CapabilitySet set, object thing, in Viewer viewer)
    {
        ViewerFlags flags = ViewerFlags.None;
        foreach (int capability in set)
        {
            flags |= registry.Find(capability)?.Flags(thing, viewer) ?? ViewerFlags.None;
        }
        return flags;
    }

    /// <summary>Placed or built by a player: Mine for them, Teammate for their team.</summary>
    private sealed class Owned : ICapability
    {
        public string Key => "owned";

        public ViewerFlags Flags(object thing, in Viewer viewer)
        {
            ulong owner = thing is IOwned o ? o.OwnerId : 0;
            return owner == 0 ? ViewerFlags.None
                : owner == viewer.Id.Value ? ViewerFlags.Mine
                : viewer.IsTeammate(owner) ? ViewerFlags.Teammate
                : ViewerFlags.None;
        }
    }

    /// <summary>Holds items: Empty while it holds nothing.</summary>
    private sealed partial class Holds : ICapability
    {
        public string Key => "container";

        public ViewerFlags Flags(object thing, in Viewer viewer) =>
            thing is IContainerHolder { Container: { IsEmpty: true } } ? ViewerFlags.Empty : ViewerFlags.None;
    }

    /// <summary>A deployable that works (burns, runs): Lit while it does.</summary>
    private abstract class Works : ICapability
    {
        public abstract string Key { get; }

        public ViewerFlags Flags(object thing, in Viewer viewer) => thing is Deployable { Lit: true } ? ViewerFlags.Lit : ViewerFlags.None;
    }

    /// <summary>Burns fuel (a campfire, a furnace).</summary>
    private sealed partial class Burner : Works
    {
        public override string Key => "burner";
    }

    /// <summary>Works on what is in it (a recycler, a research table).</summary>
    private sealed partial class Machine : Works
    {
        public override string Key => "machine";
    }

    /// <summary>Opens and shuts: CanUse when the viewer may pass it now (no lock, unlocked, on its list, its key).</summary>
    private sealed partial class DoorCapability : ICapability
    {
        public string Key => "door";

        public ViewerFlags Flags(object thing, in Viewer viewer) =>
            thing is Door door && BuildingWorld.CanUse(viewer.Id.Value, door, viewer.Keys) ? ViewerFlags.CanUse : ViewerFlags.None;
    }

    /// <summary>A key lock on a door: Locked.</summary>
    private sealed partial class KeyLockCapability : ICapability
    {
        public string Key => "key-lock";

        public ViewerFlags Flags(object thing, in Viewer viewer) =>
            thing is Door { Lock: KeyLock { Locked: true } } ? ViewerFlags.Locked : ViewerFlags.None;
    }

    /// <summary>A code lock on a door: Locked; HasCode once set; Authorised when the viewer is on its list.</summary>
    private sealed partial class CodeLockCapability : ICapability
    {
        public string Key => "code-lock";

        public ViewerFlags Flags(object thing, in Viewer viewer)
        {
            if (thing is not Door { Lock: CodeLock code })
            {
                return ViewerFlags.None;
            }
            return (code.Locked ? ViewerFlags.Locked : ViewerFlags.None) | (code.HasCode ? ViewerFlags.HasCode : ViewerFlags.None) |
                (code.IsAuthorised(viewer.Id.Value) ? ViewerFlags.Authorised : ViewerFlags.None);
        }
    }

    /// <summary>A list of authorised players (the claim device, a ship's helm): Authorised when the viewer is on it.</summary>
    private sealed partial class CupboardCapability : ICapability
    {
        public string Key => "cupboard";

        public ViewerFlags Flags(object thing, in Viewer viewer) =>
            thing is IAuthorisations list && list.IsAuthorised(viewer.Id.Value) ? ViewerFlags.Authorised : ViewerFlags.None;
    }

    /// <summary>A ship, on every part: Locked while its lock keeps its doors shut to strangers; Authorised for its crew.</summary>
    private sealed class ShipCapability : ICapability
    {
        public string Key => "ship";

        public ViewerFlags Flags(object thing, in Viewer viewer) => thing is not Ship ship ? ViewerFlags.None
            : (ship.Locked ? ViewerFlags.Locked : ViewerFlags.None) | (ship.IsAuthorised(viewer.Id.Value) ? ViewerFlags.Authorised : ViewerFlags.None);
    }

    /// <summary>A charge or grenade with its fuse: when what it is stuck to or lies on goes, it falls (and its fuse burns on).</summary>
    private sealed class ExplosiveCapability : ICapability, IDependentCapability
    {
        public string Key => "explosive";

        public ViewerFlags Flags(object thing, in Viewer viewer) => ViewerFlags.None;

        public void SupportLost(Zone zone, in TargetRef dependent, object thing)
        {
            if (thing is LiveExplosive e)
            {
                zone.Explosives().DropExplosive(e);
            }
        }
    }

    /// <summary>
    /// Stands on what it was placed on (GROUND_SYSTEMS.md §3.6, §6): a deployable on a foundation or floor goes when that
    /// goes, as in Rust: it breaks, and what it held spills in one bag. Nothing to tell a viewer.
    /// </summary>
    private sealed class Standing : ICapability, IDependentCapability
    {
        public string Key => "standing";

        public ViewerFlags Flags(object thing, in Viewer viewer) => ViewerFlags.None;

        public void SupportLost(Zone zone, in TargetRef dependent, object thing)
        {
            if (thing is Deployable d)
            {
                zone.Things().Break(d, Combat.DestroyCause.Collapse);
            }
        }
    }

    /// <summary>
    /// A fire burning on something (IW4, IRON_WAKE.md §7): when what holds it goes, its burning fuel falls to what is below and
    /// burns on there, or goes out with nothing near enough below (<see cref="FireSystem"/>). Nothing to tell a viewer.
    /// </summary>
    private sealed class FireCapability : ICapability, IDependentCapability
    {
        public string Key => "fire";

        public ViewerFlags Flags(object thing, in Viewer viewer) => ViewerFlags.None;

        public void SupportLost(Zone zone, in TargetRef dependent, object thing)
        {
            if (thing is Fire fire)
            {
                zone.Fires().SupportLost(fire);
            }
        }
    }

    /// <summary>
    /// Goes after a while (GROUND_SYSTEMS.md §3.7): the zone's one lifetime step asks it whether a thing's time is up (or it
    /// was emptied, when its rule ends it so) and ends it: its family takes it out of the world. Nothing to tell a viewer.
    /// </summary>
    private sealed class Lifetime : ICapability, ILifetimeCapability, IDependentCapability
    {
        public string Key => "lifetime";

        public ViewerFlags Flags(object thing, in Viewer viewer) => ViewerFlags.None;

        // A loose thing lying on a ship's deck (IRON_WAKE.md §4.10: a bag, a body) falls to what is below when the ship goes.
        public void SupportLost(Zone zone, in TargetRef dependent, object thing)
        {
            if (thing is WorldObject { Rides: true } loose)
            {
                zone.Things().Fall(loose);
            }
        }

        public bool Over(object thing, long tick) =>
            thing is ILifetime l && (tick >= l.EndsAt || (l.Lifetime.EndsWhenEmptied && l.Emptied));

        public void Restart(object thing, LifetimeRestart why, long tick)
        {
            if (thing is ILifetime l && why != LifetimeRestart.None && l.Lifetime.Restart == why)
            {
                l.EndsAt = tick + l.Lifetime.Ticks;
            }
        }

        public void End(Zone zone, in TargetRef target, object thing) => zone.Targets().RemoveTarget(target);
    }
}
