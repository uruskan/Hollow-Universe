using System;
using System.Collections.Generic;
using Godot;
using Hollow.Client.Audio;

namespace HollowUniverse.Audio;

/// <summary>
/// The world's ambience around the listener (GROUND.md §4.9). The surface view gives it, every frame or so, where
/// the camera is (<see cref="Update"/>: altitude, exposure, biome, forest around, distance to the sea, the sun's
/// elevation, wind, storm, indoors); <see cref="AmbienceMix"/> turns that into the level of each bed. Beds are flat
/// loops on the Ambience bus that glide to their levels over a couple of seconds, start when needed and stop after a
/// while unheard. Accents (owls, crows, distant thunder) come at random at the mix's rates, placed far around the
/// listener.
/// </summary>
public partial class AmbienceDirector : Node
{
    private sealed class Bed
    {
        public string Key = "";
        public double Level;
        public double Target;
        public double Silent;
        public SoundHandle Handle = SoundHandle.None;
    }

    private readonly Dictionary<string, Bed> _beds = new(StringComparer.Ordinal);
    private readonly Random _rng = new();

    /// <summary>Seconds for a bed to move most of the way to a new level.</summary>
    public double Glide { get; set; } = 1.5;

    /// <summary>Plays accents (owls, crows, thunder).</summary>
    public bool Accents { get; set; } = true;

    /// <summary>The director to play through; <see cref="AudioDirector.Instance"/> when not set.</summary>
    public AudioDirector? Director { get; set; }

    /// <summary>Away from the planet's centre at the listener, in render space (accents are placed around it).</summary>
    public Vector3 Up { get; set; } = Vector3.Up;

    /// <summary>The mix last computed (the viewer shows it).</summary>
    public AmbienceLevels Levels { get; private set; } = new();

    /// <summary>Call every frame (or a few times a second) with where the listener is; <paramref name="ear"/> in render
    /// space places the accents.</summary>
    public void Update(double delta, in AmbienceInput input, Vector3 ear)
    {
        AudioDirector? director = Director ?? AudioDirector.Instance;
        if (director == null)
        {
            return;
        }
        Levels = AmbienceMix.Compute(input);
        double k = 1 - Math.Exp(-delta / Math.Max(0.05, Glide));
        foreach ((string key, double target) in Levels.Beds())
        {
            if (!_beds.TryGetValue(key, out Bed? bed))
            {
                bed = new Bed { Key = key };
                _beds[key] = bed;
            }
            bed.Target = target;
            bed.Level += (target - bed.Level) * k;
            bool audible = bed.Level > 0.015;
            if (audible && !director.IsPlaying(bed.Handle))
            {
                bed.Handle = director.Loop2D(key, 0f, startGainDb: -80f);
                director.SetGain(bed.Handle, Db(bed.Level), 0.3f);
            }
            if (director.IsPlaying(bed.Handle))
            {
                director.SetGain(bed.Handle, Db(bed.Level), 0.2f);
                bed.Silent = audible ? 0 : bed.Silent + delta;
                if (bed.Silent > 4)
                {
                    director.Stop(bed.Handle, 0.5f);
                    bed.Handle = SoundHandle.None;
                }
            }
        }
        if (Accents)
        {
            Accent(director, AmbienceMix.OwlKey, Levels.OwlRate, 50, 150, ear, delta);
            Accent(director, AmbienceMix.CrowKey, Levels.CrowRate, 70, 220, ear, delta);
            Accent(director, AmbienceMix.ThunderKey, Levels.ThunderRate, 1500, 4000, ear, delta);
        }
    }

    /// <summary>The volume a bed plays at now (dB), or null when it isn't playing.</summary>
    public float? PlayingDb(string key)
    {
        AudioDirector? director = Director ?? AudioDirector.Instance;
        return director != null && _beds.TryGetValue(key, out Bed? bed) ? director.VolumeOf(bed.Handle) : null;
    }

    /// <summary>Stops every bed.</summary>
    public void StopAll(float fade = 0.5f)
    {
        AudioDirector? director = Director ?? AudioDirector.Instance;
        foreach (Bed bed in _beds.Values)
        {
            director?.Stop(bed.Handle, fade);
            bed.Handle = SoundHandle.None;
            bed.Level = 0;
        }
    }

    /// <summary>Plays an accent now and then: <paramref name="perMinute"/> on average, somewhere around the listener.</summary>
    private void Accent(AudioDirector director, string key, double perMinute, float near, float far, Vector3 ear, double delta)
    {
        if (perMinute <= 0 || _rng.NextDouble() >= perMinute / 60.0 * delta)
        {
            return;
        }
        double angle = _rng.NextDouble() * Math.Tau;
        float distance = near + (float)_rng.NextDouble() * (far - near);
        Vector3 up = Up.LengthSquared() > 0.5f ? Up.Normalized() : Vector3.Up;
        Vector3 east = up.Cross(Mathf.Abs(up.Z) < 0.9f ? Vector3.Back : Vector3.Right).Normalized();
        Vector3 north = east.Cross(up);
        Vector3 at = ear + (east * (float)Math.Cos(angle) + north * (float)Math.Sin(angle)) * distance + up * 15f;
        director.Play(key, at, 0f);
    }

    private static float Db(double level) => level <= 0.0001 ? -80f : (float)(20 * Math.Log10(level));
}
