using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Hollow.Simulation.Building;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;

namespace Hollow.Simulation.Items;

/// <summary>A row of an item panel's INFORMATION (GROUND_SYSTEMS.md §7): "Rate of fire" and "450 rpm"; good news or not.</summary>
public readonly record struct ItemRow(string Label, string Value, bool Positive = true);

/// <summary>
/// What a describer looks at: the game data, the item, and the stack when one is described (its magazine, attachments,
/// condition, the recipe a blueprint teaches); without one, the kind. <see cref="Known"/> is the reader's blueprints,
/// when known.
/// </summary>
public readonly struct ItemDescription(GameContent content, ItemDef item, ItemStack? stack = null, BlueprintBook? known = null)
{
    public GameContent Content { get; } = content;

    public ItemDef Item { get; } = item;

    public ItemStack? Stack { get; } = stack;

    public BlueprintBook? Known { get; } = known;
}

/// <summary>A part's rows (GROUND_SYSTEMS.md §7): one describer per <see cref="ItemPart"/>.</summary>
public interface IItemDescriber
{
    void Describe(in ItemDescription d, List<ItemRow> into);
}

/// <summary>
/// An item's information (GROUND_SYSTEMS.md §7): the rows of each of its parts, in the parts' order, from a describer per
/// part found in a registry (every part has one: a new part is a describer here), then its condition. The same for the
/// item panel of every screen; nothing here asks what kind of item it is.
/// </summary>
public static class ItemRows
{
    private static readonly Registry<IItemDescriber> Describers = Create();

    /// <summary>A registry with every part's describer, sealed (throws listing a part without one).</summary>
    public static Registry<IItemDescriber> Create()
    {
        var registry = Registry<IItemDescriber>.ForEnum<ItemPart>("item describers", complete: true);
        registry.Add((int)ItemPart.Melee, new MeleeRows());
        registry.Add((int)ItemPart.Bow, new BowRows());
        registry.Add((int)ItemPart.Arrow, new ArrowRows());
        registry.Add((int)ItemPart.Consumable, new ConsumableRows());
        registry.Add((int)ItemPart.Wearable, new WearableRows());
        registry.Add((int)ItemPart.Fuel, new FuelRows());
        registry.Add((int)ItemPart.Cookable, new CookableRows());
        registry.Add((int)ItemPart.Deployable, new DeployableRows());
        registry.Add((int)ItemPart.Ship, new ShipRows());
        registry.Add((int)ItemPart.Firearm, new FirearmRows());
        registry.Add((int)ItemPart.Round, new RoundRows());
        registry.Add((int)ItemPart.Attachment, new AttachmentRows());
        registry.Add((int)ItemPart.Throwable, new ThrowableRows());
        registry.Add((int)ItemPart.Plan, new PlanRows());
        registry.Add((int)ItemPart.Hammer, new HammerRows());
        registry.Add((int)ItemPart.Cupboard, new CupboardRows());
        registry.Add((int)ItemPart.Lock, new LockRows());
        registry.Add((int)ItemPart.Key, new KeyRows());
        registry.Add((int)ItemPart.Door, new DoorRows());
        registry.Add((int)ItemPart.Blueprint, new BlueprintRows());
        registry.Add((int)ItemPart.Component, new ComponentRows());
        registry.Add((int)ItemPart.Wiring, new WiringRows());
        return registry.Seal();
    }

    /// <summary>The rows of an item (of a stack, when given: its magazine, attachments, condition, what it teaches).</summary>
    public static List<ItemRow> Describe(GameContent content, ItemDef item, ItemStack? stack = null, BlueprintBook? known = null)
    {
        var rows = new List<ItemRow>();
        var d = new ItemDescription(content, item, stack, known);
        foreach (ItemPart part in item.Parts)
        {
            Describers[(int)part].Describe(d, rows);
        }
        if (stack is { IsEmpty: false } s && item.HasCondition)
        {
            float condition = Math.Clamp((float)s.Condition / item.MaxCondition, 0, 1);
            rows.Add(new ItemRow("Condition", N(condition * 100, "0") + "%", condition >= 0.25f));
        }
        return rows;
    }

    // ---------------------------------------------------------------- formatting

    private sealed class WiringRows : IItemDescriber
    {
        public void Describe(in ItemDescription d, List<ItemRow> into)
        {
            into.Add(new ItemRow("Cable endpoint span", "20 m"));
            into.Add(new ItemRow("Tool reach", Metres(d.Item.Wiring!.Reach)));
            into.Add(new ItemRow("Routing points", N(d.Item.Wiring.MaxAnchors)));
        }
    }

    /// <summary>A number as the panel shows it: whole when whole, else to <paramref name="format"/>.</summary>
    internal static string N(double v, string format = "0.##") => v.ToString(v % 1 == 0 ? "0" : format, CultureInfo.InvariantCulture);

    internal static string Signed(double v) => (v > 0 ? "+" : "") + N(v);

    internal static string Percent(double fraction) => N(fraction * 100, "0.#") + "%";

    /// <summary>A multiplier against 1 as a change: ×0.85 is "−15%", ×1.33 "+33%".</summary>
    internal static string Change(double factor) => (factor >= 1 ? "+" : "−") + N(Math.Abs(factor - 1) * 100, "0") + "%";

    internal static string Seconds(double s) => N(s, "0.#") + " s";

    internal static string Metres(double m) => N(m, "0.#") + " m";

    /// <summary>Names in a line: the first <paramref name="most"/>, then how many more.</summary>
    internal static string Names(IEnumerable<string> names, int most = 3)
    {
        List<string> all = names.Distinct().ToList();
        string shown = string.Join(", ", all.Take(most));
        return all.Count > most ? $"{shown} +{all.Count - most} more" : shown;
    }

    // ---------------------------------------------------------------- the parts

    private sealed class MeleeRows : IItemDescriber
    {
        public void Describe(in ItemDescription d, List<ItemRow> into)
        {
            MeleeStats m = d.Item.Melee!;
            into.Add(new ItemRow("Damage", N(m.Damage)));
            if (m.Tree > 0)
            {
                into.Add(new ItemRow("Wood per hit", N(m.Tree), m.Tree >= 20));
            }
            if (m.Ore > 0)
            {
                into.Add(new ItemRow("Ore per hit", N(m.Ore), m.Ore >= 20));
            }
            if (m.Structure > 0)
            {
                into.Add(new ItemRow("Against structures", N(m.Structure)));
            }
            if (m.Bleed > 0)
            {
                into.Add(new ItemRow("Bleeding", N(m.Damage * m.Bleed) + " a hit"));
            }
            into.Add(new ItemRow("Swing", Seconds(m.SwingSeconds)));
            into.Add(new ItemRow("Reach", Metres(m.Reach)));
        }
    }

    private sealed class BowRows : IItemDescriber
    {
        public void Describe(in ItemDescription d, List<ItemRow> into)
        {
            BowStats bow = d.Item.Bow!;
            into.Add(new ItemRow("Draw", Seconds(bow.DrawSeconds)));
            if (bow.Ammo.Count > 0 && bow.Ammo[0].Ammo is { } arrow)
            {
                into.Add(new ItemRow("Damage", N(arrow.Damage * bow.DamageScale)));
                into.Add(new ItemRow("Arrow speed", N(arrow.Speed * bow.SpeedScale, "0") + " m/s"));
            }
            into.Add(new ItemRow("Shoots", Names(bow.Ammo.Select(a => a.Name))));
        }
    }

    private sealed class ArrowRows : IItemDescriber
    {
        public void Describe(in ItemDescription d, List<ItemRow> into)
        {
            AmmoStats a = d.Item.Ammo!;
            into.Add(new ItemRow("Damage", N(a.Damage)));
            into.Add(new ItemRow("Speed", N(a.Speed, "0") + " m/s"));
            if (a.Bleed > 0)
            {
                into.Add(new ItemRow("Bleeding", N(a.Damage * a.Bleed) + " a hit"));
            }
            if (a.Structure > 0)
            {
                into.Add(new ItemRow("Against structures", N(a.Damage * a.Structure)));
            }
            if (a.Recover > 0)
            {
                into.Add(new ItemRow("Found again", Percent(a.Recover)));
            }
        }
    }

    private sealed class ConsumableRows : IItemDescriber
    {
        public void Describe(in ItemDescription d, List<ItemRow> into)
        {
            ConsumeStats food = d.Item.Consume!;
            if (food.Calories != 0)
            {
                into.Add(new ItemRow("Calories", Signed(food.Calories), food.Calories > 0));
            }
            if (food.Hydration != 0)
            {
                into.Add(new ItemRow("Hydration", Signed(food.Hydration), food.Hydration > 0));
            }
            if (food.Health != 0)
            {
                into.Add(new ItemRow("Instant health", Signed(food.Health), food.Health > 0));
            }
            if (food.Heal != 0)
            {
                into.Add(new ItemRow("Healing", Signed(food.Heal), food.Heal > 0));
            }
            if (food.StopsBleeding)
            {
                into.Add(new ItemRow("Bleeding", "Stops"));
            }
            if (food.Poison > 0)
            {
                into.Add(new ItemRow("Poison", "−" + N(food.Poison) + " health", false));
            }
        }
    }

    private sealed class WearableRows : IItemDescriber
    {
        // The layers in the order the interface names them.
        private static readonly (WearLayer Layer, string Name)[] Layers =
        {
            (WearLayer.Head, "Head"), (WearLayer.Face, "Face"), (WearLayer.Torso, "Torso"), (WearLayer.TorsoArmour, "Torso armour"),
            (WearLayer.Legs, "Legs"), (WearLayer.LegsArmour, "Leg armour"), (WearLayer.Hands, "Hands"), (WearLayer.Feet, "Feet"),
        };

        public void Describe(in ItemDescription d, List<ItemRow> into)
        {
            WearStats wear = d.Item.Wear!;
            into.Add(new ItemRow("Worn on", Names(Layers.Where(l => (wear.Layers & l.Layer) != 0).Select(l => l.Name), 4)));
            if (wear.Melee > 0)
            {
                into.Add(new ItemRow("Melee protection", Percent(wear.Melee)));
            }
            if (wear.Projectile > 0)
            {
                into.Add(new ItemRow("Projectile protection", Percent(wear.Projectile)));
            }
            if (wear.Insulation > 0)
            {
                into.Add(new ItemRow("Warmth", "+" + N(wear.Insulation) + " °C"));
            }
        }
    }

    private sealed class FuelRows : IItemDescriber
    {
        public void Describe(in ItemDescription d, List<ItemRow> into)
        {
            FuelStats fuel = d.Item.Fuel!;
            into.Add(new ItemRow("Burns for", Seconds(fuel.Seconds)));
            if (fuel.Byproduct is { } left)
            {
                into.Add(new ItemRow("Leaves", left.Name));
            }
        }
    }

    private sealed class CookableRows : IItemDescriber
    {
        public void Describe(in ItemDescription d, List<ItemRow> into)
        {
            CookStats cook = d.Item.Cook!;
            if (cook.Into is { } into2)
            {
                // Where it turns: the deployables whose device cooks its kind (a furnace, a campfire).
                string where = Names(d.Content.Deployables.Where(x => x.Device?.Cooks == cook.In).Select(x => x.Item.Name.ToLowerInvariant()), 2);
                into.Add(new ItemRow("Becomes", $"{into2.Name} ({(where.Length > 0 ? where : cook.In)}, {Seconds(cook.Seconds)})"));
            }
        }
    }

    private sealed class DeployableRows : IItemDescriber
    {
        public void Describe(in ItemDescription d, List<ItemRow> into)
        {
            DeployableDef def = d.Item.Deploys!;
            into.Add(new ItemRow("Health", N(def.Health)));
            if (def.Slots > 0)
            {
                into.Add(new ItemRow("Slots", N(def.Slots)));
            }
            if (def.Workbench > 0)
            {
                into.Add(new ItemRow("Workbench level", N(def.Workbench)));
            }
            if (def.Device is { } device)
            {
                into.Add(new ItemRow("Burns", Names(d.Content.Items.Where(i => i.Fuel != null).Select(i => i.Name), 2)));
                if (device.Cooks.Length > 0)
                {
                    into.Add(new ItemRow("Cooks", Names(d.Content.Items.Where(i => i.Cook?.In == device.Cooks).Select(i => i.Name), 2)));
                }
                if (device.WarmthCelsius > 0)
                {
                    into.Add(new ItemRow("Warmth", $"+{N(device.WarmthCelsius)} °C within {Metres(device.WarmthRadius)}"));
                }
            }
            if (def.Recycler is { } recycler)
            {
                into.Add(new ItemRow("Recycles", $"{Percent(recycler.Efficiency)} of a recipe, every {Seconds(recycler.Seconds)}"));
            }
            if (def.Research is { } research)
            {
                into.Add(new ItemRow("Researches", $"a blueprint in {Seconds(research.Seconds)}"));
            }
            if (def.IsRespawnPoint)
            {
                into.Add(new ItemRow("Respawn point", "every " + Seconds(def.RespawnSeconds)));
            }
            if (def.Bay != null)
            {
                into.Add(new ItemRow("Builds", Names(d.Content.Recipes.Where(r => r.Station == def).Select(r => r.Output.Name))));
            }
        }

    }

    private sealed class ShipRows : IItemDescriber
    {
        public void Describe(in ItemDescription d, List<ItemRow> into)
        {
            Ships.ShipDef ship = d.Item.Ship!;
            into.Add(new ItemRow("Hull", N(ship.Health)));
            into.Add(new ItemRow("Fuel", $"{N(ship.FuelCapacity)} {ship.Fuel.Name.ToLowerInvariant()}"));
            into.Add(new ItemRow("Top speed", $"{N(ship.ForwardSpeed, "0")} m/s ({N(ship.BoostSpeed, "0")} boosted)"));
            if (ship.CargoSlots > 0)
            {
                into.Add(new ItemRow("Cargo", N(ship.CargoSlots) + " slots"));
            }
        }
    }

    private sealed class FirearmRows : IItemDescriber
    {
        public void Describe(in ItemDescription d, List<ItemRow> into)
        {
            FirearmStats gun = d.Item.Firearm!;
            GunMods mods = d.Stack is { } s ? GunMods.Of(d.Content, s, gun) : new GunMods(gun.Zoom, 1, 1, 1, 1, 1, false, false, 0);
            if (gun.Damage > 0)
            {
                into.Add(new ItemRow("Damage", N(gun.Damage * mods.Damage, "0.#")));
            }
            into.Add(new ItemRow("Fire", $"{(gun.Mode == FireMode.Auto ? "Auto" : "Semi")}, {N(60 / gun.IntervalSeconds, "0")} rpm"));
            into.Add(new ItemRow("Magazine", N(gun.Magazine)));
            into.Add(new ItemRow("Reload", Seconds(gun.ReloadSeconds)));
            into.Add(new ItemRow("Aim cone", $"{N(gun.Spread.Hip * mods.HipSpread)}° hip, {N(gun.Spread.Aimed * mods.AimedSpread)}° aimed"));
            into.Add(new ItemRow("Range", gun.FalloffEnd > gun.FalloffStart && gun.Damage > 0
                ? $"{Metres(gun.Range)}, full to {Metres(gun.FalloffStart)}"
                : Metres(gun.Range)));
            RecoilStats r = gun.Recoil;
            into.Add(new ItemRow("Recoil", r.Mode == RecoilMode.Pattern && r.Pattern.Count > 0
                ? $"{N(Math.Min(r.PitchLimit, r.Pattern.Take(gun.Magazine).Sum(p => p.Pitch)) * mods.Recoil, "0")}° up, a set pattern"
                : $"{N(r.PitchPerShot * mods.Recoil, "0.#")}° a shot"));
            if (d.Stack is { } stack)
            {
                if (d.Content.Item(stack.LoadedAmmo) is { } round)
                {
                    into.Add(new ItemRow("Loaded", $"{stack.Loaded} {round.Name.ToLowerInvariant()}", stack.Loaded > 0));
                }
                List<string> fitted = new();
                foreach (AttachmentSlot slot in GunMods.Slots)
                {
                    if (d.Content.Item(stack.Mod(slot)) is { } part)
                    {
                        fitted.Add(part.Name.ToLowerInvariant());
                    }
                }
                if (fitted.Count > 0)
                {
                    into.Add(new ItemRow("Fitted", string.Join(", ", fitted)));
                }
            }
            into.Add(new ItemRow("Takes", gun.Ammo.Count == 1 ? gun.Ammo[0].Name : $"{gun.Calibre} rounds, {gun.Ammo.Count} kinds"));
        }
    }

    private sealed class RoundRows : IItemDescriber
    {
        public void Describe(in ItemDescription d, List<ItemRow> into)
        {
            BulletStats b = d.Item.Bullet!;
            ItemDef item = d.Item;
            List<FirearmStats> guns = d.Content.Firearms.Where(g => g.Ammo.Contains(item)).ToList();
            into.Add(new ItemRow("For", Names(guns.Select(g => g.Item.Name))));
            if (guns.Count > 0 && guns[0].Damage > 0)
            {
                into.Add(new ItemRow("Damage", $"{N(guns[0].Damage * b.Damage, "0.#")} from the {guns[0].Item.Name.ToLowerInvariant()}", b.Damage >= 1));
            }
            if (guns.Count > 0)
            {
                into.Add(new ItemRow("Speed", $"{N(guns[0].Speed * b.Speed, "0")} m/s", b.Speed >= 1));
            }
            if (b.Bleed > 0)
            {
                into.Add(new ItemRow("Bleeding", Percent(b.Bleed) + " of the damage"));
            }
            if (b.Burn > 0)
            {
                into.Add(new ItemRow("Sets on fire", N(b.Burn) + " health"));
            }
            if (b.Structure > 0)
            {
                into.Add(new ItemRow("Against structures", Percent(b.Structure)));
            }
            if (b.Blast is { } blast)
            {
                BlastRows(blast, into);
            }
        }
    }

    // A blast (explosives.json), as its throwable or round shows it: how far it reaches and what it does to people, what it
    // does to whoever it strikes when that is more (IW4), to deployables, and the fire it leaves (IW4, IRON_WAKE.md §7).
    private static void BlastRows(BlastStats blast, List<ItemRow> into)
    {
        into.Add(new ItemRow("Blast", $"{Metres(blast.Radius)}, {N(blast.Flesh)} damage"));
        if (blast.Direct > blast.Flesh)
        {
            into.Add(new ItemRow("Direct hit", $"{N(blast.Direct)} damage"));
        }
        into.Add(new ItemRow("Deployables", N(blast.Deployable)));
        if (blast.Fire is { } fire)
        {
            into.Add(new ItemRow("Fire", $"{Metres(fire.Radius)} for {Seconds(fire.Seconds)}"));
            into.Add(new ItemRow("Burns", $"{N(fire.Flesh)} a second, and sets people alight"));
            into.Add(new ItemRow("Burns down", "wooden things, twig and wood"));
        }
    }

    private sealed class AttachmentRows : IItemDescriber
    {
        public void Describe(in ItemDescription d, List<ItemRow> into)
        {
            AttachmentStats a = d.Item.Attachment!;
            ItemDef item = d.Item;
            into.Add(new ItemRow("Fits", Names(d.Content.Firearms.Where(g => g.Takes(item)).Select(g => g.Item.Name))));
            into.Add(new ItemRow("Slot", a.Slot.ToString()));
            if (a.Zoom > 0)
            {
                into.Add(new ItemRow("Zoom", "×" + N(a.Zoom)));
            }
            Factor("Recoil", a.Recoil, lowerIsBetter: true);
            Factor("Hip cone", a.HipSpread, lowerIsBetter: true);
            Factor("Aimed cone", a.AimedSpread, lowerIsBetter: true);
            Factor("Damage", a.Damage, lowerIsBetter: false);
            Factor("Bullet speed", a.Speed, lowerIsBetter: false);
            if (a.Silenced)
            {
                into.Add(new ItemRow("Silenced", "No flash, quiet shots"));
            }
            if (a.Light)
            {
                into.Add(new ItemRow("Light", "Shines while held"));
            }

            void Factor(string label, double factor, bool lowerIsBetter)
            {
                if (Math.Abs(factor - 1) > 1e-6)
                {
                    into.Add(new ItemRow(label, Change(factor), lowerIsBetter ? factor < 1 : factor > 1));
                }
            }
        }
    }

    private sealed class ThrowableRows : IItemDescriber
    {
        public void Describe(in ItemDescription d, List<ItemRow> into)
        {
            ThrowableStats t = d.Item.Throwable!;
            BlastStats blast = t.Blast;
            string fuse = t.FuseLeast == t.FuseMost ? Seconds(t.FuseLeast) : $"{N(t.FuseLeast, "0.#")}–{Seconds(t.FuseMost)}";
            into.Add(new ItemRow("Fuse", t.DudChance > 0 ? $"{fuse}, {Percent(t.DudChance)} duds" : fuse, t.DudChance <= 0));
            BlastRows(blast, into);
            // How many it takes to break a wall of each grade (building.json's order; from its hard side) and a door.
            BuildingData b = d.Content.Building;
            foreach (GradeDef grade in b.Grades)
            {
                double damage = blast.Piece(grade.Key, soft: false);
                if (damage > 0 && grade.Health > 0)
                {
                    into.Add(new ItemRow($"{grade.Name} wall", ToBreak(grade.Health, damage)));
                }
            }
            var doors = new List<string>();
            foreach (DoorDef door in b.Doors.Where(x => x.Opening == Opening.Door))
            {
                double damage = blast.Door(door.Protection.Key);
                if (damage > 0)
                {
                    doors.Add($"{door.Protection.Name.ToLowerInvariant()} {Math.Ceiling(door.Health / damage - 1e-9):0}");
                }
            }
            if (doors.Count > 0)
            {
                into.Add(new ItemRow("Doors", string.Join(", ", doors.Distinct())));
            }
        }

        private static string ToBreak(double health, double damage)
        {
            int count = (int)Math.Ceiling(health / damage - 1e-9);
            return count == 1 ? "1 breaks it" : $"{count} to break";
        }
    }

    private sealed class PlanRows : IItemDescriber
    {
        public void Describe(in ItemDescription d, List<ItemRow> into)
        {
            BuildingData b = d.Content.Building;
            into.Add(new ItemRow("Builds", $"{b.Wheel.Count} pieces in {(b.Grades.Count > 0 ? b.Grades[0].Name.ToLowerInvariant() : "twig")}"));
            into.Add(new ItemRow("Reach", Metres(b.Reach)));
        }
    }

    private sealed class HammerRows : IItemDescriber
    {
        public void Describe(in ItemDescription d, List<ItemRow> into)
        {
            BuildingData b = d.Content.Building;
            into.Add(new ItemRow("Upgrades", Names(b.Grades.Skip(1).Select(g => g.Name.ToLowerInvariant()), 4)));
            into.Add(new ItemRow("Repairs", $"{N(b.RepairPerHit)} a hit, {Seconds(b.RepairDelay)} after damage"));
            into.Add(new ItemRow("Turns, demolishes", $"for {N(b.EditWindow / 60)} min after placing"));
        }
    }

    private sealed class CupboardRows : IItemDescriber
    {
        public void Describe(in ItemDescription d, List<ItemRow> into)
        {
            BuildingData b = d.Content.Building;
            into.Add(new ItemRow("Health", N(b.ClaimHealth)));
            into.Add(new ItemRow("Claims", $"{Metres(b.ClaimPieceRadius)} round its building"));
            into.Add(new ItemRow("Upkeep slots", N(b.ClaimSlots)));
            into.Add(new ItemRow("Authorised", $"up to {b.ClaimMaxAuthorised} players"));
        }
    }

    private sealed class LockRows : IItemDescriber
    {
        public void Describe(in ItemDescription d, List<ItemRow> into)
        {
            BuildingData b = d.Content.Building;
            bool code = d.Item.Building == BuildingRole.CodeLock;
            into.Add(new ItemRow("Lock", code ? $"a {b.CodeLength}-digit code" : "a key"));
            into.Add(new ItemRow("Fits", "a door"));
            into.Add(new ItemRow(code ? "Authorised" : "Keys", code ? $"up to {b.LockMaxAuthorised} players"
                : $"{b.KeyCost} {b.MaterialName(b.KeyCostMaterial).ToLowerInvariant()} each"));
        }
    }

    private sealed class KeyRows : IItemDescriber
    {
        public void Describe(in ItemDescription d, List<ItemRow> into) => into.Add(new ItemRow("Opens", "the key lock it was cut for"));
    }

    private sealed class DoorRows : IItemDescriber
    {
        public void Describe(in ItemDescription d, List<ItemRow> into)
        {
            DoorDef door = d.Item.Door!;
            BuildingData b = d.Content.Building;
            into.Add(new ItemRow("Health", N(door.Health)));
            into.Add(new ItemRow("Protection", door.Protection.Name));
            into.Add(new ItemRow("Fits", door.Opening == Opening.Door ? "a doorway" : "a wall frame"));
            into.Add(new ItemRow("Repair", $"{door.RepairFull} {b.MaterialName(door.RepairMaterial).ToLowerInvariant()} from nothing"));
            if (door.Rolls)
            {
                into.Add(new ItemRow("Opens", "rolling up"));
            }
        }
    }

    private sealed class BlueprintRows : IItemDescriber
    {
        public void Describe(in ItemDescription d, List<ItemRow> into)
        {
            if (d.Stack is not { } stack || d.Content.Recipe(stack.Condition) is not { } recipe)
            {
                into.Add(new ItemRow("Teaches", "the recipe written on it"));
                return;
            }
            into.Add(new ItemRow("Teaches", recipe.Output.Name));
            if (d.Known is { } known)
            {
                bool knows = known.Knows(recipe);
                into.Add(new ItemRow("Known", knows ? "Yes" : "Not yet", !knows));
            }
            if (recipe.Workbench > 0)
            {
                into.Add(new ItemRow("Made at", $"workbench level {recipe.Workbench}"));
            }
        }
    }

    private sealed class ComponentRows : IItemDescriber
    {
        public void Describe(in ItemDescription d, List<ItemRow> into)
        {
            ItemDef item = d.Item;
            if (item.UsedIn.Count > 0)
            {
                into.Add(new ItemRow("Used in", Names(item.UsedIn.Select(r => r.Output.Name))));
            }
            if (d.Content.World.FixedYield(item.Id) is { Count: > 0 } yields)
            {
                into.Add(new ItemRow("Recycles into", Names(yields.Select(y => $"{y.Count} {y.Item.Name.ToLowerInvariant()}"), 3)));
            }
        }
    }
}
