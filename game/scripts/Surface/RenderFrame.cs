using System;
using Godot;
using Hollow.Core;
using Hollow.Surface;

namespace HollowUniverse.Surface;

/// <summary>
/// The floating origin of the surface view (ARCHITECTURE.md §6): Godot's world is a tangent frame of the planet (+X
/// east, +Y up, +Z south, as <see cref="ZoneFrame"/>) whose origin follows the camera. Moving the origin keeps the
/// axes, so nothing turns and textures stay put; only positions shift. Body-frame positions stay in double precision.
/// </summary>
public sealed class RenderFrame
{
    private readonly Vector3D _east;
    private readonly Vector3D _up;
    private readonly Vector3D _south;

    public RenderFrame(Vector3D origin)
    {
        var zone = new ZoneFrame(origin);
        _east = zone.East;
        _up = zone.Up;
        _south = zone.South;
        Origin = origin;
        // Columns are the images of the body axes: render = (dot(v, east), dot(v, up), dot(v, south)).
        Basis = new Basis(
            new Vector3((float)_east.X, (float)_up.X, (float)_south.X),
            new Vector3((float)_east.Y, (float)_up.Y, (float)_south.Y),
            new Vector3((float)_east.Z, (float)_up.Z, (float)_south.Z));
    }

    /// <summary>The body-frame point at Godot's origin.</summary>
    public Vector3D Origin { get; private set; }

    /// <summary>Body-frame offsets of all origin moves so far (for textures and clouds that must not jump).</summary>
    public Vector3D Travelled { get; private set; }

    /// <summary>Rotation from body-frame axes to render axes.</summary>
    public Basis Basis { get; }

    public Vector3D Up => _up;

    public Vector3 ToRender(Vector3D body) => DirectionToRender(body - Origin);

    public Vector3 DirectionToRender(Vector3D v) =>
        new((float)Vector3D.Dot(v, _east), (float)Vector3D.Dot(v, _up), (float)Vector3D.Dot(v, _south));

    /// <summary>Render-axis components of a body-frame vector, in double precision.</summary>
    public Vector3D DirectionToRenderD(Vector3D v) => new(Vector3D.Dot(v, _east), Vector3D.Dot(v, _up), Vector3D.Dot(v, _south));

    public Vector3D ToBody(Vector3 render) => Origin + DirectionToBody(render);

    public Vector3D DirectionToBody(Vector3 v) => _east * v.X + _up * v.Y + _south * v.Z;

    /// <summary>Transform of something whose local axes are the body axes and whose local origin is a body-frame point.</summary>
    public Transform3D Anchor(Vector3D bodyPoint) => new(Basis, ToRender(bodyPoint));

    /// <summary>Moves the origin (axes unchanged); returns the shift to subtract from render positions.</summary>
    public Vector3 Move(Vector3D newOrigin)
    {
        Vector3D shift = newOrigin - Origin;
        Origin = newOrigin;
        Travelled += shift;
        return DirectionToRender(shift);
    }

    /// <summary>Render-axis offset of the origin from where the view started, wrapped to a period (for texture coordinates).</summary>
    public Vector3 WrappedTravel(double period)
    {
        Vector3D t = DirectionToRenderD(Travelled);
        static float Wrap(double x, double p) => (float)(x - p * Math.Floor(x / p));
        return new Vector3(Wrap(t.X, period), Wrap(t.Y, period), Wrap(t.Z, period));
    }
}
