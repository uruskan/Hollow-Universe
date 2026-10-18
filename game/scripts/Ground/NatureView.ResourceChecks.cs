using Godot;

namespace HollowUniverse.Ground;

public partial class NatureView
{
    /// <summary>Actual streamed model instance, for hosted acceptance checks rather than catalogue-only checks.</summary>
    public bool TryRenderedNatural(ulong id, out Node3D? model, out int stage)
    {
        foreach (var tile in _outcrops.Values)
        {
            foreach (var entry in tile)
            {
                if (entry.Id != id) continue;
                model = entry.Node;
                stage = entry.Stage;
                return GodotObject.IsInstanceValid(model) && model.IsInsideTree() && !model.IsQueuedForDeletion();
            }
        }
        model = null;
        stage = -1;
        return false;
    }
}
