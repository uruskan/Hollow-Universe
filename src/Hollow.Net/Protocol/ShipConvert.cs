using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hollow.Core;
using Hollow.Net.Bits;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Targets;

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
    public static OwnShipState ToOwn(in ShipState s, uint netId, uint kind, ShipInput cruise = default) => new()
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
        FuelReserve = s.FuelReserve,
        BoostTicks = s.BoostTicks,
        CooldownTicks = s.CooldownTicks,
        RestTicks = s.RestTicks,
        Flags = s.Flags,
        CruiseThrottle = cruise.Throttle,
        CruiseStrafe = cruise.Strafe,
        CruiseVertical = cruise.Vertical,
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
            FuelReserve = o.FuelReserve,
            BoostTicks = o.BoostTicks,
            CooldownTicks = o.CooldownTicks,
            RestTicks = o.RestTicks,
            Flags = o.Flags,
        };
    }

    /// <summary>
    /// A ship as one viewer sees it: its state, what they are told of it (<paramref name="viewer"/>), whether the server
    /// has it at rest (<paramref name="still"/>, its one rule) and slow and low enough for its slow doors
    /// (<paramref name="slow"/>).
    /// </summary>
    public static ShipNetState ToNet(Ship ship, ViewerFlags viewer, bool still, bool slow = false)
    {
        ShipState s = ship.State;
        (byte largest, short a, short b, short c) = Quantize.SmallestThree(s.Rotation);
        ShipNetFlags flags = (ship.Piloted || ship.Autopilot ? ShipNetFlags.Piloted : 0) | (slow ? ShipNetFlags.Slow : 0)
            | (s.Has(ShipFlags.Landed) ? ShipNetFlags.Landed : 0) | (s.Has(ShipFlags.Destroyed) ? ShipNetFlags.Destroyed : 0)
            | (s.Has(ShipFlags.InOrbit) ? ShipNetFlags.InOrbit : 0) | (s.Has(ShipFlags.Boosting) ? ShipNetFlags.Boosting : 0)
            | (s.Has(ShipFlags.EnginesOn) ? ShipNetFlags.EnginesOn : 0) | (s.Has(ShipFlags.AssistOff) ? ShipNetFlags.AssistOff : 0)
            | (s.Has(ShipFlags.Wet) ? ShipNetFlags.Wet : 0) | (s.Asleep ? ShipNetFlags.Asleep : 0) | (still ? ShipNetFlags.Still : 0);
        return new ShipNetState
        {
            X = Quantize.Fixed(s.Position.X, Quantize.PositionScale),
            Y = Quantize.Fixed(s.Position.Y, Quantize.PositionScale),
            Z = Quantize.Fixed(s.Position.Z, Quantize.PositionScale),
            TurnLargest = largest,
            TurnA = a,
            TurnB = b,
            TurnC = c,
            Flags = flags,
            Viewer = viewer,
            ThrustX = Thrust(s.Thrust.X),
            ThrustY = Thrust(s.Thrust.Y),
            ThrustZ = Thrust(s.Thrust.Z),
            Health = (byte)Math.Clamp(MathF.Ceiling(s.Health / ship.Def.Health * 255), 0, 255),
            Kind = ship.Def.Id,
            Body = ship.ParkedBody,
        };
    }

    /// <summary>
    /// A ship's details (IRON_WAKE.md §5, <see cref="ShipDetails"/>): its moving parts away from rest that no door drives,
    /// its doors' motion, its seats taken and its parts' health below their most. <paramref name="seat"/> gives the net ID
    /// of each occupant.
    /// </summary>
    public static ShipDetails Details(Ship ship, uint netId, Func<EntityId, ulong> seat)
    {
        ShipDef def = ship.Def;
        var parts = new List<ShipPartNet>();
        if (def.Interior != null)
        {
            float[] rest = def.Interior.Rest;
            for (int i = 0; i < ship.Parts.Length && i < rest.Length; i++)
            {
                if (ship.Parts[i] != rest[i] && !DoorDriven(def, i))
                {
                    parts.Add(new ShipPartNet { Part = (uint)i, Bits = BitConverter.SingleToUInt32Bits(ship.Parts[i]) });
                }
            }
        }
        var doors = new ShipDoorNet[ship.Doors.Length];
        for (int i = 0; i < doors.Length; i++)
        {
            ShipDoorState d = ship.Doors[i];
            float max = def.Doors[i].Health?.Max ?? 0;
            doors[i] = new ShipDoorNet
            {
                From = BitConverter.SingleToUInt32Bits(d.From),
                Moving = d.Moving,
                Waiting = d.Waiting,
                Since = (uint)d.Since,
                Health = max > 0 ? Share(d.Health, max) : (byte)255,
            };
        }
        var seats = new List<ShipSeatNet>();
        for (int i = 0; i < ship.Occupants.Length; i++)
        {
            if (!ship.Occupants[i].IsNone)
            {
                seats.Add(new ShipSeatNet { Seat = (byte)i, Occupant = seat(ship.Occupants[i]) });
            }
        }
        var health = new List<ShipPartHealthNet>();
        for (int i = 0; i < ship.PartHealth.Length; i++)
        {
            float max = def.Parts[i].Health?.Max ?? 0;
            if (max > 0 && ship.PartHealth[i] < max)
            {
                health.Add(new ShipPartHealthNet { Part = (byte)i, Health = Share(ship.PartHealth[i], max) });
            }
        }
        return new ShipDetails { Ship = netId, Parts = parts.ToArray(), Doors = doors, Seats = seats.ToArray(), Health = health.ToArray() };
    }

    /// <summary>A door's motion as the wire carries it.</summary>
    public static ShipDoorState DoorOf(in ShipDoorNet d, float max) => new()
    {
        From = Math.Clamp(BitConverter.UInt32BitsToSingle(d.From) is float f && float.IsFinite(f) ? f : 0, 0, 1),
        Moving = (sbyte)Math.Sign(d.Moving),
        Waiting = (sbyte)Math.Sign(d.Waiting),
        Since = d.Since,
        Health = max * d.Health / 255f,
    };

    /// <summary>Health as a share of its most on the wire: 0 only when gone, 255 only when whole.</summary>
    public static byte Share(float health, float max) =>
        health <= 0 ? (byte)0 : health >= max ? (byte)255 : (byte)Math.Clamp((int)MathF.Ceiling(health / max * 254), 1, 254);

    /// <summary>An interior part a door moves (its state follows the door's progress on every machine).</summary>
    public static bool DoorDriven(ShipDef def, int part) => def.DoorOfPart(part) >= 0;

    /// <summary>The centre of mass a ship's state puts on the wire's grid.</summary>
    public static Vector3 PositionOf(in ShipNetState s) => new(
        Quantize.FromFixed(s.X, Quantize.PositionScale), Quantize.FromFixed(s.Y, Quantize.PositionScale), Quantize.FromFixed(s.Z, Quantize.PositionScale));

    public static Quaternion RotationOf(in ShipNetState s) => Quantize.FromSmallestThree(s.TurnLargest, s.TurnA, s.TurnB, s.TurnC);

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
