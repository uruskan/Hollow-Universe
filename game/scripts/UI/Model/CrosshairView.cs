namespace HollowUniverse.UI.Model;

/// <summary>Presentation state for the single ground crosshair; weapon spread is a cone half-angle in degrees.</summary>
public readonly record struct CrosshairView(bool Aiming = false, float SpreadDegrees = 0, float FieldOfView = 75, bool Hidden = false);
