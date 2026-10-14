using System;
using System.Numerics;

namespace Hollow.Net.Protocol;

public partial struct NaturalNet
{
    /// <summary>The step of the last hit's point (metres): ±3.8 m from the object's base.</summary>
    public const float HitStep = 0.03f;

    /// <summary>A last hit this many ticks old or more is old news: its effects aren't played.</summary>
    public const byte OldHit = byte.MaxValue;

    /// <summary>Where the last hit met the drawn surface, from the object's base.</summary>
    public readonly Vector3 HitOffset => new Vector3(HitX, HitY, HitZ) * HitStep;

    /// <summary>Sets <see cref="HitX"/>, <see cref="HitY"/>, <see cref="HitZ"/> from an offset in metres.</summary>
    public void SetHitOffset(Vector3 offset)
    {
        HitX = Step(offset.X);
        HitY = Step(offset.Y);
        HitZ = Step(offset.Z);
    }

    private static sbyte Step(float metres) => (sbyte)Math.Clamp(Math.Round(metres / HitStep), -127, 127);
}
