using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollow.Client;
using Hollow.Core;
using Hollow.Simulation;
using Hollow.Simulation.Building;
using Hollow.Simulation.Content;
using Hollow.Simulation.Items;
using HollowUniverse.Build;
using HollowUniverse.Fx;
using HollowUniverse.UI.Model;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

/// <summary>
/// Building in the ground game (GROUND.md §6.5, phase 2b): the replicated structures drawn by the building package's
/// <see cref="BuildingRenderer"/> (batched per piece kind, grade and chunk), the lines under the crosshair and the held
/// tool's keys (<see cref="BuildHud"/>: what <see cref="PlacementView"/> says of whatever is being placed, the plan's piece
/// with its cost and stability), the plan's and the hammer's radials, and what the held tool does: a building tool that
/// places (the plan's piece, a door, a lock, the claim device) places through the one placement controller
/// (GROUND_SYSTEMS.md §6), the hammer repairs, upgrades, rotates or demolishes. Doors open and shut with E; a code lock
/// asks for its code (<see cref="Keypad"/>); held E on a door or the claim device offers its options (lock, change the
/// code, cut a key, authorise, clear the list, pick up). Pieces placed and upgraded, and doors, sound and show (the
/// effects library); pieces broken, brought down or demolished break apart through the gibs (<see cref="GibsView"/>).
/// What E does to doors and the tool cupboard is the interaction's (GROUND_SYSTEMS.md §5); with no building tool held, the
/// lines under the crosshair say what the door or cupboard aimed at is.
/// </summary>
public partial class BuildView : Node3D, IHammerActions
{
    private BuildingAssets? _assets;
    private BuildingRenderer? _renderer;
    private BuildingWorld? _world;
    private readonly Dictionary<Piece, int> _grades = new();
    private readonly Dictionary<Door, bool> _open = new();
    private double _since;
    private object? _target;
    private RayHit _hit;
    private ItemWallet? _wallet;
    private PlanRadial? _planRadial;
    private IRadialSource? _hammer;

    public GameClient Client { get; set; } = null!;

    /// <summary>The placement controller (GROUND_SYSTEMS.md §6): what the held item places, its ghost and verdict.</summary>
    public PlacementView Placing { get; set; } = null!;

    /// <summary>The building's art, once loaded (the placement ghost draws pieces and doors from it).</summary>
    public BuildingAssets? Assets => _assets;

    /// <summary>The plan's chosen piece as a placeable while the building plan is held, else null.</summary>
    public Hollow.Simulation.Placement.Placeable? PlannedPiece =>
        !Client.Local.IsDead && Client.Local.HeldItem?.Building == BuildingRole.Plan ? Client.Content.Placement.Of(Plan.Chosen) : null;

    /// <summary>The lines under the crosshair (in the interface's layer).</summary>
    public BuildHud Hud { get; set; } = null!;

    /// <summary>Notices ("Not enough wood").</summary>
    public Action<string>? Say { get; set; }

    /// <summary>The plan's chosen piece (an index into the building data's wheel).</summary>
    public int PlanPiece
    {
        get => Plan.Piece;
        set => Plan.Piece = value;
    }

    /// <summary>The building plan's radial: its pieces, and the one chosen.</summary>
    public PlanRadial Plan => _planRadial ??= NewPlan();

    /// <summary>The hammer's radial: upgrade, turn or demolish the piece aimed at.</summary>
    public IRadialSource HammerRadial => _hammer ??= BuildingRadials.Hammer(() => _target as Piece, Data, Wallet, () => Client.ClockSeconds, this);

    /// <summary>What the held item does in building (none: not a building tool).</summary>
    public BuildingRole Held { get; private set; }

    /// <summary>What the interact key acts on now (set by the view each frame): a door or the tool cupboard has its line.</summary>
    public Hollow.Simulation.Targets.TargetRef Aimed { get; set; }

    private BuildingData Data => Client.Content.Building;

    // The player's line for an answer (texts.json).
    private string Line(Outcome outcome) => Client.Content.Texts.Line(outcome);

    /// <summary>Every frame, after the camera: the building drawn, the ghost and the lines for the held tool.</summary>
    public void Refresh(double delta, Num.Vector3 eye, Num.Vector3 forward)
    {
        _since += delta;
        Attach();
        if (_world == null)
        {
            return;
        }
        _target = _world.Target(eye, forward, (float)Data.Reach, out _hit);
        LocalPlayer me = Client.Local;
        ItemDef? item = me.IsDead ? null : me.HeldItem;
        Held = item?.Building ?? BuildingRole.None;
        Hud.Line = null;
        Hud.Reason = null;
        Hud.Hints = null;
        if (Placing.Placeable is { } placing)
        {
            PlacingLines(placing, item);
        }
        else if (Held == BuildingRole.Hammer)
        {
            Hud.Hints = "LMB REPAIR   ·   HOLD RMB UPGRADE / ROTATE / DEMOLISH";
            Hud.Line = _target != null ? TargetLine(_target) : null;
        }
        else if (AimedPart() is { } part)
        {
            Hud.Line = TargetLine(part);
        }
        _lockSync -= delta;
        if (_lockSync <= 0)
        {
            _lockSync = 0.5;
            DrawLocks();
        }
    }

    // What the hands do with each use of a held item that places something (the keys' hints).
    private static readonly Dictionary<HeldUse, string> PlaceHints = new()
    {
        [HeldUse.Placer] = "LMB PLACE   ·   R TURN",
        [HeldUse.Throwable] = "LMB THROW   ·   RMB PLACE ON THE SURFACE",
    };

    // The lines for what is being placed: its name (and price, if it costs materials), then why it doesn't fit, or (a
    // piece) how stable it would be.
    private void PlacingLines(Hollow.Simulation.Placement.Placeable placing, ItemDef? item)
    {
        string line = placing.Name.ToUpperInvariant();
        foreach (Hollow.Simulation.Placement.PlacementCost cost in placing.Costs)
        {
            line += $"  ·  {cost.Amount} {Data.MaterialName(cost.Material).ToUpperInvariant()} ({Owned(cost.Material):N0})";
        }
        Hud.Line = line;
        Hud.Reason = Placing.Reason;
        Hud.ReasonIsWarning = Placing.Current != null;
        if (Hud.Reason == null && Placing.Current?.Detail is PlacementPlan plan)
        {
            Hud.Reason = $"{plan.Stability / 10.0:0.#}% STABLE";
            Hud.ReasonIsWarning = false;
        }
        Hud.Hints = Held == BuildingRole.Plan
            ? "LMB BUILD   ·   HOLD RMB SELECT PIECE   ·   R ROTATE"
            : item != null ? PlaceHints.GetValueOrDefault(item.Held) : null;
    }

    private double _lockSync;

    // Locks on doors: the catalogue's code or key lock on the outside of the first leaf, by its free edge, so it swings
    // with the door.
    private void DrawLocks()
    {
        if (_renderer == null)
        {
            return;
        }
        foreach (Node child in _renderer.GetChildren())
        {
            if (child is not DoorView view || view.Door.Def.Leaves.Count == 0)
            {
                continue;
            }
            string? want = view.Door.Lock switch
            {
                CodeLock => "code-lock",
                KeyLock => "key-lock",
                _ => null,
            };
            LeafDef leaf = view.Door.Def.Leaves[0];
            var hinge = new Vector3(leaf.Hinge.X, leaf.Hinge.Y, leaf.Hinge.Z);
            MeshInstance3D? leafNode = view.GetChildren().OfType<MeshInstance3D>().FirstOrDefault(m => m.Position.IsEqualApprox(hinge));
            if (leafNode == null)
            {
                continue;
            }
            Node3D? fitted = leafNode.GetNodeOrNull<Node3D>("Lock");
            if (fitted != null && fitted.GetMeta("key", "").AsString() == want)
            {
                continue;
            }
            fitted?.QueueFree();
            if (want == null || HollowUniverse.Items.ModelCatalog.Instantiate(want) is not { } model)
            {
                continue;
            }
            var holder = new Node3D { Name = "Lock" };
            holder.SetMeta("key", want);
            // By the free edge, at hand height, its back on the outside face (a mounted model faces +Z from its back): where
            // the placement ghost put it.
            Num.Vector3 spot = Door.LockOffset(leaf);
            holder.Position = new Vector3(spot.X, spot.Y, spot.Z);
            holder.AddChild(model);
            leafNode.AddChild(holder);
        }
    }

    // ---------------------------------------------------------------- the building drawn

    private void Attach()
    {
        if (_world != null || Client.Building().World is not { } world)
        {
            return;
        }
        _world = world;
        _assets = BuildingAssets.Load(Data);
        _renderer = new BuildingRenderer { Name = "Buildings" };
        AddChild(_renderer);
        _renderer.Attach(world, _assets);
        foreach (Piece p in world.AllPieces)
        {
            _grades[p] = p.Grade;
            if (p.Door is { } door)
            {
                _open[door] = door.Open;
            }
        }
        world.Changed += OnChanged;
    }

    public override void _ExitTree()
    {
        if (_world != null)
        {
            _world.Changed -= OnChanged;
        }
    }

    // What happens to the building sounds and shows: a piece placed (once the world is in) or upgraded; a door opened or
    // shut. (What breaks is the gibs' own path, GibsView.)
    private void OnChanged(BuildingEvent e)
    {
        bool near(Num.Vector3 at) => Num.Vector3.Distance(at, Client.Character.Position) < 60;
        switch (e.Change)
        {
            case BuildingChange.PieceAdded when e.Piece is { } added:
            {
                _grades[added] = added.Grade;
                (Num.Vector3 at, _) = added.ZoneTransform;
                if (_since > 3 && near(at))
                {
                    Sounds.At($"build.place.{Data.Grades[added.Grade].Key}", GroundView.ToGodot(at));
                }
                break;
            }
            case BuildingChange.PieceChanged when e.Piece is { } changed:
            {
                (Num.Vector3 at, Num.Quaternion rotation) = changed.ZoneTransform;
                if (_grades.TryGetValue(changed, out int was) && changed.Grade > was && near(at))
                {
                    string grade = Data.Grades[changed.Grade].Key;
                    Sounds.At($"build.upgrade.{grade}", GroundView.ToGodot(at));
                    FxDirector.Play($"world.upgrade.{grade}", GroundView.ToGodot(at), Up(at), new FxParams { Area = HalfSize(changed) });
                }
                _grades[changed] = changed.Grade;
                break;
            }
            case BuildingChange.PieceRemoved when e.Piece is { } removed:
                _grades.Remove(removed);
                break;
            case BuildingChange.DoorAdded when e.Door is { } fitted:
                _open[fitted] = fitted.Open;
                break;
            case BuildingChange.DoorRemoved when e.Door is { } gone:
                _open.Remove(gone);
                break;
            case BuildingChange.DoorChanged when e.Door is { } door:
            {
                bool was = _open.TryGetValue(door, out bool w) && w;
                _open[door] = door.Open;
                if (was == door.Open)
                {
                    break;
                }
                (Num.Vector3 at, _) = door.Frame.ZoneTransform;
                if (!near(at))
                {
                    break;
                }
                string? key = Sounds.Item(door.Def.Key, door.Open ? "open" : "close");
                Sounds.At(key ?? (door.Open ? "door.open.wood" : "door.close.wood"), GroundView.ToGodot(at + Up(at).ToNumerics() * 1.0f));
                if (!door.Open)
                {
                    FxDirector.Play("world.door-slam", GroundView.ToGodot(at), Up(at));
                }
                break;
            }
        }
    }

    private Vector3 Up(Num.Vector3 at) => GroundView.ToGodot(Client.Frame.UpAt(at));

    private Vector3 HalfSize(Piece piece) => piece.Def.IsSlab
        ? new Vector3(1.5f, 0.15f, 1.5f)
        : new Vector3(1.5f, (float)piece.Def.Height / 2, 0.15f);

    // ---------------------------------------------------------------- the held tool

    private PieceDef ChosenPiece => Plan.Chosen;

    // What the player carries, counted as the building rules count it.
    private ItemWallet Wallet => _wallet ??= new ItemWallet(Client.Content, Client.Local.Inventory.Carried);

    private PlanRadial NewPlan() => new(Data, Wallet);

    void IHammerActions.Upgrade(Piece piece, int grade) => Client.Local.Upgrade(piece, grade);

    void IHammerActions.Rotate(Piece piece) => Client.Local.Rotate(piece);

    void IHammerActions.Demolish(Piece piece) => Client.Local.Demolish(piece);

    private int Owned(string material) => Wallet.Count(material);

    private string TargetLine(object target) => target switch
    {
        Piece p => $"{Data.Grades[p.Grade].Name.ToUpperInvariant()} {p.Def.Name.ToUpperInvariant()}  ·  {Math.Ceiling(p.Health):0} / {p.MaxHealth:0}  ·  {p.StabilityPercent:0.#}% STABLE",
        Door d => $"{d.Def.Name.ToUpperInvariant()}  ·  {Math.Ceiling(d.Health):0} / {d.Def.Health:0}  ·  {(d.Open ? "OPEN" : "SHUT")}" +
            (d.Lock is CodeLock ? "  ·  CODE LOCK" : d.Lock is KeyLock ? "  ·  KEY LOCK" : ""),
        ClaimDevice c => $"{Data.ClaimName.ToUpperInvariant()}  ·  PROTECTED {Protected(c)}" +
            (Client.Building().Replica?.StateOf(c).Authorised == true ? "  ·  AUTHORISED" : ""),
        _ => "",
    };

    private string Protected(ClaimDevice claim)
    {
        double hours = Client.Building().Replica?.StateOf(claim).ProtectedHours ?? 0;
        return double.IsInfinity(hours) ? "FOREVER" : hours < 1 ? $"{hours * 60:0} MIN" : $"{hours:0.#} H";
    }

    /// <summary>The hammer's attack button: repairs the piece aimed at. (What a building tool places goes through the placement controller, <see cref="PlacementView"/>.)</summary>
    public void Use()
    {
        if (Held == BuildingRole.Hammer && _target is Piece piece)
        {
            Client.Local.Repair(piece);
        }
    }

    // The door or tool cupboard the interact key acts on (an ID names one thing, whatever its family), for its line.
    private object? AimedPart()
    {
        var id = new Hollow.Simulation.Entities.EntityId(Aimed.Id);
        return Aimed.IsNone || _world == null ? null : (object?)_world.DoorOf(id) ?? _world.ClaimOf(id);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>The claim device's name and building rights where the player stands, for the HUD.</summary>
    public Hollow.Simulation.Building.Privilege PrivilegeHere() =>
        _world == null || !Client.HasCharacter ? Hollow.Simulation.Building.Privilege.None : _world.PrivilegeAt(Client.EntityId.Value, Client.Character.Position);
}

internal static class GodotVectors
{
    public static Num.Vector3 ToNumerics(this Vector3 v) => new(v.X, v.Y, v.Z);
}
