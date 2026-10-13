using System.Collections.Generic;
using System.Numerics;
using Hollow.Simulation.Building;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;

namespace Hollow.Simulation;

// Building in a live zone (GROUND.md §6.3): the zone's BuildingWorld on its Jolt world, run on the zone's tick
// (collapses) and on a slow schedule (upkeep and decay, closed form), and hits on pieces and doors. The building package's
// phase 2a brings the same members with the player's building actions, the claim device as a deployable, replication
// and persistence; G5 (raiding) needs only this much of it, under the same names, so the two meet without a seam.
public sealed partial class Zone
{
    /// <summary>Upkeep and decay are applied this often (in closed form), and once with the elapsed time on load.</summary>
    public const double BuildingAdvanceSeconds = 60;

    private double _buildingAdvancedAt = double.NaN;

    /// <summary>The zone's building (null in a G1 zone, without game data).</summary>
    public BuildingWorld? Building { get; private set; }

    /// <summary>
    /// The building rules' clock in seconds: the real clock (Unix time), so edit windows, repair delays, code waits,
    /// and upkeep and decay run on while the server is down.
    /// </summary>
    public double BuildingNow => UnixMilliseconds / 1000.0;

    /// <summary>When upkeep and decay were last applied (Unix seconds; NaN before the first tick).</summary>
    public double BuildingAdvancedAt
    {
        get => _buildingAdvancedAt;
        set => _buildingAdvancedAt = value;
    }

    private void InitBuilding(GameContent content)
    {
        Building = new BuildingWorld(content.Building, Physics, NextId) { Characters = CharacterCapsules };
    }

    // Each tick: collapses due (nothing at all while none is under way). On the slow schedule: upkeep and decay.
    private void StepBuilding()
    {
        BuildingWorld building = Building!;
        double now = BuildingNow;
        building.Tick(now);
        if (double.IsNaN(_buildingAdvancedAt))
        {
            _buildingAdvancedAt = now;
        }
        else if (now - _buildingAdvancedAt >= BuildingAdvanceSeconds || now < _buildingAdvancedAt)
        {
            AdvanceBuilding();
        }
    }

    /// <summary>Applies upkeep and decay from <see cref="BuildingAdvancedAt"/> to now in one closed-form step (and on load, the time the server was down).</summary>
    public void AdvanceBuilding()
    {
        if (Building == null)
        {
            return;
        }
        double now = BuildingNow;
        if (!double.IsNaN(_buildingAdvancedAt) && now > _buildingAdvancedAt)
        {
            Building.AdvanceTime(now - _buildingAdvancedAt, now);
        }
        _buildingAdvancedAt = now;
    }

    // Who placement and doors must not trap: every living character's standing capsule.
    private IEnumerable<Capsule> CharacterCapsules()
    {
        ComponentPool<Character> characters = Entities.Pool<Character>();
        for (int i = 0; i < characters.Count; i++)
        {
            CharacterState s = characters.Values[i].State;
            if (!s.IsDead)
            {
                yield return new Capsule(s.Position, Frame.UpAt(s.Position), Tuning.Radius, Tuning.StandingHeight);
            }
        }
    }

    /// <summary>The nearest building body along a ray within <paramref name="length"/>: a piece, a door or a claim device, and how far.</summary>
    public bool TraceBuilding(Vector3 origin, Vector3 direction, float length, out float distance, out object? part)
    {
        distance = -1;
        part = null;
        if (Building?.Physics is not { } physics || !physics.Ray(origin, direction, length, out RayHit hit, buildingOnly: true))
        {
            return false;
        }
        part = physics.OwnerOf(hit.Body) switch
        {
            DoorLeaf leaf => leaf.Door,
            { } owner => owner,
            null => null,
        };
        distance = hit.Distance;
        return part != null;
    }

    /// <summary>
    /// Damage to a piece or door through the building rules (its grade's protection, a wall's soft side): melee as melee,
    /// arrows and bullets as bullets. Returns the health lost.
    /// </summary>
    public float DamageBuilding(object part, Vector3 direction, float amount, bool projectile)
    {
        BuildingWorld building = Building!;
        var hit = projectile ? new DamageHit(direction, Bullet: amount) : new DamageHit(direction, Melee: amount);
        DamageResult result = part switch
        {
            Piece piece => building.Damage(piece, hit, BuildingNow),
            Door door => building.Damage(door, hit, BuildingNow),
            _ => default,
        };
        return (float)result.Loss;
    }
}
