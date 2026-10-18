using System;
using Godot;
using Hollow.Surface;

namespace HollowUniverse.Surface;

public sealed partial class GrassField
{
    /// <summary>
    /// Tiny native resource fixture for the opt-in lifetime probe. Uses the production workers, lattice construction,
    /// upload, authored rebatching and visibility logic. Fixture materials are not appearance/performance evidence.
    /// </summary>
    internal void SetupLifecycleFixture(ISurface surface, RenderFrame frame)
    {
        BeginSetup();
        Visible = true;
        _surface = surface;
        _frame = frame;
        _level = TileKey.LevelFor(surface.Radius, CellMetres);
        _faceCells = 1L << _level;
        _cellMetres = TileKey.EdgeLength(_level, surface.Radius);
        _heightShift = TileKey.LevelFor(surface.Radius, TerrainLod.FinestTileMetres) + TileCellsLog2;
        BuiltWith = (1, 1);
        _far = 8;
        _gridWidth = 9;
        _litterWidth = 7;
        _latticeSize = 13;
        _buildContext = CaptureBuildContext(surface, frame);

        var shader = new Shader
        {
            Code = "shader_type spatial; uniform sampler2D lattice; uniform sampler2D lattice_cover; " +
                "uniform int face; uniform ivec2 lattice_cell; uniform vec2 face_origin; uniform ivec2 grid_offset; " +
                "uniform vec3 anchor; uniform vec4 trample; void fragment() { ALBEDO = vec3(0.2, 0.4, 0.1); }",
        };
        var material = new ShaderMaterial { Shader = shader };
        Mesh tuft = TuftMesh(), litter = LitterMesh();
        for (int g = 0; g < _grids.Length; g++)
        {
            var grassMaterial = (ShaderMaterial)material.Duplicate();
            var litterMaterial = (ShaderMaterial)material.Duplicate();
            _grids[g] = new FaceGrid
            {
                GrassMaterial = grassMaterial, LitterMaterial = litterMaterial,
                Grass = Instance(tuft, grassMaterial, _gridWidth),
                Litter = Instance(litter, litterMaterial, _litterWidth),
            };
        }
        _coverRoot = new Node3D { Name = "AuthoredGroundCover" };
        AddChild(_coverRoot);
        var art = new CoverMesh(tuft, material, 30);
        for (int i = 0; i < _coverBatches.Length; i++)
        {
            var instance = new MultiMeshInstance3D
            {
                Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                    UseCustomData = true, Mesh = tuft },
                MaterialOverride = material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            };
            _coverRoot.AddChild(instance);
            _coverBatches[i] = new CoverBatch { Instance = instance, Art = art };
        }
        _coverReported = true;
        _setupComplete = true;
    }
}
