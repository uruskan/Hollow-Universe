using System;
using Hollow.Client.Audio;
using Hollow.Client.Effects;
using Hollow.Client.Vehicles;

namespace Hollow.Tests;

/// <summary>
/// VEHICLES.md §9.10, what an overboosted diesel shows and sounds like: the revs' turbo pushed to its most and its release
/// when Shift is let go; the engine rocking on its mounts (a lurch on a stab, a kick at a gear change, a shake at idle,
/// harder overboosted) and the chassis twisting on a free rev standing still; heavy black smoke.
/// </summary>
public static class TruckFeelTests
{
    private const float Dt = 1f / 60f;

    private static EngineSoundData Diesel => new()
    {
        Kind = "truck", IdleRpm = 750, Redline = 3000, Limiter = 3100, Gears = new[] { 4.2f, 7f, 10.5f, 15f, 20.6f }, Reverse = 5,
        Response = 0.22f, Upshift = 0.85f, Downshift = 0.45f, ShiftTime = 0.45f, Launch = 0.42f, TurboSpool = 0.8f,
    };

    [Test]
    public static void OverboostSpoolsTheTurboHarderAndReleasesIt()
    {
        float Pull(bool boosting, out VehicleRevs revs)
        {
            revs = new VehicleRevs(Diesel);
            for (int i = 0; i < 60; i++)
            {
                revs.Step(5, 1, true, true, Dt, boosting);
            }
            return revs.Boost;
        }
        float plain = Pull(false, out _), boosted = Pull(true, out VehicleRevs r);
        Fixtures.Report($"a second's hard pull at 5 m/s: boost {plain:0.00}; overboosted {boosted:0.00}, overboost {r.Overboost:0.00}");
        Assert.True(boosted > plain + 0.15f && boosted > 0.6f, "overboosted the turbo spools higher, sooner");
        Assert.True(r.Overboost > 0.95f, "the overboost in within a second");
        int released = 0;
        for (int i = 0; i < 60; i++)
        {
            r.Step(5, 1, true, true, Dt, boosting: false);
            released += r.Released ? 1 : 0;
        }
        Assert.Equal(1, released, "Shift let go on the throttle: one release of the turbo");
        Assert.True(r.Overboost < 0.1f, "the overboost gone");
        var stopped = new VehicleRevs(Diesel);
        stopped.Step(0, 0, false, true, Dt, boosting: true);
        Assert.Equal(0f, stopped.Overboost, "no overboost with the engine stopped");
    }

    [Test]
    public static void TheEngineRocksWithItsTorque()
    {
        var revs = new VehicleRevs(Diesel);
        var rock = new EngineRock();
        float shake = 0;
        for (int i = 0; i < 120; i++)
        {
            revs.Step(0, 0, true, true, Dt);
            rock.Step(revs, 0, Dt);
            shake = MathF.Max(shake, MathF.Abs(rock.Shake));
        }
        Fixtures.Report($"idling: lean {rock.Lean:0.0000}, shake up to {shake:0.0000} rad");
        Assert.True(MathF.Abs(rock.Lean) < 0.005f && shake > EngineRock.IdleShake * 0.5f, "at idle it stands and shakes");

        // Pulling gently in second, then a stab of the throttle: it lurches past where it settles, then settles leaned.
        for (int i = 0; i < 90; i++)
        {
            revs.Step(4, 0.3f, true, true, Dt);
            rock.Step(revs, 4, Dt);
        }
        float most = 0;
        for (int i = 0; i < 90; i++)
        {
            revs.Step(4, 1, true, true, Dt);
            rock.Step(revs, 4, Dt);
            most = MathF.Max(most, rock.Lean);
        }
        float settled = rock.Lean;
        Fixtures.Report($"a stab: up to {most:0.000} rad, settled at {settled:0.000}");
        Assert.True(most > settled * 1.1f && settled > EngineRock.MostLean * 0.6f, "a lurch past its lean, then held leaned under load");

        // Overboosted it leans harder.
        for (int i = 0; i < 90; i++)
        {
            revs.Step(4, 1, true, true, Dt, boosting: true);
            rock.Step(revs, 4, Dt);
        }
        Fixtures.Report($"overboosted: lean {rock.Lean:0.000} rad");
        Assert.True(rock.Lean > settled * 1.4f, "overboosted it leans harder");

        // A gear change kicks it.
        var change = new VehicleRevs(Diesel);
        var kicked = new EngineRock();
        float speed = 2, swing = 0;
        bool shifted = false;
        for (int i = 0; i < 400 && !shifted; i++)
        {
            speed += 2.5f * Dt;
            change.Step(speed, 1, true, true, Dt);
            float before = kicked.Lean;
            kicked.Step(change, speed, Dt);
            if (change.Shifted)
            {
                shifted = true;
                for (int k = 0; k < 15; k++)
                {
                    change.Step(speed, 1, true, true, Dt);
                    kicked.Step(change, speed, Dt);
                    swing = MathF.Max(swing, before - kicked.Lean);
                }
            }
        }
        Fixtures.Report($"a gear change: swung back {swing:0.000} rad");
        Assert.True(shifted && swing > 0.01f, "a gear change swings it back");
    }

    [Test]
    public static void AFreeRevStandingStillTwistsTheChassis()
    {
        float Twist(float speed)
        {
            var revs = new VehicleRevs(Diesel);
            var rock = new EngineRock();
            for (int i = 0; i < 60; i++)
            {
                revs.Step(speed, 0, true, true, Dt);
                rock.Step(revs, speed, Dt);
            }
            float most = 0;
            for (int i = 0; i < 30; i++)
            {
                // Standing still the gearbox is in neutral and the revs run free.
                revs.Step(speed, speed < 0.1f ? 1 : 0.3f, true, true, Dt);
                rock.Step(revs, speed, Dt);
                most = MathF.Max(most, MathF.Abs(rock.Twist));
            }
            return most;
        }
        float still = Twist(0), rolling = Twist(12);
        Fixtures.Report($"a free rev: the chassis twists {still * 180 / MathF.PI:0.00}° standing, {rolling * 180 / MathF.PI:0.00}° rolling at 12 m/s");
        Assert.True(still > 0.005f && still > rolling * 2, "standing still a free rev twists the chassis");
    }

    [Test]
    public static void OverboostPoursBlackSmoke()
    {
        var s = new VehicleSmoke();
        for (int i = 0; i < 60; i++)
        {
            s.Update(true, 1, 1, false, Dt);
        }
        Assert.Equal(0f, s.Black, "no black smoke without the overboost");
        for (int i = 0; i < 30; i++)
        {
            s.Update(true, 1, 1, false, Dt, overboost: 1);
        }
        Assert.True(s.Black > 0.9f, $"overboosted under load: black within half a second ({s.Black:0.00})");
        s.Update(true, 1, 1, shifted: true, Dt, overboost: 1);
        float overboosted = s.Puff;
        var plain = new VehicleSmoke();
        plain.Update(true, 1, 1, false, Dt);
        plain.Update(true, 1, 1, shifted: true, Dt);
        Assert.True(overboosted > plain.Puff * 1.4f, "a gear change overboosted puffs harder");
        for (int i = 0; i < 30; i++)
        {
            s.Update(true, 1, 1, false, Dt, overboost: 0);
        }
        Assert.True(s.Black > 0.4f && s.Black < 0.9f, $"let go: the black clears slowly ({s.Black:0.00})");
    }

    /// <summary>A locomotive's billows: overboosted several a second, quicker with the revs; pulling hard, fewer; idling, none.</summary>
    [Test]
    public static void BillowsComeInTheEnginesRhythm()
    {
        (int count, float biggest) Billows(float load, float overboost, float rpm)
        {
            var s = new VehicleSmoke();
            int n = 0;
            float most = 0;
            for (int i = 0; i < 60 * 6; i++)
            {
                s.Update(true, load, load, false, Dt, overboost, rpm);
                if (i >= 60 && s.Billow > 0)
                {
                    n++;
                    most = MathF.Max(most, s.Billow);
                }
            }
            return (n, most);
        }
        var boosted = Billows(1, 1, 1500);
        var boostedHigh = Billows(1, 1, 2300);
        var pulling = Billows(1, 0, 1500);
        var idling = Billows(0, 0, 600);
        Fixtures.Report($"five seconds: overboosted {boosted.count} billows at 1,500 rpm, {boostedHigh.count} at 2,300 (biggest {boosted.biggest:0.00}); pulling hard {pulling.count} ({pulling.biggest:0.00}); idling {idling.count}");
        Assert.InRange(10, 14, boosted.count, "overboosted at 1,500 rpm: a billow every 0.42 s");
        Assert.True(boostedHigh.count > boosted.count, "quicker with the revs");
        Assert.True(pulling.count is > 0 && pulling.count < boosted.count / 2 + 1 && pulling.biggest < boosted.biggest, "pulling hard: fewer and smaller");
        Assert.Equal(0, idling.count, "idling: none");
    }

    [Test]
    public static void OverboostedTheEngineShakesHard()
    {
        float Most(float load, bool boosting, out float shudder)
        {
            var revs = new VehicleRevs(Diesel);
            var rock = new EngineRock();
            float most = 0;
            shudder = 0;
            for (int i = 0; i < 120; i++)
            {
                revs.Step(4, load, true, true, Dt, boosting);
                rock.Step(revs, 4, Dt);
                if (i >= 60)
                {
                    most = MathF.Max(most, MathF.Abs(rock.Shake));
                    shudder = MathF.Max(shudder, MathF.Abs(rock.Shudder));
                }
            }
            return most;
        }
        float idle = Most(0, false, out _), pulling = Most(1, false, out _), boosted = Most(1, true, out float shudder);
        Fixtures.Report($"the engine's shake: idling {idle * 180 / MathF.PI:0.0}°, pulling {pulling * 180 / MathF.PI:0.0}°, overboosted {boosted * 180 / MathF.PI:0.0}° (the body {shudder * 180 / MathF.PI:0.0}°)");
        Assert.True(pulling > idle * 1.3f, "it shakes harder pulling than idling");
        Assert.True(boosted > pulling * 2.5f && boosted > 0.035f, "overboosted it shakes its head: two degrees and more");
        Assert.True(shudder > 0.01f, "and the whole vehicle shudders with it");
    }
}
