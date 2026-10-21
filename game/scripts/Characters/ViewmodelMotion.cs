using System;
using Godot;

namespace HollowUniverse.Characters;

/// <summary>
/// The first-person swing and reload as key poses (GROUND.md §4.8): each has an anticipation, an impact and a
/// recovery. Poses are rotations (pitch, yaw, roll; radians) about a pivot and an offset (metres), in camera space;
/// each key says how the motion eases into it.
/// </summary>
public static class ViewmodelMotion
{
    public enum Ease
    {
        Linear,
        Smooth,
        In,
        Out,
    }

    public readonly record struct Key(float At, Vector3 Turn, Vector3 Offset, Ease Ease = Ease.Smooth);

    /// <summary>The pose at <paramref name="t"/> (0–1) between the keys around it.</summary>
    public static (Vector3 Turn, Vector3 Offset) Sample(ReadOnlySpan<Key> keys, float t)
    {
        if (t <= keys[0].At)
        {
            return (keys[0].Turn, keys[0].Offset);
        }
        for (int i = 1; i < keys.Length; i++)
        {
            if (t <= keys[i].At)
            {
                Key a = keys[i - 1], b = keys[i];
                float u = Math.Clamp((t - a.At) / Math.Max(1e-4f, b.At - a.At), 0f, 1f);
                u = b.Ease switch
                {
                    Ease.Smooth => u * u * (3f - 2f * u),
                    Ease.In => u * u * u,
                    Ease.Out => 1f - (1f - u) * (1f - u),
                    _ => u,
                };
                return (a.Turn.Lerp(b.Turn, u), a.Offset.Lerp(b.Offset, u));
            }
        }
        return (keys[^1].Turn, keys[^1].Offset);
    }

    /// <summary>
    /// A swing by the handle, turning about the hand: raise it up and back (anticipation), hang there for a beat,
    /// strike forward, down and across to the centre (fast, so the head arrives at the strike time), jolt on the
    /// impact, then follow through and come back. The hand stays in view throughout. <paramref name="strike"/> is the
    /// impact's share of the swing (items.json: strike / swing).
    /// </summary>
    public static (Vector3 Turn, Vector3 Offset) Swing(float t, float strike, float seconds)
    {
        float s = Math.Clamp(strike, 0.2f, 0.6f);
        Span<Key> keys = stackalloc Key[]
        {
            new(0f, Vector3.Zero, Vector3.Zero),
            new(s * 0.72f, new Vector3(0.6f, -0.25f, -0.15f), new Vector3(0f, 0.06f, 0.05f), Ease.Smooth),
            new(s * 0.86f, new Vector3(0.68f, -0.28f, -0.18f), new Vector3(0f, 0.065f, 0.055f), Ease.Out),
            new(s, new Vector3(-1.1f, 0.35f, 0.3f), new Vector3(-0.08f, -0.05f, -0.12f), Ease.In),
            new(s + 0.05f, new Vector3(-0.95f, 0.3f, 0.26f), new Vector3(-0.07f, -0.045f, -0.1f), Ease.Out),
            new(s + 0.2f, new Vector3(-0.8f, 0.25f, 0.2f), new Vector3(-0.06f, -0.04f, -0.08f), Ease.Smooth),
            new(1f, Vector3.Zero, Vector3.Zero, Ease.Smooth),
        };
        (Vector3 turn, Vector3 offset) = Sample(keys, t);
        // Contact response belongs to the accepted hit, never to the clock crossing a strike time on a miss.
        return (turn, offset);
    }

    /// <summary>
    /// A reload: tilt the gun in (anticipation), magazine out and a new one in (the gun jolts as it seats), then the
    /// slide or the charging handle is worked (another jolt), and the gun comes back.
    /// </summary>
    public static (Vector3 Turn, Vector3 Offset) Reload(float t)
    {
        Span<Key> keys = stackalloc Key[]
        {
            new(0f, Vector3.Zero, Vector3.Zero),
            new(0.12f, new Vector3(0.15f, 0.35f, 0.6f), new Vector3(-0.04f, -0.03f, 0.03f), Ease.Smooth),
            new(0.42f, new Vector3(0.19f, 0.37f, 0.63f), new Vector3(-0.04f, -0.035f, 0.03f), Ease.Smooth),
            new(0.58f, new Vector3(0.12f, 0.35f, 0.6f), new Vector3(-0.04f, -0.04f, 0.03f), Ease.Smooth),
            new(0.62f, new Vector3(0.26f, 0.35f, 0.58f), new Vector3(-0.04f, -0.015f, 0.03f), Ease.In),
            new(0.68f, new Vector3(0.15f, 0.35f, 0.6f), new Vector3(-0.04f, -0.03f, 0.03f), Ease.Out),
            new(0.76f, new Vector3(0.05f, 0.22f, 0.22f), new Vector3(-0.02f, -0.02f, 0.02f), Ease.Smooth),
            new(0.8f, new Vector3(-0.04f, 0.2f, 0.12f), new Vector3(-0.02f, -0.03f, 0.04f), Ease.In),
            new(0.85f, new Vector3(0.04f, 0.18f, 0.12f), new Vector3(-0.015f, -0.02f, 0.02f), Ease.Out),
            new(1f, Vector3.Zero, Vector3.Zero, Ease.Smooth),
        };
        return Sample(keys, t);
    }

    /// <summary>
    /// Where the support hand is during a reload, as a blend from its grip (0) towards the magazine well (1), away
    /// below the gun (2) and to the slide or charging handle (3): a weight for each of the four.
    /// </summary>
    public static Vector4 ReloadHand(float t)
    {
        Span<Key> keys = stackalloc Key[]
        {
            // Turn holds the weights of the well, away and slide; the grip gets what is left.
            new(0f, Vector3.Zero, Vector3.Zero),
            new(0.14f, Vector3.Zero, Vector3.Zero),
            new(0.28f, new Vector3(1f, 0f, 0f), Vector3.Zero, Ease.Smooth),
            new(0.42f, new Vector3(0f, 1f, 0f), Vector3.Zero, Ease.Smooth),
            new(0.56f, new Vector3(0.85f, 0.15f, 0f), Vector3.Zero, Ease.Smooth),
            new(0.62f, new Vector3(1f, 0f, 0f), Vector3.Zero, Ease.In),
            new(0.72f, new Vector3(0f, 0f, 1f), Vector3.Zero, Ease.Smooth),
            new(0.8f, new Vector3(0f, 0f, 1.25f), Vector3.Zero, Ease.In),
            new(0.92f, Vector3.Zero, Vector3.Zero, Ease.Smooth),
            new(1f, Vector3.Zero, Vector3.Zero),
        };
        Vector3 w = Sample(keys, t).Turn;
        return new Vector4(Math.Max(0f, 1f - w.X - w.Y - Math.Min(1f, w.Z)), w.X, w.Y, w.Z);
    }

    /// <summary>The under-barrel support palm turns sideways to wrap a vertical magazine, then returns to its grip.</summary>
    public static Basis ReloadGrip(Basis support, float phase)
    {
        float handOffGrip = Math.Clamp(1f - ReloadHand(phase).X, 0, 1);
        return new Basis(support.GetRotationQuaternion().Slerp(HandFrame.PowerGrip.GetRotationQuaternion(), handOffGrip));
    }
}
