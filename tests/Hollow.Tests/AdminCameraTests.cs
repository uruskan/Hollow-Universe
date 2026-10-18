using System;
using System.Linq;
using System.Numerics;
using Hollow.Client;
using Hollow.Net.Commands;

namespace Hollow.Tests;

public static class AdminCameraTests
{
    [Test]
    public static void AdminSelfCameraRequiresEligibilityAndOnlyRightDragChangesItsAngle()
    {
        var camera = new AdminSelfCamera();
        Assert.True(!camera.Active && !camera.SecondaryCaptured, "a new session starts in first person");
        Assert.True(!camera.SetActive(true, false), "an ineligible player cannot enable the camera");
        Assert.True(!camera.HandleSecondary(true), "ordinary first-person right mouse belongs to the held item");
        Assert.True(camera.SetActive(true, true), "an eligible admin can enable the camera");
        float yaw = camera.Orbit.Yaw, pitch = camera.Orbit.Pitch;
        camera.Drag(0.8f, 0.2f);
        Assert.Near(yaw, camera.Orbit.Yaw, 1e-6, "free mouse movement leaves the orbit yaw alone");
        Assert.Near(pitch, camera.Orbit.Pitch, 1e-6, "free mouse movement leaves the orbit pitch alone");
        Assert.True(camera.HandleSecondary(true) && camera.Orbit.Dragging && camera.SecondaryCaptured,
            "right press belongs to the orbit while active");
        camera.Drag(0.8f, 0.2f);
        Assert.Near(yaw + 0.8f, camera.Orbit.Yaw, 1e-6, "right drag changes yaw");
        Assert.Near(pitch + 0.2f, camera.Orbit.Pitch, 1e-6, "right drag changes pitch");
        Assert.True(camera.HandleSecondary(false), "the orbit consumes its right-button release");
        Assert.True(!camera.Orbit.Dragging && !camera.SecondaryCaptured, "release ends dragging and capture");
    }

    [Test]
    public static void AdminSelfCameraKeepsHeldRightMouseCapturedUntilReleaseAfterDisable()
    {
        var camera = new AdminSelfCamera();
        camera.SetActive(true, true);
        camera.HandleSecondary(true);
        camera.SetActive(false, true);
        Assert.True(!camera.Active && !camera.Orbit.Dragging && camera.SecondaryCaptured,
            "turning the camera off cannot turn the held orbit button into a weapon's aim button");
        camera.Validate(true, true, true);
        Assert.True(camera.SecondaryCaptured, "polling a held button keeps it captured");
        Assert.True(camera.HandleSecondary(false) && !camera.SecondaryCaptured, "release clears the capture");
        Assert.True(!camera.HandleSecondary(true), "a fresh press is available to first-person controls");
        camera.Validate(true, true, false);
        Assert.True(!camera.SecondaryCaptured, "an observed release also clears a capture");
    }

    [Test]
    public static void AdminSelfCameraKeepsBothHeldButtonsCapturedAcrossDisable()
    {
        var camera = new AdminSelfCamera();
        Assert.True(!camera.HandlePrimary(true), "ordinary first-person left mouse belongs to the held item");
        camera.SetActive(true, true);
        Assert.True(camera.HandlePrimary(true) && camera.PrimaryCaptured, "inspection captures a left press");
        camera.HandleSecondary(true);
        camera.SetActive(false, true);
        camera.Validate(true, true, true, primaryHeld: true);
        Assert.True(camera.PrimaryCaptured && camera.SecondaryCaptured,
            "disabling inspection cannot convert either held mouse button into an attack or aim");
        Assert.True(camera.HandleSecondary(false) && !camera.SecondaryCaptured && camera.PrimaryCaptured,
            "releasing right mouse leaves the held left button captured");
        Assert.True(camera.HandlePrimary(false) && !camera.PrimaryCaptured, "left release clears its own capture");
        Assert.True(!camera.HandlePrimary(true), "a fresh left press is available to first-person controls");
        camera.SetActive(true, true);
        camera.Validate(true, false, false, primaryHeld: true);
        Assert.True(camera.PrimaryCaptured, "polling captures a held left button even when a panel consumed its event");
        camera.SetActive(false, true);
        camera.Validate(true, true, false, primaryHeld: false);
        Assert.True(!camera.PrimaryCaptured, "polling clears a release whose event was consumed elsewhere");
    }

    [Test]
    public static void AdminSelfCameraCancelsDragForMenusAndDisablesWhenEligibilityIsLost()
    {
        var camera = new AdminSelfCamera();
        camera.SetActive(true, true);
        camera.HandleSecondary(true);
        camera.Validate(true, false, true);
        Assert.True(camera.Active && !camera.Orbit.Dragging && camera.SecondaryCaptured,
            "a menu or lost focus cancels the drag while retaining the chosen view and held-button capture");
        camera.Validate(true, true, true);
        Assert.True(!camera.Orbit.Dragging, "returning focus cannot resume a stale drag");
        camera.HandleSecondary(false);
        camera.HandleSecondary(true);
        camera.Validate(false, true, true);
        Assert.True(!camera.Active && !camera.Orbit.Dragging && camera.SecondaryCaptured,
            "death, seating or lost admin rights returns to first person without leaking held aim");
        camera.Validate(false, true, false);
        Assert.True(!camera.SecondaryCaptured, "polling clears a release whose UI event was consumed elsewhere");
        camera.Validate(true, true, false);
        Assert.True(!camera.Active, "eligibility returning does not silently reenable the camera");
    }

    [Test]
    public static void AdminSelfCameraFollowsThePlayerAndZoomsWithinItsOwnLimits()
    {
        var camera = new AdminSelfCamera();
        Assert.Near(3.5, camera.Orbit.Distance, 1e-6, "the self view begins close enough to inspect the character");
        Vector3 player = new(500, 800, -600), up = Vector3.Normalize(player);
        camera.SetActive(true, true);
        camera.HandleSecondary(true);
        camera.Drag(1.2f, 0.4f);
        camera.Orbit.Zoom(2);
        var pose = camera.Orbit.Follow(player, up, -Vector3.UnitZ);
        Assert.Near(camera.Orbit.Distance, Vector3.Distance(player, pose.Eye), 1e-4, "orbit distance is centred on the player");
        Assert.True(Vector3.Dot(pose.Forward, Vector3.Normalize(player - pose.Eye)) > 0.9999f,
            "the camera still looks at the player away from the planet's equator");
        Vector3 shift = new(20, -10, 5);
        var moved = camera.Orbit.Follow(player + shift, up, -Vector3.UnitZ);
        Assert.True(Vector3.Distance(pose.Eye + shift, moved.Eye) < 1e-4, "moving carries the orbit with the player");
        camera.Orbit.Zoom(1000);
        Assert.Near(1.5, camera.Orbit.Distance, 1e-6, "zoom in stops before entering the character");
        camera.Orbit.Zoom(-1000);
        Assert.Near(20, camera.Orbit.Distance, 1e-6, "self inspection zoom has a bounded reach");
        var pole = camera.Orbit.Follow(Vector3.Zero, Vector3.UnitY, Vector3.UnitY);
        Assert.True(float.IsFinite(pole.Eye.X) && float.IsFinite(pole.Eye.Y) && float.IsFinite(pole.Eye.Z),
            "a view parallel to local up has a finite fallback heading");
    }

    [Test]
    public static void AdminSelfCameraCommandAndAliasesAreAdminOnlyLocalCommandsWithCompletion()
    {
        foreach (string name in new[] { "thirdperson", "tps", "selfcamera" })
        {
            var parsed = ConsoleCatalog.Parse(name + " on");
            Assert.Equal("thirdperson", parsed.Command?.Name, "all names select the same command");
            Assert.True(parsed.Command is { Admin: true, Side: CommandSide.Client },
                "the camera is explicitly admin-only and runs in the client");
        }
        var completion = ConsoleCompletion.Complete("thirdperson o", CompletionSources.From(ContentFixture.Content, Array.Empty<string>()));
        Assert.True(completion.Candidates.Contains("on") && completion.Candidates.Contains("off"), "Tab offers both explicit camera states");
    }
}
