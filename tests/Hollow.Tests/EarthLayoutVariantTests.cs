using Hollow.Core;
using Hollow.Galaxy;
using Hollow.Surface;

namespace Hollow.Tests;

public static class EarthLayoutVariantTests
{
    [Test]
    public static void EarthLayoutVariantsPreserveZeroAndGenerateRepeatableDifferentLand()
    {
        const ulong seed = 0x123456789ABCDEF0;
        Assert.Equal(seed, WalkingWorld.LayoutSeed(seed, 0), "variant zero preserves the established seed exactly");
        Assert.True(WalkingWorld.LayoutSeed(seed, 1) != seed && WalkingWorld.LayoutSeed(seed, 1) != WalkingWorld.LayoutSeed(seed, 2), "candidate variants have distinct domain-separated seeds");
        var (system, body) = StartingWorld.Find();
        WalkingParameters profile = WalkingParameters.Earth with { LayoutVariant = 1 };
        WalkingWorld first = WalkingWorld.For(system, body, GameScale.PlanetRadius(body), profile);
        WalkingWorld again = WalkingWorld.For(system, body, first.Radius, profile);
        WalkingWorld other = WalkingWorld.For(system, body, first.Radius, profile with { LayoutVariant = 2 });
        bool changed = false;
        foreach (Vector3D p in PlanetSurface.FibonacciSphere(48))
        {
            Assert.Equal(first.Fields(p), again.Fields(p), "independently generated candidates reproduce all height/water/relief fields");
            Assert.Equal(first.BiomeAt(p), again.BiomeAt(p), "candidate biomes reproduce");
            changed |= first.Fields(p).Height != other.Fields(p).Height;
        }
        Assert.True(changed, "changing candidate variation changes actual geography");
        Assert.True(GameScale.SurfaceArea(first.Radius) <= GameScale.MaximumSurfaceArea, "candidates retain the whole-planet surface cap");
        Assert.Equal(WalkingParameters.Earth.TerrainRevision, profile.TerrainRevision, "offline candidates do not mutate the production save revision");
    }
}
