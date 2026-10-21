using System;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>Exact frozen body-space position at a canonical terrain vertex; never a height callback.</summary>
public readonly record struct FormationTerrainPin(CubeVertexKey Vertex, Vector3D Position);

/// <summary>
/// Evidence created only by the field builder after every interval of an authored loop was proved buried.
/// The physical definition and placement are immutable references/values. Guard pins require these exact fixed
/// positions and topology in terrain consumers; this does not authorize deformation, morphing, or planting.
/// </summary>
public sealed class FormationContactSealReceipt
{
    private readonly TerrainLatticeTriangle[] _witnesses;
    private readonly FormationTerrainPin[] _pins;
    private readonly TileKey[] _tiles;
    internal FormationContactSealReceipt(FormationPiece piece, FormationContactLoop loop, FormationFieldContract contract,
        string physicalContentDigest, string generationDigest, TerrainLatticeTriangle[] witnesses,
        FormationTerrainPin[] pins, TileKey[] tiles, FormationTerrainSealDiagnostics diagnostics)
    {
        Piece = piece; Loop = loop; Contract = contract; PhysicalContentDigest = physicalContentDigest;
        GenerationDigest = generationDigest; _witnesses = witnesses; _pins = pins; _tiles = tiles; Diagnostics = diagnostics;
    }
    public FormationPiece Piece { get; }
    public FormationContactLoop Loop { get; }
    public FormationFieldContract Contract { get; }
    public string PhysicalContentDigest { get; }
    public string GenerationDigest { get; }
    public ReadOnlySpan<TerrainLatticeTriangle> Witnesses => _witnesses;
    public ReadOnlySpan<FormationTerrainPin> Pins => _pins;
    public ReadOnlySpan<TileKey> DependencyTiles => _tiles;
    public FormationTerrainSealDiagnostics Diagnostics { get; }
}

/// <summary>Immutable authoritative geometry/dependency union. Runtime view readiness belongs to the renderer.</summary>
public sealed class FormationCohort
{
    private readonly FormationPiece[] _pieces;
    private readonly FormationContactSealReceipt[] _receipts;
    private readonly TerrainLatticeTriangle[] _witnesses;
    private readonly FormationTerrainPin[] _pins;
    private readonly TileKey[] _tiles;
    internal FormationCohort(FormationId id, FormationPiece[] pieces, FormationContactSealReceipt[] receipts,
        TerrainLatticeTriangle[] witnesses, FormationTerrainPin[] pins, TileKey[] tiles)
    { Id = id; _pieces = pieces; _receipts = receipts; _witnesses = witnesses; _pins = pins; _tiles = tiles; }
    public FormationId Id { get; }
    public ReadOnlySpan<FormationPiece> Pieces => _pieces;
    public ReadOnlySpan<FormationContactSealReceipt> Receipts => _receipts;
    public ReadOnlySpan<TerrainLatticeTriangle> Witnesses => _witnesses;
    public ReadOnlySpan<FormationTerrainPin> Pins => _pins;
    public ReadOnlySpan<TileKey> DependencyTiles => _tiles;
}
