using System;

namespace HollowUniverse.Surface;

// What the loading pipeline reads of the terrain (GROUND.md §4.3.1).
public sealed partial class TerrainLod
{
    /// <summary>Tiles put in place a frame while nobody sees the frames (behind the loading screen).</summary>
    private const int HurriedMeshesPerFrame = 64;

    /// <summary>The view is behind the loading screen: finished tiles go in place as fast as they come.</summary>
    public bool Hurry { get; set; }

    /// <summary>The finest level among the tiles drawn last frame (the camera's own tiles, once streamed).</summary>
    public int FinestLevel
    {
        get
        {
            int finest = 0;
            foreach (Node n in _drawn)
            {
                finest = Math.Max(finest, n.Key.Level);
            }
            return finest;
        }
    }
}
