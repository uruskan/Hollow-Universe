using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Hollow.Core;
using Hollow.Surface;
using HollowUniverse.Surface.Flora;

namespace HollowUniverse.Surface;

public partial class EnvironmentLifetimeCheck
{
    // Render helpers the native probes share (they began with the retired vertex-grass render probe).
    private static ArrayMesh GrassRenderGround(DrawnTerrainTile tile, Vector3 camera)
    {
        var vertices = new Vector3[33 * 33];
        var normals = new Vector3[vertices.Length];
        float count = 1 << tile.Key.Level;
        for (int y = 0; y <= 32; y++)
        for (int x = 0; x <= 32; x++)
        {
            Vector2 uv = new((tile.Key.X + x / 32f) / count, (tile.Key.Y + y / 32f) / count);
            if (!TerrainLod.TrySampleContact(tile, tile.Key, uv, camera, out TerrainContactSample sample))
                throw new InvalidOperationException("Grass ground fixture has an unsupported drawn vertex.");
            vertices[y * 33 + x] = sample.RenderPosition;
            normals[y * 33 + x] = sample.GeometricNormal;
        }
        using var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Index] = TileMesh.Indices(32);
        var mesh = new ArrayMesh();
        try { mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays); return mesh; }
        catch { mesh.Dispose(); throw; }
    }

    private static ImageTexture GrassRenderFloatTexture(float[] values, int width, int height)
    {
        var bytes = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        using Image image = Image.CreateFromData(width, height, false, Image.Format.Rgbaf, bytes);
        return ImageTexture.CreateFromImage(image);
    }

    private static ImageTexture GrassRenderSolid(Color colour)
    {
        using Image image = Image.CreateEmpty(4, 4, false, Image.Format.Rgba8);
        image.Fill(colour); image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    private static ImageTexture3D GrassRenderVolume(Color colour)
    {
        using Image image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        image.Fill(colour);
        var images = new Godot.Collections.Array<Image> { image };
        using var imageStorage = (Godot.Collections.Array)images;
        var texture = new ImageTexture3D();
        try
        {
            if (texture.Create(Image.Format.Rgba8, 1, 1, 1, false, images) != Error.Ok)
                throw new InvalidOperationException("Cannot create neutral diagnostic atmosphere volume.");
            return texture;
        }
        catch { texture.Dispose(); throw; }
    }

    private static int GrassRenderDifference(Image first, Image second)
    {
        byte[] a = first.GetData(), b = second.GetData();
        if (a.Length != b.Length) throw new InvalidOperationException("Grass comparison image sizes differ.");
        int count = 0;
        for (int p = 0; p < a.Length; p += 4)
            if (Math.Max(Math.Abs(a[p] - b[p]), Math.Max(Math.Abs(a[p + 1] - b[p + 1]), Math.Abs(a[p + 2] - b[p + 2]))) > 6) count++;
        return count;
    }

}
