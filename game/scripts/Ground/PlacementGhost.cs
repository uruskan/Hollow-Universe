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

    private readonly Dictionary<Placeable, Node3D> _live = new();
    private readonly Dictionary<Placeable, List<Node3D>> _pending = new();
    private readonly List<Node3D> _usedPending = new();
    private readonly Dictionary<Placeable, int> _taken = new();
    private ShaderMaterial _liveMaterial = null!;
    private ShaderMaterial _pendingMaterial = null!;
    private Node3D? _shown;

    /// <summary>The building kit, for pieces and doors (null: their hulls and boxes).</summary>
    public BuildingAssets? Assets { get; set; }

    /// <summary>Models by a placeable kind's name (placement.json's kinds); a kind not here takes the catalogue's model.</summary>
    private Dictionary<string, Func<Placeable, Node3D?>> Sources => _sources ??= new(StringComparer.Ordinal)
    {
        ["piece"] = Piece,
        ["door"] = Door,
    };

    private Dictionary<string, Func<Placeable, Node3D?>>? _sources;

    public override void _Ready()
    {
        _shader ??= GD.Load<Shader>("res://shaders/placement_ghost.gdshader");
        _liveMaterial = new ShaderMaterial { Shader = _shader, RenderPriority = 1 };
        _pendingMaterial = new ShaderMaterial { Shader = _shader, RenderPriority = 1 };
        _pendingMaterial.SetShaderParameter("colour", Waiting);
        _pendingMaterial.SetShaderParameter("pending", 1.0f);
    }

    /// <summary>The live ghost: a placeable where a candidate puts it, in its verdict's colour; null hides it.</summary>
    public void Show(PlacementCandidate? candidate)
    {
        float exposure = Glow.Exposure;
        _liveMaterial.SetShaderParameter("exposure", exposure);
        _pendingMaterial.SetShaderParameter("exposure", exposure);
        Node3D? model = candidate != null ? Live(candidate.Placeable) : null;
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
            if (!_pending.TryGetValue(p.Placeable, out List<Node3D>? pool))
            {
                pool = new List<Node3D>();
                _pending.Add(p.Placeable, pool);
            }
            int index = _taken.GetValueOrDefault(p.Placeable);
            _taken[p.Placeable] = index + 1;
            if (index >= pool.Count)
            {
                if (Make(p.Placeable, _pendingMaterial) is not { } made)
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

    private Node3D? Live(Placeable placeable)
    {
        if (!_live.TryGetValue(placeable, out Node3D? model))
        {
            model = Make(placeable, _liveMaterial);
            if (model != null)
            {
                _live.Add(placeable, model);
            }
        }
        return model;
    }

    // A placeable's model from its kind's source (or the catalogue's), every mesh in the hologram material, no shadows.
    private Node3D? Make(Placeable placeable, Material material)
    {
        Node3D? model = Sources.TryGetValue(placeable.Kind.Name, out Func<Placeable, Node3D?>? source) ? source(placeable) : Catalogue(placeable);
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
    private Node3D? Piece(Placeable placeable)
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

    // A door: the kit's frame and leaves, shut (or the garage door's frame and curtain).
    private Node3D? Door(Placeable placeable)
    {
        if (Assets == null)
        {
            return null;
        }
        var root = new Node3D { Name = placeable.Key };
        DoorView.Parts((DoorDef)placeable.Definition, Assets, root);
        return root;
    }
}
