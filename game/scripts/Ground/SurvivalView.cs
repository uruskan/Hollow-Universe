using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollow.Client;
using Hollow.Simulation.Content;
using Hollow.Simulation.Nature;
using Hollow.Simulation.Physics;
using Hollow.Simulation.World;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

/// <summary>
/// Placeholder drawing of the survival game (GROUND.md §6) until the real models arrive from the other packages:
/// natural objects as simple shapes per 64 m tile (multimeshes, rebuilt when they change), the weak spots of the trees
/// and outcrops near the player, deployables, dropped items and corpses as boxes and capsules, arrows in flight, the
/// item in hand in front of the camera (with a swing), and a ghost where a held deployable would go.
/// </summary>
public partial class SurvivalView : Node3D
{
    /// <summary>Tiles drawn out from the player's tile: 5 × 5, 320 m across (the haze hides the rest).</summary>
    private const int Radius = 2;

    private readonly Dictionary<(int X, int Z), Node3D> _tiles = new();
    private readonly Dictionary<uint, (Node3D Node, uint Revision)> _things = new();
    private readonly List<MeshInstance3D> _arrows = new();
    private readonly List<MeshInstance3D> _spots = new();
    private readonly List<NaturalObject> _scratch = new();
    private readonly Dictionary<(PartShape, Color), StandardMaterial3D> _materials = new();
    private uint _natureRevision = uint.MaxValue;
    private (int X, int Z) _centre = (int.MinValue, 0);
    private Node3D _held = null!;
    private MeshInstance3D _heldMesh = null!;
    private MeshInstance3D _ghost = null!;
    private OmniLight3D _torchLight = null!;
    private double _swingTime = -1;
    private double _swingLength = 0.5;
    private float _draw;

    public GameClient Client { get; set; } = null!;

    /// <summary>Where a held deployable would go, and whether it may (set by the view; the server decides).</summary>
    public Num.Vector3? PlacementPoint { get; set; }

    private enum PartShape
    {
        Trunk,
        PineCrown,
        LeafCrown,
        Rock,
        Pile,
        Plant,
        Bush,
    }

    public override void _Ready()
    {
        _held = new Node3D { Name = "Held" };
        _heldMesh = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.07f, 0.07f, 0.42f) } };
        _held.AddChild(_heldMesh);
        _torchLight = new OmniLight3D { LightColor = new Color(1f, 0.62f, 0.3f), LightEnergy = 1.6f, OmniRange = 9, Visible = false };
        _held.AddChild(_torchLight);
        AddChild(_held);
        _ghost = new MeshInstance3D
        {
            Name = "PlacementGhost",
            Mesh = new BoxMesh(),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.4f, 0.85f, 1f, 0.35f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
            Visible = false,
        };
        AddChild(_ghost);
    }

    /// <summary>Updates everything from the client, every frame. <paramref name="camera"/> carries the held item.</summary>
    public void Refresh(Camera3D camera, double delta, bool primary, bool secondary)
    {
        if (Client.Nature == null || Client.Natural == null || Client.Physics == null || !Client.HasCharacter)
        {
            return;
        }
        Num.Vector3 feet = Client.RenderPosition;
        UpdateNature(feet);
        UpdateSpots(feet);
        UpdateThings();
        UpdateArrows();
        UpdateHeld(camera, delta, primary, secondary);
        UpdateGhost();
    }

    // ---------------------------------------------------------------- natural objects

    private void UpdateNature(Num.Vector3 feet)
    {
        (int, int) centre = PhysicsWorld.TileOf(feet);
        bool rebuild = Client.Natural!.Revision != _natureRevision;
        if (centre == _centre && !rebuild)
        {
            return;
        }
        _centre = centre;
        _natureRevision = Client.Natural.Revision;
        foreach ((int X, int Z) key in _tiles.Keys.Where(k => Math.Max(Math.Abs(k.X - _centre.X), Math.Abs(k.Z - _centre.Z)) > Radius).ToList())
        {
            _tiles[key].QueueFree();
            _tiles.Remove(key);
        }
        for (int z = _centre.Z - Radius; z <= _centre.Z + Radius; z++)
        {
            for (int x = _centre.X - Radius; x <= _centre.X + Radius; x++)
            {
                if (_tiles.TryGetValue((x, z), out Node3D? old))
                {
                    if (!rebuild)
                    {
                        continue;
                    }
                    old.QueueFree();
                }
                _tiles[(x, z)] = BuildTile(x, z);
            }
        }
    }

    private Node3D BuildTile(int tx, int tz)
    {
        var node = new Node3D { Name = $"Nature {tx},{tz}" };
        AddChild(node);
        _scratch.Clear();
        Client.Nature!.Query(tx * PhysicsWorld.TileSize, tz * PhysicsWorld.TileSize, (tx + 1) * PhysicsWorld.TileSize, (tz + 1) * PhysicsWorld.TileSize, _scratch);
        var parts = new Dictionary<(PartShape, Color), List<Transform3D>>();
        void Add(PartShape shape, Color colour, Transform3D transform)
        {
            if (!parts.TryGetValue((shape, colour), out List<Transform3D>? list))
            {
                list = new List<Transform3D>();
                parts.Add((shape, colour), list);
            }
            list.Add(transform);
        }
        GameContent content = Client.Content;
        NaturalState state = Client.Natural!;
        foreach (NaturalObject o in _scratch)
        {
            NaturalKind? kind = content.Natural(o.Kind);
            if (kind == null)
            {
                continue;
            }
            bool depleted = state.IsDepleted(o.Id);
            if (depleted && kind.Class != NaturalClass.Bush)
            {
                continue;
            }
            Basis basis = Upright(o.Up, o.Yaw);
            Vector3 at = GroundView.ToGodot(o.Position);
            float s = o.Scale;
            switch (kind.Class)
            {
                case NaturalClass.Tree:
                {
                    float height = kind.Height * s, radius = kind.Radius * s;
                    Add(PartShape.Trunk, new Color(0.36f, 0.25f, 0.16f), new Transform3D(Stretched(basis, new Vector3(radius, height, radius)), at + basis.Y * height * 0.5f));
                    bool pine = kind.Key.StartsWith("pine", StringComparison.Ordinal);
                    float crown = pine ? 2.4f * s : 3.2f * s;
                    Add(pine ? PartShape.PineCrown : PartShape.LeafCrown, pine ? new Color(0.13f, 0.27f, 0.13f) : new Color(0.24f, 0.40f, 0.16f),
                        new Transform3D(Stretched(basis, new Vector3(crown, height * (pine ? 0.75f : 0.45f), crown)), at + basis.Y * height * (pine ? 0.6f : 0.78f)));
                    break;
                }
                case NaturalClass.Node:
                {
                    // Smaller with each depletion stage, as the outcrop models will be.
                    float shrink = 1 - 0.22f * state.Stage(o, kind);
                    float r = kind.Radius * s * shrink;
                    Color colour = kind.Key switch
                    {
                        "metal-node" => new Color(0.42f, 0.33f, 0.28f),
                        "sulphur-node" => new Color(0.66f, 0.60f, 0.27f),
                        _ => new Color(0.48f, 0.48f, 0.46f),
                    };
                    Add(PartShape.Rock, colour, new Transform3D(Stretched(basis, new Vector3(r, r * 0.75f, r)), at + basis.Y * (r * 0.35f)));
                    break;
                }
                case NaturalClass.Pickup:
                {
                    Color colour = kind.Key switch
                    {
                        "wood-pile" => new Color(0.45f, 0.31f, 0.18f),
                        "stone-pile" => new Color(0.55f, 0.55f, 0.53f),
                        "metal-pile" => new Color(0.47f, 0.36f, 0.30f),
                        "sulphur-pile" => new Color(0.75f, 0.68f, 0.30f),
                        "tuber-plant" => new Color(0.42f, 0.55f, 0.22f),
                        _ => new Color(0.36f, 0.62f, 0.25f),
                    };
                    bool plant = kind.Key is "fibre-plant" or "tuber-plant";
                    float w = kind.Radius * s, h = kind.Height * s;
                    Add(plant ? PartShape.Plant : PartShape.Pile, colour, new Transform3D(Stretched(basis, new Vector3(w, h, w)), at + basis.Y * h * 0.5f));
                    break;
                }
                case NaturalClass.Bush:
                {
                    float r = kind.Radius * s;
                    Add(PartShape.Bush, depleted ? new Color(0.20f, 0.30f, 0.14f) : new Color(0.22f, 0.42f, 0.18f),
                        new Transform3D(Stretched(basis, new Vector3(r, r * 0.8f, r)), at + basis.Y * r * 0.7f));
                    if (!depleted)
                    {
                        // The berries: a few dots of their colour.
                        Color berry = kind.Key.StartsWith("red", StringComparison.Ordinal) ? new Color(0.80f, 0.12f, 0.12f) : new Color(0.18f, 0.25f, 0.78f);
                        for (int i = 0; i < 5; i++)
                        {
                            float a = i * 1.257f + o.Yaw;
                            Vector3 p = at + basis.Y * r * (0.7f + 0.25f * MathF.Sin(i * 2.1f)) + basis.X * MathF.Cos(a) * r * 0.85f + basis.Z * MathF.Sin(a) * r * 0.85f;
                            Add(PartShape.Bush, berry, new Transform3D(Basis.FromScale(new Vector3(0.09f, 0.09f, 0.09f)), p));
                        }
                    }
                    break;
                }
            }
        }
        foreach (((PartShape shape, Color colour), List<Transform3D> transforms) in parts)
        {
            var multi = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = MeshOf(shape),
                InstanceCount = transforms.Count,
            };
            for (int i = 0; i < transforms.Count; i++)
            {
                multi.SetInstanceTransform(i, transforms[i]);
            }
            node.AddChild(new MultiMeshInstance3D { Multimesh = multi, MaterialOverride = Material(shape, colour) });
        }
        return node;
    }

    private static Mesh MeshOf(PartShape shape) => shape switch
    {
        PartShape.Trunk => new CylinderMesh { TopRadius = 0.8f, BottomRadius = 1f, Height = 1, RadialSegments = 8 },
        PartShape.PineCrown => new CylinderMesh { TopRadius = 0, BottomRadius = 1, Height = 1, RadialSegments = 10 },
        PartShape.LeafCrown => new SphereMesh { Radius = 1, Height = 2, RadialSegments = 12, Rings = 6 },
        PartShape.Rock => new SphereMesh { Radius = 1, Height = 2, RadialSegments = 10, Rings = 6 },
        PartShape.Plant => new CylinderMesh { TopRadius = 0.2f, BottomRadius = 0.6f, Height = 1, RadialSegments = 6 },
        PartShape.Bush => new SphereMesh { Radius = 1, Height = 2, RadialSegments = 10, Rings = 6 },
        _ => new BoxMesh { Size = new Vector3(2, 1, 2) },
    };

    private StandardMaterial3D Material(PartShape shape, Color colour)
    {
        if (!_materials.TryGetValue((shape, colour), out StandardMaterial3D? material))
        {
            material = new StandardMaterial3D { AlbedoColor = colour, Roughness = 0.9f };
            _materials.Add((shape, colour), material);
        }
        return material;
    }

    // A basis scaled along its own axes.
    private static Basis Stretched(Basis basis, Vector3 scale) => basis * Basis.FromScale(scale);

    // An object's basis: +Y along its up, turned by its yaw.
    private static Basis Upright(Num.Vector3 up, float yaw)
    {
        (Num.Vector3 forward, _) = Hollow.Simulation.Movement.CharacterMotor.HorizontalBasis(yaw, up);
        return Basis.LookingAt(GroundView.ToGodot(forward), GroundView.ToGodot(up));
    }

    // Glowing weak spots on the trees and outcrops within reach of a tool.
    private void UpdateSpots(Num.Vector3 feet)
    {
        int used = 0;
        _scratch.Clear();
        Client.Nature!.Query(feet.X - 6, feet.Z - 6, feet.X + 6, feet.Z + 6, _scratch);
        foreach (NaturalObject o in _scratch)
        {
            if (Client.Content.Natural(o.Kind) is not { IsHarvested: true } kind || used >= 8 || Client.Natural!.WeakSpot(o, kind) is not { } weak)
            {
                continue;
            }
            if (used >= _spots.Count)
            {
                var spot = new MeshInstance3D
                {
                    Mesh = new SphereMesh { Radius = 0.09f, Height = 0.18f },
                    MaterialOverride = new StandardMaterial3D
                    {
                        AlbedoColor = new Color(1f, 0.85f, 0.4f),
                        EmissionEnabled = true,
                        Emission = new Color(1f, 0.7f, 0.25f),
                        EmissionEnergyMultiplier = 2.5f,
                    },
                };
                AddChild(spot);
                _spots.Add(spot);
            }
            _spots[used].Position = GroundView.ToGodot(weak);
            _spots[used].Visible = true;
            used++;
        }
        for (int i = used; i < _spots.Count; i++)
        {
            _spots[i].Visible = false;
        }
    }

    // ---------------------------------------------------------------- deployables, dropped items, corpses

    private void UpdateThings()
    {
        var seen = new HashSet<uint>();
        foreach (RemoteThing thing in Client.Things)
        {
            seen.Add(thing.NetId);
            if (_things.TryGetValue(thing.NetId, out (Node3D Node, uint Revision) existing))
            {
                if (existing.Revision == thing.Revision)
                {
                    continue;
                }
                existing.Node.QueueFree();
            }
            Node3D node = BuildThing(thing);
            AddChild(node);
            _things[thing.NetId] = (node, thing.Revision);
        }
        foreach (uint gone in _things.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            _things[gone].Node.QueueFree();
            _things.Remove(gone);
        }
    }

    private Node3D BuildThing(RemoteThing thing)
    {
        Num.Vector3 up = Client.Frame.UpAt(thing.Position);
        var node = new Node3D { Name = $"Thing {thing.NetId}" };
        node.Transform = new Transform3D(Upright(up, thing.Yaw), GroundView.ToGodot(thing.Position));
        switch (thing.Kind)
        {
            case StaticKind.Deployable when thing.Deployable(Client.Content) is { } def:
            {
                Vector3 size = new(def.HalfExtents.X * 2, def.HalfExtents.Y * 2, def.HalfExtents.Z * 2);
                Color colour = def.Key switch
                {
                    "sleeping-bag" => thing.Mine ? new Color(0.30f, 0.45f, 0.75f) : new Color(0.45f, 0.40f, 0.35f),
                    "campfire" => new Color(0.30f, 0.22f, 0.16f),
                    "furnace" => new Color(0.42f, 0.40f, 0.38f),
                    "small-box" or "large-box" => new Color(0.55f, 0.40f, 0.24f),
                    _ => new Color(0.48f, 0.42f, 0.30f),
                };
                node.AddChild(new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = size },
                    MaterialOverride = new StandardMaterial3D { AlbedoColor = colour.Darkened(1 - Math.Clamp(thing.Health, 0.35f, 1f)), Roughness = 0.85f },
                    Position = new Vector3(0, size.Y / 2, 0),
                });
                if (thing.Lit)
                {
                    node.AddChild(new OmniLight3D { LightColor = new Color(1f, 0.55f, 0.2f), LightEnergy = 2.2f, OmniRange = 10, Position = new Vector3(0, size.Y + 0.3f, 0) });
                    node.AddChild(new MeshInstance3D
                    {
                        Mesh = new CylinderMesh { TopRadius = 0, BottomRadius = 0.22f, Height = 0.5f },
                        MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.6f, 0.2f), EmissionEnabled = true, Emission = new Color(1f, 0.45f, 0.1f), EmissionEnergyMultiplier = 3 },
                        Position = new Vector3(0, size.Y + 0.25f, 0),
                    });
                }
                break;
            }
            case StaticKind.DroppedItem:
                node.AddChild(new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(0.3f, 0.18f, 0.3f) },
                    MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.62f, 0.55f, 0.42f), Roughness = 0.9f },
                    Position = new Vector3(0, 0.09f, 0),
                });
                break;
            case StaticKind.Corpse:
                node.AddChild(new MeshInstance3D
                {
                    Mesh = new CapsuleMesh { Radius = 0.3f, Height = 1.7f },
                    MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.35f, 0.33f, 0.36f), Roughness = 0.8f },
                    Position = new Vector3(0, 0.3f, 0),
                    RotationDegrees = new Vector3(90, 0, 0),
                });
                break;
        }
        return node;
    }

    // ---------------------------------------------------------------- arrows

    private void UpdateArrows()
    {
        double now = Client.ClockSeconds;
        int used = 0;
        foreach (ClientProjectile p in Client.Projectiles)
        {
            if (used >= _arrows.Count)
            {
                var arrow = new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(0.02f, 0.02f, 0.7f) },
                    MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.75f, 0.68f, 0.5f) },
                };
                AddChild(arrow);
                _arrows.Add(arrow);
            }
            Num.Vector3 at = p.PositionAt(now), dir = p.DirectionAt(now);
            Num.Vector3 up = MathF.Abs(dir.Y) > 0.95f ? Num.Vector3.UnitX : Num.Vector3.UnitY;
            _arrows[used].GlobalTransform = new Transform3D(Basis.LookingAt(GroundView.ToGodot(dir), GroundView.ToGodot(up)), GroundView.ToGodot(at));
            _arrows[used].Visible = true;
            used++;
        }
        for (int i = used; i < _arrows.Count; i++)
        {
            _arrows[i].Visible = false;
        }
    }

    // ---------------------------------------------------------------- the item in hand

    private void UpdateHeld(Camera3D camera, double delta, bool primary, bool secondary)
    {
        ItemDef? item = Client.Local.HeldItem;
        _held.Visible = item != null && !Client.Local.IsDead;
        if (item == null)
        {
            _swingTime = -1;
            return;
        }
        // A swing plays while the attack is held, at the item's pace (the server decides what it hits).
        if (item.Melee is { } melee)
        {
            _swingLength = melee.SwingSeconds;
            if (_swingTime < 0 && primary)
            {
                _swingTime = 0;
            }
        }
        float swing = 0;
        if (_swingTime >= 0)
        {
            _swingTime += delta;
            float t = (float)(_swingTime / _swingLength);
            swing = t < 0.35f ? t / 0.35f : Math.Max(0, 1 - (t - 0.35f) / 0.65f);
            if (_swingTime >= _swingLength)
            {
                _swingTime = primary ? 0 : -1;
            }
        }
        _draw = Mathf.MoveToward(_draw, item.Bow != null && secondary ? 1 : 0, (float)delta * 2.5f);
        Color colour = item.Category switch
        {
            "weapons" => new Color(0.55f, 0.42f, 0.30f),
            "tools" => item.Key == "torch" ? new Color(0.75f, 0.45f, 0.2f) : new Color(0.5f, 0.5f, 0.52f),
            _ => new Color(0.6f, 0.6f, 0.6f),
        };
        _heldMesh.MaterialOverride ??= new StandardMaterial3D { Roughness = 0.6f };
        ((StandardMaterial3D)_heldMesh.MaterialOverride).AlbedoColor = colour;
        float length = item.Melee?.Reach > 2 ? 0.9f : item.Bow != null ? 0.8f : 0.42f;
        ((BoxMesh)_heldMesh.Mesh).Size = new Vector3(0.07f, 0.07f, length);
        Transform3D eye = camera.GlobalTransform;
        Vector3 rest = new(0.32f - 0.22f * _draw, -0.30f + 0.1f * _draw, -0.55f - 0.1f * _draw);
        var local = new Transform3D(Basis.FromEuler(new Vector3(-0.35f - 1.2f * swing, 0.15f, 0)), rest + new Vector3(-0.05f * swing, 0.05f * swing, -0.15f * swing));
        _held.GlobalTransform = eye * local;
        _torchLight.Visible = item.Key == "torch";
    }

    // ---------------------------------------------------------------- placement

    private void UpdateGhost()
    {
        ItemDef? item = Client.Local.HeldItem;
        if (item?.Deploys is not { } def || PlacementPoint is not { } at || Client.Local.IsDead)
        {
            _ghost.Visible = false;
            return;
        }
        Num.Vector3 up = Client.Frame.UpAt(at);
        ((BoxMesh)_ghost.Mesh).Size = new Vector3(def.HalfExtents.X * 2, def.HalfExtents.Y * 2, def.HalfExtents.Z * 2);
        _ghost.GlobalTransform = new Transform3D(Upright(up, PlacementYaw), GroundView.ToGodot(at + up * def.HalfExtents.Y));
        _ghost.Visible = true;
    }

    /// <summary>The turn a placement gets (the player's view yaw, set by the view).</summary>
    public float PlacementYaw { get; set; }
}
