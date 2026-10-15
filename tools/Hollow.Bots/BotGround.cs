using System.Collections.Generic;
using System.Numerics;
using Hollow.Client;
using Hollow.Simulation.Movement;

namespace Hollow.Bots;

/// <summary>
/// The bots' ground on a whole world (docs/WHOLE_WORLD.md §13): the landing site, each bot's home (where it arrived),
/// headings toward points measured from local north, and offsets along the ground. The zone's frame is the world's own,
/// so up at a point is its direction from the centre.
/// </summary>
internal static class BotGround
{
    /// <summary>A bot that first arrived this close to the landing site arrived with the crowd there (<c>--spawn origin</c>).</summary>
    public const float AtLandingRadius = 30f;

    /// <summary>A move this long between two frames is an arrival (a respawn, a teleport): the bot's home moves there.</summary>
    private const float ArrivalJump = 100f;

    private static readonly Dictionary<GameClient, (Vector3 First, Vector3 Home, Vector3 Last)> Bots = new();

    /// <summary>The landing site's ground (set by the first bot that knows it): the load test's crowd and the raid ring are round it.</summary>
    public static Vector3 Landing { get; private set; }

    /// <summary>
    /// Every frame, for a bot with a character: learns the landing site once, and the bot's home: where it arrived, and
    /// again wherever it arrives after a respawn or a teleport.
    /// </summary>
    public static void Follow(GameClient client)
    {
        if (Landing == default && client.Zone is { } spec && client.Placement().Ground is { } ground)
        {
            Landing = ground.On(spec.LandingDirection);
        }
        Vector3 at = client.Character.Position;
        if (!Bots.TryGetValue(client, out (Vector3 First, Vector3 Home, Vector3 Last) bot))
        {
            Bots[client] = (at, at, at);
            return;
        }
        Bots[client] = (bot.First, Vector3.DistanceSquared(at, bot.Last) > ArrivalJump * ArrivalJump ? at : bot.Home, at);
    }

    /// <summary>Where the bot lives: where it last arrived (the landing site's crowd, or a beach anywhere on the world).</summary>
    public static Vector3 Home(GameClient client) => Bots.TryGetValue(client, out var bot) ? bot.Home : client.Character.Position;

    /// <summary>Where the bot first arrived.</summary>
    public static Vector3 FirstArrival(GameClient client) => Bots.TryGetValue(client, out var bot) ? bot.First : client.Character.Position;

    /// <summary>Whether the bot first arrived at the landing site with the crowd, rather than on a beach of its own.</summary>
    public static bool ArrivedAtLanding(GameClient client) =>
        Landing != default && Vector3.Distance(FirstArrival(client), Landing) < AtLandingRadius;

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
