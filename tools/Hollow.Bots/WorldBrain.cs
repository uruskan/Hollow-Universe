using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hollow.Client;
using Hollow.Core;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Economy;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.World;

namespace Hollow.Bots;

/// <summary>
/// A bot that lives off the world's loot (GROUND.md §6.8, G7) through the same client API as a player: it walks to the
/// barrels and crates it can see, breaks barrels with its machete and picks up the salvage, components and blueprints
/// that spill, empties crates, takes its components to the nearest recycler and turns it on, places a research table
/// once and researches its metal hatchet into a blueprint, and learns every blueprint it finds. One bot in four leads a
/// team and invites the bots near it, the others accept; each bot puts a marker on its map. Deterministic per bot; it
/// roams up to <see cref="Roam"/> metres from the zone's origin.
/// </summary>
public sealed class WorldBrain
{
    public const float Roam = 450f;

    private enum Goal
    {
        None,
        Barrel,
        Crate,
        Pick,
        Recycle,
        Research,
        Wander,
    }

    private readonly int _index;
    private readonly HashSet<EntityId> _skip = new();
    private readonly HashSet<EntityId> _invited = new();
    private Rng _rng;
    private Goal _goal;
    private EntityId _target;
    private int _phase;
    private bool _hit;
    private double _until;
    private double _nextStep;
    private double _wait;
    private double _nextPlan;
    private double _nextChore;
    private float _yaw;
    private float _pitch;
    private float _heading;
    private bool _researched;
    private bool _marked;
    private bool _teamed;
    private bool _said;
    private Vector3 _lastFeet;
    private double _lastMoved;
    private readonly Dictionary<uint, string> _sent = new();
    private bool _listening;
    private static readonly bool Trace = Environment.GetEnvironmentVariable("HOLLOW_BOT_REFUSALS") != null;

    public WorldBrain(int index)
    {
        _index = index;
        _rng = new Rng(Hash64.Of((ulong)index, 0x60A7));
        _heading = (float)_rng.Range(-Math.PI, Math.PI);
        _yaw = _heading;
        _nextPlan = _rng.Range(0, 1);
        _nextChore = _rng.Range(0, 2);
    }

    /// <summary>What the bot did, for the load test's report.</summary>
    public int Broken { get; private set; }

    public int Emptied { get; private set; }

    public int Picked { get; private set; }

    public int Recycled { get; private set; }

    public int Researched { get; private set; }

    public int Learnt { get; private set; }

    public InputFrame Think(double time, double delta, GameClient client)
    {
        LocalPlayer me = client.Local;
        var input = new InputFrame { Yaw = _yaw, Pitch = _pitch };
        if (Trace && !_listening)
        {
            _listening = true;
            me.IntentRejected += a => Console.WriteLine($"{client.Name}: {_sent.GetValueOrDefault(a.Seq, "?")} refused: {a.Code}");
        }
        if (me.IsDead)
        {
            if (time >= _nextChore)
            {
                _nextChore = time + 2;
                _goal = Goal.None;
                Note(me.Respawn(), "Respawn");
            }
            return input;
        }
        if (me.IsWounded)
        {
            return input;
        }
        if (time >= _nextChore && me.PendingCount == 0 && _goal is not (Goal.Recycle or Goal.Research or Goal.Crate or Goal.Pick))
        {
            _nextChore = time + _rng.Range(1, 2);
            Chores(client, me);
        }
        if (_goal != Goal.None && time > _until)
        {
            GiveUp(me);
        }
        if (_goal is Goal.None or Goal.Wander && time >= _nextPlan)
        {
            _nextPlan = time + _rng.Range(0.6, 1.2);
            Plan(time, client, me);
        }
        Vector3 feet = client.Character.Position;
        Vector3 up = client.Frame.UpAt(feet);
        Vector3 eye = feet + up * client.EyeHeight;
        GameContent c = client.Content;
        switch (_goal)
        {
            case Goal.Barrel:
                if (client.ThingOf(_target) is not { } barrel)
                {
                    Broken += _hit ? 1 : 0;
                    Done();
                    break;
                }
                if (Steer(ref input, time, feet, barrel.Position, 1.0f, delta))
                {
                    Look(ref input, eye, barrel.Position + up * 0.45f, up);
                    // Hold the swing while someone else stands at the barrel (friendly fire is on).
                    bool crowded = client.Entities.Any(e => (e.Flags & MoveFlags.Dead) == 0 && new Vector2(e.Position.X - barrel.Position.X, e.Position.Z - barrel.Position.Z).Length() < 1.3f);
                    input.Primary = !crowded;
                    _hit |= !crowded;
                }
                break;
            case Goal.Crate:
                Crate(ref input, time, delta, client, me, feet, eye, up);
                break;
            case Goal.Pick:
                PickFromBag(ref input, time, delta, client, me, feet, eye, up);
                break;
            case Goal.Recycle:
                Recycle(ref input, time, delta, client, me, feet, eye, up);
                break;
            case Goal.Research:
                Research(ref input, time, delta, client, me, feet, eye, up);
                break;
            case Goal.Wander:
                Wander(ref input, feet, delta);
                break;
        }
        _yaw = input.Yaw;
        _pitch = input.Pitch;
        return input;
    }

    // Choose what to do next: pick up what lies near, recycle a pocketful of components, research once, else the
    // nearest barrels and crates (one of the closest few, so the crowd spreads out), else walk on.
    private void Plan(double time, GameClient client, LocalPlayer me)
    {
        GameContent c = client.Content;
        Vector3 feet = client.Character.Position;
        // A bag shows what it holds only once opened: every bag near is worth one look.
        RemoteThing? near = client.Things
            .Where(t => t.Kind == StaticKind.DroppedBag && !_skip.Contains(t.Id) && Vector3.Distance(t.Position, feet) < 12)
            .OrderBy(t => Vector3.Distance(t.Position, feet)).FirstOrDefault();
        if (near != null)
        {
            Begin(Goal.Pick, near.Id, time, 10);
            return;
        }
        // Any of the recyclers in range, so a crowd spreads over them.
        List<RemoteThing> recyclers = client.Props.Where(p => p.Prop(c)!.Kind == WorldPropKind.Recycler && Vector3.Distance(p.Position, feet) < 400).ToList();
        if (Components(c, me) >= 3 && recyclers.Count > 0 && _rng.Chance(0.35))
        {
            Begin(Goal.Recycle, recyclers[_rng.NextInt(recyclers.Count)].Id, time, 60);
            return;
        }
        if (!_researched && Researchable(c, me) != null && _rng.Chance(0.25))
        {
            Begin(Goal.Research, default, time, 45);
            return;
        }
        List<RemoteThing> loot = client.Props
            .Where(p => p.Prop(c)!.Kind is WorldPropKind.Barrel or WorldPropKind.Crate && !_skip.Contains(p.Id) &&
                        new Vector2(p.Position.X, p.Position.Z).Length() < Roam)
            .OrderBy(p => Vector3.Distance(p.Position, feet)).Take(5).ToList();
        if (loot.Count > 0)
        {
            RemoteThing pick = loot[_rng.NextInt(loot.Count)];
            bool barrel = pick.Prop(c)!.Kind == WorldPropKind.Barrel;
            Begin(barrel ? Goal.Barrel : Goal.Crate, pick.Id, time, barrel ? 30 : 25);
            if (barrel)
            {
                Hold(me, c, "machete", "metal-hatchet", "rock");
            }
            return;
        }
        Begin(Goal.Wander, default, time, 3);
    }

    private void Begin(Goal goal, EntityId target, double time, double seconds)
    {
        _goal = goal;
        _target = target;
        _phase = 0;
        _hit = false;
        _until = time + seconds;
        _lastMoved = time;
    }

    private void Done()
    {
        _goal = Goal.None;
        _nextPlan = 0;
    }

    // Out of time (stuck, crowded out, refused): leave it alone from now on.
    private void GiveUp(LocalPlayer me)
    {
        if (_goal is Goal.Barrel or Goal.Crate or Goal.Pick)
        {
            _skip.Add(_target);
        }
        if (_goal is Goal.Crate or Goal.Pick or Goal.Recycle or Goal.Research && me.Loot != null)
        {
            Note(me.Close(), "Close");
        }
        if (_goal == Goal.Research)
        {
            _researched = true;
        }
        Done();
    }

    // At a bag of dropped items (what it holds shows only once it is open): opens it, takes what is wanted a stack at a
    // time, then closes it and leaves the rest (perhaps the bot's own junk) alone. A bag emptied goes by itself.
    private void PickFromBag(ref InputFrame input, double time, double delta, GameClient client, LocalPlayer me, Vector3 feet, Vector3 eye, Vector3 up)
    {
        if (client.ThingOf(_target) is not { Kind: StaticKind.DroppedBag } bag)
        {
            Picked += _hit ? 1 : 0;
            if (me.Loot != null)
            {
                Note(me.Close(), "Close");
            }
            Done();
            return;
        }
        if (!Steer(ref input, time, feet, bag.Position, 0.9f, delta))
        {
            return;
        }
        Look(ref input, eye, bag.Position, up);
        if (time < _nextStep || me.PendingCount > 0)
        {
            return;
        }
        _nextStep = time + 0.25;
        if (_phase == 0)
        {
            // Looked into rather than taken whole (a tap takes a bag of one stack): only what is wanted is taken.
            Note(me.Interact(Interacting.Thing(_target), "loot"), "Loot");
            _phase = 1;
            return;
        }
        if (me.LootTarget != _target || me.Loot is not { } contents)
        {
            return;
        }
        GameContent c = client.Content;
        int slot = Enumerable.Range(0, contents.Size).FirstOrDefault(i => !contents[i].IsEmpty && Wanted(c, contents[i].Item), -1);
        if (slot >= 0)
        {
            Note(me.QuickMove(ContainerRef.Loot, slot), "QuickMove");
            _hit = true;
            return;
        }
        Note(me.Close(), "Close");
        _skip.Add(_target);
        Picked += _hit ? 1 : 0;
        Done();
    }

    // Opens the crate, takes everything a slot at a time; the crate goes once emptied.
    private void Crate(ref InputFrame input, double time, double delta, GameClient client, LocalPlayer me, Vector3 feet, Vector3 eye, Vector3 up)
    {
        if (client.ThingOf(_target) is not { } crate)
        {
            Emptied += _phase > 1 ? 1 : 0;
            if (me.Loot != null)
            {
                Note(me.Close(), "Close");
            }
            Done();
            return;
        }
        if (!Steer(ref input, time, feet, crate.Position, 1.2f, delta))
        {
            return;
        }
        Look(ref input, eye, crate.Position + up * 0.3f, up);
        if (time < _nextStep || me.PendingCount > 0)
        {
            return;
        }
        _nextStep = time + 0.25;
        if (_phase == 0)
        {
            Note(Interacting.Tap(client, Interacting.Thing(_target)), "Open");
            _phase = 1;
            return;
        }
        if (me.LootTarget != _target || me.Loot is not { } contents)
        {
            return;
        }
        int slot = FirstFilled(contents, 0, contents.Size);
        if (slot >= 0)
        {
            Note(me.QuickMove(ContainerRef.Loot, slot), "QuickMove");
            _phase = 2;
        }
        else
        {
            Note(me.Close(), "Close");
            Emptied++;
            Done();
        }
    }

    // At the recycler: open it, put the components in, turn it on if it isn't, wait for the batch, take the outputs.
    private void Recycle(ref InputFrame input, double time, double delta, GameClient client, LocalPlayer me, Vector3 feet, Vector3 eye, Vector3 up)
    {
        GameContent c = client.Content;
        if (client.ThingOf(_target) is not { } recycler)
        {
            Done();
            return;
        }
        if (!Steer(ref input, time, feet, recycler.Position, 1.3f, delta))
        {
            return;
        }
        Look(ref input, eye, recycler.Position + up * 0.8f, up);
        if (time < _nextStep || me.PendingCount > 0)
        {
            return;
        }
        _nextStep = time + 0.25;
        IReadOnlyList<int> inputs = c.World.Recycler.InputSlots, outputs = c.World.Recycler.OutputSlots;
        switch (_phase)
        {
            case 0:
                Note(Interacting.Tap(client, Interacting.Thing(_target)), "Open");
                _phase = 1;
                return;
            case 1 when me.LootTarget != _target || me.Loot == null:
                return;
            case 1:
            {
                Container machine = me.Loot!;
                int free = inputs.FirstOrDefault(i => machine[i].IsEmpty, -1);
                (ContainerRef Which, int Slot)? component = Find(me, s => c.Item(s.Item) is { } item && c.World.FixedYield(item.Id) != null);
                if (free >= 0 && component is { } from)
                {
                    Note(me.MoveItem(from.Which, from.Slot, ContainerRef.Loot, free), "MoveItem");
                    return;
                }
                _phase = 2;
                return;
            }
            case 2:
                if (me.Machine is { Running: false } && inputs.Any(i => !me.Loot![i].IsEmpty))
                {
                    Note(me.Interact(Interacting.Thing(_target), "turn-on"), "Turn on");
                    Recycled++;
                }
                _phase = 3;
                _wait = time + 12;
                return;
            case 3:
                // Done once it stops (it stops itself when its inputs are gone), or after a while with others' work in it.
                if (me.Machine is { Running: true } && time < _wait)
                {
                    return;
                }
                _phase = 4;
                return;
            default:
            {
                int slot = outputs.FirstOrDefault(i => !me.Loot![i].IsEmpty, -1);
                if (slot >= 0 && me.Inventory.Main.Slots.ToArray().Any(s => s.IsEmpty))
                {
                    Note(me.QuickMove(ContainerRef.Loot, slot), "QuickMove");
                    return;
                }
                Note(me.Close(), "Close");
                Done();
                return;
            }
        }
    }

    // Once: a research table in front of the bot, its metal hatchet and the salvage in, BEGIN, the blueprint out.
    private void Research(ref InputFrame input, double time, double delta, GameClient client, LocalPlayer me, Vector3 feet, Vector3 eye, Vector3 up)
    {
        GameContent c = client.Content;
        ushort tableItem = c.ItemByKey(c.World.Research.TableKey).Id;
        ResearchStats stats = c.World.Research;
        if (_phase < 2)
        {
            RemoteThing? mine = client.Things.Where(t => t.Kind == StaticKind.Deployable && t.Look == tableItem && t.Mine)
                .OrderBy(t => Vector3.Distance(t.Position, feet)).FirstOrDefault();
            if (mine != null && Vector3.Distance(mine.Position, feet) < 30)
            {
                _target = mine.Id;
                _phase = 2;
                return;
            }
            if (_phase == 1 && time > _wait)
            {
                // Not there: the spot was blocked (a bot, a barrel). Turn and try another.
                _phase = 0;
                input.Yaw = _yaw + (float)_rng.Range(1, 2.5);
            }
            if (_phase == 0 && time >= _nextStep && me.PendingCount == 0 && Slot(me, c, c.World.Research.TableKey) is { } slot)
            {
                (Vector3 forward, _) = CharacterMotor.HorizontalBasis(_yaw, up);
                if (client.AimGround(feet + up * 2 + forward * 2.2f, -up, 4) is { } at)
                {
                    Note(me.Place(slot.Which, slot.Slot, at, _yaw), "Place");
                    _phase = 1;
                    _wait = time + 3;
                }
                _nextStep = time + 1;
            }
            return;
        }
        if (client.ThingOf(_target) is not { } table)
        {
            GiveUp(me);
            return;
        }
        if (!Steer(ref input, time, feet, table.Position, 1.2f, delta))
        {
            return;
        }
        Look(ref input, eye, table.Position + up * 0.8f, up);
        if (time < _nextStep || me.PendingCount > 0)
        {
            return;
        }
        _nextStep = time + 0.3;
        switch (_phase)
        {
            case 2:
                Note(Interacting.Tap(client, Interacting.Thing(_target)), "Open");
                _phase = 3;
                return;
            case 3 when me.LootTarget != _target || me.Loot == null:
                return;
            case 3:
                if (Researchable(c, me) is not { } thing)
                {
                    GiveUp(me);
                    return;
                }
                Note(me.MoveItem(thing.Which, thing.Slot, ContainerRef.Loot, stats.ItemSlot, 1), "MoveItem");
                _phase = 4;
                return;
            case 4:
            {
                if (c.Recipe(me.Loot![stats.ItemSlot].Item) is not { } recipe || Slot(me, c, c.World.Currency!.Key) is not { } salvage)
                {
                    GiveUp(me);
                    return;
                }
                Note(me.MoveItem(salvage.Which, salvage.Slot, ContainerRef.Loot, stats.SalvageSlot, Hollow.Simulation.Economy.Research.Cost(c, recipe)), "MoveItem");
                _phase = 5;
                return;
            }
            case 5:
                Note(me.Interact(Interacting.Thing(_target), "turn-on"), "Turn on");
                Researched++;
                _phase = 6;
                return;
            case 6:
                if (me.Machine is { Running: true } || me.Loot![stats.OutputSlot].IsEmpty)
                {
                    return;
                }
                Note(me.QuickMove(ContainerRef.Loot, stats.OutputSlot), "QuickMove");
                _phase = 7;
                return;
            default:
                Note(me.Close(), "Close");
                _researched = true;
                Done();
                return;
        }
    }

    // Learn blueprints, keep the machete on the belt, make room, the team (invites within 5 m, as the server allows) and
    // the map marker.
    private void Chores(GameClient client, LocalPlayer me)
    {
        GameContent c = client.Content;
        ushort blueprint = c.World.Blueprint?.Id ?? 0;
        if (Find(me, s => s.Item == blueprint && c.Recipe(s.Condition) is { } r && !me.Knows(r)) is { } paper)
        {
            Note(me.Learn(paper.Which, paper.Slot), "Learn");
            Learnt++;
            return;
        }
        if (Slot(me, c, "machete") is { Which: ContainerRef.Main } blade)
        {
            int free = Enumerable.Range(0, PlayerInventory.BeltSlots).FirstOrDefault(b => me.Inventory.Belt[b].IsEmpty, 0);
            Note(me.MoveItem(ContainerRef.Main, blade.Slot, ContainerRef.Belt, free), "MoveItem");
            return;
        }
        Container main = me.Inventory.Main;
        if (Enumerable.Range(0, main.Size).Count(i => main[i].IsEmpty) < 3)
        {
            // Make room: food, ammunition and the like go; salvage, components, blueprints and the kit stay.
            int junk = Enumerable.Range(0, main.Size).FirstOrDefault(i => !main[i].IsEmpty && !Wanted(c, main[i].Item) && !Kit(c, main[i].Item), -1);
            if (junk >= 0)
            {
                Note(me.Drop(ContainerRef.Main, junk), "Drop");
                return;
            }
        }
        bool leader = _index % 4 == 0;
        if (!leader && me.TeamInvites.Count > 0 && me.Team == null && !_teamed)
        {
            _teamed = true;
            Note(me.AcceptInvite(me.TeamInvites[0].Team), "AcceptInvite");
            return;
        }
        if (leader && !_teamed && me.Team == null)
        {
            _teamed = true;
            Note(me.CreateTeam(), "CreateTeam");
            return;
        }
        if (me.LeadsTeam && me.Team!.Members.Count < 4 && _invited.Count < 8)
        {
            Vector3 feet = client.Character.Position;
            RemoteEntity? other = client.Entities.Where(e => !_invited.Contains(e.Id) && Vector3.Distance(e.Position, feet) < 5)
                .OrderBy(e => Vector3.Distance(e.Position, feet)).FirstOrDefault();
            if (other != null)
            {
                _invited.Add(other.Id);
                Note(me.Interact(Interacting.Character(other.Id), "invite"), "Invite");
                return;
            }
        }
        if (me.LeadsTeam && me.Team!.Members.Count > 1 && !_said)
        {
            _said = true;
            client.SendTeamChat("Barrels by the coast, recycler at the landing site.");
            return;
        }
        if (!_marked)
        {
            _marked = true;
            float angle = (float)_rng.Range(-Math.PI, Math.PI), distance = (float)_rng.Range(50, 300);
            Note(me.PlaceMarker(new Vector3(MathF.Sin(angle) * distance, 0, MathF.Cos(angle) * distance), (byte)(_index % 6), "loot"), "PlaceMarker");
        }
    }

    // Remembers what an intent was, so a refusal can be told (HOLLOW_BOT_REFUSALS).
    private void Note(uint seq, string what)
    {
        if (Trace)
        {
            _sent[seq] = what;
        }
    }

    // What the bot picks up and keeps: salvage, components, blueprints.
    private static bool Wanted(GameContent c, ushort item) =>
        item == c.World.Currency?.Id || item == c.World.Blueprint?.Id || c.World.FixedYield(item) != null;

    private static bool Kit(GameContent c, ushort item) =>
        c.Item(item)?.Key is "machete" or "metal-hatchet" or "research-table";

    private static int Components(GameContent c, LocalPlayer me)
    {
        int units = 0;
        foreach (Container container in new[] { me.Inventory.Main, me.Inventory.Belt })
        {
            for (int i = 0; i < container.Size; i++)
            {
                if (!container[i].IsEmpty && c.World.FixedYield(container[i].Item) != null)
                {
                    units += container[i].Count;
                }
            }
        }
        return units;
    }

    // Something the bot can research: its metal hatchet (or any item the rules allow) while it has the salvage.
    private static (ContainerRef Which, int Slot)? Researchable(GameContent c, LocalPlayer me)
    {
        if (c.World.Currency is not { } salvage || me.Inventory.Count(salvage) < c.World.Research.CostFor(1))
        {
            return null;
        }
        return Find(me, s => c.Item(s.Item) is { } item && item.Key != "machete" && Hollow.Simulation.Economy.Research.CanResearch(c, item) &&
                             c.Recipe(item.Id) is { } r && !me.Knows(r));
    }

    private static int FirstFilled(Container container, int from, int to)
    {
        for (int i = from; i < to; i++)
        {
            if (!container[i].IsEmpty)
            {
                return i;
            }
        }
        return -1;
    }

    private static (ContainerRef Which, int Slot)? Find(LocalPlayer me, Func<ItemStack, bool> match)
    {
        foreach (ContainerRef which in new[] { ContainerRef.Main, ContainerRef.Belt })
        {
            Container container = me.Resolve(which)!;
            for (int i = 0; i < container.Size; i++)
            {
                if (!container[i].IsEmpty && match(container[i]))
                {
                    return (which, i);
                }
            }
        }
        return null;
    }

    private static (ContainerRef Which, int Slot)? Slot(LocalPlayer me, GameContent c, string key)
    {
        ushort id = c.ItemByKey(key).Id;
        return Find(me, s => s.Item == id);
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

    // Walks toward a point until within reach of it; true once there. Stuck for a second and a half (a tree, a rock),
    // it jumps and sidesteps.
    private bool Steer(ref InputFrame input, double time, Vector3 feet, Vector3 to, float reach, double delta)
    {
        Vector3 d = to - feet;
        var flat = new Vector2(d.X, d.Z);
        if (flat.Length() <= reach)
        {
            _lastMoved = time;
            return true;
        }
        float wanted = MathF.Atan2(-flat.X, -flat.Y);
        input.Yaw = _yaw + Math.Clamp(Wrap(wanted - _yaw), -(float)delta * 5, (float)delta * 5);
        input.Pitch = 0;
        input.Forward = 1;
        input.Sprint = flat.Length() > 10;
        if (Vector3.Distance(feet, _lastFeet) > 0.4f)
        {
            _lastFeet = feet;
            _lastMoved = time;
        }
        else if (time - _lastMoved > 1.5)
        {
            input.JumpPressed = input.Jump = true;
            input.Right = (_index & 1) == 0 ? 1 : -1;
            if (time - _lastMoved > 2.5)
            {
                _lastMoved = time;
            }
        }
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

    // Nothing in sight: walk on along the bot's own heading, turning back toward the origin beyond the roaming range.
    private void Wander(ref InputFrame input, Vector3 feet, double delta)
    {
        var flat = new Vector2(feet.X, feet.Z);
        if (flat.Length() > Roam * 0.9f)
        {
            _heading = MathF.Atan2(flat.X, flat.Y);
        }
        input.Yaw = _yaw + Math.Clamp(Wrap(_heading - _yaw), -(float)delta * 2, (float)delta * 2);
        input.Pitch = 0;
        input.Forward = 1;
        input.Sprint = true;
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
