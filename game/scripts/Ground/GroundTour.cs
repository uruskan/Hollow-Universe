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
/// and the blast, a firefight at the hut, the death screen, night by a fire) and
/// quits; with <c>--tour guest</c>, a second client that dresses in what it is given, takes up its hatchet and
/// faces the host, and fires a rifle it is given at the nearest building wall. Pictures are 1600 × 900 JPEG at the
/// window's size.
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
            ("death", Death), ("night", Night),
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
        NaturalObject? bush = Nearest("red-berry-bush", Num.Vector3.Zero, 120, open: true) ?? Nearest("blue-berry-bush", Num.Vector3.Zero, 120, open: true);
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
        if (View.Aimed is { Kind: InteractionKind.PickNatural } aimed)
        {
            Client.Interact(aimed);
        }
        await Until(() => Client.Natural?.IsDepleted(b.Id) == true, 3);
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

    // A tree chopped with the stone hatchet until it falls.
    private async Task Chop()
    {
        Num.Vector3 from = Client.Character.Position;
        NaturalObject? tree = null;
        // A birch or a broadleaf tree in the open if there is one near (lighter to see), else any tree.
        foreach (string key in new[] { "birch-tree", "broadleaf-tree", "pine-tree", "fir-tree" })
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
        View.Scripted = new InputFrame { Primary = true };
        // Aim at the weak spot once it shows (Rust's chopping); two hits in, the picture.
        double started = Time.GetTicksMsec() / 1000.0;
        int wood = Count("wood"), hits = 0;
        bool shot = false;
        while (Time.GetTicksMsec() / 1000.0 - started < 60 && Client.Natural?.IsDepleted(trunk.Id) != true)
        {
            if (Count("wood") > wood)
            {
                wood = Count("wood");
                hits++;
            }
            if (Client.Natural?.WeakSpot(trunk, kind) is { } spot)
            {
                Face(spot);
                if (!shot && hits >= 2)
                {
                    await Seconds(0.45);
                    await Shot("chop");
                    shot = true;
                }
            }
            await Seconds(0.05);
        }
        View.Scripted = null;
        if (!shot)
        {
            await Shot("chop");
        }
        await Seconds(2.0);
        await Shot("chop-falling");
        await Seconds(6.0);
        Face(trunk.Position + trunk.Up * 1.5f);
        await Seconds(0.5);
        await Shot("chopped");
    }

    // An outcrop broken with the stone pickaxe down to its last stage.
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
        double started = Time.GetTicksMsec() / 1000.0;
        while (Time.GetTicksMsec() / 1000.0 - started < 40 && Client.Natural!.Stage(rock, kind) < 2 && !Client.Natural.IsDepleted(rock.Id))
        {
            if (Client.Natural.WeakSpot(rock, kind) is { } spot)
            {
                Face(spot);
            }
            await Seconds(0.05);
        }
        View.Scripted = null;
        await Seconds(0.6);
        Face(rock.Position + rock.Up * 0.4f);
        await Seconds(0.3);
        await Shot("outcrop-broken");
        Me.HeldSlot = 2;
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

    // ---------------------------------------------------------------- building and raiding (G3, G5)

    // A base being started in the open: two foundations and three walls, and the plan's ghost of a doorway in front.
    private async Task Build()
    {
        Admin("give", "building-plan 1");
        Admin("give", "wood 4000");
        Admin("give", "stone 2000");
        await Until(() => Count("building-plan") > 0 && Count("wood") >= 4000, 5);
        (Num.Vector3 spot, Num.Vector3 towards) = OpenSpot(new Num.Vector3(45, 0, -35));
        await TeleportTo(spot);
        View.Yaw = MathF.Atan2(-towards.X, -towards.Z);
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
        (Num.Vector3 spot, Num.Vector3 towards) = OpenSpot(new Num.Vector3(-40, 0, 45));
        await TeleportTo(spot);
        View.Yaw = MathF.Atan2(-towards.X, -towards.Z);
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
        Me.SetCode(locked, "2580");
        await Seconds(0.6);
        // The keypad, two digits in.
        View.ShowKeypad(locked, set: false);
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
        Piece? wall = Client.Building?.AllPieces.Where(p => p.Def.Key == "wall")
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
        await Until(() => Client.Explosives.Any(e => e.State == ExplosiveState.Stuck), 5);
        if (Client.Explosives.FirstOrDefault(e => e.State == ExplosiveState.Stuck) is not { } charge)
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
        Client.Detonated += Blast;
        await TeleportTo(charge.ArcStart + normal * 9f + Num.Vector3.Normalize(Num.Vector3.Cross(normal, up)) * 3f);
        Face(charge.ArcStart + up * 0.8f);
        await Until(() => blasted, 12);
        Client.Detonated -= Blast;
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
        Piece? wall = Client.Building?.AllPieces.Where(p => p.Def.Key == "wall")
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
        int ships = Client.Ships.Count();
        if (Client.Content.Item("tin-finch") is { } finch)
        {
            Me.Craft(finch.Id);
        }
        await Until(() => Client.Ships.Count() > ships, 140);
        if (Client.Ships.OrderBy(s => Num.Vector3.Distance(s.Position, Client.Character.Position)).FirstOrDefault() is not { Def: { } def } ship)
        {
            GD.PushWarning("[tour] no ship rolled out");
            return;
        }
        await Seconds(1.0);
        // To the hatch: open it, and the prompt to board.
        Num.Vector3 hatch = ship.PointOf(def.Hatch);
        Num.Vector3 side = FlatUnit(hatch - ship.Position);
        await TeleportTo(hatch + side * 2.4f);
        Face(hatch);
        await Seconds(0.6);
        if (!ship.Has(Hollow.Net.Protocol.ShipNetFlags.HatchOpen))
        {
            Me.ShipHatch(ship.Id);
            await Until(() => ship.Hatch > 0.95f, 4);
        }
        // The tank filled with the lamp oil left over from the recipe.
        Me.RefuelShip(ship.Id);
        await Seconds(0.5);
        Face(ship.PointOf(def.Hatch));
        await Seconds(0.4);
        await Shot("ship-boarding");
        Me.Board(ship.Id);
        await Until(() => Client.Piloting, 5);
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
        await Pilot(new ShipInput { Vertical = -1 }, 12, () => Client.OwnShip.Has(ShipFlags.Landed));
        await Pilot(default, 1.5);
        View.Scripted = null;
        await Shot("ship-landed", frames: 0);
        Me.ExitShip();
        await Until(() => !Client.Piloting, 5);
        await Seconds(0.8);
    }

    // The vehicle workbench put down 2.2 m ahead of a spot near `near`, facing `towards`: tried where the bench's ground
    // and its bay's are flat (under 12°) and nothing solid stands within 4 m of the bay, until the server lets it stand.
    private async Task<bool> PlaceBench(Num.Vector3 near, Num.Vector3 towards)
    {
        Num.Vector3 up0 = Client.Frame.UpAt(near);
        var solid = new List<NaturalObject>();
        Client.Nature?.Query(near.X - 60, near.Z - 60, near.X + 60, near.Z + 60, solid);
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
                View.Yaw = MathF.Atan2(-towards.X, -towards.Z);
                View.Pitch = -0.2f;
                await Settle();
                Num.Vector3 up = Client.Frame.UpAt(Client.Character.Position);
                // Within the placement reach of the eye (3 m).
                Num.Vector3 at = Client.Character.Position + Forward() * 2.2f;
                ActionResult? refused = null;
                void Refused(IntentAnswer answer) => refused = answer.Result;
                Me.IntentRejected += Refused;
                if (SlotOf(Me.Inventory.Main, "vehicle-workbench") is int slot && Client.AimGround(at + up * 2, -up, 4) is { } ground)
                {
                    Me.Place(ContainerRef.Main, slot, ground, View.Yaw);
                }
                await Until(() => Client.Things.Any(t => t.Deployable(Client.Content)?.Key == "vehicle-workbench") || refused != null, 2);
                Me.IntentRejected -= Refused;
                if (refused != null)
                {
                    GD.Print($"[tour] the vehicle workbench was refused there: {refused}");
                }
                if (Client.Things.Any(t => t.Deployable(Client.Content)?.Key == "vehicle-workbench"))
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
        Num.Vector3? Ground(Num.Vector3 q) => Client.AimGround(q + up * 6, -up, 14);
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
        while (Client.Piloting && Time.GetTicksMsec() / 1000.0 - start < seconds)
        {
            ShipState s = Client.OwnShip;
            Num.Vector3 up = Client.Frame.UpAt(s.Position);
            Num.Vector3 to = target - s.Position;
            Num.Vector3 flat = to - up * Num.Vector3.Dot(to, up);
            float distance = flat.Length();
            float speed = s.Velocity.Length();
            if (distance < arrive && speed < 4)
            {
                break;
            }
            float over = OverGround(s.Origin(Client.OwnShipDef!.CentreOfMass));
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
        while (Client.Piloting)
        {
            ShipState s = Client.OwnShip;
            Num.Vector3 up = Client.Frame.UpAt(s.Position);
            Num.Vector3 to = face - s.Position;
            Num.Vector3 flat = to - up * Num.Vector3.Dot(to, up);
            float over = OverGround(s.Origin(Client.OwnShipDef!.CentreOfMass));
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
        Client.Nature?.Query(near.X - 50, near.Z - 50, near.X + 50, near.Z + 50, all);
        List<NaturalObject> solid = all.Where(o => Client.Content.Natural(o.Kind) is { IsSolid: true }).ToList();
        List<Num.Vector3> pieces = Client.Building?.AllPieces.Select(Centre).ToList() ?? new List<Num.Vector3>();
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
        Piece? wall = Client.Building?.AllPieces.Where(p => p.Def.Key == "wall").OrderBy(p => Num.Vector3.Distance(Centre(p), me)).FirstOrDefault();
        return wall != null ? Centre(wall) + Client.Frame.UpAt(me) * 1.3f : null;
    }

    private IEnumerable<Door> Doors() =>
        Client.Building?.AllPieces.Where(p => p.Door != null).Select(p => p.Door!) ?? Enumerable.Empty<Door>();

    private static Num.Vector3 Centre(Piece piece) => piece.Structure.Frame.ToZone(piece.Pose).Position;

    private static Num.Vector3 At(PlacementPlan plan) => plan.ZoneTransform.Position;

    private static Num.Vector3 FlatUnit(Num.Vector3 d)
    {
        d.Y = 0;
        return d.LengthSquared() > 1e-6f ? Num.Vector3.Normalize(d) : new Num.Vector3(0, 0, 1);
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
        if (!await AimAt(key, want) || Client.Building is not { } world)
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
        PieceDef def = data.Wheel[index];
        Num.Vector3 up = Client.Frame.UpAt(Client.RenderPosition);
        Num.Vector3 eye = Client.RenderPosition + up * Client.EyeHeight;
        float bestCost = float.MaxValue, bestYaw = View.Yaw, bestPitch = View.Pitch;
        for (float dy = -1.2f; dy <= 1.2f; dy += 0.03f)
        {
            for (float pitch = -1.2f; pitch <= 0.3f; pitch += 0.03f)
            {
                Num.Vector3 dir = CharacterMotor.ViewDirection(View.Yaw + dy, pitch, up);
                if (Client.PlanPiece(def, eye, dir) is { Valid: true } plan && want(plan))
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

    private async Task Death()
    {
        Admin("kill", "");
        await Until(() => Me.IsDead, 5);
        await Seconds(1.5);
        await Shot("death");
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
        (Num.Vector3 spot, Num.Vector3 towards) = OpenSpot(_bush is { } b ? Away(b.Position, 7f) : Num.Vector3.Zero);
        await TeleportTo(spot);
        View.Yaw = MathF.Atan2(-towards.X, -towards.Z);
        View.Pitch = -0.25f;
        await Settle();
        // The fire two metres ahead, lit with wood.
        Num.Vector3 at = Client.Character.Position + Forward() * 2.2f;
        if (SlotOf(Me.Inventory.Main, "campfire") is int fire && Client.AimGround(at + Client.Frame.UpAt(at) * 2, -Client.Frame.UpAt(at), 4) is { } ground)
        {
            Me.Place(ContainerRef.Main, fire, ground, View.Yaw);
            await Until(() => Client.Things.Any(t => t.Deployable(Client.Content)?.Key == "campfire"), 5);
            RemoteThing campfire = Client.Things.First(t => t.Deployable(Client.Content)?.Key == "campfire");
            Me.Open(campfire.Id);
            await Until(() => Me.LootKind == LootKind.Device, 5);
            if (SlotOf(Me.Inventory.Main, "wood") is int wood)
            {
                Me.MoveItem(ContainerRef.Main, wood, ContainerRef.Loot, 0, 60);
                await Seconds(0.5);
            }
            Me.Toggle(campfire.Id);
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

    private void Guest()
    {
        if (Client.State != ClientState.Playing || !Client.HasCharacter || Me.IsDead)
        {
            return;
        }
        if (GuestRifle())
        {
            return;
        }
        // Keep facing the host.
        if (Client.Entities.FirstOrDefault() is { } host)
        {
            Num.Vector3 d = host.Position - Client.Character.Position;
            View.Yaw = MathF.Atan2(-d.X, -d.Z);
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

    // ---------------------------------------------------------------- helpers

    private void Admin(string command, string args) => Command?.Invoke($"{command} {Client.Name} {args}".Trim());

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
        if (Client.Nature == null || Client.Content.Natural(key) is not { } kind)
        {
            return null;
        }
        var all = new List<NaturalObject>();
        Client.Nature.Query(near.X - radius, near.Z - radius, near.X + radius, near.Z + radius, all);
        IEnumerable<NaturalObject> candidates = all.Where(o => o.Kind == kind.Index && !Client.Natural!.IsDepleted(o.Id));
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
    private (Num.Vector3 Spot, Num.Vector3 Towards) OpenSpot(Num.Vector3 near)
    {
        var all = new List<NaturalObject>();
        Client.Nature!.Query(near.X - 60, near.Z - 60, near.X + 60, near.Z + 60, all);
        var blockers = all.Where(o => Client.Content.Natural(o.Kind)!.Class is NaturalClass.Tree or NaturalClass.Boulder or NaturalClass.Bush or NaturalClass.Node).ToList();
        for (int ring = 0; ring < 12; ring++)
        {
            for (int k = 0; k < 16; k++)
            {
                float a = k * MathF.Tau / 16;
                Num.Vector3 spot = near + new Num.Vector3(MathF.Cos(a), 0, MathF.Sin(a)) * (ring * 4f);
                Num.Vector3 towards = Num.Vector3.Normalize(new Num.Vector3(-MathF.Sin(a + 1), 0, MathF.Cos(a + 1)));
                Num.Vector3 front = spot + towards * 3;
                if (!blockers.Any(o => Flat(o.Position - spot) < 6 || Flat(o.Position - front) < 6))
                {
                    return (spot, towards);
                }
            }
        }
        return (near, Forward());
    }

    private static float Flat(Num.Vector3 d) => new Num.Vector2(d.X, d.Z).Length();

    // A point a distance from an object, on the side towards the landing site (usually the open meadow).
    private Num.Vector3 Away(Num.Vector3 target, float distance)
    {
        Num.Vector3 back = Num.Vector3.Zero - target;
        back.Y = 0;
        back = back.LengthSquared() > 0.01f ? Num.Vector3.Normalize(back) : new Num.Vector3(0, 0, 1);
        return target + back * distance;
    }

    private void Face(Num.Vector3 point)
    {
        Num.Vector3 eye = Client.Character.Position + Client.Frame.UpAt(Client.Character.Position) * Client.EyeHeight;
        Num.Vector3 up = Client.Frame.UpAt(eye), d = point - eye;
        Num.Vector3 flat = d - up * Num.Vector3.Dot(d, up);
        View.Yaw = MathF.Atan2(-flat.X, -flat.Z);
        View.Pitch = MathF.Atan2(Num.Vector3.Dot(d, up), flat.Length());
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
