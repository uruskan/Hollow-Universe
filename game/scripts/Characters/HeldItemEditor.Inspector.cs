using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using HollowUniverse.Common;

namespace HollowUniverse.Characters;

public partial class HeldItemEditor
{
    private void BuildInspector()
    {
        int selected = Math.Max(0, _tabs.CurrentTab);
        _refreshing = true;
        foreach (Node child in _tabs.GetChildren()) { _tabs.RemoveChild(child); child.QueueFree(); }
        _refreshing = false;
        _refreshFields.Clear();
        VBoxContainer placement = Tab("Placement");
        Note(placement, "Base placement at rest. FPS uses camera space: +X right, +Y up, −Z forward. TPS adjusts the character's held attachment.");
        Heading(placement, "First person");
        VectorField(placement, "Position · m", () => _profile.FirstPerson.Position, v => Edit("fps.position", p => p.FirstPerson.Position = v));
        VectorField(placement, "Rotation · degrees", () => _profile.FirstPerson.Rotation, v => Edit("fps.rotation", p => p.FirstPerson.Rotation = v), degrees: true);
        if (_held.Kind is HeldKind.Pistol or HeldKind.Rifle || _held.LeftHanded)
        {
            Heading(placement, "Aimed / drawn placement");
            Note(placement, "Offsets from the sight or bow aim pose. Preview with Aim / draw or its action clip.");
            VectorField(placement, "Aim position offset · m", () => _profile.AimOffset.Position, v => Edit("aim.position", p => p.AimOffset.Position = v));
            VectorField(placement, "Aim rotation offset · degrees", () => _profile.AimOffset.Rotation, v => Edit("aim.rotation", p => p.AimOffset.Rotation = v), degrees: true);
            if (_held.LeftHanded) ScalarField(placement, "Bow string draw distance · m", () => _profile.BowDrawDistance, v => Edit("bow.draw", p => p.BowDrawDistance = v), 0.01, 1, 0.005);
        }
        Heading(placement, "Third person");
        VectorField(placement, "Attachment offset · m", () => _profile.ThirdPerson.Position, v => Edit("tps.position", p => p.ThirdPerson.Position = v));
        VectorField(placement, "Attachment rotation · degrees", () => _profile.ThirdPerson.Rotation, v => Edit("tps.rotation", p => p.ThirdPerson.Rotation = v), degrees: true);
        Heading(placement, "Swing pivot");
        VectorField(placement, "Item-local pivot · m", () => _profile.SwingPivot, v => Edit("pivot", p => p.SwingPivot = v));
        placement.AddChild(Ui.Label("Contact socket", TextStyle.Secondary));
        _contactSocketField = new LineEdit { Text = _profile.ContactSocket, PlaceholderText = "impact / tip / flame" };
        void CommitSocket()
        {
            if (!_refreshing && _contactSocketField.Text != _profile.ContactSocket) Edit("contact.socket", p => p.ContactSocket = _contactSocketField.Text.Trim());
        }
        _contactSocketField.TextSubmitted += _ => CommitSocket();
        _contactSocketField.FocusExited += CommitSocket;
        _refreshFields.Add(() => _contactSocketField.Text = _profile.ContactSocket);
        placement.AddChild(_contactSocketField);
        Note(placement, "The item socket used for the event pose: impact for axe heads, tip for spears, flame for torches. Its actual position appears as an orange dot when Move grip is on.");
        Note(placement, "Changing this item does not alter another item in its family. Save all commits every dirty profile.");
        placement.AddChild(Button("Restore this item's saved profile", () =>
        {
            SetPlaying(false);
            PushUndo("restore", coalesce: false);
            _profile = HeldPresentations.Clone(_saved[_held.Id]);
            Commit("Restored the last saved profile. Undo is available.");
            RefreshKeys(); RefreshFields();
        }));

        VBoxContainer hands = Tab("Hands");
        Note(hands, "Grip offsets are relative to the item's grip socket. Wrist offsets and elbow hints refine the arm solve. These settings are shared by FPS and TPS.");
        var hand = new OptionButton(); hand.AddItem("Right hand"); hand.AddItem("Left / support hand"); hand.Select(_hand);
        hand.ItemSelected += i => { _hand = (int)i; RefreshFields(); };
        _refreshFields.Add(() => hand.Select(_hand));
        hands.AddChild(hand);
        Heading(hands, "Grip and wrist");
        VectorField(hands, "Grip offset · m", () => Hand.GripOffset.Position, v => EditHand("grip.position", h => h.GripOffset.Position = v));
        VectorField(hands, "Grip orientation · degrees", () => Hand.GripOffset.Rotation, v => EditHand("grip.rotation", h => h.GripOffset.Rotation = v), degrees: true);
        VectorField(hands, "Wrist offset · m", () => Hand.WristOffset.Position, v => EditHand("wrist.position", h => h.WristOffset.Position = v));
        VectorField(hands, "Wrist rotation · degrees", () => Hand.WristOffset.Rotation, v => EditHand("wrist.rotation", h => h.WristOffset.Rotation = v), degrees: true);
        Heading(hands, "Arm solve");
        VectorField(hands, "Elbow pole · m", () => Hand.Pole, v => EditHand("pole", h => h.Pole = v));
        VectorField(hands, "Shoulder offset · m", () => Hand.Shoulder, v => EditHand("shoulder", h => h.Shoulder = v));
        ScalarField(hands, "Handle radius · m", () => Hand.Radius, v => EditHand("radius", h => h.Radius = v), 0, 0.15, 0.001);
        ScalarField(hands, "Palm seat · m", () => Hand.Seat, v => EditHand("seat", h => h.Seat = v), 0, 0.2, 0.001);
        Heading(hands, "Finger joints · degrees");
        Note(hands, "Each row: knuckle / middle / tip. Changes are explicit poses; inspect the fingers from the orbit camera.");
        string[] names = { "Thumb", "Index", "Middle", "Ring", "Little" };
        for (int finger = 0; finger < names.Length; finger++)
        {
            int offset = finger * 3;
            VectorField(hands, names[finger], () => Hand.Fingers.Length == 15 ? new Vector3(Hand.Fingers[offset], Hand.Fingers[offset + 1], Hand.Fingers[offset + 2]) : Vector3.Zero,
                v => EditHand("finger." + offset, h =>
                {
                    if (h.Fingers.Length != 15) h.Fingers = new float[15];
                    h.Fingers[offset] = v.X; h.Fingers[offset + 1] = v.Y; h.Fingers[offset + 2] = v.Z;
                }), degrees: true, axisLabels: false, min: -30, max: 140);
        }

        VBoxContainer motion = Tab("Motion");
        Note(motion, "Scrub the timeline, then edit a value to add or update a key at that time. Base placement is kept separate from these motion offsets.");
        ScalarField(motion, "Authored clip duration · seconds", () => Clip.Duration, v => Edit("duration", p => p.Actions[_action].Duration = v), 0.05, 20, 0.01);
        ScalarField(motion, "Authored event pose · fraction (−1 = none)", () => Clip.Marker, v => Edit("marker", p => p.Actions[_action].Marker = v), -1, 1, 0.001);
        ScalarField(motion, "Move selected key to phase", () => _phase, MoveKey, 0, 1, 0.001);
        var ease = new OptionButton();
        ease.AddItem("Smooth interpolation"); ease.AddItem("Linear interpolation"); ease.AddItem("Ease in"); ease.AddItem("Ease out");
        string[] easeValues = { "smooth", "linear", "in", "out" };
        ease.ItemSelected += i => { if (!_refreshing) EditKey("ease", k => k.Ease = easeValues[(int)i]); };
        _refreshFields.Add(() => ease.Select(Math.Max(0, Array.IndexOf(easeValues, Clip.Sample(_phase).Ease))));
        motion.AddChild(ease);
        Heading(motion, "First-person item motion");
        VectorField(motion, "Position offset · m", () => Clip.Sample(_phase).Position, v => EditKey("key.fps.position", k => k.Position = v));
        VectorField(motion, "Rotation offset · degrees", () => Clip.Sample(_phase).Rotation, v => EditKey("key.fps.rotation", k => k.Rotation = v), degrees: true);
        Heading(motion, "Third-person item motion");
        VectorField(motion, "Position offset · m", () => Clip.Sample(_phase).ThirdPersonPosition, v => EditKey("key.tps.position", k => k.ThirdPersonPosition = v));
        VectorField(motion, "Rotation offset · degrees", () => Clip.Sample(_phase).ThirdPersonRotation, v => EditKey("key.tps.rotation", k => k.ThirdPersonRotation = v), degrees: true);
        Heading(motion, "Animated hand offsets");
        VectorField(motion, "Right hand position · m", () => Clip.Sample(_phase).RightHandPosition, v => EditKey("key.right.position", k => k.RightHandPosition = v));
        VectorField(motion, "Right hand rotation · degrees", () => Clip.Sample(_phase).RightHandRotation, v => EditKey("key.right.rotation", k => k.RightHandRotation = v), degrees: true);
        VectorField(motion, "Left hand position · m", () => Clip.Sample(_phase).LeftHandPosition, v => EditKey("key.left.position", k => k.LeftHandPosition = v));
        VectorField(motion, "Left hand rotation · degrees", () => Clip.Sample(_phase).LeftHandRotation, v => EditKey("key.left.rotation", k => k.LeftHandRotation = v), degrees: true);
        Heading(motion, "Animated fingers");
        Note(motion, "Empty channels inherit the base grip; explicit keys can open fingers during a release or reload.");
        var animatedHand = new OptionButton(); animatedHand.AddItem("Right fingers"); animatedHand.AddItem("Left fingers");
        animatedHand.ItemSelected += i => { _hand = (int)i; RefreshFields(); };
        _refreshFields.Add(() => animatedHand.Select(_hand));
        motion.AddChild(animatedHand);
        var inherit = new CheckButton { Text = "Inherit base finger pose at this key" };
        inherit.Toggled += value =>
        {
            if (_refreshing) return;
            EditKey("fingers.inherit", key =>
            {
                float[] values = value ? Array.Empty<float>() : (float[])Hand.Fingers.Clone();
                if (_hand == 0) key.RightFingers = values; else key.LeftFingers = values;
            });
        };
        _refreshFields.Add(() =>
        {
            var key = Clip.Keys.FirstOrDefault(k => MathF.Abs(k.At - _phase) < 0.0005f);
            inherit.ButtonPressed = key == null || (_hand == 0 ? key.RightFingers : key.LeftFingers).Length == 0;
            inherit.Text = (_hand == 0 ? "Right" : "Left") + " fingers: inherit base pose at this key";
        });
        motion.AddChild(inherit);
        for (int finger = 0; finger < names.Length; finger++)
        {
            int offset = finger * 3;
            VectorField(motion, names[finger], () =>
            {
                HeldPresentationKey key = Clip.Sample(_phase, _profile.RightHand.Fingers, _profile.LeftHand.Fingers);
                float[] values = _hand == 0 ? key.RightFingers : key.LeftFingers;
                if (values.Length != 15) values = Hand.Fingers;
                return values.Length == 15 ? new Vector3(values[offset], values[offset + 1], values[offset + 2]) : Vector3.Zero;
            }, v => EditKey("fingers." + _hand + "." + offset, key =>
            {
                float[] values = _hand == 0 ? key.RightFingers : key.LeftFingers;
                if (values.Length != 15) values = Hand.Fingers.Length == 15 ? (float[])Hand.Fingers.Clone() : new float[15];
                values[offset] = v.X; values[offset + 1] = v.Y; values[offset + 2] = v.Z;
                if (_hand == 0) key.RightFingers = values; else key.LeftFingers = values;
            }), degrees: true, axisLabels: false, min: -30, max: 140);
        }
        Note(motion, "This timeline edits the animation. Gameplay keeps its own authoritative durations and hit times; the animation is retimed to place this event pose at the gameplay event. Changing this marker does not change damage timing.");
        _tabs.CurrentTab = Math.Min(selected, 2);
        RefreshFields();
    }

    private HeldPresentationHand Hand => _hand == 0 ? _profile.RightHand : _profile.LeftHand;

    private VBoxContainer Tab(string title)
    {
        var scroll = new ScrollContainer { Name = title, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var content = VBox(6);
        content.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(content); _tabs.AddChild(scroll);
        return content;
    }

    private static void Heading(VBoxContainer parent, string title)
    {
        parent.AddChild(Ui.Separator()); parent.AddChild(Ui.Label(title, TextStyle.Overline));
    }

    private static void Note(VBoxContainer parent, string text)
    {
        var label = Ui.Label(text, TextStyle.Caption);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        parent.AddChild(label);
    }

    private void VectorField(VBoxContainer parent, string name, Func<Vector3> get, Action<Vector3> set, bool degrees = false, bool axisLabels = true, double? min = null, double? max = null)
    {
        parent.AddChild(Ui.Label(name, TextStyle.Secondary));
        var row = HBox(4); parent.AddChild(row);
        var fields = new SpinBox[3];
        string[] axes = { "X", "Y", "Z" };
        for (int i = 0; i < 3; i++)
        {
            int axis = i;
            fields[i] = NumberField(min ?? (degrees ? -360 : -2), max ?? (degrees ? 360 : 2), degrees ? 0.5 : 0.001, get()[i], value =>
            {
                if (_refreshing) return;
                Vector3 vector = get(); vector[axis] = (float)value; set(vector);
            });
            fields[i].Prefix = axisLabels ? axes[i] : "";
            fields[i].TooltipText = name + (axisLabels ? " / " + axes[i] : " / joint " + (i + 1));
            Ui.Size(fields[i], 85);
            row.AddChild(fields[i]);
        }
        if (name == "Position · m") _testPositionX = fields[0];
        _refreshFields.Add(() => { Vector3 value = get(); for (int i = 0; i < 3; i++) fields[i].Value = value[i]; });
    }

    private void ScalarField(VBoxContainer parent, string name, Func<float> get, Action<float> set, double min, double max, double step)
    {
        parent.AddChild(Ui.Label(name, TextStyle.Secondary));
        var field = NumberField(min, max, step, get(), v => { if (!_refreshing) set((float)v); });
        parent.AddChild(field);
        _refreshFields.Add(() => field.Value = get());
    }

    private void RefreshFields()
    {
        if (_profile == null) return;
        _refreshing = true;
        foreach (Action refresh in _refreshFields) refresh();
        int key = Clip.Keys.FindIndex(k => MathF.Abs(k.At - _phase) < 0.0005f);
        if (key >= 0) _keys.Select(key);
        else _keys.Select(-1);
        _refreshing = false;
    }

    private void RefreshKeys()
    {
        _refreshing = true;
        _keys.Clear();
        for (int i = 0; i < Clip.Keys.Count; i++)
        {
            HeldPresentationKey key = Clip.Keys[i];
            _keys.AddItem($"Key {i + 1}  ·  {key.At * 100:0.0}%  ·  {key.At * Clip.Duration:0.000} s");
        }
        _ticks.Keys = Clip.Keys.Select(k => k.At).ToArray();
        _ticks.Marker = Clip.Marker;
        _ticks.QueueRedraw();
        _refreshing = false;
    }

    private void Edit(string key, Action<HeldPresentationProfile> edit)
    {
        SetPlaying(false);
        PushUndo(key);
        edit(_profile);
        Commit("Preview updated. Save all commits the edited profiles.");
        RefreshKeys(); RefreshFields();
        RefreshTiming();
    }

    private void EditHand(string key, Action<HeldPresentationHand> edit) => Edit((_hand == 0 ? "right." : "left.") + key, _ => edit(Hand));

    private void EditKey(string key, Action<HeldPresentationKey> edit)
    {
        SetPlaying(false);
        PushUndo(_action + "." + _phase.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + "." + key);
        HeldPresentationKey? target = Clip.Keys.FirstOrDefault(k => MathF.Abs(k.At - _phase) < 0.0005f);
        if (target == null)
        {
            target = Clip.Sample(_phase, _profile.RightHand.Fingers, _profile.LeftHand.Fingers);
            target.At = _phase;
            Clip.Keys.Add(target);
            Clip.Keys.Sort((a, b) => a.At.CompareTo(b.At));
        }
        edit(target);
        Commit($"Key recorded at {_phase * 100:0.0}% in {_action}. Undo is available.");
        RefreshKeys(); RefreshFields();
    }

    private void UpsertKey() => EditKey("insert", _ => { });

    private void DeleteKey()
    {
        SetPlaying(false);
        int index = Clip.Keys.FindIndex(k => MathF.Abs(k.At - _phase) < 0.0005f);
        if (index < 0) { _statusText = "Select an existing key before deleting."; RefreshStatus(); return; }
        if (index == 0 || index == Clip.Keys.Count - 1) { _statusText = "Start and end keys are required. Edit their poses instead."; RefreshStatus(); return; }
        PushUndo("delete", coalesce: false);
        Clip.Keys.RemoveAt(index);
        Commit("Key deleted. Undo is available.");
        RefreshKeys(); RefreshFields();
    }

    private void MoveKey(float phase)
    {
        SetPlaying(false);
        int index = Clip.Keys.FindIndex(k => MathF.Abs(k.At - _phase) < 0.0005f);
        if (index <= 0 || index == Clip.Keys.Count - 1)
        {
            _statusText = "Choose an interior key to move. Start and end keys stay at 0 and 1.";
            RefreshStatus(); RefreshFields(); return;
        }
        if (Clip.Keys.Any(k => MathF.Abs(k.At - phase) < 0.0005f))
        {
            _statusText = "A key already occupies that phase."; RefreshStatus(); RefreshFields(); return;
        }
        PushUndo("move-key", coalesce: false);
        Clip.Keys[index].At = phase;
        Clip.Keys.Sort((a, b) => a.At.CompareTo(b.At));
        _phase = phase;
        Commit("Moved the animation key."); RefreshKeys(); SetPhase(phase);
    }

    private void PushUndo(string key, bool coalesce = true)
    {
        if (!_undo.TryGetValue(_held.Id, out Stack<HeldPresentationProfile>? history)) _undo[_held.Id] = history = new();
        if (!coalesce || _lastEditKey != _held.Id + "/" + key || _elapsed - _lastEditAt > 0.4)
        {
            history.Push(HeldPresentations.Clone(_profile));
            // Retain the most recent 100 edits without sharing mutable profile references.
            if (history.Count > 100) _undo[_held.Id] = new Stack<HeldPresentationProfile>(history.Take(100).Reverse());
        }
        if (_redo.TryGetValue(_held.Id, out var redo)) redo.Clear();
        _lastEditKey = _held.Id + "/" + key; _lastEditAt = _elapsed;
    }

    private void Commit(string status)
    {
        try
        {
            HeldPresentations.Replace(HeldPresentations.Clone(_profile));
            var json = new JsonSerializerOptions { IncludeFields = true };
            if (JsonSerializer.Serialize(_profile, json) == JsonSerializer.Serialize(_saved[_held.Id], json)) _dirty.Remove(_held.Id);
            else _dirty.Add(_held.Id);
            _statusText = status;
        }
        catch (Exception error)
        {
            // A combination of individually finite controls can still be invalid (for example a zero elbow pole).
            // Retain the last valid live profile, report why, and keep the authoring session usable.
            _profile = HeldPresentations.Clone(HeldPresentations.Get(_held.Id));
            _statusText = "Edit refused: " + error.Message;
        }
        RefreshPreview(); RefreshStatus();
        RefreshTiming();
    }

    private void Undo()
    {
        SetPlaying(false);
        if (!_undo.TryGetValue(_held.Id, out var history) || history.Count == 0) return;
        if (!_redo.TryGetValue(_held.Id, out var redo)) _redo[_held.Id] = redo = new();
        redo.Push(HeldPresentations.Clone(_profile));
        _profile = history.Pop(); _lastEditKey = "";
        Commit("Undid this item's last edit."); RefreshKeys(); RefreshFields();
    }

    private void Redo()
    {
        SetPlaying(false);
        if (!_redo.TryGetValue(_held.Id, out var redo) || redo.Count == 0) return;
        if (!_undo.TryGetValue(_held.Id, out var history)) _undo[_held.Id] = history = new();
        history.Push(HeldPresentations.Clone(_profile));
        _profile = redo.Pop(); _lastEditKey = "";
        Commit("Redid this item's edit."); RefreshKeys(); RefreshFields();
    }

    private void RefreshStatus()
    {
        _selection.Text = _held.Name + (_dirty.Contains(_held.Id) ? "  •" : "");
        _selection.TooltipText = $"{_held.Id} · {_profile.Family}";
        _save.Text = _dirty.Count > 0 ? $"Save all ({_dirty.Count})" : "Save all";
        _status.Text = _statusText + "   •   Ctrl+S save · Ctrl+Z undo · Space play · ← → step";
        _undoButton.Disabled = !_undo.TryGetValue(_held.Id, out var undo) || undo.Count == 0;
        _redoButton.Disabled = !_redo.TryGetValue(_held.Id, out var redo) || redo.Count == 0;
        for (int i = 0; i < _visibleItems.Count; i++) _items.SetItemText(i, (_dirty.Contains(_visibleItems[i].Id) ? "• " : "") + _visibleItems[i].Name);
    }

    private void Save()
    {
        CommitFocusedField();
        try
        {
            if (DiskFingerprint() != _diskFingerprint)
                throw new InvalidOperationException("The catalog changed on disk after this workshop opened. Your edits are still in memory; reload or restart before saving to avoid overwriting external changes.");
            HeldPresentations.Save();
            _diskFingerprint = DiskFingerprint();
            foreach (HeldItemModel item in HeldItems.All) _saved[item.Id] = HeldPresentations.Clone(HeldPresentations.Get(item.Id));
            _dirty.Clear();
            _statusText = "Saved and validated all profiles. Restart a running game to load them. Gameplay damage/reload timings are unchanged.";
            RefreshStatus();
        }
        catch (Exception error) { ShowError("Profiles were not saved", error); }
    }

    private void Reload()
    {
        try
        {
            HeldPresentations.Reload();
            _diskFingerprint = DiskFingerprint();
            _dirty.Clear(); _undo.Clear(); _redo.Clear();
            foreach (HeldItemModel item in HeldItems.All) _saved[item.Id] = HeldPresentations.Clone(HeldPresentations.Get(item.Id));
            SelectItem(_held);
            _statusText = "Reloaded the saved catalog."; RefreshStatus();
        }
        catch (Exception error) { ShowError("Profiles could not be reloaded", error); }
    }

    private void ConfirmDiscard(string text, Action action)
    {
        CommitFocusedField();
        if (_dirty.Count == 0) { action(); return; }
        SetPlaying(false);
        _afterDiscard = action;
        _discard.DialogText = text + $"\n{_dirty.Count} item(s) have unsaved changes. Cancel to keep editing or save first.";
        _discard.PopupCentered(new Vector2I(Ui.Px(520), Ui.Px(190)));
    }

    private void RequestClose() => ConfirmDiscard("Close the workshop and discard unsaved edits?", () => GetTree().Quit());

    private void ShowError(string title, Exception error)
    {
        _statusText = title + ": " + error.Message; RefreshStatus();
        _error.DialogText = _statusText;
        _error.PopupCentered(new Vector2I(Ui.Px(650), Ui.Px(240)));
        GD.PushError(_statusText);
    }
}

/// <summary>Key and event markers above the scrubber, at the same normalized coordinates as runtime sampling.</summary>
public partial class HeldItemEditorTimeline : Control
{
    public float[] Keys = Array.Empty<float>();
    public float Marker = -1;
    public float Phase;

    public HeldItemEditorTimeline() { MouseFilter = MouseFilterEnum.Ignore; }

    public override void _Draw()
    {
        float left = Ui.Px(7), width = Math.Max(0, Size.X - 2 * left), bottom = Size.Y - Ui.Px(2);
        DrawLine(new Vector2(left, bottom), new Vector2(left + width, bottom), Ui.Divider, Ui.Px(1));
        foreach (float time in Keys)
        {
            float x = left + time * width;
            DrawLine(new Vector2(x, bottom - Ui.Px(10)), new Vector2(x, bottom), Ui.Secondary, Ui.Px(2));
        }
        if (Marker >= 0)
        {
            float x = left + Marker * width;
            DrawLine(new Vector2(x, Ui.Px(2)), new Vector2(x, bottom), Ui.Warm, Ui.Px(2));
            DrawString(Ui.Medium, new Vector2(Math.Clamp(x + Ui.Px(5), left, Math.Max(left, Size.X - Ui.Px(60))), Ui.Px(13)), "EVENT", fontSize: Ui.Px(11), modulate: Ui.Warm);
        }
        float playhead = left + Phase * width;
        DrawCircle(new Vector2(playhead, bottom), Ui.Px(3), Ui.Accent);
    }
}
