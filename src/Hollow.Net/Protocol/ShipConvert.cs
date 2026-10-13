using System;
using System.Numerics;
using Hollow.Net.Bits;
using Hollow.Simulation.Ships;

namespace Hollow.Net.Protocol;

/// <summary>
/// Between ships' simulation values and their wire form (G6). A pilot's client predicts with
/// <c>ToShipInput(ToControls(...))</c>, the values the server gets, so both step identically.
/// </summary>
public static class ShipConvert
{
    public static ShipControls ToControls(in ShipInput input) => new()
    {
        Pitch = Axis(input.Pitch),
        Yaw = Axis(input.Yaw),
        Roll = Axis(input.Roll),
        Throttle = Axis(input.Throttle),
        Strafe = Axis(input.Strafe),
        Vertical = Axis(input.Vertical),
        Buttons = input.Buttons & (ShipButtons.Boost | ShipButtons.AssistOff),
    };

    /// <summary>A pilot's controls from a command (untrusted: clamped, unknown buttons dropped); none: hands off.</summary>
    public static ShipInput ToShipInput(in InputCommand command) =>
        command.Ship is { Length: > 0 } ship ? ToShipInput(ship[0]) : default;

    public static ShipInput ToShipInput(in ShipControls c) => new()
    {
        Pitch = Value(c.Pitch),
        Yaw = Value(c.Yaw),
        Roll = Value(c.Roll),
        Throttle = Value(c.Throttle),
        Strafe = Value(c.Strafe),
        Vertical = Value(c.Vertical),
        Buttons = c.Buttons & (ShipButtons.Boost | ShipButtons.AssistOff),
    };

    /// <summary>A ship's state exactly, for its pilot's reconciliation.</summary>
    public static OwnShipState ToOwn(in ShipState s, uint netId, uint kind) => new()
    {
        NetId = netId,
        Kind = kind,
        PositionX = s.Position.X,
        PositionY = s.Position.Y,
        PositionZ = s.Position.Z,
        RotationX = s.Rotation.X,
        RotationY = s.Rotation.Y,
        RotationZ = s.Rotation.Z,
        RotationW = s.Rotation.W,
        VelocityX = s.Velocity.X,
        VelocityY = s.Velocity.Y,
        VelocityZ = s.Velocity.Z,
        SpinX = s.AngularVelocity.X,
        SpinY = s.AngularVelocity.Y,
        SpinZ = s.AngularVelocity.Z,
        Health = s.Health,
        Fuel = s.Fuel,
        BoostTicks = s.BoostTicks,
        CooldownTicks = s.CooldownTicks,
        RestTicks = s.RestTicks,
        Flags = s.Flags,
    };

    /// <summary>The state an own-ship message carries (untrusted floats: a rotation that isn't one becomes level).</summary>
    public static ShipState FromOwn(in OwnShipState o)
    {
        var rotation = new Quaternion(o.RotationX, o.RotationY, o.RotationZ, o.RotationW);
        if (!(MathF.Abs(rotation.Length() - 1) < 1e-3f))
        {
            rotation = Quaternion.Identity;
        }
        return new ShipState
        {
            Position = new Vector3(o.PositionX, o.PositionY, o.PositionZ),
            Rotation = rotation,
            Velocity = new Vector3(o.VelocityX, o.VelocityY, o.VelocityZ),
            AngularVelocity = new Vector3(o.SpinX, o.SpinY, o.SpinZ),
            Health = o.Health,
            Fuel = o.Fuel,
            BoostTicks = o.BoostTicks,
            CooldownTicks = o.CooldownTicks,
            RestTicks = o.RestTicks,
            Flags = o.Flags,
        };
    }

    /// <summary>A ship as others see it.</summary>
    public static ShipNetState ToNet(Ship ship, bool mine)
    {
        ShipState s = ship.State;
        (float yaw, float pitch, float roll) = Angles(s.Rotation);
        ShipNetFlags flags = (ship.Piloted || ship.Autopilot ? ShipNetFlags.Piloted : 0) | (ship.HatchOpen ? ShipNetFlags.HatchOpen : 0)
            | (ship.Locked ? ShipNetFlags.Locked : 0) | (mine ? ShipNetFlags.Mine : 0)
            | (s.Has(ShipFlags.Landed) ? ShipNetFlags.Landed : 0) | (s.Has(ShipFlags.Destroyed) ? ShipNetFlags.Destroyed : 0)
            | (s.Has(ShipFlags.InOrbit) ? ShipNetFlags.InOrbit : 0) | (s.Has(ShipFlags.Boosting) ? ShipNetFlags.Boosting : 0)
            | (s.Has(ShipFlags.EnginesOn) ? ShipNetFlags.EnginesOn : 0) | (s.Has(ShipFlags.AssistOff) ? ShipNetFlags.AssistOff : 0)
            | (s.Has(ShipFlags.Wet) ? ShipNetFlags.Wet : 0) | (s.Asleep ? ShipNetFlags.Asleep : 0);
        return new ShipNetState
        {
            X = Quantize.Fixed(s.Position.X, Quantize.PositionScale),
            Y = Quantize.Fixed(s.Position.Y, Quantize.PositionScale),
            Z = Quantize.Fixed(s.Position.Z, Quantize.PositionScale),
            Yaw = Quantize.Angle16(yaw),
            Pitch = Quantize.Angle16(pitch),
            Roll = Quantize.Angle16(roll),
            Flags = flags,
            Hatch = (byte)Math.Clamp(ship.HatchProgress, 0, 31),
            ThrustX = Thrust(s.Thrust.X),
            ThrustY = Thrust(s.Thrust.Y),
            ThrustZ = Thrust(s.Thrust.Z),
            Health = (byte)Math.Clamp(MathF.Ceiling(s.Health / ship.Def.Health * 255), 0, 255),
            Kind = ship.Def.Id,
        };
    }

    /// <summary>The centre of mass a ship's state puts on the wire's grid.</summary>
    public static Vector3 PositionOf(in ShipNetState s) => new(
        Quantize.FromFixed(s.X, Quantize.PositionScale), Quantize.FromFixed(s.Y, Quantize.PositionScale), Quantize.FromFixed(s.Z, Quantize.PositionScale));

    public static Quaternion RotationOf(in ShipNetState s) =>
        Rotation(Quantize.FromAngle16(s.Yaw), Quantize.FromAngle16(s.Pitch), Quantize.FromAngle16(s.Roll));

    /// <summary>The thrust fractions a state carries (each axis's thrust; the main engine's boost goes over 1).</summary>
    public static Vector3 ThrustOf(in ShipNetState s) => new(s.ThrustX / 63.5f, s.ThrustY / 63.5f, s.ThrustZ / 63.5f);

    /// <summary>
    /// A rotation as yaw (about the zone's +Y), pitch (then about its own x; + nose down) and roll (then about its own z;
    /// + right wing down): <c>Ry(yaw) Rx(pitch) Rz(roll)</c>.
    /// </summary>
    public static (float Yaw, float Pitch, float Roll) Angles(Quaternion q)
    {
        Vector3 f = Vector3.Transform(Vector3.UnitZ, q);
        Vector3 r = Vector3.Transform(Vector3.UnitX, q);
        Vector3 u = Vector3.Transform(Vector3.UnitY, q);
        float pitch = MathF.Asin(Math.Clamp(-f.Y, -1f, 1f));
        if (MathF.Abs(f.Y) > 0.99999f)
        {
            // Straight up or down: the yaw takes the roll.
            return (MathF.Atan2(-r.Z, r.X), pitch, 0);
        }
        return (MathF.Atan2(f.X, f.Z), pitch, MathF.Atan2(r.Y, u.Y));
    }

    public static Quaternion Rotation(float yaw, float pitch, float roll) =>
        Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw) * Quaternion.CreateFromAxisAngle(Vector3.UnitX, pitch) *
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, roll));

    private static sbyte Axis(float value) => (sbyte)Math.Round(Math.Clamp(value, -1f, 1f) * 127f, MidpointRounding.ToEven);

    private static float Value(sbyte value) => Math.Clamp(value / 127f, -1f, 1f);

    private static sbyte Thrust(float fraction) => (sbyte)Math.Clamp(Math.Round(fraction * 63.5f, MidpointRounding.ToEven), -127, 127);
}
