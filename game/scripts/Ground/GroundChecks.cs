using Godot;
using Hollow.Simulation.Movement;

namespace HollowUniverse.Ground;

/// <summary>Checks of the ground game that run inside the client process.</summary>
public static class GroundChecks
{
    /// <summary>
    /// <c>--physics-check</c>: runs the physics spike (<see cref="MovementCheck"/>) in this process, prints its line and
    /// quits. The line must match the one the engine-free tests print: same Jolt, same inputs, same result.
    /// </summary>
    public static void RunPhysicsCheck(Node node)
    {
        MovementCheck.Result result = MovementCheck.Run();
        GD.Print(result.ToString());
        node.GetTree().Quit(0);
    }
}
