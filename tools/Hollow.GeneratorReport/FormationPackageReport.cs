using System;
using System.Diagnostics;
using System.Text.Json;
using Hollow.Core;
using Hollow.Surface;

namespace Hollow.GeneratorReport;

/// <summary>Explicit offline package qualification; never selects a live generation recipe or writes assets.</summary>
internal static class FormationPackageReport
{
    public static int Run(string[] args)
    {
        if (args.Length != 3 && (args.Length != 5 || args[3] != "--chart-y-up"))
        {
            Console.Error.WriteLine("usage: formation-package <package-directory> <trusted-receipt-sha256> [--chart-y-up <patch-key>]");
            return 2;
        }
        long allocated = GC.GetTotalAllocatedBytes();
        var clock = Stopwatch.StartNew();
        try
        {
            FormationPackage package = FormationPackageLoader.Load(args[1], Program.RepoRoot(), args[2]);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                phase = "package-loaded", receipt = package.ReceiptSha256, seconds = clock.Elapsed.TotalSeconds,
                allocatedBytes = GC.GetTotalAllocatedBytes() - allocated, serverGc = System.Runtime.GCSettings.IsServerGC,
                definitions = package.Catalog.Qualified.Length, catalogEstimatedResidentBytes = package.Catalog.EstimatedResidentBytes,
                productionActivated = false,
            }));
            foreach (CliffSupportDefinition definition in package.Catalog.Qualified)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { definition.Key, vertices = definition.Geometry.VertexCount,
                    triangles = definition.Geometry.TriangleCount, patches = definition.Patches.Length, contacts = definition.Contacts.Length,
                    lods = definition.Lods.Length, definition.EstimatedResidentBytes }));
                if (args.Length != 5) continue;
                long began = Stopwatch.GetTimestamp(), before = GC.GetTotalAllocatedBytes();
                FormationChartQualificationResult result = FormationSurfaceChart.Qualify(definition, args[4],
                    new FormationChartBasis(Vector3D.Zero, Vector3D.UnitX, -Vector3D.UnitZ));
                Console.WriteLine(JsonSerializer.Serialize(new { phase = "y-up-chart", patch = args[4], status = result.Status.ToString(),
                    seconds = Stopwatch.GetElapsedTime(began).TotalSeconds, allocatedBytes = GC.GetTotalAllocatedBytes() - before,
                    diagnostics = result.Diagnostics, supportPermissionsGranted = false }));
                if (result.Chart == null) return 1;
            }
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new { phase = "qualification-failed", error = error.GetType().Name,
                error.Message, seconds = clock.Elapsed.TotalSeconds, allocatedBytes = GC.GetTotalAllocatedBytes() - allocated }));
            return 1;
        }
    }
}
