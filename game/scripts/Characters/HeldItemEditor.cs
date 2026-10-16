using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Godot;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using HollowUniverse.Common;

namespace HollowUniverse.Characters;

/// <summary>
/// Visual authoring of the very same held-item profiles and renderers used on the ground.
/// Run --view held-editor. The 3D preview has its own viewport so panel widths never change the FPS framing.
/// All edits are live, undoable catalog replacements; only Save writes the validated catalog to disk.
/// </summary>
public partial class HeldItemEditor : Node3D
{
    private readonly Dictionary<string, string> _options;
    private readonly Dictionary<string, HeldPresentationProfile> _saved = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Stack<HeldPresentationProfile>> _undo = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Stack<HeldPresentationProfile>> _redo = new(StringComparer.Ordinal);
    private readonly HashSet<string> _dirty = new(StringComparer.Ordinal);
    private readonly List<Action> _refreshFields = new();
    private readonly List<HeldItemModel> _visibleItems = new();
    private readonly List<string> _actionNames = new();
    private HeldPresentationProfile _profile = null!;
    private HeldItemModel _held = null!;
    private string _action = "idle";
    private float _phase, _speed = 1, _targetDistance = 1.1f, _yaw = 160, _pitch = 8, _distance = 3.3f;
    private float _externalYaw = 105, _externalPitch = 8, _externalDistance = 1.0f;
    private int _view, _outfit, _hand;
    private BodyType _body;
    private bool _refreshing, _playing, _aiming, _orbiting, _showTarget = true, _contactPreview;
    private bool _automating, _capturing, _oldAutoAcceptQuit;
    private double _elapsed, _lastEditAt = -10;
    private string _lastEditKey = "";
    private Control _ui = null!;
    private SubViewportContainer _previewContainer = null!;
    private SubViewport _preview = null!;
    private Node3D _stage = null!, _eyes = null!, _target = null!, _guides = null!;
    private Camera3D _camera = null!;
    private ViewmodelView _viewmodel = null!;
    private CharacterView _character = null!;
    private ItemList _items = null!;
    private OptionButton _actions = null!, _keys = null!, _views = null!;
    private HSlider _timeline = null!;
    private HeldItemEditorTimeline _ticks = null!;
    private Label _status = null!, _selection = null!, _clock = null!, _previewLabel = null!;
    private Button _play = null!, _save = null!, _undoButton = null!, _redoButton = null!;
    private TabContainer _tabs = null!;
    private ConfirmationDialog _discard = null!;
    private AcceptDialog _error = null!;
    private Action? _afterDiscard;
    private string _statusText = "Select an item, adjust a grip or keyframe, then Save all.";
    private SpinBox? _testPositionX;
    private Label _captureCaption = null!;
    private GameContent _content = null!;
    private Label _timing = null!;
    private MeshInstance3D _reach = null!;
    private bool _gameTiming;
    private float _playPhase;
    private string _diskFingerprint = "";
    private LineEdit? _contactSocketField;

    public HeldItemEditor(Dictionary<string, string> options)
    {
        _options = options;
        Name = "HeldItemEditor";
        ProcessPriority = -20;
    }

    public bool ReadyForShot => _elapsed > 0.6 && _held != null;

    public override void _Ready()
    {
        try { Initialize(); }
        catch (Exception error)
        {
            GD.PushError("[held-editor-startup] " + error);
            SetProcess(false); SetProcessInput(false); SetProcessUnhandledKeyInput(false);
            GetTree().Root.CloseRequested -= RequestClose;
            GetTree().AutoAcceptQuit = true;
            if (_options.ContainsKey("editor-check") || _options.ContainsKey("editor-sheet") || _options.ContainsKey("editor-animation-dir") || _options.ContainsKey("shot"))
            {
                GetTree().Quit(1);
                return;
            }
            var failure = new AcceptDialog
            {
                Title = "Held item workshop could not open",
                DialogText = "The held-item catalog could not be loaded. No files were changed.\n\n" + error.Message + "\n\nCorrect the catalog or restore its .bak file, then reopen the workshop.",
                OkButtonText = "Close", Theme = Ui.Theme,
            };
            failure.Confirmed += () => GetTree().Quit(1);
            failure.Canceled += () => GetTree().Quit(1);
            AddChild(failure);
            failure.PopupCentered(new Vector2I(Ui.Px(760), Ui.Px(340)));
        }
    }

    private void Initialize()
    {
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _oldAutoAcceptQuit = GetTree().AutoAcceptQuit;
        GetTree().AutoAcceptQuit = false;
        GetTree().Root.CloseRequested += RequestClose;
        _body = Option("body", "male") == "female" ? BodyType.Female : BodyType.Male;
        _outfit = Math.Clamp((int)Number("outfit", 0), 0, Wardrobe.Outfits.Count - 1);
        _view = Option("camera", "fps") switch { "external" => 1, "tps" => 2, _ => 0 };
        _showGizmo = _options.ContainsKey("gizmo");
        _aiming = _options.ContainsKey("ads");
        _gameTiming = _options.ContainsKey("game-timing");
        _contactPreview = _options.ContainsKey("contact");
        _phase = Math.Clamp((float)Number("phase", 0), 0, 1);
        _targetDistance = Math.Clamp((float)Number("target-distance", 1.1), 0.2f, 3.0f);
        _content = GameContent.Load(ProjectSettings.GlobalizePath("res://data/ground"));
        foreach (HeldItemModel item in HeldItems.All)
        {
            _saved[item.Id] = HeldPresentations.Clone(HeldPresentations.Get(item.Id));
        }
        _diskFingerprint = DiskFingerprint();
        BuildInterface();
        BuildStage();
        BuildCharacters();
        FilterItems("");
        SelectItem(HeldItems.Find(Option("item", "metal-hatchet")) ?? HeldItems.Find("stone-hatchet") ?? HeldItems.All[0]);
        string wantedAction = Option("action", "idle");
        if (_profile.Actions.ContainsKey(wantedAction)) SelectAction(wantedAction);
        SetPhase((float)Number("phase", 0));
        SetView(_view);
        _tabs.CurrentTab = Option("editor-tab", "placement") switch { "hands" => 1, "motion" => 2, _ => 0 };
    }

    public override void _ExitTree()
    {
        if (GetTree() == null) return;
        GetTree().Root.CloseRequested -= RequestClose;
        GetTree().AutoAcceptQuit = _oldAutoAcceptQuit;
    }

    private void BuildInterface()
    {
        var canvas = new CanvasLayer();
        AddChild(canvas);
        _ui = new Control { Theme = Ui.Theme };
        _ui.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        canvas.AddChild(_ui);
        var background = new ColorRect { Color = new Color(0.022f, 0.031f, 0.045f), MouseFilter = Control.MouseFilterEnum.Ignore };
        background.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _ui.AddChild(background);
        var margin = new MarginContainer();
        margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        Ui.Track(margin, c =>
        {
            c.AddThemeConstantOverride("margin_left", Ui.Px(14));
            c.AddThemeConstantOverride("margin_right", Ui.Px(14));
            c.AddThemeConstantOverride("margin_top", Ui.Px(10));
            c.AddThemeConstantOverride("margin_bottom", Ui.Px(10));
        });
        _ui.AddChild(margin);
        var layout = VBox(9);
        margin.AddChild(layout);
        var header = HBox(10);
        layout.AddChild(header);
        var title = VBox(0);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        title.AddChild(Ui.Label("Held item workshop", TextStyle.Title));
        title.AddChild(Ui.Label("Pose hands · author motion · compare first and third person", TextStyle.Caption));
        header.AddChild(title);
        _undoButton = Button("Undo", Undo, "Ctrl+Z — undo this item's last edit");
        _redoButton = Button("Redo", Redo, "Ctrl+Y — redo this item's last edit");
        header.AddChild(_undoButton);
        header.AddChild(_redoButton);
        header.AddChild(Button("Reload", () => ConfirmDiscard("Reload the saved catalog and discard all unsaved edits?", Reload), "Reload all profiles from disk"));
        _save = Button("Save all", Save, "Ctrl+S — validate and atomically save all edited item profiles");
        _save.Modulate = new Color(0.65f, 0.87f, 1f);
        header.AddChild(_save);
        header.AddChild(Button("Close", RequestClose));

        var main = HBox(10);
        main.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        layout.AddChild(main);
        (PanelContainer libraryPanel, VBoxContainer library) = Ui.Card(7);
        Ui.Size(libraryPanel, 220);
        main.AddChild(libraryPanel);
        library.AddChild(Ui.Label("ITEM LIBRARY", TextStyle.Overline));
        var search = new LineEdit { PlaceholderText = "Search held items…", ClearButtonEnabled = true };
        search.TextChanged += FilterItems;
        library.AddChild(search);
        _items = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill, SelectMode = ItemList.SelectModeEnum.Single };
        _items.ItemSelected += index => { if (!_refreshing && index < _visibleItems.Count) SelectItem(_visibleItems[(int)index]); };
        library.AddChild(_items);
        library.AddChild(Ui.Separator());
        library.AddChild(Ui.Label("Preview character", TextStyle.Overline));
        var body = new OptionButton();
        body.AddItem("Male body"); body.AddItem("Female body"); body.Select((int)_body);
        body.ItemSelected += i => { _body = (BodyType)i; BuildCharacters(); RefreshPreview(); };
        library.AddChild(body);
        var outfit = new OptionButton();
        foreach (var look in Wardrobe.Outfits) outfit.AddItem(look.Name);
        outfit.Select(_outfit);
        outfit.ItemSelected += i => { _outfit = (int)i; Wear(); };
        library.AddChild(outfit);
        var skin = NumberField(0, 1000, 1, 0, seed =>
        {
            Look look = Looks.ForPlayer((ulong)seed, _body);
            _character.Look = look; _viewmodel.Look = look;
        });
        library.AddChild(Ui.Label("Appearance seed", TextStyle.Caption));
        library.AddChild(skin);

        var middle = VBox(7);
        middle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        main.AddChild(middle);
        var previewBar = HBox(8);
        middle.AddChild(previewBar);
        _views = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _views.AddItem("First person"); _views.AddItem("First-person arms · orbit"); _views.AddItem("Third person · orbit");
        _views.Select(_view);
        _views.ItemSelected += i => SetView((int)i);
        previewBar.AddChild(_views);
        var gizmo = new CheckButton { Text = "Move FPS grip", ButtonPressed = _showGizmo };
        gizmo.TooltipText = "Drag the red / green / blue tips to move the FPS grip. Use arms orbit to adjust depth. Shift drags slowly; Ctrl snaps to centimetres.";
        gizmo.Toggled += value => { _showGizmo = value; if (!value) _dragAxis = -1; UpdateGizmo(); };
        previewBar.AddChild(gizmo);
        previewBar.AddChild(Button("Reset camera", ResetCamera));
        _previewContainer = new SubViewportContainer
        {
            Stretch = true,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        Ui.Size(_previewContainer, 300, 230);
        middle.AddChild(_previewContainer);
        _preview = new SubViewport
        {
            Name = "PreviewViewport", OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            Msaa3D = Viewport.Msaa.Msaa2X,
        };
        _previewContainer.AddChild(_preview);
        var annotation = new CanvasLayer();
        _preview.AddChild(annotation);
        _captureCaption = Ui.Label("", TextStyle.Caption);
        _captureCaption.Theme = Ui.Theme;
        _captureCaption.Position = new Vector2(Ui.Px(12), Ui.Px(12));
        _captureCaption.Visible = false;
        annotation.AddChild(_captureCaption);
        _previewContainer.GuiInput += PreviewInput;
        _previewLabel = Ui.Label("", TextStyle.Caption);
        _previewLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        middle.AddChild(_previewLabel);
        var targetBar = HBox(6);
        middle.AddChild(targetBar);
        var targetToggle = new CheckButton { Text = "Target", ButtonPressed = true };
        targetToggle.Toggled += value => { _showTarget = value; RefreshTarget(); };
        targetBar.AddChild(targetToggle);
        var targetDistance = NumberField(0.2, 3, 0.01, _targetDistance, v => { _targetDistance = (float)v; RefreshTarget(); });
        targetDistance.TooltipText = "Distance from eyes in metres. The cyan dot is the intended contact point.";
        Ui.Size(targetDistance, 100);
        targetBar.AddChild(targetDistance);
        targetBar.AddChild(Ui.Label("m", TextStyle.Caption));
        var aim = new CheckButton { Text = "Aim / draw", ButtonPressed = _aiming };
        aim.Toggled += v => _aiming = v;
        targetBar.AddChild(aim);
        var contact = new CheckButton { Text = "Contact solve", ButtonPressed = _contactPreview };
        contact.TooltipText = "Preview the FPS correction toward the target. This does not simulate a server hit.";
        contact.Toggled += v => { _contactPreview = v; RefreshTarget(); };
        targetBar.AddChild(contact);

        (PanelContainer inspectorPanel, VBoxContainer inspector) = Ui.Card(6);
        Ui.Size(inspectorPanel, 380);
        main.AddChild(inspectorPanel);
        _selection = Ui.Label("", TextStyle.Heading);
        _selection.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        inspector.AddChild(_selection);
        _tabs = new TabContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        inspector.AddChild(_tabs);

        (PanelContainer timelinePanel, VBoxContainer timeline) = Ui.Card(4);
        layout.AddChild(timelinePanel);
        var controls = HBox(8);
        timeline.AddChild(controls);
        controls.AddChild(Ui.Label("ACTION", TextStyle.Overline));
        _actions = new OptionButton();
        Ui.Size(_actions, 150);
        _actions.ItemSelected += i => { if (!_refreshing && i < _actionNames.Count) SelectAction(_actionNames[(int)i]); };
        controls.AddChild(_actions);
        _play = Button("Play", () => SetPlaying(!_playing), "Space — play or pause the selected action");
        controls.AddChild(_play);
        controls.AddChild(Button("Start", () => SetPhase(0)));
        controls.AddChild(Button("Marker", () => SetPhase(Math.Max(0, Clip.Marker)), "Jump to the contact, release, or consumption marker"));
        controls.AddChild(Ui.Label("Speed", TextStyle.Caption));
        var speed = NumberField(0.05, 2, 0.05, 1, v => _speed = (float)v);
        Ui.Size(speed, 90); controls.AddChild(speed);
        _clock = Ui.Label("", TextStyle.Value);
        _clock.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _clock.HorizontalAlignment = HorizontalAlignment.Right;
        controls.AddChild(_clock);
        var timingMode = new CheckButton { Text = "Gameplay timing", ButtonPressed = _gameTiming };
        timingMode.TooltipText = "Play this art clip at the gameplay duration, aligning its contact pose to the real strike tick. The scrubber always shows authored phase.";
        timingMode.Toggled += value => { _gameTiming = value; _playPhase = GameplayPhase(_phase); };
        controls.AddChild(timingMode);
        _timing = Ui.Label("", TextStyle.Caption);
        _timing.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        timeline.AddChild(_timing);
        _ticks = new HeldItemEditorTimeline();
        Ui.Size(_ticks, 0, 26);
        timeline.AddChild(_ticks);
        _timeline = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.001, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _timeline.ValueChanged += value => { if (!_refreshing) { SetPlaying(false); SetPhase((float)value); } };
        timeline.AddChild(_timeline);
        var keybar = HBox(8);
        timeline.AddChild(keybar);
        _keys = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _keys.ItemSelected += i => { if (!_refreshing && i < Clip.Keys.Count) SetPhase(Clip.Keys[(int)i].At); };
        keybar.AddChild(_keys);
        keybar.AddChild(Button("Add / update key", UpsertKey, "Record the current sampled motion at the playhead; motion field edits do this automatically"));
        keybar.AddChild(Button("Delete key", DeleteKey, "Delete the key at the playhead; endpoint keys are retained"));
        _status = Ui.Label(_statusText, TextStyle.Caption);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        layout.AddChild(_status);
        _discard = new ConfirmationDialog { Title = "Unsaved held-item edits", OkButtonText = "Discard edits" };
        _discard.Confirmed += () => { Action? action = _afterDiscard; _afterDiscard = null; action?.Invoke(); };
        _ui.AddChild(_discard);
        _error = new AcceptDialog { Title = "Held item workshop" };
        _ui.AddChild(_error);
    }

    private void BuildStage()
    {
        _stage = new Node3D { Name = "WorkshopStage" };
        _preview.AddChild(_stage);
        _stage.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.075f, 0.105f, 0.14f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.72f, 0.80f, 0.90f), AmbientLightEnergy = 0.65f,
                TonemapMode = Godot.Environment.ToneMapper.Agx,
                SsaoEnabled = true, SsaoRadius = 0.3f, SsaoIntensity = 1f,
            },
        });
        _stage.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-35, -28, 0), LightEnergy = 2.0f, LightColor = new Color(1, 0.92f, 0.83f), ShadowEnabled = true });
        _stage.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-20, 140, 0), LightEnergy = 1.0f, LightColor = new Color(0.56f, 0.75f, 1f) });
        _stage.AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(14, 14) },
            MaterialOverride = Material(new Color(0.095f, 0.13f, 0.16f)),
        });
        var gridMaterial = Material(new Color(0.17f, 0.23f, 0.28f), true);
        for (int i = -7; i <= 7; i++)
        {
            _stage.AddChild(Box(new Vector3(0.006f, 0.005f, 14), new Vector3(i, 0.003f, 0), gridMaterial));
            _stage.AddChild(Box(new Vector3(14, 0.005f, 0.006f), new Vector3(0, 0.003f, i), gridMaterial));
        }
        _eyes = new Node3D { Name = "Eyes" };
        _stage.AddChild(_eyes);
        _camera = new Camera3D { Fov = 70, Near = 0.015f, Far = 50, CullMask = 1 | ViewmodelView.RenderLayer, Current = true };
        _stage.AddChild(_camera);
        _target = new Node3D { Name = "Target" };
        _target.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.19f, BottomRadius = 0.19f, Height = 2.5f },
            Position = new Vector3(0, 1.25f, -0.19f),
            MaterialOverride = Material(new Color(0.31f, 0.24f, 0.17f)),
        });
        _stage.AddChild(_target);
        _guides = new Node3D { Name = "ContactGuides" };
        var cyan = Material(new Color(0.25f, 0.85f, 1f), true);
        _guides.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.016f, Height = 0.032f }, MaterialOverride = cyan });
        _guides.AddChild(Box(new Vector3(0.14f, 0.004f, 0.004f), Vector3.Zero, cyan));
        _guides.AddChild(Box(new Vector3(0.004f, 0.14f, 0.004f), Vector3.Zero, cyan));
        _stage.AddChild(_guides);
        _reach = new MeshInstance3D { Name = "GameplayReach", RotationDegrees = new Vector3(90, 0, 0) };
        var rangeMaterial = Material(new Color(0.18f, 0.72f, 1f, 0.08f), true);
        rangeMaterial.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        _reach.MaterialOverride = rangeMaterial;
        _stage.AddChild(_reach);
        BuildGizmo();
    }

    private void BuildCharacters()
    {
        if (_character != null) { _stage.RemoveChild(_character); _character.QueueFree(); }
        if (_viewmodel != null) { _eyes.RemoveChild(_viewmodel); _viewmodel.QueueFree(); }
        _character = new CharacterView { Name = "GameplayThirdPerson", Body = _body, ForcedLod = 0, FootPlacement = false, BlinkOverride = 0 };
        _stage.AddChild(_character);
        _viewmodel = new ViewmodelView { Name = "GameplayFirstPerson", Body = _body };
        _eyes.AddChild(_viewmodel);
        _eyes.Position = new Vector3(0, CharacterAssets.For(_body).EyeHeight, 0);
        Wear();
        RefreshTarget();
    }

    private void Wear()
    {
        _character.Wear(Wardrobe.Outfits[_outfit].Items);
        _viewmodel.Wear(Wardrobe.Outfits[_outfit].Items);
    }

    private void FilterItems(string query)
    {
        _refreshing = true;
        _items.Clear(); _visibleItems.Clear();
        foreach (HeldItemModel item in HeldItems.All)
        {
            if (!item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) && !item.Id.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
            _visibleItems.Add(item);
            _items.AddItem((_dirty.Contains(item.Id) ? "• " : "") + item.Name);
            _items.SetItemTooltip(_visibleItems.Count - 1, item.Id);
            if (_held?.Id == item.Id) _items.Select(_visibleItems.Count - 1);
        }
        _refreshing = false;
    }

    private void SelectItem(HeldItemModel item)
    {
        CommitFocusedField();
        _held = item;
        _profile = HeldPresentations.Clone(HeldPresentations.Get(item.Id));
        _playing = false;
        _phase = 0;
        string action = _profile.Actions.ContainsKey(_action) ? _action : "idle";
        _refreshing = true;
        _actionNames.Clear(); _actions.Clear();
        foreach (string name in _profile.Actions.Keys) { _actionNames.Add(name); _actions.AddItem(name); }
        _refreshing = false;
        SelectAction(action);
        int index = _visibleItems.FindIndex(i => i.Id == item.Id);
        if (index >= 0) _items.Select(index);
        _statusText = _dirty.Contains(item.Id) ? "Unsaved edits retained for this item. Save all writes every edited item." : "Metres for positions; degrees for rotations. Changes preview immediately.";
        BuildInspector();
        RefreshPreview();
        RefreshTarget();
        RefreshStatus();
    }

    private HeldPresentationClip Clip => _profile.Actions[_action];

    private void SelectAction(string action)
    {
        CommitFocusedField();
        _action = _profile.Actions.ContainsKey(action) ? action : _profile.Actions.Keys.First();
        _actions.Select(_actionNames.IndexOf(_action));
        _phase = Math.Clamp(_phase, 0, 1);
        SetPlaying(false);
        RefreshKeys();
        RefreshFields();
        RefreshPreview();
        RefreshTiming();
    }

    private void RefreshPreview()
    {
        if (_held == null || _viewmodel == null) return;
        _viewmodel.Hold(_held.Kind, _held.Id);
        _viewmodel.RefreshPresentation();
        _character.RefreshPresentation();
        SamplePreview();
    }

    private void SamplePreview()
    {
        if (_held == null) return;
        CharacterFrame frame = CharacterFrame.Standing(Vector3.Zero, Vector3.Up);
        frame.Held = _held.Kind; frame.HeldItem = _held.Id; frame.Aiming = _aiming;
        _character.Apply(frame);
        _character.SetPreview(_action, _phase);
        _viewmodel.Apply(new ViewmodelFrame { Grounded = true, Aiming = _aiming });
        _viewmodel.SetPreview(_action, _phase);
        _character.Visible = _view == 2;
        _viewmodel.Visible = _view != 2;
        _clock.Text = $"{_phase * Clip.Duration:0.000} / {Clip.Duration:0.000} s  ·  {_phase * 100:0.0}%";
        float correction = _viewmodel.AuthoredReachCorrection;
        float wrist = _viewmodel.PrimaryWristBendDegrees, palm = _viewmodel.PrimaryPalmContactError;
        string help = _view == 0 ? "Live gameplay FPS renderer · cyan cross = target contact point" : "Right-drag to orbit · wheel to zoom · live gameplay renderer";
        if (_view != 2)
            help += $"\nWrist bend {wrist:0}° · palm / grip gap {palm * 1000:0.0} mm · reach correction {correction * 100:0.0} cm";
        if (_previewLabel.Text != help)
        {
            _previewLabel.Text = help;
            _previewLabel.AddThemeColorOverride("font_color", _view != 2 && (correction > 0.005f || wrist > 65 || palm > 0.025f) ? Ui.Warm : Ui.Tertiary);
        }
        _ticks.Phase = _phase;
        _ticks.QueueRedraw();
    }

    private void SetView(int view)
    {
        _dragAxis = -1; _orbiting = false;
        _view = view; _views.Select(view);
        _previewLabel.Text = view == 0
            ? "Live gameplay FPS renderer · cyan cross = target contact point"
            : "Right-drag to orbit · wheel to zoom · live gameplay renderer";
        UpdateCamera(); SamplePreview();
        RefreshTarget();
    }

    private void ResetCamera()
    {
        _yaw = 160; _pitch = 8; _distance = 3.3f;
        _externalYaw = 105; _externalPitch = 8; _externalDistance = 1.0f;
        UpdateCamera();
    }

    private void UpdateCamera()
    {
        if (_view == 0)
        {
            _camera.Transform = _eyes.Transform;
            _camera.Fov = _aiming ? 54 : 70;
        }
        else
        {
            float yaw = Mathf.DegToRad(_view == 1 ? _externalYaw : _yaw);
            float pitch = Mathf.DegToRad(_view == 1 ? _externalPitch : _pitch);
            Vector3 focus = _view == 1 ? _eyes.Position + new Vector3(0.05f, -0.18f, -0.35f) : new Vector3(0, 1.02f, -0.15f);
            float distance = _view == 1 ? _externalDistance : _distance;
            Vector3 offset = new Vector3(MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), MathF.Cos(yaw) * MathF.Cos(pitch)) * distance;
            _camera.LookAtFromPosition(focus + offset, focus);
            _camera.Fov = 50;
        }
    }

    private void RefreshTarget()
    {
        if (_target == null || _eyes == null) return;
        _target.Position = new Vector3(0, 0, -_targetDistance);
        _target.Visible = _showTarget;
        _guides.Position = _eyes.Position + new Vector3(0, 0, -_targetDistance + 0.005f);
        _guides.Visible = _showTarget;
        _viewmodel?.SetContactTarget(_showTarget && _contactPreview ? new Vector3(0, 0, -_targetDistance) : null, confirmed: false);
        if (_held != null && _content.Item(_held.Id)?.Melee is { } melee)
        {
            _reach.Mesh = new CylinderMesh { TopRadius = CombatSystem.SweepRadius, BottomRadius = CombatSystem.SweepRadius, Height = melee.Reach };
            _reach.Position = _eyes.Position + Vector3.Forward * (melee.Reach / 2);
            _reach.Visible = _showTarget && _view != 0;
        }
        else if (_reach != null) _reach.Visible = false;
    }

    private void RefreshTiming()
    {
        ItemDef? item = _content.Item(_held.Id);
        float duration = HeldActionTiming.Duration(item, _action, Clip.Duration);
        string detail = _action == "swing" && item?.Melee is { } melee
            ? $" · hit {HeldActionTiming.Seconds(melee.StrikeTicks):0.000} s · reach {melee.Reach:0.00} m / radius {CombatSystem.SweepRadius:0.00} m"
            : HeldActionTiming.AnticipationSeconds(_action) > 0 ? $" · event {HeldActionTiming.AnticipationSeconds(_action):0.000} s" : "";
        _timing.Text = $"ART CLIP PHASE  ·  Gameplay duration {duration:0.000} s{detail}  ·  target is a presentation preview";
    }

    private float GameplayPhase(float artPhase)
    {
        float strike = GameplayMarker();
        if (strike < 0 || Clip.Marker <= 0 || Clip.Marker >= 1) return artPhase;
        return artPhase <= Clip.Marker ? artPhase / Clip.Marker * strike : strike + (artPhase - Clip.Marker) / (1 - Clip.Marker) * (1 - strike);
    }

    private float GameplayMarker()
    {
        if (_action == "swing" && _content.Item(_held.Id)?.Melee is { } melee) return HeldActionTiming.StrikeFraction(melee);
        return HeldActionTiming.AnticipationSeconds(_action) > 0
            ? HeldActionTiming.CommitFraction(_action, HeldActionTiming.Duration(_content.Item(_held.Id), _action, Clip.Duration)) : -1;
    }

    private void PreviewInput(InputEvent input)
    {
        if (GizmoInput(input)) return;
        if (input is InputEventMouseButton button)
        {
            if (button.ButtonIndex == MouseButton.Right) _orbiting = button.Pressed;
            if (button.Pressed && _view != 0 && button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                float factor = button.ButtonIndex == MouseButton.WheelUp ? 0.9f : 1.1f;
                if (_view == 1) _externalDistance = Math.Clamp(_externalDistance * factor, 0.22f, 4);
                else _distance = Math.Clamp(_distance * factor, 0.5f, 8);
                UpdateCamera();
            }
        }
        if (input is InputEventMouseMotion motion && _orbiting && _view != 0)
        {
            if (_view == 1) { _externalYaw -= motion.Relative.X * 0.35f; _externalPitch = Math.Clamp(_externalPitch + motion.Relative.Y * 0.3f, -75, 75); }
            else { _yaw -= motion.Relative.X * 0.35f; _pitch = Math.Clamp(_pitch + motion.Relative.Y * 0.3f, -75, 75); }
            UpdateCamera();
        }
    }

    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (input is not InputEventKey { Pressed: true, Echo: false } key) return;
        bool handled = true;
        if (key.CtrlPressed && key.Keycode == Key.S) Save();
        else if (key.CtrlPressed && key.Keycode == Key.Z) { if (key.ShiftPressed) Redo(); else Undo(); }
        else if (key.CtrlPressed && key.Keycode == Key.Y) Redo();
        else if (key.Keycode == Key.Space) SetPlaying(!_playing);
        else if (key.Keycode == Key.Escape) RequestClose();
        else if (key.Keycode == Key.Left) SetPhase(Math.Max(0, _phase - 1f / 60f));
        else if (key.Keycode == Key.Right) SetPhase(Math.Min(1, _phase + 1f / 60f));
        else handled = false;
        if (handled) GetViewport().SetInputAsHandled();
    }

    public override void _Input(InputEvent input)
    {
        if (input is InputEventMouseButton { Pressed: false } mouse)
        {
            if (mouse.ButtonIndex == MouseButton.Left) _dragAxis = -1;
            if (mouse.ButtonIndex == MouseButton.Right) _orbiting = false;
        }
        if (_held == null || _discard.Visible || _error.Visible || input is not InputEventKey { Pressed: true, Echo: false, CtrlPressed: true } key) return;
        if (key.Keycode is not (Key.S or Key.Z or Key.Y)) return;
        Control? focused = GetViewport().GuiGetFocusOwner();
        SpinBox? spin = focused?.GetParent() as SpinBox;
        // Search and other ordinary text fields retain their own text undo. Authored values use document undo.
        if (key.Keycode != Key.S && focused is LineEdit && spin == null && focused != _contactSocketField) return;
        spin?.Apply();
        if (focused == _contactSocketField || spin != null) focused?.ReleaseFocus();
        if (key.Keycode == Key.S) Save();
        else if (key.Keycode == Key.Y || key.ShiftPressed) Redo();
        else Undo();
        GetViewport().SetInputAsHandled();
    }

    private static string DiskFingerprint() => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(HeldPresentations.FilePath)));

    private void CommitFocusedField()
    {
        Control? focused = GetViewport().GuiGetFocusOwner();
        if (focused?.GetParent() is SpinBox spin) { spin.Apply(); focused.ReleaseFocus(); }
        else if (focused == _contactSocketField) focused?.ReleaseFocus();
    }

    private void SetPlaying(bool value) { _playing = value; if (_held != null) _playPhase = GameplayPhase(_phase); if (_play != null) _play.Text = value ? "Pause" : "Play"; }

    private void SetPhase(float value)
    {
        _phase = Math.Clamp(value, 0, 1);
        _refreshing = true; _timeline.Value = _phase; _refreshing = false;
        RefreshFields(); SamplePreview();
    }

    public override void _Process(double delta)
    {
        _elapsed += delta;
        if (_held == null) return;
        if (_playing)
        {
            if (_gameTiming)
            {
                _playPhase += (float)delta * _speed / HeldActionTiming.Duration(_content.Item(_held.Id), _action, Clip.Duration);
                if (_playPhase > 1) _playPhase %= 1;
                float marker = GameplayMarker();
                _phase = marker >= 0 ? Clip.AlignMarker(_playPhase, marker) : _playPhase;
            }
            else
            {
                _phase += (float)delta * _speed / Math.Max(0.001f, Clip.Duration);
                if (_phase > 1) _phase %= 1;
            }
            _refreshing = true; _timeline.Value = _phase; _refreshing = false;
        }
        UpdateCamera();
        SamplePreview();
        UpdateGizmo();
        if (!_automating && _options.ContainsKey("editor-check") && _elapsed > 0.8)
        {
            _automating = true;
            RunEditorCheck();
        }
        if (!_automating && _options.TryGetValue("editor-sheet", out string? sheet) && _elapsed > 0.8)
        {
            _automating = true;
            CaptureSheet(sheet);
        }
        if (!_automating && _options.TryGetValue("editor-animation-dir", out string? animationDirectory) && _elapsed > 0.8)
        {
            _automating = true;
            CaptureAnimation(animationDirectory);
        }
        if (!_capturing && !_options.ContainsKey("editor-check") && !_options.ContainsKey("editor-sheet") && !_options.ContainsKey("editor-animation-dir") && _options.TryGetValue("shot", out string? path) && _elapsed > Number("wait", 2))
        {
            _capturing = true;
            Capture(path);
        }
    }

    private static VBoxContainer VBox(float spacing)
    {
        var box = new VBoxContainer(); Ui.Track(box, b => b.AddThemeConstantOverride("separation", Ui.Px(spacing))); return box;
    }
    private static HBoxContainer HBox(float spacing)
    {
        var box = new HBoxContainer(); Ui.Track(box, b => b.AddThemeConstantOverride("separation", Ui.Px(spacing))); return box;
    }
    private static Button Button(string text, Action action, string tooltip = "")
    {
        var b = new Button { Text = text, TooltipText = tooltip }; b.Pressed += action; return b;
    }
    private static SpinBox NumberField(double min, double max, double step, double value, Action<double> change)
    {
        var field = new SpinBox { MinValue = min, MaxValue = max, Step = step, Value = value, UpdateOnTextChanged = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        field.ValueChanged += value => change(value); return field;
    }
    private static StandardMaterial3D Material(Color color, bool unshaded = false) => new()
    {
        AlbedoColor = color, Roughness = 0.8f,
        ShadingMode = unshaded ? BaseMaterial3D.ShadingModeEnum.Unshaded : BaseMaterial3D.ShadingModeEnum.PerPixel,
    };
    private static MeshInstance3D Box(Vector3 size, Vector3 position, Material material) => new() { Mesh = new BoxMesh { Size = size }, Position = position, MaterialOverride = material };
    private string Option(string name, string fallback) => _options.TryGetValue(name, out string? value) ? value : fallback;
    private double Number(string name, double fallback) => _options.TryGetValue(name, out string? value) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ? number : fallback;
}
