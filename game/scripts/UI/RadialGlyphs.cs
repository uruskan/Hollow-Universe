using System;
using System.Collections.Generic;
using Godot;

namespace HollowUniverse.UI;

/// <summary>
/// The radial menu's outline glyphs (RustMVP's own perspective outlines, from SBareRoot.cpp), by name: every building
/// piece under its key in building.json, the hammer's <c>upgrade</c>, <c>rotate</c> and <c>demolish</c>, and the
/// interaction's <c>door</c>, <c>lock</c>, <c>unlock</c>, <c>keypad</c> and <c>pick-up</c>. Each is a set of polylines in
/// units where the glyph spans about −1 to 1; the radial scales, places and strokes them at their final size, so they
/// stay crisp at any interface scale. A name the table doesn't know draws a plain ring, so a missing glyph shows.
/// </summary>
public static class RadialGlyphs
{
    private static readonly Dictionary<string, Vector2[][]> Table = Build();
    private static readonly Vector2[][] Unknown = { Ring(0.6f) };

    /// <summary>Whether the table has a glyph of this name (for data checks).</summary>
    public static bool Has(string name) => Table.ContainsKey(name);

    /// <summary>A glyph's polylines.</summary>
    public static IReadOnlyList<Vector2[]> Strokes(string name) => Table.TryGetValue(name, out Vector2[][]? strokes) ? strokes : Unknown;

    private static Dictionary<string, Vector2[][]> Build()
    {
        var table = new Dictionary<string, Vector2[][]>(StringComparer.Ordinal);
        void Glyph(string name, params Vector2[][] strokes) => table.Add(name, strokes);

        // Building pieces, by their keys in building.json.
        Glyph("foundation", Diamond(.9f, -.38f), S(-.74f, -.38f, -.74f, .5f, 0, .88f, .74f, .5f, .74f, -.38f), S(0, .02f, 0, .88f));
        Glyph("foundation-triangle", S(-.82f, .18f, 0, -.72f, .82f, .18f, -.82f, .18f, -.82f, .63f, .82f, .63f, .82f, .18f));
        Vector2[][] floor = { Diamond(1f, -.08f), S(-.82f, -.08f, -.82f, .16f, 0, .6f, .82f, .16f, .82f, -.08f), S(0, .36f, 0, .6f) };
        Glyph("floor", floor);
        Glyph("floor-frame", [.. floor, Diamond(.55f, -.08f)]);
        Vector2[] triangle = S(-.82f, .28f, 0, -.55f, .82f, .28f, -.82f, .28f, -.82f, .48f, .82f, .48f, .82f, .28f);
        Glyph("floor-triangle", triangle);
        Glyph("floor-frame-triangle", triangle, S(-.38f, .12f, 0, -.25f, .38f, .12f, -.38f, .12f));
        Glyph("wall", Wall(-.65f));
        Glyph("half-wall", Wall(-.12f));
        Glyph("low-wall", Wall(.28f));
        Glyph("window", [.. Wall(-.65f), S(-.35f, .35f, -.35f, -.22f, .28f, -.37f, .28f, .2f, -.35f, .35f)]);
        Glyph("doorway", Wall(-.65f, jamb: .32f));
        Glyph("wall-frame", Wall(-.65f, jamb: .47f));
        Glyph("stairs-l", S(-.82f, .65f, -.82f, .3f, -.3f, .3f, -.3f, -.6f, .75f, -.8f, .75f, -.3f, .05f, -.1f, .05f, .7f, -.82f, .9f, -.82f, .65f),
            S(-.3f, .3f, .05f, .18f), S(-.3f, -.1f, .05f, -.2f));
        var flights = new List<Vector2[]> { S(-.8f, .8f, -.8f, -.6f, .7f, -.85f, .7f, .55f, .2f, .65f, .2f, -.25f, -.3f, -.15f, -.3f, .7f, -.8f, .8f) };
        for (int j = 0; j < 3; j++)
        {
            flights.Add(S(-.8f, .45f - j * .35f, -.3f, .35f - j * .35f));
            flights.Add(S(.2f, .4f - j * .3f, .7f, .3f - j * .3f));
        }
        Glyph("stairs-u", flights.ToArray());
        Vector2[][] spiral = { S(-.55f, .5f, .48f, .28f, .48f, -.38f, -.25f, -.2f, -.25f, .12f, .1f, .04f), S(.1f, .65f, .1f, -.7f) };
        Glyph("stairs-spiral", [S(-.8f, .6f, -.8f, -.55f, .7f, -.8f, .7f, .35f), .. spiral]);
        Glyph("stairs-spiral-triangle", [S(-.85f, .7f, 0, -.85f, .85f, .7f, -.85f, .7f), .. spiral]);
        Glyph("ramp", S(-.85f, .65f, .5f, -.65f, .85f, -.4f, -.5f, .88f, -.85f, .65f), S(.85f, -.4f, .85f, .58f, -.5f, .88f));
        Glyph("steps", S(-.85f, .75f, -.85f, .4f, -.3f, .4f, -.3f, -.1f, .25f, -.1f, .25f, -.65f, .85f, -.65f),
            S(-.85f, .75f, -.5f, .9f, -.5f, .55f, .05f, .55f, .05f, .05f, .6f, .05f, .6f, -.5f, .95f, -.5f));
        Glyph("roof", S(-.85f, .25f, 0, -.7f, .85f, .25f, .4f, .65f, -.45f, .65f, -.85f, .25f), S(0, -.7f, .4f, -.3f, .85f, .25f),
            S(.4f, -.3f, -.45f, .65f));
        Glyph("roof-triangle", S(-.85f, .65f, 0, -.85f, .85f, .65f, -.85f, .65f), S(0, -.85f, 0, .65f));

        // The hammer: a raised box (every upgrade), a turning arrow, a cross.
        Glyph("upgrade", Diamond(.9f, -.18f), Diamond(.9f, .28f), S(-.74f, -.18f, -.74f, .28f), S(.74f, -.18f, .74f, .28f), S(0, .22f, 0, .68f));
        Glyph("rotate", S(-.65f, .65f, -.65f, -.55f, .55f, -.55f, .8f, -.25f), S(.8f, -.72f, .8f, -.25f, .3f, -.25f), S(.65f, .65f, -.25f, .65f));
        Glyph("demolish", S(-.7f, -.7f, .7f, .7f), S(.7f, -.7f, -.7f, .7f));

        // Interaction: a door ajar, a padlock shut and open, a keypad, an arrow up out of a tray.
        Glyph("door", S(-.65f, .8f, -.65f, -.8f, .6f, -.8f, .6f, .8f), S(-.65f, .8f, .2f, .55f, .2f, -.55f, -.65f, -.8f), S(-.03f, .06f, .08f, .06f));
        Glyph("lock", Padlock(-.05f));
        Glyph("unlock", Padlock(-.3f));
        var keypad = new List<Vector2[]> { S(-.62f, -.8f, .62f, -.8f, .62f, .85f, -.62f, .85f, -.62f, -.8f) };
        for (int x = 0; x < 3; x++)
        {
            for (int y = 0; y < 3; y++)
            {
                keypad.Add(S(-.35f + x * .35f, -.35f + y * .4f, -.27f + x * .35f, -.35f + y * .4f));
            }
        }
        Glyph("keypad", keypad.ToArray());
        Glyph("pick-up", S(-.72f, .1f, 0, .5f, .72f, .1f, .72f, .75f, 0, 1f, -.72f, .75f, -.72f, .1f), S(0, .25f, 0, -.9f),
            S(-.35f, -.5f, 0, -.9f, .35f, -.5f));
        return table;
    }

    private static Vector2[] S(params float[] xy)
    {
        var points = new Vector2[xy.Length / 2];
        for (int i = 0; i < points.Length; i++)
        {
            points[i] = new Vector2(xy[2 * i], xy[2 * i + 1]);
        }
        return points;
    }

    // A flat square seen from above at an angle, s wide, its middle at height y.
    private static Vector2[] Diamond(float s, float y) => S(0, -.44f * s + y, .82f * s, y, 0, .44f * s + y, -.82f * s, y, 0, -.44f * s + y);

    // A wall panel in perspective whose top edge starts at height top; a doorway or frame cuts an opening of half width jamb.
    private static Vector2[][] Wall(float top, float jamb = 0) => new[]
    {
        jamb > 0
            ? S(-.65f, .82f, -.65f, top, .58f, top - .28f, .58f, .54f, jamb, .57f, jamb, top + .22f, -jamb, top + .38f, -jamb, .76f, -.65f, .82f)
            : S(-.65f, .82f, -.65f, top, .58f, top - .28f, .58f, .54f, -.65f, .82f),
        S(-.65f, .82f, -.82f, .71f, -.82f, top - .11f, .41f, top - .39f, .58f, top - .28f),
        S(-.82f, top - .11f, -.65f, top),
    };

    // A padlock whose shackle comes down on the right to the height shackle (−.05 shut, −.3 open).
    private static Vector2[][] Padlock(float shackle) => new[]
    {
        S(-.65f, -.05f, .65f, -.05f, .65f, .8f, -.65f, .8f, -.65f, -.05f),
        S(-.4f, -.05f, -.4f, -.65f, .35f, -.65f, .35f, shackle),
        S(0, .25f, 0, .5f),
    };

    private static Vector2[] Ring(float radius)
    {
        var points = new Vector2[25];
        for (int i = 0; i < points.Length; i++)
        {
            float a = MathF.Tau * i / (points.Length - 1);
            points[i] = new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius;
        }
        return points;
    }
}
