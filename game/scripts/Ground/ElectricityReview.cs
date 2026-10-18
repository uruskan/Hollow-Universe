using System;
using System.IO;
using Godot;
using Hollow.Simulation.Content;
using HollowUniverse.Common;
using HollowUniverse.Items;

namespace HollowUniverse.Ground;

/// <summary>Small native review scene using the production models, port definitions and cable renderer.</summary>
public partial class ElectricityReview : Node3D
{
    public string OutputDirectory { get; set; } = "";
    public override async void _Ready()
    {
        try
        {
            GameContent content = GameContent.Load(ProjectSettings.GlobalizePath("res://data/ground"));
            if (content.Problems.Count != 0) throw new InvalidOperationException(string.Join("\n", content.Problems));
            AddChild(new WorldEnvironment { Environment = new Godot.Environment {
                BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(.035f,.045f,.06f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(.7f,.78f,.9f), AmbientLightEnergy = .65f,
                ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled, TonemapMode = Godot.Environment.ToneMapper.Filmic } });
            AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-40,-30,0), LightEnergy = 1.6f, ShadowEnabled = true });
            AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(35,35) },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(.16f,.19f,.21f), Roughness = .9f } });
            Node3D Add(string key, Vector3 position)
            {
                Node3D node = ModelCatalog.Instantiate(key) ?? throw new InvalidOperationException("Missing model " + key);
                node.Position = position; AddChild(node);
                ElectricalView.AddPorts(node, content.Deployable(key)!.Power!);
                return node;
            }
            Node3D wind = Add("windmill", new Vector3(-4,0,0));
            Node3D turret = Add("auto-turret", new Vector3(0,0,0));
            Node3D sam = Add("sam-site", new Vector3(4,0,0));
            Vector3 Port(Node3D node, string key, ushort id) => node.GlobalPosition + GroundView.ToGodot(content.Deployable(key)!.Power!.Port(id)!.Position);
            AddChild(ElectricalView.CreateCableMesh(new[] { Port(wind,"windmill",1), new Vector3(-3,.08f,2), new Vector3(-1,.08f,2), Port(turret,"auto-turret",1) }));
            AddChild(ElectricalView.CreateCableMesh(new[] { Port(turret,"auto-turret",2), new Vector3(1,.08f,1), new Vector3(3,.08f,1), Port(sam,"sam-site",1) }));
            var camera = new Camera3D { Current = true, Fov = 52, Far = 100 };
            AddChild(camera);
            var layer = new CanvasLayer(); AddChild(layer);
            var label = Ui.Label("ELECTRICITY  ·  WIND → TURRET → SAM\nAmber: output / passthrough     Cyan: input     20 m between endpoint ports", TextStyle.Body);
            Ui.Place(label, Control.LayoutPreset.TopLeft, 24); layer.AddChild(label);
            Directory.CreateDirectory(OutputDirectory);
            camera.Position = new Vector3(12,9,18); camera.LookAt(new Vector3(-.8f,4,0));
            for (int i = 0; i < 16; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using (Image image = GetViewport().GetTexture().GetImage()) image.SavePng(Path.Combine(OutputDirectory,"electricity-overview.png"));
            camera.Position = new Vector3(6,3.2f,7); camera.LookAt(new Vector3(0,.6f,.5f));
            for (int i = 0; i < 8; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using (Image image = GetViewport().GetTexture().GetImage()) image.SavePng(Path.Combine(OutputDirectory,"electricity-ports.png"));
            GD.Print("ELECTRICITY_REVIEW_PASS: production port definitions, models and routed cable meshes rendered");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
