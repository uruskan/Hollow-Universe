using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollow.Core;
using Hollow.Galaxy;
using HollowUniverse.Common;

namespace HollowUniverse.GalaxyMap;

/// <summary>The finite playable galaxy, resident as one small point mesh. No unbounded cell streaming.</summary>
public partial class StarStreamer : Node3D
{
    private GalaxyStar[] _stars = Array.Empty<GalaxyStar>();
    private MeshInstance3D? _mesh;
    private Vector3D _focus;
    private double _radius = 1000;
    private bool _built;

    public Galaxy? Galaxy { get; set; } // retained for development-view callers
    public PlayableGalaxy? Playable { get; set; }
    public ShaderMaterial? Material { get; set; }
    public Vector3D RenderOrigin { get; private set; }
    public int LoadedStars => _stars.Length;
    public int LoadedBlocks => _built ? 1 : 0;
    public int PendingBlocks => _built ? 0 : 1;
    public bool IsIdle => _built;
    public double ApplyBudgetMs { get; set; } = 4;
    public const double LevelBudget = PlayableGalaxy.MaximumStars;
    public static readonly double[] LevelMaxRadius = { 10000, 10000, 10000, 10000, 10000, 10000, 10000, 10000 };
    public const int BucketsPerEdge = 8;

    public static double ViewRadius(double distance) => Math.Max(1.5 * distance, 6);
    public static bool LevelVisible(int level, double distance) => level == 0;
    public double LevelRadius(int level) => level == 0 ? _radius : 0;
    public bool IsBudgetLimited(int level) => false;
    public double ShownCount(int level) => level == 0 ? _stars.Length : 0;
    public double NearbyRadius(double count) => _radius;
    public bool IsShown(in GalaxyStar star, double fraction = 1) => Playable?.Contains(star.Address) == true;

    public void SetView(Vector3D focus, double distance)
    {
        _focus = focus;
        _radius = Math.Max(1000, 2 * ((Playable?.MapRadius ?? 500) + focus.Length));
    }

    public void SetRenderOrigin(Vector3D origin)
    {
        RenderOrigin = origin;
        if (_mesh != null) _mesh.Position = ToRender(Vector3D.Zero);
    }

    public Vector3 ToRender(Vector3D position)
    {
        Vector3D relative = position - RenderOrigin;
        return new Vector3((float)relative.X, (float)relative.Y, (float)relative.Z);
    }

    public readonly record struct StarBlock(Vector3D Min, double Size, GalaxyStar[] Stars, int[] BucketStarts)
    {
        public double BucketSize => Size / BucketsPerEdge;
        public Vector3D BucketCentre(int bucket) => Min + new Vector3D(
            (bucket % 8 + 0.5) * BucketSize, (bucket / 8 % 8 + 0.5) * BucketSize, (bucket / 64 + 0.5) * BucketSize);
    }

    public IEnumerable<StarBlock> BlocksWithStars()
    {
        if (_stars.Length == 0) yield break;
        double radius = (Playable?.MapRadius ?? 500) + 1;
        // At most 1000 entries: map picking scans this block directly, without spatial buckets.
        yield return new StarBlock(new Vector3D(-radius, -radius, -radius), radius * 2, _stars, new[] { 0, _stars.Length });
    }

    public override void _Process(double delta)
    {
        if (_built || Playable == null || Material == null) return;
        _stars = Playable.Systems.Select(entry => entry.MapStar).ToArray();
        var positions = new Vector3[_stars.Length];
        var colors = new Color[_stars.Length];
        var data = new Vector2[_stars.Length];
        for (int i = 0; i < _stars.Length; i++)
        {
            GalaxyStar star = _stars[i];
            positions[i] = new Vector3((float)star.Position.X, 0, (float)star.Position.Z);
            colors[i] = StarColors.For(star.Class, star.Physics.Temperature);
            // Map symbols retain spectral colour, with readable sizes independent of physical luminosity.
            data[i] = new Vector2(star.Address.Equals(Hollow.Galaxy.Systems.SolSystem.Address) ? 1.6f : 1, 0);
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = positions;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.TexUV] = data;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Points, arrays);
        _mesh = new MeshInstance3D
        {
            Mesh = mesh, MaterialOverride = Material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            ExtraCullMargin = (float)Playable.MapRadius,
        };
        AddChild(_mesh);
        _mesh.Position = ToRender(Vector3D.Zero);
        _built = true;
    }
}
