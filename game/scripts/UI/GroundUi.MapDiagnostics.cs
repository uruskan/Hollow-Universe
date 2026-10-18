using System;
using System.Diagnostics;

namespace HollowUniverse.UI;

public partial class GroundUi
{
    public readonly record struct MapDrawMeasurement(int Frames, double MeanMilliseconds, double MaximumMilliseconds,
        double AllocatedBytesPerFrame);

    private bool _measureMapDraw;
    private long _mapDrawStart, _mapDrawBytes, _mapDrawTotal, _mapDrawMaximum, _mapDrawAllocated;
    private int _mapDrawFrames;

    /// <summary>Opt-in timing of the real map overlay, including grid, markers and labels.</summary>
    public void MeasureMapDrawing(bool enabled)
    {
        _measureMapDraw = enabled;
        _mapDrawStart = _mapDrawBytes = _mapDrawTotal = _mapDrawMaximum = _mapDrawAllocated = 0;
        _mapDrawFrames = 0;
    }

    public MapDrawMeasurement MapDrawingMeasurement => new(_mapDrawFrames,
        _mapDrawFrames == 0 ? 0 : _mapDrawTotal * 1000.0 / Stopwatch.Frequency / _mapDrawFrames,
        _mapDrawMaximum * 1000.0 / Stopwatch.Frequency,
        _mapDrawFrames == 0 ? 0 : (double)_mapDrawAllocated / _mapDrawFrames);

    private void BeginMapDrawMeasurement()
    {
        if (!_measureMapDraw) return;
        _mapDrawBytes = GC.GetAllocatedBytesForCurrentThread();
        _mapDrawStart = Stopwatch.GetTimestamp();
    }

    private void EndMapDrawMeasurement()
    {
        if (!_measureMapDraw) return;
        long elapsed = Stopwatch.GetTimestamp() - _mapDrawStart;
        _mapDrawTotal += elapsed;
        _mapDrawMaximum = Math.Max(_mapDrawMaximum, elapsed);
        _mapDrawAllocated += GC.GetAllocatedBytesForCurrentThread() - _mapDrawBytes;
        _mapDrawFrames++;
    }
}
