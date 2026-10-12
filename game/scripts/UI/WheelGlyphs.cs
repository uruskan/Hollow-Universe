using System;
using Godot;

namespace HollowUniverse.UI;

/// <summary>
/// The wheels' outline glyphs (RustMVP's own perspective outlines): building pieces 0–19, hammer actions 100–105
/// (upgrade 100–103, rotate 104, demolish 105) and the interaction glyphs 200–204. Each is a set of polylines in units
/// where the glyph spans about −1 to 1; the caller scales, places and strokes them.
/// </summary>
public static class WheelGlyphs
{
    public static void Draw(int glyph, Action<Vector2[]> stroke)
    {
        void S(params float[] xy)
        {
            var points = new Vector2[xy.Length / 2];
            for (int i = 0; i < points.Length; i++)
            {
                points[i] = new Vector2(xy[2 * i], xy[2 * i + 1]);
            }
            stroke(points);
        }
        void Diamond(float s, float y) => S(0, -.44f * s + y, .82f * s, y, 0, .44f * s + y, -.82f * s, y, 0, -.44f * s + y);

        if (glyph >= 200)
        {
            if (glyph == 200)
            {
                S(-.65f, .8f, -.65f, -.8f, .6f, -.8f, .6f, .8f);
                S(-.65f, .8f, .2f, .55f, .2f, -.55f, -.65f, -.8f);
                S(-.03f, .06f, .08f, .06f);
            }
            else if (glyph == 204)
            {
                S(-.72f, .1f, 0, .5f, .72f, .1f, .72f, .75f, 0, 1f, -.72f, .75f, -.72f, .1f);
                S(0, .25f, 0, -.9f);
                S(-.35f, -.5f, 0, -.9f, .35f, -.5f);
            }
            else if (glyph == 202)
            {
                S(-.62f, -.8f, .62f, -.8f, .62f, .85f, -.62f, .85f, -.62f, -.8f);
                for (int x = 0; x < 3; x++)
                {
                    for (int y = 0; y < 3; y++)
                    {
                        S(-.35f + x * .35f, -.35f + y * .4f, -.27f + x * .35f, -.35f + y * .4f);
                    }
                }
            }
            else
            {
                S(-.65f, -.05f, .65f, -.05f, .65f, .8f, -.65f, .8f, -.65f, -.05f);
                S(-.4f, -.05f, -.4f, -.65f, .35f, -.65f, .35f, glyph == 203 ? -.3f : -.05f);
                S(0, .25f, 0, .5f);
            }
            return;
        }
        if (glyph >= 100)
        {
            if (glyph == 104)
            {
                S(-.65f, .65f, -.65f, -.55f, .55f, -.55f, .8f, -.25f);
                S(.8f, -.72f, .8f, -.25f, .3f, -.25f);
                S(.65f, .65f, -.25f, .65f);
            }
            else if (glyph == 105)
            {
                S(-.7f, -.7f, .7f, .7f);
                S(.7f, -.7f, -.7f, .7f);
            }
            else
            {
                Diamond(.9f, -.18f);
                Diamond(.9f, .28f);
                S(-.74f, -.18f, -.74f, .28f);
                S(.74f, -.18f, .74f, .28f);
                S(0, .22f, 0, .68f);
            }
            return;
        }
        switch (glyph)
        {
            case 0:
                Diamond(.9f, -.38f);
                S(-.74f, -.38f, -.74f, .5f, 0, .88f, .74f, .5f, .74f, -.38f);
                S(0, .02f, 0, .88f);
                return;
            case 1:
                S(-.82f, .18f, 0, -.72f, .82f, .18f, -.82f, .18f, -.82f, .63f, .82f, .63f, .82f, .18f);
                return;
            case 2 or 10:
                Diamond(1f, -.08f);
                S(-.82f, -.08f, -.82f, .16f, 0, .6f, .82f, .16f, .82f, -.08f);
                S(0, .36f, 0, .6f);
                if (glyph == 10)
                {
                    Diamond(.55f, -.08f);
                }
                return;
            case 3 or 11:
                S(-.82f, .28f, 0, -.55f, .82f, .28f, -.82f, .28f, -.82f, .48f, .82f, .48f, .82f, .28f);
                if (glyph == 11)
                {
                    S(-.38f, .12f, 0, -.25f, .38f, .12f, -.38f, .12f);
                }
                return;
            case >= 4 and <= 9:
            {
                float top = glyph == 5 ? -.12f : glyph == 6 ? .28f : -.65f;
                if (glyph is 8 or 9)
                {
                    float jamb = glyph == 8 ? .32f : .47f;
                    S(-.65f, .82f, -.65f, top, .58f, top - .28f, .58f, .54f, jamb, .57f, jamb, top + .22f, -jamb, top + .38f, -jamb, .76f, -.65f, .82f);
                }
                else
                {
                    S(-.65f, .82f, -.65f, top, .58f, top - .28f, .58f, .54f, -.65f, .82f);
                }
                S(-.65f, .82f, -.82f, .71f, -.82f, top - .11f, .41f, top - .39f, .58f, top - .28f);
                S(-.82f, top - .11f, -.65f, top);
                if (glyph == 7)
                {
                    S(-.35f, .35f, -.35f, -.22f, .28f, -.37f, .28f, .2f, -.35f, .35f);
                }
                return;
            }
            case 12:
                S(-.82f, .65f, -.82f, .3f, -.3f, .3f, -.3f, -.6f, .75f, -.8f, .75f, -.3f, .05f, -.1f, .05f, .7f, -.82f, .9f, -.82f, .65f);
                S(-.3f, .3f, .05f, .18f);
                S(-.3f, -.1f, .05f, -.2f);
                return;
            case 13:
                S(-.8f, .8f, -.8f, -.6f, .7f, -.85f, .7f, .55f, .2f, .65f, .2f, -.25f, -.3f, -.15f, -.3f, .7f, -.8f, .8f);
                for (int j = 0; j < 3; j++)
                {
                    S(-.8f, .45f - j * .35f, -.3f, .35f - j * .35f);
                    S(.2f, .4f - j * .3f, .7f, .3f - j * .3f);
                }
                return;
            case 14 or 15:
                if (glyph == 14)
                {
                    S(-.8f, .6f, -.8f, -.55f, .7f, -.8f, .7f, .35f);
                }
                else
                {
                    S(-.85f, .7f, 0, -.85f, .85f, .7f, -.85f, .7f);
                }
                S(-.55f, .5f, .48f, .28f, .48f, -.38f, -.25f, -.2f, -.25f, .12f, .1f, .04f);
                S(.1f, .65f, .1f, -.7f);
                return;
            case 16:
                S(-.85f, .65f, .5f, -.65f, .85f, -.4f, -.5f, .88f, -.85f, .65f);
                S(.85f, -.4f, .85f, .58f, -.5f, .88f);
                return;
            case 17:
                S(-.85f, .75f, -.85f, .4f, -.3f, .4f, -.3f, -.1f, .25f, -.1f, .25f, -.65f, .85f, -.65f);
                S(-.85f, .75f, -.5f, .9f, -.5f, .55f, .05f, .55f, .05f, .05f, .6f, .05f, .6f, -.5f, .95f, -.5f);
                return;
            case 18:
                S(-.85f, .25f, 0, -.7f, .85f, .25f, .4f, .65f, -.45f, .65f, -.85f, .25f);
                S(0, -.7f, .4f, -.3f, .85f, .25f);
                S(.4f, -.3f, -.45f, .65f);
                return;
            case 19:
                S(-.85f, .65f, 0, -.85f, .85f, .65f, -.85f, .65f);
                S(0, -.85f, 0, .65f);
                return;
        }
    }
}
