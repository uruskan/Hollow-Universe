using System;
using System.Numerics;
using Hollow.Net.Bits;
using Hollow.Simulation;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;

namespace Hollow.Net.Protocol;

/// <summary>
/// Between simulation values and their wire form. The client predicts with <c>ToMoveInput(ToCommand(...))</c>, the
/// same values the server gets, so both step identically (GROUND.md §4.5).
/// </summary>
public static class NetConvert
{
    public static InputCommand ToCommand(in MoveInput input) => new()
    {
        Forward = Axis(input.Forward),
        Right = Axis(input.Right),
        Yaw = Quantize.Angle16(input.Yaw),
        Pitch = Quantize.Angle16(Math.Clamp(input.Pitch, -1.5533f, 1.5533f)),
        Buttons = input.Buttons,
    };

    /// <summary>A tick's movement and actions as one command (the actions ride along: G2).</summary>
    public static InputCommand ToCommand(in MoveInput input, in ActionInput actions)
    {
        InputCommand command = ToCommand(input);
        command.Held = (byte)Math.Min(actions.HeldSlot, ActionInput.NoSlot);
        command.Actions = actions.Buttons & (ActionButtons.Primary | ActionButtons.Secondary | ActionButtons.PrimaryPressed);
        command.ViewDelay = actions.ViewDelay;
        return command;
    }

    /// <summary>The actions of a command (untrusted: a slot past the belt is "none").</summary>
    public static ActionInput ToActions(in InputCommand command) => new()
    {
        HeldSlot = command.Held < 6 ? command.Held : ActionInput.NoSlot,
        Buttons = command.Actions & (ActionButtons.Primary | ActionButtons.Secondary | ActionButtons.PrimaryPressed),
        ViewDelay = command.ViewDelay,
    };

    public static MoveInput ToMoveInput(in InputCommand command) => new()
    {
        Forward = Math.Clamp(command.Forward / 127f, -1f, 1f),
        Right = Math.Clamp(command.Right / 127f, -1f, 1f),
        Yaw = Quantize.FromAngle16(command.Yaw),
        Pitch = Quantize.FromAngle16(command.Pitch),
        Buttons = command.Buttons & (MoveButtons.Jump | MoveButtons.JumpPressed | MoveButtons.Crouch | MoveButtons.Sprint),
    };

    /// <summary>The exact state, for the player's own reconciliation.</summary>
    public static OwnState ToOwn(in CharacterState s) => new()
    {
        PositionX = s.Position.X,
        PositionY = s.Position.Y,
        PositionZ = s.Position.Z,
        VelocityX = s.Velocity.X,
        VelocityY = s.Velocity.Y,
        VelocityZ = s.Velocity.Z,
        Stance = s.Stance,
        Health = s.Health,
        Flags = s.Flags,
    };

    public static CharacterState FromOwn(in OwnState o) => new()
    {
        Position = new Vector3(o.PositionX, o.PositionY, o.PositionZ),
        Velocity = new Vector3(o.VelocityX, o.VelocityY, o.VelocityZ),
        Stance = o.Stance,
        Health = o.Health,
        Flags = o.Flags,
    };

    /// <summary>A character as others see it; the view angles are those of its last input.</summary>
    public static CharacterNetState ToNet(in CharacterState s, ushort yaw, ushort pitch) => new()
    {
        X = Quantize.Fixed(s.Position.X, Quantize.PositionScale),
        Y = Quantize.Fixed(s.Position.Y, Quantize.PositionScale),
        Z = Quantize.Fixed(s.Position.Z, Quantize.PositionScale),
        Yaw = yaw,
        Pitch = pitch,
        Flags = s.Flags,
        Stance = s.Stance,
        Health = s.Health,
    };

    public static Vector3 PositionOf(in CharacterNetState s) => new(
        Quantize.FromFixed(s.X, Quantize.PositionScale),
        Quantize.FromFixed(s.Y, Quantize.PositionScale),
        Quantize.FromFixed(s.Z, Quantize.PositionScale));

    public static ZoneInfo ToInfo(ZoneSpec spec) => new()
    {
        SystemAddress = spec.SystemAddress,
        BodyPath = spec.BodyPath,
        Radius = spec.Radius,
        SurfaceGravity = spec.SurfaceGravity,
        SurfaceSeed = spec.SurfaceSeed,
        LatitudeDegrees = spec.LatitudeDegrees,
        LongitudeDegrees = spec.LongitudeDegrees,
        StandIn = spec.StandIn,
        RotationTimeScale = (float)spec.RotationTimeScale,
        AirDensity = (float)spec.AirDensity,
        AirScaleHeight = (float)spec.AirScaleHeight,
    };

    public static ZoneSpec FromInfo(in ZoneInfo info) => new()
    {
        SystemAddress = info.SystemAddress,
        BodyPath = info.BodyPath,
        Radius = info.Radius,
        SurfaceGravity = info.SurfaceGravity,
        SurfaceSeed = info.SurfaceSeed,
        LatitudeDegrees = info.LatitudeDegrees,
        LongitudeDegrees = info.LongitudeDegrees,
        StandIn = info.StandIn,
        RotationTimeScale = info.RotationTimeScale > 0 && float.IsFinite(info.RotationTimeScale) ? info.RotationTimeScale : 1,
        AirDensity = info.AirDensity,
        AirScaleHeight = info.AirScaleHeight,
    };

    private static sbyte Axis(float value) => (sbyte)Math.Round(Math.Clamp(value, -1f, 1f) * 127f, MidpointRounding.ToEven);
}
