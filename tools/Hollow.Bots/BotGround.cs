using System.Numerics;
using Hollow.Client;
using Hollow.Simulation.Movement;

namespace Hollow.Bots;

/// <summary>
/// The bots' ground on a whole world (docs/WHOLE_WORLD.md §13): their home (the landing site, where the load test's
/// bots arrive and stay near), headings toward points measured from local north, and offsets along the ground. The
/// zone's frame is the world's own, so up at a point is its direction from the centre.
/// </summary>
internal static class BotGround
{
    /// <summary>The landing site's ground: home for every bot of the swarm (set by the first bot that knows it).</summary>
    public static Vector3 Home { get; private set; }

    /// <summary>Learns home from a client that has joined (once).</summary>
    public static void Ensure(GameClient client)
    {
        if (Home == default && client.Zone is { } spec && client.Placement().Ground is { } ground)
        {
            Home = ground.On(spec.LandingDirection);
        }
    }

    public static Vector3 UpAt(Vector3 p) => p.LengthSquared() > 0 ? Vector3.Normalize(p) : Vector3.UnitY;

    /// <summary>A vector laid onto the ground at a point.</summary>
    public static Vector3 Flat(Vector3 v, Vector3 at)
    {
        Vector3 up = UpAt(at);
        return v - up * Vector3.Dot(v, up);
    }

    /// <summary>How far one point is from another along the ground at the second.</summary>
    public static float Distance(Vector3 a, Vector3 b) => Flat(a - b, b).Length();

    /// <summary>The heading (from local north) from one point toward another.</summary>
    public static float YawTo(Vector3 from, Vector3 to) => Facing.YawOf(to - from, UpAt(from));

    /// <summary>The heading and pitch of a look from an eye at a point.</summary>
    public static (float Yaw, float Pitch) LookAt(Vector3 eye, Vector3 at)
    {
        Vector3 d = at - eye;
        float length = d.Length();
        return length < 1e-5f ? (0, 0) : (Facing.YawOf(d, UpAt(eye)), Facing.PitchOf(d / length, UpAt(eye)));
    }

    /// <summary>The ground east and south of a point, along the ground (the client's own surface); the point itself without one.</summary>
    public static Vector3 Offset(GameClient client, Vector3 from, float east, float south) =>
        client.Placement().Ground is { } ground ? ground.Offset(from, east, south) : from;
}
