using System.Collections.Generic;
using Godot;
using Hollow.Simulation;
using Num = System.Numerics;

namespace HollowUniverse.Gibs;

/// <summary>
/// What a break did (GROUND.md §9.4): whether pieces were placed or only dust, the middle of what broke, and the sounds
/// the data names for it: the cause's (the hammer's), else the material's (a model's own <c>break</c> sound wins over the
/// material's; the caller looks that up).
/// </summary>
public readonly record struct GibBreak(bool Shown, bool Pieces, Vector3 Centre, string? CauseSound, string? MaterialSound);

/// <summary>
/// Things breaking apart (GROUND.md §9.4), from anywhere, as <see cref="Fx.FxDirector"/> plays effects:
/// <see cref="Break(string, string, Transform3D, Vector3, DestroyCause, Vector3, float, ulong)"/>
/// breaks the model a catalogue key names (and a kit's part) into its gibs where it stood, whatever it was: the pieces come
/// from the model catalogue, how they fly and their dust from <c>gibs.json</c>. The world of pieces puts itself under the
/// scene tree's root on first use, or <see cref="Install"/> puts it under a node.
/// </summary>
public static class GibDirector
{
    private static GibWorld? _world;

    /// <summary>The world of pieces, created under the scene tree's root on first use.</summary>
    public static GibWorld World
    {
        get
        {
            if (_world == null || !GodotObject.IsInstanceValid(_world))
            {
                _world = new GibWorld { Name = "Gibs" };
                ((SceneTree)Engine.GetMainLoop()).Root.CallDeferred(Node.MethodName.AddChild, _world);
                GibLibrary.Request();
            }
            return _world;
        }
    }

    /// <summary>Puts the world of pieces under <paramref name="parent"/> now, and starts loading every gib file.</summary>
    public static GibWorld Install(Node parent)
    {
        if (_world != null && GodotObject.IsInstanceValid(_world))
        {
            return _world;
        }
        _world = new GibWorld { Name = "Gibs" };
        parent.AddChild(_world);
        GibLibrary.Request();
        return _world;
    }

    /// <summary>
    /// Breaks <paramref name="model"/> (a catalogue key; <paramref name="part"/>: a kit's part, empty for a whole model)
    /// standing at <paramref name="at"/>: its pieces fly from <paramref name="blow"/> (a unit direction or zero) as
    /// <paramref name="cause"/> throws them, under gravity along −<paramref name="up"/> (<paramref name="gravity"/> m/s²;
    /// 0: gibs.json's), with its material's dust. Nothing for a model without gibs; dust only far away.
    /// </summary>
    public static GibBreak Break(string model, string part, Transform3D at, Vector3 blow, DestroyCause cause, Vector3 up, float gravity = 0,
        ulong seed = 0)
    {
        if (GibLibrary.Get(model, part) is not { } set)
        {
            return default;
        }
        GibCause how = GibCatalog.Cause(cause);
        bool pieces = World.Break(set, at, blow, how, up, gravity, seed, out Vector3 centre);
        return new GibBreak(true, pieces, centre, how.Sound, set.Material.Sound);
    }

    /// <summary>The same for a model placed as the simulation reports it (zone frame = Godot's coordinates in the ground game).</summary>
    public static GibBreak Break(in ModelPlacement placed, Num.Vector3 blow, DestroyCause cause, Num.Vector3 up, float gravity = 0, ulong seed = 0) =>
        Break(placed.Model, placed.Part, Transform(placed), new Vector3(blow.X, blow.Y, blow.Z), cause, new Vector3(up.X, up.Y, up.Z), gravity, seed);

    /// <summary>A placed model's transform: its turn, its size, its place.</summary>
    public static Transform3D Transform(in ModelPlacement placed)
    {
        var q = new Quaternion(placed.Rotation.X, placed.Rotation.Y, placed.Rotation.Z, placed.Rotation.W).Normalized();
        return new Transform3D(new Basis(q).Scaled(Vector3.One * placed.Scale), new Vector3(placed.Position.X, placed.Position.Y, placed.Position.Z));
    }

    /// <summary>Reads every gib set now and draws a piece of each once before <paramref name="camera"/> (while a world opens).</summary>
    public static void Warm(Camera3D camera)
    {
        var sets = new List<GibSet>(GibLibrary.Prepare());
        World.Warm(camera, sets);
    }
}
