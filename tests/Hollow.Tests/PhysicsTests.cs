using Hollow.Simulation.Movement;

namespace Hollow.Tests;

public static class PhysicsTests
{
    /// <summary>
    /// GROUND.md §4.5, the physics spike: Jolt runs here, and the same inputs give the same result in two fresh worlds.
    /// The Godot client runs the same check (<c>-- --physics-check</c>) and must print the same hash.
    /// </summary>
    [Test]
    public static void MovementCheckIsRepeatable()
    {
        MovementCheck.Result first = MovementCheck.Run();
        MovementCheck.Result second = MovementCheck.Run();
        Fixtures.Report(first.ToString());
        Assert.Equal(first.Hash, second.Hash, "hash of every state in two runs");
        Assert.True(first.Position.Length() > 40, "the character got somewhere");
        Assert.Equal(100, (int)first.Health, "nothing in the script hurts");
    }
}
