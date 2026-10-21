using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Hollow.Client;
using Hollow.Client.Audio;
using Hollow.Simulation.Movement;
using HollowUniverse.Audio;
using HollowUniverse.UI;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

public partial class GroundTour
{
    // Actual predicted movement on the hosted terrain, never direct calls to play a footstep.
    // Use a fresh/copied save and --tour <folder> --tour-steps audio-movement --sound-trace.
    // The caller chooses terrain with --lat/--lon/--spawn and runs this with a real audio driver and no other players.
    private async Task AudioMovement()
    {
        AudioDirector director = AudioDirector.Instance ?? throw new InvalidOperationException("audio-movement needs sound enabled (omit --mute)");
        if (View.IsolatedCapture) throw new InvalidOperationException("audio-movement needs ordinary prediction input (omit --shot)");
        View.Interface.Open(GroundScreen.Hud);
        View.Scripted = new InputFrame();
        await Until(() => Client.HasCharacter && !View.LoadingShown && (Client.Character.Flags & MoveFlags.OnGround) != 0, 20);
        if (!Client.HasCharacter || Me.IsDead || Client.Character.Frame != 0 ||
            (Client.Character.Flags & MoveFlags.OnGround) == 0 || (Client.Character.Cheats & MoveCheats.Noclip) != 0)
            throw new InvalidOperationException("audio-movement needs a living grounded character on the planet, with noclip off");
        await Seconds(1.0);
        await Shot("audio-movement-start");

        var events = new List<object>();
        var frames = new List<object>();
        var stages = new List<object>();
        var captures = new List<object>();
        var errors = new List<string>();
        var footfalls = new List<(string Stage, string Key)>();
        string stage = "initializing";
        string? failure = null;
        Exception? originalError = null;
        double start = Time.GetTicksMsec() / 1000.0;
        double captureDuration = 0;
        const double durationTolerance = 0.25;
        double Now() => Time.GetTicksMsec() / 1000.0 - start;
        void Failed(string message, Exception? error = null)
        {
            failure ??= message;
            originalError ??= error;
            errors.Add(message);
        }
        object Pose(bool sampleSurface = true)
        {
            CharacterState c = Client.Character;
            FootInput input = Footsteps.Input(c, Client.Frame, (c.Flags & MoveFlags.Sprinting) != 0,
                sampleSurface ? GroundAudio.SurfaceAt(Client, c.Position, c.Frame) : FootSurface.Grass,
                GroundAudio.WaterDepth(Client, c.Position));
            return new { position = new[] { c.Position.X, c.Position.Y, c.Position.Z }, c.Frame, flags = c.Flags.ToString(),
                stance = c.Stance.ToString(), input.Speed, input.VerticalSpeed,
                surface = sampleSurface ? input.Surface.ToString() : null, input.WaterDepth };
        }
        void Started(string key, string layer, float distance, float db, float pitch)
        {
            events.Add(new { seconds = Now(), stage, key, layer, distance, db, pitch, pose = Pose() });
            if (key.StartsWith("footstep.", StringComparison.Ordinal)) footfalls.Add((stage, key));
            GD.Print($"[audio-movement] {Now():0.000}s {stage}: {key} ({db:0.0} dB, pitch {pitch:0.000})");
        }
        var recordings = new List<(string Name, int Bus, AudioEffectRecord Effect)>();
        var buses = new List<object>();
        try
        {
            foreach ((string busName, string name) in new[] { (AudioBuses.Master, "mix"), (AudioBuses.Footsteps, "feet") })
            {
                int bus = AudioServer.GetBusIndex(busName);
                if (bus < 0) throw new InvalidOperationException($"audio-movement missing {busName} bus");
                var effect = new AudioEffectRecord { Format = AudioStreamWav.FormatEnum.Format16Bits };
                recordings.Add((name, bus, effect));
                AudioServer.AddBusEffect(bus, effect);
            }
            foreach (string name in new[] { AudioBuses.Master, AudioBuses.Footsteps, AudioBuses.Sfx, AudioBuses.Ambience, AudioBuses.Ui })
            {
                int bus = AudioServer.GetBusIndex(name);
                buses.Add(new { name, volumeDb = AudioServer.GetBusVolumeDb(bus), mute = AudioServer.IsBusMute(bus) });
            }
            director.Started += Started;
            start = Time.GetTicksMsec() / 1000.0;
            foreach (var recording in recordings) recording.Effect.SetRecordingActive(true);
            await Stage("idle", new InputFrame(), 2);
            await Stage("walk", new InputFrame { Forward = 1 }, 4);
            await Stage("stop-walk", new InputFrame(), 1);
            await Stage("run", new InputFrame { Forward = 1, Sprint = true }, 4);
            await Stage("stop-run", new InputFrame(), 1);
            await Stage("crouch", new InputFrame { Forward = 1, Crouch = true }, 4);
            await Stage("stop-crouch", new InputFrame(), 1);
            for (int i = 1; i <= 3; i++)
                await Stage("jump-" + i, new InputFrame { Jump = true, JumpPressed = true }, 2.6, jump: true);
            await Stage("idle-end", new InputFrame(), 2);

            foreach (string gait in new[] { "walk", "run", "crouch" })
                if (!footfalls.Any(e => e.Stage == gait && e.Key.StartsWith("footstep." + gait + ".", StringComparison.Ordinal)))
                    throw new InvalidOperationException($"audio-movement: no {gait} footsteps occurred; inspect the trajectory for obstruction");
            foreach (string idle in new[] { "idle", "idle-end" })
                if (footfalls.Any(e => e.Stage == idle)) throw new InvalidOperationException($"audio-movement: unexpected feet during {idle}");
            for (int i = 1; i <= 3; i++)
            {
                var jump = footfalls.Where(e => e.Stage == "jump-" + i).ToArray();
                if (jump.Length != 1 || !jump[0].Key.StartsWith("footstep.land.", StringComparison.Ordinal))
                    throw new InvalidOperationException($"audio-movement: jump {i} produced {jump.Length} foot events instead of exactly one landing");
            }
        }
        catch (Exception error)
        {
            Failed(error.Message, error);
        }
        finally
        {
            View.Scripted = new InputFrame();
            director.Started -= Started;
            captureDuration = Now();
            try
            {
                // Freeze both buses at the same endpoint before decoding or writing either file.
                foreach (var recording in recordings)
                {
                    try { recording.Effect.SetRecordingActive(false); }
                    catch (Exception error) { Failed($"{recording.Name} recording could not stop: {error.Message}", error); }
                }
                foreach (var recording in recordings)
                {
                    try
                    {
                        using AudioStreamWav wav = recording.Effect.GetRecording();
                        string path = Path.Combine(Folder, "audio-movement-" + recording.Name + ".wav");
                        Error saved = wav.SaveToWav(path);
                        byte[] pcm = wav.Data;
                        int samples = pcm.Length / 2, activeSamples = 0, peak = 0;
                        for (int i = 0; i + 1 < pcm.Length; i += 2)
                        {
                            int sample = (short)(pcm[i] | pcm[i + 1] << 8);
                            if (sample != 0) activeSamples++;
                            peak = Math.Max(peak, Math.Abs(sample));
                        }
                        int channels = wav.Stereo ? 2 : 1;
                        double actualDuration = wav.GetLength();
                        double durationDifference = Math.Abs(actualDuration - captureDuration);
                        bool valid = saved == Error.Ok && wav.Format == AudioStreamWav.FormatEnum.Format16Bits &&
                            pcm.Length % (2 * channels) == 0 && samples > 0 && activeSamples > 0 &&
                            actualDuration >= 1 && durationDifference <= durationTolerance;
                        captures.Add(new { recording.Name, path, saved = saved.ToString(), valid,
                            expectedDuration = captureDuration, actualDuration, durationDifference, durationTolerance,
                            mixRate = wav.MixRate, channels, samples, sampleFrames = samples / channels, activeSamples,
                            activeFraction = samples > 0 ? (double)activeSamples / samples : 0, peakPcm16 = peak });
                        GD.Print($"[audio-movement] recording {saved}: {path} ({actualDuration:0.000}s / {captureDuration:0.000}s expected, {activeSamples}/{samples} active samples)");
                        if (!valid) Failed($"{recording.Name} recording invalid: save={saved}, duration={actualDuration:0.000}s (expected {captureDuration:0.000}s ±{durationTolerance:0.00}s), active samples={activeSamples}/{samples}");
                    }
                    catch (Exception error)
                    {
                        Failed($"{recording.Name} recording extraction/save failed: {error.Message}", error);
                        captures.Add(new { recording.Name, valid = false, error = error.Message });
                    }
                }
            }
            finally { RemoveRecorders(); }
        }
        try
        {
            File.WriteAllText(Path.Combine(Folder, "audio-movement.json"), JsonSerializer.Serialize(new
            {
                technicalChecksPassed = failure == null, failure, errors,
                qualityAccepted = false,
                evidence = "Unedited Godot Master and Footsteps bus recordings during ordinary hosted-client prediction. Technical checks do not establish audible quality or user acceptance.",
                eventTiming = "Events timestamp sound start requests, not sample-accurate audible onset; audio-buffer latency and any 3D distance delay still apply. Run without other players because event logging includes every voice.",
                mixRate = AudioServer.GetMixRate(), duration = captureDuration, captures, buses, stages, events, frames,
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error) { Failed($"audio-movement metadata could not be written: {error.Message}", error); }
        if (failure != null) throw new InvalidOperationException(failure, originalError);
        await Shot("audio-movement-end");
        GD.Print("[audio-movement] technical movement/cadence checks passed; audible quality remains a listening decision");

        void RemoveRecorders()
        {
            foreach (var recording in recordings)
            {
                try
                {
                    for (int i = AudioServer.GetBusEffectCount(recording.Bus) - 1; i >= 0; i--)
                        if (AudioServer.GetBusEffect(recording.Bus, i) == recording.Effect) AudioServer.RemoveBusEffect(recording.Bus, i);
                }
                catch (Exception error) { Failed($"{recording.Name} recording cleanup failed: {error.Message}", error); }
                finally
                {
                    try { recording.Effect.Dispose(); }
                    catch (Exception error) { Failed($"{recording.Name} recorder disposal failed: {error.Message}", error); }
                }
            }
        }

        async Task Stage(string name, InputFrame input, double seconds, bool jump = false)
        {
            stage = name;
            double began = Now(), previous = began;
            bool released = false;
            Num.Vector3 at = Client.Character.Position;
            stages.Add(new { stage, seconds = began, requestedDuration = seconds, pose = Pose() });
            View.Scripted = input;
            GD.Print($"[audio-movement] {began:0.000}s begin {name}");
            while (Now() - began < seconds)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (Client.State != ClientState.Playing || Me.IsDead) throw new InvalidOperationException("audio-movement lost its living connection");
                double now = Now();
                frames.Add(new { seconds = now, delta = now - previous, stage, pose = Pose(sampleSurface: false) });
                previous = now;
                if (jump && !released && now - began >= 0.08)
                {
                    View.Scripted = new InputFrame();
                    released = true;
                }
            }
            float displaced = Num.Vector3.Distance(at, Client.Character.Position);
            GD.Print($"[audio-movement] {Now():0.000}s end {name}, displaced {displaced:0.00}m");
            if (name is "walk" or "run" or "crouch" && displaced < 1)
                throw new InvalidOperationException($"audio-movement: {name} displaced only {displaced:0.00}m; the route may be obstructed");
        }
    }
}
