using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hollow.Client;
using Hollow.Core;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Nature;

namespace Hollow.Bots;

/// <summary>
/// A bot that plays the survival game (GROUND.md §6) through the same client API as a player: it walks to trees and
/// outcrops and hits their weak spots, picks pickups and berry bushes, crafts stone tools, bandages and arrows,
/// eats and bandages itself, lights a campfire now and then, and once in a while picks a fight with a bot nearby,
/// with a spear or the bow. Deterministic per bot (seeded by its number); stays within
/// <see cref="BotBrain.HomeRadius"/> of the zone's origin so the crowd stays in view of itself (the bandwidth worst case).
/// </summary>
public sealed class SurvivalBrain
{
    private enum Goal
    {
        Gather,
        Pick,
        Fight,
        Wander,
    }

    private readonly List<NaturalObject> _nearby = new();
    private Rng _rng;
    private Goal _goal = Goal.Wander;
    private ulong _target;
    private EntityId _enemy;
    private double _nextPlan;
    private double _nextChore;
    private double _goalUntil;
    private double _drawStart = -1;
    private float _yaw;
    private float _pitch;
    private bool _campfire;

    public SurvivalBrain(int index)
    {
        _rng = new Rng(Hash64.Of((ulong)index, 0x5B07));
        _yaw = (float)_rng.Range(-Math.PI, Math.PI);
        _nextPlan = _rng.Range(0, 1);
        _nextChore = _rng.Range(0, 2);
    }

    /// <summary>What the bot did, for the load test's report.</summary>
    public int Crafts { get; private set; }

    public int Picks { get; private set; }

    public int Fights { get; private set; }

    public InputFrame Think(double time, double delta, GameClient client)
    {
        LocalPlayer me = client.Local;
        var input = new InputFrame { Yaw = _yaw, Pitch = _pitch };
        if (me.IsDead)
        {
            if (time >= _nextChore)
            {
                _nextChore = time + 2;
                me.Respawn();
            }
            return input;
        }
        if (time >= _nextChore && me.PendingCount == 0)
        {
            _nextChore = time + _rng.Range(1.5, 3);
            Chores(client, me);
        }
        if (time >= _nextPlan)
        {
            _nextPlan = time + _rng.Range(0.8, 1.6);
            Plan(time, client, me);
        }
        Vector3 feet = client.Character.Position;
        Vector3 up = client.Frame.UpAt(feet);
        Vector3 eye = feet + up * client.EyeHeight;
        switch (_goal)
        {
            case Goal.Gather or Goal.Pick when client.Nature!.TryGet(_target, out NaturalObject o) && !client.Natural!.IsDepleted(_target):
            {
                NaturalKind kind = client.Content.Natural(o.Kind)!;
                // A tree's or outcrop's weak spot once it shows (after the first hit, facing the hitter), else its middle.
                Vector3 aim = kind.IsHarvested
                    ? client.Natural.WeakSpot(o, kind) ?? o.Position + o.Up * (kind.Class == NaturalClass.Tree ? 1.2f : kind.Radius * o.Scale * 0.6f)
                    : NaturalShapes.Middle(o, kind);
                float reach = _goal == Goal.Gather ? 1.1f + kind.Radius * o.Scale : 1.6f;
                if (Steer(ref input, feet, o.Position, reach, delta))
                {
                    Look(ref input, eye, aim, up);
                    if (_goal == Goal.Gather)
                    {
                        input.Primary = true;
                    }
                    else if (client.Aim(eye, Vector3.Normalize(aim - eye)) is { Kind: InteractionKind.PickNatural } pick)
                    {
                        client.Interact(pick);
                        Picks++;
                        _goal = Goal.Wander;
                    }
                }
                break;
            }
            case Goal.Fight when client.Entities.FirstOrDefault(e => e.Id == _enemy) is { } enemy && !enemy.Sleeping &&
                                 (enemy.Flags & MoveFlags.Dead) == 0 && time < _goalUntil:
            {
                Vector3 chest = enemy.Position + up * 1.2f;
                bool bow = me.HeldItem?.Bow != null;
                bool close = Steer(ref input, feet, enemy.Position, bow ? 14f : 1.6f, delta);
                Look(ref input, eye, chest + up * (bow ? 0.4f : 0f), up);
                if (bow)
                {
                    input.Secondary = close || Vector3.Distance(feet, enemy.Position) < 25;
                    if (input.Secondary && _drawStart < 0)
                    {
                        _drawStart = time;
                    }
                    if (close && time - _drawStart > 1.2)
                    {
                        input.Primary = input.PrimaryPressed = true;
                        _drawStart = time;
                    }
                }
                else
                {
                    input.Primary = close;
                }
                break;
            }
            default:
                _goal = Goal.Wander;
                Wander(ref input, feet, delta);
                break;
        }
        _yaw = input.Yaw;
        _pitch = input.Pitch;
        return input;
    }

    // Choose what to do next: mostly gather what the inventory lacks, sometimes pick something up, now and then fight.
    private void Plan(double time, GameClient client, LocalPlayer me)
    {
        if (_goal == Goal.Fight && time < _goalUntil)
        {
            return;
        }
        Vector3 feet = client.Character.Position;
        if (_rng.Chance(0.015))
        {
            RemoteEntity? other = client.Entities
                .Where(e => !e.Sleeping && (e.Flags & MoveFlags.Dead) == 0 && Vector3.Distance(e.Position, feet) < 30)
                .OrderBy(e => Vector3.Distance(e.Position, feet)).FirstOrDefault();
            if (other != null)
            {
                _goal = Goal.Fight;
                _enemy = other.Id;
                _goalUntil = time + _rng.Range(6, 12);
                Fights++;
                Hold(me, client.Content, _rng.Chance(0.5) ? "bow" : "stone-spear", "wooden-spear", "machete", "rock");
                return;
            }
        }
        _nearby.Clear();
        client.Nature!.Query(feet.X - 40, feet.Z - 40, feet.X + 40, feet.Z + 40, _nearby);
        bool home = new Vector2(feet.X, feet.Z).Length() < BotBrain.HomeRadius;
        bool wantsOre = me.Inventory.Count(client.Content.ItemByKey("stone")) < me.Inventory.Count(client.Content.ItemByKey("wood"));
        NaturalObject? best = null;
        float bestScore = float.MaxValue;
        foreach (NaturalObject o in _nearby)
        {
            NaturalKind kind = client.Content.Natural(o.Kind)!;
            if (client.Natural!.IsDepleted(o.Id) || (!home && new Vector2(o.Position.X, o.Position.Z).Length() > BotBrain.HomeRadius))
            {
                continue;
            }
            float distance = Vector3.Distance(o.Position, feet);
            float score = distance * (kind.IsPicked ? (distance < 12 ? 0.5f : 3f) : kind.Class == NaturalClass.Node == wantsOre ? 1f : 1.6f);
            score += (float)_rng.Range(0, 6);
            if (score < bestScore)
            {
                bestScore = score;
                best = o;
            }
        }
        if (best is { } target)
        {
            NaturalKind kind = client.Content.Natural(target.Kind)!;
            _target = target.Id;
            _goal = kind.IsPicked ? Goal.Pick : Goal.Gather;
            if (kind.Class == NaturalClass.Tree)
            {
                Hold(me, client.Content, "metal-hatchet", "stone-hatchet", "rock");
            }
            else if (kind.Class == NaturalClass.Node)
            {
                Hold(me, client.Content, "metal-pickaxe", "stone-pickaxe", "rock");
            }
        }
        else
        {
            _goal = Goal.Wander;
        }
    }

    // Keep tools on the belt, craft what is missing, eat and bandage, light a fire once.
    private void Chores(GameClient client, LocalPlayer me)
    {
        GameContent c = client.Content;
        // Clothing on, where a layer is free; tools and weapons onto the belt, swapped for whatever else is there.
        for (int i = 0; i < PlayerInventory.MainSlots; i++)
        {
            ItemStack stack = me.Inventory.Main[i];
            ItemDef? item = c.Item(stack.Item);
            if (item?.Wear != null && Enumerable.Range(0, PlayerInventory.WearSlots)
                    .Any(w => me.Inventory.Wear[w].IsEmpty && me.Inventory.Wear.Accepts(c, w, stack)))
            {
                me.QuickMove(ContainerRef.Main, i);
                return;
            }
            if (item is { Melee: not null } or { Bow: not null })
            {
                int free = Enumerable.Range(0, PlayerInventory.BeltSlots).FirstOrDefault(b => !IsTool(c, me.Inventory.Belt[b]), -1);
                if (free >= 0)
                {
                    me.MoveItem(ContainerRef.Main, i, ContainerRef.Belt, free);
                    return;
                }
            }
        }
        if (me.Vitals.IsBleeding && Slot(me, c, "bandage") is { } bandage)
        {
            me.Use(bandage.Which, bandage.Slot);
            return;
        }
        if (me.Vitals.Calories < 200 && (Slot(me, c, "roasted-tuber") ?? Slot(me, c, "red-berries") ?? Slot(me, c, "blue-berries") ?? Slot(me, c, "wild-tuber")) is { } food)
        {
            me.Use(food.Which, food.Slot);
            return;
        }
        if (me.Crafting.Count == 0)
        {
            foreach (string want in new[] { "stone-hatchet", "stone-pickaxe", "bandage", "arrow", "torch" })
            {
                Recipe recipe = c.Recipe(c.ItemByKey(want).Id)!;
                bool missing = want switch
                {
                    "bandage" => me.Inventory.Count(c.ItemByKey(want)) < 2,
                    "arrow" => me.Inventory.Count(c.ItemByKey(want)) < 10,
                    _ => me.Inventory.Count(c.ItemByKey(want)) == 0,
                };
                if (missing && me.CanCraft(recipe) == Hollow.Simulation.ActionResult.Accepted)
                {
                    me.Craft(recipe.Id);
                    Crafts++;
                    return;
                }
            }
            // Keep the queue busy now and then, as players do.
            Recipe spare = c.Recipe(c.ItemByKey(_rng.Chance(0.5) ? "arrow" : "torch").Id)!;
            if (_rng.Chance(0.3) && me.CanCraft(spare, 2) == Hollow.Simulation.ActionResult.Accepted)
            {
                me.Craft(spare.Id, 2);
                Crafts++;
                return;
            }
        }
        if (!_campfire && Slot(me, c, "campfire") is { } fire)
        {
            Vector3 feet = client.Character.Position;
            Vector3 up = client.Frame.UpAt(feet);
            (Vector3 forward, _) = CharacterMotor.HorizontalBasis(_yaw, up);
            Vector3? at = client.AimGround(feet + up * 2 + forward * 2.2f, -up, 4);
            if (at != null)
            {
                me.Place(fire.Which, fire.Slot, at.Value, _yaw);
                _campfire = true;
            }
        }
    }

    private static bool IsTool(GameContent c, ItemStack stack) => c.Item(stack.Item) is { Melee: not null } or { Bow: not null };

    private static (ContainerRef Which, int Slot)? Slot(LocalPlayer me, GameContent c, string key)
    {
        ushort id = c.ItemByKey(key).Id;
        foreach (ContainerRef which in new[] { ContainerRef.Belt, ContainerRef.Main })
        {
            Container container = me.Resolve(which)!;
            for (int i = 0; i < container.Size; i++)
            {
                if (container[i].Item == id)
                {
                    return (which, i);
                }
            }
        }
        return null;
    }

    // Takes the first of these items that is on the belt into the hand.
    private static void Hold(LocalPlayer me, GameContent c, params string[] keys)
    {
        foreach (string key in keys)
        {
            ushort id = c.ItemByKey(key).Id;
            for (int i = 0; i < PlayerInventory.BeltSlots; i++)
            {
                if (me.Inventory.Belt[i].Item == id)
                {
                    me.HeldSlot = i;
                    return;
                }
            }
        }
    }

    // Walks toward a point until within reach of it; true once there.
    private bool Steer(ref InputFrame input, Vector3 feet, Vector3 to, float reach, double delta)
    {
        Vector3 d = to - feet;
        var flat = new Vector2(d.X, d.Z);
        if (flat.Length() <= reach)
        {
            return true;
        }
        float wanted = MathF.Atan2(-flat.X, -flat.Y);
        input.Yaw = _yaw + Math.Clamp(Wrap(wanted - _yaw), -(float)delta * 4, (float)delta * 4);
        input.Pitch = 0;
        input.Forward = 1;
        input.Sprint = flat.Length() > 12;
        return false;
    }

    private static void Look(ref InputFrame input, Vector3 eye, Vector3 at, Vector3 up)
    {
        Vector3 d = at - eye;
        Vector3 flat = d - up * Vector3.Dot(d, up);
        input.Yaw = MathF.Atan2(-flat.X, -flat.Z);
        input.Pitch = MathF.Atan2(Vector3.Dot(d, up), flat.Length());
        input.Forward = 0;
    }

    private void Wander(ref InputFrame input, Vector3 feet, double delta)
    {
        var flat = new Vector2(feet.X, feet.Z);
        float wanted = flat.Length() > BotBrain.HomeRadius * 0.8f ? MathF.Atan2(flat.X, flat.Y) : _yaw + (float)_rng.Range(-0.3, 0.3);
        input.Yaw = _yaw + Math.Clamp(Wrap(wanted - _yaw), -(float)delta * 2, (float)delta * 2);
        input.Pitch = 0;
        input.Forward = 1;
    }

    private static float Wrap(float a)
    {
        while (a > MathF.PI)
        {
            a -= 2 * MathF.PI;
        }
        while (a < -MathF.PI)
        {
            a += 2 * MathF.PI;
        }
        return a;
    }
}
