using System;
using System.Collections.Generic;
using Godot;
using Hollow.Client;
using Hollow.Simulation.Building;
using Hollow.Simulation.Placement;
using HollowUniverse.Build;
using HollowUniverse.Items;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

/// <summary>
/// The one placement ghost (GROUND_SYSTEMS.md §6): whatever is being placed, drawn as its own model in one hologram
/// material (<c>placement_ghost.gdshader</c>): one colour when it fits, red when it doesn't, breathing while the server's
/// answer is on its way (the placements sent and not yet answered). A placeable's model comes from its kind's source,
/// found by the kind's name: a building piece's batched kit mesh, a door's kit frame and leaves, and for everything else
/// the model catalogue's model by the placeable's key. The game and the build sandbox share it.
/// </summary>
public partial class PlacementGhost : Node3D
{
    /// <summary>The hologram's colour when it fits, when it doesn't, and while it waits for the server.</summary>
    public static readonly Color Fits = new(0.35f, 0.85f, 1.0f, 0.42f);

    public static readonly Color Refused = new(1.0f, 0.22f, 0.16f, 0.45f);

    public static readonly Color Waiting = new(0.95f, 0.9f, 0.75f, 0.38f);

    private static Shader? _shader;

    private readonly Dictionary<(Placeable, int), Node3D> _live = new();
    private readonly Dictionary<(Placeable, int), List<Node3D>> _pending = new();
    private readonly List<Node3D> _usedPending = new();
    private readonly Dictionary<(Placeable, int), int> _taken = new();
    private ShaderMaterial _liveMaterial = null!;
    private ShaderMaterial _pendingMaterial = null!;
    private Node3D? _shown;

    /// <summary>The building kit, for pieces and doors (null: their hulls and boxes).</summary>
    public BuildingAssets? Assets { get; set; }

    /// <summary>Models by a placeable kind's name (placement.json's kinds); a kind not here takes the catalogue's model.</summary>
    private Dictionary<string, Func<Placeable, int, Node3D?>> Sources => _sources ??= new(StringComparer.Ordinal)
    {
        ["piece"] = Piece,
        ["door"] = Door,
        ["deployable"] = Deployable,
    };

    private Dictionary<string, Func<Placeable, int, Node3D?>>? _sources;

    public override void _Ready()
    {
        _shader ??= GD.Load<Shader>("res://shaders/placement_ghost.gdshader");
        _liveMaterial = new ShaderMaterial { Shader = _shader, RenderPriority = 1 };
        _pendingMaterial = new ShaderMaterial { Shader = _shader, RenderPriority = 1 };
        _pendingMaterial.SetShaderParameter("colour", Waiting);
        _pendingMaterial.SetShaderParameter("pending", 1.0f);
    }

    /// <summary>The hologram's material on a mesh, for the renderer's warm-up (its shader compiled before play).</summary>
    public static MeshInstance3D Warmup(Mesh mesh) => new()
    {
        Mesh = mesh,
        MaterialOverride = new ShaderMaterial { Shader = _shader ??= GD.Load<Shader>("res://shaders/placement_ghost.gdshader"), RenderPriority = 1 },
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    };

    /// <summary>The live ghost: a placeable where a candidate puts it, in its verdict's colour; null hides it.</summary>
    public void Show(PlacementCandidate? candidate)
    {
        float exposure = Glow.Exposure;
        _liveMaterial.SetShaderParameter("exposure", exposure);
        _pendingMaterial.SetShaderParameter("exposure", exposure);
        Node3D? model = candidate != null ? Live(candidate.Placeable, candidate.Variant) : null;
        if (_shown != null && _shown != model)
        {
            _shown.Visible = false;
        }
        _shown = model;
        if (model == null || candidate == null)
        {
            return;
        }
        _liveMaterial.SetShaderParameter("colour", candidate.Valid ? Fits : Refused);
        model.Transform = BuildingRenderer.ToGodot(candidate.Position, candidate.Rotation);
        model.Visible = true;
    }

    /// <summary>The placements sent and not yet answered (or just accepted), each where it will stand, breathing.</summary>
    public void ShowPending(IReadOnlyList<PendingPlacement> pending)
    {
        foreach (Node3D used in _usedPending)
        {
            used.Visible = false;
        }
        _usedPending.Clear();
        _taken.Clear();
        foreach (PendingPlacement p in pending)
        {
            (Placeable, int) key = (p.Placeable, Varies(p.Placeable) ? p.Variant : 0);
            if (!_pending.TryGetValue(key, out List<Node3D>? pool))
            {
                pool = new List<Node3D>();
                _pending.Add(key, pool);
            }
            int index = _taken.GetValueOrDefault(key);
            _taken[key] = index + 1;
            if (index >= pool.Count)
            {
                if (Make(p.Placeable, key.Item2, _pendingMaterial) is not { } made)
                {
                    continue;
                }
                pool.Add(made);
            }
            Node3D model = pool[index];
            model.Transform = BuildingRenderer.ToGodot(p.Position, p.Rotation);
            model.Visible = true;
            _usedPending.Add(model);
        }
    }

    // Only a door's model depends on its variant (its layout): one model for each layout it is shown in.
    private static bool Varies(Placeable placeable) => placeable.Kind.Name == "door";

    private Node3D? Live(Placeable placeable, int variant)
    {
        (Placeable, int) key = (placeable, Varies(placeable) ? variant : 0);
        if (!_live.TryGetValue(key, out Node3D? model))
        {
            model = Make(placeable, key.Item2, _liveMaterial);
            if (model != null)
            {
                _live.Add(key, model);
            }
        }
        return model;
    }

    // A placeable's model from its kind's source (or the catalogue's), every mesh in the hologram material, no shadows.
    private Node3D? Make(Placeable placeable, int variant, Material material)
    {
        Node3D? model = Sources.TryGetValue(placeable.Kind.Name, out Func<Placeable, int, Node3D?>? source) ? source(placeable, variant) : Catalogue(placeable);
        if (model == null)
        {
            return null;
        }
        foreach (MeshInstance3D mesh in ModelCatalog.Meshes(model))
        {
            mesh.MaterialOverride = material;
            mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }
        model.Visible = false;
        AddChild(model);
        return model;
    }

    // A deployable: the catalogue's model, and for a station that builds ships its bay's outline on the ground in front of
    // it (deployables.json bay), the room a ship will need there.
    private static Node3D? Deployable(Placeable placeable, int variant)
    {
        Node3D model = Catalogue(placeable);
        if (placeable.Definition is Hollow.Simulation.Content.DeployableDef { Bay: { } bay })
        {
            model.AddChild(BayMarking.Outline(bay));
        }
        return model;
    }

    // The model catalogue's model of the placeable's key, or a box of its footprint.
    private static Node3D Catalogue(Placeable placeable)
    {
        if (ModelCatalog.Instantiate(placeable.ModelKey) is { } model)
        {
            return model;
        }
        Num.Vector3 half = placeable.Footprint.HalfExtents;
        var size = new Vector3(Math.Max(half.X, 0.1f) * 2, Math.Max(half.Y, 0.1f) * 2, Math.Max(half.Z, 0.1f) * 2);
        var root = new Node3D { Name = placeable.Key };
        root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, Position = new Vector3(0, size.Y / 2, 0) });
        return root;
    }

    // A building piece: the kit's mesh (the stone grade's middle level, as the plan's ghost always was), else its hulls.
    private Node3D? Piece(Placeable placeable, int variant)
    {
        if (Assets == null)
        {
            return null;
        }
        var def = (PieceDef)placeable.Definition;
        var root = new Node3D { Name = def.Key };
        root.AddChild(new MeshInstance3D { Mesh = Assets.HasArt(def, 2) ? Assets.Piece(def, 2, 1) : Assets.Hulls(def) });
        return root;
    }

    // The door as it would hang (docs/DOORS.md §3): its leaves part way open on their hinge pins in the layout, and the arc each
    // sweeps to open drawn on the floor, so the hinge side and the swing side show before the door is placed.
    private Node3D? Door(Placeable placeable, int variant)
    {
        if (Assets == null)
        {
            return null;
        }
        var root = new Node3D { Name = placeable.Key };
        var def = (DoorDef)placeable.Definition;
        var leaves = new List<(Node3D Pivot, float OpenYaw)>();
        DoorView.Parts(def, variant, Assets, root, leaves);
        foreach ((Node3D pivot, float openYaw) in leaves)
        {
            pivot.Rotation = new Vector3(0, openYaw * 0.35f, 0);
        }
        if (def.Rolls)
        {
            // A raised arrow shows the roll direction and identifies the operated face of the curtain.
            root.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.025f, BottomRadius = 0.025f, Height = 0.5f }, Position = new Vector3(0, 1.35f, 0.25f) });
            root.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0, BottomRadius = 0.12f, Height = 0.22f }, Position = new Vector3(0, 1.7f, 0.25f) });
        }
        else
        {
            for (int i = 0; i < def.Leaves.Count; i++)
            {
                root.AddChild(SwingArc(def, i, variant));
            }
        }
        return root;
    }

    // The floor the leaf sweeps over as it opens: a fan of triangles about its hinge pin from the shut leaf to the open one,
    // a few centimetres over the sill, drawn from both sides.
    private static MeshInstance3D SwingArc(DoorDef def, int index, int layout)
    {
        LeafDef leaf = def.Leaves[index];
        LeafHinging hinging = def.Hinging(index, layout);
        var reach = new Vector3(Math.Abs(leaf.Size.X) * (hinging.Mirrored ? -1 : Math.Sign(leaf.Size.X)), 0, hinging.Offset.Z);
        var pin = new Vector3(hinging.Pin.X, 0.03f, hinging.Pin.Z);
        const int segments = 16;
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        for (int s = 0; s < segments; s++)
        {
            Vector3 a = pin + reach.Rotated(Vector3.Up, hinging.OpenYaw * s / segments);
            Vector3 b = pin + reach.Rotated(Vector3.Up, hinging.OpenYaw * (s + 1) / segments);
            tool.AddVertex(pin);
            tool.AddVertex(a);
            tool.AddVertex(b);
            tool.AddVertex(pin);
            tool.AddVertex(b);
            tool.AddVertex(a);
        }
        tool.GenerateNormals();
        return new MeshInstance3D { Name = "Swing arc", Mesh = tool.Commit() };
    }
}
