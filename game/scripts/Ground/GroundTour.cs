using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Hollow.Client;
using Hollow.Core;
using Hollow.Server;
using Hollow.Simulation;
using Hollow.Simulation.Targets;
using Hollow.Simulation.Building;
using Hollow.Simulation.Content;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Nature;
using Hollow.Simulation.Raid;
using Hollow.Simulation.Ships;
using HollowUniverse.Fx;
using HollowUniverse.UI;
using HollowUniverse.UI.Model;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

/// <summary>
/// The scripted screenshot run of the ground game (GROUND.md §5, <c>--tour &lt;folder&gt;</c>): on the host, a walk
/// through the game at the landing site that saves one picture per moment (the HUD by berry bushes, picking berries,
/// the inventory, crafting, the other player, chopping a tree and after, an outcrop, a rifle fired down the sights, a
/// base being built with the plan's ghost, a finished hut with a code lock and its keypad, a timed charge on its wall
/// and the blast, a firefight at the hut, the Tin Finch crafted, boarded, flown and landed, G7's team, props, recycler,
/// tech tree, research table and map, a bag of dropped items and its loot, the death screen, night by a fire, P3's held
/// items: a gun's slot and rounds, the AK's, bag charge's and a blueprint's panels, crafting at a workbench, and things
/// placed with their one ghost) and quits;
/// with <c>--tour guest</c>, a second
/// client that dresses in what it is given, takes up its hatchet and faces the host, fires a rifle it is given at the
/// nearest building wall, and takes a team invite. Pictures are 1600 × 900 JPEG at the window's size.
/// </summary>
public partial class GroundTour : Node
{
    private readonly Queue<Func<Task>> _steps = new();
    private bool _running;
    private string _guestName = "Bo";

    public GroundView View { get; set; } = null!;

    /// <summary>Where the pictures go (relative to game/, where Godot runs), or "guest" for the second client's part.</summary>
    public string Folder { get; set; } = "";

    /// <summary>Admin commands to the game hosted here (in this process or its own), or null when joined.</summary>
    public Func<string, bool>? Command { get; set; }

    private GameClient Client => View.Client;

    private LocalPlayer Me => Client.Local;

    public override void _Ready()
    {
        if (Folder == "guest")
        {
            return;
        }
        Directory.CreateDirectory(Folder);
        var all = new (string Name, Func<Task> Step)[]
        {
            ("hud", Hud), ("berries", Berries), ("inventory", Inventory), ("crafting", Crafting), ("player", OtherPlayer),
            ("chop", Chop), ("mine", Mine), ("gun", Gun), ("build", Build), ("base", Base), ("charge", Charge), ("firefight", Firefight), ("ship", Ship),
            ("world", World), ("bags", Bags), ("death", Death), ("night", Night), ("f1", F1), ("f2", F2), ("look", Look),
            ("foundations", Foundations), ("held", Held), ("place", Place), ("interact", Interaction), ("map", Map),
        };
        _steps.Enqueue(Prepare);
        foreach ((string name, Func<Task> step) in all)
        {
            if (Only == null || Only.Contains(name))
            {
                _steps.Enqueue(step);
            }
        }
    }

    /// <summary>Only these steps (names, comma-separated on the command line: <c>--tour-steps hud,player</c>); null: all.</summary>
    public HashSet<string>? Only { get; set; }

    /// <summary>The kind of tree the chop step fells (<c>--tour-tree pine-tree</c>); null: a birch or a broadleaf if near.</summary>
    public string? Tree { get; set; }

    public override void _Process(double delta)
    {
        if (Folder == "guest")
        {
            Guest();
            return;
        }
        if (_running || !View.ReadyForShot)
        {
            return;
        }
        _running = true;
        Run();
    }

    private async void Run()
    {
        try
        {
            while (_steps.Count > 0)
            {
                await _steps.Dequeue()();
            }
            GD.Print("[tour] done");
        }
        catch (Exception e)
        {
            GD.PushError($"[tour] failed: {e}");
        }
        GetTree().Quit();
    }

    // ---------------------------------------------------------------- the steps

    private async Task Prepare()
    {
        Admin("give", "stone-hatchet 1");
        Admin("give", "stone-pickaxe 1");
        Admin("give", "bandage 3");
        Admin("give", "wood 600");
        Admin("give", "stone 250");
        Admin("give", "fibre 40");
        Admin("give", "woven-shirt 1");
        Admin("give", "woven-trousers 1");
        Admin("give", "campfire 1");
        await Until(() => Count("campfire") > 0, 5);
        // The tools on the belt, the clothes on.
        MoveToBelt("stone-hatchet", 2);
        await Seconds(0.3);
        MoveToBelt("stone-pickaxe", 3);
        await Seconds(0.3);
        MoveToBelt("bandage", 4);
        await Seconds(0.3);
        foreach (string wear in new[] { "woven-shirt", "woven-trousers" })
        {
            if (SlotOf(Me.Inventory.Main, wear) is int slot)
            {
                Me.QuickMove(ContainerRef.Main, slot);
                await Seconds(0.3);
            }
        }
        Me.HeldSlot = 2;
        await Seconds(0.5);
    }

    // The meadow by the landing site, a berry bush ahead.
    private async Task Hud()
    {
        NaturalObject? bush = Nearest("red-berry-bush", Landing, 120, open: true) ?? Nearest("blue-berry-bush", Landing, 120, open: true);
        if (bush is not { } b)
        {
            GD.PushWarning("[tour] no berry bush near the landing site");
            await Shot("hud");
            return;
        }
        _bush = b;
        Num.Vector3 stand = Away(b.Position, 4.5f);
        await TeleportTo(stand);
        Face(b.Position + b.Up * 0.4f);
        await Settle();
        await Shot("hud");
    }

    private NaturalObject? _bush;

    private async Task Berries()
    {
        if (_bush is not { } b)
        {
            return;
        }
        await TeleportTo(Away(b.Position, 1.7f));
        Face(b.Position + b.Up * 0.5f);
        await Seconds(1.0);
        if (View.Aimed is { Target.Family: TargetFamily.Natural })
        {
            TapAimed();
        }
        await Until(() => Client.Survival().Natural?.IsDepleted(b.Id) == true, 3);
        await Seconds(0.4);
        await Shot("berries");
    }

    private async Task Inventory()
    {
        View.Interface.Open(GroundScreen.Inventory);
        View.Interface.Select(new SlotRef(ContainerId.Belt, 2));
        await Seconds(0.8);
        await Shot("inventory");
    }

    private async Task Crafting()
    {
        // Something in the queue, and a recipe the player has the makings of.
        if (Client.Content.Item("wooden-spear") is { } spear)
        {
            Me.Craft(spear.Id, 2);
        }
        View.Interface.Open(GroundScreen.Crafting);
        View.Interface.SelectRecipe("stone-hatchet");
        await Seconds(1.2);
        await Shot("crafting");
        View.Interface.Open(GroundScreen.Hud);
        await Seconds(0.3);
    }

    // The second player (run with --tour guest) in front of the host, dressed and holding a hatchet.
    private async Task OtherPlayer()
    {
        await Until(() => Client.Entities.Any(e => e.Name.Length > 0), 60);
        RemoteEntity? guest = Client.Entities.FirstOrDefault();
        if (guest == null)
        {
            GD.PushWarning("[tour] no second player joined: no picture of one");
            return;
        }
        _guestName = guest.Name;
        foreach (string item in new[] { "woven-hood 1", "woven-shirt 1", "woven-trousers 1", "work-boots 1", "chest-plate 1", "stone-hatchet 1" })
        {
            Command?.Invoke($"give {_guestName} {item}");
        }
        // An open spot of the meadow; the guest three metres ahead, the host's hands empty.
        (Num.Vector3 spot, Num.Vector3 towards) = OpenSpot(Client.Character.Position);
        await TeleportTo(spot);
        Me.HeldSlot = -1;
        Num.Vector3 ahead = spot + towards * 3.0f;
        Command?.Invoke(string.Create(CultureInfo.InvariantCulture, $"teleport {_guestName} {ahead.X:0.00} {ahead.Z:0.00}"));
        // Until the guest has dressed (their look arrives with what they wear).
        await Until(() => Client.Entities.FirstOrDefault(e => e.Name == _guestName)?.Wear.Count(w => w != 0) >= 4, 15);
        await Seconds(2.0);
        RemoteEntity? there = Client.Entities.FirstOrDefault(e => e.Name == _guestName);
        if (there != null)
        {
            Face(there.Position + Client.Frame.UpAt(there.Position) * 1.05f);
            Num.Vector3 toMe = Client.Character.Position - there.Position;
            GD.Print($"[tour] {_guestName} yaw {there.Yaw:0.00}, towards the host {MathF.Atan2(-toMe.X, -toMe.Z):0.00}, view faces {View.People.ViewOf(_guestName)?.GlobalBasis.Z}");
        }
        await Settle();
        await Seconds(1.0);
        await Shot("player");
        Me.HeldSlot = 2;
    }

    // A tree chopped with the stone hatchet until it falls (GROUND.md §6.1): the X after the first hit, a hit on it
    // bursting, the tree tipping over and landing, and the stump's place empty.
    private async Task Chop()
    {
        Num.Vector3 from = Client.Character.Position;
        NaturalObject? tree = null;
        // A birch or a broadleaf tree in the open if there is one near (lighter to see), else any tree.
        foreach (string key in Tree != null ? new[] { Tree } : new[] { "birch-tree", "broadleaf-tree", "pine-tree", "fir-tree" })
        {
            if (Nearest(key, from, 80, open: true) is { } t)
            {
                tree = t;
                break;
            }
        }
        if (tree is not { } trunk)
        {
            GD.PushWarning("[tour] no tree to chop");
            return;
        }
        NaturalKind kind = Client.Content.Natural(trunk.Kind)!;
        float radius = kind.Radius * trunk.Scale;
        await TeleportTo(Away(trunk.Position, radius + 1.45f));
        Face(trunk.Position + trunk.Up * 1.3f);
        Me.HeldSlot = 2;
        await Settle();
        // The first hit: the X appears, facing the player near the hit's height.
        int wood = Count("wood");
        View.Scripted = new InputFrame { Primary = true };
        await Until(() => Hits(trunk.Id).Seen >= 1, 5);
        View.Scripted = null;
        await Seconds(1.0);
        if (Client.Survival().Natural?.WeakSpot(trunk, kind) is { } x)
        {
            Face(x);
        }
        await Seconds(0.4);
        await Shot("chop-x");
        // On the X until it falls: each hit on it pays more (the notice shows the wood); the second one's burst pictured.
        var log = new List<string>();
        int seen = Hits(trunk.Id).Seen;
        bool burst = false;
        double started = Time.GetTicksMsec() / 1000.0;
        View.Scripted = new InputFrame { Primary = true };
        while (Time.GetTicksMsec() / 1000.0 - started < 60 && Client.Survival().Natural?.IsDepleted(trunk.Id) != true)
        {
            if (Client.Survival().Natural?.WeakSpot(trunk, kind) is { } spot)
            {
                Face(spot);
            }
            (int now, int streak, _) = Hits(trunk.Id);
            if (now != seen)
            {
                seen = now;
                log.Add($"{Count("wood") - wood}{(streak > 0 ? $"(X{streak})" : "")}");
                wood = Count("wood");
                if (!burst && streak >= 2)
                {
                    burst = true;
                    await Shot("chop-hit", frames: 2);
                }
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        View.Scripted = null;
        GD.Print($"[tour] chop: wood a hit {string.Join(" ", log)}; felled {Count("wood") - wood}");
        // It tips away from the blow and lands along the ground.
        Num.Vector3 away = trunk.Position - Client.Character.Position;
        away = Num.Vector3.Normalize(away - trunk.Up * Num.Vector3.Dot(away, trunk.Up));
        Face(trunk.Position + trunk.Up * 3.0f + away * 4.0f);
        await Seconds(2.0);
        await Shot("chop-falling");
        Face(trunk.Position + away * 5.0f + trunk.Up * 0.2f);
        await Seconds(0.75);
        await Shot("chop-landed");
        await Seconds(5.0);
        Face(trunk.Position + trunk.Up * 1.5f);
        await Seconds(0.5);
        await Shot("chopped");
    }

    // An outcrop broken with the stone pickaxe down to its last stage (GROUND.md §6.1): the hotspot after the first hit,
    // a stage breaking, and the hotspot on the broken rock.
    private async Task Mine()
    {
        Num.Vector3 from = Client.Character.Position;
        NaturalObject? found = null;
        foreach (string key in new[] { "metal-node", "sulphur-node", "stone-node" })
        {
            if (Nearest(key, from, 150, open: false) is { } o)
            {
                found = o;
                break;
            }
        }
        if (found is not { } rock)
        {
            GD.PushWarning("[tour] no outcrop near");
            return;
        }
        NaturalKind kind = Client.Content.Natural(rock.Kind)!;
        await TeleportTo(Away(rock.Position, kind.Radius * rock.Scale + 0.75f));
        Face(rock.Position + rock.Up * 0.6f);
        await Settle();
        await Shot("outcrop");
        Me.HeldSlot = 3;
        await Seconds(0.5);
        View.Scripted = new InputFrame { Primary = true };
        await Until(() => Hits(rock.Id).Seen >= 1, 5);
        View.Scripted = null;
        await Seconds(0.9);
        if (Client.Survival().Natural?.WeakSpot(rock, kind) is { } hot)
        {
            Face(hot);
        }
        await Seconds(0.3);
        await Shot("mine-hotspot");
        var log = new List<string>();
        int seen = Hits(rock.Id).Seen, stage = 0;
        bool broke = false;
        double started = Time.GetTicksMsec() / 1000.0;
        View.Scripted = new InputFrame { Primary = true };
        while (Time.GetTicksMsec() / 1000.0 - started < 40 && Hits(rock.Id).Stage < 2 && !Client.Survival().Natural!.IsDepleted(rock.Id))
        {
            if (Client.Survival().Natural?.WeakSpot(rock, kind) is { } spot)
            {
                Face(spot);
            }
            (int now, int streak, int reached) = Hits(rock.Id);
            if (now != seen)
            {
                seen = now;
                log.Add(streak > 0 ? $"X{streak}" : "-");
            }
            if (reached > stage)
            {
                stage = reached;
                if (!broke)
                {
                    broke = true;
                    View.Scripted = null;
                    Face(rock.Position + rock.Up * 0.5f);
                    await Shot("mine-stage", frames: 4);
                    View.Scripted = new InputFrame { Primary = true };
                }
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        View.Scripted = null;
        GD.Print($"[tour] mine: {log.Count} hits to stage 2 ({string.Join(" ", log)}); the nature view: {NatureView.CostReport()}");
        await Seconds(1.0);
        Face(Client.Survival().Natural!.WeakSpot(rock, kind) ?? rock.Position + rock.Up * 0.4f);
        await Seconds(0.3);
        await Shot("outcrop-broken");
        // Mined out: its remnant crumbles into its gibs (GROUND.md §9.4).
        View.Scripted = new InputFrame { Primary = true };
        double mining = Time.GetTicksMsec() / 1000.0;
        while (Time.GetTicksMsec() / 1000.0 - mining < 40 && !Client.Survival().Natural!.IsDepleted(rock.Id))
        {
            if (Client.Survival().Natural?.WeakSpot(rock, kind) is { } spot)
            {
                Face(spot);
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        View.Scripted = null;
        GD.Print($"[tour] the outcrop {(Client.Survival().Natural!.IsDepleted(rock.Id) ? "is mined out" : "still stands")}");
        if (Client.Survival().Natural?.IsDepleted(rock.Id) == true)
        {
            Face(rock.Position + rock.Up * 0.3f);
            await Seconds(0.3);
            await Shot("outcrop-crumble");
        }
        Me.HeldSlot = 2;
    }

    // How many fresh hits this client has seen on a natural object, the run of weak-spot hits and the stage.
    private (int Seen, int Streak, int Stage) Hits(ulong id)
    {
        if (Client.Survival().Natural is not { } state || !state.TryGetOverride(id, out NaturalOverride o) ||
            !Client.Survival().Nature!.TryGet(id, out NaturalObject obj) || Client.Content.Natural(obj.Kind) is not { } kind)
        {
            return (0, 0, 0);
        }
        return (o.SeenHits, o.Streak, state.Stage(obj, kind));
    }

    // A rifle aimed down the sights at a tree 20–60 m off, the moment a shot leaves (G4).
    private async Task Gun()
    {
        Admin("give", "rifle 1");
        Admin("give", "rifle-ammo 90");
        await Until(() => Count("rifle") > 0 && Count("rifle-ammo") > 0, 5);
        MoveToBelt("rifle", 5);
        await Seconds(0.4);
        Me.HeldSlot = 5;
        await Until(() => Me.Gun.Ready, 3);
        Me.Gun.Reload();
        await Until(() => Me.Gun.Loaded > 0 && !Me.Gun.Reloading, 8);
        Num.Vector3 from = Client.Character.Position;
        foreach (string key in new[] { "fir-tree", "pine-tree", "broadleaf-tree", "birch-tree" })
        {
            if (Nearest(key, from, 60, open: true) is { } t && Num.Vector3.Distance(t.Position, from) > 20)
            {
                Face(t.Position + t.Up * 2.0f);
                break;
            }
        }
        await Settle();
        // The picture is the frame the shot leaves in (its muzzle effect lasts 50 ms, a frame or two).
        Task? capture = null;
        void Fired(ShotEvent _) => capture ??= Shot("gun", frames: 0);
        Me.Gun.ShotFired += Fired;
        View.Scripted = new InputFrame { Secondary = true };
        await Seconds(0.9);
        View.Scripted = new InputFrame { Secondary = true, Primary = true, PrimaryPressed = true };
        await Until(() => capture != null, 3);
        if (capture != null)
        {
            await capture;
        }
        Me.Gun.ShotFired -= Fired;
        View.Scripted = null;
        await Seconds(0.6);
        Me.HeldSlot = 2;
    }

    // ---------------------------------------------------------------- the owner's first playtest: looking and holding

    // The rock held in the right hand; free look (Alt held, the mouse to the right) turning the camera while the arms
    // keep to the view; and a free foundation lifted by a raised aim (GROUND.md §4.5, §4.8, §6.3).
    private async Task Look()
    {
        Admin("give", "building-plan 1");
        Admin("give", "wood 600");
        await Until(() => Count("building-plan") > 0, 5);
        (Num.Vector3 spot, Num.Vector3 towards) = OpenSpot(Offset(-40, 30));
        await TeleportTo(spot);
        View.TurnToward(towards);
        View.Pitch = -0.12f;
        await Settle();
        await Hold("rock", 0);
        await Seconds(0.6);
        await Shot("rock");
        Input.MouseMode = Input.MouseModeEnum.Captured;
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Alt, PhysicalKeycode = Key.Alt, Pressed = true });
        await Seconds(0.2);
        Input.ParseInputEvent(new InputEventMouseMotion { Relative = new Vector2(420, -60) });
        await Seconds(0.3);
        await Shot("freelook");
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Alt, PhysicalKeycode = Key.Alt, Pressed = false });
        await Seconds(0.3);
        await Hold("building-plan", 5);
        View.Building.PlanPiece = Client.Content.Building.Wheel.ToList().FindIndex(p => p.Key == "foundation");
        // The aim 3.5 m out half a metre over the ground: the foundation's top stands that high.
        View.Pitch = -MathF.Atan2(Client.EyeHeight - 0.5f, 3.5f);
        await Seconds(0.8);
        await Shot("foundation-lifted");
    }

    // ---------------------------------------------------------------- building and raiding (G3, G5)

    // A base being started in the open: two foundations and three walls, and the plan's ghost of a doorway in front.
    private async Task Build()
    {
        Admin("give", "building-plan 1");
        Admin("give", "wood 4000");
        Admin("give", "stone 2000");
        await Until(() => Count("building-plan") > 0 && Count("wood") >= 4000, 5);
        (Num.Vector3 spot, Num.Vector3 towards) = OpenSpot(Offset(45, -35));
        await TeleportTo(spot);
        View.TurnToward(towards);
        View.Pitch = -0.3f;
        await Settle();
        await Hold("building-plan", 5);
        Num.Vector3 up = Client.Frame.UpAt(Client.Character.Position);
        Num.Vector3 ahead = Forward(), right = Num.Vector3.Normalize(Num.Vector3.Cross(ahead, up));
        if (await PlaceAimed("foundation", p => p.Support == null) is not { } first)
        {
            return;
        }
        Num.Vector3 c1 = Centre(first);
        Piece? second = await PlaceAimed("foundation", p => p.Support == first && Num.Vector3.Dot(At(p) - c1, right) > 2);
        await PlaceAimed("wall", p => p.Support == first && Num.Vector3.Dot(At(p) - c1, ahead) > 1);
        await PlaceAimed("wall", p => p.Support == first && Num.Vector3.Dot(At(p) - c1, right) < -1);
        if (second != null)
        {
            Num.Vector3 c2 = Centre(second);
            await PlaceAimed("wall", p => p.Support == second && Num.Vector3.Dot(At(p) - c2, ahead) > 1);
            // Back a few steps, the doorway's ghost on the second foundation's near edge.
            await TeleportTo(Client.Character.Position - ahead * 2.5f + right * 1.0f);
            await Settle();
            if (await AimAt("doorway", p => p.Support == second && Num.Vector3.Dot(At(p) - c2, ahead) < -1))
            {
                View.Pitch = Math.Max(View.Pitch, -0.32f);
                await Seconds(0.5);
                await Shot("build");
            }
        }
    }

    // A stone hut with a sheet metal door (the raidbase command), a code lock fitted, its keypad, the hut from outside.
    private async Task Base()
    {
        Admin("give", "code-lock 1");
        await Until(() => Count("code-lock") > 0, 5);
        (Num.Vector3 spot, Num.Vector3 towards) = OpenSpot(Offset(-40, 45));
        await TeleportTo(spot);
        View.TurnToward(towards);
        View.Pitch = 0;
        await Seconds(0.6);
        int doors = Doors().Count();
        Admin("raidbase", "2");
        await Until(() => Doors().Count() > doors, 5);
        await Settle();
        if (Doors().OrderBy(d => Num.Vector3.Distance(Centre(d.Frame), Client.Character.Position)).FirstOrDefault() is not { } door)
        {
            GD.PushWarning("[tour] no hut");
            return;
        }
        Num.Vector3 up = Client.Frame.UpAt(Client.Character.Position);
        Num.Vector3 doorAt = Centre(door.Frame), outward = FlatUnit(Client.Character.Position - doorAt);
        _hut = (doorAt, outward);
        await TeleportTo(doorAt + outward * 2.4f);
        Face(doorAt + up * 1.1f);
        await Hold("code-lock", 5);
        await Seconds(0.4);
        View.UseHeld();
        await Until(() => Doors().Any(d => d.Lock is CodeLock), 5);
        Door locked = Doors().FirstOrDefault(d => d.Lock is CodeLock) ?? door;
        Me.Interact(locked.Ref, "set-code", code: "2580");
        await Seconds(0.6);
        // The keypad, two digits in.
        View.ShowKeypad("ENTER CODE");
        foreach (Key digit in new[] { Key.Key2, Key.Key5 })
        {
            Input.ParseInputEvent(new InputEventKey { Keycode = digit, PhysicalKeycode = digit, Pressed = true });
            await Seconds(0.15);
        }
        await Seconds(0.3);
        await Shot("keypad");
        View.Keypad.Close();
        await TeleportTo(doorAt + outward * 6.5f - Num.Vector3.Normalize(Num.Vector3.Cross(outward, up)) * 1.5f);
        Face(doorAt + up * 1.3f);
        await Settle();
        await Seconds(0.5);
        await Shot("base");
    }

    private (Num.Vector3 Door, Num.Vector3 Outward)? _hut;

    // A timed charge put on the hut's side wall, its lamp lit; the blast from nine metres.
    private async Task Charge()
    {
        if (_hut is not { } hut)
        {
            return;
        }
        Admin("give", "timed-charge 2");
        await Until(() => Count("timed-charge") > 0, 5);
        Num.Vector3 up = Client.Frame.UpAt(hut.Door);
        Num.Vector3 centre = hut.Door - hut.Outward * 1.5f;
        Piece? wall = Client.Building().World?.AllPieces.Where(p => p.Def.Key == "wall")
            .OrderBy(p => Num.Vector3.Distance(Centre(p), hut.Door)).FirstOrDefault();
        if (wall == null)
        {
            GD.PushWarning("[tour] no wall");
            return;
        }
        Num.Vector3 wallAt = Centre(wall), normal = FlatUnit(wallAt - centre);
        Num.Vector3 target = wallAt + up * 1.3f;
        await TeleportTo(wallAt + normal * 1.6f);
        Face(target);
        await Hold("timed-charge", 5);
        await Seconds(0.4);
        View.UseHeld(secondary: true);
        await Until(() => Client.Raid().Explosives.Any(e => e.State == ExplosiveState.Stuck), 5);
        if (Client.Raid().Explosives.FirstOrDefault(e => e.State == ExplosiveState.Stuck) is not { } charge)
        {
            GD.PushWarning("[tour] the charge was not placed");
            return;
        }
        await TeleportTo(charge.ArcStart + normal * 1.9f + Num.Vector3.Normalize(Num.Vector3.Cross(normal, up)) * 0.6f);
        Face(charge.ArcStart);
        await Until(() => View.Raid.LampLit, 3);
        await Shot("charge", frames: 0);
        bool blasted = false;
        void Blast(DetonationEvent _) => blasted = true;
        Client.Raid().Detonated += Blast;
        await TeleportTo(charge.ArcStart + normal * 9f + Num.Vector3.Normalize(Num.Vector3.Cross(normal, up)) * 3f);
        Face(charge.ArcStart + up * 0.8f);
        await Until(() => blasted, 12);
        Client.Raid().Detonated -= Blast;
        // The fireball at its fullest.
        await Seconds(0.22);
        FxDirector.Instance.Paused = true;
        await Shot("explosion", frames: 1);
        FxDirector.Instance.Paused = false;
        await Seconds(1.0);
    }

    // A firefight at the stone hut: the host's rifle with a holo sight and a muzzle brake, the guest's (if in) with a
    // holo sight and a light, both firing at its side wall from the meadow in bursts; the picture is taken as one of
    // the host's shots leaves with a tracer (G4 with the effects library, attachments on guns in hand).
    private async Task Firefight()
    {
        if (_hut is not { } hut)
        {
            return;
        }
        Admin("give", "rifle 1");
        Admin("give", "rifle-ammo 150");
        Admin("give", "holo-sight 1");
        Admin("give", "muzzle-brake 1");
        await Until(() => Count("rifle") > 0 && Count("rifle-ammo") > 0 && Count("muzzle-brake") > 0, 5);
        Num.Vector3 up = Client.Frame.UpAt(hut.Door);
        Num.Vector3 centre = hut.Door - hut.Outward * 1.5f;
        Piece? wall = Client.Building().World?.AllPieces.Where(p => p.Def.Key == "wall")
            .OrderBy(p => Num.Vector3.Distance(Centre(p), hut.Door)).FirstOrDefault();
        if (wall == null)
        {
            return;
        }
        Num.Vector3 wallAt = Centre(wall), normal = FlatUnit(wallAt - centre);
        Num.Vector3 left = Num.Vector3.Normalize(Num.Vector3.Cross(normal, up));
        Num.Vector3 stand = wallAt + normal * 10f + left * 1.5f;
        Num.Vector3 target = wallAt + normal * 0.2f + up * 1.3f;
        await Until(() => Client.Entities.Any(e => e.Name.Length > 0), 30);
        if (Client.Entities.FirstOrDefault(e => e.Name.Length > 0)?.Name is { } guest)
        {
            // The guest ahead and to the right, in view; it fires at the wall nearest it once it has a loaded rifle.
            Num.Vector3 at = wallAt + normal * 5.5f - left * 2f;
            foreach (string item in new[] { "rifle 1", "rifle-ammo 150", "holo-sight 1", "weapon-light 1" })
            {
                Command?.Invoke($"give {guest} {item}");
            }
            Command?.Invoke(string.Create(CultureInfo.InvariantCulture, $"teleport {guest} {at.X:0.00} {at.Z:0.00}"));
        }
        await TeleportTo(stand);
        Face(target);
        await Hold("rifle", 5);
        if (SlotOf(Me.Inventory.Belt, "rifle") is int gun)
        {
            foreach (string part in new[] { "holo-sight", "muzzle-brake" })
            {
                if (SlotOf(Me.Inventory.Main, part) is int from)
                {
                    Me.Attach(ContainerRef.Main, from, ContainerRef.Belt, gun);
                    await Seconds(0.3);
                }
            }
        }
        await Until(() => Me.Gun.Ready, 3);
        Me.Gun.Reload();
        await Until(() => Me.Gun.Loaded > 0 && !Me.Gun.Reloading, 8);
        await Settle();
        // The picture is the frame a shot with a tracer leaves in, the second once the wall has taken a few hits.
        Task? capture = null;
        bool armed = false;
        int tracers = 0;
        void Fired(ShotEvent shot)
        {
            if (armed && capture == null && FxDirector.WantsTracer(Client.NetId, shot.Seq) && ++tracers >= 2)
            {
                capture = Shot("firefight", frames: 0);
            }
        }
        Me.Gun.ShotFired += Fired;
        double started = Time.GetTicksMsec() / 1000.0;
        for (int burst = 0; burst < 10 && capture == null; burst++)
        {
            double until = Time.GetTicksMsec() / 1000.0 + 0.45;
            View.Scripted = new InputFrame { Primary = true, PrimaryPressed = true };
            armed = Time.GetTicksMsec() / 1000.0 - started > 1.6;
            while (Time.GetTicksMsec() / 1000.0 < until && capture == null)
            {
                Face(target);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            View.Scripted = null;
            await Seconds(0.35);
        }
        Me.Gun.ShotFired -= Fired;
        if (capture != null)
        {
            await capture;
        }
        await Seconds(0.5);
        Me.HeldSlot = 2;
    }

    // ---------------------------------------------------------------- the Tin Finch (G6)

    // A vehicle workbench put down in the open near the hut, the Tin Finch crafted there (it rolls out onto the bench's
    // bay), boarded through its hatch, flown over the forest from the cockpit, back past the guest on the ground in the
    // chase camera, and landed beside the hut.
    private async Task Ship()
    {
        Num.Vector3 home = _hut?.Door ?? Client.Character.Position;
        Num.Vector3 outward = _hut?.Outward ?? Forward();
        // The guest's name while they are in range (far out in the ship they won't be).
        string? guestName = Client.Entities.FirstOrDefault(e => e.Name.Length > 0)?.Name;
        // Room in the inventory for the workbench and the ship's makings.
        foreach (string junk in new[] { "stone", "wooden-spear", "metal-ore", "sulphur-ore", "stone-pile" })
        {
            while (SlotOf(Me.Inventory.Main, junk) is int slot)
            {
                Me.Drop(ContainerRef.Main, slot, Me.Inventory.Main[slot].Count);
                await Seconds(0.15);
            }
        }
        foreach (string item in new[] { "vehicle-workbench 1", "metal-fragments 1200", "refined-metal 120", "wood 300", "fibre 100", "lamp-oil 400" })
        {
            Admin("give", item);
        }
        await Until(() => Count("vehicle-workbench") > 0 && Count("lamp-oil") >= 400 && Count("refined-metal") >= 120, 5);
        // The bench on flat ground just ahead, out from the hut's door, its bay beyond it clear.
        Num.Vector3 towards = outward;
        if (!await PlaceBench(home + outward * 30f, towards))
        {
            GD.PushWarning("[tour] the vehicle workbench found no place");
            return;
        }
        Num.Vector3 up = Client.Frame.UpAt(Client.Character.Position);
        await Seconds(0.5);
        // The recipe at the bench: crafted there, it rolls out onto the bay.
        View.Interface.Open(GroundScreen.Crafting);
        View.Interface.SelectRecipe("tin-finch");
        await Seconds(1.0);
        await Shot("ship-craft");
        View.Interface.Open(GroundScreen.Hud);
        int ships = Client.Ships().All.Count();
        if (Client.Content.Item("tin-finch") is { } finch)
        {
            Me.Craft(finch.Id);
        }
        await Until(() => Client.Ships().All.Count() > ships, 140);
        if (Client.Ships().All.OrderBy(s => Num.Vector3.Distance(s.Position, Client.Character.Position)).FirstOrDefault() is not { Def: { } def } ship)
        {
            GD.PushWarning("[tour] no ship rolled out");
            return;
        }
        await Seconds(1.0);
        // To the hatch (the helm's part): open it, and the prompt to board.
        byte hatchPart = (byte)def.PartOfSeat(def.Helm.Index);
        Num.Vector3 hatch = ship.PointOf(def.PartPoint(hatchPart));
        Num.Vector3 side = FlatUnit(hatch - ship.Position);
        await TeleportTo(hatch + side * 2.4f);
        Face(hatch);
        await Seconds(0.6);
        var atHatch = new TargetRef(TargetFamily.Ship, ship.Id.Value, hatchPart);
        if (ship.Doors.Length > 0 && !ship.Doors[0].Opening)
        {
            Me.Interact(atHatch, "hatch-open");
            await Until(() => ship.DoorAt(0, Client.ServerTickNow) > 0.95f, 4);
        }
        // The tank filled with the lamp oil left over from the recipe.
        Me.Interact(atHatch with { Part = (byte)Math.Max(0, def.PartIndex("fuel")) }, "refuel");
        await Seconds(0.5);
        Face(hatch);
        await Seconds(0.4);
        await Shot("ship-boarding");
        Me.Interact(atHatch, def.Helm.VerbKey);
        await Until(() => Client.Ships().Piloting, 5);
        await Seconds(0.8);
        // Up and out over the forest, from the cockpit.
        View.ShipCamera.Mode = HollowUniverse.Ships.ShipCameraMode.Cockpit;
        await Pilot(new ShipInput { Vertical = 1 }, 3.5);
        Num.Vector3 away = ship.Position + towards * 600f;
        await FlyTo(away, 70f, 60f, 25, cockpitShot: true);
        // Back to the hut, in the chase camera: a hover off to one side of the guest standing on the ground, turned to
        // face them, for the picture.
        View.ShipCamera.Mode = HollowUniverse.Ships.ShipCameraMode.Chase;
        View.ShipCamera.Reset();
        Num.Vector3 across = Num.Vector3.Normalize(Num.Vector3.Cross(outward, up));
        Num.Vector3 watch = ClearSpot(home + outward * 16f + across * 12f, 5f);
        if (guestName != null)
        {
            Command?.Invoke(string.Create(CultureInfo.InvariantCulture, $"teleport {guestName} {watch.X:0.00} {watch.Z:0.00}"));
        }
        // Over open ground (trees stand 20 m and more), the guest ahead and to one side of the ship.
        Num.Vector3 hover = ClearSpot(watch + outward * 13f - across * 3f, 8f);
        await FlyTo(hover, 10f, 5f, 60);
        await Hover(watch + across * 6f, 10f, 3.0);
        await Shot("ship-chase", frames: 0);
        // Down beside the hut on open ground: over the spot, turned to face the hut, then "down" until the skids touch.
        Num.Vector3 pad = ClearSpot(home + outward * 8f - across * 10f, 9f);
        await FlyTo(pad, 10f, 3f, 40);
        await Hover(home - outward * 1.5f, 10f, 2.0);
        await Pilot(new ShipInput { Vertical = -1 }, 12, () => Client.Ships().OwnShip.Has(ShipFlags.Landed));
        await Pilot(default, 1.5);
        View.Scripted = null;
        await Shot("ship-landed", frames: 0);
        Me.LeaveSeat();
        await Until(() => !Client.Ships().Seated, 5);
        await Seconds(0.8);
    }

    // The vehicle workbench put down 2.2 m ahead of a spot near `near`, facing `towards`: tried where the bench's ground
    // and its bay's are flat (under 12°) and nothing solid stands within 4 m of the bay, until the server lets it stand.
    private async Task<bool> PlaceBench(Num.Vector3 near, Num.Vector3 towards)
    {
        Num.Vector3 up0 = Client.Frame.UpAt(near);
        var solid = new List<NaturalObject>();
        Client.Survival().Nature?.Query(near, 60, solid);
        solid.RemoveAll(o => Client.Content.Natural(o.Kind) is not { IsSolid: true });
        int tries = 0;
        for (int ring = 0; ring < 6 && tries < 5; ring++)
        {
            for (int k = 0; k < (ring == 0 ? 1 : 8) && tries < 5; k++)
            {
                float a = k * MathF.Tau / 8;
                Num.Vector3 stand = near + new Num.Vector3(MathF.Cos(a), 0, MathF.Sin(a)) * (ring * 7f);
                Num.Vector3 bench = stand + towards * 2.2f, bay = bench + towards * 4.6f;
                if (Slope(bench) > 12 || Slope(bay) > 12 || solid.Any(o => Flat(o.Position - bay) < 4.5f || Flat(o.Position - bench) < 2.5f))
                {
                    continue;
                }
                tries++;
                await TeleportTo(stand);
                View.TurnToward(towards);
                View.Pitch = -0.2f;
                await Settle();
                Num.Vector3 up = Client.Frame.UpAt(Client.Character.Position);
                // Within the placement reach of the eye (3 m).
                Num.Vector3 at = Client.Character.Position + Forward() * 2.2f;
                Outcome? refused = null;
                void Refused(IntentAnswer answer) => refused = answer.Outcome;
                Me.IntentRejected += Refused;
                if (SlotOf(Me.Inventory.Main, "vehicle-workbench") is int slot && Placeable("vehicle-workbench") is { } workbench &&
                    Client.Placement().PlanAt(workbench, Client.Placement().Ground?.Settle(at) ?? at, View.Yaw) is { } ghost)
                {
                    if (ghost.Valid)
                    {
                        Me.Place(ghost, ContainerRef.Main, slot);
                    }
                    else
                    {
                        refused = ghost.Outcome;
                    }
                }
                await Until(() => Client.Survival().Things.Any(t => t.Deployable(Client.Content)?.Key == "vehicle-workbench") || refused != null, 2);
                Me.IntentRejected -= Refused;
                if (refused != null)
                {
                    GD.Print($"[tour] the vehicle workbench was refused there: {refused}");
                }
                if (Client.Survival().Things.Any(t => t.Deployable(Client.Content)?.Key == "vehicle-workbench"))
                {
                    return true;
                }
            }
        }
        return false;
    }

    // The ground's slope at a point (degrees), from three points of the ground a metre round it.
    private float Slope(Num.Vector3 p)
    {
        Num.Vector3 up = Client.Frame.UpAt(p);
        Num.Vector3 east = Num.Vector3.Normalize(Num.Vector3.Cross(up, new Num.Vector3(0, 0, 1)));
        Num.Vector3 north = Num.Vector3.Cross(east, up);
        Num.Vector3? Ground(Num.Vector3 q) => Client.Placement().Ground?.Settle(q);
        if (Ground(p) is not { } a || Ground(p + east) is not { } b || Ground(p + north) is not { } c)
        {
            return 90;
        }
        Num.Vector3 n = Num.Vector3.Normalize(Num.Vector3.Cross(b - a, c - a));
        return MathF.Acos(Math.Clamp(MathF.Abs(Num.Vector3.Dot(n, up)), 0, 1)) * 180 / MathF.PI;
    }

    // The pilot's controls for a while (or until done): the scripted input carries them.
    private async Task Pilot(ShipInput controls, double seconds, Func<bool>? done = null)
    {
        double until = Time.GetTicksMsec() / 1000.0 + seconds;
        while (Time.GetTicksMsec() / 1000.0 < until && !(done?.Invoke() ?? false))
        {
            View.Scripted = new InputFrame { Ship = controls };
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    // A small autopilot with flight assist on: turn toward the point and level; a speed that lets the ship stop at the
    // point (flight assist brakes at about 8 m/s²) on the lever; over the trees (45 m) until slow and near, then to the
    // height asked; done within `arrive` metres (flat). The cockpit picture is taken over the forest.
    private async Task FlyTo(Num.Vector3 target, float height, float arrive, double seconds, bool cockpitShot = false)
    {
        double start = Time.GetTicksMsec() / 1000.0;
        bool shot = false;
        while (Client.Ships().Piloting && Time.GetTicksMsec() / 1000.0 - start < seconds)
        {
            ShipState s = Client.Ships().OwnShip;
            Num.Vector3 up = Client.Frame.UpAt(s.Position);
            Num.Vector3 to = target - s.Position;
            Num.Vector3 flat = to - up * Num.Vector3.Dot(to, up);
            float distance = flat.Length();
            float speed = s.Velocity.Length();
            if (distance < arrive && speed < 4)
            {
                break;
            }
            float over = OverGround(s.Origin(Client.Ships().OwnShipDef!.CentreOfMass));
            // Over the trees until over the point and slow, then down to the height asked.
            float wanted = distance > 12 || speed > 6 ? Math.Max(height, 45f) : height;
            float cruise = MathF.Min(95f, MathF.Sqrt(2 * 5f * MathF.Max(0, distance - arrive * 0.5f)));
            View.Scripted = new InputFrame { Ship = Steer(s, up, flat, distance > 2 ? cruise / 160f : 0, wanted, over) };
            View.ShipControls.Throttle = View.Scripted.Value.Ship.Throttle;
            if (cockpitShot && !shot && Time.GetTicksMsec() / 1000.0 - start > 6 && over > 40)
            {
                shot = true;
                await Shot("ship-cockpit", frames: 0);
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        View.Scripted = new InputFrame { Ship = default };
        View.ShipControls.Throttle = 0;
    }

    // Holds the ship where it is, down (or up) to a height, turning to face a point: at least `seconds`, until it is
    // there and still (at most 15 s).
    private async Task Hover(Num.Vector3 face, float height, double seconds)
    {
        double start = Time.GetTicksMsec() / 1000.0;
        while (Client.Ships().Piloting)
        {
            ShipState s = Client.Ships().OwnShip;
            Num.Vector3 up = Client.Frame.UpAt(s.Position);
            Num.Vector3 to = face - s.Position;
            Num.Vector3 flat = to - up * Num.Vector3.Dot(to, up);
            float over = OverGround(s.Origin(Client.Ships().OwnShipDef!.CentreOfMass));
            double t = Time.GetTicksMsec() / 1000.0 - start;
            if (t > 15 || (t > seconds && MathF.Abs(over - height) < 1.5f && s.Velocity.Length() < 1.5f))
            {
                break;
            }
            View.Scripted = new InputFrame { Ship = Steer(s, up, flat, 0, height, over) };
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        View.Scripted = new InputFrame { Ship = default };
    }

    // The nearest point to `near` (within 30 m) with nothing solid of the planet's within `clear` metres, ground under
    // 15° and no building within 6 m: somewhere to hover low or set down.
    private Num.Vector3 ClearSpot(Num.Vector3 near, float clear)
    {
        var all = new List<NaturalObject>();
        Client.Survival().Nature?.Query(near, 50, all);
        List<NaturalObject> solid = all.Where(o => Client.Content.Natural(o.Kind) is { IsSolid: true }).ToList();
        List<Num.Vector3> pieces = Client.Building().World?.AllPieces.Select(Centre).ToList() ?? new List<Num.Vector3>();
        for (int ring = 0; ring < 8; ring++)
        {
            for (int k = 0; k < (ring == 0 ? 1 : 12); k++)
            {
                float a = k * MathF.Tau / 12;
                Num.Vector3 p = near + new Num.Vector3(MathF.Cos(a), 0, MathF.Sin(a)) * (ring * 4f);
                if (!solid.Any(o => Flat(o.Position - p) < clear) && !pieces.Any(c => Flat(c - p) < 6) && Slope(p) <= 15)
                {
                    return p;
                }
            }
        }
        return near;
    }

    // The stick and the lever: yaw toward a flat direction, the nose and the wings level, up or down to a height.
    private static ShipInput Steer(in ShipState s, Num.Vector3 up, Num.Vector3 flat, float lever, float height, float over)
    {
        Num.Vector3 want = flat.LengthSquared() > 1e-4f ? Num.Vector3.Normalize(flat) : s.Forward;
        Num.Vector3 f = s.Forward - up * Num.Vector3.Dot(s.Forward, up);
        f = f.LengthSquared() > 1e-4f ? Num.Vector3.Normalize(f) : want;
        float angle = MathF.Atan2(Num.Vector3.Dot(Num.Vector3.Cross(f, want), up), Num.Vector3.Dot(f, want));
        float pitch = -MathF.Asin(Math.Clamp(Num.Vector3.Dot(s.Forward, up), -1, 1));
        return new ShipInput
        {
            Yaw = Math.Clamp(-angle * 1.6f, -1, 1),
            Pitch = Math.Clamp(pitch * 2f, -1, 1),
            Roll = Math.Clamp(Num.Vector3.Dot(s.Right, up) * 2f, -1, 1),
            Throttle = MathF.Abs(angle) > 0.6f ? MathF.Min(lever, 0.08f) : lever,
            Vertical = Math.Clamp((height - over) / 8f, over < 20 ? -0.6f : -1, 1),
        };
    }

    // How far a point is above the planet's ground or sea.
    private float OverGround(Num.Vector3 point)
    {
        if (Client.Surface is not { } surface)
        {
            return float.MaxValue;
        }
        Hollow.Core.Vector3D body = Client.Frame.ToBody(point);
        double r = body.Length;
        double ground = surface.HeightAt(body / r);
        if (surface.SeaRadius > 0)
        {
            ground = Math.Max(ground, surface.SeaRadius - surface.Radius);
        }
        return (float)(r - surface.Radius - ground);
    }

    // The guest's part of the firefight: a rifle it is given goes on the belt with its attachments, into its hands and
    // is loaded; then it fires in bursts for eight seconds at the building wall nearest it.
    private double _guestFiring = -1;

    private bool GuestRifle()
    {
        double now = Time.GetTicksMsec() / 1000.0;
        if (_guestFiring > 0 && now - _guestFiring < 8 && SlotOf(Me.Inventory.Belt, "rifle") is int held && Me.HeldSlot == held &&
            NearestWall() is { } aim)
        {
            Face(aim);
            bool on = (now % 0.8) < 0.45 && Me.Gun.Loaded > 0;
            View.Scripted = new InputFrame { Primary = on, PrimaryPressed = on };
            if (Me.Gun.Loaded == 0 && !Me.Gun.Reloading)
            {
                Me.Gun.Reload();
            }
            return true;
        }
        if (View.Scripted != null)
        {
            View.Scripted = null;
        }
        if (Me.PendingCount > 0 || _guestFiring > 0)
        {
            return _guestFiring > 0 && now - _guestFiring < 8;
        }
        if (SlotOf(Me.Inventory.Main, "rifle") is int rifle)
        {
            Me.MoveItem(ContainerRef.Main, rifle, ContainerRef.Belt, 3);
            return true;
        }
        if (SlotOf(Me.Inventory.Belt, "rifle") is not int belt)
        {
            return false;
        }
        foreach (string part in new[] { "holo-sight", "weapon-light" })
        {
            if (SlotOf(Me.Inventory.Main, part) is int from)
            {
                Me.Attach(ContainerRef.Main, from, ContainerRef.Belt, belt);
                return true;
            }
        }
        if (Me.HeldSlot != belt)
        {
            Me.HeldSlot = belt;
            return true;
        }
        if (Me.Gun.Ready && Me.Gun.Loaded == 0 && !Me.Gun.Reloading && Count("rifle-ammo") > 0)
        {
            Me.Gun.Reload();
        }
        else if (Me.Gun.Loaded > 0 && !Me.Gun.Reloading)
        {
            _guestFiring = now;
        }
        return true;
    }

    // The middle of the building wall nearest the player, at chest height.
    private Num.Vector3? NearestWall()
    {
        Num.Vector3 me = Client.Character.Position;
        Piece? wall = Client.Building().World?.AllPieces.Where(p => p.Def.Key == "wall").OrderBy(p => Num.Vector3.Distance(Centre(p), me)).FirstOrDefault();
        return wall != null ? Centre(wall) + Client.Frame.UpAt(me) * 1.3f : null;
    }

    private IEnumerable<Door> Doors() =>
        Client.Building().World?.AllPieces.Where(p => p.Door != null).Select(p => p.Door!) ?? Enumerable.Empty<Door>();

    private static Num.Vector3 Centre(Piece piece) => piece.Structure.Frame.ToZone(piece.Pose).Position;

    private static Num.Vector3 At(PlacementPlan plan) => plan.ZoneTransform.Position;

    // A direction laid along the ground where the player is (made unit length).
    private Num.Vector3 FlatUnit(Num.Vector3 d)
    {
        Num.Vector3 up = Client.Frame.UpAt(Client.Character.Position);
        d -= up * Num.Vector3.Dot(d, up);
        return d.LengthSquared() > 1e-6f ? Num.Vector3.Normalize(d) : Facing.Compass(up).North;
    }

    // Takes an item in hand: from the belt if it is there, else moved from the main inventory to a belt slot.
    private async Task Hold(string key, int slot)
    {
        if (SlotOf(Me.Inventory.Belt, key) is not int onBelt)
        {
            MoveToBelt(key, slot);
            await Until(() => SlotOf(Me.Inventory.Belt, key) != null, 3);
            onBelt = SlotOf(Me.Inventory.Belt, key) ?? slot;
        }
        Me.HeldSlot = onBelt;
        await Seconds(0.4);
    }

    // Places a piece with the plan in hand where a wanted, valid placement is nearest the view; the piece, once there.
    private async Task<Piece?> PlaceAimed(string key, Func<PlacementPlan, bool> want)
    {
        if (!await AimAt(key, want) || Client.Building().World is not { } world)
        {
            return null;
        }
        var before = world.AllPieces.ToHashSet();
        View.UseHeld();
        await Until(() => world.AllPieces.Any(p => !before.Contains(p) && p.Def.Key == key), 5);
        await Seconds(0.2);
        return world.AllPieces.FirstOrDefault(p => !before.Contains(p) && p.Def.Key == key);
    }

    // Chooses a piece on the plan's wheel and turns the view to the nearest aim where its placement is valid and wanted.
    private async Task<bool> AimAt(string key, Func<PlacementPlan, bool> want)
    {
        BuildingData data = Client.Content.Building;
        int index = -1;
        for (int i = 0; i < data.Wheel.Count; i++)
        {
            if (data.Wheel[i].Key == key)
            {
                index = i;
            }
        }
        if (index < 0)
        {
            return false;
        }
        View.Building.PlanPiece = index;
        if (Client.Content.Placement.Of(data.Wheel[index]) is not { } piece)
        {
            return false;
        }
        Num.Vector3 up = Client.Frame.UpAt(Client.RenderPosition);
        Num.Vector3 eye = Client.RenderPosition + up * Client.EyeHeight;
        float bestCost = float.MaxValue, bestYaw = View.Yaw, bestPitch = View.Pitch;
        for (float dy = -1.2f; dy <= 1.2f; dy += 0.03f)
        {
            for (float pitch = -1.2f; pitch <= 0.3f; pitch += 0.03f)
            {
                Num.Vector3 dir = CharacterMotor.ViewDirection(View.Yaw + dy, pitch, up);
                if (Client.Placement().Plan(piece, eye, dir, View.Yaw + dy) is { Valid: true, Detail: PlacementPlan plan } && want(plan))
                {
                    float cost = MathF.Abs(dy) + MathF.Abs(pitch - View.Pitch) * 0.5f;
                    if (cost < bestCost)
                    {
                        (bestCost, bestYaw, bestPitch) = (cost, View.Yaw + dy, pitch);
                    }
                }
            }
        }
        if (bestCost == float.MaxValue)
        {
            GD.PushWarning($"[tour] nowhere to place a {key}");
            return false;
        }
        View.Yaw = bestYaw;
        View.Pitch = bestPitch;
        await Seconds(0.3);
        return true;
    }

    // G7 (GROUND.md §6.8): a team with the second player (the invite prompt, then their name tag far off), the barrels
    // and crate nearest the landing site (a barrel broken, the crate opened), a recycler at work, a workbench's tech tree
    // with a node unlocked, a research table before and after, a blueprint read, and the map with markers and the team.
    private async Task World()
    {
        // Room in the inventory: what the earlier steps left goes on the ground (the pictures are taken away from it).
        for (int i = 0; i < Me.Inventory.Main.Size; i++)
        {
            if (!Me.Inventory.Main[i].IsEmpty)
            {
                Me.Drop(ContainerRef.Main, i, Me.Inventory.Main[i].Count);
                await Seconds(0.2);
            }
        }
        await Seconds(0.5);
        Admin("give", "stone-hatchet 1");
        Admin("give", "workbench-1 1");
        Admin("give", "research-table 1");
        Admin("give", "sleeping-bag 1");
        Admin("give", "salvage 600");
        Admin("give", "pistol 1");
        Admin("give", "gears 10");
        Admin("give", "metal-pipe 3");
        await Until(() => Count("research-table") > 0 && Count("salvage") >= 600 && Count("metal-pipe") >= 3, 8);
        if (SlotOf(Me.Inventory.Belt, "stone-hatchet") == null)
        {
            MoveToBelt("stone-hatchet", 5);
            await Seconds(0.3);
        }
        Me.HeldSlot = -1;

        // A team, and the second player (close by, looked at) invited into it.
        if (Me.Team != null && !Me.LeadsTeam)
        {
            Me.LeaveTeam();
            await Until(() => Me.Team == null, 5);
        }
        if (Me.Team == null)
        {
            Me.CreateTeam();
            await Until(() => Me.Team != null, 5);
        }
        (Num.Vector3 spot, Num.Vector3 towards) = OpenSpot(Client.Character.Position + Forward() * 25);
        await TeleportTo(spot);
        RemoteEntity? guest = Client.Entities.FirstOrDefault(e => e.Name.Length > 0);
        if (guest != null)
        {
            string name = guest.Name;
            Num.Vector3 ahead = spot + towards * 3.0f;
            Command?.Invoke(string.Create(CultureInfo.InvariantCulture, $"teleport {name} {ahead.X:0.00} {ahead.Z:0.00}"));
            await Until(() => Client.Entities.FirstOrDefault(e => e.Name == name) is { } g && Flat(g.Position - ahead) < 1.5f, 10);
            await Seconds(1.5);
            if (Client.Entities.FirstOrDefault(e => e.Name == name) is { } there)
            {
                Face(there.Position + Client.Frame.UpAt(there.Position) * 1.1f);
            }
            await Settle();
            await Until(() => AimedVerb() == "invite", 5);
            await Seconds(0.5);
            await Shot("team-invite");
            if (AimedVerb() == "invite")
            {
                TapAimed();
            }
            else
            {
                Me.Interact(new TargetRef(TargetFamily.Character, guest.Id.Value), "invite");
            }
            await Until(() => Me.Team?.Members.Count >= 2, 15);
            GD.Print($"[tour] team: {string.Join(", ", Me.Team?.Members.Select(m => m.Name) ?? Array.Empty<string>())}");
            // Further off than strangers are named: the teammate's name tag in the team's green, with how far.
            Num.Vector3 far = spot - towards * 70;
            await TeleportTo(far);
            if (Client.Entities.FirstOrDefault(e => e.Name == name) is { } mate)
            {
                Face(mate.Position + Client.Frame.UpAt(mate.Position) * 1.0f);
            }
            await Settle();
            await Seconds(1.0);
            await Shot("teammate");
        }

        // The barrels and crate nearest the landing site: the cluster, a barrel broken with the hatchet, the crate opened.
        RemoteThing? crate = NearestProp(WorldPropKind.Crate, Landing);
        GD.Print($"[tour] props in range: {Client.World().Props.Count()}, crate at {crate?.Position}");
        if (crate != null)
        {
            Num.Vector3 up = Client.Frame.UpAt(crate.Position);
            await TeleportTo(ClearView(crate.Position, 6.5f));
            Face(crate.Position + up * 0.3f);
            View.Pitch -= 0.05f;
            await Settle();
            await Seconds(1.5);
            await Shot("loot-props");
            if (NearestProp(WorldPropKind.Barrel, crate.Position) is { } barrel && Flat(barrel.Position - crate.Position) < 12)
            {
                await TeleportTo(Away(barrel.Position, 1.5f));
                Face(barrel.Position + Client.Frame.UpAt(barrel.Position) * 0.5f);
                if (SlotOf(Me.Inventory.Belt, "stone-hatchet") is int hatchet)
                {
                    Me.HeldSlot = hatchet;
                }
                await Seconds(1.0);
                View.Scripted = new InputFrame { Primary = true };
                await Until(() => !Client.World().Props.Any(t => t.Id == barrel.Id), 15);
                View.Scripted = null;
                GD.Print($"[tour] the barrel {(Client.World().Props.Any(t => t.Id == barrel.Id) ? "still stands" : "broke")}");
                // Its pieces in the air, then on the ground (gibs, GROUND.md §9.4).
                await Seconds(0.25);
                await Shot("barrel-gibs");
                await Seconds(1.6);
                await Shot("barrel-gibs-rest");
                Me.HeldSlot = -1;
                await Seconds(0.8);
            }
            await TeleportTo(Away(crate.Position, 1.6f));
            Face(crate.Position + up * 0.3f);
            await Seconds(0.5);
            Me.Interact(Thing(crate.Id), "open");
            await Until(() => Me.LootKind == LootKind.Crate, 5);
            await Seconds(1.0);
            await Shot("crate");
            Me.Close();
            View.Interface.Open(GroundScreen.Hud);
            await Seconds(0.5);
        }

        // A recycler at work: gears and pipes in, salvage and metal out.
        // One recycler stands on each cube face of the world (docs/WHOLE_WORLD.md decision 9): pictured when one is near.
        RemoteThing? recycler = NearestProp(WorldPropKind.Recycler, Client.Character.Position);
        if (recycler != null)
        {
            Num.Vector3 up = Client.Frame.UpAt(recycler.Position);
            Num.Vector3 middle = recycler.Position + up * 0.9f;
            await TeleportTo(Away(recycler.Position, 2.4f));
            Face(middle);
            await Settle();
            Me.Interact(Thing(recycler.Id), "open");
            await Until(() => Me.LootKind == LootKind.Device, 5);
            if (SlotOf(Me.Inventory.Main, "gears") is int gears)
            {
                Me.MoveItem(ContainerRef.Main, gears, ContainerRef.Loot, 0);
                await Seconds(0.4);
            }
            if (SlotOf(Me.Inventory.Main, "metal-pipe") is int pipes)
            {
                Me.MoveItem(ContainerRef.Main, pipes, ContainerRef.Loot, 1);
                await Seconds(0.4);
            }
            Me.Interact(Thing(recycler.Id), "turn-on");
            await Until(() => Me.Machine?.Running == true, 5);
            await Seconds(6.5);
            await Shot("recycler");
            Me.Close();
            View.Interface.Open(GroundScreen.Hud);
            await Seconds(0.8);
            Face(middle);
            await Shot("recycler-running");
        }

        // A workbench, a research table and a sleeping bag in the open.
        (spot, towards) = OpenSpot(Client.Character.Position);
        await TeleportTo(spot);
        View.TurnToward(towards);
        View.Pitch = -0.3f;
        await Settle();
        // Each within placing reach (3 m from the eye): the bag to the left, the bench ahead, the table ahead of a step right.
        Num.Vector3 right = Num.Vector3.Normalize(Num.Vector3.Cross(towards, Client.Frame.UpAt(spot)));
        float yaw = View.Yaw;
        await PlaceNear("sleeping-bag", spot, -right, yaw);
        RemoteThing? bench = await PlaceNear("workbench-1", spot, towards, yaw);
        Num.Vector3 side = spot + right * 2.8f;
        await TeleportTo(side);
        RemoteThing? table = await PlaceNear("research-table", side, towards, yaw);
        await TeleportTo(spot);
        if (bench != null)
        {
            Face(bench.Position + Client.Frame.UpAt(bench.Position) * 0.8f);
            await Seconds(0.8);
            await Shot("workbench");
            // The tech tree: a root unlocked for its salvage, then the node after it chosen.
            View.Interface.OpenTechTree();
            View.Interface.SelectTechNode("metal-hatchet");
            await Seconds(0.5);
            View.Model.Unlock("metal-hatchet");
            await Until(() => Client.Content.Item("metal-hatchet") is { } h && Client.Content.Recipe(h.Id) is { } r && Me.Knows(r), 5);
            await Seconds(1.5);
            View.Interface.SelectTechNode("metal-pickaxe");
            await Seconds(0.5);
            await Shot("techtree");
            View.Interface.Open(GroundScreen.Hud);
            await Seconds(0.5);
        }
        if (table != null)
        {
            // The research table: a pistol and salvage in, the price; then the blueprint out, and read.
            await TeleportTo(side);
            Face(table.Position + Client.Frame.UpAt(table.Position) * 0.8f);
            Me.Interact(Thing(table.Id), "open");
            await Until(() => Me.LootKind == LootKind.Device, 5);
            if (SlotOf(Me.Inventory.Main, "pistol") is int pistol)
            {
                Me.MoveItem(ContainerRef.Main, pistol, ContainerRef.Loot, 0);
                await Seconds(0.4);
            }
            if (SlotOf(Me.Inventory.Main, "salvage") is int salvage)
            {
                Me.MoveItem(ContainerRef.Main, salvage, ContainerRef.Loot, 1, 200);
                await Seconds(0.4);
            }
            await Seconds(0.8);
            await Shot("research");
            Me.Interact(Thing(table.Id), "turn-on");
            await Until(() => Me.Loot is { Size: > 2 } slots && !slots[2].IsEmpty, 15);
            await Seconds(0.8);
            await Shot("research-done");
            Me.QuickMove(ContainerRef.Loot, 2);
            await Seconds(0.6);
            Me.Close();
            View.Interface.Open(GroundScreen.Hud);
            if (SlotOf(Me.Inventory.Main, "blueprint") is int blueprint)
            {
                Me.Learn(ContainerRef.Main, blueprint);
                await Until(() => Client.Content.Item("pistol") is { } p && Client.Content.Recipe(p.Id) is { } r && Me.Knows(r), 5);
            }
            await Seconds(0.5);
        }

        // The map (G): two markers, the teammate, the bag, the player.
        Me.PlaceMarker(spot + towards * 40 + right * 25, 2, "Base");
        if (crate != null)
        {
            Me.PlaceMarker(crate.Position, 3, "Crates");
        }
        await Until(() => Me.Markers.Count >= 1, 5);
        View.Interface.ToggleMap();
        await Until(() => View.Interface.MapReady, 60);
        await Seconds(1.0);
        await Shot("map");
        View.Interface.Open(GroundScreen.Hud);
        await Seconds(0.3);
    }

    // The nearest world prop of a kind in range.
    private RemoteThing? NearestProp(WorldPropKind kind, Num.Vector3 near) =>
        Client.World().Props.Where(t => t.Prop(Client.Content)?.Kind == kind).OrderBy(t => Flat(t.Position - near)).FirstOrDefault();

    // Places a deployable from the main slots 2.2 m from where the player stands, towards a direction or, refused (too
    // steep, something in the way), turned a little further each time; waits for it to stand there.
    private async Task<RemoteThing?> PlaceNear(string key, Num.Vector3 from, Num.Vector3 towards, float yaw)
    {
        Num.Vector3 up = Client.Frame.UpAt(from);
        Outcome refused = OutcomeCode.Done;
        void Refused(IntentAnswer answer) => refused = answer.Outcome;
        Me.IntentRejected += Refused;
        try
        {
            foreach (float turn in new[] { 0f, 0.6f, -0.6f, 1.2f, -1.2f, 1.9f, -1.9f })
            {
                if (SlotOf(Me.Inventory.Main, key) is not int slot)
                {
                    GD.PushWarning($"[tour] no {key} to place");
                    return null;
                }
                Num.Vector3 dir = Num.Vector3.Transform(towards, Num.Quaternion.CreateFromAxisAngle(up, turn));
                Num.Vector3 at = from + dir * 2.2f;
                if (Placeable(key) is not { } placeable || Client.Placement().PlanAt(placeable, Client.Placement().Ground?.Settle(at) ?? at, yaw) is not { } ghost)
                {
                    continue;
                }
                var before = Client.Survival().Things.Select(t => t.Id).ToHashSet();
                refused = ghost.Outcome;
                if (ghost.Valid)
                {
                    Me.Place(ghost, ContainerRef.Main, slot);
                }
                await Until(() => refused != OutcomeCode.Done ||
                    Client.Survival().Things.Any(t => !before.Contains(t.Id) && t.Deployable(Client.Content)?.Key == key), 5);
                RemoteThing? placed = Client.Survival().Things.FirstOrDefault(t => !before.Contains(t.Id) && t.Deployable(Client.Content)?.Key == key);
                if (placed != null)
                {
                    await Seconds(0.3);
                    return placed;
                }
                GD.Print($"[tour] the {key} was refused there: {refused}");
                await Seconds(0.3);
            }
            GD.PushWarning($"[tour] the {key} wasn't placed");
            return null;
        }
        finally
        {
            Me.IntentRejected -= Refused;
        }
    }

    // A point a distance from a target with the clearest view of it: the fewest trees, bushes and rocks near the line of
    // sight (for pictures of things among the plants).
    private Num.Vector3 ClearView(Num.Vector3 target, float distance)
    {
        var all = new List<NaturalObject>();
        float r = distance + 4;
        Client.Survival().Nature!.Query(target, r, all);
        Num.Vector3 best = Away(target, distance);
        float bestCost = float.MaxValue;
        for (int k = 0; k < 16; k++)
        {
            float a = k * MathF.Tau / 16;
            Num.Vector3 eye = target + new Num.Vector3(MathF.Cos(a), 0, MathF.Sin(a)) * distance;
            float cost = 0;
            foreach (NaturalObject o in all)
            {
                if (Client.Content.Natural(o.Kind) is not { Class: NaturalClass.Tree or NaturalClass.Bush or NaturalClass.Boulder or NaturalClass.Node } kind ||
                    Client.Survival().Natural?.IsDepleted(o.Id) == true)
                {
                    continue;
                }
                // How close the object stands to the line from the eye to the target (in the ground plane).
                Num.Vector2 p = new(o.Position.X - eye.X, o.Position.Z - eye.Z), d = new(target.X - eye.X, target.Z - eye.Z);
                float t = Math.Clamp(Num.Vector2.Dot(p, d) / d.LengthSquared(), 0, 1);
                float off = (p - d * t).Length() - kind.Radius * o.Scale;
                if (off < 1.6f && t < 0.9f)
                {
                    cost += kind.Class == NaturalClass.Tree ? 3 : 2;
                }
            }
            if (cost < bestCost)
            {
                (bestCost, best) = (cost, eye);
            }
        }
        return best;
    }

    // P3 (GROUND_SYSTEMS.md §7): the AK's information with its round's icon on its slot, its rounds on R held, the bag
    // charge's information, a blueprint with its LEARN, and a recipe at a workbench above its level, shorter.
    private async Task Held()
    {
        for (int i = 0; i < Me.Inventory.Main.Size; i++)
        {
            if (!Me.Inventory.Main[i].IsEmpty)
            {
                Me.Drop(ContainerRef.Main, i, Me.Inventory.Main[i].Count);
                await Seconds(0.2);
            }
        }
        await Seconds(0.5);
        foreach (string give in new[] { "rifle 1", "rifle-ammo 60", "rifle-ammo-hv 24", "holo-sight 1", "bag-charge 3", "pistol.blueprint 1",
                     "workbench-2 1", "wood 300" })
        {
            Admin("give", give);
        }
        await Until(() => Count("rifle") > 0 && Count("bag-charge") > 0 && Count("workbench-2") > 0 && Count("blueprint") > 0, 8);
        // The rifle on the belt with its sight, taken up and loaded.
        MoveToBelt("rifle", 1);
        await Seconds(0.4);
        if (SlotOf(Me.Inventory.Main, "holo-sight") is int sight)
        {
            Me.Attach(ContainerRef.Main, sight, ContainerRef.Belt, 1);
            await Seconds(0.4);
        }
        Me.HeldSlot = 1;
        await Until(() => Me.Gun.Ready, 5);
        Me.Reload();
        await Until(() => Me.Gun.Loaded > 0 && !Me.Gun.Reloading && Me.Gun.PendingCount == 0, 8);
        await Seconds(0.6);
        await Shot("ammo-icon");
        // R held: the rifle's rounds, the loaded kind in the middle.
        if (View.Model.HeldRadial?.Invoke(RadialInput.Reload) is { } rounds)
        {
            View.Interface.OpenRadial(rounds, RadialInput.Reload);
            await Seconds(0.5);
            await Shot("ammo-radial");
            View.Interface.CloseRadial(apply: false);
            await Seconds(0.3);
        }
        // The inventory: the AK's panel, the bag charge's, the blueprint's.
        View.Interface.Open(GroundScreen.Inventory);
        View.Interface.Select(new SlotRef(ContainerId.Belt, 1));
        await Seconds(0.8);
        await Shot("ak-info");
        foreach ((string key, string shot) in new[] { ("bag-charge", "bag-charge-info"), ("blueprint", "blueprint-learn") })
        {
            if (SlotOf(Me.Inventory.Main, key) is int slot)
            {
                View.Interface.Select(new SlotRef(ContainerId.Main, slot));
                await Seconds(0.6);
                await Shot(shot);
            }
        }
        View.Interface.Open(GroundScreen.Hud);
        await Seconds(0.3);
        // A level 2 workbench in the open, and a spear (no workbench needed) crafted in a quarter of its time beside it.
        (Num.Vector3 spot, Num.Vector3 towards) = OpenSpot(Client.Character.Position);
        await TeleportTo(spot);
        View.TurnToward(towards);
        View.Pitch = -0.3f;
        await Settle();
        if (await PlaceNear("workbench-2", spot, towards, View.Yaw) is { } bench)
        {
            Face(bench.Position + Client.Frame.UpAt(bench.Position) * 0.6f);
            await Until(() => Me.WorkbenchLevel >= 2, 5);
            View.Interface.Open(GroundScreen.Crafting);
            View.Interface.SelectRecipe("wooden-spear");
            await Seconds(1.0);
            await Shot("crafting-workbench");
            View.Interface.Open(GroundScreen.Hud);
            await Seconds(0.3);
        }
    }

    private async Task Death()
    {
        Admin("kill", "");
        await Until(() => Me.IsDead, 5);
        await Seconds(1.5);
        await Shot("death");
    }

    // docs/WHOLE_WORLD.md §11: the map of the whole world as G opens it, following the player, local north up.
    private async Task Map()
    {
        GroundUi ui = View.Interface;
        ui.HoldPointer(new Vector2(-10000, -10000));
        ui.ToggleMap();
        await Until(() => ui.MapReady, 120);
        await Seconds(1.0);
        await Shot("map-open");
        ui.Open(GroundScreen.Hud);
        await Seconds(0.3);
    }

    // Back on the beach, then the meadow at night by a campfire.
    private async Task Night()
    {
        Me.Respawn();
        await Until(() => !Me.IsDead && Client.HasCharacter, 5);
        Admin("give", "campfire 1");
        Admin("give", "wood 200");
        Admin("give", "woven-shirt 1");
        await Until(() => Count("campfire") > 0 && Count("wood") > 0, 5);
        (Num.Vector3 spot, Num.Vector3 towards) = OpenSpot(_bush is { } b ? Away(b.Position, 7f) : Landing);
        await TeleportTo(spot);
        View.TurnToward(towards);
        View.Pitch = -0.25f;
        await Settle();
        // The fire two metres ahead, lit with wood.
        Num.Vector3 at = Client.Character.Position + Forward() * 2.2f;
        if (SlotOf(Me.Inventory.Main, "campfire") is int fire && Placeable("campfire") is { } campfireItem &&
            Client.Placement().PlanAt(campfireItem, Client.Placement().Ground?.Settle(at) ?? at, View.Yaw) is { Valid: true } ghost)
        {
            Me.Place(ghost, ContainerRef.Main, fire);
            await Until(() => Client.Survival().Things.Any(t => t.Deployable(Client.Content)?.Key == "campfire"), 5);
            RemoteThing campfire = Client.Survival().Things.First(t => t.Deployable(Client.Content)?.Key == "campfire");
            Me.Interact(Thing(campfire.Id), "open");
            await Until(() => Me.LootKind == LootKind.Device, 5);
            if (SlotOf(Me.Inventory.Main, "wood") is int wood)
            {
                Me.MoveItem(ContainerRef.Main, wood, ContainerRef.Loot, 0, 60);
                await Seconds(0.5);
            }
            Me.Interact(Thing(campfire.Id), "light");
            await Seconds(0.5);
            Me.Close();
            View.Interface.Open(GroundScreen.Hud);
            Face(campfire.Position + Client.Frame.UpAt(campfire.Position) * 0.3f);
            View.Pitch -= 0.05f;
        }
        // The sky at eleven at night (the client's clock only).
        if (View.World is { Sky: { } sky } world)
        {
            Hollow.Core.Vector3D site = world.Frame!.ToBody(Godot.Vector3.Zero).Normalized;
            double hours = sky.Ephemeris.LocalSolarHours(site, sky.Sun.Index, world.ShownTime);
            double ahead = ((23 - hours) % 24 + 24) % 24;
            world.JumpClock(ahead / 24 * Math.Abs(sky.Ephemeris.Planet.RotationPeriod));
        }
        await Settle();
        await Seconds(2.0);
        await Shot("night");
    }

    // ---------------------------------------------------------------- the guest

    private bool _guestDressed;
    private double _guestAnswered = -10;

    private void Guest()
    {
        if (Client.State != ClientState.Playing || !Client.HasCharacter || Me.IsDead)
        {
            return;
        }
        // G7: a team invite is taken (leaving any team first).
        if (Me.TeamInvites.Count > 0 && Client.ClockSeconds - _guestAnswered > 2)
        {
            _guestAnswered = Client.ClockSeconds;
            if (Me.Team != null)
            {
                Me.LeaveTeam();
            }
            else
            {
                Me.AcceptInvite(Me.TeamInvites[0].Team);
            }
        }
        if (GuestRifle())
        {
            return;
        }
        // Keep facing the host.
        if (Client.Entities.FirstOrDefault() is { } host)
        {
            Num.Vector3 d = host.Position - Client.Character.Position;
            View.TurnToward(d);
            View.Pitch = _guestDressed ? -0.05f : 0;
        }
        if (Me.PendingCount > 0)
        {
            return;
        }
        foreach (string wear in new[] { "woven-hood", "woven-shirt", "woven-trousers", "work-boots", "chest-plate" })
        {
            if (SlotOf(Me.Inventory.Main, wear) is int slot)
            {
                Me.QuickMove(ContainerRef.Main, slot);
                return;
            }
        }
        if (SlotOf(Me.Inventory.Main, "stone-hatchet") is int hatchet)
        {
            Me.MoveItem(ContainerRef.Main, hatchet, ContainerRef.Belt, 2);
            return;
        }
        if (SlotOf(Me.Inventory.Belt, "stone-hatchet") is int held && Me.HeldSlot != held)
        {
            Me.HeldSlot = held;
            _guestDressed = true;
        }
    }

    // The F1 panel (GROUND.md §8.2), checked once in the real game: the host is the game's admin; its console runs help,
    // give, spawn, teleport, noclip and time over the network as a player's would; then a picture of each tab.
    private async Task F1()
    {
        int checks = 0, failed = 0;
        void Check(bool ok, string what)
        {
            checks++;
            failed += ok ? 0 : 1;
            GD.Print($"[tour] f1 check: {(ok ? "ok  " : "FAIL")} {what}");
        }
        ClientConsole console = View.Console;
        GroundUi ui = View.Interface;
        await Until(() => Client.Console().IsAdmin, 10);
        Check(Client.Console().IsAdmin, "the host is the game's admin");
        // Nothing hovered in the pictures, wherever the real mouse is.
        ui.HoldPointer(new Vector2(-10000, -10000));
        if (ui.Screen != GroundScreen.Console)
        {
            ui.ToggleConsole();
        }
        ui.ConsoleTab = ConsoleTab.Console;
        await Seconds(0.3);
        Check(ui.Screen == GroundScreen.Console, "F1 opens the panel");

        console.Submit("help");
        await Seconds(0.3);
        Check(console.Lines.Any(l => l.Text.StartsWith("give <item>", StringComparison.Ordinal)), "help lists the admin commands");
        int rifles = Count("rifle");
        console.Submit("give rifle");
        await Until(() => Count("rifle") > rifles, 5);
        Check(Count("rifle") > rifles, "give rifle: a rifle in the inventory");
        (Num.Vector3 spot, Num.Vector3 towards) = OpenSpot(Offset(40, 30));
        console.Submit(string.Create(CultureInfo.InvariantCulture, $"teleport {spot.X:0.0} {spot.Z:0.0}"));
        await Until(() => new Num.Vector2(Client.Character.Position.X - spot.X, Client.Character.Position.Z - spot.Z).Length() < 1.5f, 5);
        Check(new Num.Vector2(Client.Character.Position.X - spot.X, Client.Character.Position.Z - spot.Z).Length() < 1.5f, "teleport: there");
        View.TurnToward(towards);
        await Settle();
        int wolves = Client.Animals().All.Count(a => a.SpeciesKey == "wolf" && !a.Dead);
        console.Submit("spawn wolf 2");
        await Until(() => Client.Animals().All.Count(a => a.SpeciesKey == "wolf" && !a.Dead) >= wolves + 2, 8);
        Check(Client.Animals().All.Count(a => a.SpeciesKey == "wolf" && !a.Dead) >= wolves + 2, "spawn wolf 2: two wolves in front");
        console.Submit("noclip");
        await Until(() => (Client.Console().Cheats & MoveCheats.Noclip) != 0, 5);
        Check((Client.Console().Cheats & MoveCheats.Noclip) != 0, "noclip: on in the prediction");
        float before = Client.Character.Position.Length();
        long corrections = Client.Stats.Corrections;
        View.Scripted = new InputFrame { Jump = true };
        await Seconds(0.5);
        View.Scripted = null;
        await Seconds(0.2);
        Check(Client.Character.Position.Length() - before > 3, $"noclip: rose {Client.Character.Position.Length() - before:0.0} m, {Client.Stats.Corrections - corrections} corrections");
        console.Submit("noclip");
        await Until(() => (Client.Console().Cheats & MoveCheats.Noclip) == 0, 5);
        await Seconds(1.5);
        Check((Client.Console().Cheats & MoveCheats.Noclip) == 0 && (Client.Character.Flags & MoveFlags.OnGround) != 0, "noclip off: back on the ground");
        console.Submit("time 21");
        await Until(() => Client.Console().Day is { Overridden: true }, 5);
        await Seconds(0.3);
        double hours = View.World?.Sky?.LocalHours ?? -1;
        Check(Math.Abs(hours - 21) < 0.3, $"time 21: the sky says {hours:0.00} h");
        // Day again for the pictures.
        console.Submit("time 10");
        await Seconds(1.0);
        await Settle();
        await Shot("f1-console");
        ui.ConsoleTab = ConsoleTab.Info;
        await Seconds(0.5);
        await Shot("f1-info");
        ui.ConsoleTab = ConsoleTab.Net;
        await Seconds(0.5);
        await Shot("f1-net");
        ui.ConsoleTab = ConsoleTab.Items;
        ui.SelectConsoleItem("rifle");
        await Seconds(0.6);
        await Shot("f1-items");
        console.Run("ents");
        ui.ConsoleTab = ConsoleTab.Tools;
        await Seconds(1.0);
        await Shot("f1-tools");
        GD.Print($"[tour] f1: {checks - failed} of {checks} checks passed");
    }

    // GROUND.md §8.5: the F2 panel on the host, as the admin: the five tabs pictured (two wolves and a small box spawned in
    // front for the hitboxes and the LOOK tab), then the panel closed and the drawings in the world.
    private async Task F2()
    {
        ClientConsole console = View.Console;
        GroundUi ui = View.Interface;
        await Until(() => Client.Console().IsAdmin, 10);
        ui.HoldPointer(new Vector2(-10000, -10000));
        (Num.Vector3 spot, Num.Vector3 towards) = OpenSpot(Offset(60, -20));
        await TeleportTo(spot);
        Face(spot + towards * 8);
        await Settle();
        int wolves = Client.Animals().All.Count(a => a.SpeciesKey == "wolf" && !a.Dead);
        console.Submit("spawn wolf 2");
        await Until(() => Client.Animals().All.Count(a => a.SpeciesKey == "wolf" && !a.Dead) >= wolves + 2, 8);
        console.Submit("spawn small-box");
        await Until(() => Client.Survival().Things.Any(t => t.Deployable(Client.Content)?.Key == "small-box"), 8);
        if (ui.Screen != GroundScreen.Developer)
        {
            ui.ToggleDeveloper();
        }
        ui.DeveloperTab = DeveloperTab.Server;
        await Until(() => Client.Dev().Server != null, 6);
        await Seconds(1.2);
        await Shot("f2-server");
        ui.DeveloperTab = DeveloperTab.Players;
        await Seconds(0.5);
        await Shot("f2-players");
        RemoteThing? box = Client.Survival().Things.FirstOrDefault(t => t.Deployable(Client.Content)?.Key == "small-box");
        if (box != null)
        {
            Face(box.Position + Client.Frame.UpAt(box.Position) * 0.3f);
        }
        ui.DeveloperTab = DeveloperTab.Look;
        await Until(() => Client.Dev().Lines.Count > 3, 6);
        await Seconds(0.5);
        await Shot("f2-look");
        ui.DeveloperTab = DeveloperTab.Draw;
        string[] toggles = { "hitboxes", "tiles", "relevance", "nav", "areas" };
        foreach (string key in toggles)
        {
            View.Developer.ToggleDraw(key);
        }
        await Seconds(0.5);
        await Shot("f2-draw");
        ui.DeveloperTab = DeveloperTab.Log;
        await Seconds(0.8);
        await Shot("f2-log");
        ui.ToggleDeveloper();
        // The wolves wandered while the tabs were pictured: two more, just spawned, for the hitboxes.
        console.Submit("spawn wolf 2");
        Face(spot + towards * 8);
        View.Pitch = -0.2f;
        await Seconds(2.5);
        await Shot("f2-world");
        foreach (string key in toggles)
        {
            View.Developer.ToggleDraw(key);
        }
        GD.Print("[tour] f2: pictured");
    }

    // GROUND.md §6.1: what is dropped together lies in one bag. Five stacks dropped in the open by the landing site, the
    // bag with its prompt, then the bag open beside the inventory.
    private async Task Bags()
    {
        (Num.Vector3 spot, Num.Vector3 towards) = OpenSpot(Landing);
        await TeleportTo(spot);
        Face(spot + towards * 3);
        await Settle();
        Admin("give", "metal-ore 120");
        Admin("give", "cloth 20");
        await Until(() => Count("metal-ore") >= 120 && Count("cloth") >= 20, 5);
        foreach ((string key, int amount) in new[] { ("wood", 200), ("stone", 100), ("fibre", 40), ("metal-ore", 120), ("cloth", 20) })
        {
            if (SlotOf(Me.Inventory.Main, key) is int slot)
            {
                Me.Drop(ContainerRef.Main, slot, Math.Min(amount, Me.Inventory.Main[slot].Count));
                await Seconds(0.25);
            }
        }
        const Hollow.Simulation.World.StaticKind bagKind = Hollow.Simulation.World.StaticKind.DroppedBag;
        await Until(() => Client.Survival().Things.Any(t => t.Kind == bagKind && t.Count >= 5), 5);
        RemoteThing? bag = Client.Survival().Things.Where(t => t.Kind == bagKind)
            .OrderBy(t => Num.Vector3.Distance(t.Position, Client.Character.Position)).FirstOrDefault();
        if (bag == null)
        {
            GD.PushWarning("[tour] the dropped stacks made no bag");
            return;
        }
        await TeleportTo(bag.Position - towards * 1.3f);
        Face(bag.Position + Client.Frame.UpAt(bag.Position) * 0.15f);
        await Seconds(1.0);
        await Shot("bag");
        if (View.Aimed is { Target.Family: TargetFamily.Thing })
        {
            TapAimed();
        }
        await Until(() => Me.LootKind == LootKind.Crate && Me.Loot != null, 4);
        await Seconds(1.0);
        await Shot("bag-open");
        Me.Close();
        View.Interface.Open(GroundScreen.Hud);
        await Seconds(0.3);
    }

    // The shared foundations in the game (GROUND_SYSTEMS.md §3, P0): a code lock fitted to a hut's door offers its owner SET
    // CODE first when E is held on the door (the door's viewer flags say the lock is theirs and has no code yet), and a
    // refused intent shows its line from texts.json: the hut's claim device, opened from outside the wall beside it, is in
    // reach but out of sight.
    private async Task Foundations()
    {
        Admin("give", "code-lock 1");
        await Until(() => Count("code-lock") > 0, 5);
        (Num.Vector3 spot, Num.Vector3 towards) = OpenSpot(Offset(40, -40));
        await TeleportTo(spot);
        View.TurnToward(towards);
        View.Pitch = 0;
        await Seconds(0.6);
        int doors = Doors().Count();
        Admin("raidbase", "1");
        await Until(() => Doors().Count() > doors, 5);
        await Settle();
        if (Doors().OrderBy(d => Num.Vector3.Distance(Centre(d.Frame), Client.Character.Position)).FirstOrDefault() is not { } door)
        {
            GD.PushWarning("[tour] no hut");
            return;
        }
        Num.Vector3 up = Client.Frame.UpAt(Client.Character.Position);
        Num.Vector3 doorAt = Centre(door.Frame), outward = FlatUnit(Client.Character.Position - doorAt);
        await TeleportTo(doorAt + outward * 2.4f);
        Face(doorAt + up * 1.1f);
        await Hold("code-lock", 5);
        await Seconds(0.4);
        View.UseHeld();
        await Until(() => Doors().Any(d => d.Lock is CodeLock && Num.Vector3.Distance(Centre(d.Frame), doorAt) < 0.5f), 5);
        await Seconds(1.0);

        // E held on the door: what its flags allow, SET CODE first; letting go chooses it (the keypad asks the new code).
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.E, PhysicalKeycode = Key.E, Pressed = true });
        await Seconds(0.8);
        await Shot("set-code");
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.E, PhysicalKeycode = Key.E, Pressed = false });
        await Seconds(0.5);
        GD.Print($"[tour] E let go on the door: the keypad {(View.Keypad.Visible ? "asks a new code" : "did not open")}");
        View.Keypad.Close();
        await Seconds(0.3);

        // The claim device from outside the side wall by it (square to the wall, clear of the corners): in reach, out of
        // sight, refused with its line.
        if (Client.Building().World?.Claims.OrderBy(c => Num.Vector3.Distance(c.ZoneTransform.Position, doorAt)).FirstOrDefault() is not { } claim)
        {
            GD.PushWarning("[tour] the hut has no claim device");
            return;
        }
        Num.Vector3 claimAt = claim.ZoneTransform.Position;
        Num.Vector3 side = FlatUnit(Num.Vector3.Cross(outward, up));
        if (Num.Vector3.Dot(side, claimAt - (doorAt - outward * 1.5f)) < 0)
        {
            side = -side;
        }
        await TeleportTo(claimAt + side * 2.0f);
        Face(claimAt + up * 0.8f);
        await Seconds(0.6);
        string? refused = null;
        void Rejected(IntentAnswer answer) => refused = Client.Content.Texts.Line(answer.Outcome);
        Me.IntentRejected += Rejected;
        Me.Interact(claim.Ref, "open");
        await Until(() => refused != null, 3);
        Me.IntentRejected -= Rejected;
        GD.Print($"[tour] the claim device through its wall: {refused ?? "not refused"}");
        await Seconds(0.3);
        await Shot("refusal");
    }

    // Placing anything with its one ghost (GROUND_SYSTEMS.md §6, P2), whatever is held. On a foundation built with the
    // plan, a doorway on its far edge: a furnace's ghost that fits on it, then one pushed into the doorway's wall
    // (refused, with the reason); a garage door's in the doorway (it needs a wall frame); the claim device's on the
    // foundation. At a hut (the raidbase command): a code lock's on its door and a timed charge's on its wall. On the
    // meadow: the Tin Finch's hologram, then the ship placed from its item.
    private async Task Place()
    {
        foreach (string gift in new[]
                 {
                     "building-plan 1", "wood 3000", "furnace 1", "garage-door 1", "claim-device 1", "code-lock 1", "timed-charge 1",
                     "tin-finch 1",
                 })
        {
            Admin("give", gift);
        }
        await Until(() => Count("tin-finch") > 0 && Count("timed-charge") > 0 && Count("building-plan") > 0 && Count("wood") >= 3000, 5);
        (Num.Vector3 spot, Num.Vector3 towards) = OpenSpot(Offset(-60, -20));
        await TeleportTo(spot);
        View.TurnToward(towards);
        View.Pitch = -0.3f;
        await Settle();

        await Hold("building-plan", 5);
        Num.Vector3 ahead = Forward(), up = Client.Frame.UpAt(Client.Character.Position);
        Num.Vector3 right = Num.Vector3.Normalize(Num.Vector3.Cross(ahead, up));
        if (await PlaceAimed("foundation", p => p.Support == null) is not { } foundation)
        {
            return;
        }
        Num.Vector3 c = Centre(foundation);
        if (await PlaceAimed("doorway", p => p.Support == foundation && Num.Vector3.Dot(At(p) - c, ahead) > 1) is not { } doorway)
        {
            return;
        }
        Num.Vector3 opening = Centre(doorway);

        // The furnace: in the middle of the foundation, then (from its side) pushed into the doorway's wall beside the
        // opening.
        await TeleportTo(c - ahead * 2.6f + right * 0.5f);
        await Hold("furnace", 5);
        Face(c);
        await Seconds(0.8);
        await Shot("furnace-fits");
        Num.Vector3 byTheWall = opening - ahead * 0.3f + right * 1.0f;
        await TeleportTo(byTheWall + right * 2.4f - ahead * 0.4f);
        Face(byTheWall);
        await Seconds(0.8);
        await Shot("furnace-refused");

        // The garage door aimed into the doorway from outside: a doorway is too narrow for it.
        await TeleportTo(opening + ahead * 2.8f);
        await Hold("garage-door", 5);
        Face(opening + up * 1.1f);
        await Seconds(0.8);
        await Shot("garage-door");

        // The claim device on the foundation, from a few steps back (it stands 1.8 m tall).
        await TeleportTo(c - ahead * 3.8f - right * 0.4f);
        await Hold("claim-device", 5);
        Face(c + right * 0.6f);
        await Seconds(0.8);
        await Shot("cupboard");

        // A stone hut with a sheet metal door: a code lock's ghost on its door, a timed charge's on its side wall.
        (Num.Vector3 hutSpot, Num.Vector3 hutTowards) = OpenSpot(Offset(-60, 20));
        await TeleportTo(hutSpot);
        View.TurnToward(hutTowards);
        View.Pitch = 0;
        await Seconds(0.6);
        int doors = Doors().Count();
        Admin("raidbase", "2");
        await Until(() => Doors().Count() > doors, 5);
        await Settle();
        if (Doors().OrderBy(d => Num.Vector3.Distance(Centre(d.Frame), Client.Character.Position)).FirstOrDefault() is not { } door)
        {
            GD.PushWarning("[tour] no hut");
            return;
        }
        Num.Vector3 doorAt = Centre(door.Frame), outward = FlatUnit(Client.Character.Position - doorAt);
        await TeleportTo(doorAt + outward * 2.2f);
        await Hold("code-lock", 5);
        Face(door.LockSpot.Position);
        await Seconds(0.8);
        await Shot("code-lock");
        Piece? wall = Client.Building().World?.AllPieces.Where(p => p.Def.Key == "wall")
            .OrderBy(p => Num.Vector3.Distance(Centre(p), doorAt)).FirstOrDefault();
        if (wall != null)
        {
            Num.Vector3 wallAt = Centre(wall), normal = FlatUnit(wallAt - (doorAt - outward * 1.5f));
            await TeleportTo(wallAt + normal * 1.6f);
            await Hold("timed-charge", 5);
            Face(wallAt + up * 1.3f);
            await Seconds(0.8);
            await Shot("charge");
        }

        // The Tin Finch: its hologram on the meadow, then the ship placed there.
        (Num.Vector3 pad, Num.Vector3 padTowards) = OpenSpot(Offset(-20, -70));
        await TeleportTo(pad);
        View.TurnToward(padTowards);
        await Settle();
        await Hold("tin-finch", 5);
        Num.Vector3 feet = Client.Character.Position;
        Face(Client.Placement().Ground?.Settle(feet + padTowards * 6f) ?? feet + padTowards * 6f);
        await Seconds(0.8);
        await Shot("tin-finch");
        int ships = Client.Ships().All.Count();
        View.UseHeld();
        await Until(() => Client.Ships().All.Count() > ships, 5);
        await Seconds(1.0);
        View.Pitch = Math.Max(View.Pitch, -0.2f);
        await Seconds(0.3);
        await Shot("tin-finch-placed");
        GD.Print($"[tour] place: the Tin Finch {(Client.Ships().All.Count() > ships ? "placed" : "not placed")}");
    }

    // Interaction in the game (GROUND_SYSTEMS.md §5, P1), through the keys: the prompt on a hut's door with a fresh code lock
    // and E held there (SET CODE first); the tool cupboard's options inside; arrows shot into the hut's wall, one taken
    // back with E; an arrow in a deer; a garage door rolled up and shut with E through its opening.
    private async Task Interaction()
    {
        foreach (string gift in new[] { "code-lock 1", "garage-door 1", "building-plan 1", "wood 2000", "bow 1", "arrow 24" })
        {
            Admin("give", gift);
        }
        await Until(() => Count("code-lock") > 0 && Count("garage-door") > 0 && Count("bow") > 0 && Count("arrow") >= 24 && Count("wood") >= 2000, 5);
        (Num.Vector3 spot, Num.Vector3 towards) = OpenSpot(Offset(-70, 30));
        await TeleportTo(spot);
        View.TurnToward(towards);
        View.Pitch = 0;
        await Seconds(0.6);
        int doors = Doors().Count();
        Admin("raidbase", "1");
        await Until(() => Doors().Count() > doors, 5);
        await Settle();
        if (Doors().OrderBy(d => Num.Vector3.Distance(Centre(d.Frame), Client.Character.Position)).FirstOrDefault() is not { } door)
        {
            GD.PushWarning("[tour] no hut");
            return;
        }
        Num.Vector3 up = Client.Frame.UpAt(Client.Character.Position);
        Num.Vector3 doorAt = Centre(door.Frame), outward = FlatUnit(Client.Character.Position - doorAt);
        Num.Vector3 side = FlatUnit(Num.Vector3.Cross(outward, up)), middle = doorAt - outward * 1.5f;

        // The prompt and E held on the door with a fresh code lock: its owner sets the code first.
        await TeleportTo(doorAt + outward * 2.2f);
        await Hold("code-lock", 5);
        Face(door.LockSpot.Position);
        await Seconds(0.4);
        View.UseHeld();
        await Until(() => Doors().Any(d => d.Lock is CodeLock && Num.Vector3.Distance(Centre(d.Frame), doorAt) < 0.5f), 5);
        Me.HeldSlot = 5;
        Face(doorAt + up * 1.1f);
        await Seconds(1.0);
        GD.Print($"[tour] the door's prompt: {PromptLine()}");
        await Shot("prompt");
        Press(Godot.Key.E, true);
        await Seconds(0.8);
        await Shot("code-lock");
        Press(Godot.Key.Escape, true);
        Press(Godot.Key.Escape, false);
        Press(Godot.Key.E, false);
        await Seconds(0.4);

        // Inside: the tool cupboard's options (the hut lists its builder).
        if (Client.Building().World?.Claims.OrderBy(c => Num.Vector3.Distance(c.ZoneTransform.Position, middle)).FirstOrDefault() is { } cupboard)
        {
            Num.Vector3 at = cupboard.ZoneTransform.Position;
            await TeleportTo(middle + FlatUnit(middle - at) * 0.6f);
            Face(at + up * 0.9f);
            await Seconds(0.8);
            GD.Print($"[tour] the cupboard's prompt: {PromptLine()}");
            Press(Godot.Key.E, true);
            await Seconds(0.8);
            await Shot("cupboard");
            Press(Godot.Key.Escape, true);
            Press(Godot.Key.Escape, false);
            Press(Godot.Key.E, false);
            await Seconds(0.4);
        }

        // Arrows into the hut's side wall from 7 m; one stuck taken back with E.
        Num.Vector3 wall = middle + side * 1.6f + up * 1.3f;
        await TeleportTo(middle + side * 8.5f);
        await Hold("bow", 4);
        int arrows = Client.Arrows().All.Count;
        for (int shot = 0; shot < 4 && Client.Arrows().All.Count < arrows + 2; shot++)
        {
            await Loose(wall + up * (0.25f * shot) - outward * (0.35f * shot - 0.5f));
        }
        await Seconds(0.5);
        GD.Print($"[tour] arrows stuck in the wall: {Client.Arrows().All.Count - arrows}");
        if (Client.Arrows().All.Where(a => a.Placed).OrderBy(a => Num.Vector3.Distance(a.Pose.Position, wall)).FirstOrDefault() is { } stuck)
        {
            Num.Vector3 nock = SpentArrowNock(stuck);
            await TeleportTo(middle + side * 3.3f + outward * 0.4f);
            Face(nock + (stuck.Pose.Position - nock) * 0.35f);
            await Seconds(0.8);
            GD.Print($"[tour] at the arrow: {PromptLine()}");
            await Shot("arrow-wall");
            int carried = Count("arrow");
            Press(Godot.Key.E, true);
            await Seconds(0.08);
            Press(Godot.Key.E, false);
            await Until(() => Count("arrow") > carried, 3);
            GD.Print($"[tour] tapped E on the arrow: {(Count("arrow") > carried ? "taken back" : "not taken")}");
            await Seconds(0.3);
            await Shot("arrow-taken");
        }

        // A deer 5 m ahead, met by an arrow loosed as it comes (the bow drawn first, then put away so it hides nothing); the
        // arrow turns with the deer as it turns to run.
        (spot, towards) = OpenSpot(Offset(-40, 70));
        await TeleportTo(spot);
        View.TurnToward(towards);
        View.Pitch = 0;
        await Settle();
        for (int attempt = 0; attempt < 6; attempt++)
        {
            await Hold("bow", 4);
            int before = Client.Arrows().All.Count;
            View.Scripted = new InputFrame { Secondary = true };
            await Seconds(1.1);
            var known = Client.Animals().All.Select(a => a.Id).ToHashSet();
            Command?.Invoke($"spawn deer {Client.Name} 1 6");
            await Until(() => Client.Animals().All.Any(a => !known.Contains(a.Id)), 3);
            if (Client.Animals().All.FirstOrDefault(a => !known.Contains(a.Id)) is not { } deer)
            {
                View.Scripted = null;
                continue;
            }
            // At the deer's nearest hit shape, a little into it.
            Num.Vector3 eye = Client.Character.Position + Client.Frame.UpAt(Client.Character.Position) * Client.EyeHeight;
            Num.Vector3 aim = deer.Position + Client.Frame.UpAt(deer.Position) * 0.9f;
            if (Client.Targets().TargetViewOf(TargetFamily.Animal) is { } animals && animals.TryNearest(new TargetRef(TargetFamily.Animal, deer.Id.Value), eye, out Num.Vector3 near))
            {
                aim = near + Num.Vector3.Normalize(near - eye) * 0.08f;
            }
            Face(aim);
            View.Scripted = new InputFrame { Secondary = true, Primary = true, PrimaryPressed = true };
            await Seconds(0.1);
            View.Scripted = null;
            Me.HeldSlot = 5;
            await Until(() => Client.Arrows().All.Count > before, 1.0);
            GD.Print($"[tour] the deer, try {attempt + 1}: {(Client.Arrows().All.Count > before ? "an arrow stuck" : "no arrow stuck")} ({(deer.HurtCount > 0 ? "hit" : "not hit")})");
            if (Client.Arrows().All.Count > before)
            {
                await Seconds(0.3);
                await Shot("arrow-deer");
                break;
            }
        }
        View.Scripted = null;

        // A garage door in a wall frame on a foundation, rolled up, shut with a tap of E through its opening.
        (spot, towards) = OpenSpot(Offset(60, 60));
        await TeleportTo(spot);
        View.TurnToward(towards);
        View.Pitch = -0.3f;
        await Settle();
        await Hold("building-plan", 5);
        Num.Vector3 ahead = Forward();
        if (await PlaceAimed("foundation", p => p.Support == null) is not { } floor)
        {
            GD.PushWarning("[tour] no foundation for the garage");
            return;
        }
        Num.Vector3 c1 = Centre(floor);
        if (await PlaceAimed("wall-frame", p => p.Support == floor && Num.Vector3.Dot(At(p) - c1, ahead) < -1) is not { } frame)
        {
            GD.PushWarning("[tour] no wall frame for the garage");
            return;
        }
        Num.Vector3 frameAt = Centre(frame), front = FlatUnit(Client.Character.Position - frameAt);
        await TeleportTo(frameAt + front * 3.0f);
        Face(frameAt + up * 1.4f);
        await Hold("garage-door", 5);
        await Seconds(0.4);
        View.UseHeld();
        await Until(() => Doors().Any(d => d.Frame == frame), 5);
        if (Doors().FirstOrDefault(d => d.Frame == frame) is not { } garage)
        {
            GD.PushWarning("[tour] the garage door wasn't fitted");
            return;
        }
        Me.HeldSlot = 5;
        Me.Interact(garage.Ref, "door-open");
        await Until(() => garage.Open, 3);
        await Seconds(1.4);
        Face(frameAt + up * 1.2f);
        await Seconds(0.5);
        GD.Print($"[tour] the open garage door: {PromptLine()}");
        await Shot("garage-open");
        Press(Godot.Key.E, true);
        await Seconds(0.08);
        Press(Godot.Key.E, false);
        await Until(() => !garage.Open, 3);
        // The curtain unrolls over three seconds (the kit's animation): halfway down.
        await Seconds(1.5);
        GD.Print($"[tour] tapped E through the opening: the garage door {(garage.Open ? "stayed open" : "is shutting")}");
        await Shot("garage-closing");
    }

    // A key pressed or let go, as the player's own.
    private static void Press(Godot.Key key, bool pressed) =>
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed });

    // The bow drawn a second and loosed at a point.
    private async Task Loose(Num.Vector3 at)
    {
        Face(at);
        View.Scripted = new InputFrame { Secondary = true };
        await Seconds(1.1);
        View.Scripted = new InputFrame { Secondary = true, Primary = true, PrimaryPressed = true };
        await Seconds(0.1);
        View.Scripted = null;
        await Seconds(0.5);
    }

    private static Num.Vector3 SpentArrowNock(RemoteArrow arrow) => Hollow.Simulation.World.SpentArrow.Shaft(arrow.Pose).Nock;

    // What the interact prompt says now.
    private string PromptLine() => View.Aimed is { } aim ? $"{aim.Target} {(aim.Primary >= 0 ? Client.Content.Interactions.Verb(aim.Options[aim.Primary].Verb)?.Key : "-")} ({aim.Options.Count} options)" : "nothing";

    // ---------------------------------------------------------------- helpers

    private void Admin(string command, string args) => Command?.Invoke($"{command} {Client.Name} {args}".Trim());

    private static TargetRef Thing(Hollow.Simulation.Entities.EntityId id) => new(TargetFamily.Thing, id.Value);

    // The verb a tap of E would do now (the prompt's), by its key; null for none.
    private string? AimedVerb() =>
        View.Aimed is { Primary: >= 0 } aim ? Client.Content.Interactions.Verb(aim.Options[aim.Primary].Verb)?.Key : null;

    // A tap of E now, as the player's: the aimed thing's primary verb.
    private void TapAimed()
    {
        if (View.Aimed is { Primary: >= 0 } aim)
        {
            Me.Interact(aim.Target, aim.Options[aim.Primary].Verb);
        }
    }

    // What an item places (placement.json), or null.
    private Hollow.Simulation.Placement.Placeable? Placeable(string itemKey) => Client.Content.Item(itemKey)?.Places;

    private int Count(string key) => Client.Content.Item(key) is { } def ? Me.Inventory.Count(def) : 0;

    private int? SlotOf(Hollow.Simulation.Items.Container c, string key)
    {
        ushort id = Client.Content.Item(key)?.Id ?? 0;
        for (int i = 0; i < c.Size; i++)
        {
            if (c[i].Item == id && id != 0)
            {
                return i;
            }
        }
        return null;
    }

    private void MoveToBelt(string key, int belt)
    {
        if (SlotOf(Me.Inventory.Main, key) is int slot)
        {
            Me.MoveItem(ContainerRef.Main, slot, ContainerRef.Belt, belt);
        }
    }

    private Num.Vector3 Forward() => Hollow.Simulation.Movement.CharacterMotor.ViewDirection(View.Yaw, 0, Client.Frame.UpAt(Client.Character.Position));

    // The nearest natural object of a kind, preferring ones with no tree within 3 m (in the open).
    private NaturalObject? Nearest(string key, Num.Vector3 near, float radius, bool open)
    {
        if (Client.Survival().Nature is not { } nature || Client.Survival().Natural is not { } natural || Client.Content.Natural(key) is not { } kind)
        {
            return null;
        }
        var all = new List<NaturalObject>();
        nature.Query(near, radius, all);
        IEnumerable<NaturalObject> candidates = all.Where(o => o.Kind == kind.Index && !natural.IsDepleted(o.Id));
        if (open)
        {
            var solid = all.Where(o => Client.Content.Natural(o.Kind)!.IsSolid).ToList();
            candidates = candidates.Where(o => !solid.Any(s => s.Id != o.Id && Num.Vector3.Distance(s.Position, o.Position) < 3.2f));
        }
        NaturalObject[] list = candidates.OrderBy(o => Num.Vector3.Distance(o.Position, near)).Take(1).ToArray();
        return list.Length > 0 ? list[0] : null;
    }

    // A spot near a point with nothing solid or bushy within 6 m of it and of the ground 3 m ahead (in the direction
    // returned), for pictures of the open meadow.
    // The landing site, and a point east and south of it along the ground (docs/WHOLE_WORLD.md §10.5: the tour finds its
    // spots on the ball, not on a plane through its centre).
    private Num.Vector3 Landing => Client.Placement().Ground!.Value.On(Client.Zone!.LandingDirection);

    private Num.Vector3 Offset(float east, float south) => Client.Placement().Ground!.Value.Offset(Landing, east, south);

    private (Num.Vector3 Spot, Num.Vector3 Towards) OpenSpot(Num.Vector3 near)
    {
        var all = new List<NaturalObject>();
        Client.Survival().Nature!.Query(near, 60, all);
        var blockers = all.Where(o => Client.Content.Natural(o.Kind)!.Class is NaturalClass.Tree or NaturalClass.Boulder or NaturalClass.Bush or NaturalClass.Node).ToList();
        Hollow.Simulation.GroundProbe ground = Client.Placement().Ground!.Value;
        for (int ring = 0; ring < 12; ring++)
        {
            for (int k = 0; k < 16; k++)
            {
                float a = k * MathF.Tau / 16;
                Num.Vector3 spot = ground.Offset(near, MathF.Cos(a) * ring * 4f, MathF.Sin(a) * ring * 4f);
                (Num.Vector3 north, Num.Vector3 east) = Facing.Compass(Client.Frame.UpAt(spot));
                Num.Vector3 towards = Num.Vector3.Normalize(east * -MathF.Sin(a + 1) - north * MathF.Cos(a + 1));
                Num.Vector3 front = spot + towards * 3;
                if (!blockers.Any(o => Flat(o.Position - spot) < 6 || Flat(o.Position - front) < 6))
                {
                    return (spot, towards);
                }
            }
        }
        return (near, Forward());
    }

    // Distances between nearby points (straight: as good as along the ground over metres).
    private static float Flat(Num.Vector3 d) => d.Length();

    // A point a distance from an object, on the side towards the landing site (usually the open meadow), along the ground.
    private Num.Vector3 Away(Num.Vector3 target, float distance)
    {
        Num.Vector3 up = Client.Frame.UpAt(target), back = Landing - target;
        back -= up * Num.Vector3.Dot(back, up);
        back = back.LengthSquared() > 0.01f ? Num.Vector3.Normalize(back) : Facing.Compass(up).North;
        return target + back * distance;
    }

    private void Face(Num.Vector3 point)
    {
        Num.Vector3 eye = Client.Character.Position + Client.Frame.UpAt(Client.Character.Position) * Client.EyeHeight;
        View.LookToward(point - eye);
    }

    private async Task TeleportTo(Num.Vector3 point)
    {
        Command?.Invoke(string.Create(CultureInfo.InvariantCulture, $"teleport {Client.Name} {point.X:0.00} {point.Z:0.00}"));
        await Until(() => new Num.Vector2(Client.Character.Position.X - point.X, Client.Character.Position.Z - point.Z).Length() < 1.5f, 5);
        await Seconds(0.5);
    }

    // Until the world around has streamed in again.
    private async Task Settle()
    {
        await Seconds(0.5);
        await Until(() => View.World?.IsReady ?? true, 40);
    }

    private async Task Until(Func<bool> done, double seconds)
    {
        double start = Time.GetTicksMsec() / 1000.0;
        while (!done() && Time.GetTicksMsec() / 1000.0 - start < seconds)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private async Task Seconds(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

    private async Task Shot(string name, int frames = 3)
    {
        // A far teleport is an arrival (GROUND.md §4.3.1): its short loading screen goes first.
        await Until(() => !View.LoadingShown, 30);
        for (int i = 0; i < frames; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Image image = GetViewport().GetTexture().GetImage();
        string path = Path.Combine(Folder, name + ".jpg");
        Error error = image.SaveJpg(path, 0.9f);
        GD.Print(error == Error.Ok ? $"[tour] saved {path}" : $"[tour] could not save {path}: {error}");
    }
}
