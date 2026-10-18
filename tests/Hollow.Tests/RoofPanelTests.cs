using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Hollow.Core;
using Hollow.Simulation;
using Hollow.Simulation.Building;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Physics;
using Hollow.Surface;

namespace Hollow.Tests;

/// <summary>Open roof sides, retained deck collision and the installed roof kit agree.</summary>
public static class RoofPanelTests
{
    private static BuildingData Data => ContentFixture.Content.Building;
    private static PieceDef P(string key) => Data.Piece(key) ?? throw new TestFailure("no piece " + key);
    private static readonly SocketId Top = new(SocketKind.Top, 0);

    [Test]
    public static void BothRoofShapesHaveOpenSidesAndSolidDecks()
    {
        using var physics = new PhysicsWorld(new SphereSurface(1000, 1, 0), ZoneFrame.World);
        using var building = new BuildingPhysics(physics);
        foreach (string key in new[] { "roof", "roof-triangle" })
        {
            PieceDef def = P(key);
            uint body = building.Add(def, building.ShapeFor(def, PieceShapes.For(Data, def)),
                Vector3.Zero, Quaternion.Identity);
            Assert.True(body != 0, key + ": collision body created");
            foreach (int side in new[] { -1, 1 })
            {
                Assert.True(!building.Ray(new Vector3(side * 4, 0.4f, 0), new Vector3(-side, 0, 0), 8,
                    out _, buildingOnly: true), key + ": a ray passes through both open sloping sides");
            }
            double underside = key == "roof" ? 1.5 : 1.0;
            Assert.True(building.Ray(new Vector3(0, 6, 0), -Vector3.UnitY, 10,
                out RayHit above, buildingOnly: true), key + ": the deck blocks a downward ray");
            Assert.Equal(body, above.Body, key + ": the deck is the hit body");
            Assert.InRange(underside + 0.1, underside + 0.5, above.Point.Y, key + ": solid deck thickness");
            Assert.True(building.Ray(new Vector3(0, -1, 0), Vector3.UnitY, 6,
                out RayHit below, buildingOnly: true), key + ": the underside blocks an upward ray");
            Assert.Near(underside, below.Point.Y, 0.02, key + ": original underside height");
            building.Remove(body);
        }
    }

    [Test]
    public static void AdjacentSupportedRoofsPlaceThroughTheExistingRules()
    {
        using var physics = new PhysicsWorld(new SphereSurface(1000, 1, 0), ZoneFrame.World);
        ulong next = 0;
        using var world = new BuildingWorld(Data, physics, () => new EntityId(++next));
        Piece first = world.AddFirst(BuildingFrame.At(new Vector3D(0, 1010, 0), Vector3D.UnitY, -Math.PI / 2),
            P("foundation"), 0, 1, 0);
        Piece second = world.Add(first.Structure, P("foundation"), 0,
            new Pose(new Vector3D(3, 0, 0), 0), 1, 0);
        Piece Place(Piece support, SocketId socket, string key)
        {
            PlacementPlan plan = world.FromIntent(P(key), support, socket, 0)
                ?? throw new TestFailure("missing existing socket for " + key);
            Assert.True(world.CheckBuilding(plan, 1, null).Ok, key + ": accepted by shared placement rules");
            Piece added = world.Add(plan.Structure!, plan.Piece, 0, plan.Pose, 1, 0);
            Assert.True(added.Supporters.Any(link => link.Other == support), key + ": retains its declared support");
            Assert.True(added.Stability >= Data.MinimumStability && !added.Collapsing, key + ": stable");
            return added;
        }
        Piece leftWall = Place(first, new SocketId(SocketKind.Edge, 0), "wall");
        Piece rightWall = Place(second, new SocketId(SocketKind.Edge, 0), "wall");
        Piece left = Place(leftWall, Top, "roof");
        Piece right = Place(rightWall, Top, "roof");
        foreach (Vector3D end in new[] { new Vector3D(1.5, 0, 1.5), new Vector3D(1.5, 3, -1.5) })
        {
            Vector3D a = PieceGeometry.ToFrame(left.Pose, end);
            Vector3D b = PieceGeometry.ToFrame(right.Pose, new Vector3D(-1.5, end.Y, end.Z));
            Assert.Near(0, (a - b).Length, 1e-8, "adjacent slope endpoints meet without a side socket");
        }
        Assert.Equal(left.Stability, right.Stability, "both roofs retain the same support strength");
        PlacementPlan occupied = world.FromIntent(P("roof"), leftWall, Top, 0)!;
        Assert.Equal(OutcomeCode.Occupied, world.CheckBuilding(occupied, 1, null).Code,
            "the normal occupied-position refusal still applies");
    }

    [Test]
    public static void LegacySquareTopStackingPosesStayFixed()
    {
        var parent = new Pose(new Vector3D(7, 4, -9), 5);
        foreach (int orientation in new[] { 0, 1 })
        {
            Assert.True(PieceGeometry.Snap(Data, P("roof"), parent, Top, P("roof"), orientation, out Pose child),
                "legacy square roof Top accepts both orientations");
            Vector3D local = orientation == 0 ? new Vector3D(0, 3, -3) : new Vector3D(0, 3, 0);
            Assert.Near(0, (child.Position - PieceGeometry.ToFrame(parent, local)).Length, 1e-8,
                "legacy Top position");
            Assert.Equal(orientation == 0 ? 5 : 11, child.Yaw, "legacy Top yaw code");
        }
        Assert.True(!PieceGeometry.Snap(Data, P("roof-triangle"), parent, Top, P("roof"), 0, out _),
            "triangle apex has no new structural socket");
    }

    [Test]
    public static void EveryInstalledGradeUsesTheSameDeckHullAsSimulation()
    {
        foreach (string grade in new[] { "twig", "wood", "stone", "metal", "armoured" })
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(Fixtures.Root, "game", "assets", "building", $"building_{grade}.glb"));
            int jsonLength = BitConverter.ToInt32(bytes, 12);
            using JsonDocument document = JsonDocument.Parse(bytes.AsMemory(20, jsonLength));
            JsonElement root = document.RootElement;
            JsonElement[] nodes = root.GetProperty("nodes").EnumerateArray().ToArray();
            foreach (string key in new[] { "roof", "roof-triangle" })
            {
                string what = grade + " " + key;
                JsonElement[] hulls = nodes.Where(n => n.TryGetProperty("name", out JsonElement name) &&
                    name.GetString()!.StartsWith(key + "_col", StringComparison.Ordinal)).ToArray();
                Assert.Equal(1, hulls.Length, what + ": one slab hull and no gable hulls");
                Assert.Equal(key + "_col00-convcolonly", hulls[0].GetProperty("name").GetString()!,
                    what + ": retained collision-node name");
                foreach (string transform in new[] { "translation", "rotation", "scale", "matrix" })
                {
                    Assert.True(!hulls[0].TryGetProperty(transform, out _), what + ": hull retains its identity pivot");
                }
                ConvexPart[] parts = PieceShapes.For(Data, P(key));
                Assert.Equal(1, parts.Length, what + ": one authoritative deck hull");
                Vector3[] actual = Positions(bytes, root, hulls[0], 28 + jsonLength).ToArray();
                Assert.True(actual.Length > 0, what + ": exported hull contains vertices");
                foreach (Vector3 point in actual)
                {
                    Assert.True(parts[0].Points.Any(p => Vector3.Distance(p, point) < 0.00001f),
                        what + ": exported collision vertex matches the simulation");
                }
                foreach (Vector3 point in parts[0].Points)
                {
                    Assert.True(actual.Any(p => Vector3.Distance(p, point) < 0.00001f),
                        what + ": every simulation hull corner is exported");
                }
                foreach (string suffix in new[] { "", "_LOD1", "_LOD2" })
                {
                    Assert.Equal(1, nodes.Count(n => n.TryGetProperty("name", out JsonElement name) &&
                        name.GetString() == key + suffix && n.TryGetProperty("mesh", out _)),
                        what + suffix + ": retained render LOD");
                }
            }
        }
    }

    private static IEnumerable<Vector3> Positions(byte[] bytes, JsonElement root, JsonElement node, int binaryStart)
    {
        JsonElement mesh = root.GetProperty("meshes")[node.GetProperty("mesh").GetInt32()];
        foreach (JsonElement primitive in mesh.GetProperty("primitives").EnumerateArray())
        {
            int index = primitive.GetProperty("attributes").GetProperty("POSITION").GetInt32();
            JsonElement accessor = root.GetProperty("accessors")[index];
            Assert.Equal(5126, accessor.GetProperty("componentType").GetInt32(), "float collision positions");
            Assert.Equal("VEC3", accessor.GetProperty("type").GetString()!, "three collision coordinates");
            JsonElement view = root.GetProperty("bufferViews")[accessor.GetProperty("bufferView").GetInt32()];
            int start = binaryStart + (view.TryGetProperty("byteOffset", out JsonElement v) ? v.GetInt32() : 0)
                + (accessor.TryGetProperty("byteOffset", out JsonElement a) ? a.GetInt32() : 0);
            int stride = view.TryGetProperty("byteStride", out JsonElement s) ? s.GetInt32() : 12;
            for (int i = 0; i < accessor.GetProperty("count").GetInt32(); i++)
            {
                int at = start + i * stride;
                yield return new Vector3(BitConverter.ToSingle(bytes, at), BitConverter.ToSingle(bytes, at + 4),
                    BitConverter.ToSingle(bytes, at + 8));
            }
        }
    }
}
