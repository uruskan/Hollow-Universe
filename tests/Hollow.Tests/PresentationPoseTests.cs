using System;
using System.Numerics;
using Hollow.Client;
using Hollow.Simulation.Targets;

namespace Hollow.Tests;

public static class PresentationPoseTests
{
    [Test]
    public static void PresentationOverridesAreLazyAndNeverChangeGameplayPose()
    {
        using var targets = new TargetModule();
        targets.Welcome(); // Ground needs no live client, and supplies a real registered gameplay fallback.
        Assert.True(targets.TryPresentationPose(TargetRef.Ground, out TargetPose fallback), "unregistered presentation uses family pose");
        Assert.Equal(new TargetPose(Vector3.Zero, Quaternion.Identity), fallback, "ground family fallback");
        int calls = 0;
        var drawn = new TargetPose(new Vector3(4, 8, 12), Quaternion.Identity);
        using IDisposable token = targets.RegisterPresentationPose(TargetRef.Ground, () => { calls++; return drawn; });
        Assert.Equal(0, calls, "registration never polls a renderer");
        Assert.True(targets.TryPose(TargetRef.Ground, out TargetPose gameplay), "gameplay remains resolvable");
        Assert.Equal(fallback, gameplay, "visual pose cannot alter gameplay targeting");
        Assert.Equal(0, calls, "gameplay lookup never invokes presentation callback");
        Assert.True(targets.TryPresentationPose(TargetRef.Ground, out TargetPose current), "presentation invokes renderer");
        Assert.Equal(drawn, current, "renderer pose wins for presentation only");
        drawn = drawn with { Position = new Vector3(9, 8, 7) };
        Assert.True(targets.TryPresentationPose(TargetRef.Ground, out current), "subsequent lookup queries live pose");
        Assert.Equal(drawn, current, "moving render transform is not cached");
        Assert.Equal(2, calls, "only presentation lookups invoke callback");
    }

    [Test]
    public static void PoseRegistrationReplacementAndPartOwnershipAreIndependent()
    {
        using var targets = new TargetModule();
        var left = new TargetRef(TargetFamily.Door, 44, 1);
        var right = left with { Part = 2 };
        var oldPose = new TargetPose(Vector3.UnitX, Quaternion.Identity);
        var replacementPose = oldPose with { Position = Vector3.UnitY };
        IDisposable old = targets.RegisterPresentationPose(left, () => oldPose);
        IDisposable replacement = targets.RegisterPresentationPose(left, () => replacementPose);
        using IDisposable sibling = targets.RegisterPresentationPose(right, () => oldPose);
        old.Dispose(); old.Dispose();
        Assert.True(targets.TryPresentationPose(left, out TargetPose actual), "late old renderer disposal preserves replacement");
        Assert.Equal(replacementPose, actual, "replacement owns left part");
        Assert.True(targets.TryPresentationPose(right, out actual), "same target's other part remains independent");
        Assert.Equal(oldPose, actual, "full target including part is the registry key");
        replacement.Dispose(); replacement.Dispose();
        Assert.True(!targets.TryPresentationPose(left, out _), "current owner's disposal unregisters only its override");
        Assert.True(targets.TryPresentationPose(right, out _), "disposing left cannot unregister right");
    }

    [Test]
    public static void GoneRendererSuppressesFallbackUntilItsRegistrationIsDisposed()
    {
        using var targets = new TargetModule();
        targets.Welcome();
        IDisposable token = targets.RegisterPresentationPose(TargetRef.Ground, () => null);
        Assert.True(targets.TryPose(TargetRef.Ground, out _), "gameplay family remains present");
        Assert.True(!targets.TryPresentationPose(TargetRef.Ground, out _), "explicitly gone renderer does not fall back to stale family pose");
        token.Dispose();
        Assert.True(targets.TryPresentationPose(TargetRef.Ground, out _), "absent override restores fallback");
    }

    [Test]
    public static void ModuleDisposalClearsProvidersWithoutLettingOldTokensRemoveNewOwners()
    {
        var targets = new TargetModule();
        var target = new TargetRef(TargetFamily.Thing, 12, 3);
        int oldCalls = 0;
        IDisposable old = targets.RegisterPresentationPose(target, () => { oldCalls++; return new TargetPose(Vector3.One, Quaternion.Identity); });
        targets.Dispose();
        Assert.True(!targets.TryPresentationPose(target, out _), "module disposal clears render registrations");
        Assert.Equal(0, oldCalls, "disposed callback never invoked");
        using IDisposable fresh = targets.RegisterPresentationPose(target, () => new TargetPose(Vector3.UnitZ, Quaternion.Identity));
        old.Dispose();
        Assert.True(targets.TryPresentationPose(target, out TargetPose pose), "old zone token cannot remove new zone registration");
        Assert.Equal(Vector3.UnitZ, pose.Position, "new owner retains current pose");
        targets.Dispose();
        Assert.True(!targets.TryPresentationPose(target, out _), "repeated module cleanup is safe");
    }
}
