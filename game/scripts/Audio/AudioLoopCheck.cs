using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Hollow.Client.Audio;

namespace HollowUniverse.Audio;

/// <summary>
/// Native regression: add this node to the main tree and it checks actual pooled Godot players, then quits 0/1.
/// Uses existing imported recordings with isolated event definitions. It does not modify catalogue, settings or saves.
/// This proves scheduling/lifecycle behavior; it is not a listening acceptance or a crowded-game performance benchmark.
/// </summary>
public partial class AudioLoopCheck : Node3D
{
    private AudioDirector _audio = null!;
    private int _checks;

    public override async void _Ready()
    {
        try
        {
            await Run();
            GD.Print($"[audio-loop-check] PASS {_checks} checks: 700 retained emitters, 2 spatial/1 flat voices, virtual gain/distance, event limits, eviction recovery, source cleanup.");
            _audio?.StopAll();
            GetTree().Quit(0);
        }
        catch (Exception e)
        {
            GD.PushError("[audio-loop-check] FAIL " + e);
            _audio?.StopAll();
            GetTree().Quit(1);
        }
    }

    private async Task Run()
    {
        string loopFile = SoundLibrary.Find("campfire.loop")?.Files[0]
            ?? throw new InvalidOperationException("campfire loop fixture is missing");
        SoundEventData rifle = SoundLibrary.Find("rifle.shot") ?? throw new InvalidOperationException("rifle fixture is missing");
        IReadOnlyList<string> shots = rifle.Layers[0].Files;
        var events = new Dictionary<string, SoundEventData>(StringComparer.Ordinal)
        {
            ["check.loop.a"] = LoopEvent("check.loop.a", loopFile),
            ["check.loop.b"] = LoopEvent("check.loop.b", loopFile),
            ["check.shot"] = new() { Key = "check.shot", Files = shots, Priority = 100, Limit = 2, UnitSize = 4, MaxDistance = 100, Occlude = false },
        };
        var camera = new Camera3D { Current = true };
        AddChild(camera);
        _audio = new AudioDirector
        {
            Voices3D = 2, Voices2D = 1, Listener = camera, Occlusion = false,
            Budget = AudioVoiceBudget.Default with { UpdateHz = 20 },
            EventLookup = key => events.GetValueOrDefault(key),
        };
        AddChild(_audio);
        _audio.Seed(41);
        var emitters = new Node3D[700];
        var handles = new SoundHandle[700];
        for (int i = 0; i < emitters.Length; i++)
        {
            var emitter = new Node3D { Position = new Vector3(i, 0, -1000) };
            AddChild(emitter);
            emitters[i] = emitter;
            handles[i] = _audio.Loop(i % 2 == 0 ? "check.loop.a" : "check.loop.b", emitter);
            Check(handles[i].IsValid, "every live source retains a handle even beyond a voice/candidate cap");
        }
        await Settle();
        Check(_audio.Loops.Logical == 700 && _audio.Busy.Placed == 0, "far crowd is virtual");
        Check(_audio.Loop("check.loop.a", emitters[698]) == handles[698] && _audio.Loops.Logical == 700, "same emitter/event is idempotent");
        emitters[698].Position = new Vector3(0, 0, -6);
        emitters[699].Position = new Vector3(0, 0, -12);
        await Settle();
        Check(_audio.HasVoice(handles[698]) && _audio.HasVoice(handles[699]) && _audio.Busy.Placed == 2, "last-created approaching sources both acquire voices");
        emitters[0].Position = new Vector3(0, 0, -1);
        await Settle();
        Check(_audio.HasVoice(handles[0]) && !_audio.HasVoice(handles[698]) && _audio.IsPlaying(handles[698]), "per-event limit favors nearer source and retains virtual handle");
        emitters[0].Position = new Vector3(0, 0, -1000);
        await Settle();
        Check(_audio.HasVoice(handles[698]), "same-event displaced loop recovers without caller retry");
        SoundHandle shot = _audio.Play("check.shot", new Vector3(0, 0, -1), 3);
        Check(shot.IsValid && _audio.HasVoice(shot) && _audio.Busy.Placed == 2, "shot steals within fixed spatial budget");
        Check(!_audio.HasVoice(handles[699]) && _audio.IsPlaying(handles[699]), "stolen loop remains logically active");
        for (int i = 0; i < 60 && _audio.IsPlaying(shot); i++) await Settle();
        Check(!_audio.IsPlaying(shot), "real one-shot playback finishes");
        await Settle();
        Check(_audio.HasVoice(handles[698]) && _audio.HasVoice(handles[699]), "loops recover when the one-shot releases its voice");
        _audio.SetGain(handles[698], -80, 0);
        await Settle();
        Check(!_audio.HasVoice(handles[698]) && _audio.IsPlaying(handles[698]), "silent engine layer virtualizes");
        _audio.SetPitch(handles[698], 1.4f);
        _audio.SetGain(handles[698], 0, 0.1f);
        await Settle();
        Check(_audio.HasVoice(handles[698]), "virtual pitch/gain controls survive reactivation");
        emitters[698].Position = new Vector3(0, 0, -1000);
        await Settle();
        _audio.Stop(handles[698], 0.1f);
        await Settle();
        Check(!_audio.IsPlaying(handles[698]), "faded stop retires a virtual loop");
        emitters[699].QueueFree();
        await Settle();
        Check(!_audio.IsPlaying(handles[699]) && _audio.Loops.Logical == 698 && _audio.Busy.Placed == 0, "freed owner releases logical and physical resources");
        SoundHandle bed = _audio.Loop2D("check.loop.a", startGainDb: -80);
        await Settle();
        Check(_audio.IsPlaying(bed) && _audio.Busy.Flat == 0, "silent flat bed is virtual");
        _audio.SetGain(bed, 0, 0);
        await Settle();
        Check(_audio.HasVoice(bed) && _audio.Busy.Flat == 1, "flat loop reacquires its separate pool");
        Check(SoundLibrary.Stream(loopFile, true) is AudioStreamOggVorbis { Loop: true } &&
            SoundLibrary.Stream(loopFile, false) is AudioStreamOggVorbis { Loop: false }, "loop and one-shot resource modes do not mutate one another");
        _audio.StopAll();
        Check(_audio.Loops.Logical == 0 && _audio.Busy == (0, 0), "StopAll clears every ownership and player");
        Check(_audio.GetChildCount() == 3, "native player nodes stay fixed at configured pool sizes");
        var second = new AudioDirector
        {
            Voices3D = 0, Voices2D = 1, Listener = camera, Occlusion = false,
            EventLookup = key => events.GetValueOrDefault(key),
        };
        AddChild(second);
        SoundHandle shared = second.Loop2D("check.loop.a", startGainDb: 0);
        await Settle();
        AudioStream? modeClone = SoundLibrary.Stream(loopFile, false);
        Check(SoundLibrary.OwnedModeCount > 0, "mode mismatch creates explicitly owned clones");
        _audio.QueueFree();
        _audio = null!;
        await Settle();
        Check(second.HasVoice(shared) && AudioDirector.Instance == second && GodotObject.IsInstanceValid(modeClone),
            "another director keeps shared streams alive after the first director exits");
        second.QueueFree();
        await Settle();
        Check(AudioDirector.Instance == null && SoundLibrary.OwnedModeCount == 0,
            "last director releases owned mode resources after clearing every player");
    }

    private static SoundEventData LoopEvent(string key, string file) => new()
    {
        Key = key, Files = new[] { file }, Loop = true, Priority = 40, Limit = 1,
        UnitSize = 4, MaxDistance = 35, Occlude = false,
    };

    private async Task Settle() => await ToSignal(GetTree().CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout);

    private void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }
}
