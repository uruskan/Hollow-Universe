using Godot;
using Hollow.Core;
using Hollow.Galaxy.Systems;
using Hollow.Surface;

namespace HollowUniverse.Surface;

// The surface view as the ground game's world (GROUND.md §4.3): the game drives the camera and owns the keys; the view
// draws the terrain, water, sky, plants and grass around it. Additive to the look-dev view.
public partial class SurfaceView
{
    /// <summary>
    /// Driven by the ground game: no free camera, no keys, no look-dev card, and no floating-origin moves, so the
    /// render frame stays the zone's frame (the zone keeps within 8.2 km of its origin, where floats are good to half
    /// a millimetre). Set before <see cref="OpenZone"/>.
    /// </summary>
    public bool Driven { get; set; }

    /// <summary>The camera the view draws for (the game places it every frame when <see cref="Driven"/>).</summary>
    public Camera3D Camera => _camera;

    /// <summary>The render frame (the zone's, when driven by the game).</summary>
    public RenderFrame? Frame => _frame;

    /// <summary>The universe time shown (seconds since J2000).</summary>
    public double ShownTime => _time;

    /// <summary>
    /// Opens the planet for a zone: render coordinates are the zone frame's (+X east, +Y up, +Z south at
    /// <paramref name="zoneOrigin"/>, a body-frame point on the ground), on foot, at a universe time, the planet
    /// turning <paramref name="rotationTimeScale"/> times faster than real counted from J2000, as the server's clock.
    /// </summary>
    public void OpenZone(StarSystem system, Body planet, PlanetSurface surface, Vector3D zoneOrigin, double time, double rotationTimeScale)
    {
        Driven = true;
        Vector3D up = zoneOrigin.Normalized;
        double latitude = DMath.Asin(up.Y) * DMath.RadToDeg;
        double longitude = DMath.Atan2(-up.Z, up.X) * DMath.RadToDeg;
        Open(system, planet, surface, latitude, longitude, EyeHeight, 0, 0, time);
        // The frame's origin exactly on the zone's (the axes agree to rounding, about 1e-16).
        _frame!.Move(zoneOrigin);
        _terrain.Reanchor();
        _vegetation?.Reanchor();
        _sky!.Ephemeris.SetRotationTimeScale(rotationTimeScale, 0);
        InterfaceVisible = false;
    }
}
