using System;
using System.Numerics;
using Hollow.Simulation.Content;

namespace Hollow.Simulation.Combat;

/// <summary>
/// A battery's rocket motor (IRON_WAKE.md §6.4): it lights once the rocket has flown <see cref="Ignite"/> metres and pushes
/// it <see cref="Accel"/> m/s² along its flight for <see cref="Burn"/> seconds; gravity is <see cref="GravityBurning"/> of
/// the planet's while it burns (the launcher's rule) and <see cref="GravityAfter"/> after; drag takes <see cref="Drag"/> of
/// the speed a metre flown. The server's rockets and every client's drawing of them fly by <see cref="RocketFlight"/>.
/// </summary>
public sealed record RocketMotor(float Ignite, float Accel, float Burn, float GravityBurning, float GravityAfter, float Drag)
{
    /// <summary>A battery's motor for a round, from its launch profile.</summary>
    public static RocketMotor Of(BatteryLaunch launch, LaunchProfile profile) =>
        new(profile.Ignite, profile.Accel, profile.Burn, launch.GravityBurning, launch.Gravity, launch.Drag);
}

/// <summary>A rocket's flight under its motor (IRON_WAKE.md §6.4): the one step the server and the clients take.</summary>
public static class RocketFlight
{
    /// <summary>The step the server flies rockets in, and the clients draw them with (two a tick, as bullets).</summary>
    public const float StepSeconds = Movement.CharacterMotor.DeltaTime / 2;

    /// <summary>
    /// One step of <paramref name="h"/> seconds from a position and velocity, <paramref name="flown"/> metres and
    /// <paramref name="burnt"/> seconds of burn so far, with the planet's gravity <paramref name="g"/> m/s² toward
    /// <paramref name="down"/> (unit): the motor's push along the flight while lit, the gravity for burning or not, and
    /// quadratic drag, integrated with the velocity's average. Returns the new position and velocity and the burn used.
    /// </summary>
    public static (Vector3 Position, Vector3 Velocity, float Burnt) Step(Vector3 position, Vector3 velocity, Vector3 down, float g, RocketMotor motor,
        float flown, float burnt, float h)
    {
        bool lit = flown >= motor.Ignite && burnt < motor.Burn;
        float speed = velocity.Length();
        Vector3 along = speed > 1e-4f ? velocity / speed : Vector3.Zero;
        Vector3 accel = down * (g * (lit ? motor.GravityBurning : motor.GravityAfter)) - velocity * (motor.Drag * speed);
        if (lit)
        {
            accel += along * motor.Accel;
        }
        Vector3 next = velocity + accel * h;
        return (position + (velocity + next) * (0.5f * h), next, lit ? burnt + h : burnt);
    }
}
