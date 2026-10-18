using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Hollow.Client.Feedback;
using HollowUniverse.Common;
using HollowUniverse.Fx;

namespace HollowUniverse.Ground;

/// <summary>Native contact presentation fixture: actual decal attachment and material responses, without gameplay/save state.</summary>
public partial class ContactFeedbackCheck : Node
{
    public string OutputPath { get; set; } = "";
    public string ScreenshotPath { get; set; } = "";
    private sealed record Check(string Name, bool Passed, string Detail);
    private readonly List<Check> _checks = new();
    private SubViewport _viewport = null!;
    private FxDirector _director = null!;

    public override async void _Ready()
    {
        try
        {
            CreateWorld();
            PresentationPoseCheck.Run(_viewport, Expect);
            CheckDecalAttachment();
            CheckDelayedDecalAttachment();
            CheckResponses();
            // Prime the same material/particle paths before the reviewed capture, then replay from a clean scene.
            PlayPanels();
            await ToSignal(GetTree().CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);
            _director.ClearAll();
            PlayPanels();
            await ToSignal(GetTree().CreateTimer(0.10), SceneTreeTimer.SignalName.Timeout);
            _director.Paused = true;
            Expect("three material responses rendered", _director.ActiveDecals >= 3 && _director.Batches["chunk_wood"].EmittedTotal > 0 &&
                _director.Batches["chunk_stone"].EmittedTotal > 0 && _director.Batches["spark"].EmittedTotal > 0,
                $"decals={_director.ActiveDecals}; wood, stone and spark batches emitted from the real response table");
            if (!string.IsNullOrWhiteSpace(ScreenshotPath))
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using Image image = _viewport.GetTexture().GetImage();
                string path = Absolute(ScreenshotPath);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                Expect("material response screenshot", image.SavePng(path) == Error.Ok, path);
            }
        }
        catch (Exception exception) { Expect("native exception", false, exception.ToString()); }
        finally
        {
            if (IsInstanceValid(_viewport)) _viewport.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        int failed = _checks.Count(check => !check.Passed);
        if (!string.IsNullOrWhiteSpace(OutputPath))
        {
            string path = Absolute(OutputPath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, JsonSerializer.Serialize(new { passed = failed == 0, checks = _checks },
                new JsonSerializerOptions { WriteIndented = true }));
        }
        GD.Print($"[contact-feedback-check] {_checks.Count - failed}/{_checks.Count} passed");
        GetTree().Quit(failed == 0 ? 0 : 1);
    }

    private void CreateWorld()
    {
        _viewport = new SubViewport { Size = new Vector2I(1500, 800), OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        AddChild(_viewport);
        _viewport.AddChild(new WorldEnvironment { Environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.045f, 0.055f, 0.07f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = Colors.White, AmbientLightEnergy = 0.4f,
        } });
        _viewport.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-35, -30, 0), LightEnergy = 1.3f });
        var camera = new Camera3D { Position = new Vector3(0, 3.6f, 10), Fov = 48, Current = true };
        _viewport.AddChild(camera);
        camera.LookAt(new Vector3(0, 1.8f, 0));
        _viewport.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(15, 0.2f, 8) }, Position = new Vector3(0, -0.15f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.09f, 0.105f, 0.12f), Roughness = 0.9f } });
        (string Name, Color Color, float Metallic)[] panels =
        {
            ("WOOD", new Color(0.30f, 0.16f, 0.07f), 0),
            ("STONE", new Color(0.35f, 0.37f, 0.39f), 0),
            ("METAL", new Color(0.18f, 0.24f, 0.28f), 0.7f),
        };
        for (int i = 0; i < panels.Length; i++)
        {
            var panel = panels[i];
            float x = (i - 1) * 3.6f;
            _viewport.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(3.15f, 3, 0.25f) }, Position = new Vector3(x, 1.5f, 0),
                MaterialOverride = new StandardMaterial3D { AlbedoColor = panel.Color, Metallic = panel.Metallic, Roughness = 0.65f } });
            _viewport.AddChild(new Label3D { Text = panel.Name, Position = new Vector3(x, 3.3f, 0.15f), FontSize = Ui.Px(38),
                PixelSize = 0.008f, Modulate = new Color(0.85f, 0.9f, 0.95f), NoDepthTest = true });
        }
        _director = new FxDirector { CameraOverride = camera, PreviewOnly = true };
        _viewport.AddChild(_director);
    }

    private void CheckDecalAttachment()
    {
        var holder = new Node3D { Name = "DecalAttachmentFixture" };
        _viewport.AddChild(holder);
        var decals = new FxDecals(holder) { PerTexture = 2 };
        const string texture = "decals/hole_wood_0";
        Transform3D? anchor = new Transform3D(Basis.Identity, new Vector3(4, 1, -2));
        int lookups = 0;
        Transform3D? Pose() { lookups++; return anchor; }
        Vector3 point = anchor.Value * new Vector3(0.3f, 0.4f, 0.1f);
        decals.Place(0, texture, point, Vector3.Back, 0.3f, 0.16f, 5, 1, 0, 0.2f, Colors.White, Pose);
        decals.Update(0);
        Decal first = holder.GetChildren().OfType<Decal>().Single(decal => decal.Visible);
        Transform3D local = anchor.Value.AffineInverse() * first.GlobalTransform;
        Expect("decal initial attachment", first.GlobalPosition.DistanceTo(point) < 0.0001f && decals.Active == 1, "real Godot decal placed at the confirmed contact");
        anchor = new Transform3D(new Basis(Vector3.Up, 0.7f), new Vector3(-2, 3, 1));
        decals.Update(0.2);
        Transform3D expected = anchor.Value * local;
        Expect("decal follows translated and rotated target", Near(expected, first.GlobalTransform),
            "current pose times captured local decal transform preserves contact position and orientation");
        anchor = null;
        decals.Update(0.3);
        Expect("missing target retires decal", !first.Visible && decals.Active == 0, "null registered target pose hides and releases the decal");
        int afterRemoval = lookups;
        decals.Update(0.4);
        Expect("retired decal releases callback", lookups == afterRemoval, "an inactive decal no longer resolves its former target");
        decals.Place(0.5, texture, point, Vector3.Back, 0.3f, 0.16f, 5, 1, 0, 0, Colors.White, Pose);
        Expect("removed target cannot create a new decal", !holder.GetChildren().OfType<Decal>().Any(decal => decal.Visible), "missing pose is checked at placement");
        anchor = Transform3D.Identity;
        for (int i = 0; i < 20; i++) decals.Place(1 + i * 0.01, texture, new Vector3(i, 1, -2), Vector3.Back,
            0.3f, 0.16f, 5, 1, 0, 0, Colors.White, Pose);
        decals.Update(1.3);
        Expect("decal pool remains bounded under reuse", holder.GetChildCount() == 2 && decals.Active == 2,
            $"children={holder.GetChildCount()}, active={decals.Active}; old marks reuse their fixed texture pool");
        decals.Clear();
        Expect("clear resets counter and visibility", decals.Active == 0 && !holder.GetChildren().OfType<Decal>().Any(decal => decal.Visible),
            "Clear immediately resets the active count and visible nodes");
        int afterClear = lookups;
        decals.Update(2);
        Expect("clear releases pose callbacks", lookups == afterClear, "cleared pooled entries no longer retain active lookups");
        // A pooled slot used next for a static mark must not inherit its previous moving anchor.
        decals.Place(2, texture, point, Vector3.Back, 0.3f, 0.16f, 0.5f, 0.1f, 0, 0, Colors.White);
        decals.Update(2.1);
        anchor = new Transform3D(Basis.Identity, Vector3.One * 100);
        decals.Update(2.2);
        Decal reused = holder.GetChildren().OfType<Decal>().Single(decal => decal.Visible);
        Expect("static reuse forgets old anchor", reused.GlobalPosition.DistanceTo(point) < 0.0001f && lookups == afterClear,
            "static reuse keeps its world contact despite changes to the old owner");
        decals.Update(3);
        Expect("decal lifetime remains enforced", decals.Active == 0 && !reused.Visible, "ordinary expiration still retires reused decals");
        holder.QueueFree();
    }

    private void CheckResponses()
    {
        foreach (string surface in new[] { "wood", "rock", "metal" })
        {
            ContactResponse response = ContactFeedback.Data.Resolve(surface, "bullet");
            Expect($"{surface} response resolves", FxCatalog.Find(response.Effect) != null && response.Sound.Length > 0,
                $"effect={response.Effect}, sound={response.Sound}");
        }
    }

    private void CheckDelayedDecalAttachment()
    {
        // Drive the actual catalogue's delayed layer deterministically, without waiting on renderer timing.
        _director.SetProcess(false);
        try
        {
            _director.ClearAll();
            Transform3D reference = new(new Basis(Vector3.Up, 0.3f), new Vector3(-1, 1, -1));
            Transform3D? anchor = reference;
            Vector3 localPoint = new(0.4f, 0.2f, 0.1f), localNormal = Vector3.Back;
            Vector3 point = reference * localPoint, normal = reference.Basis * localNormal;
            int lookups = 0;
            Transform3D? Pose() { lookups++; return anchor; }
            void Start() => _director.PlayNow("blood.drip", point, normal, new FxParams { DecalPose = Pose, Seed = 91 });
            Decal[] Visible() => _director.GetChildren().OfType<Decal>().Where(decal => decal.Visible).ToArray();

            Start();
            _director._Process(0.2);
            Expect("delayed decal waits for its layer", Visible().Length == 0 && _director.ActiveDecals == 0,
                "blood.drip has emitted particles but its catalogue decal waits until 0.42 seconds");
            anchor = new Transform3D(new Basis(Vector3.Right, 0.6f) * new Basis(Vector3.Up, -0.8f), new Vector3(2, 2.5f, 1));
            _director._Process(0.23);
            Decal delayed = Visible().Single();
            Expect("delayed decal preserves moving contact", delayed.GlobalPosition.DistanceTo(anchor.Value * localPoint) < 0.0001f &&
                delayed.GlobalBasis.Y.DistanceTo(anchor.Value.Basis * localNormal) < 0.0001f,
                "translation and rotation before emission preserve the original local contact point and normal");
            Transform3D localDecal = anchor.Value.AffineInverse() * delayed.GlobalTransform;
            anchor = new Transform3D(new Basis(Vector3.Up, 1.2f), new Vector3(-2, 1.5f, 0));
            _director._Process(0.05);
            Expect("delayed decal continues following", Near(anchor.Value * localDecal, delayed.GlobalTransform),
                "the emitted mark retains its full local transform, including its roll");
            anchor = null;
            _director._Process(0.05);
            Expect("delayed decal retires with owner", Visible().Length == 0 && _director.ActiveDecals == 0,
                "an absent owner retires a delayed mark through the real director");

            anchor = reference;
            Start();
            _director._Process(0.1);
            _director.ClearAll();
            int beforePendingUpdate = lookups;
            anchor = new Transform3D(Basis.Identity, new Vector3(7, 2, 0));
            _director._Process(0.5);
            Expect("clear releases pending decal callback", lookups == beforePendingUpdate && Visible().Length == 0 && _director.ActiveDecals == 0,
                "a cleared delayed layer neither resolves a reused owner nor creates a mark");

            anchor = reference;
            Start();
            _director._Process(0.43);
            Expect("delayed decal can reuse cleared pool", Visible().Length == 1 && _director.ActiveDecals == 1,
                "a new owned effect can use the pooled decals after retirement");
            _director.ClearAll();
            int beforeActiveUpdate = lookups;
            anchor = new Transform3D(Basis.Identity, new Vector3(-7, 2, 0));
            _director._Process(0.5);
            Expect("clear releases active decal callback", lookups == beforeActiveUpdate && Visible().Length == 0 && _director.ActiveDecals == 0,
                "ClearAll releases active attachment queries before a new world can reuse target IDs");

            anchor = reference;
            Start();
            anchor = null;
            _director._Process(0.43);
            Expect("owner removed before delayed emission", Visible().Length == 0 && _director.ActiveDecals == 0,
                "a pending mark cannot appear after its owner disappears");
        }
        finally
        {
            _director.ClearAll();
            _director.SetProcess(true);
        }
    }

    private void PlayPanels()
    {
        string[] surfaces = { "wood", "rock", "metal" };
        for (int i = 0; i < surfaces.Length; i++)
        {
            ContactResponse response = ContactFeedback.Data.Resolve(surfaces[i], "bullet");
            float x = (i - 1) * 3.6f;
            _director.PlayNow(response.Effect, new Vector3(x, 1.65f, 0.132f), Vector3.Back,
                new FxParams { Direction = Vector3.Forward, Seed = (ulong)(73 + i), Scale = 1.6f, Floor = new Vector3(x, 0, 0) });
        }
    }

    private static bool Near(Transform3D expected, Transform3D actual) =>
        expected.Origin.DistanceTo(actual.Origin) < 0.0001f && expected.Basis.X.DistanceTo(actual.Basis.X) < 0.0001f &&
        expected.Basis.Y.DistanceTo(actual.Basis.Y) < 0.0001f && expected.Basis.Z.DistanceTo(actual.Basis.Z) < 0.0001f;

    private void Expect(string name, bool passed, string detail)
    {
        _checks.Add(new Check(name, passed, detail));
        GD.Print($"[contact-feedback-check] {(passed ? "PASS" : "FAIL")} {name}: {detail}");
    }

    private static string Absolute(string path) => System.IO.Path.GetFullPath(path.StartsWith("res://", StringComparison.Ordinal)
        || path.StartsWith("user://", StringComparison.Ordinal) ? ProjectSettings.GlobalizePath(path) : path);
}
