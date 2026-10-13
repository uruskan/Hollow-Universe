using Godot;
using Hollow.Client;
using Hollow.Simulation.Building;
using HollowUniverse.Gibs;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

/// <summary>
/// Things breaking apart in the ground game (GROUND.md §9.4): whatever the damage pipeline's Destroyed event names
/// (<see cref="GameClient.Destroyed"/>, §6.4.2) breaks into its gibs where it stood, by its key and grade, whatever it was
/// (a barrel, a box, a door, a wall), with the sound the data names; the pieces land on the client's own collision (ground,
/// building, what stands on them). The one path from the fact to the pieces.
/// </summary>
public partial class GibsView : Node
{
    public GameClient Client { get; set; } = null!;

    public override void _Ready()
    {
        GibDirector.Install(this).Floor = Floor;
        Client.Destroyed += OnDestroyed;
    }

    public override void _ExitTree() => Client.Destroyed -= OnDestroyed;

    private void OnDestroyed(DestroyedEvent e)
    {
        Num.Vector3 up = Client.Frame.UpAt(e.Position);
        float gravity = Client.Zone is { } zone ? (float)(zone.SurfaceGravity * 9.80665) : 0f;
        GibBreak broke = GibDirector.Break(e.Key, e.Grade, e.Position, e.Rotation, e.Direction, e.Cause, up, gravity, e.Target.Id);
        if (broke.Shown)
        {
            Sounds.At(broke.CauseSound ?? Sounds.Item(e.Key, "break") ?? broke.MaterialSound, broke.Centre);
        }
    }

    // The client's own collision under a point.
    private bool Floor(Vector3 from, Vector3 down, float length, out Vector3 point, out Vector3 normal)
    {
        point = default;
        normal = Vector3.Up;
        if (Client.Building?.Physics is not { } physics ||
            !physics.Ray(new Num.Vector3(from.X, from.Y, from.Z), new Num.Vector3(down.X, down.Y, down.Z), length, out RayHit hit))
        {
            return false;
        }
        point = new Vector3(hit.Point.X, hit.Point.Y, hit.Point.Z);
        normal = new Vector3(hit.Normal.X, hit.Normal.Y, hit.Normal.Z);
        return true;
    }
}
