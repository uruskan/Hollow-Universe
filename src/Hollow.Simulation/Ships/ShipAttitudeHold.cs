using System;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation.Movement;

namespace Hollow.Simulation.Ships;

/// <summary>Captured unattended attitude; desired rates still go through the motor's torque, fuel and contact rules.</summary>
public static class ShipAttitudeHold
{
    public static void Clear(ref ShipState state)
    {
        state.AttitudeHoldActive = false;
        state.AttitudeHoldRotation = Quaternion.Identity;
    }

    /// <summary>Occupancy is authoritative system state. These are the shared motor's additional power/input gates.</summary>
    public static bool CanHold(in ShipState state, in ShipInput input) =>
        input.Engines && state.Fuel > 0 && !state.Asleep &&
        !state.Has(ShipFlags.EngineStopped | ShipFlags.Landed | ShipFlags.Destroyed | ShipFlags.InOrbit) &&
        !input.Has(ShipButtons.AssistOff) && input.Throttle == 0 && input.Strafe == 0 && input.Vertical == 0;

    public static bool Valid(Quaternion rotation) =>
        float.IsFinite(rotation.X) && float.IsFinite(rotation.Y) && float.IsFinite(rotation.Z) && float.IsFinite(rotation.W) &&
        MathF.Abs(rotation.LengthSquared() - 1) < 0.002f;

    /// <summary>Only an inactive-to-eligible transition captures; braking never moves the target to the displaced hull.</summary>
    public static void Capture(ref ShipState state)
    {
        if (!state.AttitudeHoldActive && Valid(state.Rotation))
        {
            state.AttitudeHoldRotation = Quaternion.Normalize(state.Rotation);
            state.AttitudeHoldActive = true;
        }
    }

    public static Vector3 Rates(Quaternion current, Quaternion target, ShipDef def)
    {
        Quaternion error = Quaternion.Normalize(Quaternion.Conjugate(current) * target);
        // q and -q describe the same orientation. The half-turn tie must also choose the same direction for both.
        if (error.W < 0 || (error.W == 0 && (error.X < 0 || (error.X == 0 && (error.Y < 0 || (error.Y == 0 && error.Z < 0))))))
        {
            error = Quaternion.Negate(error);
        }
        var vector = new Vector3(error.X, error.Y, error.Z);
        float length = vector.Length();
        Vector3 angle = length < 1e-7f ? vector * 2 : vector * (float)(2 * DMath.Atan2(length, error.W) / length);
        return new Vector3(Rate(angle.X, def.Pitch), Rate(angle.Y, def.Yaw), Rate(angle.Z, def.Roll));
    }

    private static float Rate(float error, TurnAxis axis)
    {
        if (axis.MaxRate <= 0 || axis.Acceleration <= 0) return 0;
        float distance = MathF.Abs(error);
        float speed = MathF.Min(axis.MaxRate, MathF.Min(axis.Acceleration / axis.MaxRate * distance,
            MathF.Min((float)Math.Sqrt(2 * axis.Acceleration * distance), distance / CharacterMotor.DeltaTime)));
        return error < 0 ? -speed : speed;
    }
}
