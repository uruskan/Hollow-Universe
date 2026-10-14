using System.Collections.Generic;
using Godot;
using Hollow.Simulation.Combat;
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
/// <see cref="Break(string, int, Transform3D, Vector3, DestroyCause, Vector3, float, ulong)"/> breaks whatever the Destroyed
/// event names (its key and grade) into its gibs where it stood, whatever it was: the pieces come from the model catalogue,
/// how they fly and their dust from <c>gibs.json</c>. The world of pieces puts itself under the scene tree's root on first
/// use, or <see cref="Install"/> puts it under a node.
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
    /// Breaks what <paramref name="key"/> and <paramref name="grade"/> name (as the Destroyed event names it) standing at
    /// <paramref name="at"/>: its pieces fly from <paramref name="blow"/> (a unit direction or zero) as <paramref name="cause"/>
    /// throws them, under gravity along −<paramref name="up"/> (<paramref name="gravity"/> m/s²; 0: gibs.json's), with its
    /// material's dust. Nothing for what has no gibs; dust only far away.
    /// </summary>
    public static GibBreak Break(string key, int grade, Transform3D at, Vector3 blow, DestroyCause cause, Vector3 up, float gravity = 0, ulong seed = 0)
    {
        if (GibLibrary.Find(key, grade) is not { } set)
        {
            return default;
        }
        GibCause how = GibCatalog.Cause(cause);
        bool pieces = World.Break(set, at, blow, how, up, gravity, seed, out Vector3 centre);
        return new GibBreak(true, pieces, centre, how.Sound, set.Material.Sound);
    }

    /// <summary>Breaks a set itself (the viewer, which goes through the catalogue's entries).</summary>
    public static GibBreak Break(GibSet set, Transform3D at, Vector3 blow, DestroyCause cause, Vector3 up, float gravity = 0, ulong seed = 0)
    {
        GibCause how = GibCatalog.Cause(cause);
        bool pieces = World.Break(set, at, blow, how, up, gravity, seed, out Vector3 centre);
        return new GibBreak(true, pieces, centre, how.Sound, set.Material.Sound);
    }

    /// <summary>The same where the simulation says (zone frame = Godot's coordinates in the ground game), with a uniform size.</summary>
    public static GibBreak Break(string key, int grade, Num.Vector3 position, Num.Quaternion rotation, Num.Vector3 blow, DestroyCause cause,
        Num.Vector3 up, float gravity = 0, ulong seed = 0, float scale = 1) =>
        Break(key, grade, Transform(position, rotation, scale), new Vector3(blow.X, blow.Y, blow.Z), cause, new Vector3(up.X, up.Y, up.Z), gravity, seed);

    /// <summary>A transform from the simulation's place, turn and size.</summary>
    public static Transform3D Transform(Num.Vector3 position, Num.Quaternion rotation, float scale = 1)
    {
        var q = new Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W).Normalized();
        return new Transform3D(new Basis(q).Scaled(Vector3.One * scale), new Vector3(position.X, position.Y, position.Z));
    }

    /// <summary>Reads every gib set now and draws a piece of each once before <paramref name="camera"/> (while a world opens).</summary>
    public static void Warm(Camera3D camera)
    {
        var sets = new List<GibSet>(GibLibrary.Prepare());
        World.Warm(camera, sets);
    }
}
