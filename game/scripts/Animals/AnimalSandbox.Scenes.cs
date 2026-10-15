using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollow.Simulation;
using Hollow.Simulation.Animals;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Nature;
using Hollow.Simulation.Players;
using HollowUniverse.Fx;
using HollowUniverse.Items;
using Num = System.Numerics;

namespace HollowUniverse.Animals;

// The sandbox's scenes: each at a clearing near the landing site with trees behind it, found once from the zone's own
// natural objects (the same trees its rules and the surface view have), searched in metres east and south of the
// landing site along the ground (Zone.GroundAt, docs/WHOLE_WORLD.md §10.5).
public partial class AnimalSandbox
{
    /// <summary>How far from the landing site clearings are looked for, and how far apart the scenes are.</summary>
    private const float SearchRadius = 700, Apart = 230;

    private void FindClearings()
    {
        var trees = new TreeGrid(25);
        var objects = new List<NaturalObject>();
        _zone.Nature!.Query(_zone.Landing, SearchRadius + 100, objects);
        foreach (NaturalObject o in objects)
        {
            if (_content.Natural(o.Kind)?.Class == NaturalClass.Tree)
            {
                (float east, float south) = _zone.OffsetOf(o.Position);
                trees.Add(east, south);
            }
        }
        var found = new List<(Num.Vector3 Site, Num.Vector3 Toward)>();
        foreach (int needed in new[] { 8, 3, 0 })
        {
            for (float r = 0; r <= SearchRadius && found.Count < SceneNames.Length; r += 24)
            {
                int around = r == 0 ? 1 : (int)(MathF.Tau * r / 24);
                for (int i = 0; i < around && found.Count < SceneNames.Length; i++)
                {
                    float angle = MathF.Tau * i / around;
                    float x = r * MathF.Cos(angle), z = r * MathF.Sin(angle);
                    if (found.Any(f => Flat(f.Site, _zone.GroundAt(x, z)) < Apart))
                    {
                        continue;
                    }
                    if (Clearing(trees, x, z, needed, out Num.Vector3 toward))
                    {
                        found.Add((_zone.GroundAt(x, z), toward));
                    }
                }
            }
            if (found.Count == SceneNames.Length)
            {
                break;
            }
        }
        while (found.Count < SceneNames.Length)
        {
            // Nowhere better: side by side from the landing site.
            float x = found.Count * Apart;
            Num.Vector3 site = _zone.GroundAt(x, 0);
            found.Add((site, Facing.Compass(_zone.Frame.UpAt(site)).North));
        }
        GD.Print("Animal sandbox clearings: " + string.Join(", ", found.Select((f, i) => $"{SceneNames[i]} ({f.Site.X:0}, {f.Site.Z:0})")));
        AddHerd(found[0].Site, found[0].Toward);
        AddWolves(found[1].Site, found[1].Toward);
        AddBear(found[2].Site, found[2].Toward);
        AddHarvest(found[3].Site, found[3].Toward);
        (Num.Vector3 woods, Num.Vector3 into) = Woods(trees, found[4].Site, found[4].Toward);
        AddCooking(woods, into);
    }

    // Open (no tree within 20 m), gentle, dry, and with trees within 75 m to look across to.
    private bool Clearing(TreeGrid trees, float x, float z, int treesNear, out Num.Vector3 toward)
    {
        Num.Vector3 here = _zone.GroundAt(x, z);
        (Num.Vector3 north, Num.Vector3 east) = Facing.Compass(_zone.Frame.UpAt(here));
        toward = north;
        if (trees.Count(x, z, 20, out _) > 0)
        {
            return false;
        }
        int near = trees.Count(x, z, 75, out Num.Vector2 centroid);
        if (near < treesNear)
        {
            return false;
        }
        float h = Height(x, z);
        float steepest = 0;
        foreach ((float dx, float dz) in new[] { (12f, 0f), (-12f, 0f), (0f, 12f), (0f, -12f) })
        {
            steepest = MathF.Max(steepest, MathF.Abs(Height(x + dx, z + dz) - h) / 12f);
        }
        if (steepest > 0.17f || !Dry(x, z) || !Dry(x + 30, z) || !Dry(x - 30, z) || !Dry(x, z + 30) || !Dry(x, z - 30))
        {
            return false;
        }
        if (near > 0)
        {
            // Toward the trees, from offsets east and south to a direction along the ground here.
            Num.Vector3 d = east * (centroid.X - x) - north * (centroid.Y - z);
            if (d.LengthSquared() > 1)
            {
                toward = Num.Vector3.Normalize(d);
            }
        }
        return true;
    }

    // A campfire's place: among the trees (the grass is thin under them), clear for 3 m, near a clearing.
    private (Num.Vector3 Site, Num.Vector3 Toward) Woods(TreeGrid trees, Num.Vector3 near, Num.Vector3 toward)
    {
        (float nearEast, float nearSouth) = _zone.OffsetOf(_zone.Settle(near + toward * 30));
        for (float r = 0; r <= 120; r += 4)
        {
            int around = r == 0 ? 1 : (int)(MathF.Tau * r / 4);
            for (int i = 0; i < around; i++)
            {
                float angle = MathF.Tau * i / around;
                float x = nearEast + r * MathF.Cos(angle), z = nearSouth + r * MathF.Sin(angle);
                if (trees.Count(x, z, 3, out _) == 0 && trees.Count(x, z, 12, out _) >= 4 && Dry(x, z) &&
                    MathF.Abs(Height(x + 3, z) - Height(x - 3, z)) < 0.6f && MathF.Abs(Height(x, z + 3) - Height(x, z - 3)) < 0.6f)
                {
                    return (_zone.GroundAt(x, z), toward);
                }
            }
        }
        return (near, toward);
    }

    // The ground's distance from the centre there: differences of it are heights.
    private float Height(float x, float z) => (float)_zone.Frame.ToBody(_zone.GroundAt(x, z)).Length;

    private bool Dry(float x, float z)
    {
        Hollow.Surface.ISurface surface = _zone.Surface;
        if (surface.SeaRadius <= 0)
        {
            return true;
        }
        Num.Vector3 ground = _zone.GroundAt(x, z);
        return _zone.Frame.ToBody(ground).Length > surface.SeaRadius + 2.5;
    }

    // ---------------------------------------------------------------- the scenes

    private void AddHerd(Num.Vector3 site, Num.Vector3 toward)
    {
        Herd? herd = null;
        var scene = new Scene { Name = "herd", Site = site, Toward = toward, Hour = 9.5, Fov = 42 };
        scene.Build = () =>
        {
            herd = _zone.Animals().World!.SpawnHerd(_content.Animal("deer")!, site, 5, range: 40);
            // Boars rooting at the trees' edge across the clearing.
            Num.Vector3 edge = site + toward * 30 + Side(toward, _zone.Frame.UpAt(site)) * 14;
            _zone.Animals().World.SpawnHerd(_content.Animal("boar")!, _zone.Settle(edge), 2, range: 30);
        };
        scene.Camera = () =>
        {
            Num.Vector3 c = herd != null ? Centre(herd) : site;
            Num.Vector3 up = _zone.Frame.UpAt(c);
            return (c - toward * 11 + Side(toward, up) * 3f + up * 1.7f, c + up * 0.6f);
        };
        scene.Moment = () => _sceneTime > 8;
        _scenes.Add(scene);
    }

    private void AddWolves(Num.Vector3 site, Num.Vector3 toward)
    {
        Person? dummy = null;
        Herd? pack = null;
        var scene = new Scene { Name = "wolves", Site = site, Toward = toward, Hour = 16.5, Fov = 38 };
        scene.Build = () =>
        {
            dummy = AddPerson("Dummy", site, YawOf(toward, site), dummy: true);
            pack = _zone.Animals().World!.SpawnHerd(_content.Animal("wolf")!, _zone.Settle(site + toward * 42), 4,
                range: 40, facing: YawOf(-toward, site));
            foreach (Animal w in pack.Members)
            {
                w.Hunger = 0.95f;
            }
        };
        scene.Camera = () =>
        {
            Num.Vector3 d = dummy != null ? dummy.Current : site;
            Num.Vector3 c = pack != null ? Centre(pack) : site + toward * 40;
            Num.Vector3 up = _zone.Frame.UpAt(d);
            // Over the dummy's shoulder, a little above the grass, at the pack coming in.
            return (d - toward * 2.8f + Side(toward, up) * 0.75f + up * 2.3f, Num.Vector3.Lerp(d, c, 0.62f) + up * 0.45f);
        };
        // Stalking: the pack trotting in, its middle 9 to 15 m off.
        scene.Moment = () => dummy != null && pack != null && pack.Members.All(w => w.State == AnimalState.Chase) &&
            Flat(Centre(pack), dummy.Current) is > 9 and < 15;
        _scenes.Add(scene);
    }

    private void AddBear(Num.Vector3 site, Num.Vector3 toward)
    {
        Person? dummy = null;
        Animal? bear = null;
        var scene = new Scene { Name = "bear", Site = site, Toward = toward, Hour = 17.5, Fov = 40 };
        scene.Build = () =>
        {
            dummy = AddPerson("Dummy", site, YawOf(toward, site), dummy: true);
            bear = _zone.Animals().World!.SpawnHerd(_content.Animal("bear")!, _zone.Settle(site + toward * 13.5f), 1,
                range: 30, facing: YawOf(-toward, site)).Leader!;
            bear.Hunger = 0;
        };
        scene.Camera = () =>
        {
            Num.Vector3 d = dummy != null ? dummy.Current : site;
            Num.Vector3 b = bear?.Position ?? site + toward * 13;
            Num.Vector3 up = _zone.Frame.UpAt(d);
            // Over the dummy's shoulder: the bear rearing at it, the dummy in the near corner.
            return (d - toward * 4f + Side(toward, up) * 2.2f + up * 1.45f, Num.Vector3.Lerp(d, b, 0.8f) + up * 1.0f);
        };
        // The warning: rearing, half a second in.
        scene.Moment = () => bear is { Warning: true } && _animals.ViewOf((uint)bear.Id.Value) is { Playing: "Threaten", PlayingFor: > 0.6 };
        scene.Trace = () => bear != null && dummy != null ? $"bear {Flat(bear.Position, dummy.Current):0.0} m from the dummy, {bear.State}{(bear.Warning ? " warning" : "")}" : "";
        _scenes.Add(scene);
    }

    private void AddHarvest(Num.Vector3 site, Num.Vector3 toward)
    {
        Person? butcher = null;
        Animal? corpse = null;
        double gone = 0;
        double lastSwing = 0;
        byte swings = 0;
        var scene = new Scene { Name = "harvest", Site = site, Toward = toward, Hour = 11 };
        void NewCorpse()
        {
            Num.Vector3 up = _zone.Frame.UpAt(site);
            Num.Vector3 at = site + toward * 1.3f;
            corpse = _zone.Animals().World!.SpawnHerd(_content.Animal("deer")!, _zone.Settle(at), 1, range: 5,
                facing: YawOf(Side(toward, up), at)).Leader!;
            Player p = butcher!.Player;
            _zone.Animals().DamageAnimal(corpse, 1000, HitRegion.Body, butcher.Current + up * 1.2f, p, p.Id, p.Name);
            _zone.Events.Clear();
        }
        scene.Build = () =>
        {
            butcher = AddPerson("Butcher", site, YawOf(toward, site), held: "bone-knife");
            NewCorpse();
        };
        scene.Tick = () =>
        {
            if (butcher == null || corpse == null)
            {
                return;
            }
            ref Character c = ref _zone.Entities.Get<Character>(butcher.Entity);
            c.Input = new MoveInput { Buttons = MoveButtons.Crouch, View = c.Input.View };
            if (corpse.Removed)
            {
                // Used up: another in a moment.
                c.Actions = new ActionInput { HeldSlot = 0 };
                gone += TickSeconds;
                if (gone > 2.5)
                {
                    gone = 0;
                    NewCorpse();
                }
                return;
            }
            Look(butcher, corpse.Position + _zone.Frame.UpAt(corpse.Position) * 0.25f);
            c.Actions = new ActionInput { HeldSlot = 0, Buttons = ActionButtons.Primary };
            if (butcher.Player.ActionCount != swings)
            {
                swings = butcher.Player.ActionCount;
                lastSwing = _elapsed;
            }
        };
        scene.Camera = () =>
        {
            Num.Vector3 b = corpse?.Position ?? site + toward;
            Num.Vector3 up = _zone.Frame.UpAt(b);
            return (b + Side(toward, up) * 2.3f - toward * 1.7f + up * 1.3f, b + up * 0.35f - toward * 0.45f);
        };
        // Mid-butchering: a few cuts in, the knife going in.
        scene.Moment = () => corpse is { Removed: false } && corpse.HarvestFraction < 0.8f && _elapsed - lastSwing is > 0.12 and < 0.3;
        _scenes.Add(scene);
    }

    private void AddCooking(Num.Vector3 site, Num.Vector3 toward)
    {
        Node3D? cooking = null;
        int stage = -1;
        Num.Vector3 up = _zone.Frame.UpAt(site);
        Vector3 fire = ToRender(site);
        Vector3 upR = ToRender(up);
        Basis level = Characters.CharacterFrame.BasisOf(upR, Characters.CharacterFrame.YawToward(upR, ToRender(toward)));
        float rim = 0.38f;
        var scene = new Scene { Name = "cooking", Site = site, Toward = toward, Hour = 18.3 };
        var wood = new StandardMaterial3D { AlbedoColor = new Color(0.33f, 0.23f, 0.15f), Roughness = 0.9f };
        // A green stick pushed into the ground outside the ring and leaning over the flames, a piece of meat on its end:
        // roasting, drawn above the grass. (The game's campfire cooks in its slots; this shows what cooks in them.)
        (Vector3 At, Basis Along) Skewer(float angle, float reach, float height)
        {
            Vector3 outward = level * new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle));
            Vector3 foot = fire + outward * (rim + 0.3f);
            Vector3 tip = fire + outward * reach + upR * height;
            Vector3 y = (tip - foot).Normalized();
            Vector3 x = y.Cross(upR).Normalized();
            var along = new Basis(x, y, x.Cross(y).Normalized());
            var stick = new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.007f, BottomRadius = 0.011f, Height = (tip - foot).Length(), RadialSegments = 6, Rings = 1 },
                MaterialOverride = wood,
            };
            AddChild(stick);
            stick.GlobalTransform = new Transform3D(along, (foot + tip) / 2);
            return (foot + (tip - foot) * 0.82f, along);
        }
        Node3D? Meat(string key, (Vector3 At, Basis Along) on)
        {
            if (ModelCatalog.Instantiate(key) is not { } node)
            {
                return null;
            }
            AddChild(node);
            node.GlobalTransform = new Transform3D(on.Along, on.At);
            return node;
        }
        (Vector3, Basis) middle = default;
        scene.Build = () =>
        {
            if (ModelCatalog.Instantiate("campfire") is { } campfire)
            {
                AddChild(campfire);
                campfire.GlobalTransform = new Transform3D(level, fire);
                Aabb box = ModelCatalog.LocalBounds(campfire);
                rim = Math.Clamp(MathF.Max(box.Size.X, box.Size.Z) * 0.38f, 0.25f, 0.6f);
                _audio?.Loop("campfire.loop", campfire);
            }
            FxDirector.Start("fire.campfire", fire, upR);
            Meat("cooked-venison", Skewer(4.0f, 0.16f, 0.5f));
            Meat("raw-pork", Skewer(2.2f, 0.2f, 0.46f));
            Meat("burnt-wolf-meat", Skewer(5.4f, 0.18f, 0.44f));
            middle = Skewer(3.1f, 0.12f, 0.52f);
            // The cook across the fire, facing it (and the camera), a cooked steak in hand.
            Person cook = AddPerson("Cook", site + toward * 1.3f + Side(toward, up) * 0.45f, YawOf(-toward, site), held: "cooked-venison");
            Look(cook, site + up * 0.9f);
        };
        // One piece cooks through as the rules have it: raw for 15 s, cooked for 45 s, then burnt.
        scene.Frame = _ =>
        {
            ItemDef raw = _content.ItemByKey("raw-venison");
            double cook = raw.Cook?.Seconds ?? 15;
            double burn = _content.ItemByKey("cooked-venison").Cook?.Seconds ?? 45;
            double t = _sceneTime % (cook + burn + 15);
            int now = t < cook ? 0 : t < cook + burn ? 1 : 2;
            if (now == stage || middle == default)
            {
                return;
            }
            stage = now;
            cooking?.QueueFree();
            cooking = Meat(now switch { 0 => "raw-venison", 1 => "cooked-venison", _ => "burnt-venison" }, middle);
        };
        scene.Camera = () => (site - toward * 2.3f + Side(toward, up) * 0.9f + up * 1.75f, site + toward * 0.55f + up * 0.45f);
        scene.Moment = () => _sceneTime > 4;
        _scenes.Add(scene);
    }

    // ---------------------------------------------------------------- helpers

    private static Num.Vector3 Centre(Herd herd)
    {
        Num.Vector3 sum = Num.Vector3.Zero;
        int n = 0;
        foreach (Animal a in herd.Members)
        {
            if (!a.Removed)
            {
                sum += a.Position;
                n++;
            }
        }
        return n > 0 ? sum / n : herd.Home;
    }

    /// <summary>The right hand of a flat heading.</summary>
    private static Num.Vector3 Side(Num.Vector3 toward, Num.Vector3 up) => Num.Vector3.Normalize(Num.Vector3.Cross(toward, up));

    // The distance between two points near each other on the ground (straight, as good as along the ground within a
    // kilometre on a 2.41 km world).
    private static float Flat(Num.Vector3 a, Num.Vector3 b) => Num.Vector3.Distance(a, b);

    /// <summary>Tree trunks in buckets, for counting those near a point.</summary>
    private sealed class TreeGrid(float cell)
    {
        private readonly Dictionary<(int, int), List<Num.Vector2>> _cells = new();

        public void Add(float x, float z)
        {
            var key = ((int)MathF.Floor(x / cell), (int)MathF.Floor(z / cell));
            if (!_cells.TryGetValue(key, out List<Num.Vector2>? list))
            {
                _cells[key] = list = new List<Num.Vector2>();
            }
            list.Add(new Num.Vector2(x, z));
        }

        public int Count(float x, float z, float radius, out Num.Vector2 centroid)
        {
            int n = 0;
            Num.Vector2 sum = Num.Vector2.Zero;
            int x0 = (int)MathF.Floor((x - radius) / cell), x1 = (int)MathF.Floor((x + radius) / cell);
            int z0 = (int)MathF.Floor((z - radius) / cell), z1 = (int)MathF.Floor((z + radius) / cell);
            for (int j = z0; j <= z1; j++)
            {
                for (int i = x0; i <= x1; i++)
                {
                    if (!_cells.TryGetValue((i, j), out List<Num.Vector2>? list))
                    {
                        continue;
                    }
                    foreach (Num.Vector2 t in list)
                    {
                        if ((t.X - x) * (t.X - x) + (t.Y - z) * (t.Y - z) <= radius * radius)
                        {
                            sum += t;
                            n++;
                        }
                    }
                }
            }
            centroid = n > 0 ? sum / n : new Num.Vector2(x, z);
            return n;
        }
    }
}
