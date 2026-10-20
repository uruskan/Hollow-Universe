using Hollow.Client.Effects;

namespace Hollow.Tests;

/// <summary>
/// VEHICLES.md §9.9: a diesel's smoke from plain inputs. A puff on starting, haze at idle, soot that comes in quickly with
/// the load and clears slowly, a puff on a stab of the throttle (not on a slow push), one on a gear change, nothing stopped.
/// </summary>
public static class VehicleSmokeTests
{
    private const float Step = 1f / 60;

    private static void Run(VehicleSmoke s, float seconds, bool running, float throttle, out float puffs)
    {
        puffs = 0;
        for (float t = 0; t < seconds; t += Step)
        {
            s.Update(running, throttle, System.Math.Abs(throttle), false, Step);
            puffs += s.Puff > 0 ? 1 : 0;
        }
    }

    [Test]
    public static void VehicleSmokeFollowsTheEngine()
    {
        var s = new VehicleSmoke();
        Run(s, 1, false, 0, out float puffs);
        Assert.Equal(0f, s.Haze + s.Soot + puffs, "stopped: no smoke");
        s.Update(true, 0, 0, false, Step);
        Assert.True(s.Puff >= 1.4f, "starting puffs black");
        Run(s, 2, true, 0, out puffs);
        Assert.Equal(0f, puffs, "idling: no more puffs");
        Assert.InRange(0.99, 1.0, s.Haze, "idling: the haze");
        Assert.True(s.Soot < 0.01f, "idling: no soot");

        s.Update(true, 1, 1, false, Step);
        Assert.True(s.Puff > 0, "a stab of the throttle puffs");
        Run(s, 0.5f, true, 1, out puffs);
        Assert.Equal(0f, puffs, "held down: one puff");
        Assert.True(s.Soot > 0.9f, $"under load: soot within half a second ({s.Soot:0.00})");
        Assert.True(s.Haze < 0.4f, "under load: the haze gives way");

        Run(s, 0.5f, true, 0, out _);
        Assert.True(s.Soot > 0.3f && s.Soot < 0.9f, $"let go: the soot clears slowly ({s.Soot:0.00})");
        Run(s, 4, true, 0, out _);

        // A slow push on the throttle is no stab.
        float total = 0;
        for (float t = 0; t < 2; t += Step)
        {
            s.Update(true, t / 2, t / 2, false, Step);
            total += s.Puff;
        }
        Assert.Equal(0f, total, "a slow push: no puff");
        s.Update(true, 1, 1, shifted: true, Step);
        Assert.True(s.Puff > 0.8f, "a gear change under load puffs");
    }
}
