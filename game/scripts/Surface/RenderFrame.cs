using System;
using Godot;
using Hollow.Core;
using Hollow.Surface;

namespace HollowUniverse.Surface;

/// <summary>
/// The frame Godot's world is drawn in (ARCHITECTURE.md §6). Two kinds:
/// <list type="bullet">
/// <item><see cref="World"/>: the body's own frame (docs/WHOLE_WORLD.md §3.1), origin at the centre, axes the body's, so
/// render coordinates are a zone's own (the ground game's whole world fits in floats within 7.3 km of its centre). It
/// never moves.</item>
/// <item><see cref="RenderFrame(Vector3D)"/>: a tangent frame of a large planet (+X east, +Y up, +Z south, as
/// <see cref="ZoneFrame"/>) whose origin follows the camera (the look-dev view at real size). Moving the origin keeps the
/// axes, so nothing turns and textures stay put; only positions shift.</item>
/// </list>
/// Body-frame positions stay in double precision.
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
        Basis = BasisOf(_east, _up, _south);
    }

    private RenderFrame()
    {
        _east = Vector3D.UnitX;
        _up = Vector3D.UnitY;
        _south = Vector3D.UnitZ;
        Origin = Vector3D.Zero;
        Basis = Basis.Identity;
        IsWorld = true;
    }

    /// <summary>The body's own frame: render coordinates are body coordinates as floats (docs/WHOLE_WORLD.md §3.1).</summary>
    public static RenderFrame World => new();

    /// <summary>Whether this is the body's own frame (origin at the centre; it never moves).</summary>
    public bool IsWorld { get; }

    // Columns are the images of the body axes: render = (dot(v, east), dot(v, up), dot(v, south)).
    private static Basis BasisOf(Vector3D east, Vector3D up, Vector3D south) => new(
        new Vector3((float)east.X, (float)up.X, (float)south.X),
        new Vector3((float)east.Y, (float)up.Y, (float)south.Y),
        new Vector3((float)east.Z, (float)up.Z, (float)south.Z));

    /// <summary>The body-frame point at Godot's origin.</summary>
    public Vector3D Origin { get; private set; }

    /// <summary>Body-frame offsets of all origin moves so far (for textures and clouds that must not jump).</summary>
    public Vector3D Travelled { get; private set; }

    /// <summary>Rotation from body-frame axes to render axes.</summary>
    public Basis Basis { get; }

    /// <summary>The planet's centre in render coordinates (floats: within a metre on a planet of thousands of kilometres).</summary>
    public Vector3 Centre => ToRender(Vector3D.Zero);

    public Vector3 ToRender(Vector3D body) => DirectionToRender(body - Origin);

    public Vector3 DirectionToRender(Vector3D v) =>
        new((float)Vector3D.Dot(v, _east), (float)Vector3D.Dot(v, _up), (float)Vector3D.Dot(v, _south));

    /// <summary>Render-axis components of a body-frame vector, in double precision.</summary>
    public Vector3D DirectionToRenderD(Vector3D v) => new(Vector3D.Dot(v, _east), Vector3D.Dot(v, _up), Vector3D.Dot(v, _south));

    public Vector3D ToBody(Vector3 render) => Origin + DirectionToBody(render);

    public Vector3D DirectionToBody(Vector3 v) => _east * v.X + _up * v.Y + _south * v.Z;

    /// <summary>Transform of something whose local axes are the body axes and whose local origin is a body-frame point.</summary>
    public Transform3D Anchor(Vector3D bodyPoint) => new(Basis, ToRender(bodyPoint));

    /// <summary>Moves the origin (axes unchanged); returns the shift to subtract from render positions. The world frame never moves.</summary>
    public Vector3 Move(Vector3D newOrigin)
    {
        if (IsWorld)
        {
            return Vector3.Zero;
        }
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

    /// <summary>
    /// The local axes at a body-frame point, in render axes (docs/WHOLE_WORLD.md §3.1): up from the centre, east the pole
    /// axis crossed with up (−Z of the body within a metre of a pole), north up crossed with east.
    /// </summary>
    public (Vector3 Up, Vector3 East, Vector3 North) LocalAxes(Vector3D bodyPoint)
    {
        var zone = new ZoneFrame(bodyPoint.LengthSquared > 0 ? bodyPoint : Vector3D.UnitY);
        Vector3 up = DirectionToRender(zone.Up), east = DirectionToRender(zone.East);
        return (up, east, up.Cross(east));
    }
}
