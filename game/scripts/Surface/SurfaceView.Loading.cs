using System;
using System.Collections.Generic;
using Hollow.Client.Loading;

namespace HollowUniverse.Surface;

// The surface view's part of the loading pipeline (GROUND.md §4.3.1): what IsReady waits for, as two stages with their
// progress. Both run again whenever the player arrives somewhere new.
public partial class SurfaceView
{
    public const string LandStage = "land";
    public const string ForestStage = "forest";

    /// <summary>
    /// The view is behind the loading screen (the game sets it while a load runs): what streams goes in place as fast as
    /// it comes, rather than a few pieces a frame to keep play smooth.
    /// </summary>
    public bool Hurry
    {
        get => _terrain.Hurry;
        set => _terrain.Hurry = value;
    }

    /// <summary>
    /// The view's loading stages: <c>land</c>, the terrain around the camera down to its finest tiles (progress: the finest
    /// level drawn), done once nothing changed for the plan's <c>idleFrames</c>; <c>forest</c>, the plants (the library
    /// painted, the impostors baked, the scatter near and far laid out) and the grass, done the same way.
    /// </summary>
    public IReadOnlyList<LoadStage> LoadStages(LoadingPlan plan)
    {
        int landFrames = Math.Max(1, (int)plan.Stage(LandStage).Number("idleFrames", 10));
        int forestFrames = Math.Max(1, (int)plan.Stage(ForestStage).Number("idleFrames", 10));
        int landIdle = 0, forestIdle = 0, scatterPeak = 0;
        bool LandIdle() => _frame != null && _terrain.IsIdle;
        bool ForestIdle() => _frame != null && (_vegetation?.IsIdle ?? false) && (_grass?.IsIdle ?? false);
        return new[]
        {
            new LoadStage(LandStage, () => landIdle >= landFrames, LandProgress, scope: LoadScope.Arrival,
                begin: () => landIdle = 0, frame: _ => landIdle = LandIdle() ? landIdle + 1 : 0),
            new LoadStage(ForestStage, () => forestIdle >= forestFrames, () => ForestProgress(ref scatterPeak), scope: LoadScope.Arrival,
                begin: () => forestIdle = scatterPeak = 0, frame: _ => forestIdle = ForestIdle() ? forestIdle + 1 : 0),
        };
    }

    // Coarse to fine: the finest level drawn of the levels there are, short of all while tiles are still coming.
    private double LandProgress() =>
        _frame == null ? 0 : (double)_terrain.FinestLevel / Math.Max(1, _terrain.MaxLevel) * (_terrain.Pending > 0 ? 0.9 : 1);

    // The library painted, the impostors baked, the scatter laid out (against the most that waited), the grass.
    private double ForestProgress(ref int scatterPeak)
    {
        if (_frame == null || _vegetation == null)
        {
            return 0;
        }
        int left = _vegetation.ScatterLeft;
        scatterPeak = Math.Max(scatterPeak, left);
        double scatter = scatterPeak == 0 ? (_vegetation.LibraryReady ? 1 : 0) : 1 - (double)left / scatterPeak;
        return (_vegetation.LibraryReady ? 0.2 : 0) + (_vegetation.ImpostorsReady ? 0.3 : 0) + 0.3 * scatter + (_grass?.IsIdle == true ? 0.2 : 0);
    }
}
