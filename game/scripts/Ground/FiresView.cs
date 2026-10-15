using System;
using System.Collections.Generic;
using Godot;
using Hollow.Client;
using Hollow.Simulation.Entities;
using HollowUniverse.Audio;
using HollowUniverse.Fx;

namespace HollowUniverse.Ground;

/// <summary>
/// Fires in the ground game (IW4, IRON_WAKE.md §7, GROUND.md §9.3): each pool of burning fuel the server tells of is the
/// effects library's fire loop (the item's own, fx.json items' <c>fire</c>: <c>fire.patch</c> for the incendiary rocket)
/// on a node placed every frame where the fire burns (riding a ship's deck as the ship is drawn), scaled to its radius,
/// for the time it has left, with its flickering light; its fuel catching and its loop heard (sounds.json items'
/// <c>fireStart</c> and <c>fire</c>); when it goes out, the burnt patch it leaves (<c>fireOut</c>: <c>fire.patch.out</c>).
/// </summary>
public partial class FiresView : Node3D
{
    /// <summary>The radius the fire loops are made for (fx.json's fire.patch: a 3.5 m pool, IW-F): their scale is a fire's radius over it.</summary>
    public const float PatchRadius = 3.5f;

    /// <summary>How long a fire's loop fades as it goes out (fx.json's fire.patch fadeOut, 2.5 s): its node stays that long.</summary>
    private const double FadeSeconds = 3;

    private sealed class Shown
    {
        public Node3D Node = null!;
        public FxHandle Flames;
        public SoundHandle Sound;
    }

    private readonly Dictionary<EntityId, Shown> _shown = new();
    private readonly List<(Node3D Node, ClientFire Fire, double Until)> _fading = new();
    private double _now;

    public GameClient Client { get; set; } = null!;

    public override void _Ready()
    {
        Client.Fires().Started += OnStarted;
        Client.Fires().Ended += OnEnded;
    }

    public override void _ExitTree()
    {
        Client.Fires().Started -= OnStarted;
        Client.Fires().Ended -= OnEnded;
    }

    /// <summary>Every frame, after the ships have been placed as drawn: each fire where it burns now.</summary>
    public void Refresh(double delta)
    {
        _now += delta;
        foreach ((EntityId id, Shown shown) in _shown)
        {
            if (Client.Fires().FireOf(id) is { } fire)
            {
                Place(shown.Node, fire);
            }
        }
        for (int i = _fading.Count - 1; i >= 0; i--)
        {
            (Node3D node, ClientFire fire, double until) = _fading[i];
            if (_now >= until)
            {
                node.QueueFree();
                _fading.RemoveAt(i);
            }
            else if (Client.Targets().TryPose(fire.Link, out Hollow.Simulation.Targets.TargetPose pose))
            {
                // Fading where what held it is now (a ship's deck flies on).
                Place(node, pose.Position, System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitY, pose.Rotation));
            }
        }
    }

    private static void Place(Node3D node, ClientFire fire) => Place(node, fire.Position, fire.Normal);

    // Its middle on its surface, +Y the surface's normal: the loop's frame.
    private static void Place(Node3D node, System.Numerics.Vector3 position, System.Numerics.Vector3 normal)
    {
        Vector3 up = GroundView.ToGodot(normal).Normalized();
        Vector3 side = Mathf.Abs(up.Dot(Vector3.Forward)) > 0.9f ? Vector3.Right : Vector3.Forward;
        Vector3 x = side.Cross(up).Normalized();
        node.GlobalTransform = new Transform3D(new Basis(x, up, x.Cross(up)).Orthonormalized(), GroundView.ToGodot(position));
    }

    private void OnStarted(ClientFire fire, bool moved)
    {
        if (_shown.TryGetValue(fire.Id, out Shown? shown))
        {
            // It fell onto something else: the loop follows its node to its new place.
            Place(shown.Node, fire);
            return;
        }
        var node = new Node3D { Name = "Fire " + fire.Id };
        AddChild(node);
        Place(node, fire);
        string item = fire.Item.Key;
        double left = fire.SecondsLeft(Client.ServerTickNow);
        Vector3 at = node.GlobalPosition, normal = node.GlobalBasis.Y;
        shown = new Shown
        {
            Node = node,
            Flames = FxDirector.Start(FxCatalog.ItemEffect(item, "fire") ?? "fire.patch", at, normal,
                new FxParams { Follow = node, Duration = (float)Math.Max(0.5, left), Scale = fire.Radius / PatchRadius }),
            Sound = Sounds.Loop(Sounds.Item(item, "fire"), node),
        };
        _shown[fire.Id] = shown;
        // Lit just now (not one that came into range burning): the fuel catching.
        if (!moved && Client.ServerTickNow - fire.StartTick < GameClient.TickRate * 0.6)
        {
            Sounds.At(Sounds.Item(item, "fireStart"), at);
        }
    }

    private void OnEnded(ClientFire fire, bool burntOut)
    {
        if (!_shown.Remove(fire.Id, out Shown? shown))
        {
            return;
        }
        FxDirector.Stop(shown.Flames);
        Sounds.Stop(shown.Sound, 2f);
        if (burntOut)
        {
            // The burnt patch it leaves: the last smoke and embers, and its scorch.
            Place(shown.Node, fire);
            FxDirector.Play(FxCatalog.ItemEffect(fire.Item.Key, "fireOut") ?? "fire.patch.out", shown.Node.GlobalPosition, shown.Node.GlobalBasis.Y,
                new FxParams { Scale = fire.Radius / PatchRadius });
        }
        // The node stays while the loop fades on it (a fire on a ship's deck fades where the deck is).
        _fading.Add((shown.Node, fire, _now + FadeSeconds));
    }
}
