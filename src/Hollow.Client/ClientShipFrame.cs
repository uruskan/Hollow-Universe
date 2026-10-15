using System;
using System.Numerics;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Physics;
using Hollow.Simulation.Ships;

namespace Hollow.Client;

/// <summary>
/// A ship's frame on a client (IRON_WAKE.md §4.3, §4.8), keyed by the ship's net ID as the wire names frames: its space,
/// in which the player's character is predicted aboard and the aim meets its pieces. The ships' module makes one for each
/// ship with an interior within <see cref="ShipModule.SpaceRadius"/> of the player, and for the ship the player is aboard
/// wherever it is. Its pose as a tick begins is the pilot's prediction of the tick before for the ship the player flies,
/// the newest state exactly for one asleep (the server's parked pose, on the wire's grid), and the snapshots' otherwise:
/// only a hand-over between frames asks for it, so a passenger's prediction never depends on it.
/// </summary>
public sealed class ClientShipFrame : IShipFrame, IDisposable
{
    private readonly ShipPredictor _pilot;
    private readonly Func<uint, uint> _parkedBody;

    internal ClientShipFrame(RemoteShip ship, ShipDef def, ShipPredictor pilot, Func<uint, uint> parkedBody, MovementTuning tuning, float characterGravity)
    {
        Ship = ship;
        Def = def;
        Interior = def.Interior ?? throw new ArgumentException($"{def.Key} has no interior", nameof(def));
        _pilot = pilot;
        _parkedBody = parkedBody;
        States = new float[Interior.Parts.Count];
        ship.PartsInto(Interior, States);
        PartsVersion = ship.PartsVersion;
        Space = new ShipSpace(Interior, States, tuning);
        Gravity = characterGravity * Interior.DeckGravity;
    }

    /// <summary>The ship as this client knows it (its states, its parts).</summary>
    public RemoteShip Ship { get; }

    public ShipDef Def { get; }

    public ulong Key => Ship.NetId;

    public ShipInterior Interior { get; }

    public float[] States { get; }

    public ShipSpace Space { get; }

    /// <summary>Its body in the prediction's physics while parked (0 when flying, or not known yet).</summary>
    public uint ParkedBody => _parkedBody(Ship.NetId);

    public float Gravity { get; }

    public double Radius => 0;

    public CharacterBody Body => Space.Body;

    /// <summary>The ship's <see cref="RemoteShip.PartsVersion"/> its space is posed at.</summary>
    public int PartsVersion { get; private set; }

    public Vector3 UpAt(Vector3 position) => Vector3.UnitY;

    public float Submersion(Vector3 position, MovementTuning tuning) => 0;

    public bool TryPose(long tick, out ShipPose pose)
    {
        // The server steps its characters before its ships: a tick begins where the ship's step of the tick before left it.
        if (_pilot.Active && _pilot.NetId == Ship.NetId && _pilot.TryGetState((uint)Math.Max(0, tick - 1), out ShipState s))
        {
            pose = ShipFrame.PoseOf(s, Def.CentreOfMass);
            return true;
        }
        return Ship.TryPoseAt(tick - 1, out pose);
    }

    /// <summary>Poses the space's parts as the server last said (true when they moved).</summary>
    public bool FollowParts()
    {
        if (Ship.PartsVersion == PartsVersion)
        {
            return false;
        }
        PartsVersion = Ship.PartsVersion;
        Ship.PartsInto(Interior, States);
        Space.Pose(States);
        return true;
    }

    public void Dispose() => Space.Dispose();
}
