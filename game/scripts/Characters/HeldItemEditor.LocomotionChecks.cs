using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Hollow.Client.Audio;
using Hollow.Simulation.Content;
using HollowUniverse.Common;

namespace HollowUniverse.Characters;

public partial class HeldItemEditor
{
    private sealed record LocomotionCheckSample(string Body, string Item, string State, int Frame, string Action,
        float ActionPhase, string? SourceClip, double SourceTime, bool SourceFidelityChecked, float SourceArmErrorDegrees,
        float GripPositionErrorMetres, float GripBasisError, float WholeRigPositionErrorMetres,
        float WholeRigBasisError, float Gait, float Sprint, float ActionWeight, double DistancePhase,
        float[] OverlayPosition, float[] OverlayEulerDegrees, float PalmGapMetres);

    /// <summary>Bounded native FPS checks. Only the source-bound families opting into locomotion are exercised;
    /// no server, world generation, profile writes or gameplay hit/audio events are involved.</summary>
    private async void RunLocomotionChecks()
    {
        const int columns = 4, rows = 4, tileWidth = 384, tileHeight = 216;
        string output = Option("locomotion-check-out", Path.Combine(ProjectSettings.GlobalizePath("user://"),
            "held-locomotion-check-" + Guid.NewGuid().ToString("N")));
        bool outputReady = false;
        var failures = new List<string>();
        var samples = new List<LocomotionCheckSample>();
        var screenshots = new List<string>();
        var cadenceChecks = new List<object>();
        var pitchChecks = new List<object>();
        var actionChecks = new List<object>();
        var crouchChecks = new List<object>();
        float maximumStepPosition = 0, maximumStepDegrees = 0;
        string sourceFingerprint = DiskFingerprint();
        void SaveReport() => File.WriteAllText(Path.Combine(output, "locomotion-check.json"), JsonSerializer.Serialize(new
        {
            status = failures.Count == 0 ? "pass" : "fail", sourceFingerprint,
            locomotionConfiguration = Godot.FileAccess.GetFileAsString(HeldLocomotion.ResourcePath),
            limits = new { sourceArmDegrees = 0.1, gripMetres = 0.0002, rigMetres = 0.00002,
                overlayStepMetresAt60Hz = 0.035, overlayStepDegreesAt60Hz = 4 },
            note = "Numeric invariants and labelled native renders; visual quality, world contact, source equip trajectory and sound are not asserted. Distance uses the shared footfall stride convention, not audio event phase.",
            maximumStepPosition, maximumStepDegrees, cadenceChecks, pitchChecks, actionChecks, crouchChecks, failures, screenshots, samples,
        }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        try
        {
            output = Path.GetFullPath(output);
            Require(!Directory.Exists(output) || !Directory.EnumerateFileSystemEntries(output).Any(),
                "Locomotion output must be a new or empty directory: " + output);
            Directory.CreateDirectory(output); outputReady = true;
            SetProcess(false); SetPlaying(false); _aiming = false; _contactPreview = false; _showTarget = false;
            _showGizmo = false; UpdateGizmo(); SetView(0);
            _previewContainer.Stretch = false; _preview.Size = new Vector2I(tileWidth * 2, tileHeight * 2);
            _captureCaption.Visible = true;
            _captureCaption.AddThemeFontSizeOverride("font_size", Ui.Px(18));
            HeldItemModel[] items = HeldItems.All.Where(item => HeldLocomotion.ForFamily(item.Presentation.Family) != null &&
                item.Presentation.Actions.TryGetValue("idle", out HeldPresentationClip? idle) && idle.Skeletal != null &&
                (!_options.ContainsKey("item") || item.Id == Option("item", ""))).ToArray();
            Require(HeldLocomotion.LoadError == null, "Invalid locomotion configuration: " + HeldLocomotion.LoadError);
            Require(items.Length > 0, "No selected source-bound item opts into held locomotion.");
            BodyType[] bodies = _options.ContainsKey("locomotion-both-bodies")
                ? new[] { BodyType.Male, BodyType.Female } : new[] { _body };
            foreach (BodyType body in bodies)
            {
                _body = body; BuildCharacters(); _viewmodel.SetProcess(false); _character.SetProcess(false);
                foreach (HeldItemModel item in items)
                {
                    SelectItem(item); SelectAction("idle"); SetPhase(0); SetView(0);
                    _viewmodel._Process(0); // Reset any live state before measuring the uncomposed accepted hold.
                    Skeleton3D skeleton = FindCheckSkeleton(_viewmodel);
                    Node3D rig = skeleton.GetParent<Node3D>();
                    Transform3D restRig = rig.Transform;
                    Transform3D restPresentedGrip = _viewmodel.PresentedGrip ?? throw new InvalidOperationException("Missing baseline held grip.");
                    int hand = skeleton.FindBone(item.LeftHanded ? "hand_l" : "hand_r");
                    Require(hand >= 0, item.Id + ": primary hand missing.");
                    Transform3D restGrip = AdmireHandToGrip(skeleton, hand);
                    _viewmodel.ClearPreview();
                    var rest = new ViewmodelFrame { Grounded = true, GroundSpeed = 0 };
                    var walk = new ViewmodelFrame { Grounded = true, GroundSpeed = 2.5f, LocalVelocity = new Vector3(0, 0, -2.5f) };
                    var run = new ViewmodelFrame { Grounded = true, GroundSpeed = 5.5f, Sprinting = true, LocalVelocity = new Vector3(0, 0, -5.5f) };
                    HeldLocomotionProfile locomotion = HeldLocomotion.ForFamily(item.Presentation.Family)!;
                    ItemDef definition = _content.Item(item.Id) ?? throw new InvalidOperationException(item.Id + ": missing item definition.");
                    MeleeStats melee = definition.Melee ?? throw new InvalidOperationException(item.Id + ": source tool needs melee timing.");
                    HeldPresentationClip swing = item.Presentation.Actions["swing"];
                    float swingSeconds = HeldActionTiming.Duration(definition, "swing", swing.Duration);
                    float contactSeconds = HeldActionTiming.Seconds(melee.StrikeTicks);
                    float equipSeconds = item.Presentation.Actions["equip"].Duration;
                    const double frameSeconds = 1d / 60d;
                    double settleSeconds = 12 * Math.Max(locomotion.GaitResponseSeconds, locomotion.SprintResponseSeconds);
                    int frameNumber = 0, tile = 0;
                    using Image sheet = Image.CreateEmpty(columns * tileWidth, rows * tileHeight, false, Image.Format.Rgba8);
                    sheet.Fill(new Color(0.025f, 0.035f, 0.05f));
                    LocomotionCheckSample Measure(string state, bool sourceFidelity = true)
                    {
                        string context = $"{body}/{item.Id}/{state}/{frameNumber}";
                        CheckFinite(_viewmodel, context);
                        Require(_viewmodel.UsingSkeletalMotion && _viewmodel.SkeletalMotionError == null, context + ": source motion must remain active.");
                        Transform3D grip = AdmireHandToGrip(skeleton, hand), overlay = _viewmodel.SkeletalLocomotionTransform;
                        Transform3D expectedRig = overlay * restRig;
                        float gripPosition = grip.Origin.DistanceTo(restGrip.Origin), gripBasis = LocomotionBasisError(grip.Basis, restGrip.Basis);
                        float rigPosition = rig.Transform.Origin.DistanceTo(expectedRig.Origin), rigBasis = LocomotionBasisError(rig.Transform.Basis, expectedRig.Basis);
                        float sourceError = sourceFidelity ? LocomotionSourceError(skeleton) : 0;
                        Require(!sourceFidelity || sourceError <= 0.1f, context + $": source arm rotation changed by {sourceError:0.000} degrees.");
                        // Admire intentionally solves its arms after the source pose; locomotion itself must still be zero.
                        Require(gripPosition < (sourceFidelity ? 0.0002f : 0.002f) && gripBasis < 0.001f,
                            context + ": hand/item relative transform changed.");
                        Require(rigPosition < 0.00002f && rigBasis < 0.00002f, context + ": arms did not receive the same complete-rig composition.");
                        Vector3 euler = overlay.Basis.GetEuler() * (180f / MathF.PI);
                        var sample = new LocomotionCheckSample(body.ToString(), item.Id, state, frameNumber, _viewmodel.ActiveAction,
                            _viewmodel.ActivePhase, _viewmodel.SkeletalMotionClip, _viewmodel.SkeletalSourceTime, sourceFidelity, sourceError,
                            gripPosition, gripBasis, rigPosition, rigBasis, _viewmodel.SkeletalLocomotionGait,
                            _viewmodel.SkeletalLocomotionSprint, _viewmodel.SkeletalLocomotionWeight, _viewmodel.SkeletalLocomotionPhase,
                            new[] { overlay.Origin.X, overlay.Origin.Y, overlay.Origin.Z }, new[] { euler.X, euler.Y, euler.Z }, _viewmodel.PrimaryPalmContactError);
                        samples.Add(sample); return sample;
                    }
                    void StepDelta(in ViewmodelFrame input, double seconds, string state, bool sourceFidelity = true)
                    {
                        _viewmodel.Apply(input);
                        Transform3D before = _viewmodel.SkeletalLocomotionTransform;
                        _viewmodel._Process(seconds); frameNumber++;
                        Transform3D after = _viewmodel.SkeletalLocomotionTransform;
                        float position = before.Origin.DistanceTo(after.Origin);
                        float degrees = Mathf.RadToDeg(before.Basis.GetRotationQuaternion().AngleTo(after.Basis.GetRotationQuaternion()));
                        maximumStepPosition = Math.Max(maximumStepPosition, position);
                        maximumStepDegrees = Math.Max(maximumStepDegrees, degrees);
                        Require(position < 0.035f && degrees < 4f, item.Id + "/" + state + ": locomotion transition snapped.");
                        Measure(state, sourceFidelity);
                    }
                    void Step(in ViewmodelFrame input, int frames, string state, bool sourceFidelity = true)
                    {
                        for (int frame = 0; frame < frames; frame++) StepDelta(input, frameSeconds, state, sourceFidelity);
                    }
                    void StepSeconds(in ViewmodelFrame input, double seconds, string state)
                    {
                        Require(double.IsFinite(seconds) && seconds >= 0, item.Id + "/" + state + ": invalid diagnostic duration.");
                        // Keep every integration step at or below 60 Hz, with a final partial step at the real contact tick.
                        for (double elapsed = 0; elapsed < seconds - 0.00000001;)
                        {
                            double delta = Math.Min(frameSeconds, seconds - elapsed);
                            StepDelta(input, delta, state); elapsed += delta;
                        }
                    }
                    async Task Capture(string label, bool sourceFidelity = true)
                    {
                        LocomotionCheckSample sample = Measure(label, sourceFidelity);
                        _captureCaption.Text = $"{body} · {item.Name} · {label}\n" +
                            $"gait {sample.Gait:0.00} · sprint {sample.Sprint:0.00} · layer {sample.ActionWeight:0.00} · grip Δ {sample.GripPositionErrorMetres * 1000:0.00} mm";
                        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        using Image image = _preview.GetTexture().GetImage();
                        image.Convert(Image.Format.Rgba8); image.Resize(tileWidth, tileHeight, Image.Interpolation.Lanczos);
                        sheet.BlitRect(image, new Rect2I(0, 0, tileWidth, tileHeight), new Vector2I(tile % columns * tileWidth, tile / columns * tileHeight));
                        tile++;
                    }
                    void ExactRest(string context)
                    {
                        Require(_viewmodel.SkeletalLocomotionTransform == Transform3D.Identity, item.Id + "/" + context + ": stationary overlay must be exactly identity.");
                        Require(_viewmodel.PresentedGrip is { } grip && AdmireNear(restPresentedGrip, grip, 0.000002f),
                            item.Id + "/" + context + ": accepted stationary hold changed.");
                    }

                    Step(rest, 1, "stationary"); ExactRest("stationary"); await Capture("stationary baseline");
                    Step(walk, 3, "walk-start"); await Capture("walk start · 50 ms");
                    Step(walk, 42, "walk"); await Capture("walk");
                    var strafe = walk; strafe.LocalVelocity = new Vector3(2.5f, 0, 0);
                    Step(strafe, 18, "strafe"); await Capture("strafe");
                    Step(run, 5, "run-start"); await Capture("run start · 83 ms");
                    Step(run, 42, "run"); await Capture("run");
                    var turn = run; turn.LookRate = new Vector2(2, -1);
                    Step(turn, 12, "look-lag"); await Capture("running + look lag");
                    var air = run; air.Grounded = false;
                    Step(air, 18, "airborne"); await Capture("airborne fade");
                    Step(rest, 30, "stop"); await Capture("stop · 500 ms");
                    Step(rest, 120, "settled"); ExactRest("settled"); await Capture("settled exact rest");
                    Step(run, 36, "before-swing");
                    _viewmodel.PlayAction("swing", swingSeconds);
                    Step(run, 4, "swing-fade");
                    Require(_viewmodel.SkeletalLocomotionWeight == 0, "Swing must suppress the layer by 67 ms.");
                    await Capture("swing · layer suppressed");
                    _viewmodel.CancelActions(); Step(run, 1, "cancel-start"); await Capture("cancel · recovery start");
                    Step(run, 20, "cancel-recovered");
                    Require(_viewmodel.SkeletalLocomotionWeight == 1, "Cancelled action must recover the locomotion layer.");
                    await Capture("cancel · recovered run");
                    _viewmodel.PlayAction("admire", 1f); Step(run, 5, "admire-fade", false);
                    Require(_viewmodel.SkeletalLocomotionWeight == 0, "Admire must suppress locomotion.");
                    await Capture("admire · layer suppressed", false);
                    _viewmodel.SetPreview("idle", 0); _viewmodel._Process(0); ExactRest("preview-after-run");
                    await Capture("preview · exact baseline");
                    // A newly equipped source item cannot inherit the previous item's running offset.
                    _viewmodel.Hold(HeldKind.Unarmed); _viewmodel.Hold(item.Kind, item.Id);
                    _viewmodel.Apply(run); _viewmodel._Process(1d / 60d); frameNumber++;
                    Require(_viewmodel.SkeletalLocomotionWeight < 1, "Equip must suppress the locomotion layer.");
                    await Capture("equip · entering carry");
                    Require(tile == columns * rows, "Locomotion sheet capture count changed.");
                    string file = body.ToString().ToLowerInvariant() + "-" + item.Id + "-locomotion.png";
                    Require(sheet.SavePng(Path.Combine(output, file)) == Error.Ok, "Could not save locomotion sheet.");
                    screenshots.Add(file);

                    // Continue the equip pictured in the last tile through suppression and recovery. This checks
                    // the carry layer only: the source route's static equip pose still needs its own artistic review.
                    double equippedFor = frameSeconds;
                    int suppressedEquipSamples = 0;
                    while (equippedFor < equipSeconds + locomotion.ActionFadeInSeconds + 2 * frameSeconds)
                    {
                        Step(run, 1, "equip-complete"); equippedFor += frameSeconds;
                        if (equippedFor >= locomotion.ActionFadeOutSeconds + frameSeconds && equippedFor < equipSeconds - frameSeconds)
                        {
                            Require(_viewmodel.SkeletalLocomotionWeight == 0, item.Id + ": equip must keep locomotion suppressed before entry completes.");
                            suppressedEquipSamples++;
                        }
                    }
                    Require(suppressedEquipSamples > 0, item.Id + ": equip check did not sample its suppression interval.");
                    Require(_viewmodel.ActiveAction == "idle" && _viewmodel.SkeletalLocomotionWeight == 1,
                        item.Id + ": completed equip must recover the carry layer without cancelling or scrubbing.");
                    actionChecks.Add(new { body = body.ToString(), item = item.Id, state = "equip", equipSeconds,
                        suppressedEquipSamples, observedSeconds = equippedFor, recoveredWeight = _viewmodel.SkeletalLocomotionWeight });

                    // Crouching wins over a stale sprint flag, keeps the shorter travelled-distance cadence,
                    // settles to the exact stationary hold, and allows running to recover after standing up.
                    var crouch = walk; crouch.GroundSpeed = 1.25f; crouch.LocalVelocity = new Vector3(0, 0, -1.25f);
                    crouch.Crouching = true; crouch.Sprinting = true;
                    StepSeconds(crouch, settleSeconds, "crouch-enter");
                    double crouchStartPhase = _viewmodel.SkeletalLocomotionPhase;
                    Step(crouch, 60, "crouch-walk");
                    double expectedCrouchPhase = (crouchStartPhase + Math.PI * 1.25 / Footsteps.Stride(1.25, true)) % (2 * Math.PI);
                    double crouchPhaseError = Math.Abs(_viewmodel.SkeletalLocomotionPhase - expectedCrouchPhase);
                    crouchPhaseError = Math.Min(crouchPhaseError, 2 * Math.PI - crouchPhaseError);
                    Require(crouchPhaseError < 0.00002, item.Id + ": crouching did not use its shorter ground stride.");
                    Require(_viewmodel.SkeletalLocomotionSprint == 0 && _viewmodel.SkeletalLocomotionGait > 0 &&
                        _viewmodel.SkeletalLocomotionTransform != Transform3D.Identity,
                        item.Id + ": crouch walk must animate without sprint carry, even with Sprinting set.");
                    var crouchRest = rest; crouchRest.Crouching = true;
                    StepSeconds(crouchRest, settleSeconds, "crouch-stop"); ExactRest("crouch-stop");
                    StepSeconds(run, settleSeconds, "stand-and-run");
                    Require(_viewmodel.SkeletalLocomotionSprint > 0.99f, item.Id + ": sprint did not recover after crouching.");
                    crouchChecks.Add(new { body = body.ToString(), item = item.Id, groundSpeed = 1.25,
                        staleSprintFlag = true, crouchPhaseError, stationaryIdentity = true,
                        recoveredSprint = _viewmodel.SkeletalLocomotionSprint });

                    // Complete three consecutive actions using content tick durations. Each cycle samples the
                    // real strike phase, the last recovery pose and natural completion; the next starts before
                    // the carry fade-in finishes. No hit, cancellation or preview seek is used to finish a cycle.
                    Require(contactSeconds > locomotion.ActionFadeOutSeconds && contactSeconds < swingSeconds - frameSeconds,
                        item.Id + ": swing needs distinct anticipation, contact and recovery intervals.");
                    HeldSkeletalClip swingSource = swing.Skeletal ?? throw new InvalidOperationException(item.Id + ": swing must remain source-bound.");
                    double expectedContactPhase = swingSource.Start + swing.Marker *
                        (swingSource.End - swingSource.Start + (swingSource.Wrap ? 1d : 0d));
                    if (swingSource.Wrap) expectedContactPhase %= 1d;
                    double expectedContactTime = expectedContactPhase * CharacterAssets.For(body).Library.GetAnimation(swingSource.Clip).Length;
                    for (int cycle = 1; cycle <= 3; cycle++)
                    {
                        string state = "full-swing-" + cycle;
                        float startingWeight = _viewmodel.SkeletalLocomotionWeight;
                        _viewmodel.PlayAction("swing", swingSeconds);
                        StepSeconds(run, contactSeconds, state + "-anticipation-to-contact");
                        Require(_viewmodel.ActiveAction == "swing" &&
                            Math.Abs(_viewmodel.ActivePhase - HeldActionTiming.StrikeFraction(melee)) < 0.00002f,
                            item.Id + ": complete swing did not reach its content strike phase.");
                        Require(Math.Abs(_viewmodel.SkeletalSourceTime - expectedContactTime) < 0.0001 &&
                            _viewmodel.SkeletalLocomotionWeight == 0,
                            item.Id + ": contact must sample the authored source marker with locomotion suppressed.");
                        Vector3 contact = _viewmodel.PresentedContact ?? throw new InvalidOperationException(item.Id + ": missing contact socket.");
                        Require(contact.IsFinite(), item.Id + ": contact socket is not finite.");
                        double sampledContactTime = _viewmodel.SkeletalSourceTime;
                        StepSeconds(run, swingSeconds - contactSeconds - frameSeconds / 2, state + "-recovery");
                        Require(_viewmodel.ActiveAction == "swing" && _viewmodel.SkeletalLocomotionWeight == 0,
                            item.Id + ": locomotion resumed before swing recovery completed.");
                        Step(run, 1, state + "-natural-completion");
                        Require(_viewmodel.ActiveAction == "idle" && _viewmodel.SkeletalLocomotionWeight < 1,
                            item.Id + ": completed swing must return naturally to idle and ease carry back in.");
                        actionChecks.Add(new { body = body.ToString(), item = item.Id, state, swingSeconds, contactSeconds,
                            startingWeight, expectedContactTime, sampledContactTime,
                            contactSocket = new[] { contact.X, contact.Y, contact.Z },
                            completedAction = _viewmodel.ActiveAction, completedWeight = _viewmodel.SkeletalLocomotionWeight });
                    }
                    StepSeconds(run, locomotion.ActionFadeInSeconds + frameSeconds, "full-swing-carry-recovered");
                    Require(_viewmodel.SkeletalLocomotionWeight == 1, item.Id + ": natural swing completion did not fully recover carry.");
                    StepSeconds(rest, settleSeconds, "full-swing-stationary-recovered"); ExactRest("full-swing-stationary-recovered");

                    Transform3D? cadenceReference = null;
                    double referencePhase = 0;
                    foreach (int fps in new[] { 30, 60, 120 })
                    {
                        _viewmodel.SetPreview("idle", 0); _viewmodel._Process(0); _viewmodel.ClearPreview(); _viewmodel.Apply(walk);
                        for (int frame = 0; frame < fps; frame++) _viewmodel._Process(1d / fps);
                        Transform3D result = _viewmodel.SkeletalLocomotionTransform;
                        double phase = _viewmodel.SkeletalLocomotionPhase;
                        if (cadenceReference is { } reference)
                        {
                            Require(result.Origin.DistanceTo(reference.Origin) < 0.00002f && LocomotionBasisError(result.Basis, reference.Basis) < 0.00002f,
                                item.Id + ": one-second carry differs across render frame rates.");
                            Require(Math.Abs(phase - referencePhase) < 0.00002, item.Id + ": travelled-distance phase differs across render frame rates.");
                        }
                        cadenceReference ??= result; referencePhase = phase;
                        cadenceChecks.Add(new { body = body.ToString(), item = item.Id, fps, seconds = 1, phase });
                    }
                    Transform3D? pitchReference = null;
                    double pitchPhase = 0;
                    float pitchGait = 0, pitchSprint = 0;
                    foreach (float pitch in new[] { 0f, 60f, -60f })
                    {
                        float radians = Mathf.DegToRad(pitch);
                        var pitchedRun = run;
                        pitchedRun.LocalVelocity = new Vector3(0, MathF.Sin(radians) * 5.5f, -MathF.Cos(radians) * 5.5f);
                        _viewmodel.SetPreview("idle", 0); _viewmodel._Process(0); _viewmodel.ClearPreview();
                        Step(pitchedRun, 60, "run-pitch-" + pitch);
                        Transform3D result = _viewmodel.SkeletalLocomotionTransform;
                        double phase = _viewmodel.SkeletalLocomotionPhase;
                        float gait = _viewmodel.SkeletalLocomotionGait, sprint = _viewmodel.SkeletalLocomotionSprint;
                        Require(sprint > 0.99f, item.Id + ": ground sprint was lost when the view pitched.");
                        if (pitchReference is { } reference)
                        {
                            Require(result.Origin.DistanceTo(reference.Origin) < 0.00002f && LocomotionBasisError(result.Basis, reference.Basis) < 0.00002f,
                                item.Id + ": identical ground movement changed its carry when looking up/down.");
                            Require(Math.Abs(phase - pitchPhase) < 0.00002 && MathF.Abs(gait - pitchGait) < 0.00002f && MathF.Abs(sprint - pitchSprint) < 0.00002f,
                                item.Id + ": camera pitch changed ground stride phase, gait or sprint weight.");
                        }
                        else { pitchReference = result; pitchPhase = phase; pitchGait = gait; pitchSprint = sprint; }
                        pitchChecks.Add(new { body = body.ToString(), item = item.Id, pitchDegrees = pitch, groundSpeed = 5.5f,
                            cameraVelocity = new[] { pitchedRun.LocalVelocity.X, pitchedRun.LocalVelocity.Y, pitchedRun.LocalVelocity.Z }, phase, gait, sprint });
                    }
                    _viewmodel.SetPreview("idle", 0); _viewmodel._Process(0); _viewmodel.ClearPreview();
                    var vertical = rest; vertical.LocalVelocity = new Vector3(0, 5.5f, 0); vertical.Sprinting = true;
                    Step(vertical, 60, "vertical-without-ground-travel");
                    ExactRest("vertical-without-ground-travel");
                    Require(_viewmodel.SkeletalLocomotionGait == 0 && _viewmodel.SkeletalLocomotionSprint == 0 && _viewmodel.SkeletalLocomotionPhase == 0,
                        item.Id + ": vertical velocity must not generate ground gait.");
                }
            }
            Require(sourceFingerprint == DiskFingerprint(), "Locomotion checks must not change held presentation data.");
            SaveReport();
            GD.Print($"[locomotion-check] PASS: {samples.Count} native samples, {screenshots.Count} sheets; {output}");
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            failures.Add(error.Message);
            if (outputReady) SaveReport();
            GD.PushError("[locomotion-check] FAIL: " + error); GetTree().Quit(1);
        }
    }

    private float LocomotionSourceError(Skeleton3D skeleton)
    {
        string source = _viewmodel.SkeletalMotionClip ?? throw new InvalidOperationException("Missing locomotion source clip.");
        Animation animation = CharacterAssets.For(_body).Library.GetAnimation(source);
        float maximum = 0;
        foreach (string boneName in SourceArmBones)
        {
            int bone = skeleton.FindBone(boneName), track = -1;
            Require(bone >= 0, "Missing source bone: " + boneName);
            for (int index = 0; index < animation.GetTrackCount(); index++)
                if (animation.TrackGetType(index) == Animation.TrackType.Rotation3D &&
                    animation.TrackGetPath(index).GetConcatenatedSubNames() == boneName) { track = index; break; }
            Require(track >= 0, "Missing source rotation track: " + boneName);
            Quaternion expected = animation.RotationTrackInterpolate(track, _viewmodel.SkeletalSourceTime).Normalized();
            Quaternion actual = skeleton.GetBonePoseRotation(bone).Normalized();
            maximum = Math.Max(maximum, Mathf.RadToDeg(2 * MathF.Acos(Math.Clamp(MathF.Abs(actual.Dot(expected)), 0, 1))));
        }
        return maximum;
    }

    private static float LocomotionBasisError(Basis left, Basis right) =>
        Math.Max(left.X.DistanceTo(right.X), Math.Max(left.Y.DistanceTo(right.Y), left.Z.DistanceTo(right.Z)));
}
