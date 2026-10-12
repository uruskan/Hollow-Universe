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
using Hollow.Simulation.Content;
using Hollow.Simulation.Items;
using Hollow.Simulation.Nature;
using HollowUniverse.UI;
using HollowUniverse.UI.Model;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

/// <summary>
/// The scripted screenshot run of the ground game (GROUND.md §5, <c>--tour &lt;folder&gt;</c>): on the host, a walk
/// through the game at the landing site that saves one picture per moment (the HUD by berry bushes, picking berries,
/// the inventory, crafting, the other player, chopping a tree and after, the death screen, night by a fire) and
/// quits; with <c>--tour guest</c>, a second client that dresses in what it is given, takes up its hatchet and
/// faces the host. Pictures are 1600 × 900 JPEG at the window's size.
/// </summary>
public partial class GroundTour : Node
{
    private readonly Queue<Func<Task>> _steps = new();
    private bool _running;
    private string _guestName = "Bo";

    public GroundView View { get; set; } = null!;

    /// <summary>Where the pictures go, or "guest" for the second client's part.</summary>
    public string Folder { get; set; } = "";

    /// <summary>The hosted server (admin commands), or null when joined.</summary>
    public GameServer? Server { get; set; }

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
            ("chop", Chop), ("mine", Mine), ("death", Death), ("night", Night),
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
        foreach (string item in new[] { "woven-shirt 1", "woven-trousers 1", "work-boots 1", "chest-plate 1", "stone-hatchet 1" })
        {
            Server?.Execute($"give {_guestName} {item}");
        }
        // An open spot of the meadow; the guest three metres ahead, the host's hands empty.
        (Num.Vector3 spot, Num.Vector3 towards) = OpenSpot(Client.Character.Position);
        await TeleportTo(spot);
        Me.HeldSlot = -1;
        Num.Vector3 ahead = spot + towards * 3.0f;
        Server?.Execute(string.Create(CultureInfo.InvariantCulture, $"teleport {_guestName} {ahead.X:0.00} {ahead.Z:0.00}"));
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
        foreach (string wear in new[] { "woven-shirt", "woven-trousers", "work-boots", "chest-plate" })
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

    private void Admin(string command, string args) => Server?.Execute($"{command} {Client.Name} {args}".Trim());

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
        Server?.Execute(string.Create(CultureInfo.InvariantCulture, $"teleport {Client.Name} {point.X:0.00} {point.Z:0.00}"));
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

    private async Task Shot(string name)
    {
        for (int i = 0; i < 3; i++)
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
