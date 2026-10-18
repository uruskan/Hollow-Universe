using System.Collections.Generic;
using Hollow.Core;
using Hollow.Simulation.Content;
using Hollow.Simulation.Nature;
using Hollow.Surface;
using HollowUniverse.Surface;
using HollowUniverse.Surface.Flora;

namespace HollowUniverse.Ground;

/// <summary>
/// The ground game's plants (docs/WHOLE_WORLD.md §10.3): the zone's natural objects, the very ones its server has
/// (<see cref="ZoneSpec.CreateNature"/>: the planet's scatter on a real surface, the stand-in's placement on the stand-in
/// world), by the tiles they are placed on. Scatter mappings describe placement, not renderer ownership. Only kinds
/// the actual flora library can draw belong to vegetation; the rest (outcrops, pickups) are <see cref="NatureView"/>'s
/// models. Its index in its tile is its place in the tile's list.
/// </summary>
public sealed class NaturePlants : IPlantSource
{
    private readonly INaturalObjects _nature;
    private readonly ZoneFrame _frame;
    private readonly double _radius;
    private readonly ScatterKind?[] _kinds;
    private HashSet<ScatterKind>? _drawnKinds;

    public NaturePlants(INaturalObjects nature, GameContent content, ZoneFrame frame, double radius)
    {
        _nature = nature;
        _frame = frame;
        _radius = radius;
        _kinds = new ScatterKind?[content.NaturalKinds.Count];
        foreach (NaturalKind kind in content.NaturalKinds)
        {
            _kinds[kind.Index] = kind.ScatterKinds.Count > 0 ? kind.ScatterKinds[0] : null;
        }
    }

    public int Level => _nature.Level;

    /// <summary>Renderer ownership is known once the world's actual flora library has been built.</summary>
    public bool Ready => _drawnKinds != null;

    public void BindLibrary(FloraLibrary library)
    {
        var drawn = new HashSet<ScatterKind>();
        foreach (ScatterKind? kind in _kinds)
            if (kind is { } value && library.For(value) is { Variants.Count: > 0 }) drawn.Add(value);
        _drawnKinds = drawn;
    }

    /// <summary>Whether the active flora renderer has geometry for this natural kind (the rest are models).</summary>
    public bool Draws(ushort kind) => kind < _kinds.Length && _kinds[kind] is { } scatter &&
        _drawnKinds?.Contains(scatter) == true;

    public IReadOnlyList<ScatterItem> Tile(TileKey tile, bool treesOnly)
    {
        IReadOnlyList<NaturalObject> objects = _nature.InTile(tile);
        var items = new List<ScatterItem>(objects.Count);
        for (int i = 0; i < objects.Count; i++)
        {
            NaturalObject o = objects[i];
            if (o.Kind >= _kinds.Length || _kinds[o.Kind] is not { } kind || treesOnly && kind > ScatterKind.Broadleaf)
            {
                continue;
            }
            Vector3D body = _frame.ToBody(o.Position);
            double r = body.Length;
            byte variant = (byte)(Hash64.Of(o.Id) >> 56);
            items.Add(new ScatterItem(i, kind, variant, body / r, (float)(r - _radius), o.Yaw, o.Scale));
        }
        return items;
    }

    public ulong Id(TileKey tile, in ScatterItem item)
    {
        IReadOnlyList<NaturalObject> objects = _nature.InTile(tile);
        return (uint)item.Index < (uint)objects.Count ? objects[item.Index].Id : 0;
    }
}
