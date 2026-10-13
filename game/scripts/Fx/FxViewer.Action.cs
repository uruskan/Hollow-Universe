using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using HollowUniverse.Items;

namespace HollowUniverse.Fx;

/// <summary>
/// The in-action scenes (<c>--action &lt;family&gt;</c>): each family as it would be seen in play, caught at a telling
/// moment: a rifle fired in first person with others firing back, tracers across a dusk sky, a burst into wood and
/// metal, a hit on a body, ore struck with a pickaxe, a timed charge on a stone wall, a camp at night, a wall coming down.
/// </summary>
public partial class FxViewer
{
    private double _captureAt = -1;
    private readonly List<(Node3D Node, Vector3 Velocity)> _movers = new();

    private void StartAction(string family)
    {
        _fx.DistanceLod = false;
        _hud.Visible = false;
        _camera.Fov = 70;
        switch (family)
        {
            case "muzzle":
                ActionMuzzle();
                break;
            case "tracer":
                ActionTracer();
                break;
            case "impact":
                ActionImpact();
                break;
            case "blood":
                ActionBlood();
                break;
            case "gather":
                ActionGather();
                break;
            case "explosion":
                ActionExplosion();
                break;
            case "fire":
                ActionFire();
                break;
            case "world":
                ActionWorld();
                break;
            default:
                GD.PushError($"--action: no scene for '{family}'.");
                break;
        }
        if (Options.TryGetValue("at", out string? at))
        {
            _captureAt = double.Parse(at, CultureInfo.InvariantCulture);
        }
    }

    private void UpdateAction(double delta)
    {
        if (!_fx.Paused)
        {
            foreach ((Node3D node, Vector3 v) in _movers)
            {
                node.GlobalPosition += v * (float)delta;
            }
        }
        if (_captureAt > 0 && _clock >= _captureAt && !_fx.Paused)
        {
            _fx.Paused = true;
            At(0.12, () => ReadyForShot = true);
        }
    }

    /// <summary>A gun held in first person on the camera, as the view model draws it; returns its muzzle socket.</summary>
    private Node3D? HoldInFirstPerson(string key)
    {
        Node3D? gun = ModelCatalog.Instantiate(key);
        ModelEntry? entry = ModelCatalog.Find(key);
        if (gun == null || entry?.Held == null)
        {
            return null;
        }
        ModelCatalog.ForceLod(gun, 0);
        PrepareViewmodel(gun);
        _camera.AddChild(gun);
        gun.Transform = entry.Held.FirstPerson.Transform;
        return gun.FindChild("socket_muzzle", true, false) as Node3D ?? gun;
    }

    /// <summary>A plain figure holding a gun at chest height, facing <paramref name="facing"/>; returns the muzzle.</summary>
    private Node3D Gunman(Vector3 at, Vector3 facing, string gun = "rifle")
    {
        var body = new Node3D { Position = at };
        AddChild(body);
        body.LookAt(at + new Vector3(facing.X, 0, facing.Z), Vector3.Up);
        var cloth = new StandardMaterial3D { AlbedoColor = new Color(0.3f, 0.29f, 0.25f), Roughness = 0.9f };
        body.AddChild(new MeshInstance3D { Mesh = new CapsuleMesh { Radius = 0.24f, Height = 1.78f }, Position = new Vector3(0, 0.89f, 0), MaterialOverride = cloth });
        body.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.12f, Height = 0.24f }, Position = new Vector3(0, 1.72f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.42f, 0.34f), Roughness = 0.8f } });
        // The gun points along the body's -Z (its front, after LookAt): the held-item frame's +X turned onto -Z.
        var hand = new Node3D { Position = new Vector3(0.18f, 1.38f, -0.25f), Rotation = new Vector3(0, MathF.PI / 2, 0) };
        body.AddChild(hand);
        Node3D? model = ModelCatalog.Instantiate(gun);
        if (model == null)
        {
            return hand;
        }
        hand.AddChild(model);
        return model.FindChild("socket_muzzle", true, false) as Node3D ?? hand;
    }

    private void Burst(Node3D muzzle, bool firstPerson, string key, double start, int shots, double interval)
    {
        for (int i = 0; i < shots; i++)
        {
            At(start + i * interval, () =>
            {
                if (IsInstanceValid(muzzle))
                {
                    FxDirector.Muzzle(key, muzzle, firstPerson);
                }
            });
        }
    }

    // ---------------------------------------------------------------- the scenes

    private void ActionMuzzle()
    {
        // In first person with a rifle; two of the other side fire back from behind cover, one more from the right.
        FxStage.Build(this, "wood", new Vector3(-2.5f, 0, -11), 0f);
        FxStage.Build(this, "metal", new Vector3(3.2f, 0, -13), 0.2f);
        FxStage.Build(this, "wall", new Vector3(-7f, 0, -15), 0.35f);
        FxStage.Build(this, "rock", new Vector3(6.5f, 0, -8.5f), 0.6f);
        _camera.Position = new Vector3(0, 1.62f, 0);
        _camera.LookAt(new Vector3(0.4f, 1.3f, -11), Vector3.Up);
        Node3D? mine = HoldInFirstPerson("rifle");
        Node3D a = Gunman(new Vector3(-1.9f, 0, -12.4f), new Vector3(0.12f, 0, 1));
        Node3D b = Gunman(new Vector3(4.4f, 0, -14.3f), new Vector3(-0.25f, 0, 1), "pistol");
        Node3D c = Gunman(new Vector3(5.6f, 0, -6.2f), new Vector3(-1, 0, 0.35f));
        if (mine != null)
        {
            Burst(mine, true, "muzzle.rifle", 1.0, 9, 60.0 / 450.0);
        }
        Burst(a, false, "muzzle.rifle", 1.15 - 2 * 60.0 / 450.0, 9, 60.0 / 450.0);
        Burst(b, false, "muzzle.pistol", 1.3 - 0.15, 6, 0.15);
        Burst(c, false, "muzzle.rifle.brake", 1.0 + 0.004, 9, 60.0 / 450.0);
        _captureAt = 1.0 + 8 * 60.0 / 450.0 + 0.004;
    }

    private void ActionTracer()
    {
        // Dusk: two lines firing across the view at each other, tracers crossing, sparks where they land.
        Twilight();
        FxStage.Build(this, "metal", new Vector3(13, 0, -7), -MathF.PI / 2);
        FxStage.Build(this, "wall", new Vector3(-13, 0, -9), MathF.PI / 2);
        FxStage.Build(this, "wood", new Vector3(13.5f, 0, -12.5f), -MathF.PI / 2);
        _camera.Position = new Vector3(0.5f, 0.75f, 1.5f);
        _camera.LookAt(new Vector3(0, 2.2f, -9), Vector3.Up);
        var random = new FxRandom(5);
        for (int i = 0; i < 26; i++)
        {
            int side = i % 2 == 0 ? 1 : -1;
            double t = 0.6 + i * 0.035 + random.Range(0, 0.03f);
            var from = new Vector3(side * -26f, 1.6f + random.Range(-0.2f, 0.4f), -5f + random.Range(-3, 3));
            var to = new Vector3(side * 13f, random.Range(0.6f, 2.6f), -7f + random.Range(-3, 3));
            Vector3 v = (to - from).Normalized() * 300f;
            float flight = (to - from).Length() / 300f;
            string key = i % 5 == 0 ? "tracer.incendiary" : "tracer.bullet";
            At(t, () =>
            {
                int id = FxDirector.Tracer(key, from, v, new Vector3(0, -9f, 0), 0.3f);
                At(flight, () =>
                {
                    FxDirector.EndTracer(id, to);
                    FxDirector.Impact(FxSurface.Metal, to, new Vector3(-MathF.Sign(to.X), 0, 0), v.Normalized());
                });
            });
        }
        _captureAt = 0.6 + 26 * 0.035 * 0.62;
    }

    /// <summary>Dusk: the sun just down, a dim warm horizon, a dark blue sky.</summary>
    private void Twilight()
    {
        Godot.Environment env = _environment.Environment;
        var sky = (ProceduralSkyMaterial)env.Sky.SkyMaterial;
        sky.SkyTopColor = new Color(0.05f, 0.07f, 0.14f);
        sky.SkyHorizonColor = new Color(0.42f, 0.24f, 0.16f);
        sky.GroundHorizonColor = new Color(0.1f, 0.08f, 0.07f);
        sky.GroundBottomColor = new Color(0.03f, 0.03f, 0.03f);
        env.AmbientLightEnergy = 0.8f;
        env.FogLightColor = new Color(0.16f, 0.12f, 0.11f);
        env.TonemapExposure = 1.9f;
        _sun.LightColor = new Color(1f, 0.5f, 0.3f);
        _sun.LightEnergy = 0.12f;
        _sun.LookAtFromPosition(new Vector3(0.2f, 0.12f, -1f), Vector3.Zero, Vector3.Up);
    }

    private void ActionImpact()
    {
        // A burst stitched across a wooden wall, a sheet-metal wall and the earth in front of them.
        FxStation wood = FxStage.Build(this, "wood", new Vector3(-1.7f, 0, -5), 0.12f);
        FxStation metal = FxStage.Build(this, "metal", new Vector3(1.7f, 0, -5.3f), -0.1f);
        _camera.Position = new Vector3(0.35f, 1.35f, -1.9f);
        _camera.LookAt(new Vector3(0, 0.75f, -5.1f), Vector3.Up);
        var random = new FxRandom(17);
        double t = 0.8;
        for (int i = 0; i < 14; i++)
        {
            FxStation st = i < 7 ? wood : metal;
            FxSurface surface = i < 7 ? FxSurface.Wood : FxSurface.Metal;
            Vector3 p;
            Vector3 n;
            if (i % 4 == 3)
            {
                p = new Vector3(random.Range(-2.2f, 2.2f), 0.02f, random.Range(-4.6f, -3.6f));
                n = Vector3.Up;
                surface = FxSurface.Dirt;
            }
            else
            {
                (p, n) = st.Scatter!(random);
            }
            Vector3 dir = (p - _camera.Position).Normalized();
            FxSurface s = surface;
            At(t, () => FxDirector.Impact(s, p, n, dir));
            t += 60.0 / 450.0;
        }
        _captureAt = t - 60.0 / 450.0 + 0.035;
    }

    private void ActionBlood()
    {
        FxStation dummy = FxStage.Build(this, "flesh", new Vector3(0, 0, -3), 0.5f);
        FxStage.Build(this, "dirt", new Vector3(0, 0, -3), 0f);
        _camera.Position = dummy.Root.GlobalPosition + dummy.Root.Basis * new Vector3(-0.95f, 1.45f, 1.35f);
        _camera.LookAt(dummy.Look + new Vector3(0, -0.15f, 0), Vector3.Up);
        Vector3 chest = dummy.Spot;
        At(0.6, () => FxDirector.Play("blood.hit", chest + new Vector3(0, 0.1f, 0), dummy.Normal, new FxParams { Direction = dummy.Direction }));
        At(1.4, () => FxDirector.Play("blood.hit.heavy", chest + new Vector3(0.02f, -0.06f, 0), dummy.Normal, new FxParams { Direction = dummy.Direction }));
        for (int i = 0; i < 6; i++)
        {
            Vector3 g = dummy.Root.GlobalPosition + new Vector3(0.12f * MathF.Sin(i * 2.1f), 0.04f, 0.15f + 0.08f * MathF.Cos(i * 1.7f));
            At(0.2 + i * 0.12, () => FxDirector.Play("blood.drip", g, Vector3.Up));
        }
        _captureAt = 1.48;
    }

    private void ActionGather()
    {
        // A pickaxe striking metal ore in first person: sparks, chips, dust; the stroke before left its dust hanging.
        Node3D? node = ModelCatalog.Instantiate("metal-node", 0, ModelUse.Static);
        if (node != null)
        {
            node.Position = new Vector3(0, 0, -2.3f);
            node.Rotation = new Vector3(0, 0.6f, 0);
            AddChild(node);
        }
        _camera.Position = new Vector3(0.1f, 1.55f, 0);
        _camera.LookAt(new Vector3(0, 0.75f, -2.0f), Vector3.Up);
        Node3D? pick = ModelCatalog.Instantiate("metal-pickaxe");
        if (pick != null)
        {
            ModelCatalog.ForceLod(pick, 0);
            PrepareViewmodel(pick);
            _camera.AddChild(pick);
            // Mid-swing: the head down at the rock.
            pick.Transform = new Transform3D(Basis.FromEuler(new Vector3(Mathf.DegToRad(-58), Mathf.DegToRad(100), Mathf.DegToRad(-25))), new Vector3(0.24f, -0.3f, -0.62f));
        }
        var hit = new Vector3(-0.05f, 0.85f, -1.45f);
        Vector3 normal = new Vector3(0.1f, 0.45f, 1f).Normalized();
        At(0.6, () => FxDirector.Play("gather.sparks", hit + new Vector3(0.15f, -0.1f, -0.05f), normal));
        At(1.5, () => FxDirector.Play("gather.sparks", hit, normal));
        At(1.5, () => FxDirector.Play("gather.rock", hit, normal));
        _captureAt = 1.56;
    }

    private void ActionExplosion()
    {
        // A timed charge on a stone wall, seen from behind a low wall across the yard.
        FxStation wall = FxStage.Build(this, "wall", new Vector3(0, 0, -11), 0f);
        FxStage.Build(this, "wood", new Vector3(-4.5f, 0, -3.5f), 0.3f);
        Node3D? charge = ModelCatalog.Instantiate("timed-charge");
        Vector3 at = wall.Root.GlobalPosition + new Vector3(0.2f, 1.15f, 0.16f);
        if (charge != null)
        {
            charge.Position = at;
            AddChild(charge);
        }
        _camera.Position = new Vector3(-5.2f, 1.95f, -2.6f);
        _camera.LookAt(new Vector3(0.4f, 1.7f, -10.5f), Vector3.Up);
        At(1.0, () =>
        {
            charge?.QueueFree();
            FxDirector.Play("explosion.timed-charge", at, Vector3.Back, new FxParams { Floor = 0f });
        });
        _captureAt = 1.0 + 0.26;
    }

    private void ActionFire()
    {
        // Night at camp: a campfire close by, a furnace behind it, a torch in hand.
        _night = true;
        ApplyNight();
        Node3D? fire = ModelCatalog.Instantiate("campfire");
        Node3D? furnace = ModelCatalog.Instantiate("furnace");
        if (fire != null)
        {
            fire.Position = new Vector3(0.3f, 0, -2.6f);
            AddChild(fire);
            FxDirector.Start("fire.campfire", fire.GlobalPosition, Vector3.Up, new FxParams { Follow = fire });
        }
        if (furnace != null)
        {
            furnace.Position = new Vector3(-2.4f, 0, -5.6f);
            furnace.Rotation = new Vector3(0, 0.5f, 0);
            AddChild(furnace);
            FxDirector.Start("fire.furnace", furnace.GlobalPosition, Vector3.Up, new FxParams { Follow = furnace });
        }
        FxStage.Build(this, "wood", new Vector3(2.6f, 0, -6.5f), -0.35f);
        _camera.Position = new Vector3(0, 1.6f, 0.2f);
        _camera.LookAt(new Vector3(-0.3f, 0.6f, -4f), Vector3.Up);
        Node3D? torch = ModelCatalog.Instantiate("torch");
        ModelEntry? entry = ModelCatalog.Find("torch");
        if (torch != null && entry?.Held != null)
        {
            ModelCatalog.ForceLod(torch, 0);
            PrepareViewmodel(torch);
            _camera.AddChild(torch);
            torch.Transform = entry.Held.FirstPerson.Transform;
            if (torch.FindChild("socket_flame", true, false) is Node3D flame)
            {
                FxDirector.Start("fire.torch", flame.GlobalPosition, Vector3.Up, new FxParams { Follow = flame, FirstPerson = true });
            }
        }
        _captureAt = 3.0;
    }

    private void ActionWorld()
    {
        // A stone wall gives way in a burst of dust while a wooden wall beside it is upgraded to stone.
        FxStation wall = FxStage.Build(this, "wall", new Vector3(-1.6f, 0, -9), 0.15f);
        FxStation wood = FxStage.Build(this, "wood", new Vector3(3.4f, 0, -7.5f), -0.3f);
        _camera.Position = new Vector3(1.0f, 1.7f, 0.5f);
        _camera.LookAt(new Vector3(0.2f, 1.4f, -8.5f), Vector3.Up);
        Vector3 centre = wall.Root.GlobalPosition + Vector3.Up * 1.5f;
        At(1.0, () =>
        {
            wall.Root.Visible = false;
            FxDirector.Play("world.collapse", centre, Vector3.Up, new FxParams { Direction = wall.Root.Basis.X, Area = new Vector3(1.5f, 1.5f, 0.2f), Floor = 0f });
        });
        At(1.45, () => FxDirector.Play("world.upgrade.stone", wood.Root.GlobalPosition + Vector3.Up * 0.75f + wood.Root.Basis.Z * 0.16f, wood.Root.Basis.Z,
            new FxParams { Direction = wood.Root.Basis.X, Area = new Vector3(1.5f, 0.15f, 0.75f) }));
        _captureAt = 1.62;
    }
}
