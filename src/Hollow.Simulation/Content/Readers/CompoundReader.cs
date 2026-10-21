using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace Hollow.Simulation.Content.Readers;

internal sealed class CompoundReader : IContentReader
{
    public string File => "compound.json";
    public void Read(ContentReading r)
    {
        JsonFields f = r.Fields;
        var assemblies = new List<CompoundAssembly>();
        foreach (JsonElement a in f.List(r.Root(File, "assemblies"), "assemblies", File))
        {
            string key = f.Text(a, "key", File), where = File + ": " + key;
            f.OnlyKnown(a, where, "key", "source", "size", "parts", "hulls", "sockets");
            var parts = new List<CompoundPart>();
            foreach (JsonElement p in f.List(a, "parts", where))
            {
                f.OnlyKnown(p, where + ".parts", "name", "mesh", "parent", "pivot");
                parts.Add(new CompoundPart(f.Text(p, "name", where), f.Text(p, "mesh", where),
                    f.Has(p, "parent") ? f.Text(p, "parent", where) : null, f.Vector(p, "pivot", where)));
            }
            var hulls = new List<CompoundHull>();
            foreach (JsonElement h in f.List(a, "hulls", where))
            {
                f.OnlyKnown(h, where + ".hulls", "name", "part", "points");
                var points = new List<Vector3>();
                foreach (JsonElement point in f.List(h, "points", where))
                {
                    if (point.ValueKind == JsonValueKind.Array && point.GetArrayLength() == 3 &&
                        point[0].ValueKind == JsonValueKind.Number && point[1].ValueKind == JsonValueKind.Number && point[2].ValueKind == JsonValueKind.Number &&
                        point[0].TryGetSingle(out float x) && point[1].TryGetSingle(out float y) && point[2].TryGetSingle(out float z) &&
                        float.IsFinite(x + y + z)) points.Add(new Vector3(x, y, z));
                    else f.Problem(where, "hull point must contain three finite numbers");
                }
                Vector3[] unique = points.Distinct().ToArray();
                if (unique.Length is < 4 or > 64) { f.Problem(where, "a hull needs 4–64 unique points"); continue; }
                var hull = new CompoundHull(f.Text(h, "name", where), f.Text(h, "part", where), unique);
                if (hull.Shape.Planes.Length < 4) { f.Problem(where, "hull must enclose a volume"); continue; }
                hulls.Add(hull);
            }
            var sockets = new List<CompoundSocket>();
            foreach (JsonElement s in f.List(a, "sockets", where, required: false))
                sockets.Add(new CompoundSocket(f.Text(s, "name", where), f.Text(s, "part", where), f.Vector(s, "at", where)));
            assemblies.Add(new CompoundAssembly { Key = key, Size = f.Vector(a, "size", where), Parts = parts, Hulls = hulls, Sockets = sockets });
        }
        r.Loaded.Compound = new CompoundCatalog(assemblies);
    }

    public void Check(ContentChecking c)
    {
        CompoundCatalog catalog = c.Loaded.Compound ?? CompoundCatalog.Empty;
        c.Duplicates(File, catalog.Assemblies.Select(a => a.Key));
        foreach (CompoundAssembly a in catalog.Assemblies)
        {
            string where = File + ": " + a.Key;
            c.Duplicates(where, a.Parts.Select(p => p.Name));
            if (a.Size.X <= 0 || a.Size.Y <= 0 || a.Size.Z <= 0 || !float.IsFinite(a.Size.LengthSquared()) || a.Hulls.Count == 0)
                c.Problem(where, "needs positive finite size and collision hulls");
            foreach (CompoundPart p in a.Parts)
                if (p.Parent != null && (p.Parent == p.Name || a.Part(p.Parent) == null)) c.Problem(where, $"unknown or self parent '{p.Parent}'");
            foreach (CompoundHull h in a.Hulls)
                if (a.Part(h.Part) == null) c.Problem(where, $"hull names unknown part '{h.Part}'");
            foreach (CompoundSocket s in a.Sockets)
                if (a.Part(s.Part) == null) c.Problem(where, $"socket names unknown part '{s.Part}'");
        }
        foreach (DeployableDef d in c.Loaded.Deployables)
            if (d.Assembly != null && catalog.Find(d.Assembly) == null) c.Problem("deployables.json: " + d.Key, "unknown compound assembly " + d.Assembly);
    }
}
