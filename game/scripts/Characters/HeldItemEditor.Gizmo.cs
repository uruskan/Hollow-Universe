using System;
using Godot;
using HollowUniverse.Common;

namespace HollowUniverse.Characters;

public partial class HeldItemEditor
{
    private Node3D _gizmo = null!;
    private MeshInstance3D _contactGizmo = null!;
    private bool _showGizmo;
    private int _dragAxis = -1;
    private Vector2 _dragLast, _dragScreenDirection;
    private Vector3 _dragPosition;
    private float _dragPixelsPerMetre;
    private const float GizmoArm = 0.12f;
    private static readonly Vector3[] GizmoAxes = { Vector3.Right, Vector3.Up, Vector3.Back };

    private void BuildGizmo()
    {
        _gizmo = new Node3D { Name = "FpsGripMoveGizmo", Visible = false };
        _stage.AddChild(_gizmo);
        Color[] colors = { new(1, 0.28f, 0.25f), new(0.25f, 1, 0.45f), new(0.3f, 0.6f, 1) };
        for (int axis = 0; axis < 3; axis++)
        {
            var material = Material(colors[axis], true);
            material.NoDepthTest = true;
            material.UseFovOverride = true;
            material.FovOverride = CharacterMaterials.ViewmodelFov;
            Vector3 size = new(0.003f, 0.003f, 0.003f); size[axis] = GizmoArm;
            _gizmo.AddChild(Box(size, GizmoAxes[axis] * GizmoArm * 0.5f, material));
            _gizmo.AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 0.009f, Height = 0.018f },
                MaterialOverride = material,
                Position = GizmoAxes[axis] * GizmoArm,
            });
        }
        var contactMaterial = Material(new Color(1, 0.55f, 0.15f), true);
        contactMaterial.NoDepthTest = true;
        contactMaterial.UseFovOverride = true;
        contactMaterial.FovOverride = CharacterMaterials.ViewmodelFov;
        _contactGizmo = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.007f, Height = 0.014f }, MaterialOverride = contactMaterial, Visible = false,
        };
        _stage.AddChild(_contactGizmo);
    }

    private void UpdateGizmo()
    {
        if (_gizmo == null || _profile == null) return;
        _gizmo.Visible = _showGizmo && _view != 2;
        _gizmo.GlobalPosition = _viewmodel.PresentedGrip?.Origin ?? _eyes.Position + FpsPlacementPosition;
        _contactGizmo.Visible = _gizmo.Visible && _viewmodel.PresentedContact != null;
        if (_viewmodel.PresentedContact is Vector3 contact) _contactGizmo.GlobalPosition = contact;
    }

    private Vector2 ProjectViewmodel(Vector3 world)
    {
        Vector2 screen = _camera.UnprojectPosition(world);
        Vector2 center = (Vector2)_preview.Size * 0.5f;
        float ratio = MathF.Tan(Mathf.DegToRad(_camera.Fov) * 0.5f) / MathF.Tan(Mathf.DegToRad(CharacterMaterials.ViewmodelFov) * 0.5f);
        return center + (screen - center) * ratio;
    }

    private bool GizmoInput(InputEvent input)
    {
        if (!_showGizmo || _view == 2 || _profile == null) return false;
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
        {
            if (!button.Pressed)
            {
                bool wasDragging = _dragAxis >= 0;
                _dragAxis = -1;
                return wasDragging;
            }
            Vector2 center = ProjectViewmodel(_gizmo.GlobalPosition);
            int closest = -1;
            float closestDistance = Ui.Px(16);
            for (int i = 0; i < 3; i++)
            {
                Vector2 tip = ProjectViewmodel(_gizmo.GlobalPosition + GizmoAxes[i] * GizmoArm);
                Vector2 shaft = tip - center;
                if (shaft.Length() < Ui.Px(6)) continue;
                // The visible shaft is draggable too. Ignore its crowded origin so crossing
                // axes do not silently select whichever happens to be enumerated first.
                float along = Math.Clamp((button.Position - center).Dot(shaft) / shaft.LengthSquared(), 0.25f, 1f);
                float distance = (center + shaft * along).DistanceTo(button.Position);
                if (distance >= closestDistance) continue;
                closest = i; closestDistance = distance;
            }
            if (closest < 0) return false;
            SetPlaying(false);
            _dragAxis = closest;
            _dragLast = button.Position;
            _dragPosition = FpsPlacementPosition;
            Vector2 delta = ProjectViewmodel(_gizmo.GlobalPosition + GizmoAxes[closest] * GizmoArm) - center;
            _dragScreenDirection = delta.Normalized();
            _dragPixelsPerMetre = delta.Length() / GizmoArm;
            return true;
        }
        if (input is InputEventMouseMotion motion && _dragAxis >= 0)
        {
            float displacement = (motion.Position - _dragLast).Dot(_dragScreenDirection) / Math.Max(1, _dragPixelsPerMetre);
            _dragLast = motion.Position;
            if (motion.ShiftPressed) displacement *= 0.1f;
            _dragPosition[_dragAxis] += displacement;
            Vector3 position = _dragPosition;
            if (motion.CtrlPressed) position[_dragAxis] = MathF.Round(position[_dragAxis] * 100) / 100;
            Edit("gizmo." + _dragAxis, p => SetFpsPlacementPosition(p, position));
            UpdateGizmo();
            return true;
        }
        return false;
    }
}
