using System;
using System.Linq;
using Hollow.Surface;

namespace Hollow.Simulation.Content.Readers;

internal sealed class PhysicalSurfacesReader : IContentReader
{
    public string File => PhysicalSurfaceData.FileName;
    public void Read(ContentReading r) => r.Loaded.Surfaces = PhysicalSurfaceData.Parse(
        r.Root(File, "definitions", "terrain", "grades", "deployables", "doors", "natural", "ships", "shipParts", "deployableParts"), r.Fields);

    public void Check(ContentChecking c)
    {
        var loaded = c.Loaded;
        PhysicalSurfaceData data = loaded.Surfaces ?? PhysicalSurfaceData.Empty;
        Check("terrain", Enum.GetNames<GroundMaterial>());
        Check("grades", loaded.Building?.Grades.Select(g => g.Key) ?? Array.Empty<string>());
        Check("doors", loaded.Building?.Doors.Select(g => g.Key) ?? Array.Empty<string>());
        Check("deployables", loaded.Deployables.Select(d => d.Key));
        Check("deployables", loaded.World?.Props.Select(p => p.Key) ?? Array.Empty<string>());
        if (loaded.ClaimDeployable is { } claim) Check("deployables", new[] { claim.Key });
        Check("natural", loaded.Natural.Select(n => n.Key));
        Check("ships", loaded.Ships.Select(s => s.Key));
        foreach (var mapping in data.PartMappings)
        {
            if (mapping.Group == "shipParts")
            {
                var ship = loaded.Ships.FirstOrDefault(s => s.Key == mapping.Key);
                if (ship == null || mapping.Part > 0 && mapping.Part >= ship.Parts.Count)
                    c.Problem(File, $"shipParts.{mapping.Key}/{mapping.Part}: unknown ship or part");
            }
            else
            {
                DeployableDef? def = loaded.Deployables.FirstOrDefault(d => d.Key == mapping.Key)
                    ?? loaded.World?.Props.FirstOrDefault(p => p.Key == mapping.Key)?.Deployable;
                if (def == null && loaded.ClaimDeployable?.Key == mapping.Key) def = loaded.ClaimDeployable;
                if (def == null || mapping.Part > 0 && mapping.Part > (loaded.Compound?.Find(def.Assembly)?.Parts.Count ?? 0))
                    c.Problem(File, $"deployableParts.{mapping.Key}/{mapping.Part}: unknown deployable or part");
            }
        }
        void Check(string group, System.Collections.Generic.IEnumerable<string> keys)
        {
            foreach (string key in data.Missing(group, keys)) c.Problem(File, $"{group}.{key}: physical surface missing");
        }
    }
}
