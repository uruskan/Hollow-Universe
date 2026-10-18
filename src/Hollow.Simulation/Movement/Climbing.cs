using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Simulation.Content;

namespace Hollow.Simulation.Movement;

/// <summary>A climbable span posed in the character's movement frame, with a stable tie-break identity.</summary>
public readonly record struct ClimbSurface(ulong Id, Vector3 Bottom, Vector3 Top, float Width, float Speed)
{
    public static ClimbSurface At(ulong id, CompoundClimbStats climb, Vector3 position, Quaternion rotation) =>
        new(id, position + Vector3.Transform(climb.Bottom, rotation), position + Vector3.Transform(climb.Top, rotation), climb.Width, climb.Speed);
}

public interface IClimbSource
{
    void Nearby(ulong frame, Vector3 position, List<ClimbSurface> into);
}

public readonly record struct ClimbContact(Vector3 Axis, Vector3 Toward, float Along, float Height, float Speed);

/// <summary>One shared contact query for authoritative movement and client prediction.</summary>
public sealed class Climbing(IClimbSource source)
{
    private readonly List<ClimbSurface> _near = new();

    public bool Contact(ulong frame, Vector3 feet, Vector3 forward, out ClimbContact contact)
    {
        contact = default;
        _near.Clear();
        source.Nearby(frame, feet, _near);
        float best = float.MaxValue;
        ulong bestId = ulong.MaxValue;
        foreach (ClimbSurface span in _near)
        {
            Vector3 line = span.Top - span.Bottom;
            float height = line.Length();
            if (height < 0.01f) continue;
            Vector3 axis = line / height;
            float along = Vector3.Dot(feet - span.Bottom, axis);
            if (along < -0.25f || along > height + 0.15f) continue;
            Vector3 toward = span.Bottom + axis * along - feet;
            float distance = toward.Length();
            if (distance > MathF.Max(0.6f, span.Width * 0.5f + 0.35f)) continue;
            toward = distance > 0.001f ? toward / distance : forward;
            if (Vector3.Dot(toward, forward) < 0.35f) continue;
            if (distance > best || (distance == best && span.Id >= bestId)) continue;
            best = distance;
            bestId = span.Id;
            contact = new ClimbContact(axis, toward, along, height, span.Speed);
        }
        return bestId != ulong.MaxValue;
    }
}
