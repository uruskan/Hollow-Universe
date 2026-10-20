using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollow.Client;
using Hollow.Client.Feedback;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.World;
using HollowUniverse.Fx;
using HollowUniverse.Audio;
using Num = System.Numerics;
using Hollow.Simulation.Targets;

namespace HollowUniverse.Ground;

/// <summary>
/// Placed and dropped things (GROUND.md §6.1): deployables and bags of dropped items from <see cref="GroundModels"/>, a lit
/// campfire's or furnace's fire (the effects library's), arrows in flight with their trail, and bullets: a tracer for
/// some, flying the bullet's own path from the gun, and each hit's effect and sound by what it hit (§6.4, §9.3). The
/// world's props (G7, §6.8): barrels, crates and recyclers from the model catalogue by their key, a running recycler's
/// lamp and hum, a barrel's blows. Whatever breaks breaks apart through the gibs (<see cref="GibsView"/>, §9.4).
/// Corpses are <see cref="PeopleView"/>'s ragdolls; rockets are <see cref="RaidView"/>'s.
/// </summary>
public partial class ThingsView : Node3D
{
    private readonly Dictionary<uint, (Node3D Node, uint Revision)> _things = new();
    private readonly Dictionary<uint, bool> _lit = new();
    private readonly Dictionary<uint, bool> _gateOpen = new();
    private readonly Dictionary<uint, SoundHandle> _loops = new();
    private readonly Dictionary<uint, FxHandle> _fires = new();
    private readonly Dictionary<uint, (PropDef Prop, Num.Vector3 At, float Health)> _props = new();
    private readonly HashSet<ClientProjectile> _loosed = new();
    private readonly HashSet<ClientProjectile> _stopped = new();
    private readonly Dictionary<ClientProjectile, int> _trails = new();
    private readonly Dictionary<ClientProjectile, uint> _trailRevisions = new();
    private readonly List<ClientProjectile> _forget = new();
    private double _since;
    private readonly List<MeshInstance3D> _arrows = new();
    private static StandardMaterial3D? _shaft;

    public RecentContactFeedback Contacts { get; } = new();

    public GameClient Client { get; set; } = null!;

    /// <summary>Where the player's own muzzle is drawn now (render space), so our tracers leave the gun, not the eye.</summary>
    public Func<Vector3>? Muzzle { get; set; }

    /// <summary>Where another player's muzzle is (by net ID), if they are drawn holding a gun.</summary>
    public Func<uint, Vector3?>? TheirMuzzle { get; set; }

    /// <summary>Network IDs and revisions belong to one world session; discard every prior presentation.</summary>
    public void RetireWorld()
    {
        foreach (FxHandle fire in _fires.Values) FxDirector.Stop(fire, immediate: true);
        foreach (SoundHandle loop in _loops.Values) Sounds.Stop(loop, 0);
        foreach (int tracer in _trails.Values) FxDirector.RemoveTracer(tracer);
        foreach (var thing in _things.Values) CompoundView.Retire(thing.Node);
        foreach (Node child in GetChildren()) { RemoveChild(child); child.QueueFree(); }
        _things.Clear(); _lit.Clear(); _fires.Clear(); _props.Clear();
        _gateOpen.Clear(); _loops.Clear();
        _loosed.Clear(); _stopped.Clear(); _trails.Clear(); _trailRevisions.Clear(); _forget.Clear(); _arrows.Clear();
        Contacts.Clear();
        _since = 0;
    }

    public override void _ExitTree() => RetireWorld();

    public void Refresh(double time)
    {
        _since += GetProcessDeltaTime();
        var seen = new HashSet<uint>();
        foreach (RemoteThing thing in Client.Survival().Things)
        {
            if (thing.Kind == StaticKind.Corpse)
            {
                continue;
            }
            seen.Add(thing.NetId);
            bool known = _things.TryGetValue(thing.NetId, out (Node3D Node, uint Revision) existing);
            if (known)
            {
                if (thing.Deployable(Client.Content)?.Assembly != null)
                {
                    existing.Node.Transform = Placed(thing);
                    CompoundView.Update(existing.Node, thing, Client, time);
                    if (_fires.TryGetValue(thing.NetId, out FxHandle compoundFire))
                        FxDirector.Move(compoundFire, existing.Node.Transform * FireSpot(existing.Node));
                    if (existing.Revision != thing.Revision)
                    {
                        Sound(thing, existing.Node, true);
                        Fire(thing, existing.Node);
                        _things[thing.NetId] = (existing.Node, thing.Revision);
                    }
                    continue;
                }
                if (existing.Revision == thing.Revision)
                {
                    // Aboard a ship (IRON_WAKE.md §8): where the ship is drawn this frame, its fire with it.
                    if (thing.Aboard)
                    {
                        existing.Node.Transform = Placed(thing);
                        if (_fires.TryGetValue(thing.NetId, out FxHandle burning))
                        {
                            FxDirector.Move(burning, existing.Node.Transform * FireSpot(existing.Node));
                        }
                    }
                    continue;
                }
                if (_loops.Remove(thing.NetId, out SoundHandle oldLoop)) Sounds.Stop(oldLoop, 0);
                CompoundView.Retire(existing.Node);
                existing.Node.QueueFree();
            }
            Node3D node = Build(thing);
            FrameEvents.Note("thing " + thing.Kind);
            AddChild(node);
            CompoundView.Update(node, thing, Client, time);
            _things[thing.NetId] = (node, thing.Revision);
            Sound(thing, node, known);
            Fire(thing, node);
            Prop(thing, known);
        }
        foreach (uint gone in _things.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            CompoundView.Retire(_things[gone].Node);
            _things[gone].Node.QueueFree();
            _things.Remove(gone);
            _lit.Remove(gone);
            _gateOpen.Remove(gone);
            if (_loops.Remove(gone, out SoundHandle loop)) Sounds.Stop(loop, 0);
            if (_fires.Remove(gone, out FxHandle fire))
            {
                FxDirector.Stop(fire, immediate: true);
            }
            _props.Remove(gone);
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
        if (def.Gate != null)
        {
            if (_gateOpen.TryGetValue(thing.NetId, out bool open) && open != thing.Utility.Open)
                Sounds.At(Sounds.Item(def.Key, thing.Utility.Open ? "open" : "close"), at);
            _gateOpen[thing.NetId] = thing.Utility.Open;
        }
        // A world prop isn't placed: one coming back (G7) makes no sound.
        if (!known && _since > 3 && thing.Kind == StaticKind.Deployable && Num.Vector3.Distance(thing.Position, Client.Character.Position) < 25)
        {
            Sounds.At(Sounds.Item(def.Key, "place") ?? "deploy.place", at);
        }
        bool was = _lit.TryGetValue(thing.NetId, out bool w) && w;
        _lit[thing.NetId] = thing.Lit;
        // Fires ignite and go out; a recycler or research table (lit while it works, G7) starts and stops.
        if (thing.Lit)
        {
            if (known && !was)
            {
                Sounds.At(Sounds.Item(def.Key, "ignite") ?? Sounds.Item(def.Key, "start"), at);
            }
            if (!_loops.ContainsKey(thing.NetId))
            {
                SoundHandle loop = Sounds.Loop(Sounds.Item(def.Key, "loop"), node);
                if (loop.IsValid) _loops[thing.NetId] = loop;
            }
        }
        else if (known && was)
        {
            if (_loops.Remove(thing.NetId, out SoundHandle loop)) Sounds.Stop(loop);
            Sounds.At(Sounds.Item(def.Key, "extinguish") ?? Sounds.Item(def.Key, "stop") ?? Sounds.Item(def.Key, "done"), at);
        }
    }

    // A world prop (G7): a barrel rings under blows.
    private void Prop(RemoteThing thing, bool known)
    {
        if (thing.Prop(Client.Content) is not { } prop)
        {
            return;
        }
        float before = _props.TryGetValue(thing.NetId, out (PropDef Prop, Num.Vector3 At, float Health) was) ? was.Health : thing.Health;
        _props[thing.NetId] = (prop, thing.Position, thing.Health);
        if (known && thing.Health < before - 1e-4f)
        {
            Sounds.At(Sounds.Item(prop.Key, "hit"), GroundView.ToGodot(thing.Position + Client.Frame.UpAt(thing.Position) * 0.5f));
        }
    }

    // A lit campfire or furnace burns where its model says (socket_fire): the effects library's fire, which goes on
    // while the thing's model is rebuilt and fades when it is put out.
    private void Fire(RemoteThing thing, Node3D node)
    {
        bool burning = _fires.TryGetValue(thing.NetId, out FxHandle handle);
        // Only what burns fuel has a fire (a recycler or research table at work is lit too, G7).
        DeployableDef? def = thing.Lit && thing.Deployable(Client.Content) is { Device: not null } burner ? burner : null;
        if (def != null && !burning)
        {
            Vector3 local = FireSpot(node);
            Transform3D t = node.Transform;
            // Its own fire (fx.json items: the campfire's flames, the furnace's firebox).
            _fires[thing.NetId] = FxDirector.Start(FxCatalog.ItemEffect(def.Key, "fire") ?? "fire.campfire", t * local, t.Basis.Y.Normalized());
        }
        else if (def == null && burning)
        {
            FxDirector.Stop(handle);
            _fires.Remove(thing.NetId);
        }
    }

    // Where a thing's fire burns on its node (its model's socket_fire).
    private static Vector3 FireSpot(Node3D node)
    {
        Node3D? model = node.GetChildCount() > 0 ? node.GetChild(0) as Node3D : null;
        return model != null && Socket(model, "socket_fire") is { } at ? at : new Vector3(0, 0.12f, 0);
    }

    // A thing's node where it is now: standing along its up (aboard, the ship's as drawn) at its heading.
    private static Transform3D Placed(RemoteThing thing) =>
        new(NatureView.Upright(thing.Up, thing.Yaw), GroundView.ToGodot(thing.Position));

    private Node3D Build(RemoteThing thing)
    {
        var node = new Node3D { Name = $"Thing {thing.NetId}" };
        node.Transform = Placed(thing);
        switch (thing.Kind)
        {
            case StaticKind.Deployable when thing.Deployable(Client.Content) is { } def:
            {
                Vector3 half = new(def.HalfExtents.X, def.HalfExtents.Y, def.HalfExtents.Z);
                if (GroundModels.Provider.Deployable(def.Key, half) is { } model)
                {
                    node.AddChild(model);
                }
                if (def.Bay is { } bay)
                {
                    // A station that builds ships: its bay painted on the ground in front of it, where they roll out.
                    node.AddChild(BayMarking.Mark(bay));
                }
                break;
            }
            case StaticKind.Prop when thing.Prop(Client.Content) is { } prop:
            {
                // G7: a barrel, crate or recycler, by its model's key; a running recycler's lamp.
                Vector3 half = new(prop.Deployable.HalfExtents.X, prop.Deployable.HalfExtents.Y, prop.Deployable.HalfExtents.Z);
                if (GroundModels.Provider.Deployable(prop.Key, half) is { } model)
                {
                    node.AddChild(model);
                    if (prop.Kind == WorldPropKind.Recycler && thing.Lit)
                    {
                        Lamp(node, model);
                    }
                }
                break;
            }
            case StaticKind.DroppedBag when GroundModels.Provider.Bag() is { } model:
                // Whatever it holds, a bag looks like a bag (GROUND.md §6.1).
                node.AddChild(model);
                break;
        }
        return node;
    }

    // A running recycler's lamp (G7): a small green light, glowing as bright by day as by night, with a little light of
    // its own (seen at night). Where the model's socket_lamp is; the stand-in model has none, so on its body's top front
    // corner, beside the hopper.
    private static void Lamp(Node3D node, Node3D model)
    {
        Vector3 at = Socket(model, "socket_lamp") ?? model.Transform * new Vector3(-0.62f, 1.28f, 0.4f);
        var glass = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.25f, 0.9f, 0.35f),
            EmissionEnabled = true,
            Emission = new Color(0.3f, 1f, 0.4f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        Glow.Track(glass, 1.4f);
        node.AddChild(new MeshInstance3D
        {
            Name = "Lamp",
            Mesh = new SphereMesh { Radius = 0.04f, Height = 0.08f, RadialSegments = 12, Rings = 6 },
            MaterialOverride = glass,
            Position = at,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        node.AddChild(new OmniLight3D
        {
            Name = "LampLight",
            LightColor = new Color(0.4f, 1f, 0.5f),
            LightEnergy = Glow.FireLight * 0.6f,
            OmniRange = 4,
            Position = at + new Vector3(0, 0.12f, 0),
        });
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

    private void UpdateArrows(double now)
    {
        int used = 0;
        ArrowSounds();
        Bullets();
        foreach (ClientProjectile p in Client.Survival().Projectiles)
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

    // Bullets in flight: a tracer for some (every third shot of each shooter, as in Rust) flying the bullet's own path
    // from where the gun is drawn; where each stops, the hit's effect and sound by what it hit (G4, GROUND.md §6.4, §9.3).
    private void Bullets()
    {
        foreach (ClientProjectile p in Client.Survival().Projectiles)
        {
            if (!p.IsBullet || RaidView.FliesAsModel(Client, p))
            {
                continue;
            }
            if (!_trails.ContainsKey(p))
            {
                int tracer = 0;
                uint shooter = p.Own ? Client.NetId : p.Shooter;
                if (p.EndedAt == null && FxDirector.WantsTracer(shooter, p.Seq))
                {
                    Vector3 from = p.LaunchConfirmed ? GroundView.ToGodot(p.Origin)
                        : (p.Own ? Muzzle?.Invoke() : TheirMuzzle?.Invoke(p.Shooter)) ?? GroundView.ToGodot(p.Origin);
                    string key = Client.Content.Item(p.Ammo)?.Bullet is { Burn: > 0 } ? "tracer.incendiary" : "tracer.bullet";
                    tracer = FxDirector.Tracer(key, from, GroundView.ToGodot(p.Velocity), GroundView.ToGodot(p.Gravity), p.Drag);
                    FxDirector.CorrectTracer(tracer, from, GroundView.ToGodot(p.Velocity),
                        GroundView.ToGodot(p.Gravity), p.Drag, Client.ClockSeconds - p.LaunchedAt);
                }
                _trails[p] = tracer;
                _trailRevisions[p] = p.Revision;
            }
            if (_trailRevisions.GetValueOrDefault(p) != p.Revision)
            {
                FxDirector.CorrectTracer(_trails[p], GroundView.ToGodot(p.Origin), GroundView.ToGodot(p.Velocity),
                    GroundView.ToGodot(p.Gravity), p.Drag, Client.ClockSeconds - p.LaunchedAt);
                _trailRevisions[p] = p.Revision;
            }
            if (p.EndedAt is { } end && _stopped.Add(p))
            {
                FxDirector.EndTracer(_trails[p], GroundView.ToGodot(end));
                if (p.EndTarget != TargetFamily.None)
                {
                    Impact(p, end, bullet: true);
                }
            }
        }
        Forget();
    }

    // What a bullet or arrow hit: the impact for its surface (wood, rock, metal, earth, grass, sand, water, a body's
    // blood; an incendiary round's flare), and for a bullet the sound of it.
    private void Impact(ClientProjectile p, Num.Vector3 end, bool bullet)
    {
        SurfaceContact contact = p.Contact;
        string surface = Client.Content.Surfaces.Key(contact.Surface);
        string? modifier = bullet && Client.Content.Item(p.Ammo)?.Bullet is { Burn: > 0 } ? "incendiary" : null;
        ContactResponse response = ContactFeedback.Data.Resolve(surface, bullet ? "bullet" : "arrow", modifier);
        Num.Vector3 point = contact.HasSurface ? contact.Point : end;
        Num.Vector3 n = contact.Normal;
        Func<Transform3D?>? decalPose = null;
        if (contact.Anchored)
        {
            TargetRef anchor = contact.Anchor;
            if (Client.Targets().TryPresentationPose(anchor, out TargetPose pose))
            {
                point = contact.PointAt(pose);
                n = contact.NormalAt(pose);
            }
            // Resolve through the existing target-family registry; doors, ship parts and
            // supported deployables all use their actual moving frame without view switches.
            decalPose = () => Client.Targets().TryPresentationPose(anchor, out TargetPose current)
                ? new Transform3D(new Basis(new Quaternion(current.Rotation.X, current.Rotation.Y, current.Rotation.Z, current.Rotation.W)),
                    GroundView.ToGodot(current.Position)) : null;
        }
        Vector3 at = GroundView.ToGodot(point), normal = GroundView.ToGodot(n), incoming = GroundView.ToGodot(p.DirectionAt(p.EndedTime));
        FxDirector.Play(response.Effect, at, normal, new FxParams { Direction = incoming, DecalPose = decalPose });
        Sounds.At(response.Sound, at);
        if (ContactFeedback.Data.Surface(surface).Flesh) Contacts.Note(contact.Target, Client.ClockSeconds);
    }

    // An arrow loosed (the string, its flight, its trail), and where it stops (GROUND.md §4.9).
    private void ArrowSounds()
    {
        foreach (ClientProjectile p in Client.Survival().Projectiles)
        {
            if (p.IsBullet)
            {
                continue;
            }
            if (_loosed.Add(p))
            {
                Vector3 from = GroundView.ToGodot(p.Origin);
                // Accepted arrows are never predicted (Own is false); Shooter identifies the local archer.
                // This view owns release audio for both listeners; the arms only play the release animation.
                if (p.Shooter == Client.NetId)
                {
                    Sounds.Flat("bow.release");
                }
                else
                {
                    Sounds.At("bow.release", from);
                }
                Sounds.At("arrow.flight", from);
                _trails[p] = p.EndedAt == null
                    ? FxDirector.Tracer("trail.arrow", from, GroundView.ToGodot(p.Velocity), GroundView.ToGodot(p.Gravity), p.Drag)
                    : 0;
                FxDirector.CorrectTracer(_trails[p], from, GroundView.ToGodot(p.Velocity),
                    GroundView.ToGodot(p.Gravity), p.Drag, Client.ClockSeconds - p.LaunchedAt);
            }
            if (p.EndedAt is { } end && _stopped.Add(p))
            {
                if (_trails.TryGetValue(p, out int trail))
                {
                    FxDirector.EndTracer(trail, GroundView.ToGodot(end));
                }
                if (p.EndTarget != TargetFamily.None)
                {
                    Impact(p, end, bullet: false);
                }
            }
        }
    }

    // Projectiles no longer in flight or held at their end are forgotten.
    private void Forget()
    {
        _forget.Clear();
        foreach (ClientProjectile p in _trails.Keys)
        {
            if (!Client.Survival().Projectiles.Contains(p))
            {
                _forget.Add(p);
            }
        }
        foreach (ClientProjectile p in _forget)
        {
            FxDirector.RemoveTracer(_trails[p]);
            _trailRevisions.Remove(p);
            _trails.Remove(p);
            _stopped.Remove(p);
            _loosed.Remove(p);
        }
    }
}
