using System;
using System.Collections.Generic;
using Godot;
using Hollow.Client;
using Hollow.Simulation;
using Hollow.Simulation.Animals;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Physics;
using Hollow.Surface;
using HollowUniverse.Fx;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

/// <summary>
/// The F2 panel's drawings in the world (GROUND.md §8.5), over the client's dev module's toggles: the hitboxes of every
/// character and animal in view (the shapes the shots test, GROUND.md §5.6), the collision mesh of the ground tiles
/// around the player (§4.5), the circle of the relevance radius (§4.4), the animals' nav grid around the player and
/// their spawn areas near it (§6.9), the last two as the server sends them. Lines only; nothing when every toggle is off.
/// </summary>
public partial class DevDraw : Node3D
{
    private static readonly Color HeadColour = new(1.00f, 0.35f, 0.30f);
    private static readonly Color BodyColour = new(1.00f, 0.70f, 0.30f);
    private static readonly Color LimbColour = new(0.95f, 0.95f, 0.40f);
    private static readonly Color RelevanceColour = new(0.42f, 0.80f, 1.00f);
    private static readonly Color TileColour = new(0.30f, 0.95f, 0.35f, 0.9f);
    private static readonly Color WalkableColour = new(0.45f, 0.85f, 0.40f, 0.8f);
    private static readonly Color SteepColour = new(1.00f, 0.62f, 0.30f, 0.8f);
    private static readonly Color BlockedColour = new(0.95f, 0.30f, 0.25f, 0.8f);
    private static readonly Color WaterColour = new(0.35f, 0.60f, 1.00f, 0.8f);
    private static readonly Color UnknownColour = new(0.6f, 0.6f, 0.6f, 0.5f);
    private static readonly Color AreaColour = new(0.85f, 0.75f, 0.30f);
    private static readonly Color AreaIdleColour = new(0.55f, 0.50f, 0.30f);

    private const int RingSegments = 12;
    private const float TileLift = 0.05f;
    private const float NavLift = 0.12f;

    public GameClient Client { get; set; } = null!;

    private ImmediateMesh _frameMesh = null!, _navMesh = null!, _areasMesh = null!;
    private MeshInstance3D _frame = null!, _nav = null!, _areas = null!;
    private StandardMaterial3D _throughMaterial = null!, _groundMaterial = null!;
    private readonly Dictionary<TileKey, MeshInstance3D> _tiles = new();
    private readonly List<Label3D> _labels = new();
    private int _navSeen = -1, _areasSeen = -1;
    private bool _navWas, _areasWas;

    public override void _Ready()
    {
        _throughMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            NoDepthTest = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            RenderPriority = 10,
        };
        _groundMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        _frameMesh = new ImmediateMesh();
        _navMesh = new ImmediateMesh();
        _areasMesh = new ImmediateMesh();
        _frame = new MeshInstance3D { Name = "Frame", Mesh = _frameMesh, MaterialOverride = _throughMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _nav = new MeshInstance3D { Name = "Nav", Mesh = _navMesh, MaterialOverride = _groundMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _areas = new MeshInstance3D { Name = "Areas", Mesh = _areasMesh, MaterialOverride = _throughMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_frame);
        AddChild(_nav);
        AddChild(_areas);
    }

    /// <summary>Cached tile geometry and server revisions belong to one world even when tile keys are reused.</summary>
    public void RetireWorld()
    {
        foreach (MeshInstance3D tile in _tiles.Values)
        {
            if (tile.GetParent() == this) RemoveChild(tile);
            tile.QueueFree();
        }
        _tiles.Clear();
        ClearLabels();
        _frameMesh?.ClearSurfaces(); _navMesh?.ClearSurfaces(); _areasMesh?.ClearSurfaces();
        _navSeen = _areasSeen = -1;
        _navWas = _areasWas = false;
    }

    public override void _ExitTree() => RetireWorld();

    /// <summary>Every frame, from the view: what the toggles ask for, around the player's feet.</summary>
    public void Refresh(Num.Vector3 feet)
    {
        DevModule dev = Client.Dev();
        // Unshaded colours go through the sky's exposure like everything else: the materials counter it, as the markers do.
        float brightness = FxDirector.Brightness;
        _throughMaterial.AlbedoColor = _groundMaterial.AlbedoColor = new Color(brightness, brightness, brightness, 1);
        DrawFrame(dev, feet);
        DrawTiles(dev, feet);
        DrawNav(dev);
        DrawAreas(dev);
    }

    // ---------------------------------------------------------------- every frame: hitboxes and the relevance ring

    private void DrawFrame(DevModule dev, Num.Vector3 feet)
    {
        _frameMesh.ClearSurfaces();
        if (!dev.DrawHitboxes && !dev.DrawRelevance)
        {
            return;
        }
        var lines = new List<(Vector3 A, Vector3 B, Color C)>();
        if (dev.DrawHitboxes)
        {
            Span<HitShape> shapes = stackalloc HitShape[Hitboxes.MostShapes];
            foreach (RemoteEntity e in Client.Entities)
            {
                var pose = new HitPose
                {
                    Feet = e.Position,
                    Up = Client.Frame.UpAt(e.Position),
                    Yaw = e.Yaw,
                    Pose = e.Sleeping || e.Wounded ? Pose.Lying : e.Stance == Stance.Crouching ? Pose.Crouching : Pose.Standing,
                };
                int n = Hitboxes.Shapes(pose, shapes);
                for (int i = 0; i < n; i++)
                {
                    Capsule(lines, shapes[i]);
                }
            }
            foreach (ClientAnimal a in Client.Animals().All)
            {
                if (a.Species is not { } species)
                {
                    continue;
                }
                var pose = new HitPose { Feet = a.Position, Up = Client.Frame.UpAt(a.Position), Yaw = a.Yaw, Pose = a.Dead ? Pose.Lying : Pose.Standing };
                int n = AnimalHitboxes.Shapes(species, pose, shapes);
                for (int i = 0; i < n; i++)
                {
                    Capsule(lines, shapes[i]);
                }
            }
        }
        if (dev.DrawRelevance && Client.Surface is { } surface)
        {
            var probe = new GroundProbe(surface, Client.Frame);
            float radius = dev.RelevanceRadius;
            const int segments = 72;
            Vector3? last = null, first = null;
            for (int i = 0; i <= segments; i++)
            {
                float angle = i * MathF.Tau / segments;
                Num.Vector3 p = probe.Offset(feet, radius * MathF.Cos(angle), radius * MathF.Sin(angle));
                Vector3 point = GroundView.ToGodot(p + Client.Frame.UpAt(p) * 0.3f);
                if (last is { } previous)
                {
                    lines.Add((previous, point, RelevanceColour));
                }
                last = point;
                first ??= point;
            }
        }
        Fill(_frameMesh, lines);
    }

    // A capsule as lines: a ring at each end, four lines along it, and the caps as half rings in two planes.
    private static void Capsule(List<(Vector3, Vector3, Color)> lines, in HitShape shape)
    {
        Color colour = shape.Region switch
        {
            HitRegion.Head => HeadColour,
            HitRegion.Body => BodyColour,
            _ => LimbColour,
        };
        Vector3 a = GroundView.ToGodot(shape.A), b = GroundView.ToGodot(shape.B);
        Vector3 axis = b - a;
        if (axis.LengthSquared() < 1e-8f)
        {
            axis = Vector3.Up;
        }
        axis = axis.Normalized();
        Vector3 u = Mathf.Abs(axis.Dot(Vector3.Up)) > 0.9f ? Vector3.Right : Vector3.Up;
        u = (u - axis * u.Dot(axis)).Normalized();
        Vector3 v = axis.Cross(u);
        float r = shape.Radius;
        for (int i = 0; i < RingSegments; i++)
        {
            float t0 = i * MathF.Tau / RingSegments, t1 = (i + 1) * MathF.Tau / RingSegments;
            Vector3 o0 = u * MathF.Cos(t0) * r + v * MathF.Sin(t0) * r, o1 = u * MathF.Cos(t1) * r + v * MathF.Sin(t1) * r;
            lines.Add((a + o0, a + o1, colour));
            lines.Add((b + o0, b + o1, colour));
            if (i % 3 == 0)
            {
                lines.Add((a + o0, b + o0, colour));
            }
        }
        foreach (Vector3 side in new[] { u, v })
        {
            Vector3? last = null;
            for (int i = 0; i <= 6; i++)
            {
                float t = i * MathF.PI / 6;
                Vector3 cap = side * MathF.Cos(t) * r - axis * MathF.Sin(t) * r;
                Vector3 point = a + cap;
                if (last is { } previous)
                {
                    lines.Add((previous, point, colour));
                }
                last = point;
            }
            last = null;
            for (int i = 0; i <= 6; i++)
            {
                float t = i * MathF.PI / 6;
                Vector3 cap = side * MathF.Cos(t) * r + axis * MathF.Sin(t) * r;
                Vector3 point = b + cap;
                if (last is { } previous)
                {
                    lines.Add((previous, point, colour));
                }
                last = point;
            }
        }
    }

    // ---------------------------------------------------------------- the ground tiles around the player

    private void DrawTiles(DevModule dev, Num.Vector3 feet)
    {
        if (!dev.DrawTiles || Client.Physics is not { } physics)
        {
            if (_tiles.Count > 0)
            {
                foreach (MeshInstance3D tile in _tiles.Values)
                {
                    tile.QueueFree();
                }
                _tiles.Clear();
            }
            return;
        }
        var near = new List<TileKey>();
        TileKey.Around(physics.Frame.ToBody(feet), PhysicsWorld.EnsureReach, physics.Surface.Radius, physics.Level, near);
        var wanted = new HashSet<TileKey>();
        foreach (TileKey key in near)
        {
            if (!physics.HasTile(key))
            {
                continue;
            }
            wanted.Add(key);
            if (!_tiles.ContainsKey(key))
            {
                var instance = new MeshInstance3D { Name = $"Tile {key}", Mesh = TileWire(physics.TileMesh(key)), MaterialOverride = _groundMaterial };
                AddChild(instance);
                _tiles[key] = instance;
            }
        }
        foreach (TileKey key in new List<TileKey>(_tiles.Keys))
        {
            if (!wanted.Contains(key))
            {
                _tiles[key].QueueFree();
                _tiles.Remove(key);
            }
        }
    }

    // A tile's triangles as their edges, lifted a little so they show on the ground they are.
    private Mesh TileWire(Hollow.Surface.TriangleMesh mesh)
    {
        float[] p = mesh.Positions;
        int[] idx = mesh.Indices;
        var vertices = new Vector3[idx.Length * 2];
        var colours = new Color[vertices.Length];
        int k = 0;
        for (int t = 0; t + 2 < idx.Length; t += 3)
        {
            for (int e = 0; e < 3; e++)
            {
                int i0 = idx[t + e], i1 = idx[t + (e + 1) % 3];
                vertices[k] = Lifted(p, i0);
                vertices[k + 1] = Lifted(p, i1);
                colours[k] = colours[k + 1] = TileColour;
                k += 2;
            }
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Color] = colours;
        var result = new ArrayMesh();
        result.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
        return result;
    }

    private Vector3 Lifted(float[] positions, int index)
    {
        var p = new Num.Vector3(positions[index * 3], positions[index * 3 + 1], positions[index * 3 + 2]);
        return GroundView.ToGodot(p + Client.Frame.UpAt(p) * TileLift);
    }

    // ---------------------------------------------------------------- the nav grid window (from the server)

    private void DrawNav(DevModule dev)
    {
        bool on = dev.DrawNav && dev.Nav != null;
        if (!on)
        {
            if (_navWas)
            {
                _navMesh.ClearSurfaces();
                _navWas = false;
            }
            return;
        }
        if (_navSeen == dev.NavRevision && _navWas)
        {
            return;
        }
        _navSeen = dev.NavRevision;
        _navWas = true;
        NavWindow nav = dev.Nav!;
        var lines = new List<(Vector3, Vector3, Color)>();
        if (Client.Surface is { } surface)
        {
            var probe = new GroundProbe(surface, Client.Frame);
            int n = nav.Cells;
            float size = NavGrid.CellSize;
            // The window lies flat on the ground at its centre: columns east, rows south from its north-west corner
            // (docs/WHOLE_WORLD.md §7.3). The corners' heights once, then each cell as its four edges.
            (Num.Vector3 north, Num.Vector3 east) = Facing.Compass(Client.Frame.UpAt(nav.Centre));
            Num.Vector3 corner = nav.Centre - east * (n * size / 2) + north * (n * size / 2);
            var corners = new Vector3[(n + 1) * (n + 1)];
            for (int j = 0; j <= n; j++)
            {
                for (int i = 0; i <= n; i++)
                {
                    Num.Vector3 g = probe.Settle(corner + (east * i - north * j) * size);
                    corners[j * (n + 1) + i] = GroundView.ToGodot(g + Client.Frame.UpAt(g) * NavLift);
                }
            }
            for (int j = 0; j < n; j++)
            {
                for (int i = 0; i < n; i++)
                {
                    byte flags = nav.Flags[j * n + i];
                    Color colour = (flags & 8) != 0 ? UnknownColour : (flags & 1) != 0 ? WaterColour : (flags & 2) != 0 ? BlockedColour : (flags & 4) != 0 ? SteepColour : WalkableColour;
                    Vector3 c00 = corners[j * (n + 1) + i], c10 = corners[j * (n + 1) + i + 1], c01 = corners[(j + 1) * (n + 1) + i], c11 = corners[(j + 1) * (n + 1) + i + 1];
                    lines.Add((c00, c10, colour));
                    lines.Add((c00, c01, colour));
                    if (i == n - 1)
                    {
                        lines.Add((c10, c11, colour));
                    }
                    if (j == n - 1)
                    {
                        lines.Add((c01, c11, colour));
                    }
                    if ((flags & 6) != 0)
                    {
                        // Steep and blocked cells get a diagonal, so they read at a glance.
                        lines.Add((c00, c11, colour));
                    }
                }
            }
        }
        Fill(_navMesh, lines);
    }

    // ---------------------------------------------------------------- the spawn areas (from the server)

    private void DrawAreas(DevModule dev)
    {
        bool on = dev.DrawAreas && dev.Areas.Count > 0;
        if (!on)
        {
            if (_areasWas)
            {
                _areasMesh.ClearSurfaces();
                ClearLabels();
                _areasWas = false;
            }
            return;
        }
        if (_areasSeen == dev.AreasRevision && _areasWas)
        {
            return;
        }
        _areasSeen = dev.AreasRevision;
        _areasWas = true;
        ClearLabels();
        float brightness = FxDirector.Brightness;
        var lines = new List<(Vector3, Vector3, Color)>();
        foreach (SpawnAreaView area in dev.Areas)
        {
            Color colour = area.Active ? AreaColour : AreaIdleColour;
            Vector3 centre = GroundView.ToGodot(area.Centre);
            Vector3 up = GroundView.ToGodot(Client.Frame.UpAt(area.Centre));
            lines.Add((centre, centre + up * 6, colour));
            Diamond(lines, centre + up * 6, up, 1.5f, colour);
            foreach (SpawnSlotView slot in area.Slots)
            {
                Vector3 home = GroundView.ToGodot(slot.Home);
                Vector3 homeUp = GroundView.ToGodot(Client.Frame.UpAt(slot.Home));
                Circle(lines, home + homeUp * 0.5f, homeUp, slot.Range, colour, 48);
                lines.Add((home, home + homeUp * 3, colour));
                var label = new Label3D
                {
                    Text = $"{slot.Species} {slot.Living}/{slot.Size}",
                    Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                    NoDepthTest = true,
                    FontSize = 64,
                    PixelSize = 0.012f,
                    Modulate = new Color(colour.R * brightness, colour.G * brightness, colour.B * brightness, 1),
                    OutlineSize = 12,
                    Position = home + homeUp * 3.4f,
                };
                AddChild(label);
                _labels.Add(label);
            }
        }
        Fill(_areasMesh, lines);
    }

    private void ClearLabels()
    {
        foreach (Label3D label in _labels)
        {
            label.QueueFree();
        }
        _labels.Clear();
    }

    private static void Circle(List<(Vector3, Vector3, Color)> lines, Vector3 centre, Vector3 up, float radius, Color colour, int segments)
    {
        Vector3 u = Mathf.Abs(up.Dot(Vector3.Right)) > 0.9f ? Vector3.Forward : Vector3.Right;
        u = (u - up * u.Dot(up)).Normalized();
        Vector3 v = up.Cross(u);
        Vector3? last = null;
        for (int i = 0; i <= segments; i++)
        {
            float t = i * MathF.Tau / segments;
            Vector3 point = centre + u * MathF.Cos(t) * radius + v * MathF.Sin(t) * radius;
            if (last is { } previous)
            {
                lines.Add((previous, point, colour));
            }
            last = point;
        }
    }

    private static void Diamond(List<(Vector3, Vector3, Color)> lines, Vector3 centre, Vector3 up, float size, Color colour)
    {
        Vector3 u = Mathf.Abs(up.Dot(Vector3.Right)) > 0.9f ? Vector3.Forward : Vector3.Right;
        u = (u - up * u.Dot(up)).Normalized();
        Vector3 v = up.Cross(u);
        Vector3[] ring = { centre + u * size, centre + v * size, centre - u * size, centre - v * size };
        for (int i = 0; i < 4; i++)
        {
            lines.Add((ring[i], ring[(i + 1) % 4], colour));
            lines.Add((ring[i], centre + up * size, colour));
            lines.Add((ring[i], centre - up * size, colour));
        }
    }

    private static void Fill(ImmediateMesh mesh, List<(Vector3 A, Vector3 B, Color C)> lines)
    {
        mesh.ClearSurfaces();
        if (lines.Count == 0)
        {
            return;
        }
        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
        foreach ((Vector3 a, Vector3 b, Color c) in lines)
        {
            mesh.SurfaceSetColor(c);
            mesh.SurfaceAddVertex(a);
            mesh.SurfaceSetColor(c);
            mesh.SurfaceAddVertex(b);
        }
        mesh.SurfaceEnd();
    }
}
