using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Godot;
using HollowUniverse.Common;

namespace HollowUniverse.Items;

/// <summary>
/// The icon tool (<c>--view icons</c>, GROUND.md §9). With <c>--out &lt;dir&gt;</c> it renders every catalogue model
/// with an icon to transparent PNGs, <c>&lt;dir&gt;/128/&lt;key&gt;.png</c> and <c>&lt;dir&gt;/256/&lt;key&gt;.png</c>: a
/// three-quarter product shot under a soft key light, a fill and a rim, framed to fill the square evenly, rendered at
/// four times the size and filtered down. <c>--only a,b</c> renders some keys. Then (or without <c>--out</c>) it shows
/// the contact sheet of every icon, for <c>--shot</c>.
/// </summary>
public partial class IconRenderer : Node
{
    public Dictionary<string, string> Options { get; set; } = new();

    public bool ReadyForShot { get; private set; }

    private const int Supersample = 4;
    private const float Fill = 0.86f;

    private SubViewport _viewport = null!;
    private Camera3D _camera = null!;
    private Node3D _rig = null!;
    private Node3D _stage = null!;

    public override void _Ready()
    {
        if (Options.TryGetValue("out", out string? dir))
        {
            _ = RenderAll(dir);
        }
        else
        {
            ShowSheet();
        }
    }

    // ---------------------------------------------------------------- rendering

    private async Task RenderAll(string dir)
    {
        int largest = ModelCatalog.IconSizes[^1];
        BuildStudio(largest * Supersample);
        var only = new HashSet<string>(StringComparer.Ordinal);
        if (Options.TryGetValue("only", out string? list))
        {
            only.UnionWith(list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        // res:// paths, absolute paths, or paths relative to the repository (the project folder's parent).
        string repository = System.IO.Path.GetDirectoryName(ProjectSettings.GlobalizePath("res://").TrimEnd('/', '\\'))!;
        string root = dir.StartsWith("res://", StringComparison.Ordinal) ? ProjectSettings.GlobalizePath(dir)
            : System.IO.Path.IsPathRooted(dir) ? dir : System.IO.Path.GetFullPath(System.IO.Path.Combine(repository, dir));
        int count = 0;
        foreach (ModelEntry entry in ModelCatalog.Entries)
        {
            if (entry.IconKey != entry.Key || (only.Count > 0 && !only.Contains(entry.Key)))
            {
                continue;
            }
            Image? shot = await Render(entry);
            if (shot == null)
            {
                GD.PushWarning($"icons: {entry.Key}: no model");
                continue;
            }
            foreach (int size in ModelCatalog.IconSizes)
            {
                Image icon = Downsample(shot, size);
                string path = System.IO.Path.Combine(root, size.ToString(CultureInfo.InvariantCulture), entry.Key + ".png");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                icon.SavePng(path);
            }
            count++;
        }
        GD.Print($"icons: rendered {count} icons to {root}");
        _viewport.QueueFree();
        ShowSheet();
    }

    /// <summary>A lit studio in its own world: transparent background, a camera and three lights that turn with it.</summary>
    private void BuildStudio(int pixels)
    {
        var sky = new Sky { SkyMaterial = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.62f, 0.66f, 0.72f),
            SkyHorizonColor = new Color(0.78f, 0.78f, 0.76f),
            GroundBottomColor = new Color(0.22f, 0.21f, 0.2f),
            GroundHorizonColor = new Color(0.55f, 0.54f, 0.52f),
            SunAngleMax = 30,
        } };
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.ClearColor,
            Sky = sky,
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = 0.55f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            TonemapExposure = 1.05f,
            TonemapWhite = 6f,
        };
        _viewport = new SubViewport
        {
            Name = "IconStudio",
            Size = new Vector2I(pixels, pixels),
            TransparentBg = true,
            OwnWorld3D = true,
            World3D = new World3D { Environment = environment },
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            Msaa3D = Viewport.Msaa.Disabled,
            ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled,
            PositionalShadowAtlasSize = 1024,
        };
        AddChild(_viewport);
        _stage = new Node3D { Name = "Stage" };
        _viewport.AddChild(_stage);
        _rig = new Node3D { Name = "Rig" };
        _viewport.AddChild(_rig);
        _camera = new Camera3D { Name = "Camera", Projection = Camera3D.ProjectionType.Orthogonal, Current = true };
        _rig.AddChild(_camera);
        // Directions in the camera's frame: x right, y up, z toward the viewer. Key from the upper left in front,
        // fill from the right, rim from behind on the upper right.
        AddLight("Key", new Vector3(-0.9f, 1.25f, 1.0f), 2.1f, true, new Color(1f, 0.97f, 0.92f));
        AddLight("Fill", new Vector3(1.2f, 0.15f, 0.7f), 0.55f, false, new Color(0.9f, 0.94f, 1f));
        AddLight("Rim", new Vector3(0.7f, 0.9f, -1.3f), 1.7f, false, new Color(1f, 1f, 1f));
    }

    private void AddLight(string name, Vector3 from, float energy, bool shadow, Color colour)
    {
        var light = new DirectionalLight3D
        {
            Name = name,
            LightEnergy = energy,
            LightColor = colour,
            ShadowEnabled = shadow,
            LightAngularDistance = shadow ? 4f : 0f,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Orthogonal,
            ShadowBlur = 1.5f,
        };
        _rig.AddChild(light);
        light.LookAtFromPosition(from.Normalized(), Vector3.Zero, Vector3.Up);
    }

    private async Task<Image?> Render(ModelEntry entry)
    {
        Node3D? model = ModelCatalog.Instantiate(entry.Key);
        if (model == null)
        {
            return null;
        }
        ModelCatalog.ForceLod(model, 0);
        _stage.AddChild(model);

        Vector3 view = entry.IconView ?? DefaultView(entry.Class);
        var basis = Basis.FromEuler(new Vector3(-view.Y, view.X, view.Z) * (MathF.PI / 180f));
        List<Vector3> points = ModelCatalog.Points(model);
        if (points.Count == 0)
        {
            model.QueueFree();
            return null;
        }
        // Frame: the model's extent across the camera's right and up axes fills the square evenly.
        Vector3 right = basis.X, up = basis.Y, back = basis.Z;
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue, maxZ = float.MinValue, minZ = float.MaxValue;
        foreach (Vector3 p in points)
        {
            float x = p.Dot(right), y = p.Dot(up), z = p.Dot(back);
            minX = MathF.Min(minX, x);
            maxX = MathF.Max(maxX, x);
            minY = MathF.Min(minY, y);
            maxY = MathF.Max(maxY, y);
            minZ = MathF.Min(minZ, z);
            maxZ = MathF.Max(maxZ, z);
        }
        float extent = MathF.Max(maxX - minX, maxY - minY);
        float depth = maxZ - minZ;
        Vector3 centre = right * (minX + maxX) * 0.5f + up * (minY + maxY) * 0.5f;
        float distance = maxZ + MathF.Max(0.05f, depth) + 0.05f;
        _rig.Transform = new Transform3D(basis, Vector3.Zero);
        _camera.Transform = new Transform3D(Basis.Identity, new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, distance));
        _camera.Size = extent / Fill;
        _camera.Near = 0.01f;
        _camera.Far = distance - minZ + 0.1f;
        foreach (Node child in _rig.GetChildren())
        {
            if (child is DirectionalLight3D light)
            {
                light.DirectionalShadowMaxDistance = _camera.Far + depth;
            }
        }
        _ = centre;

        for (int i = 0; i < 4; i++)
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }
        Image image = _viewport.GetTexture().GetImage();
        image.Convert(Image.Format.Rgba8);
        _stage.RemoveChild(model);
        model.QueueFree();
        return image;
    }

    /// <summary>The camera's orbit (yaw, pitch, roll in degrees) for each class when the catalogue gives none.</summary>
    public static Vector3 DefaultView(string modelClass) => modelClass switch
    {
        "gun" => new Vector3(90, 4, 0),
        "tool" => new Vector3(70, 14, 42),
        "spear" or "blade" or "bow" or "arrow" => new Vector3(90, 10, -42),
        "clothing" => new Vector3(18, 12, 0),
        "door" => new Vector3(28, 12, 0),
        _ => new Vector3(35, 26, 0),
    };

    /// <summary>Area filter with coverage: colour averaged over covered samples only, so edges get no dark fringe.</summary>
    private static Image Downsample(Image source, int size)
    {
        int w = source.GetWidth();
        int f = Math.Max(1, w / size);
        byte[] src = source.GetData();
        var dst = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double r = 0, g = 0, b = 0, a = 0;
                for (int sy = 0; sy < f; sy++)
                {
                    int row = ((y * f + sy) * w + x * f) * 4;
                    for (int sx = 0; sx < f; sx++)
                    {
                        int i = row + sx * 4;
                        double alpha = src[i + 3] / 255.0;
                        r += src[i] * alpha;
                        g += src[i + 1] * alpha;
                        b += src[i + 2] * alpha;
                        a += alpha;
                    }
                }
                int o = (y * size + x) * 4;
                if (a > 0)
                {
                    dst[o] = (byte)Math.Clamp(Math.Round(r / a), 0, 255);
                    dst[o + 1] = (byte)Math.Clamp(Math.Round(g / a), 0, 255);
                    dst[o + 2] = (byte)Math.Clamp(Math.Round(b / a), 0, 255);
                    dst[o + 3] = (byte)Math.Clamp(Math.Round(a / (f * f) * 255), 0, 255);
                }
            }
        }
        Image image = Image.CreateFromData(size, size, false, Image.Format.Rgba8, dst);
        image.FixAlphaEdges();
        return image;
    }

    // ---------------------------------------------------------------- contact sheet

    private void ShowSheet()
    {
        var layer = new CanvasLayer { Name = "Sheet" };
        AddChild(layer);
        var sheet = new IconSheet { Name = "IconSheet" };
        sheet.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(sheet);
        ReadyAfterFrames();
    }

    private async void ReadyAfterFrames()
    {
        for (int i = 0; i < 8; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        ReadyForShot = true;
    }
}

/// <summary>Every rendered icon in a grid with its key, on the ground interface's slot colour.</summary>
public partial class IconSheet : Control
{
    private readonly List<(string Key, Texture2D? Icon)> _icons = new();

    public override void _Ready()
    {
        foreach (ModelEntry entry in ModelCatalog.Entries)
        {
            if (entry.IconKey == entry.Key)
            {
                string path = ModelCatalog.IconPath(entry.Key, ModelCatalog.IconSizes[^1]);
                // Fresh files are read from disk: the import of a just-rendered PNG may not have run yet.
                Image? image = Image.LoadFromFile(ProjectSettings.GlobalizePath(path));
                _icons.Add((entry.Key, image == null ? null : ImageTexture.CreateFromImage(image)));
            }
        }
        Ui.ScaleChanged += QueueRedraw;
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.105f, 0.11f, 0.105f));
        int columns = 13;
        int rows = Math.Max(1, (_icons.Count + columns - 1) / columns);
        float cellW = Size.X / columns;
        float cellH = Math.Min(Size.Y / rows, cellW + Ui.Px(22));
        float slot = Math.Min(cellW, cellH - Ui.Px(20)) - Ui.Px(8);
        Font font = Ui.Ground.Font(13);
        int fontSize = Ui.Px(13);
        for (int i = 0; i < _icons.Count; i++)
        {
            (string key, Texture2D? icon) = _icons[i];
            float x = (i % columns) * cellW + (cellW - slot) / 2;
            float y = (i / columns) * cellH + Ui.Px(6);
            var box = new Rect2(MathF.Round(x), MathF.Round(y), MathF.Round(slot), MathF.Round(slot));
            DrawRect(box, Ui.Ground.Panel);
            if (icon != null)
            {
                DrawTextureRect(icon, box.Grow(-Ui.Px(5)), false);
            }
            DrawString(font, new Vector2(MathF.Round(x), MathF.Round(y + slot + Ui.Px(15))), key, HorizontalAlignment.Center, slot, fontSize, Ui.Ground.Ink);
        }
    }
}
