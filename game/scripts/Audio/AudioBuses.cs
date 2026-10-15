using System;
using Godot;
using Hollow.Client.Audio;

namespace HollowUniverse.Audio;

/// <summary>
/// The audio bus layout (GROUND.md §4.9), made in code so no .tres has to be kept in step:
/// <code>
/// Master  ← hard limiter at −1 dBFS
///  ├ SFX        ← reverb (dry; wet follows indoors/outdoors)
///  │  └ SFX Occluded        ← low-pass 900 Hz, −3 dB
///  ├ Footsteps  ← reverb (the same)
///  │  └ Footsteps Occluded  ← low-pass 900 Hz, −3 dB
///  ├ UI
///  └ Ambience
/// </code>
/// A voice whose line of sight to the listener is blocked plays on the bus's "Occluded" child.
/// </summary>
public static class AudioBuses
{
    public const string Master = "Master";
    public const string Sfx = "SFX";
    public const string Footsteps = "Footsteps";
    public const string Ui = "UI";
    public const string Ambience = "Ambience";
    public const string OccludedSuffix = " Occluded";

    /// <summary>Low-pass cut-off of an occluded sound, Hz.</summary>
    public const float OccludedCutoff = 900f;

    private static float _space;

    /// <summary>Creates the buses and their effects if they are missing; safe to call again.</summary>
    public static void Ensure()
    {
        int master = AudioServer.GetBusIndex(Master);
        if (master < 0)
        {
            master = 0;
        }
        if (Effect<AudioEffectHardLimiter>(master) == null)
        {
            AudioServer.AddBusEffect(master, new AudioEffectHardLimiter { CeilingDb = -1f, PreGainDb = 0f, Release = 0.1f });
        }
        foreach (string name in new[] { Sfx, Footsteps, Ui, Ambience })
        {
            int bus = Bus(name, Master);
            if ((name == Sfx || name == Footsteps) && Effect<AudioEffectReverb>(bus) == null)
            {
                AudioServer.AddBusEffect(bus, new AudioEffectReverb
                {
                    RoomSize = 0.35f,
                    Damping = 0.6f,
                    Spread = 0.8f,
                    Hipass = 0.15f,
                    Dry = 1f,
                    Wet = 0f,
                    PredelayMsec = 12f,
                    PredelayFeedback = 0.2f,
                });
            }
        }
        foreach (string name in new[] { Sfx, Footsteps })
        {
            int occluded = Bus(name + OccludedSuffix, name);
            if (Effect<AudioEffectLowPassFilter>(occluded) == null)
            {
                AudioServer.AddBusEffect(occluded, new AudioEffectLowPassFilter { CutoffHz = OccludedCutoff, Resonance = 0.5f });
                AudioServer.SetBusVolumeDb(occluded, -3f);
            }
        }
        if (Space is { } space)
        {
            _preset = null;
            SetSpace(space, 0);
        }
        else
        {
            SetSpace(_space);
        }
    }

    /// <summary>
    /// The space around the listener: 0 outdoors (dry: open air has no tail of its own), 1 inside a room (a short,
    /// damped room reverb on world sounds and footsteps).
    /// </summary>
    public static void SetSpace(float indoor)
    {
        _space = Math.Clamp(indoor, 0f, 1f);
        _preset = null;
        Space = null;
        foreach (string name in new[] { Sfx, Footsteps })
        {
            int bus = AudioServer.GetBusIndex(name);
            if (bus >= 0 && Effect<AudioEffectReverb>(bus) is AudioEffectReverb reverb)
            {
                reverb.Wet = 0.02f + 0.2f * _space;
                reverb.RoomSize = 0.2f + 0.25f * _space;
                reverb.Damping = 0.7f - 0.2f * _space;
            }
        }
    }

    private static float[]? _preset;

    /// <summary>The space the listener is in now, as last set (<see cref="SetSpace(SoundSpace, double)"/>).</summary>
    public static SoundSpace? Space { get; private set; }

    /// <summary>
    /// The listener's space as sounds.json's <c>spaces</c> give it (IRON_WAKE.md §10.1: the open air, a ship's deck, its
    /// bridge): every term of the world buses' reverb moves toward the preset over about a third of a second each frame
    /// (<paramref name="delta"/>; 0 at once), so stepping through a door doesn't click the tail in.
    /// </summary>
    public static void SetSpace(SoundSpace space, double delta)
    {
        Space = space;
        float[] want = { space.RoomSize, space.Damping, space.Spread, space.Hipass, space.Dry, space.Wet, space.PredelayMs, space.PredelayFeedback };
        float k = delta <= 0 || _preset == null ? 1f : 1f - MathF.Exp(-(float)delta / 0.3f);
        _preset ??= (float[])want.Clone();
        bool changed = false;
        for (int i = 0; i < want.Length; i++)
        {
            float next = _preset[i] + (want[i] - _preset[i]) * k;
            changed |= MathF.Abs(next - _preset[i]) > 1e-4f || k >= 1f;
            _preset[i] = next;
        }
        if (!changed)
        {
            return;
        }
        foreach (string name in new[] { Sfx, Footsteps })
        {
            int bus = AudioServer.GetBusIndex(name);
            if (bus >= 0 && Effect<AudioEffectReverb>(bus) is AudioEffectReverb reverb)
            {
                reverb.RoomSize = Math.Clamp(_preset[0], 0f, 1f);
                reverb.Damping = Math.Clamp(_preset[1], 0f, 1f);
                reverb.Spread = Math.Clamp(_preset[2], 0f, 1f);
                reverb.Hipass = Math.Clamp(_preset[3], 0f, 1f);
                reverb.Dry = Math.Clamp(_preset[4], 0f, 1f);
                reverb.Wet = Math.Clamp(_preset[5], 0f, 1f);
                reverb.PredelayMsec = Math.Clamp(_preset[6], 0f, 500f);
                reverb.PredelayFeedback = Math.Clamp(_preset[7], 0f, 0.98f);
            }
        }
    }

    /// <summary>A bus's volume as the player sets it (0–1, a perceptual curve).</summary>
    public static void SetVolume(string bus, float linear)
    {
        int index = AudioServer.GetBusIndex(bus);
        if (index >= 0)
        {
            linear = Math.Clamp(linear, 0f, 1f);
            AudioServer.SetBusVolumeDb(index, linear <= 0.001f ? -80f : 40f * MathF.Log10(linear));
        }
    }

    /// <summary>The bus a voice of <paramref name="bus"/> plays on, occluded or not.</summary>
    public static string For(string bus, bool occluded) =>
        occluded && (bus == Sfx || bus == Footsteps) ? bus + OccludedSuffix : bus;

    private static int Bus(string name, string send)
    {
        int index = AudioServer.GetBusIndex(name);
        if (index < 0)
        {
            AudioServer.AddBus();
            index = AudioServer.BusCount - 1;
            AudioServer.SetBusName(index, name);
        }
        AudioServer.SetBusSend(index, send);
        return index;
    }

    private static T? Effect<T>(int bus) where T : AudioEffect
    {
        for (int i = 0; i < AudioServer.GetBusEffectCount(bus); i++)
        {
            if (AudioServer.GetBusEffect(bus, i) is T effect)
            {
                return effect;
            }
        }
        return null;
    }
}
