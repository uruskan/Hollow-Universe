using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollow.Client;
using Hollow.Simulation.Content;
using Hollow.Simulation.Nature;
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
    private readonly Dictionary<uint, bool> _lit = new();
    private readonly HashSet<ClientProjectile> _loosed = new();
    private readonly HashSet<ClientProjectile> _stopped = new();
    private readonly List<Node3D> _tracers = new();
    private readonly Dictionary<ClientProjectile, Vector3> _fromMuzzle = new();

    /// <summary>Where the player's own muzzle is now (render space), so our tracers leave the gun, not the eye.</summary>
    public Func<Vector3>? Muzzle { get; set; }
    private double _since;
    private readonly List<MeshInstance3D> _arrows = new();
    private static StandardMaterial3D? _flame;
    private static StandardMaterial3D? _shaft;

    public GameClient Client { get; set; } = null!;

    public void Refresh(double time)
    {
        _since += GetProcessDeltaTime();
        var seen = new HashSet<uint>();
        foreach (RemoteThing thing in Client.Things)
        {
            if (thing.Kind == StaticKind.Corpse)
            {
                continue;
            }
            seen.Add(thing.NetId);
            bool known = _things.TryGetValue(thing.NetId, out (Node3D Node, uint Revision) existing);
            if (known)
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
            Sound(thing, node, known);
        }
        foreach (uint gone in _things.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            _things[gone].Node.QueueFree();
            _things.Remove(gone);
            _lit.Remove(gone);
        }
        UpdateArrows(time);
    }

    // A deployable placed near the player (one that appears close by once the world is in), a fire lit or put out;
    // a lit fire crackles on its node (GROUND.md §4.9).
    private void Sound(RemoteThing thing, Node3D node, bool known)
    {
        if (thing.Deployable(Client.Content) is not { } def)
        {
            return;
        }
        Vector3 at = GroundView.ToGodot(thing.Position + Client.Frame.UpAt(thing.Position) * 0.3f);
        if (!known && _since > 3 && Num.Vector3.Distance(thing.Position, Client.Character.Position) < 25)
        {
            Sounds.At(Sounds.Item(def.Key, "place") ?? "deploy.place", at);
        }
        bool was = _lit.TryGetValue(thing.NetId, out bool w) && w;
        _lit[thing.NetId] = thing.Lit;
        if (thing.Lit)
        {
            if (known && !was)
            {
                Sounds.At(Sounds.Item(def.Key, "ignite"), at);
            }
            Sounds.Loop(Sounds.Item(def.Key, "loop"), node);
        }
        else if (known && was)
        {
            Sounds.At(Sounds.Item(def.Key, "extinguish"), at);
        }
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
                Node3D? model = GroundModels.Provider.Deployable(def.Key, half);
                if (model != null)
                {
                    node.AddChild(model);
                }
                if (thing.Lit)
                {
                    // Where the model says it burns (socket_fire), else in the middle of its base.
                    Vector3 fire = model != null && Socket(model, "socket_fire") is { } at ? at : new Vector3(0, 0.12f, 0);
                    AddFire(node, fire, def.Key == "furnace" ? 0.6f : 1f);
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

    /// <summary>Where a model's socket is, in the space of the model's parent (null if it has none of that name).</summary>
    public static Vector3? Socket(Node3D model, string name)
    {
        if (model.FindChild(name, true, false) is not Node3D socket)
        {
            return null;
        }
        Transform3D t = Transform3D.Identity;
        for (Node3D? n = socket; n != null && n != model; n = n.GetParent() as Node3D)
        {
            t = n.Transform * t;
        }
        return (model.Transform * t).Origin;
    }

    // A flame that flickers and a warm light that throws shadows.
    public static void AddFire(Node3D parent, Vector3 at, float size)
    {
        if (_flame == null)
        {
            ImageTexture flame = FlameTexture();
            _flame = new StandardMaterial3D
            {
                // Black albedo: a flame only gives light. Black emission: the texture alone is the flame's colour
                // (Godot adds the emission colour to the texture's).
                AlbedoColor = new Color(0, 0, 0, 1),
                AlbedoTexture = flame,
                EmissionEnabled = true,
                Emission = Colors.Black,
                EmissionTexture = flame,
                TextureRepeat = false,
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
                float a = edge * Math.Clamp((1 - v) * 1.6f, 0, 1) * Math.Clamp(v / 0.1f, 0, 1);
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
        ArrowSounds();
        Bullets(now);
        foreach (ClientProjectile p in Client.Projectiles)
        {
            if (p.IsBullet)
            {
                continue;
            }
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

    // Bullets in flight as tracers (our own leaving the muzzle, closing on the shot's line over 50 m), and where each
    // stops: a puff and the sound of what it hit (G4, GROUND.md §6.4).
    private void Bullets(double now)
    {
        int used = 0;
        foreach (ClientProjectile p in Client.Projectiles)
        {
            if (!p.IsBullet)
            {
                continue;
            }
            if (p.EndedAt is { } end)
            {
                if (_stopped.Add(p) && p.EndTarget != Hollow.Simulation.HitTarget.Nothing)
                {
                    Impact(p, end);
                }
                continue;
            }
            if (p.Own && !_fromMuzzle.ContainsKey(p) && Muzzle != null)
            {
                _fromMuzzle[p] = Muzzle() - GroundView.ToGodot(p.Origin);
            }
            if (used >= _tracers.Count)
            {
                Node3D tracer = ShotEffects.Provider.Tracer();
                AddChild(tracer);
                _tracers.Add(tracer);
            }
            Num.Vector3 at = p.PositionAt(now);
            Vector3 d = GroundView.ToGodot(p.DirectionAt(now));
            Vector3 position = GroundView.ToGodot(at);
            if (_fromMuzzle.TryGetValue(p, out Vector3 offset))
            {
                float travelled = Num.Vector3.Distance(at, p.Origin);
                position += offset * Math.Max(0, 1 - travelled / 50);
            }
            Vector3 side = MathF.Abs(d.Y) > 0.95f ? Vector3.Right : Vector3.Up;
            _tracers[used].GlobalTransform = new Transform3D(Basis.LookingAt(d, side), position - d * 0.8f);
            _tracers[used].Visible = true;
            used++;
        }
        for (int i = used; i < _tracers.Count; i++)
        {
            _tracers[i].Visible = false;
        }
        foreach (ClientProjectile gone in _fromMuzzle.Keys.Where(p => p.EndedAt != null || !Client.Projectiles.Contains(p)).ToList())
        {
            _fromMuzzle.Remove(gone);
        }
    }

    // What a bullet hit, by the sound of it: flesh, a tree's wood, rock, a deployable's planks, the ground or the sea.
    private void Impact(ClientProjectile p, Num.Vector3 end)
    {
        bool water = GroundAudio.WaterDepth(Client, end) > 0.05;
        string key = p.EndTarget switch
        {
            Hollow.Simulation.HitTarget.Character => "impact.flesh",
            Hollow.Simulation.HitTarget.Deployable => "impact.wood",
            Hollow.Simulation.HitTarget.Natural => NaturalAt(end) == NaturalClass.Tree ? "impact.wood" : "impact.stone",
            _ => water ? "impact.water" : "impact.dirt",
        };
        Vector3 at = GroundView.ToGodot(end);
        Sounds.At(key, at);
        ShotEffects.Provider.Impact(this, at, p.EndTarget, water);
    }

    // The class of the natural object nearest a point within 3 m (a tree, an outcrop, a boulder), if any.
    private NaturalClass? NaturalAt(Num.Vector3 point)
    {
        if (Client.Nature == null)
        {
            return null;
        }
        var near = new List<NaturalObject>();
        Client.Nature.Query(point.X - 3, point.Z - 3, point.X + 3, point.Z + 3, near);
        NaturalObject? best = null;
        float bestDistance = float.MaxValue;
        foreach (NaturalObject o in near)
        {
            float d = Num.Vector3.Distance(o.Position, point);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = o;
            }
        }
        return best is { } b ? Client.Content.Natural(b.Kind)?.Class : null;
    }

    // An arrow loosed (the string, its flight), and where it stops (GROUND.md §4.9).
    private void ArrowSounds()
    {
        _loosed.RemoveWhere(p => !Client.Projectiles.Contains(p));
        _stopped.RemoveWhere(p => !Client.Projectiles.Contains(p));
        foreach (ClientProjectile p in Client.Projectiles)
        {
            if (p.IsBullet)
            {
                continue;
            }
            if (_loosed.Add(p))
            {
                Vector3 from = GroundView.ToGodot(p.Origin);
                if (p.Own)
                {
                    Sounds.Flat("bow.release");
                }
                else
                {
                    Sounds.At("bow.release", from);
                }
                Sounds.At("arrow.flight", from);
            }
            if (p.EndedAt is { } end && _stopped.Add(p) && p.EndTarget != Hollow.Simulation.HitTarget.Nothing)
            {
                Sounds.At(p.EndTarget == Hollow.Simulation.HitTarget.Character ? "arrow.impact.flesh" : "arrow.impact", GroundView.ToGodot(end));
            }
        }
    }
}
