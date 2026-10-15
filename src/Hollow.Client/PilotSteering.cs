using System;
using System.Numerics;

namespace Hollow.Client;

/// <summary>A persistent mouse steering cursor, separate from camera look. No spring-back or hidden dead zone.</summary>
public sealed class PilotSteering
{
    public Vector2 Cursor { get; private set; }
    public Vector2 Output { get; private set; }
    public float ResponseSeconds { get; set; } = 0.08f;

    public void Move(Vector2 displacement)
    {
        Cursor = Vector2.Clamp(Cursor + displacement, -Vector2.One, Vector2.One);
    }

    public Vector2 Step(float seconds)
    {
        float blend = ResponseSeconds <= 0 ? 1 : 1 - MathF.Exp(-Math.Max(0, seconds) / ResponseSeconds);
        Output = Vector2.Lerp(Output, Cursor, blend);
        return Output;
    }

    public void Centre() => Cursor = Output = Vector2.Zero;

    /// <summary>Stop at neutral only when crossing it; small high-frame-rate steps must be able to leave zero.</summary>
    public static float AdjustThrottle(float previous, float delta)
    {
        float next = Math.Clamp(previous + delta, -1, 1);
        return previous * next < 0 ? 0 : next;
    }
}
