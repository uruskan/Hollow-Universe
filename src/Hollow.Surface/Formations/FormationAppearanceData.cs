using System;
using System.Collections.Generic;

namespace Hollow.Surface;

/// <summary>Independent authoring-chain pins. Authentication does not approve art or grant physical capabilities.</summary>
public sealed record FormationAppearancePins(string ModuleReceiptSha256, string TangentReceiptSha256, string RecipeSha256);

/// <summary>
/// Immutable additive CUSTOM0 data bound to exact authenticated visual streams. Channels are scan-to-substrate,
/// substrate-to-terrain, cap weight and toe weight. This object grants no material, placement or root permissions.
/// Its caller retains the visual payload and charges loading/retiring data separately from native upload.
/// </summary>
public sealed class FormationAppearanceData
{
    internal FormationAppearanceData(string receipt, string visualReceipt, FormationAppearanceLod[] lods, long bytes)
    { ReceiptSha256 = receipt; VisualReceiptSha256 = visualReceipt; Lods = Array.AsReadOnly(lods); EstimatedResidentBytes = bytes; }
    public string ReceiptSha256 { get; }
    public string VisualReceiptSha256 { get; }
    public IReadOnlyList<FormationAppearanceLod> Lods { get; }
    public long EstimatedResidentBytes { get; }
}

public sealed class FormationAppearanceLod
{
    internal FormationAppearanceLod(FormationVisualLod visual, FormationAppearancePrimitive[] primitives)
    { GeometryKey = visual.GeometryKey; GeometryRevision = visual.GeometryRevision; LodKey = visual.LodKey; Primitives = Array.AsReadOnly(primitives); }
    public string GeometryKey { get; }
    public int GeometryRevision { get; }
    public string LodKey { get; }
    public IReadOnlyList<FormationAppearancePrimitive> Primitives { get; }
}

public sealed class FormationAppearancePrimitive
{
    internal FormationAppearancePrimitive(int ordinal, float[] values)
    { Ordinal = ordinal; Values = new FormationVisualArray<float>(values); }
    public int Ordinal { get; }
    public int VertexCount => Values.Count / 4;
    public FormationVisualArray<float> Values { get; }
}
