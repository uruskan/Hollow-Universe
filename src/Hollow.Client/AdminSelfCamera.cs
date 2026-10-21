namespace Hollow.Client;

/// <summary>Session-only self-inspection controls. Consumed right clicks stay consumed until released.</summary>
public sealed class AdminSelfCamera
{
    public OrbitCamera Orbit { get; } = new(distance: 3.5f, maximumDistance: 20);
    public bool Active { get; private set; }
    public bool SecondaryCaptured { get; private set; }
    public bool PrimaryCaptured { get; private set; }

    public bool SetActive(bool enabled, bool eligible)
    {
        Active = enabled && eligible;
        Orbit.Dragging = false;
        return Active;
    }

    public void Validate(bool eligible, bool controlsAvailable, bool secondaryHeld, bool primaryHeld = false)
    {
        if (!eligible) SetActive(false, false);
        if (!secondaryHeld) SecondaryCaptured = false;
        if (!primaryHeld) PrimaryCaptured = false;
        if (!Active || !controlsAvailable || !secondaryHeld) Orbit.Dragging = false;
        // Opening a panel or toggling the tool with RMB already held must not leak that press into a held action.
        if (Active && secondaryHeld) SecondaryCaptured = true;
        if (Active && primaryHeld) PrimaryCaptured = true;
    }

    public bool HandlePrimary(bool pressed)
    {
        if (!Active && !PrimaryCaptured) return false;
        PrimaryCaptured = pressed;
        return true;
    }

    public bool HandleSecondary(bool pressed)
    {
        if (!Active && !SecondaryCaptured) return false;
        SecondaryCaptured = pressed;
        Orbit.Dragging = Active && pressed;
        return true;
    }

    public void Drag(float yaw, float pitch)
    {
        if (Active && Orbit.Dragging) Orbit.Drag(yaw, pitch);
    }
}
