using System;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Simulation.World;

namespace Hollow.Simulation;

// Survival, once a second (GROUND.md §6): metabolism, temperature, bleeding, healing, and the damage of starvation,
// thirst and cold. Sleepers are left as they are until they wake.
public sealed class VitalsSystem : ZoneSystem
{
    private PlayerSystem? _playersSystem;
    private PlayerSystem Players => _playersSystem ??= Zone.Require<PlayerSystem>();
    private ThingSystem? _thingsSystem;
    private ThingSystem Things => _thingsSystem ??= Zone.Require<ThingSystem>();

    private void StepVitals()
    {
        GameContent content = Content;
        SurvivalTuning s = content.Survival;
        ComponentPool<PlayerLink> players = Entities.Pool<PlayerLink>();
        double now = UnixMilliseconds / 1000.0;
        for (int i = 0; i < players.Count; i++)
        {
            Player p = players.Values[i].Player;
            Entity entity = Entities.AtSlot(players.Slots[i]);
            ref Character c = ref Entities.Get<Character>(entity);
            if (!p.Dead)
            {
                float breathBefore = p.Vitals.BreathUsed;
                bool suffocatingBefore = p.Vitals.Suffocating;
                bool hasAir = !Breathing.Underwater(Surface, Frame.ToBody(Zone.EyeOf(c.State)));
                float drowning = Breathing.Step(ref p.Vitals, s, hasAir, 1);
                if (drowning > 0) Players.Hurt(entity, p, drowning, "Drowned", EntityId.None, "", canWound: false);
                if (breathBefore != p.Vitals.BreathUsed || suffocatingBefore != p.Vitals.Suffocating) p.VitalsRevision++;
            }
            if (!p.IsAwakeAndAlive || p.Wounded)
            {
                // Sleepers and the wounded are left as they are (the wounded's timer decides).
                continue;
            }
            ref Vitals v = ref p.Vitals;
            (int health, int calories, int hydration, int temperature, bool bleeding, bool burning) before =
                ((int)Math.Ceiling(v.Health), (int)v.Calories, (int)v.Hydration, (int)Math.Round(v.Temperature), v.Bleeding > 0, v.Burning > 0);

            // Temperature felt: the air, warmed by a fire nearby (up to a comfortable level), less when wet; clothing
            // keeps the cold off.
            if ((c.State.Flags & MoveFlags.Swimming) != 0)
            {
                v.WetSeconds = s.WetSeconds;
                v.Burning = 0;
            }
            else
            {
                v.WetSeconds = Math.Max(0, v.WetSeconds - 1);
            }
            System.Numerics.Vector3 where = WorldOf(c.State);
            float air = Climate?.TemperatureAt(where, now) ?? s.ClimateMeanCelsius;
            float warmth = Things.Warmth(where);
            v.Comfort = warmth > 0;
            float warmed = warmth > 0 ? Math.Max(air, Math.Min(air + warmth, s.FireWarmsTo)) : air;
            v.Temperature = warmed - (v.WetSeconds > 0 ? s.WetCooling : 0);
            v.Cold = Math.Clamp((s.ComfortableCelsius - v.Temperature - p.Inventory.Insulation(content)) / Math.Max(1, s.ColdScale), 0, 1);

            // Needs: faster when sprinting or cold.
            float factor = ((c.State.Flags & MoveFlags.Sprinting) != 0 ? s.SprintFactor : 1) * (v.Cold > 0 ? s.ColdFactor : 1);
            v.Calories = Math.Max(0, v.Calories - s.CaloriesPerSecond * factor);
            v.Hydration = Math.Max(0, v.Hydration - s.HydrationPerSecond * factor);

            // Healing: what is pending first, then well-fed regeneration (twice as fast by a fire).
            float heal = Math.Min(v.PendingHeal, s.HealPerSecond);
            v.PendingHeal -= heal;
            if (v.Calories >= s.RegenMinCalories && v.Hydration >= s.RegenMinHydration && v.Bleeding <= 0)
            {
                heal += s.RegenPerSecond * (v.Comfort ? s.ComfortRegenFactor : 1);
            }
            v.Health = Math.Min(s.MaxHealth, v.Health + heal);

            // Harm: bleeding, burning, starvation, thirst, cold. Cold and fire kill outright (Rust wounds for the rest).
            float harm = 0;
            string cause = "";
            bool outright = false;
            if (v.Burning > 0)
            {
                float burned = Math.Min(v.Burning, s.BurnDamagePerSecond);
                v.Burning = Math.Max(0, v.Burning - burned);
                harm += burned;
                cause = "Burned";
                outright = true;
            }
            if (v.Bleeding > 0)
            {
                float bled = Math.Min(v.Bleeding, s.BleedDamagePerSecond);
                v.Bleeding = Math.Max(0, v.Bleeding - bled - s.BleedClotPerSecond);
                harm += bled;
                cause = "Bled out";
            }
            if (v.Starving)
            {
                harm += s.StarvationDamagePerSecond;
                cause = "Starved";
            }
            if (v.Dehydrated)
            {
                harm += s.ThirstDamagePerSecond;
                cause = "Died of thirst";
            }
            if (v.Cold >= s.ColdDamageFrom)
            {
                harm += s.ColdDamagePerSecond * v.Cold;
                cause = "Froze";
                outright = true;
            }
            if (harm > 0)
            {
                Players.Hurt(entity, p, harm, cause, EntityId.None, "", canWound: !outright);
            }
            else
            {
                PlayerSystem.SyncHealth(ref c, p);
            }
            if (before != ((int)Math.Ceiling(v.Health), (int)v.Calories, (int)v.Hydration, (int)Math.Round(v.Temperature), v.Bleeding > 0, v.Burning > 0))
            {
                p.VitalsRevision++;
            }
        }
    }

    /// <summary>
    /// Eats or uses a consumable (GROUND.md §6): food and water at once, health at once and over time, bandages and
    /// injectors stop bleeding.
    /// </summary>
    public static void Consume(Player p, ConsumeStats consume, SurvivalTuning s)
    {
        ref Vitals v = ref p.Vitals;
        v.Calories = Math.Min(s.MaxCalories, v.Calories + consume.Calories);
        v.Hydration = Math.Min(s.MaxHydration, v.Hydration + consume.Hydration);
        v.Health = Math.Min(s.MaxHealth, v.Health + consume.Health);
        v.PendingHeal = Math.Min(s.MaxHealth, v.PendingHeal + consume.Heal);
        if (consume.StopsBleeding)
        {
            v.Bleeding = 0;
        }
        p.VitalsRevision++;
    }

    public override int Every => CharacterMotor.TickRate;

    protected internal override void Step()
    {
        StepVitals();
    }
}

/// <summary>The accessor on the zone (GROUND_SYSTEMS.md §12.2, K4).</summary>
public static class VitalsSystemAccess
{
    public static VitalsSystem Vitals(this Zone zone) => zone.Require<VitalsSystem>();
}
