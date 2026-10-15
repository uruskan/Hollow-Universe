using System.Collections.Generic;

namespace Hollow.Client.Streaming;

/// <summary>Changes to the natural object's drawn presence and model, independent of hit counters and cues.</summary>
public readonly record struct NaturalRenderChange(bool Plants, bool Models);

/// <summary>
/// Tracks only deviations from generated, present, stage-zero objects. Newly streamed overrides are compared with
/// that default too. A fall hides the standing plant until its separate presentation ends, even after an early respawn.
/// </summary>
public sealed class NaturalRenderState
{
    private readonly record struct Appearance(bool Hidden, int Stage);
    private readonly Dictionary<ulong, Appearance> _changed = new();

    /// <param name="stage">Null when the object's tile isn't cached; keep its last known model stage.</param>
    public NaturalRenderChange Observe(ulong id, bool depleted, bool falling = false, int? stage = null)
    {
        _changed.TryGetValue(id, out Appearance before);
        var current = new Appearance(depleted || falling, stage ?? before.Stage);
        if (current == default) _changed.Remove(id);
        else _changed[id] = current;
        bool presence = current.Hidden != before.Hidden;
        return new(presence, presence || !current.Hidden && current.Stage != before.Stage);
    }
}
