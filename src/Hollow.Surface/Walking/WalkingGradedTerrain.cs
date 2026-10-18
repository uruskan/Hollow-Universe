using System;
using System.Threading;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>
/// Restricted, finalized input to formation construction. Retains the owned world only for the build;
/// the published FormationPlan retains neither this adapter nor its terrain. No cover/support query is exposed.
/// </summary>
public sealed class WalkingGradedTerrain : IGradedTerrain
{
    public const string Contract = "walking-finalized-grading-raw-fields-sites-v1";
    private readonly WalkingWorld _world;
    private readonly string _profile;
    private readonly FormationProtectedSite[] _sites;

    /// <summary>
    /// Canonical physical generator-input identity, computed without constructing a world or sites. Includes the
    /// monument/site algorithm and ordered physical content; excludes formations, LOD art and admission limits.
    /// This is an input/version contract, not a measured hash of generated terrain samples.
    /// </summary>
    public static string InputIdentity(PlanetEnvironmentDescriptor descriptor, string terrainProfileKey) =>
        EnvironmentCanonical.GradedWalkingInputIdentity(descriptor, terrainProfileKey);

    public WalkingGradedTerrain(WalkingWorld world, string profileKey, int maximumProtectedSites = 1024,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(world);
        _profile = FormationInput.Key(profileKey, nameof(profileKey));
        if (maximumProtectedSites < 0 || maximumProtectedSites > 1024)
            throw new FormationValidationException("Finalized terrain site budget must be in [0, 1024].");
        cancellation.ThrowIfCancellationRequested();
        // This completes the same lazy grading/site set used by HeightAt. Placement reads raw fields only.
        var monuments = world.Monuments.All;
        if (monuments.Count > maximumProtectedSites)
            throw new FormationCapacityException("Finalized monument snapshot exceeds its site budget.");
        _sites = new FormationProtectedSite[monuments.Count];
        for (int i = 0; i < monuments.Count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            MonumentPlacement site = monuments[i];
            Vector3D low = site.Position, high = site.Position;
            foreach (Vector3D vertex in site.Definition.Positions)
            {
                Vector3D point = site.Transform(vertex);
                low = FormationBounds.Lower(low, point); high = FormationBounds.Upper(high, point);
            }
            // The full grading annulus follows rays at raw terrain heights. A finite plane/mesh box cannot
            // enclose it. Protect its actual chord footprint at every radial height, plus the mesh itself.
            _sites[i] = new FormationProtectedSite($"monument-{site.Tile.Packed:x16}-{site.Definition.Key}",
                new FormationBounds(low, high), site.Up, (site.Definition.Radius + MonumentPlacements.Blend) / world.Radius);
        }
        _world = world;
    }

    public bool IsFinalized => true;
    public double Radius => _world.Radius;
    public ReadOnlySpan<FormationProtectedSite> ProtectedSites => _sites;
    public double HeightAt(Vector3D unitDirection) => _world.HeightAt(unitDirection);
    public double WaterLevelAt(Vector3D unitDirection) => _world.WaterLevelAt(unitDirection);
    public GradedTerrainFields RawFieldsAt(Vector3D unitDirection)
    {
        WalkingSample sample = _world.Fields(unitDirection);
        ClimateSample climate = _world.Climate(unitDirection, sample);
        return new GradedTerrainFields(_profile, climate.Temperature, climate.Moisture, _world.ForestCover(unitDirection, sample));
    }
}
