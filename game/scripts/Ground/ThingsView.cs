using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollow.Client;
using Hollow.Simulation.Content;
using Hollow.Simulation.World;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

/// <summary>
/// Placed and dropped things (GROUND.md §6.1): deployables and dropped stacks from <see cref="GroundModels"/> (the
/// assets catalogue's models once it lands, placeholders until then), a fire's flame and light while it burns, and
/// arrows in flight. Corpses are <see cref="PeopleView"/>'s ragdolls.
/// </summary>
public partial class ThingsView : Node3D
{
    private readonly Dictionary<uint, (Node3D Node, uint Revision)> _things = new();
    private readonly List<MeshInstance3D> _arrows = new();
    private static StandardMaterial3D? _flame;
    private static StandardMaterial3D? _shaft;

    public GameClient Client { get; set; } = null!;

    public void Refresh(double time)
    {
        var seen = new HashSet<uint>();
        foreach (RemoteThing thing in Client.Things)
        {
            if (thing.Kind == StaticKind.Corpse)
            {
                continue;
            }
            seen.Add(thing.NetId);
            if (_things.TryGetValue(thing.NetId, out (Node3D Node, uint Revision) existing))
            {
                if (existing.Revision == thing.Revision)
                {
                    continue;
                }
                existing.Node.QueueFree();
            }
            Node3D node = Build(thing);
            FrameEvents.Note("thing " + thing.Kind);
            AddChild(node);
            _things[thing.NetId] = (node, thing.Revision);
        }
        foreach (uint gone in _things.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            _things[gone].Node.QueueFree();
            _things.Remove(gone);
        }
        UpdateArrows(time);
    }

    private Node3D Build(RemoteThing thing)
    {
        Num.Vector3 up = Client.Frame.UpAt(thing.Position);
        var node = new Node3D { Name = $"Thing {thing.NetId}" };
        node.Transform = new Transform3D(NatureView.Upright(up, thing.Yaw), GroundView.ToGodot(thing.Position));
        switch (thing.Kind)
        {
            case StaticKind.Deployable when thing.Deployable(Client.Content) is { } def:
            {
                Vector3 half = new(def.HalfExtents.X, def.HalfExtents.Y, def.HalfExtents.Z);
                if (GroundModels.Provider.Deployable(def.Key, half) is { } model)
                {
                    node.AddChild(model);
                }
                if (thing.Lit)
                {
                    AddFire(node, def.Key == "furnace" ? new Vector3(0, 0.4f, -0.45f) : new Vector3(0, 0.12f, 0), def.Key == "furnace" ? 0.6f : 1f);
                }
                break;
            }
            case StaticKind.DroppedItem:
            {
                string key = Client.Content.Item(thing.Look)?.Key ?? "";
                if (GroundModels.Provider.Dropped(key) is { } model)
                {
                    node.AddChild(model);
                }
                break;
            }
        }
        return node;
    }

    // A flame that flickers and a warm light that throws shadows.
    public static void AddFire(Node3D parent, Vector3 at, float size)
    {
        if (_flame == null)
        {
            ImageTexture flame = FlameTexture();
            _flame = new StandardMaterial3D
            {
                AlbedoColor = new Color(0, 0, 0, 1),
                AlbedoTexture = flame,
                EmissionEnabled = true,
                Emission = Colors.White,
                EmissionTexture = flame,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.FixedY,
                DisableReceiveShadows = true,
                Roughness = 1,
                MetallicSpecular = 0,
            };
            Glow.Track(_flame, 3f);
        }
        var fire = new Fire { Position = at, Size = size };
        for (int i = 0; i < 3; i++)
        {
            fire.AddChild(new MeshInstance3D
            {
                Mesh = new QuadMesh { Size = new Vector2(0.32f, 0.55f) * size, CenterOffset = new Vector3(0, 0.27f * size, 0) },
                MaterialOverride = _flame,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Position = new Vector3((i - 1) * 0.07f * size, 0, (i % 2) * 0.05f),
            });
        }
        fire.AddChild(new OmniLight3D
        {
            LightColor = new Color(1f, 0.58f, 0.25f),
            LightEnergy = Glow.FireLight * size,
            OmniRange = 14 * size,
            Position = new Vector3(0, 0.45f * size, 0),
            DistanceFadeEnabled = true,
            DistanceFadeBegin = 60,
            DistanceFadeLength = 20,
            DistanceFadeShadow = 25,
            ShadowEnabled = true,
        });
        parent.AddChild(fire);
    }

    // A flame: a tongue of light, white-yellow at the root, orange, then red and gone at the tip (alpha in the shape).
    private static ImageTexture FlameTexture()
    {
        const int W = 64, H = 128;
        Image image = Image.CreateEmpty(W, H, false, Image.Format.Rgba8);
        for (int y = 0; y < H; y++)
        {
            float v = 1 - (y + 0.5f) / H; // 0 at the root, 1 at the tip
            float half = 0.5f * MathF.Sin(MathF.PI * MathF.Min(1, v * 1.15f + 0.08f)) * (1 - 0.55f * v);
            for (int x = 0; x < W; x++)
            {
                float u = MathF.Abs((x + 0.5f) / W - 0.5f);
                float edge = half <= 0 ? 0 : Math.Clamp((half - u) / (half * 0.55f), 0, 1);
                float a = edge * Math.Clamp((1 - v) * 1.6f, 0, 1);
                var hot = new Color(1f, 0.92f, 0.62f);
                var warm = new Color(1f, 0.55f, 0.12f);
                var cool = new Color(0.85f, 0.18f, 0.04f);
                Color c = v < 0.35f ? hot.Lerp(warm, v / 0.35f) : warm.Lerp(cool, (v - 0.35f) / 0.65f);
                image.SetPixel(x, y, new Color(c.R, c.G, c.B, a));
            }
        }
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>Flickers a fire's flames and light.</summary>
    private partial class Fire : Node3D
    {
        public float Size = 1;
        private double _time;

        public override void _Process(double delta)
        {
            _time += delta;
            float f = 0.85f + 0.1f * MathF.Sin((float)_time * 13.1f) + 0.06f * MathF.Sin((float)_time * 29.7f + 1.3f);
            int i = 0;
            foreach (Node child in GetChildren())
            {
                if (child is MeshInstance3D flame)
                {
                    flame.Scale = new Vector3(1, f + 0.08f * MathF.Sin((float)_time * 17f + i++ * 2.1f), 1);
                }
                else if (child is OmniLight3D light)
                {
                    light.LightEnergy = Glow.FireLight * Size * f;
                }
            }
        }
    }

    private void UpdateArrows(double now)
    {
        int used = 0;
        foreach (ClientProjectile p in Client.Projectiles)
        {
            if (used >= _arrows.Count)
            {
                _shaft ??= new StandardMaterial3D { AlbedoColor = new Color(0.62f, 0.52f, 0.36f), Roughness = 0.8f };
                var arrow = new MeshInstance3D
                {
                    Mesh = new CylinderMesh { TopRadius = 0.006f, BottomRadius = 0.006f, Height = 0.72f, RadialSegments = 6 },
                    MaterialOverride = _shaft,
                };
                AddChild(arrow);
                _arrows.Add(arrow);
            }
            Num.Vector3 at = p.PositionAt(now), dir = p.DirectionAt(now);
            Vector3 d = GroundView.ToGodot(dir);
            Vector3 side = MathF.Abs(d.Y) > 0.95f ? Vector3.Right : Vector3.Up;
            // The cylinder lies along its own Y: point Y along the flight.
            Vector3 x = d.Cross(side).Normalized();
            Vector3 z = x.Cross(d);
            _arrows[used].GlobalTransform = new Transform3D(new Basis(x, d, z), GroundView.ToGodot(at) - d * 0.36f);
            _arrows[used].Visible = true;
            used++;
        }
        for (int i = used; i < _arrows.Count; i++)
        {
            _arrows[i].Visible = false;
        }
    }
}
