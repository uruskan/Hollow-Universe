using System;
using Godot;
using Hollow.Client;
using Hollow.Simulation.Targets;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

public partial class GroundView
{
    private readonly AdminSelfCamera _adminCamera = new();

    public bool AdminThirdPerson => _adminCamera.Active;
    public bool FirstPersonVisible => _firstPerson.BodyVisible;
    public (float Yaw, float Pitch, float Distance, bool Dragging) AdminOrbitState =>
        (_adminCamera.Orbit.Yaw, _adminCamera.Orbit.Pitch, _adminCamera.Orbit.Distance, _adminCamera.Orbit.Dragging);

    private bool AdminCameraEligible => Client.State == ClientState.Playing && Client.HasCharacter &&
        Client.Console().IsAdmin && !Client.Local.IsDead && !Client.Ships().Seated;

    private void ValidateAdminCamera() => _adminCamera.Validate(AdminCameraEligible,
        !_ui.WantsCursor && !_ui.TakesKeys && !_keypad.Visible && !Blocked && GetWindow().HasFocus(),
        Input.IsMouseButtonPressed(MouseButton.Right), Input.IsMouseButtonPressed(MouseButton.Left));

    private bool SetAdminThirdPerson(bool enabled)
    {
        ValidateAdminCamera();
        _presses = HandPress.None;
        _freeLook.Hold(false);
        return _adminCamera.SetActive(enabled, AdminCameraEligible);
    }

    private bool AdminCameraInput(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Right } right)
            return _adminCamera.HandleSecondary(right.Pressed);
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } left)
            return _adminCamera.HandlePrimary(left.Pressed);
        if (!AdminThirdPerson) return false;
        switch (e)
        {
            case InputEventMouseMotion motion when Input.MouseMode == Input.MouseModeEnum.Captured:
                _adminCamera.Drag(-motion.Relative.X * MouseSensitivity, motion.Relative.Y * MouseSensitivity);
                return true;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel:
                _adminCamera.Orbit.Zoom(wheel.ButtonIndex == MouseButton.WheelUp ? 1 : -1);
                return true;
        }
        return false;
    }

    private (Num.Vector3 Eye, Num.Vector3 Forward) PlaceAdminCamera(Num.Vector3 up, Num.Vector3 facing)
    {
        // Aim at the torso, following the same predicted/rendered player position as the local full body.
        Num.Vector3 focus = Client.RenderEye - up * Math.Min(0.55f, Client.EyeHeight * 0.35f);
        (Num.Vector3 eye, _, _) = _adminCamera.Orbit.Follow(focus, up, facing);
        Num.Vector3 offset = eye - focus;
        var ray = new AimRay(focus, Num.Vector3.Normalize(offset), offset.Length(), Client.EntityId.Value) { AttackRadius = 0.15f };
        AimHit hit = AimHit.None;
        foreach (ITargetView view in Client.Targets().TargetViews) view.Aim(ray, ref hit);
        if (hit.Hit) eye = focus + ray.Direction * Math.Max(0.05f, hit.Distance - 0.2f);
        float clearance = OverGround(eye);
        if (clearance < 0.2f) eye += up * (0.2f - clearance);
        Num.Vector3 forward = Num.Vector3.Normalize(focus - eye);
        _camera!.GlobalTransform = new Transform3D(Basis.LookingAt(ToGodot(forward), ToGodot(up)), ToGodot(eye));
        _camera.Fov = FieldOfView;
        return (eye, forward);
    }
}
