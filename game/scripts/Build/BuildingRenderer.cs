using System;
using System.Collections.Generic;
using Godot;
using Hollow.Simulation.Building;
using Num = System.Numerics;

namespace HollowUniverse.Build;

/// <summary>
/// Draws a <see cref="BuildingWorld"/> (GROUND.md §6.3): pieces batched into one <see cref="MultiMeshInstance3D"/> per
/// piece kind, grade and level of detail in each 24 m chunk of the zone, so a 1,000-piece base is a few dozen draw calls.
/// Each batch shows exactly one of the kit's three levels at a time, chosen by the camera's distance with a hysteresis
/// (<see cref="Level"/>): two levels of one batch drawn together would fight face for face, and a camera standing at a
/// switching distance must not flip them back and forth. Only what changed is rebuilt, when it changes. Doors and
/// claim devices are nodes of their own. A piece that goes is simply no longer drawn: one that broke breaks into its gibs
/// (GROUND.md §9.4, the destruction's own path). The stability overlay recolours every piece by its stability (green
/// 100 %, through yellow, to red 0 %). Zone metres are Godot's coordinates.
/// </summary>
public partial class BuildingRenderer : Node3D
{
    private const float Chunk = 24f;

    /// <summary>Where each level of detail gives way to the next, measured from the camera to the middle of a batch's
    /// bounds (as Godot's visibility ranges measure).</summary>
    private static readonly float[] LodEnd = { 26f, 64f };

    /// <summary>How far past a switching distance the camera goes before a batch changes level (either way).</summary>
    public const float Hysteresis = 1f;

    private readonly Dictionary<BatchKey, Batch> _batches = new();
    private readonly Dictionary<Piece, Batch> _of = new();
    private readonly HashSet<Batch> _dirty = new();
    private readonly Dictionary<Door, DoorView> _doors = new();
    private readonly Dictionary<ClaimDevice, MeshInstance3D> _claims = new();
    private BuildingWorld _world = null!;
    private BuildingAssets _assets = null!;
    private CornerPosts _corners = null!;
    private bool _cornersDirty;
    private bool _overlay;

    private readonly record struct BatchKey(PieceDef Def, int Grade, int Cx, int Cz);

    private sealed class Batch(BatchKey key)
    {
        public readonly BatchKey Key = key;
        public readonly List<Piece> Pieces = new();
        public MultiMeshInstance3D[] Lods = Array.Empty<MultiMeshInstance3D>();

        /// <summary>The level shown, -1 until a camera has seen it.</summary>
        public int Shown = -1;

        /// <summary>The middle of its pieces' bounds, which the camera's distance is measured to.</summary>
        public Vector3 Middle;
    }

    /// <summary>Pieces drawn.</summary>
    public int Pieces => _of.Count;

    /// <summary>Multi-meshes in use (each one draw call per pass when in view): the pieces' levels and the corner posts.</summary>
    public int MultiMeshes => _batches.Count * BuildingAssets.Lods + (_corners?.Batches ?? 0);

    /// <summary>Corner posts drawn (<see cref="CornerPosts"/>).</summary>
    public int Corners => _corners?.Count ?? 0;

    /// <summary>Draws every piece at its finest level, at any distance (to compare).</summary>
    public bool FinestOnly { get; set; }

    /// <summary>Draws every piece at this level only, at any distance (to check the art); −1 for the usual ranges.</summary>
    public int OnlyLod { get; set; } = -1;

    public void Attach(BuildingWorld world, BuildingAssets assets)
    {
        _world = world;
        _assets = assets;
        _corners = new CornerPosts(this, assets);
        _cornersDirty = true;
        foreach (Piece p in world.AllPieces)
        {
            Add(p);
            if (p.Door != null)
            {
                AddDoor(p.Door);
            }
        }
        foreach (ClaimDevice c in world.Claims)
        {
            AddClaim(c);
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

    /// <summary>Colours every piece by its stability.</summary>
    public bool StabilityOverlay
    {
        get => _overlay;
        set
        {
            if (_overlay == value)
            {
                return;
            }
            _overlay = value;
            _corners.Hidden = value;
            foreach (Batch b in _batches.Values)
            {
                foreach (MultiMeshInstance3D m in b.Lods)
                {
                    m.MaterialOverride = value ? _assets.Overlay : _assets.Grade(b.Key.Grade);
                }
                _dirty.Add(b);
            }
        }
    }

    private void OnChanged(BuildingEvent e)
    {
        // Walls coming, going or changing grade move the corner posts.
        _cornersDirty |= e.Piece?.Def.Family == PieceFamily.Wall;
        switch (e.Change)
        {
            case BuildingChange.PieceAdded:
                Add(e.Piece!);
                break;
            case BuildingChange.PieceRemoved:
                Drop(e.Piece!);
                break;
            case BuildingChange.PieceChanged:
                Drop(e.Piece!);
                Add(e.Piece!);
                break;
            case BuildingChange.Stability when _overlay:
                foreach (Piece p in e.Structure!.Pieces)
                {
                    if (_of.TryGetValue(p, out Batch? b))
                    {
                        _dirty.Add(b);
                    }
                }
                break;
            case BuildingChange.DoorAdded:
                AddDoor(e.Door!);
                break;
            case BuildingChange.DoorRemoved:
                if (_doors.Remove(e.Door!, out DoorView? view))
                {
                    view.QueueFree();
                }
                break;
            case BuildingChange.DoorChanged:
                if (_doors.TryGetValue(e.Door!, out DoorView? changed))
                {
                    changed.Target = e.Door!.Open;
                }
                break;
            case BuildingChange.ClaimAdded:
                AddClaim(e.Claim!);
                break;
            case BuildingChange.ClaimRemoved:
                if (_claims.Remove(e.Claim!, out MeshInstance3D? device))
                {
                    device.QueueFree();
                }
                break;
        }
    }

    private void Add(Piece piece)
    {
        if (piece.Gone || _of.ContainsKey(piece))
        {
            return;
        }
        (Num.Vector3 position, _) = piece.ZoneTransform;
        var key = new BatchKey(piece.Def, piece.Grade, (int)MathF.Floor(position.X / Chunk), (int)MathF.Floor(position.Z / Chunk));
        if (!_batches.TryGetValue(key, out Batch? batch))
        {
            batch = new Batch(key);
            batch.Lods = new MultiMeshInstance3D[BuildingAssets.Lods];
            for (int lod = 0; lod < BuildingAssets.Lods; lod++)
            {
                var mm = new MultiMesh
                {
                    TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                    UseColors = true,
                    Mesh = _assets.Piece(piece.Def, piece.Grade, lod),
                };
                var instance = new MultiMeshInstance3D
                {
                    Name = $"{piece.Def.Key} g{piece.Grade} lod{lod} {key.Cx},{key.Cz}",
                    Multimesh = mm,
                    MaterialOverride = _overlay ? _assets.Overlay : _assets.Grade(piece.Grade),
                    Visible = false,
                };
                AddChild(instance);
                batch.Lods[lod] = instance;
            }
            _batches.Add(key, batch);
        }
        batch.Pieces.Add(piece);
        _of[piece] = batch;
        _dirty.Add(batch);
    }

    private void Drop(Piece piece)
    {
        if (!_of.Remove(piece, out Batch? batch))
        {
            return;
        }
        batch.Pieces.Remove(piece);
        _dirty.Add(batch);
    }

    private void AddDoor(Door door)
    {
        var view = new DoorView { Name = "Door " + door.Id, Door = door, Assets = _assets, Target = door.Open };
        AddChild(view);
        _doors[door] = view;
    }

    private void AddClaim(ClaimDevice claim)
    {
        (Num.Vector3 position, Num.Quaternion rotation) = claim.ZoneTransform;
        Num.Vector3 size = _world.Data.ClaimSize;
        var device = new MeshInstance3D
        {
            Name = "Claim device",
            Mesh = new BoxMesh { Size = new Vector3(size.X, size.Y, size.Z) },
            MaterialOverride = _assets.Grade(1),
            Transform = ToGodot(position, rotation).TranslatedLocal(new Vector3(0, size.Y / 2, 0)),
        };
        AddChild(device);
        _claims[claim] = device;
    }

    public override void _Process(double delta)
    {
        foreach (Batch batch in _dirty)
        {
            Rebuild(batch);
        }
        _dirty.Clear();
        if (_cornersDirty)
        {
            _cornersDirty = false;
            _corners.Update(_world.AllPieces, _world.Data.Width, _overlay);
        }
        // The camera in the renderer's own space, where the batches' middles are.
        Camera3D? eye = GetViewport()?.GetCamera3D();
        Vector3? camera = eye == null ? null : ToLocal(eye.GlobalPosition);
        foreach (Batch batch in _batches.Values)
        {
            Show(batch, camera);
        }
    }

    // One level of a batch visible (none while it is empty): its level for the camera, through the hysteresis.
    private void Show(Batch batch, Vector3? camera)
    {
        int level = OnlyLod >= 0 ? Math.Min(OnlyLod, BuildingAssets.Lods - 1)
            : FinestOnly ? 0
            : camera is Vector3 eye ? Level(batch.Shown, eye.DistanceTo(batch.Middle))
            : Math.Max(batch.Shown, 0);
        bool any = batch.Pieces.Count > 0;
        if (level == batch.Shown && batch.Lods[level].Visible == any)
        {
            return;
        }
        batch.Shown = level;
        for (int lod = 0; lod < batch.Lods.Length; lod++)
        {
            batch.Lods[lod].Visible = any && lod == level;
        }
    }

    /// <summary>
    /// The level a batch shows at a distance, given the one it shows now (-1: none yet, the plain thresholds): it moves to
    /// a coarser level only <see cref="Hysteresis"/> past a switching distance and back only as far inside it, so a camera
    /// standing at a threshold never flips it, and every batch always shows exactly one level.
    /// </summary>
    public static int Level(int shown, float distance)
    {
        int level = Math.Max(shown, 0);
        float slack = shown < 0 ? 0 : Hysteresis;
        while (level < LodEnd.Length && distance > LodEnd[level] + slack)
        {
            level++;
        }
        while (level > 0 && distance < LodEnd[level - 1] - slack)
        {
            level--;
        }
        return level;
    }

    private void Rebuild(Batch batch)
    {
        int n = batch.Pieces.Count;
        foreach (MultiMeshInstance3D instance in batch.Lods)
        {
            MultiMesh mm = instance.Multimesh;
            mm.InstanceCount = n;
            for (int i = 0; i < n; i++)
            {
                Piece p = batch.Pieces[i];
                (Num.Vector3 position, Num.Quaternion rotation) = p.ZoneTransform;
                mm.SetInstanceTransform(i, ToGodot(position, rotation));
                mm.SetInstanceColor(i, _overlay ? StabilityColour(p) : p.Collapsing ? new Color(1f, 0.55f, 0.5f) : Colors.White);
            }
        }
        if (n > 0)
        {
            batch.Middle = batch.Lods[0].Multimesh.GetAabb().GetCenter();
        }
    }

    /// <summary>Green at 100 %, yellow at 50 %, red at 0 %.</summary>
    public static Color StabilityColour(Piece p)
    {
        float s = Math.Clamp(p.Stability / 1000f, 0, 1);
        // Deep enough to stay saturated in sunlight under the tonemapper.
        var red = new Color(0.62f, 0.05f, 0.03f);
        var yellow = new Color(0.72f, 0.52f, 0.02f);
        var green = new Color(0.07f, 0.42f, 0.09f);
        return p.Collapsing ? new Color(0.2f, 0.02f, 0.02f) : s < 0.5f ? red.Lerp(yellow, s * 2) : yellow.Lerp(green, (s - 0.5f) * 2);
    }

    public static Transform3D ToGodot(Num.Vector3 position, Num.Quaternion rotation) =>
        new(new Basis(new Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W)), new Vector3(position.X, position.Y, position.Z));
}

/// <summary>A door in its opening: the kit's frame insert and leaves, swinging (or a garage curtain rolling) over a second.</summary>
public partial class DoorView : Node3D
{
    private readonly List<(MeshInstance3D Leaf, float OpenYaw)> _leaves = new();
    private AnimationPlayer? _garage;
    private float _swing;
    private bool _shown;

    public Door Door { get; set; } = null!;

    public BuildingAssets Assets { get; set; } = null!;

    /// <summary>Open (true) or shut: the leaves move toward it.</summary>
    public bool Target { get; set; }

    public override void _Ready()
    {
        (Num.Vector3 position, Num.Quaternion rotation) = Door.Frame.ZoneTransform;
        Transform = BuildingRenderer.ToGodot(position, rotation).RotatedLocal(Vector3.Up, Door.Flip * Mathf.Pi);
        _garage = Parts(Door.Def, Assets, this, _leaves);
        _swing = Target ? 1 : 0;
    }

    /// <summary>
    /// A door's kit parts under a node (in the frame piece's frame, flipped by the caller): its frame and its leaves on their
    /// hinges, shut, or the garage door's frame and curtain. The door drawn in the world and the placement ghost's door
    /// (GROUND_SYSTEMS.md §6) are the same parts. Returns the garage door's animation player, if it is one.
    /// </summary>
    public static AnimationPlayer? Parts(DoorDef def, BuildingAssets assets, Node3D into, List<(MeshInstance3D Leaf, float OpenYaw)>? leaves = null)
    {
        if (def.Rolls && assets.Garage != null)
        {
            Node3D garage = assets.Garage.Instantiate<Node3D>();
            into.AddChild(garage);
            foreach (Node n in garage.FindChildren("*", "MeshInstance3D", true, false))
            {
                var mesh = (MeshInstance3D)n;
                mesh.MaterialOverride = assets.Named("Door_garage");
            }
            return garage.FindChildren("*", "AnimationPlayer", true, false) is { Count: > 0 } players ? (AnimationPlayer)players[0] : null;
        }
        if (assets.Part(def.FrameMesh) is Mesh frame)
        {
            into.AddChild(new MeshInstance3D { Mesh = frame, MaterialOverride = assets.PartMaterial(frame) });
        }
        foreach (LeafDef leaf in def.Leaves)
        {
            Mesh mesh = assets.Part(leaf.Mesh) ?? new BoxMesh { Size = new Vector3(Math.Abs(leaf.Size.X), leaf.Size.Y, leaf.Size.Z) };
            var node = new MeshInstance3D
            {
                Mesh = mesh,
                MaterialOverride = assets.PartMaterial(mesh),
                Position = new Vector3(leaf.Hinge.X, leaf.Hinge.Y, leaf.Hinge.Z),
            };
            into.AddChild(node);
            leaves?.Add((node, Mathf.DegToRad((float)leaf.OpenYaw)));
        }
        return null;
    }

    public override void _Process(double delta)
    {
        float goal = Target ? 1 : 0;
        if (_garage != null)
        {
            if (!_shown || Mathf.Abs(goal - _swing) > 0.001f)
            {
                string clip = Target ? "Open" : "Close";
                if (_garage.HasAnimation(clip) && _garage.CurrentAnimation != clip)
                {
                    _garage.Play(clip);
                    if (!_shown)
                    {
                        _garage.Seek(_garage.CurrentAnimationLength, true);
                    }
                }
                _swing = goal;
                _shown = true;
            }
            return;
        }
        // The kit's leaves travel in one second, easing at both ends.
        _swing = Mathf.MoveToward(_swing, goal, (float)delta);
        float eased = _swing * _swing * (3 - 2 * _swing);
        foreach ((MeshInstance3D leaf, float openYaw) in _leaves)
        {
            leaf.Rotation = new Vector3(0, openYaw * eased, 0);
        }
    }
}
