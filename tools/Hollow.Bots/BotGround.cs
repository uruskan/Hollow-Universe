using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hollow.Client;
using Hollow.Simulation;
using Hollow.Simulation.Movement;
using Hollow.Surface;

namespace Hollow.Bots;

/// <summary>
/// The bots' ground on a whole world (docs/WHOLE_WORLD.md §13): the landing site, each bot's home (where it arrived),
/// headings toward points measured from local north, and offsets along the ground. The zone's frame is the world's own,
/// so up at a point is its direction from the centre.
/// </summary>
public sealed class BotGround : IDisposable
{
    /// <summary>A bot that first arrived this close to the landing site arrived with the crowd there (<c>--spawn origin</c>).</summary>
    public const float AtLandingRadius = 30f;

    /// <summary>A move this long between two frames is an arrival (a respawn, a teleport): the bot's home moves there.</summary>
    private const float ArrivalJump = 100f;

    private sealed class HomeState(PlanetEnvironmentDescriptor descriptor, ZoneSpec zone)
    {
        public readonly PlanetEnvironmentDescriptor Descriptor = descriptor;
        public readonly ZoneSpec Zone = zone;
        public bool Arrived;
        public Vector3 First, Home, Last, Landing;
    }

    private readonly Dictionary<GameClient, HomeState> _bots = new();
    private readonly Dictionary<(PlanetEnvironmentDescriptor World, Vector3 Landing, float Spacing), List<Vector3>> _plots = new();
    private bool _disposed;

    internal void Bind(GameClient client, PlanetEnvironmentDescriptor descriptor, ZoneSpec zone)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_bots.TryGetValue(client, out HomeState? state) || !state.Descriptor.Equals(descriptor) || state.Zone != zone)
            _bots[client] = new HomeState(descriptor, zone);
    }

    internal void Forget(GameClient client) => _bots.Remove(client);

    /// <summary>The selected client's own landing site; unavailable until its first ground observation.</summary>
    public bool TryLanding(GameClient client, out Vector3 landing)
    {
        bool found = _bots.TryGetValue(client, out HomeState? state) && state.Arrived;
        landing = found ? state!.Landing : default;
        return found;
    }

    public Vector3 Landing(GameClient client) => TryLanding(client, out Vector3 landing)
        ? landing : throw new InvalidOperationException("This bot has not observed its landing site.");

    /// <summary>
    /// Every frame, for a bot with a character: learns the landing site once, and the bot's home: where it arrived, and
    /// again wherever it arrives after a respawn or a teleport.
    /// </summary>
    public void Follow(GameClient client)
    {
        if (client.Zone is { } spec && client.Placement().Ground is { } ground)
        {
            Vector3 landing = TryLanding(client, out Vector3 known) ? known : ground.On(spec.LandingDirection);
            Observe(client, client.Character.Position, landing);
        }
    }

    internal void Observe(GameClient client, Vector3 at, Vector3 landing)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_bots.TryGetValue(client, out HomeState? bot))
            throw new InvalidOperationException("Bind the bot to its session world before tracking arrivals.");
        bot.Landing = landing;
        if (!bot.Arrived)
        {
            bot.First = bot.Home = bot.Last = at;
            bot.Arrived = true;
            return;
        }
        if (Vector3.DistanceSquared(at, bot.Last) > ArrivalJump * ArrivalJump) bot.Home = at;
        bot.Last = at;
    }

    /// <summary>Where the bot lives: where it last arrived (the landing site's crowd, or a beach anywhere on the world).</summary>
    public Vector3 Home(GameClient client) => _bots.TryGetValue(client, out var bot) && bot.Arrived ? bot.Home : client.Character.Position;

    /// <summary>Where the bot first arrived.</summary>
    public Vector3 FirstArrival(GameClient client) => _bots.TryGetValue(client, out var bot) && bot.Arrived ? bot.First : client.Character.Position;

    /// <summary>Whether the bot first arrived at the landing site with the crowd, rather than on a beach of its own.</summary>
    public bool ArrivedAtLanding(GameClient client) =>
        TryLanding(client, out Vector3 landing) && Vector3.Distance(FirstArrival(client), landing) < AtLandingRadius;

    internal Vector3 CrowdPlot(GameClient client, int index, float spacing)
    {
        HomeState state = _bots[client];
        Vector3 landing = Landing(client);
        var key = (state.Descriptor, landing, spacing);
        if (!_plots.TryGetValue(key, out List<Vector3>? plots))
        {
            var grid = new List<(float East, float South)>();
            for (int z = -8; z < 8; z++)
                for (int x = -8; x < 8; x++) grid.Add(((x + 0.5f) * spacing, (z + 0.5f) * spacing));
            plots = grid.OrderBy(p => p.East * p.East + p.South * p.South).ThenBy(p => p.South).ThenBy(p => p.East)
                .Select(p => Offset(client, landing, p.East, p.South))
                .Where(p => client.Placement().Ground is not { } ground || !ground.InWater(p, 1f)).ToList();
            if (plots.Count == 0) throw new InvalidOperationException("No dry crowd plot surrounds this bot's landing site.");
            _plots.Add(key, plots);
        }
        return plots[index % plots.Count];
    }

    internal void RetireExcept(IReadOnlySet<PlanetEnvironmentDescriptor> retained)
    {
        foreach (var key in _plots.Keys.Where(key => !retained.Contains(key.World)).ToArray()) _plots.Remove(key);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _bots.Clear();
        _plots.Clear();
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
