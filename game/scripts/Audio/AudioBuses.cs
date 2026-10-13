using System;
using Godot;

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
        SetSpace(_space);
    }

    /// <summary>
    /// The space around the listener: 0 outdoors (dry: open air has no tail of its own), 1 inside a room (a short,
    /// damped room reverb on world sounds and footsteps).
    /// </summary>
    public static void SetSpace(float indoor)
    {
        _space = Math.Clamp(indoor, 0f, 1f);
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
