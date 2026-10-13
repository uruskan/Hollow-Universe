using System;
using System.Collections.Generic;
using System.Linq;

namespace Hollow.Simulation.Content;

/// <summary>
/// Checks across the tables (GROUND.md §7): IDs and keys are unique, every reference resolves, the numbers make sense,
/// and every recipe can be made from what can be gathered: starting from what natural objects yield, a recipe is
/// makeable once its ingredients are, and its workbench level is (a workbench is an item too); a device cooks or
/// burns things once the device itself can be made.
/// </summary>
internal static class Validation
{
    public static void Check(GameContent.Loaded c, List<string> problems)
    {
        void Problem(string where, string what) => problems.Add($"{where}: {what}");

        // ---------------------------------------------------------------- uniqueness
        foreach (IGrouping<ushort, ItemDef> g in c.Items.GroupBy(i => i.Id).Where(g => g.Count() > 1))
        {
            Problem("items.json", $"id {g.Key} is used by {string.Join(", ", g.Select(i => i.Key))}");
        }
        Duplicates("categories.json", c.Categories.Select(x => x.Key));
        Duplicates("recipes.json", c.Recipes.Select(r => r.Key), "has more than one recipe (a second way needs its own key and id)");
        foreach (IGrouping<ushort, Recipe> g in c.Recipes.GroupBy(r => r.Id).Where(g => g.Count() > 1))
        {
            Problem("recipes.json", $"id {g.Key} is used by {string.Join(", ", g.Select(r => r.Key))}");
        }
        foreach (Recipe r in c.Recipes.Where(r => r.Id != r.Output.Id && c.Items.Any(i => i.Id == r.Id)))
        {
            Problem($"recipes.json: \"{r.Key}\"", $"id {r.Id} is an item's; a second way to make something needs a number no item uses");
        }
        Duplicates("gather.json", c.Natural.Select(k => k.Key));
        Duplicates("deployables.json", c.Deployables.Select(d => d.Key));
        Duplicates("loot.json", c.Loot.Select(t => t.Key));

        void Duplicates(string file, IEnumerable<string> keys, string what = "is used twice")
        {
            foreach (string key in keys.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key))
            {
                Problem(file, $"\"{key}\" {what}");
            }
        }

        // ---------------------------------------------------------------- items
        var categories = new HashSet<string>(c.Categories.Select(x => x.Key), StringComparer.Ordinal);
        var cookers = new HashSet<string>(c.Deployables.Where(d => d.Device != null).Select(d => d.Device!.Cooks), StringComparer.Ordinal);
        foreach (ItemDef item in c.Items)
        {
            string where = $"items.json: \"{item.Key}\"";
            if (item.Key.Length == 0 || item.Key.Any(ch => !(char.IsAsciiLetterLower(ch) || char.IsAsciiDigit(ch) || ch == '-')))
            {
                Problem(where, "keys are lower-case letters, digits and '-'");
            }
            if (!categories.Contains(item.Category))
            {
                Problem(where, $"category \"{item.Category}\" is not in categories.json");
            }
            if (item.StackSize < 1 || item.StackSize > 60_000)
            {
                Problem(where, $"stack {item.StackSize} must be 1 … 60000");
            }
            bool single = item.HasCondition || item.Wear != null || item.Melee != null || item.Bow != null || item.Deploys != null ||
                item.Firearm != null || item.Attachment != null || item.Ship != null;
            if (single && item.StackSize != 1)
            {
                Problem(where, "tools, weapons, clothing and deployables stack to 1");
            }
            if (item.MaxCondition < 0 || item.MaxCondition > 60_000)
            {
                Problem(where, "condition must be 0 … 60000");
            }
            if (item.Melee is { } m && (m.Reach <= 0 || m.Reach > 4 || m.SwingSeconds <= 0 || m.StrikeSeconds < 0 ||
                m.StrikeSeconds > m.SwingSeconds || m.Damage < 0 || m.Tree < 0 || m.Ore < 0 || m.Bleed is < 0 or > 2))
            {
                Problem(where, "melee: reach 0 … 4 m, swing > 0, strike within the swing, nothing negative");
            }
            if (item.Bow is { } bow && (bow.Ammo.Count == 0 || bow.Ammo.Any(a => a.Ammo == null) || bow.DrawSeconds <= 0))
            {
                Problem(where, "bow: needs ammo that is ammunition, and a draw time");
            }
            if (item.Ammo is { } ammo && (ammo.Speed is <= 0 or > 2000 || ammo.Damage < 0 || ammo.LifetimeSeconds is <= 0 or > 10 ||
                ammo.Recover is < 0 or > 1 || ammo.Gravity < 0))
            {
                Problem(where, "ammo: speed 0 … 2000 m/s, lifetime 0 … 10 s, recover 0 … 1");
            }
            if (item.Wear is { } wear && (wear.Layers == WearLayer.None || wear.Melee is < 0 or > 0.9f || wear.Projectile is < 0 or > 0.9f))
            {
                Problem(where, "wear: needs a layer; protection 0 … 0.9");
            }
            if (item.Fuel is { } fuel && fuel.Seconds <= 0)
            {
                Problem(where, "fuel: seconds must be positive");
            }
            if (item.Cook is { } cook)
            {
                if (cook.Seconds <= 0)
                {
                    Problem(where, "cook: seconds must be positive");
                }
                if (!cookers.Contains(cook.In))
                {
                    Problem(where, $"cook: no device cooks \"{cook.In}\"");
                }
                if (cook.Into == item)
                {
                    Problem(where, "cook: an item can't cook into itself");
                }
            }
        }

        // ---------------------------------------------------------------- recipes
        foreach (Recipe r in c.Recipes)
        {
            string where = $"recipes.json: \"{r.Output.Key}\"";
            if (r.Amount < 1 || r.Amount > r.Output.StackSize)
            {
                Problem(where, $"amount {r.Amount} must be 1 … the output's stack ({r.Output.StackSize})");
            }
            if (r.Seconds <= 0 || r.Seconds > 3600)
            {
                Problem(where, "seconds must be 0 … 3600");
            }
            if (r.Workbench is < 0 or > 3)
            {
                Problem(where, "workbench must be 0 … 3");
            }
            if (r.Ingredients.Count == 0 || r.Ingredients.Any(i => i.Count < 1))
            {
                Problem(where, "needs ingredients, each at least 1");
            }
            if (r.Ingredients.GroupBy(i => i.Item).Any(g => g.Count() > 1))
            {
                Problem(where, "an ingredient is listed twice");
            }
            if (r.Ingredients.Any(i => i.Item == r.Output))
            {
                Problem(where, "an ingredient is its own output");
            }
            if (r.Output.Ship != null && (r.Station?.Bay == null || r.Amount != 1))
            {
                Problem(where, "a ship is made one at a time at a station with a bay to roll out onto (G6)");
            }
        }

        // ---------------------------------------------------------------- natural objects
        foreach (NaturalKind k in c.Natural)
        {
            string where = $"gather.json: \"{k.Key}\"";
            if (k.Class != NaturalClass.Boulder && (k.Yield.Count == 0 || k.Yield.Any(y => y.Count < 1)))
            {
                Problem(where, "needs a yield, each at least 1");
            }
            if (k.IsHarvested && k.Health <= 0)
            {
                Problem(where, "trees and outcrops need health");
            }
            if (k.Stages is < 1 or > 3)
            {
                Problem(where, "stages must be 1 … 3");
            }
            if (k.RespawnSeconds <= 0 || k.Radius <= 0 || k.Height <= 0 || k.StandInPerTile < 0)
            {
                Problem(where, "respawn, radius and height must be positive");
            }
            if (k.Gather is { } g)
            {
                if (g.Spot.Radius <= 0 || g.Spot.Step < 0 || g.Spot.Max < 1 || g.Spot.FacingDegrees is < 0 or > 180)
                {
                    Problem(where, "the weak spot's radius must be positive, its step at least 0, its ceiling at least 1, its facing 0 … 180°");
                }
                if (g.Last.Share is < 0 or >= 1 || g.Last.Bonus < 0)
                {
                    Problem(where, "the last hit's share must be 0 … under 1 and its bonus at least 0");
                }
                if (g.Look.Marker is { } m && (m.Style.Length == 0 || m.Size <= 0 || m.Depth <= 0 || m.SparkleEvery <= 0))
                {
                    Problem(where, "the marker needs a style, a size, a depth and a sparkle interval");
                }
            }
        }

        // ---------------------------------------------------------------- deployables
        foreach (DeployableDef d in c.Deployables)
        {
            string where = $"deployables.json: \"{d.Key}\"";
            if (d.Item.Deploys != d)
            {
                Problem(where, $"item \"{d.Item.Key}\" must say \"deploys\": \"{d.Key}\"");
            }
            if (d.Health <= 0 || d.HalfExtents.X <= 0 || d.HalfExtents.Y <= 0 || d.HalfExtents.Z <= 0)
            {
                Problem(where, "health and size must be positive");
            }
            if (d.Slots is < 0 or > 64 || d.Workbench is < 0 or > 3 || d.RespawnSeconds < 0)
            {
                Problem(where, "slots 0 … 64, workbench 0 … 3");
            }
            if (d.Device is { } device)
            {
                int[] all = device.FuelSlots.Concat(device.InputSlots).Concat(device.OutputSlots).ToArray();
                if (all.Any(s => s < 0 || s >= d.Slots) || all.Distinct().Count() != all.Length)
                {
                    Problem(where, "device slots must be distinct and within the slots");
                }
                if (device.FuelSlots.Count == 0 || device.OutputSlots.Count == 0)
                {
                    Problem(where, "a device needs fuel and output slots");
                }
            }
        }
        foreach (DeployableDef d in c.Deployables.Where(d => d.Bay != null))
        {
            BayStats bay = d.Bay!;
            if (bay.Clear.X <= 0 || bay.Clear.Y <= 0 || bay.Clear.Z <= 0 || bay.MaxSlopeDegrees is <= 0 or > 45 || bay.At.Length() > 30)
            {
                Problem($"deployables.json: \"{d.Key}\".bay", "a clear space of positive size, within 30 m, on ground of 0 … 45°");
            }
        }

        // ---------------------------------------------------------------- ships (G6)
        Duplicates("ships.json", c.Ships.Select(x => x.Key));
        foreach (Ships.ShipDef ship in c.Ships)
        {
            string where = $"ships.json: \"{ship.Key}\"";
            if (ship.Item.Ship != ship)
            {
                Problem(where, $"item \"{ship.Item.Key}\" must say \"ship\": \"{ship.Key}\"");
            }
            if (ship.Mass <= 0 || ship.Inertia.X <= 0 || ship.Inertia.Y <= 0 || ship.Inertia.Z <= 0 || ship.Health <= 0)
            {
                Problem(where, "mass, inertia and health must be positive");
            }
            if (new[] { ship.MainThrust, ship.RetroThrust, ship.LateralThrust, ship.LiftThrust, ship.DownThrust }.Any(t => t <= 0) ||
                ship.LiftThrust / ship.Mass < 10)
            {
                Problem(where, "thrust on every axis; the lift nozzles must carry it at 1 g with some to spare (10 m/s²)");
            }
            if (new[] { ship.ForwardSpeed, ship.ReverseSpeed, ship.LateralSpeed, ship.UpSpeed, ship.DownSpeed }.Any(v => v is <= 0 or > 1000) ||
                ship.BoostSpeed < ship.ForwardSpeed || ship.BoostThrust < 1 || ship.BoostSeconds <= 0 || ship.BoostCooldownSeconds < 0)
            {
                Problem(where, "speeds 0 … 1000 m/s, boost at least the forward speed and thrust, a positive boost time");
            }
            if (new[] { ship.Pitch, ship.Yaw, ship.Roll }.Any(t => t.MaxRate <= 0 || t.Acceleration <= 0))
            {
                Problem(where, "rates: positive rates and accelerations");
            }
            if (new[] { ship.DragForward, ship.DragSide, ship.DragTop, ship.TurnDrag }.Any(d => d < 0))
            {
                Problem(where, "drag can't be negative");
            }
            if (ship.SkidPoints.Count < 3 || ship.SkidStiffness <= 0 || ship.SkidDamping <= 0 || ship.SkidTravel is <= 0 or > 1 ||
                ship.SkidFriction is < 0 or > 2 || ship.MaxLandingSlope is <= 0 or > 0.8f)
            {
                Problem(where, "at least three skid points, positive springs, travel 0 … 1 m, friction 0 … 2, a landing slope up to 45°");
            }
            if (ship.CrashSafeSpeed <= 0 || ship.CrashScale <= 0 || ship.CrashPilotShare is < 0 or > 1)
            {
                Problem(where, "crash: a positive safe speed and scale, a pilot share of 0 … 1");
            }
            if (ship.FuelCapacity <= 0 || ship.FuelIdle < 0 || ship.FuelFull < ship.FuelIdle || ship.FuelBoost < 1)
            {
                Problem(where, "fuel: a tank, idle ≤ full burn, boost at least 1");
            }
            if (ship.Hull.Count == 0 || ship.Hull.Any(h => h.HalfExtents.X <= 0 || h.HalfExtents.Y <= 0 || h.HalfExtents.Z <= 0))
            {
                Problem(where, "hull: boxes of positive size");
            }
            if (ship.CargoSlots is < 0 or > 64 || ship.Exits.Count == 0 || ship.Ceiling is < 1000 or > 19000 ||
                new[] { ship.MeleeDamage, ship.ArrowDamage, ship.BulletDamage, ship.ExplosiveDamage }.Any(m => m is < 0 or > 10))
            {
                Problem(where, "cargo 0 … 64 slots, an exit, a ceiling of 1 … 19 km (inside the zone), damage multipliers 0 … 10");
            }
        }

        // ---------------------------------------------------------------- loot and survival
        foreach (LootTable t in c.Loot)
        {
            string where = $"loot.json: \"{t.Key}\"";
            if (t.Guaranteed.Concat(t.Entries).Any(e => e.Least < 1 || e.Most < e.Least || e.Weight <= 0))
            {
                Problem(where, "each entry needs 1 ≤ least ≤ most and a positive weight");
            }
            if (t.LeastRolls < 0 || t.MostRolls < t.LeastRolls)
            {
                Problem(where, "rolls must be 0 ≤ least ≤ most");
            }
        }
        SurvivalTuning s = c.Survival;
        if (!c.Loot.Any(t => t.Key == s.RespawnKitKey))
        {
            Problem("survival.json: world.respawnKit", $"no loot table \"{s.RespawnKitKey}\"");
        }
        if (s.MaxHealth <= 0 || s.SpawnHealth <= 0 || s.SpawnHealth > s.MaxHealth || s.SpawnCalories > s.MaxCalories ||
            s.SpawnHydration > s.MaxHydration || s.MaxRewindSeconds is < 0 or > 1 || s.IntentBurst < 1 || s.IntentsPerSecond <= 0)
        {
            Problem("survival.json", "spawn vitals within the maxima; rewind 0 … 1 s; positive rate limits");
        }

        // ---------------------------------------------------------------- weapons (G4; RustMVP's valid_weapon_definition)
        foreach (FirearmStats g in c.Firearms)
        {
            string where = $"weapons.json: weapon \"{g.Item.Key}\"";
            if (!g.Item.HasCondition)
            {
                Problem(where, "a firearm has condition (shots before it breaks)");
            }
            if (g.Magazine is < 1 or > 255 || g.IntervalSeconds <= 0 || g.ReloadSeconds <= 0 || g.EquipSeconds < 0)
            {
                Problem(where, "magazine 1 … 255, interval and reload positive, equip not negative");
            }
            if (g.Speed is <= 0 or > 2000 || g.Drag is < 0 or > 5 || g.Gravity is < 0 or > 10 || g.Range <= 0 || g.LifetimeSeconds is <= 0 or > 5)
            {
                Problem(where, "speed 0 … 2000 m/s, drag 0 … 5, gravity 0 … 10, range positive, lifetime 0 … 5 s");
            }
            if (g.Damage < 0 || g.FalloffStart < 0 || g.FalloffEnd < g.FalloffStart || g.FalloffLeast is <= 0 or > 1 ||
                new[] { g.Head, g.Body, g.Limb }.Any(m => m is < 0 or > 10))
            {
                Problem(where, "damage not negative; falloff [start ≤ end, least 0 … 1]; multipliers 0 … 10");
            }
            SpreadStats sp = g.Spread;
            if (new[] { sp.Hip, sp.Aimed, sp.BloomPerShot, sp.MaxBloom, sp.BloomRecovery, sp.Moving, sp.MovingAimed, sp.Airborne, sp.Crouched }.Any(v => v < 0) ||
                sp.Hip + sp.MaxBloom >= 45 || sp.Aimed + sp.MaxBloom >= 45)
            {
                Problem(where, "spread: nothing negative, hip or aimed + the most bloom under 45°");
            }
            RecoilStats r = g.Recoil;
            if (r.PitchLimit is < 0 or > 85 || r.YawLimit is < 0 or > 180 || r.KickSeconds < 0 || r.KickSeconds > g.IntervalSeconds ||
                r.RecoveryDelaySeconds < 0 || r.RecoverySeconds < 0)
            {
                Problem(where, "recoil: limits up to 85° and 180°, the kick no longer than the interval, nothing negative");
            }
            if (r.Mode == RecoilMode.Vertical && (r.PitchPerShot < 0 || r.PitchPerShot > r.PitchLimit))
            {
                Problem(where, "recoil: the kick within the pitch limit");
            }
            if (r.Mode == RecoilMode.Pattern && (r.Pattern.Count is < 1 or > 32 || r.ResetSeconds <= 0 ||
                r.Pattern.Any(st => st.Pitch < 0 || st.Pitch > r.PitchLimit || Math.Abs(st.Yaw) > r.YawLimit)))
            {
                Problem(where, "recoil: a pattern of 1 … 32 steps within the limits, and a reset time");
            }
            if (g.Zoom < 1)
            {
                Problem(where, "zoom at least 1");
            }
            if (g.Ammo.Count == 0)
            {
                Problem(where, $"no rounds of calibre \"{g.Calibre}\"");
            }
            foreach (ItemDef a in g.Attachments.Where(a => a.Attachment == null))
            {
                Problem(where, $"\"{a.Key}\" is not an attachment");
            }
        }
        foreach (BulletStats b in c.Bullets)
        {
            string where = $"weapons.json: ammo \"{b.Item.Key}\"";
            if (!c.Firearms.Any(g => g.Calibre == b.Calibre))
            {
                Problem(where, $"no weapon fires calibre \"{b.Calibre}\"");
            }
            if (b.Damage is <= 0 or > 10 || b.Speed is <= 0 or > 10 || b.Bleed is < 0 or > 2 || b.Burn is < 0 or > 100 || b.Structure is < 0 or > 1)
            {
                Problem(where, "damage and speed 0 … 10 times, bleed 0 … 2, burn 0 … 100, structure 0 … 1");
            }
            if (b.Item.Ammo != null || b.Item.Firearm != null)
            {
                Problem(where, "a round is not an arrow or a gun");
            }
        }
        foreach (AttachmentStats a in c.Attachments)
        {
            string where = $"weapons.json: attachment \"{a.Item.Key}\"";
            if ((a.Zoom != 0 && a.Zoom < 1) || new[] { a.HipSpread, a.AimedSpread, a.Recoil, a.Speed, a.Damage }.Any(m => m is <= 0 or > 10))
            {
                Problem(where, "zoom 0 (the gun's) or at least 1; multipliers 0 … 10");
            }
            if (!c.Firearms.Any(g => g.Attachments.Contains(a.Item)))
            {
                Problem(where, "no weapon takes it");
            }
        }
        if (c.Attachments.Count > 8)
        {
            Problem("weapons.json: attachments", "at most 8 (others see a held gun's attachments as 8 bits)");
        }
        if (s.WoundMaxDamage < 0 || s.WoundedHealthLeast <= 0 || s.WoundedHealthMost < s.WoundedHealthLeast || s.WoundedHealthMost > s.MaxHealth ||
            s.WoundedSecondsLeast <= 0 || s.WoundedSecondsMost < s.WoundedSecondsLeast || s.WoundedRecoverChance is < 0 or > 1 ||
            s.WoundedFedBonus is < 0 or > 1 || s.BurnDamagePerSecond < 0 || s.GunBurst < 1 || s.GunActionsPerSecond <= 0)
        {
            Problem("survival.json", "wounded: health and seconds [least ≤ most], chances 0 … 1; burning and the gun budget positive");
        }
        if (new[] { s.InteractReach, s.OpenReach, s.HelpReach, s.InviteReach, s.StationReach }.Any(r => r is <= 0 or > 10) ||
            s.KeepOpenReach < s.OpenReach)
        {
            Problem("survival.json", "reach: every reach 0 … 10 m, and keepOpen at least open");
        }

        // ---------------------------------------------------------------- every recipe from gatherable resources
        var obtainable = new HashSet<ItemDef>();
        foreach (NaturalKind k in c.Natural)
        {
            foreach (ItemAmount y in k.Yield)
            {
                obtainable.Add(y.Item);
            }
        }
        // G7: what animals' corpses give (animals.json) is gathered too.
        foreach (AnimalSpecies species in c.Animals)
        {
            foreach (ItemAmount y in species.Harvest)
            {
                obtainable.Add(y.Item);
            }
        }
        // G7: what the world's barrels and crates hold is found, not made; the recycler turns items into others.
        foreach (PropDef prop in c.World?.Props ?? Array.Empty<PropDef>())
        {
            foreach (LootTable.Entry e in prop.Loot is { } table ? table.Guaranteed.Concat(table.Entries) : Array.Empty<LootTable.Entry>())
            {
                if (e.Tier < 0)
                {
                    obtainable.Add(e.Item);
                }
            }
        }
        bool grew = true;
        while (grew)
        {
            grew = false;
            int bench = c.Deployables.Where(d => d.Workbench > 0 && obtainable.Contains(d.Item)).Select(d => d.Workbench).DefaultIfEmpty(0).Max();
            var devices = c.Deployables.Where(d => d.Device != null && obtainable.Contains(d.Item)).Select(d => d.Device!).ToList();
            foreach (Recipe r in c.Recipes)
            {
                if (r.Workbench <= bench && r.Ingredients.All(i => obtainable.Contains(i.Item)) &&
                    (r.Station == null || obtainable.Contains(r.Station.Item)))
                {
                    grew |= obtainable.Add(r.Output);
                }
            }
            foreach (ItemDef item in obtainable.ToList())
            {
                if (item.Cook?.Into is { } into && devices.Any(d => d.Cooks == item.Cook.In))
                {
                    grew |= obtainable.Add(into);
                }
                if (item.Fuel?.Byproduct is { } byproduct && devices.Count > 0)
                {
                    grew |= obtainable.Add(byproduct);
                }
                if (c.World is { RecyclerCount: > 0 } world && world.FixedYield(item.Id) is { } yields)
                {
                    foreach (ItemAmount y in yields)
                    {
                        grew |= obtainable.Add(y.Item);
                    }
                }
            }
        }
        foreach (Recipe r in c.Recipes.Where(r => !obtainable.Contains(r.Output)))
        {
            string missing = string.Join(", ", r.Ingredients.Where(i => !obtainable.Contains(i.Item)).Select(i => i.Item.Key));
            Problem($"recipes.json: \"{r.Output.Key}\"", missing.Length > 0
                ? $"can't be made from gatherable resources (nothing gives {missing})"
                : r.Station != null && !obtainable.Contains(r.Station.Item)
                ? $"needs a {r.Station.Key}, which can't be made"
                : $"needs workbench {r.Workbench}, which can't be made");
        }
    }
}
