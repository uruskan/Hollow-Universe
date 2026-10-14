using System;
using System.Collections.Generic;
using Godot;

namespace HollowUniverse.Fx;

/// <summary>The effects library's textures and meshes in <c>res://assets/fx</c>, loaded once (GROUND.md §9.3).</summary>
public static class FxAssets
{
    public const string Root = "res://assets/fx/";

    private static readonly Dictionary<string, Texture2D> Textures = new(StringComparer.Ordinal);
    private static Dictionary<string, Mesh>? _chunks;

    /// <summary>A texture by its path under <c>res://assets/fx/</c>; a plain white one when it is missing.</summary>
    public static Texture2D Texture(string path)
    {
        if (Textures.TryGetValue(path, out Texture2D? t))
        {
            return t;
        }
        string full = Root + path;
        t = ResourceLoader.Exists(full) ? GD.Load<Texture2D>(full) : null;
        if (t == null)
        {
            GD.PushWarning($"FxAssets: {full} is missing.");
            var image = Image.CreateEmpty(4, 4, false, Image.Format.Rgba8);
            image.Fill(Colors.White);
            t = ImageTexture.CreateFromImage(image);
        }
        Textures[path] = t;
        return t;
    }

    /// <summary>A debris mesh of <c>chunks.glb</c> (chunk_stone, chunk_wood, chunk_metal); a small box when missing.</summary>
    public static Mesh ChunkMesh(string name)
    {
        if (_chunks == null)
        {
            _chunks = new Dictionary<string, Mesh>(StringComparer.Ordinal);
            const string path = Root + "chunks.glb";
            if (ResourceLoader.Exists(path))
            {
                Node3D scene = GD.Load<PackedScene>(path).Instantiate<Node3D>();
                foreach (Node child in scene.FindChildren("*", "MeshInstance3D", true, false))
                {
                    if (child is MeshInstance3D m && m.Mesh != null)
                    {
                        _chunks[m.Name] = m.Mesh;
                    }
                }
                scene.Free();
            }
        }
        if (_chunks.TryGetValue(name, out Mesh? mesh))
        {
            return mesh;
        }
        GD.PushWarning($"FxAssets: no {name} in chunks.glb.");
        return new BoxMesh { Size = Vector3.One * 0.8f };
    }
}
