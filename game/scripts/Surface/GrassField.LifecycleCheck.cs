using System;
using Godot;
using Hollow.Surface;

namespace HollowUniverse.Surface;

public sealed partial class GrassField
{
    /// <summary>
    /// Tiny native resource fixture for the opt-in lifetime probe. Uses the production workers, tile builds (from the
    /// surface itself: there is no drawn terrain), uploads, pooling, authored rebatching and visibility. Fixture meshes and
    /// materials are not appearance or performance evidence.
    /// </summary>
    internal void SetupLifecycleFixture(ISurface surface, RenderFrame frame)
    {
        BeginSetup();
        Visible = true;
        _surface = surface;
        _frame = frame;
        _terrain = null;
        BuiltWith = (1, 1);
        _far = 8;
        _litterFar = 6;
        Configure(TileKey.LevelFor(surface.Radius, TerrainLod.FinestTileMetres));
        var shader = new Shader { Code = "shader_type spatial; void fragment() { ALBEDO = vec3(0.2, 0.4, 0.1); }" };
        var material = new ShaderMaterial { Shader = shader };
        var quad = new QuadMesh { Size = new Vector2(0.3f, 0.3f) };
        GroundCoverHabitats habitats = GroundCoverHabitats.Default;
        var species = new Species[habitats.Species.Length + 1];
        var shapes = new SpeciesShape[species.Length];
        for (int i = 0; i < species.Length; i++)
        {
            bool litter = i == habitats.Species.Length;
            string key = litter ? "litter" : habitats.Species[i];
            species[i] = new Species
            {
                Key = key, Litter = litter, Meshes = new Mesh[] { quad }, Materials = new[] { material }, Cards = new[] { 1 },
                Height = 0.3f, Reach = 0.15f,
            };
            ConfigureDistances(species[i]);
            shapes[i] = new SpeciesShape(key, 0.3f, 0.15f, litter);
        }
        _species = species;
        var catalog = new SpeciesCatalog { Species = species, Shapes = shapes, Litter = species.Length - 1 };
        _context = CaptureContext(surface, catalog, new TintNoise(), 1);
        _coverRoot = new Node3D { Name = "AuthoredGroundCover" };
        AddChild(_coverRoot);
        var art = new CoverMesh(quad, material, 2);
        for (int i = 0; i < _coverBatches.Length; i++)
        {
            var instance = new MultiMeshInstance3D
            {
                Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                    UseCustomData = true, Mesh = quad },
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
