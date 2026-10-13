using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Hollow.Client;
using Hollow.Client.Loading;
using Hollow.Surface;
using HollowUniverse.Surface;
using HollowUniverse.UI;

namespace HollowUniverse.Ground;

// The ground game's start (GROUND.md §4.3.1): one loading pipeline per game, in which the parts that make the game
// register their own stages (the hosted server, the client, the view), the loading screen over everything while it
// runs, and the timeline of a start (--load-trace).
public partial class GroundMode
{
    private static LoadingPlan? _plan;

    private LoadingPipeline? _loading;
    private LoadingScreen? _screen;
    private LoadTrace? _trace;
    private ulong _frameAt;

    /// <summary>The loading screen's data, read once (a missing or broken file: every stage named by its key, and a warning).</summary>
    private static LoadingPlan Plan => _plan ??= ReadPlan();

    /// <summary>The game's client, once a game started.</summary>
    internal GameClient? Client => _client;

    /// <summary>The game's view, once a game started.</summary>
    internal GroundView? View => _view;

    /// <summary>The hosted server's process, if this game hosts one.</summary>
    internal ChildServer? Child => _child;

    /// <summary>The current game's loading pipeline.</summary>
    internal LoadingPipeline? Loading => _loading;

    /// <summary>An admin command to the game hosted here (null when joined elsewhere).</summary>
    internal Func<string, bool>? AdminCommand => Command;

    private static LoadingPlan ReadPlan()
    {
        const string path = "res://data/ground/" + LoadingPlan.FileName;
        try
        {
            return LoadingPlan.Parse(Godot.FileAccess.GetFileAsString(path));
        }
        catch (Exception e) when (e is InvalidDataException or JsonException)
        {
            GD.PushWarning($"{path}: {e.Message}");
            return LoadingPlan.Empty;
        }
    }

    // A game's load begins: the pipeline its parts register their stages in.
    private LoadingPipeline BeginLoading()
    {
        EndLoading();
        Prefetch();
        _loading = new LoadingPipeline(Plan);
        _frameAt = 0;
        return _loading;
    }

    // The screen over everything, once every part registered: the last child, so it sees input first and keeps it.
    private void ShowLoading(string destination)
    {
        if (_loading == null)
        {
            return;
        }
        _screen = new LoadingScreen { Name = "Loading", Pipeline = _loading, Destination = destination };
        AddChild(_screen);
        if (_view != null)
        {
            _view.Covered = () => _screen?.Shown == true;
        }
    }

    private void EndLoading()
    {
        _screen?.QueueFree();
        _screen = null;
        _loading = null;
    }

    // Once a frame, before the view: the pipeline measures its stages. The frame's time is the wall clock's (Godot's
    // delta is smoothed and capped, and the steady frames must see a stall as it was).
    private void UpdateLoading()
    {
        ulong now = Time.GetTicksUsec();
        double seconds = _frameAt == 0 ? 0 : (now - _frameAt) / 1e6;
        _frameAt = now;
        _loading?.Update(seconds);
    }

    // The first world's surface and air, built on worker threads while the server starts: a game lands there, and the
    // client would otherwise build both on the main thread when it is welcomed and opens the world.
    private static void Prefetch() => Task.Run(() =>
    {
        if (PlanetSurfaces.Find(FirstWorld.System.Value, FirstWorld.BodyPath) is { } world)
        {
            AtmosphereModel.Prepare(world.Body);
        }
    });

    // --load-trace [seconds]: times this start and prints it (LoadTrace).
    private void StartLoadTrace()
    {
        if (!Options.ContainsKey("load-trace"))
        {
            return;
        }
        _trace = new LoadTrace
        {
            Name = "LoadTrace", Mode = this, Seconds = Number("load-trace", 45),
            ShotPath = Options.TryGetValue("load-shot", out string? shot) && shot != "true" ? shot : null,
            ArrivalCheck = Options.ContainsKey("arrival-check"),
        };
        AddChild(_trace);
    }
}
