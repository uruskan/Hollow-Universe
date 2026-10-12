using System;

namespace Hollow.Client;

/// <summary>
/// What the network graph shows (GROUND.md §4.4): round trip, snapshot loss and rate, bandwidth both ways (with UDP
/// and IP headers), prediction error and how early inputs reach the server. Values are over the last second; the
/// histories hold one sample per <see cref="SampleSeconds"/> for drawing.
/// </summary>
public sealed class ClientStats
{
    public const int HistoryLength = 120;
    public const double SampleSeconds = 0.25;

    /// <summary>UDP and IPv4 headers, which the transport's byte counts leave out.</summary>
    public const int HeaderBytes = 28;

    private double _secondTimer;
    private double _sampleTimer;
    private long _lastBytesIn, _lastBytesOut, _lastPacketsIn, _lastPacketsOut;
    private int _snapshotsThisSecond;
    private uint _firstSequence, _lastSequence;
    private int _receivedInWindow;
    private float _errorSum;
    private float _errorMax;
    private int _errorCount;
    private int _next;

    // ---------------------------------------------------------------- last second

    public int RoundTripMs { get; internal set; }

    public double SnapshotsPerSecond { get; private set; }

    /// <summary>Snapshots that never arrived, of those sent, in percent.</summary>
    public double SnapshotLossPercent { get; private set; }

    public double KilobitsIn { get; private set; }

    public double KilobitsOut { get; private set; }

    /// <summary>How far the prediction of a tick was from the server's state of it, in metres.</summary>
    public float PredictionErrorAverage { get; private set; }

    public float PredictionErrorMax { get; private set; }

    public float LastPredictionError { get; private set; }

    /// <summary>The least margin, in ticks, by which inputs reached the server lately (from snapshots).</summary>
    public int InputMargin { get; internal set; }

    /// <summary>How fast the prediction clock runs now, against real time.</summary>
    public double ClockRate { get; internal set; } = 1;

    // ---------------------------------------------------------------- totals

    public long Snapshots { get; private set; }

    public long Corrections { get; private set; }

    public long Reconciliations { get; private set; }

    public double PredictionErrorTotal { get; private set; }

    public float PredictionErrorWorst { get; private set; }

    public long ClockJumps { get; internal set; }

    // ---------------------------------------------------------------- histories for the graph

    public float[] RoundTripHistory { get; } = new float[HistoryLength];

    public float[] KilobitsInHistory { get; } = new float[HistoryLength];

    public float[] PredictionErrorHistory { get; } = new float[HistoryLength];

    /// <summary>The index the next sample goes to (the oldest sample).</summary>
    public int HistoryStart => _next;

    internal void Snapshot(uint sequence)
    {
        Snapshots++;
        _snapshotsThisSecond++;
        if (_receivedInWindow == 0)
        {
            _firstSequence = sequence;
        }
        _lastSequence = sequence;
        _receivedInWindow++;
    }

    internal void Prediction(float? error)
    {
        Reconciliations++;
        if (error is not { } e)
        {
            return;
        }
        LastPredictionError = e;
        _errorSum += e;
        _errorMax = Math.Max(_errorMax, e);
        _errorCount++;
        PredictionErrorTotal += e;
        PredictionErrorWorst = Math.Max(PredictionErrorWorst, e);
        if (e > 0.001f)
        {
            Corrections++;
        }
    }

    /// <summary>Starts the totals afresh (to measure a steady state, after joining has settled).</summary>
    public void ResetTotals()
    {
        Corrections = 0;
        Reconciliations = 0;
        PredictionErrorTotal = 0;
        PredictionErrorWorst = 0;
        ClockJumps = 0;
    }

    /// <summary>Average prediction error since the start or <see cref="ResetTotals"/>.</summary>
    public double PredictionErrorMean => Reconciliations == 0 ? 0 : PredictionErrorTotal / Reconciliations;

    internal void Update(double delta, long bytesIn, long bytesOut, long packetsIn, long packetsOut)
    {
        _secondTimer += delta;
        _sampleTimer += delta;
        if (_secondTimer >= 1)
        {
            double seconds = _secondTimer;
            _secondTimer = 0;
            KilobitsIn = ((bytesIn - _lastBytesIn) + (packetsIn - _lastPacketsIn) * HeaderBytes) * 8 / 1000.0 / seconds;
            KilobitsOut = ((bytesOut - _lastBytesOut) + (packetsOut - _lastPacketsOut) * HeaderBytes) * 8 / 1000.0 / seconds;
            (_lastBytesIn, _lastBytesOut, _lastPacketsIn, _lastPacketsOut) = (bytesIn, bytesOut, packetsIn, packetsOut);
            SnapshotsPerSecond = _snapshotsThisSecond / seconds;
            _snapshotsThisSecond = 0;
            long sent = _receivedInWindow == 0 ? 0 : _lastSequence - _firstSequence + 1;
            SnapshotLossPercent = sent <= 0 ? 0 : Math.Max(0, 100.0 * (sent - _receivedInWindow) / sent);
            _receivedInWindow = 0;
            PredictionErrorAverage = _errorCount == 0 ? 0 : _errorSum / _errorCount;
            PredictionErrorMax = _errorMax;
            _errorSum = 0;
            _errorMax = 0;
            _errorCount = 0;
        }
        if (_sampleTimer >= SampleSeconds)
        {
            _sampleTimer = 0;
            RoundTripHistory[_next] = RoundTripMs;
            KilobitsInHistory[_next] = (float)KilobitsIn;
            PredictionErrorHistory[_next] = LastPredictionError;
            _next = (_next + 1) % HistoryLength;
        }
    }
}
