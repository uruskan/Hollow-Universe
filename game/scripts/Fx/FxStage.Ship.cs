using System;
using System.Collections.Generic;
using Godot;
using HollowUniverse.Items;

namespace HollowUniverse.Fx;

/// <summary>
/// How a battery launches its rocket (IRON_WAKE.md §6.4): the speed the gun gives it, how far out its motor lights, the
/// motor's push and how long it burns.
/// </summary>
public readonly record struct FxRocketLaunch(float MuzzleSpeed, float IgnitionDistance, float Acceleration, float Burn)
{
    /// <summary>The siege battery's rocket: 70 m/s, lit 4 m out, +60 m/s² for 1.2 s.</summary>
    public static readonly FxRocketLaunch Siege = new(70f, 4f, 60f, 1.2f);

    /// <summary>The defense mount's rocket: 35 m/s, lit 3 m out, +40 m/s² for 1.0 s.</summary>
    public static readonly FxRocketLaunch Defense = new(35f, 3f, 40f, 1.0f);
}

/// <summary>
/// The Iron Wake's stations in the effects viewer (IRON_WAKE.md §11, GROUND.md §9.3): stand-ins for its batteries at
/// their real size (the asset's siege battery, scale 1.05: two barrels 2 m apart, the muzzles 2.3 m over the deck; a
/// defense mount, scale 0.43: three tubes 0.275 m apart), a patch of its hull plate, a lift nozzle over the ground, and
/// open ground for a fire. Each station plays its effects the way the ship's views will:
/// <list type="bullet">
/// <item><c>siege</c>, <c>defense</c>: on a barrel's muzzle socket (+X along the barrel), as <c>FxDirector.Muzzle</c>;
/// the defense mount ripples its three barrels a quarter second apart.</item>
/// <item><c>siege-deck</c>: on the deck under the first muzzle, the barrel's direction along it.</item>
/// <item><c>siege-breech</c>: on the first barrel's breech socket (+X out of the open breech, backwards).</item>
/// <item><c>ignite</c>: a rocket leaves the first barrel at the battery's 70 m/s and its motor lights 4 m out: the effect
/// on its tail there, then <c>trail.rocket</c> while the motor burns.</item>
/// <item><c>hull</c>: hits scattered over the plate; <c>damaged</c>: on the siege battery's gun house; <c>wash</c> and
/// <c>patch</c>: on the ground.</item>
/// </list>
/// Their sockets are named in <see cref="FxStation.Sockets"/> (muzzle0, muzzle1, breech0, breech1, deck0, house for the
/// siege battery; muzzle0 to muzzle2 for the defense mount), so the scenes and the measurement fire them too.
/// </summary>
public static partial class FxStage
{
    /// <summary>The Iron Wake's station kinds, in the order the turntable lays them out.</summary>
    public static readonly string[] ShipKinds = { "siege", "siege-deck", "siege-breech", "ignite", "defense", "hull", "damaged", "wash", "patch" };

    /// <summary>The deck's top over the station's ground.</summary>
    private const float DeckTop = 0.3f;

    private static readonly Dictionary<string, Material> ShipMaterials = new(StringComparer.Ordinal);

    // The stand-ins' finishes: the monuments' tiling plates mapped in world space, in the ship's colours.
    private static readonly Dictionary<string, (string Texture, float Tile, Color Albedo, float Metallic, float Roughness)> Finishes =
        new(StringComparer.Ordinal)
        {
            ["paint"] = ("panel", 2.5f, new Color(0.5f, 0.64f, 0.58f), 0.3f, 0.6f),
            ["deck"] = ("panel", 2.4f, new Color(0.62f, 0.63f, 0.64f), 0.55f, 0.6f),
            ["gunmetal"] = ("hull", 3f, new Color(0.36f, 0.37f, 0.38f), 0.85f, 0.42f),
        };

    // Each station's camera yaw is from its front (0: beside the barrels, 90°: before the muzzles) less the 33° the
    // turntable swings a still camera by in a shot (FxViewer.UpdateTurntable), so a picture sees it as framed here.
    private static void BuildShip(FxStation s, Node3D root, string kind)
    {
        switch (kind)
        {
            case "siege":
            case "siege-deck":
            case "siege-breech":
            case "ignite":
            case "damaged":
                BuildSiege(s, root, kind);
                break;
            case "defense":
            {
                Node3D[] muzzles = DefenseMount(root, s);
                Node3D middle = muzzles[1];
                Vector3 along = middle.GlobalBasis.X.Normalized();
                (s.Spot, s.Normal, s.Direction, s.Follow) = (middle.GlobalPosition, along, along, middle);
                s.Look = middle.GlobalPosition + along * 1.4f;
                s.Distance = 5.5f;
                s.Pitch = 7f;
                s.Yaw += Mathf.DegToRad(7f);
                s.FullTurn = false;
                // A ripple: the three barrels a quarter second apart (IRON_WAKE.md §6.3).
                s.Play = (def, p, _, later) =>
                {
                    if (def.IsLoop)
                    {
                        return On(def, middle, p);
                    }
                    for (int i = 0; i < muzzles.Length; i++)
                    {
                        Node3D barrel = muzzles[i];
                        later(0.25 * i, () => On(def, barrel, p));
                    }
                    return default;
                };
                break;
            }
            case "hull":
            {
                Basis basis = root.Basis;
                Vector3 front = basis.Z;
                Vector3 centre = HullPlate(root);
                (s.Spot, s.Normal, s.Direction) = (centre, front, -front);
                s.Look = centre + front * 0.15f + Vector3.Down * 0.05f;
                s.Distance = 1.2f;
                s.Pitch = 6f;
                s.FullTurn = false;
                s.Area = new Vector3(1.2f, 0.8f, 0f);
                Func<FxRandom, (Vector3, Vector3)> scatter = r => (centre + basis * new Vector3(r.Range(-0.6f, 0.6f), r.Range(-0.45f, 0.45f), 0f), front);
                s.Scatter = scatter;
                s.Play = (def, p, random, _) =>
                {
                    (Vector3 hit, Vector3 n) = scatter(random);
                    // Shots come in from in front, a little from the side and above.
                    Vector3 incoming = (-front + basis.X * random.Range(-0.5f, 0.5f) + Vector3.Down * random.Range(0f, 0.3f)).Normalized();
                    return Here(def, hit, n, incoming, p);
                };
                break;
            }
            case "wash":
            {
                Vector3 ground = LiftNozzle(root);
                (s.Spot, s.Normal) = (ground, Vector3.Up);
                s.Look = ground + Vector3.Up * 3.2f;
                s.Distance = 27f;
                s.Pitch = 11f;
                s.Play = (def, p, _, _) => Here(def, ground, Vector3.Up, Vector3.Zero, p);
                break;
            }
            case "patch":
            {
                Vector3 ground = root.GlobalPosition + Vector3.Up * 0.02f;
                (s.Spot, s.Normal) = (ground, Vector3.Up);
                s.Look = ground + Vector3.Up * 1.3f;
                s.Distance = 12f;
                s.Pitch = 15f;
                s.Play = (def, p, _, _) => Here(def, ground, Vector3.Up, Vector3.Zero, p);
                break;
            }
        }
    }

    /// <summary>The siege battery's stations: the same battery, each playing at another of its places.</summary>
    private static void BuildSiege(FxStation s, Node3D root, string kind)
    {
        SiegeBattery(root, s);
        Node3D muzzle = s.Sockets["muzzle0"], deck = s.Sockets["deck0"], breech = s.Sockets["breech0"], house = s.Sockets["house"];
        Vector3 m = muzzle.GlobalPosition;
        Vector3 along = muzzle.GlobalBasis.X.Normalized();
        s.FullTurn = false;
        switch (kind)
        {
            case "siege-deck":
            {
                // The deck under the muzzle; the blast's direction along it.
                Vector3 at = deck.GlobalPosition, flat = deck.GlobalBasis.X;
                (s.Spot, s.Normal, s.Direction) = (at, Vector3.Up, flat);
                s.Look = at + Vector3.Up * 1.0f + flat * 2.5f;
                s.Distance = 14f;
                s.Pitch = 13f;
                s.Yaw += Mathf.DegToRad(2f);
                s.Play = (def, p, _, _) => Here(def, at, Vector3.Up, flat, p);
                break;
            }
            case "siege-breech":
                (s.Spot, s.Normal, s.Direction, s.Follow) = (breech.GlobalPosition, breech.GlobalBasis.X, breech.GlobalBasis.X, breech);
                s.Look = breech.GlobalPosition + Vector3.Up * 0.35f + breech.GlobalBasis.X * 0.6f;
                s.Distance = 5.5f;
                s.Pitch = 14f;
                s.Yaw -= Mathf.DegToRad(80f);
                s.Play = (def, p, _, _) => On(def, breech, p);
                break;
            case "ignite":
                (s.Spot, s.Normal, s.Direction, s.Follow) = (m, along, along, muzzle);
                s.Look = m + along * 7f;
                s.Distance = 10f;
                s.Pitch = 5f;
                s.Yaw -= Mathf.DegToRad(18f);
                s.Play = (def, p, _, _) =>
                {
                    LaunchRocket(root, muzzle, FxRocketLaunch.Siege, def.IsLoop ? null : def.Key, p);
                    return default;
                };
                break;
            case "damaged":
                (s.Spot, s.Normal, s.Follow) = (house.GlobalPosition, Vector3.Up, house);
                s.Look = house.GlobalPosition + Vector3.Up * 3.2f;
                s.Distance = 17f;
                s.Pitch = 9f;
                s.Yaw -= Mathf.DegToRad(3f);
                s.Play = (def, p, _, _) => On(def, house, p);
                break;
            default:
                (s.Spot, s.Normal, s.Direction, s.Follow) = (m, along, along, muzzle);
                s.Look = m + along * 2.5f + Vector3.Up * 0.3f;
                s.Distance = 9f;
                s.Pitch = 8f;
                s.Yaw += Mathf.DegToRad(7f);
                s.Play = (def, p, _, _) => On(def, muzzle, p);
                break;
        }
    }

    // ---------------------------------------------------------------- how the stations play

    /// <summary>An effect on a socket (+X its direction), as a battery's view plays it: Muzzle for a one-shot, Start on
    /// it for a loop.</summary>
    internal static FxHandle On(FxDef def, Node3D socket, FxParams p)
    {
        Transform3D t = socket.GlobalTransform.Orthonormalized();
        p.Follow = socket;
        p.Direction = t.Basis.X;
        if (def.IsLoop)
        {
            return FxDirector.Start(def.Key, t.Origin, Vector3.Up, p);
        }
        FxDirector.Play(def.Key, t.Origin, t.Basis.X, p);
        return default;
    }

    /// <summary>An effect at a point (a loop stays there).</summary>
    private static FxHandle Here(FxDef def, Vector3 at, Vector3 normal, Vector3 direction, FxParams p)
    {
        p.Direction = direction;
        if (def.IsLoop)
        {
            return FxDirector.Start(def.Key, at, normal, p);
        }
        FxDirector.Play(def.Key, at, normal, p);
        return default;
    }

    /// <summary>
    /// A rocket fired from <paramref name="muzzle"/> (+X its direction) as <paramref name="launch"/> says: where its
    /// motor lights it plays <paramref name="ignition"/> on its tail (none: no burst) and starts trail.rocket, which it
    /// stops when the motor burns out, as a battery's view will. The rocket flies on for a moment, then goes; it is
    /// frozen while the director is paused (screenshots).
    /// </summary>
    internal static void LaunchRocket(Node3D parent, Node3D muzzle, FxRocketLaunch launch, string? ignition, FxParams p = default)
    {
        Node3D rocket = ModelCatalog.Instantiate("rocket") ?? new Node3D();
        parent.AddChild(rocket);
        Vector3 from = muzzle.GlobalPosition;
        Vector3 dir = muzzle.GlobalBasis.X.Normalized();
        rocket.GlobalPosition = from;
        // The rocket model's nose is its -Z.
        rocket.LookAt(from + dir, Mathf.Abs(dir.Y) < 0.95f ? Vector3.Up : Vector3.Right);
        var tail = new Node3D { Name = "Tail" };
        rocket.AddChild(tail);
        tail.Position = new Vector3(0, 0.05f, 0.34f);
        FxHandle trail = default;
        bool lit = false, burntOut = false;
        double t = 0;
        float lights = launch.IgnitionDistance / launch.MuzzleSpeed;
        SceneTree tree = parent.GetTree();
        Action? step = null;
        step = () =>
        {
            if (!GodotObject.IsInstanceValid(rocket) || t > lights + launch.Burn + 0.6)
            {
                FxDirector.Stop(trail);
                if (GodotObject.IsInstanceValid(rocket))
                {
                    rocket.QueueFree();
                }
                tree.ProcessFrame -= step;
                return;
            }
            if (FxDirector.Instance.Paused)
            {
                return;
            }
            t += parent.GetProcessDeltaTime();
            float burn = (float)Math.Clamp(t - lights, 0, launch.Burn);
            float coast = (float)Math.Max(0, t - lights - launch.Burn);
            float distance = launch.MuzzleSpeed * (float)t + 0.5f * launch.Acceleration * burn * burn + launch.Acceleration * launch.Burn * coast;
            rocket.GlobalPosition = from + dir * distance;
            if (!lit && t >= lights)
            {
                lit = true;
                Vector3 at = tail.GlobalPosition;
                if (ignition != null)
                {
                    FxParams q = p;
                    q.Direction = dir;
                    q.Follow = tail;
                    FxDirector.Play(ignition, at, dir, q);
                }
                trail = FxDirector.Start("trail.rocket", at, Vector3.Up, new FxParams { Follow = tail, Direction = dir });
            }
            if (lit && !burntOut && t >= lights + launch.Burn)
            {
                burntOut = true;
                FxDirector.Stop(trail);
            }
        };
        tree.ProcessFrame += step;
    }

    // ---------------------------------------------------------------- the stand-ins

    /// <summary>
    /// The siege battery at the asset's scale: a deck plate, the turret ring, the gun house and two barrels 1.995 m
    /// apart, elevated 3°, their muzzles 2.3 m over the deck. Sockets: muzzle0 and muzzle1 (+X along the barrel),
    /// breech0 and breech1 at the barrels' rears (+X out of them), deck0 on the deck under muzzle0 (+X the barrel's
    /// direction along the deck, +Y up), house on the gun house's roof and eye, the gunner's eye.
    /// </summary>
    private static void SiegeBattery(Node3D root, FxStation s)
    {
        Part(root, new BoxMesh { Size = new Vector3(18f, DeckTop, 9f) }, new Vector3(2.5f, DeckTop * 0.5f, 0), Vector3.Zero, "deck");
        Part(root, new CylinderMesh { TopRadius = 1.9f, BottomRadius = 2.0f, Height = 0.4f }, new Vector3(-0.8f, DeckTop + 0.2f, 0), Vector3.Zero, "gunmetal");
        Part(root, new BoxMesh { Size = new Vector3(3.2f, 1.6f, 3.4f) }, new Vector3(-0.9f, DeckTop + 1.2f, 0), Vector3.Zero, "paint");
        var house = new Node3D { Name = "HouseRoof", Position = new Vector3(-1.1f, DeckTop + 2.0f, -0.7f) };
        root.AddChild(house);
        s.Sockets["house"] = house;
        // The gunner's eye, as the asset's station camera sits: 13.3 m behind the muzzles, 0.9 m over their line.
        var eye = new Node3D { Name = "GunnerEye", Position = new Vector3(-5.2f, DeckTop + 3.2f, 0f) };
        root.AddChild(eye);
        s.Sockets["eye"] = eye;
        var pitch = new Node3D { Name = "Pitch", Position = new Vector3(0.4f, DeckTop + 1.9f, 0), Rotation = new Vector3(0, 0, Mathf.DegToRad(3f)) };
        root.AddChild(pitch);
        Part(pitch, new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 3.6f }, Vector3.Zero, new Vector3(MathF.PI / 2, 0, 0), "gunmetal");
        Vector3 alongX = new(0, 0, -MathF.PI / 2);
        for (int i = 0; i < 2; i++)
        {
            float z = i == 0 ? 0.9975f : -0.9975f;
            Part(pitch, new CylinderMesh { TopRadius = 0.42f, BottomRadius = 0.42f, Height = 2.0f }, new Vector3(1.2f, 0, z), alongX, "gunmetal");
            Part(pitch, new CylinderMesh { TopRadius = 0.2f, BottomRadius = 0.22f, Height = 5.6f }, new Vector3(4.9f, 0, z), alongX, "gunmetal");
            Part(pitch, new CylinderMesh { TopRadius = 0.255f, BottomRadius = 0.255f, Height = 0.32f }, new Vector3(7.56f, 0, z), alongX, "gunmetal");
            Part(pitch, new CylinderMesh { TopRadius = 0.34f, BottomRadius = 0.34f, Height = 0.8f }, new Vector3(-2.6f, 0, z), alongX, "gunmetal");
            var muzzle = new Node3D { Name = $"Muzzle{i}", Position = new Vector3(7.72f, 0, z) };
            pitch.AddChild(muzzle);
            s.Sockets[$"muzzle{i}"] = muzzle;
            var breech = new Node3D { Name = $"Breech{i}", Position = new Vector3(-3.02f, 0, z), Rotation = new Vector3(0, MathF.PI, 0) };
            pitch.AddChild(breech);
            s.Sockets[$"breech{i}"] = breech;
        }
        // The deck under the first muzzle, +X the barrel's direction flattened onto it.
        Vector3 m = s.Sockets["muzzle0"].GlobalPosition;
        Vector3 along = s.Sockets["muzzle0"].GlobalBasis.X;
        Vector3 flat = new Vector3(along.X, 0, along.Z).Normalized();
        var deck = new Node3D { Name = "Deck0" };
        root.AddChild(deck);
        deck.GlobalTransform = new Transform3D(new Basis(flat, Vector3.Up, flat.Cross(Vector3.Up)), new Vector3(m.X, root.GlobalPosition.Y + DeckTop, m.Z));
        s.Sockets["deck0"] = deck;
    }

    /// <summary>A defense mount at the asset's scale: three tubes 0.275 m apart, elevated 6°, on a small deck. Sockets:
    /// muzzle0 to muzzle2.</summary>
    private static Node3D[] DefenseMount(Node3D root, FxStation s)
    {
        Part(root, new BoxMesh { Size = new Vector3(7f, 0.25f, 4.5f) }, new Vector3(1.0f, 0.125f, 0), Vector3.Zero, "deck");
        Part(root, new CylinderMesh { TopRadius = 0.8f, BottomRadius = 0.85f, Height = 0.35f }, new Vector3(0, 0.43f, 0), Vector3.Zero, "gunmetal");
        Part(root, new BoxMesh { Size = new Vector3(1.5f, 0.85f, 1.2f) }, new Vector3(-0.1f, 1.03f, 0), Vector3.Zero, "paint");
        var pitch = new Node3D { Name = "Pitch", Position = new Vector3(0.5f, 1.05f, 0), Rotation = new Vector3(0, 0, Mathf.DegToRad(6f)) };
        root.AddChild(pitch);
        Vector3 alongX = new(0, 0, -MathF.PI / 2);
        var muzzles = new Node3D[3];
        for (int i = 0; i < 3; i++)
        {
            float z = (i - 1) * 0.2752f;
            Part(pitch, new CylinderMesh { TopRadius = 0.075f, BottomRadius = 0.08f, Height = 2.0f }, new Vector3(1.0f, 0, z), alongX, "gunmetal");
            Part(pitch, new CylinderMesh { TopRadius = 0.092f, BottomRadius = 0.092f, Height = 0.12f }, new Vector3(1.94f, 0, z), alongX, "gunmetal");
            muzzles[i] = new Node3D { Name = $"Muzzle{i}", Position = new Vector3(2.0f, 0, z) };
            pitch.AddChild(muzzles[i]);
            s.Sockets[$"muzzle{i}"] = muzzles[i];
        }
        return muzzles;
    }

    /// <summary>A panel of hull plate facing the station's front, with weld seams; returns a point on its face.</summary>
    private static Vector3 HullPlate(Node3D root)
    {
        Part(root, new BoxMesh { Size = new Vector3(7f, 4.6f, 0.14f) }, new Vector3(0, 2.3f, 0), Vector3.Zero, "paint");
        Part(root, new BoxMesh { Size = new Vector3(7f, 0.05f, 0.02f) }, new Vector3(0, 3.1f, 0.075f), Vector3.Zero, "gunmetal");
        Part(root, new BoxMesh { Size = new Vector3(0.05f, 4.6f, 0.02f) }, new Vector3(1.6f, 2.3f, 0.075f), Vector3.Zero, "gunmetal");
        var body = new StaticBody3D();
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(7f, 4.6f, 0.14f) }, Position = new Vector3(0, 2.3f, 0) });
        root.AddChild(body);
        return root.GlobalPosition + root.Basis * new Vector3(-0.6f, 2.0f, 0.07f);
    }

    /// <summary>A lift engine's nozzle 6 m over open ground, pointing down; returns the ground under it.</summary>
    private static Vector3 LiftNozzle(Node3D root)
    {
        Part(root, new CylinderMesh { TopRadius = 1.0f, BottomRadius = 1.25f, Height = 1.7f }, new Vector3(0, 6.85f, 0), Vector3.Zero, "gunmetal");
        Part(root, new BoxMesh { Size = new Vector3(2.9f, 2.2f, 3.3f) }, new Vector3(0, 8.8f, 0), Vector3.Zero, "paint");
        return root.GlobalPosition + Vector3.Up * 0.02f;
    }

    private static MeshInstance3D Part(Node3D parent, Mesh mesh, Vector3 position, Vector3 rotation, string finish)
    {
        var part = new MeshInstance3D { Mesh = mesh, Position = position, Rotation = rotation, MaterialOverride = ShipMaterial(finish) };
        parent.AddChild(part);
        return part;
    }

    private static Material ShipMaterial(string name)
    {
        if (ShipMaterials.TryGetValue(name, out Material? m))
        {
            return m;
        }
        (string texture, float tile, Color albedo, float metallic, float roughness) = Finishes[name];
        string path = "res://assets/textures/monuments/" + texture;
        var material = new StandardMaterial3D { AlbedoColor = albedo, Metallic = metallic, Roughness = roughness };
        if (ResourceLoader.Exists(path + "_albedo.png"))
        {
            material.AlbedoTexture = GD.Load<Texture2D>(path + "_albedo.png");
            material.NormalEnabled = ResourceLoader.Exists(path + "_normal.png");
            material.NormalTexture = material.NormalEnabled ? GD.Load<Texture2D>(path + "_normal.png") : null;
            material.Uv1Triplanar = true;
            material.Uv1WorldTriplanar = true;
            material.Uv1Scale = Vector3.One / tile;
            material.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
        }
        ShipMaterials[name] = material;
        return material;
    }
}
