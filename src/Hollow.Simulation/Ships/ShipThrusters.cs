using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Hollow.Core;
using Hollow.Simulation.Content;
using Hollow.Simulation.Movement;

namespace Hollow.Simulation.Ships;

/// <summary>
/// Thrusters on every axis (GROUND.md §6.6), the Tin Finch's and the Iron Wake's drive and the default: the main engine
/// and retro along the nose, lateral nozzles, lift and down. Flight assist (on by default) holds the velocity the
/// throttle, strafe and up/down ask for and the rotation rates the stick asks for, within the thrusters' limits, and so
/// hovers against gravity; with it off the controls are thrust and torque (Newtonian, but the air still drags).
/// </summary>
public sealed class ShipThrusters : IShipDrive
{
    public const string Key = "thrusters";

    public static readonly ShipThrusters Instance = new();

    public string Kind => Key;

    public bool IdlesOnSkids => true;

    // Its block names its kind and nothing else: the numbers are the ship's thrust, speed, boost and rates.
    internal static IShipDrive Read(JsonFields f, JsonElement block, string where)
    {
        f.OnlyKnown(block, where, "kind");
        return Instance;
    }

    public void Advance(ref ShipState s, in ShipDriveContext c, in ShipInput input, ShipDef def)
    {
    }

    public void Command(in ShipDriveContext c, ref ShipState s, in ShipInput input, ShipDef def, out Vector3 thrust, out Vector3 turn)
    {
        const float dt = CharacterMotor.DeltaTime;
        float m = def.Mass;
        Quaternion q = c.Rotation;
        Quaternion qi = c.Inverse;
        Vector3 vLocal = c.VelocityLocal;
        bool boosting = c.Boosting;
        float boost = boosting ? def.BoostThrust : 1;
        float capForward = def.MainThrust * boost / m, capBack = def.RetroThrust / m, capSide = def.LateralThrust / m;
        float capUp = def.LiftThrust / m, capDown = def.DownThrust / m;
        if (c.Assist)
        {
            var target = new Vector3(
                -input.Strafe * def.LateralSpeed,
                input.Vertical >= 0 ? input.Vertical * def.UpSpeed : input.Vertical * def.DownSpeed,
                boosting ? def.BoostSpeed : input.Throttle >= 0 ? input.Throttle * def.ForwardSpeed : input.Throttle * def.ReverseSpeed);
            if (!boosting && MathF.Abs(input.Throttle) < 0.05f && c.OverGround < ShipMotor.LandingAssistHeight)
            {
                // Hovering down near the ground: flight assist eases the descent (it never stops one at full throttle).
                Vector3 world = Vector3.Transform(target, q);
                float descent = def.LandingDescent + MathF.Max(c.OverGround, 0) * def.LandingDescentPerMetre;
                float along = Vector3.Dot(world, c.Up);
                if (along < -descent)
                {
                    target = Vector3.Transform(world + c.Up * (-descent - along), qi);
                }
            }
            Vector3 need = (target - vLocal) / dt - c.GravityLocal - ShipMotor.Drag(vLocal, c.Density, def) -
                ShipMotor.Buoyancy(qi, c.Up, c.Submerged, c.World.Gravity, def);
            thrust = new Vector3(Math.Clamp(need.X, -capSide, capSide), Math.Clamp(need.Y, -capDown, capUp), Math.Clamp(need.Z, -capBack, capForward));
            var rates = new Vector3(-input.Pitch * def.Pitch.MaxRate, -input.Yaw * def.Yaw.MaxRate, input.Roll * def.Roll.MaxRate);
            if (s.AttitudeHoldActive)
            {
                rates = ShipAttitudeHold.Rates(q, s.AttitudeHoldRotation, def);
            }
            else if (def.LevelPitch > 0 || def.LevelRoll > 0)
            {
                rates = HoldLevel(rates, q, c.Up, input, def);
            }
            Vector3 angular = (rates - s.AngularVelocity) / dt + s.AngularVelocity * (def.TurnDrag * c.AirRatio + c.Submerged * 2f);
            turn = new Vector3(Math.Clamp(angular.X, -def.Pitch.Acceleration, def.Pitch.Acceleration),
                Math.Clamp(angular.Y, -def.Yaw.Acceleration, def.Yaw.Acceleration),
                Math.Clamp(angular.Z, -def.Roll.Acceleration, def.Roll.Acceleration));
        }
        else
        {
            thrust = new Vector3(-input.Strafe * capSide,
                input.Vertical >= 0 ? input.Vertical * capUp : input.Vertical * capDown,
                boosting ? capForward : input.Throttle >= 0 ? input.Throttle * capForward : input.Throttle * capBack);
            turn = new Vector3(-input.Pitch * def.Pitch.Acceleration, -input.Yaw * def.Yaw.Acceleration, input.Roll * def.Roll.Acceleration);
        }
    }

    public float Effort(Vector3 thrust, ShipDef def) =>
        Math.Min(1f, (MathF.Abs(thrust.X) + MathF.Abs(thrust.Y) + MathF.Abs(thrust.Z)) * def.Mass / (def.MainThrust + def.LiftThrust));

    public Vector3 Output(Vector3 thrust, ShipDef def)
    {
        float m = def.Mass;
        float capBack = def.RetroThrust / m, capSide = def.LateralThrust / m, capUp = def.LiftThrust / m, capDown = def.DownThrust / m;
        return new Vector3(thrust.X / capSide, thrust.Y >= 0 ? thrust.Y / capUp : thrust.Y / capDown,
            thrust.Z >= 0 ? thrust.Z * m / def.MainThrust : thrust.Z / capBack);
    }

    public IEnumerable<string> Problems(ShipDef ship)
    {
        if (new[] { ship.MainThrust, ship.RetroThrust, ship.LateralThrust, ship.LiftThrust, ship.DownThrust }.Any(t => t <= 0) ||
            ship.LiftThrust / ship.Mass < 10)
        {
            yield return "thrust on every axis; the lift nozzles must carry it at 1 g with some to spare (10 m/s²)";
        }
        if (new[] { ship.ForwardSpeed, ship.ReverseSpeed, ship.LateralSpeed, ship.UpSpeed, ship.DownSpeed }.Any(v => v is <= 0 or > 1000) ||
            ship.BoostSpeed < ship.ForwardSpeed || ship.BoostThrust < 1 || ship.BoostSeconds <= 0 || ship.BoostCooldownSeconds < 0)
        {
            yield return "speeds 0 … 1000 m/s, boost at least the forward speed and thrust, a positive boost time";
        }
        if (new[] { ship.Pitch, ship.Yaw, ship.Roll }.Any(t => t.MaxRate <= 0 || t.Acceleration <= 0))
        {
            yield return "rates: positive rates and accelerations";
        }
    }

    /// <summary>
    /// Flight assist's level hold (ships.json <c>assist.level</c>, IRON_WAKE.md §5.4): the stick's pitch and roll rates
    /// stop short of the most pitch and roll the ship may take, and with the stick centred the ship comes back toward
    /// level at <see cref="ShipDef.LevelReturn"/> radians a second per radian off. The rates are the ship's own (x: +
    /// nose down, z: + roll right); a heavy carrier never stands on its tail.
    /// </summary>
    private static Vector3 HoldLevel(Vector3 rates, Quaternion q, Vector3 up, in ShipInput input, ShipDef def)
    {
        float k = def.LevelReturn;
        if (def.LevelPitch > 0)
        {
            // The nose's elevation, + up; its rate up is −x.
            float pitch = (float)DMath.Asin(Math.Clamp(Vector3.Dot(Vector3.Transform(Vector3.UnitZ, q), up), -1f, 1f));
            float noseUp = -rates.X;
            if (MathF.Abs(input.Pitch) < 0.05f)
            {
                noseUp = Math.Clamp(-k * pitch, -def.Pitch.MaxRate, def.Pitch.MaxRate);
            }
            else if (noseUp > 0)
            {
                noseUp = MathF.Min(noseUp, k * (def.LevelPitch - pitch));
            }
            else
            {
                noseUp = MathF.Max(noseUp, -k * (def.LevelPitch + pitch));
            }
            rates.X = -noseUp;
        }
        if (def.LevelRoll > 0)
        {
            // The roll, + right wing down: the ship's right (−X) dips below the horizontal.
            float roll = (float)DMath.Asin(Math.Clamp(-Vector3.Dot(Vector3.Transform(-Vector3.UnitX, q), up), -1f, 1f));
            float right = rates.Z;
            if (MathF.Abs(input.Roll) < 0.05f)
            {
                right = Math.Clamp(-k * roll, -def.Roll.MaxRate, def.Roll.MaxRate);
            }
            else if (right > 0)
            {
                right = MathF.Min(right, k * (def.LevelRoll - roll));
            }
            else
            {
                right = MathF.Max(right, -k * (def.LevelRoll + roll));
            }
            rates.Z = right;
        }
        return rates;
    }
}
