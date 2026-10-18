using System.Threading.Tasks;
using Godot;

namespace HollowUniverse.Ground;

public sealed partial class EnvironmentRoute
{
    // Optional views of the actual walked checkpoint. The character never moves or changes collision/cheats.
    // Lifted views are explicitly labelled diagnostics, not evidence of normal eye height.
    private async Task CaptureDetailViews(string label)
    {
        float lift = View.CameraLift, pitch = View.Pitch;
        int cap = Engine.MaxFps;
        var vsync = DisplayServer.WindowGetVsyncMode();
        try
        {
            UncapPerformance();
            await SettlePerformance();
            await SamplePerformance(label + "-ground", null);
            View.Pitch = -0.75f;
            await WaitUntilReady(label + "-roots", 30);
            await Capture(label + "-roots");
            View.CameraLift = 9;
            View.Pitch = -1.0f;
            await WaitUntilReady(label + "-elevated", 30);
            await Capture(label + "-elevated");
        }
        finally
        {
            if (!_stopping && GodotObject.IsInstanceValid(View))
            {
                View.CameraLift = lift;
                View.Pitch = pitch;
                Engine.MaxFps = cap;
                DisplayServer.WindowSetVsyncMode(vsync);
                await WaitUntilReady(label + "-restored", 30);
            }
        }
    }
}
