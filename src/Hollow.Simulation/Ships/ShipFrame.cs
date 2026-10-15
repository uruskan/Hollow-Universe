using System.Numerics;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Physics;

namespace Hollow.Simulation.Ships;

/// <summary>
/// A ship's frame in a zone (IRON_WAKE.md §4.3): what characters aboard it move in, keyed by the ship's entity ID. Up is
/// the ship's +Y; gravity is the zone's characters' times the decks' (§4.4: the ship's turns and braking are never felt);
/// there is no water aboard; the ship's space is the physics. Its pose is the ship's as the zone steps its characters:
/// the ship moves after them, so it is where the ship's last step left it.
/// </summary>
public sealed class ShipFrame : IShipFrame
{
    private readonly Ship _ship;
    private readonly float _gravity;

    public ShipFrame(Ship ship, ShipSpace space, float characterGravity)
    {
        _ship = ship;
        Space = space;
        _gravity = characterGravity * space.Interior.DeckGravity;
    }

    public ulong Key => _ship.Id.Value;

    public ShipInterior Interior => Space.Interior;

    public float[] States => _ship.Parts;

    public ShipSpace Space { get; }

    public uint ParkedBody => _ship.ParkedBody;

    public float Gravity => _gravity;

    public double Radius => 0;

    public CharacterBody Body => Space.Body;

    public Vector3 UpAt(Vector3 position) => Vector3.UnitY;

    public float Submersion(Vector3 position, MovementTuning tuning) => 0;

    public bool TryPose(long tick, out ShipPose pose)
    {
        pose = PoseOf(_ship.State, _ship.Def.CentreOfMass);
        return true;
    }

    /// <summary>
    /// A ship state's pose: its turn rate in the zone's frame is its own spin and the ground's turn under it as it goes
    /// (docs/WHOLE_WORLD.md §6.5: the motor turns its attitude with the ground every substep), r × v over r².
    /// </summary>
    public static ShipPose PoseOf(in ShipState s, Vector3 centreOfMass)
    {
        Vector3 spin = Vector3.Transform(s.AngularVelocity, s.Rotation);
        float r2 = s.Position.LengthSquared();
        if (r2 > 1)
        {
            spin += Vector3.Cross(s.Position, s.Velocity) / r2;
        }
        return new ShipPose(s.Position, s.Rotation, s.Velocity, spin, centreOfMass);
    }
}
