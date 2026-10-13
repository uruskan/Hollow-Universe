using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollow.Client;
using Hollow.Core;
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
/// <see cref="BuildingRenderer"/> (batched per piece kind, grade and chunk), the plan's ghost from
/// <see cref="GameClient.PlanPiece"/> (green, or red with the reason), the lines under the crosshair and the held tool's
/// keys (<see cref="BuildHud"/>), the plan's and the hammer's wheels, and what the held tool does: place a piece, repair,
/// upgrade, rotate or demolish one, fit a door or a lock, place the claim device. Doors open and shut with E; a code lock
/// asks for its code (<see cref="Keypad"/>); held E on a door or the claim device offers its options (lock, change the
/// code, cut a key, authorise, clear the list, pick up). Pieces placed, upgraded, demolished and collapsing, and doors,
/// sound and show (the effects library).
/// </summary>
public partial class BuildView : Node3D
{
    private const float DoorReach = 3.5f;

    private BuildingAssets? _assets;
    private BuildingRenderer? _renderer;
    private BuildGhost? _ghost;
    private BuildingWorld? _world;
    private readonly List<MeshInstance3D> _pending = new();
    private readonly Dictionary<Piece, int> _grades = new();
    private readonly Dictionary<Door, bool> _open = new();
    private StandardMaterial3D? _pendingMaterial;
    private double _since;
    private int _turns, _flip;
    private PlacementPlan? _plan;
    private object? _target;
    private RayHit _hit;
    private Num.Vector3 _eye, _forward;

    public GameClient Client { get; set; } = null!;

    /// <summary>The lines under the crosshair (in the interface's layer).</summary>
    public BuildHud Hud { get; set; } = null!;

    /// <summary>Notices ("Not enough wood").</summary>
    public Action<string>? Say { get; set; }

    /// <summary>The plan's chosen piece (an index into the building data's wheel).</summary>
    public int PlanPiece { get; set; }

    /// <summary>What the held item does in building (none: not a building tool).</summary>
    public BuildingRole Held { get; private set; }

    /// <summary>The door or claim device within reach of the interact key, if any.</summary>
    public object? Interactable { get; private set; }

    private BuildingData Data => Client.Content.Building;

    /// <summary>Every frame, after the camera: the building drawn, the ghost and the lines for the held tool.</summary>
    public void Refresh(double delta, Num.Vector3 eye, Num.Vector3 forward)
    {
        _since += delta;
        _eye = eye;
        _forward = forward;
        Attach();
        if (_world == null)
        {
            return;
        }
        _target = _world.Target(eye, forward, (float)Data.Reach, out _hit);
        Interactable = _target is Door or ClaimDevice && _hit.Distance < DoorReach ? _target : null;
        LocalPlayer me = Client.Local;
        ItemDef? item = me.IsDead ? null : me.HeldItem;
        Held = item?.Building ?? BuildingRole.None;
        _plan = null;
        Hud.Line = null;
        Hud.Reason = null;
        Hud.Hints = null;
        switch (Held)
        {
            case BuildingRole.Plan:
                AimPlan();
                break;
            case BuildingRole.Hammer:
                Hud.Hints = "LMB REPAIR   ·   HOLD RMB UPGRADE / ROTATE / DEMOLISH";
                Hud.Line = _target != null ? TargetLine(_target) : null;
                break;
            case BuildingRole.Door when item!.Door is { } door:
                Hud.Hints = $"LMB PLACE {door.Name.ToUpperInvariant()}   ·   R TURN 180°";
                if (OpeningAimed() is { } opening)
                {
                    Hud.Line = $"{opening.Def.Name.ToUpperInvariant()}  ·  {(opening.Door == null ? "EMPTY" : opening.Door.Def.Name.ToUpperInvariant())}";
                }
                break;
            case BuildingRole.KeyLock or BuildingRole.CodeLock:
                Hud.Hints = "LMB FIT ON THE DOOR";
                Hud.Line = _target is Door d ? $"{d.Def.Name.ToUpperInvariant()}  ·  {(d.Lock == null ? "NO LOCK" : "LOCKED BY " + (d.Lock is CodeLock ? "A CODE LOCK" : "A KEY LOCK"))}" : null;
                break;
            case BuildingRole.Claim:
                Hud.Hints = "LMB PLACE ON A FOUNDATION OR FLOOR";
                Hud.Line = _target is Piece { Def.IsSlab: true } slab ? TargetLine(slab) : null;
                break;
            default:
                if (Interactable != null)
                {
                    Hud.Line = TargetLine(Interactable);
                }
                break;
        }
        _ghost!.Show(_plan);
        DrawPending();
        _lockSync -= delta;
        if (_lockSync <= 0)
        {
            _lockSync = 0.5;
            DrawLocks();
        }
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
            // By the free edge, at hand height, on the outside face; the model's front (+X) out of the door.
            float edge = leaf.Size.X * 0.86f, thickness = MathF.Abs(leaf.Size.Z) / 2 + 0.02f;
            holder.Position = new Vector3(edge, 1.05f, thickness);
            holder.RotationDegrees = new Vector3(0, -90, 0);
            holder.AddChild(model);
            leafNode.AddChild(holder);
        }
    }

    // ---------------------------------------------------------------- the building drawn

    private void Attach()
    {
        if (_world != null || Client.Building is not { } world)
        {
            return;
        }
        _world = world;
        _assets = BuildingAssets.Load(Data);
        _renderer = new BuildingRenderer { Name = "Buildings" };
        AddChild(_renderer);
        _renderer.Attach(world, _assets);
        _ghost = new BuildGhost { Name = "Ghost", Assets = _assets };
        AddChild(_ghost);
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

    // What happens to the building sounds and shows: a piece placed (once the world is in), upgraded, demolished or
    // collapsing; a door opened or shut.
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
            {
                _grades.Remove(removed);
                (Num.Vector3 at, _) = removed.ZoneTransform;
                if (!near(at))
                {
                    break;
                }
                bool fell = removed.Collapsing || removed.Health <= 0;
                Sounds.At(fell ? "build.collapse" : "build.demolish", GroundView.ToGodot(at));
                FxDirector.Play("world.collapse", GroundView.ToGodot(at), Up(at), new FxParams
                {
                    Area = HalfSize(removed),
                    Tint = GradeTint(removed.Grade),
                });
                break;
            }
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

    private static Color GradeTint(int grade) => grade switch
    {
        0 => new Color(0.75f, 0.62f, 0.42f),
        1 => new Color(0.62f, 0.48f, 0.32f),
        2 => new Color(0.62f, 0.6f, 0.56f),
        _ => new Color(0.48f, 0.5f, 0.52f),
    };

    // Placements not yet answered, drawn where they will stand.
    private void DrawPending()
    {
        IReadOnlyList<PendingPiece> pending = Client.Local.PendingPieces;
        _pendingMaterial ??= new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(0.85f, 0.78f, 0.6f, 0.55f),
        };
        int used = 0;
        foreach (PendingPiece p in pending)
        {
            if (_assets == null)
            {
                break;
            }
            if (used >= _pending.Count)
            {
                var mesh = new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, MaterialOverride = _pendingMaterial };
                AddChild(mesh);
                _pending.Add(mesh);
            }
            MeshInstance3D m = _pending[used++];
            m.Mesh = _assets.HasArt(p.Piece, 0) ? _assets.Piece(p.Piece, 0, 0) : _assets.Hulls(p.Piece);
            (Num.Vector3 position, Num.Quaternion rotation) = p.ZoneTransform;
            m.Transform = BuildingRenderer.ToGodot(position, rotation);
            m.Visible = true;
        }
        for (int i = used; i < _pending.Count; i++)
        {
            _pending[i].Visible = false;
        }
    }

    // ---------------------------------------------------------------- the held tool

    private PieceDef ChosenPiece => Data.Wheel[Math.Clamp(PlanPiece, 0, Data.Wheel.Count - 1)];

    private void AimPlan()
    {
        PieceDef piece = ChosenPiece;
        _plan = Client.PlanPiece(piece, _eye, _forward, _turns);
        GradeDef twig = Data.Grades[0];
        Hud.Line = $"{piece.Name.ToUpperInvariant()}  ·  {Data.CostOf(piece, 0)} {Data.MaterialName(twig.Material).ToUpperInvariant()} ({Owned(twig.Material):N0})";
        if (_plan == null)
        {
            Hud.Reason = Outcome.DefaultText(Refusal.NoSocket);
            Hud.ReasonIsWarning = false;
        }
        else if (!_plan.Valid)
        {
            Hud.Reason = _plan.Outcome.Text;
            Hud.ReasonIsWarning = true;
        }
        else
        {
            Hud.Reason = $"{_plan.Stability / 10.0:0.#}% STABLE";
            Hud.ReasonIsWarning = false;
        }
        Hud.Hints = "LMB BUILD   ·   HOLD RMB SELECT PIECE   ·   R ROTATE";
    }

    private int Owned(string material) => Client.Content.Item(material) is { } def ? Client.Local.Inventory.Count(def) : 0;

    private string TargetLine(object target) => target switch
    {
        Piece p => $"{Data.Grades[p.Grade].Name.ToUpperInvariant()} {p.Def.Name.ToUpperInvariant()}  ·  {Math.Ceiling(p.Health):0} / {p.MaxHealth:0}  ·  {p.StabilityPercent:0.#}% STABLE",
        Door d => $"{d.Def.Name.ToUpperInvariant()}  ·  {Math.Ceiling(d.Health):0} / {d.Def.Health:0}  ·  {(d.Open ? "OPEN" : "SHUT")}" +
            (d.Lock is CodeLock ? "  ·  CODE LOCK" : d.Lock is KeyLock ? "  ·  KEY LOCK" : ""),
        ClaimDevice c => $"{Data.ClaimName.ToUpperInvariant()}  ·  PROTECTED {Protected(c)}" +
            (Client.BuildingReplica?.StateOf(c).Authorised == true ? "  ·  AUTHORISED" : ""),
        _ => "",
    };

    private string Protected(ClaimDevice claim)
    {
        double hours = Client.BuildingReplica?.StateOf(claim).ProtectedHours ?? 0;
        return double.IsInfinity(hours) ? "FOREVER" : hours < 1 ? $"{hours * 60:0} MIN" : $"{hours:0.#} H";
    }

    /// <summary>The plan's wheel: every piece with its cost in twig and what the player has.</summary>
    public IReadOnlyList<WheelChoice> PlanChoices()
    {
        GradeDef twig = Data.Grades[0];
        int owned = Owned(twig.Material);
        string material = Data.MaterialName(twig.Material).ToUpperInvariant();
        return Data.Wheel.Select(p =>
        {
            int cost = Data.CostOf(p, 0);
            return new WheelChoice(p.Id, p.Name, p.Description, $"{cost} x {material} ({owned:N0})",
                owned < cost ? $"Not enough {material.ToLowerInvariant()}" : null);
        }).ToArray();
    }

    /// <summary>The hammer's wheel for the piece aimed at: upgrade to each grade, rotate, demolish.</summary>
    public IReadOnlyList<WheelChoice> HammerChoices()
    {
        Piece? piece = _target as Piece;
        var choices = new List<WheelChoice>();
        const string strength = "Improve this piece's strength";
        for (int grade = 1; grade < Data.Grades.Count; grade++)
        {
            GradeDef g = Data.Grades[grade];
            string? reason = piece == null ? "Aim at a building piece" : piece.Grade >= grade ? "Already at this grade or stronger" : null;
            string? cost = piece == null ? null : $"{Data.CostOf(piece.Def, grade)} x {Data.MaterialName(g.Material).ToUpperInvariant()} ({Owned(g.Material):N0})";
            choices.Add(new WheelChoice(99 + grade, $"UPGRADE TO {g.Name.ToUpperInvariant()}", strength, cost, reason));
        }
        bool late = piece != null && Client.ClockSeconds - piece.PlacedAt > Data.EditWindow;
        choices.Add(new WheelChoice(104, "ROTATE", "Turn this piece around",
            Reason: piece == null ? "Aim at a building piece" : piece.Def.RotateSteps == 0 ? "This piece can't be rotated" : late ? "The 10 minutes are over" : null));
        choices.Add(new WheelChoice(105, "DEMOLISH", "Remove this piece and unsupported pieces",
            Reason: piece == null ? "Aim at a building piece" : late ? "The 10 minutes are over" : null));
        return choices;
    }

    /// <summary>A sector chosen on the plan's or the hammer's wheel.</summary>
    public void ChooseWheel(WheelKind kind, int index)
    {
        if (kind == WheelKind.Plan)
        {
            PlanPiece = index;
            _turns = 0;
            return;
        }
        if (_target is not Piece piece)
        {
            return;
        }
        int grades = Data.Grades.Count - 1;
        if (index < grades)
        {
            Client.Local.Upgrade(piece, index + 1);
        }
        else if (index == grades)
        {
            Client.Local.Rotate(piece);
        }
        else
        {
            Client.Local.Demolish(piece);
        }
    }

    /// <summary>R: the next turn of the piece, or a door the other way round.</summary>
    public void Rotate()
    {
        _turns++;
        _flip ^= 1;
    }

    /// <summary>The attack button with a building tool: place, repair, fit, claim.</summary>
    public void Use()
    {
        LocalPlayer me = Client.Local;
        int slot = me.HeldSlot;
        switch (Held)
        {
            case BuildingRole.Plan when _plan is { Valid: true }:
                me.PlacePiece(_plan);
                break;
            case BuildingRole.Plan:
                Say?.Invoke(_plan?.Outcome.Text ?? Outcome.DefaultText(Refusal.NoSocket));
                break;
            case BuildingRole.Hammer when _target is Piece piece:
                me.Repair(piece);
                break;
            case BuildingRole.Door:
                if (OpeningAimed() is { } opening)
                {
                    me.PlaceDoor(ContainerRef.Belt, slot, opening, _flip == 1);
                }
                else
                {
                    Say?.Invoke(Outcome.DefaultText(Refusal.NotAnOpening));
                }
                break;
            case BuildingRole.KeyLock or BuildingRole.CodeLock when _target is Door door:
                me.AttachLock(ContainerRef.Belt, slot, door);
                break;
            case BuildingRole.Claim when _target is Piece { Def.IsSlab: true } slab:
            {
                Vector3D local = slab.Structure.Frame.ToLocal(new Vector3D(_hit.Point.X, _hit.Point.Y, _hit.Point.Z));
                me.PlaceClaim(ContainerRef.Belt, slot, slab, local.X, local.Z, slab.Pose.Yaw);
                break;
            }
        }
    }

    // A doorway or wall frame the aim passes through (its empty opening counts, as in RustMVP).
    private Piece? OpeningAimed()
    {
        if (_target is Piece { Def.Opening: Opening.Door or Opening.Double } aimed)
        {
            return aimed;
        }
        if (_world == null)
        {
            return null;
        }
        var near = new List<Piece>();
        Num.Vector3 end = _eye + _forward * (float)Data.Reach;
        _world.PiecesNear(_eye, end, 3.5f, near);
        Piece? best = null;
        double nearest = double.MaxValue;
        foreach (Piece p in near)
        {
            if (p.Def.Opening is not (Opening.Door or Opening.Double))
            {
                continue;
            }
            (double w, double h) = p.Def.Opening == Opening.Door ? Data.DoorOpening : Data.DoubleOpening;
            BuildingFrame frame = p.Structure.Frame;
            Vector3D eye = new(_eye.X, _eye.Y, _eye.Z), far = new(end.X, end.Y, end.Z);
            if (PieceGeometry.RayOpening(p.Pose, w, h, frame.ToLocal(eye), frame.ToLocal(far), out Vector3D hit))
            {
                double distance = (frame.ToZone(hit) - eye).Length;
                if (distance < nearest)
                {
                    nearest = distance;
                    best = p;
                }
            }
        }
        return best;
    }

    // ---------------------------------------------------------------- doors and the claim device

    /// <summary>The interact key's prompt for a door or the claim device within reach (null: none).</summary>
    public InteractionPrompt? Prompt()
    {
        switch (Interactable)
        {
            case Door door:
            {
                DoorState state = Client.BuildingReplica!.StateOf(door);
                string action = state.CanUse ? door.Open ? "CLOSE DOOR" : "OPEN DOOR" : state.Lock == LockKind.Code ? "ENTER CODE" : "LOCKED";
                return new InteractionPrompt("E", action, MoreOptions: Options(door).Count > 0);
            }
            case ClaimDevice:
                return new InteractionPrompt("E", "OPEN " + Data.ClaimName.ToUpperInvariant(), MoreOptions: true);
        }
        return null;
    }

    /// <summary>A tap of the interact key on what is within reach: a door opens or shuts (or asks for its code), the claim
    /// device's store opens. Returns false when there is nothing.</summary>
    public bool Interact(Action<Door, bool> keypad)
    {
        switch (Interactable)
        {
            case Door door:
            {
                DoorState state = Client.BuildingReplica!.StateOf(door);
                if (state.CanUse)
                {
                    Client.Local.ToggleDoor(door);
                }
                else if (state.Lock == LockKind.Code)
                {
                    keypad(door, false);
                }
                else
                {
                    Sounds.At("lock.key", GroundView.ToGodot(door.Frame.ZoneTransform.Position));
                    Say?.Invoke("It's locked.");
                }
                return true;
            }
            case ClaimDevice claim:
                Client.Local.Open(claim.Id);
                return true;
        }
        return false;
    }

    /// <summary>What holding the interact key offers on a door or the claim device: (label, action).</summary>
    public IReadOnlyList<(string Label, Action Act)> Options(object? target, Action<Door, bool>? keypad = null)
    {
        var options = new List<(string, Action)>();
        LocalPlayer me = Client.Local;
        switch (target)
        {
            case Door door:
            {
                DoorState state = Client.BuildingReplica!.StateOf(door);
                bool mine = door.Owner == Client.EntityId.Value || door.Lock?.Owner == Client.EntityId.Value;
                if (door.Lock is CodeLock && state.CanUse)
                {
                    options.Add((state.Locked ? "UNLOCK" : "LOCK", () => { if (state.Locked) { me.Unlock(door); } else { me.Lock(door); } }));
                    if (mine && keypad != null)
                    {
                        options.Add(("CHANGE CODE", () => keypad(door, true)));
                    }
                }
                if (door.Lock is KeyLock && state.CanUse)
                {
                    options.Add((state.Locked ? "UNLOCK" : "LOCK", () => { if (state.Locked) { me.Unlock(door); } else { me.Lock(door); } }));
                    if (mine)
                    {
                        options.Add(("CUT A KEY", () => me.MakeKey(door)));
                    }
                }
                if (door.Lock != null && mine)
                {
                    options.Add(("TAKE THE LOCK", () => me.RemoveLock(door)));
                }
                if (mine && state.CanUse)
                {
                    options.Add(("PICK UP DOOR", () => me.PickUpDoor(door)));
                }
                break;
            }
            case ClaimDevice claim:
            {
                bool authorised = Client.BuildingReplica!.StateOf(claim).Authorised;
                options.Add((authorised ? "DEAUTHORISE" : "AUTHORISE", () => { if (authorised) { me.Deauthorise(claim); } else { me.Authorise(claim); } }));
                if (authorised)
                {
                    options.Add(("CLEAR THE LIST", () => me.ClearAuthorised(claim)));
                    options.Add(("PICK UP", () => me.PickUpClaim(claim)));
                }
                break;
            }
        }
        return options;
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
