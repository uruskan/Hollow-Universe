namespace Hollow.Simulation;

/// <summary>
/// The building's doors moving (docs/DOORS.md §4, §5): each tick, once the characters have moved, every moving door's leaf
/// bodies go where its progress at this tick puts them, and what rides on a leaf (a charge, an arrow) follows in the systems
/// after it. A character that stood in the way is pushed by the character controller's penetration recovery on its next step;
/// nothing stops or reverses the door. Under the physics gate because it moves bodies.
/// </summary>
public sealed class DoorMotionSystem : ZoneSystem
{
    private BuildingSystem? _building;

    public override bool InsidePhysicsGate => true;

    public override int InitOrder => 30;

    protected internal override void Step() => (_building ??= Zone.Require<BuildingSystem>()).World?.StepDoors(Tick);
}
