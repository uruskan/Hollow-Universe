using System;
using System.Collections.Generic;

namespace Hollow.Client.Audio;

/// <summary>A sound's loudness (ITU-R BS.1770), as the sound pipeline measures it with ffmpeg's ebur128.</summary>
/// <param name="Momentary">The highest momentary loudness (400 ms window, every 100 ms), LUFS.</param>
/// <param name="Integrated">The gated integrated loudness, LUFS (-70 when everything is gated out).</param>
/// <param name="TruePeak">The highest sample of the 4× oversampled signal, dBTP.</param>
/// <param name="SamplePeak">The highest sample, dBFS.</param>
/// <param name="Seconds">The length of the sound.</param>
public readonly record struct LoudnessResult(double Momentary, double Integrated, double TruePeak, double SamplePeak, double Seconds)
{
    /// <summary>The loudness of a one-shot over its own length (<see cref="Loudness.EventLoudness"/>).</summary>
    public double Event => Loudness.EventLoudness(Momentary, Seconds);
}

/// <summary>
/// Loudness metering after ITU-R BS.1770-4 (K-weighting, 400 ms blocks, gating) and a 4× oversampled true peak. The
/// pipeline (art/audio) measures with ffmpeg; this is the same measurement for the audition viewer's readout and the
/// tests, within a few tenths of a dB.
/// </summary>
public static class Loudness
{
    /// <summary>Silence added after the sound, as the pipeline does, so a short sound fills a whole window.</summary>
    public const double PadSeconds = 0.5;

    /// <summary>
    /// The loudness of a one-shot over its own length: the highest momentary loudness, corrected for a sound shorter
    /// than the 400 ms window (down to 100 ms, about how long the ear integrates). The pipeline's "event" metric.
    /// </summary>
    public static double EventLoudness(double momentary, double seconds)
    {
        if (momentary <= -100)
        {
            return momentary;
        }
        double s = Math.Min(0.4, Math.Max(0.1, seconds));
        return Math.Round(momentary + 10 * Math.Log10(0.4 / s), 1);
    }

    /// <summary>Measures one sound: <paramref name="channels"/> are the channels' samples (−1…1) at <paramref name="rate"/> Hz.</summary>
    public static LoudnessResult Measure(IReadOnlyList<float[]> channels, int rate)
    {
        if (channels.Count == 0 || rate <= 0)
        {
            return new LoudnessResult(-120, -70, -120, -120, 0);
        }
        int n = channels[0].Length;
        int padded = n + (int)(PadSeconds * rate);
        int hop = rate / 10;
        int blocks = padded / hop;
        // Mean square per 100 ms hop, summed over channels (every channel weighs 1: mono, left, right).
        var hopEnergy = new double[blocks];
        double samplePeak = 0;
        double truePeak = 0;
        foreach (float[] x in channels)
        {
            var k = new KWeighting(rate);
            for (int i = 0; i < blocks * hop; i++)
            {
                double v = i < n ? x[i] : 0.0;
                double y = k.Next(v);
                hopEnergy[i / hop] += y * y;
                samplePeak = Math.Max(samplePeak, Math.Abs(v));
            }
            truePeak = Math.Max(truePeak, TruePeakLinear(x, rate));
        }
        // Momentary loudness every 100 ms over the last 400 ms (zeros before the start).
        var momentary = new double[blocks];
        double best = double.NegativeInfinity;
        for (int b = 0; b < blocks; b++)
        {
            double sum = 0;
            for (int j = Math.Max(0, b - 3); j <= b; j++)
            {
                sum += hopEnergy[j];
            }
            momentary[b] = LoudnessOf(sum / (4.0 * hop));
            best = Math.Max(best, momentary[b]);
        }
        // Integrated: the same blocks, gated at -70 LUFS and then 10 LU under their mean.
        double integrated = -70;
        var kept = new List<double>();
        foreach (double m in momentary)
        {
            if (m > -70)
            {
                kept.Add(m);
            }
        }
        if (kept.Count > 0)
        {
            double mean = MeanLoudness(kept);
            var gated = kept.FindAll(m => m > mean - 10);
            if (gated.Count > 0)
            {
                integrated = MeanLoudness(gated);
            }
        }
        return new LoudnessResult(Math.Round(best, 1), Math.Round(integrated, 1), Math.Round(Db(truePeak), 2),
            Math.Round(Db(samplePeak), 2), (double)n / rate);
    }

    private static double MeanLoudness(List<double> blocks)
    {
        double sum = 0;
        foreach (double m in blocks)
        {
            sum += Math.Pow(10, (m + 0.691) / 10);
        }
        return LoudnessOf(sum / blocks.Count);
    }

    private static double LoudnessOf(double meanSquare) => meanSquare <= 1e-20 ? -120 : -0.691 + 10 * Math.Log10(meanSquare);

    private static double Db(double linear) => linear <= 1e-9 ? -180 : 20 * Math.Log10(linear);

    /// <summary>The true peak of one channel in dBTP (4× oversampling below 96 kHz).</summary>
    public static double TruePeak(float[] x, int rate) => Math.Round(Db(TruePeakLinear(x, rate)), 2);

    private const int Taps = 12;

    private static double[][]? _phases;

    private static double TruePeakLinear(float[] x, int rate)
    {
        double peak = 0;
        foreach (float v in x)
        {
            peak = Math.Max(peak, Math.Abs(v));
        }
        if (rate >= 96000)
        {
            return peak;
        }
        double[][] phases = _phases ??= Interpolator();
        // y[4m + p] = Σ x[m − k] h_p[k]; the filter is centred half its length back.
        for (int m = 0; m < x.Length + Taps; m++)
        {
            for (int p = 0; p < 4; p++)
            {
                double[] h = phases[p];
                double y = 0;
                for (int k = 0; k < Taps; k++)
                {
                    int i = m - k;
                    if (i >= 0 && i < x.Length)
                    {
                        y += x[i] * h[k];
                    }
                }
                peak = Math.Max(peak, Math.Abs(y));
            }
        }
        return peak;
    }

    /// <summary>A 48-tap windowed-sinc interpolator for 4× upsampling, as four 12-tap phases with unit gain each.</summary>
    private static double[][] Interpolator()
    {
        const int length = Taps * 4;
        double centre = (length - 1) / 2.0;
        var phases = new double[4][];
        for (int p = 0; p < 4; p++)
        {
            phases[p] = new double[Taps];
        }
        for (int i = 0; i < length; i++)
        {
            double t = (i - centre) / 4.0;
            double sinc = Math.Abs(t) < 1e-12 ? 1 : Math.Sin(Math.PI * t) / (Math.PI * t);
            double window = 0.5 - 0.5 * Math.Cos(2 * Math.PI * (i + 0.5) / length);
            phases[i % 4][i / 4] = sinc * window;
        }
        foreach (double[] h in phases)
        {
            double sum = 0;
            foreach (double v in h)
            {
                sum += v;
            }
            for (int k = 0; k < h.Length; k++)
            {
                h[k] /= sum;
            }
        }
        return phases;
    }

    /// <summary>BS.1770's K-weighting: a high shelf (+4 dB above ~1.7 kHz) then a high-pass at ~38 Hz, for any rate.</summary>
    private sealed class KWeighting
    {
        private readonly double _b0, _b1, _b2, _a1, _a2, _c0, _c1, _c2, _d1, _d2;
        private double _x1, _x2, _y1, _y2, _u1, _u2, _z1, _z2;

        public KWeighting(int rate)
        {
            // Pre-filter (high shelf), after ITU-R BS.1770-4 and its usual analog prototype.
            double f0 = 1681.974450955533, g = 3.999843853973347, q = 0.7071752369554196;
            double k = Math.Tan(Math.PI * f0 / rate);
            double vh = Math.Pow(10, g / 20);
            double vb = Math.Pow(vh, 0.4996667741545416);
            double a0 = 1 + k / q + k * k;
            _b0 = (vh + vb * k / q + k * k) / a0;
            _b1 = 2 * (k * k - vh) / a0;
            _b2 = (vh - vb * k / q + k * k) / a0;
            _a1 = 2 * (k * k - 1) / a0;
            _a2 = (1 - k / q + k * k) / a0;
            // RLB high-pass.
            f0 = 38.13547087602444;
            q = 0.5003270373238773;
            k = Math.Tan(Math.PI * f0 / rate);
            a0 = 1 + k / q + k * k;
            _c0 = 1;
            _c1 = -2;
            _c2 = 1;
            _d1 = 2 * (k * k - 1) / a0;
            _d2 = (1 - k / q + k * k) / a0;
        }

        public double Next(double x)
        {
            double y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
            _x2 = _x1;
            _x1 = x;
            _y2 = _y1;
            _y1 = y;
            double z = _c0 * y + _c1 * _u1 + _c2 * _u2 - _d1 * _z1 - _d2 * _z2;
            _u2 = _u1;
            _u1 = y;
            _z2 = _z1;
            _z1 = z;
            return z;
        }
    }
}
