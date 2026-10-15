using System;
using System.Numerics;
using Hollow.Simulation.Ships;

namespace HollowUniverse.Ships;

public enum ShipCameraMode : byte
{
    /// <summary>First person, from the pilot's eye in the cockpit, looking along the nose.</summary>
    Cockpit,

    /// <summary>Third person, behind and above the ship.</summary>
    Chase,
}

/// <summary>
/// The pilot's two cameras (G6, GROUND.md §6.7), in the zone frame. The cockpit camera sits at the eye socket and turns
/// with the ship. The chase camera hangs behind and above: it follows the nose's heading and climb but not its roll,
/// swings after the ship in turns (its offset is smoothed, never its position, so it never falls behind at speed), looks
/// a little ahead, and stays above the ground. Presentation only; the host turns the result into render space.
/// </summary>
public sealed class ShipCamera
{
    private Vector3 _offset;
    private Vector3 _up;
    private bool _has;

    public ShipCameraMode Mode { get; set; } = ShipCameraMode.Chase;

    /// <summary>Metres behind the centre of mass, and above it.</summary>
    public float Distance { get; set; } = 12.5f;

    public float Height { get; set; } = 3.4f;

    /// <summary>The chase camera looks at a point this far ahead of the ship.</summary>
    public float LookAhead { get; set; } = 9f;

    /// <summary>Seconds for the chase camera to swing halfway to where it should be.</summary>
    public float Swing { get; set; } = 0.12f;

    /// <summary>Starts the chase camera where it should be (after a cut or a teleport).</summary>
    public void Reset() => _has = false;

    /// <summary>
    /// The camera this frame: its eye, the direction it looks and its up, in the zone frame. <paramref name="overGround"/>
    /// (optional) gives how far a point is above the ground, to keep the chase camera out of hills.
    /// </summary>
    public (Vector3 Eye, Vector3 Forward, Vector3 Up) Follow(in ShipState s, ShipDef def, Vector3 worldUp, float delta,
        Func<Vector3, float>? overGround = null)
    {
        Vector3 forward = s.Forward;
        Vector3 shipUp = s.Up;
        if (Mode == ShipCameraMode.Cockpit)
        {
            _has = false;
            Vector3 eye = s.PointOf(Hollow.Simulation.ShipSeats.EyeInFrame(def, def.Helm.Index, def.Interior?.Rest ?? Array.Empty<float>()), def.CentreOfMass);
            // A few degrees down from the nose line: the ground ahead shows over the dash.
            Vector3 look = Vector3.Normalize(forward * MathF.Cos(0.07f) - shipUp * MathF.Sin(0.07f));
            return (eye, look, shipUp);
        }

        // Upright: the world's up, leaning a little with the ship's roll; behind the nose as it climbs or dives; as far back
        // and up as the ship is big (the Tin Finch's distances, for a carrier ten times them).
        float size = MathF.Max(1, def.BoundingRadius / 3.6f);
        Vector3 up = Vector3.Normalize(worldUp * 0.8f + shipUp * 0.2f);
        Vector3 flat = forward - worldUp * Vector3.Dot(forward, worldUp);
        Vector3 heading = flat.LengthSquared() > 1e-4f ? Vector3.Normalize(flat) : Vector3.Normalize(-shipUp - worldUp * Vector3.Dot(-shipUp, worldUp) + new Vector3(1e-3f, 0, 0));
        // Follow the climb half way, so a steep climb still shows the ground.
        Vector3 back = Vector3.Normalize(heading * 0.5f + forward * 0.5f);
        Vector3 want = (-back * Distance + up * Height) * size;
        float k = _has ? 1 - MathF.Exp(-delta * MathF.Log(2) / Swing) : 1;
        _offset = Vector3.Lerp(_offset, want, k);
        _offset = Vector3.Normalize(_offset) * want.Length();
        _up = _has ? Vector3.Normalize(Vector3.Lerp(_up, up, k)) : up;
        _has = true;
        Vector3 centre = s.Position;
        Vector3 camera = centre + _offset;
        if (overGround != null)
        {
            float clear = overGround(camera);
            if (clear < 1.6f)
            {
                camera += worldUp * (1.6f - clear);
            }
        }
        Vector3 target = centre + (back * LookAhead + _up * 1.2f) * size;
        return (camera, Vector3.Normalize(target - camera), _up);
    }
}
