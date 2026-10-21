using System;
using System.Numerics;
using Hollow.Client.Map;
using Hollow.Core;
using Hollow.Surface;

namespace Hollow.Tests;

public static class MapInteractionTests
{
    [Test]
    public static void MapHeldDragKeepsItsGroundPointAcrossFrameUpdates()
    {
        foreach (double across in new[] { 900.0, 2000.0, 2700.0 })
        {
            var camera = new GlobeCamera(1200, ZoneFrame.Direction(45 * DMath.DegToRad, 30 * DMath.DegToRad), across);
            var pointer = new Vector2(140, 70);
            Assert.True(camera.Projection(800).ToDirection(pointer, out Vector3D grabbed), "grab a visible map point");
            for (int frame = 0; frame < 60; frame++)
            {
                Vector2 next = pointer + new Vector2(-2, 1);
                camera.Drag(pointer, next, 800);
                camera.Settle(1 / 30.0, dragging: true);
                Assert.True(camera.Projection(800).ToDirection(next, out Vector3D under), "the dragged point remains visible");
                Assert.True((grabbed - under).Length * camera.Radius < 0.02,
                    "automatic north-up does not pull the map away from a held pointer");
                pointer = next;
            }
            Vector3D heldUp = camera.Up;
            for (int frame = 0; frame < 30; frame++) camera.Settle(1 / 30.0, dragging: true);
            Assert.Equal(heldUp, camera.Up, "pausing a held drag does not cause map drift");
            camera.Settle(0.2);
            Assert.True((camera.Up - heldUp).Length > 1e-6, "north-up settling resumes after release");
        }
    }

    [Test]
    public static void MapCacheIncludesExplicitWalkingTerrainRevision()
    {
        var first = new WalkingWorld(783, 1200, 0, 0, 285, WalkingParameters.Default with { TerrainRevision = 2 });
        var next = new WalkingWorld(783, 1200, 0, 0, 285, WalkingParameters.Default with { TerrainRevision = 3 });
        ulong before = ZoneMap.SurfaceVersion(first), after = ZoneMap.SurfaceVersion(next);
        Assert.True(before != after, "an explicit terrain revision invalidates cached map pictures even when sampled heights stay equal");
        Assert.Equal(before, ZoneMap.SurfaceVersion(first), "the same surface and revision retain their cache identity");
    }
}
