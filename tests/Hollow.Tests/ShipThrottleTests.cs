using Hollow.Client;
using Hollow.Net.Protocol;
using Hollow.Simulation.Ships;

namespace Hollow.Tests;

public static class ShipThrottleTests
{
    // The live control's default full reverse-to-forward stroke is 1.6 seconds.
    private const float LeverRate = 2f / 1.6f;

    [Test]
    public static void ForwardAndReverseLeaveNeutralAtEveryFrameRateWithoutBoost()
    {
        foreach (int fps in new[] { 30, 60, 120, 144, 240 })
        foreach (int direction in new[] { -1, 1 })
        {
            float delta = direction * LeverRate / fps;
            float throttle = PilotSteering.AdjustThrottle(0, delta);
            Assert.True(throttle * direction > 0, $"{fps} FPS: the first W/S frame leaves neutral in direction {direction}");
            for (int frame = 1; frame < fps / 2; frame++)
                throttle = PilotSteering.AdjustThrottle(throttle, delta);

            Assert.Near(direction * 0.625f, throttle, 1e-5, $"{fps} FPS: half a second moves the same lever distance");
            ShipInput received = ShipConvert.ToShipInput(ShipConvert.ToControls(new ShipInput { Throttle = throttle }));
            Assert.True(received.Throttle * direction > 0 && !received.Has(ShipButtons.Boost), "the server receives forward/reverse throttle without boost");
        }
    }

    [Test]
    public static void CrossingNeutralStopsOnceThenAllowsTheOppositeDirection()
    {
        foreach (int direction in new[] { -1, 1 })
        {
            float throttle = PilotSteering.AdjustThrottle(direction * 0.005f, -direction * 0.01f);
            Assert.Equal(0f, throttle, "crossing neutral stops at zero instead of jumping into reverse");
            throttle = PilotSteering.AdjustThrottle(throttle, -direction * 0.01f);
            Assert.Near(-direction * 0.01f, throttle, 1e-7, "the next held frame leaves neutral immediately");
            Assert.Equal(0f, PilotSteering.AdjustThrottle(direction * 0.01f, -direction * 0.01f), "landing exactly on neutral is also stable");
        }
    }

    [Test]
    public static void ReleasingTheLeverHoldsItsPositionAndTravelStaysBounded()
    {
        foreach (float throttle in new[] { -1f, -0.6f, -0.005f, 0f, 0.005f, 0.6f, 1f })
            Assert.Equal(throttle, PilotSteering.AdjustThrottle(throttle, 0), "releasing W/S preserves the lever, including near neutral");

        foreach (int direction in new[] { -1, 1 })
        {
            float limit = PilotSteering.AdjustThrottle(direction * 0.99f, direction * 0.1f);
            Assert.Equal((float)direction, limit, "full lever travel clamps at its limit");
            Assert.Equal(limit, PilotSteering.AdjustThrottle(limit, direction * 0.1f), "holding against the limit cannot exceed it");
            Assert.Near(direction * 0.99f, PilotSteering.AdjustThrottle(limit, -direction * 0.01f), 1e-7, "the lever can move away from either limit");
        }
    }

    [Test]
    public static void SubQuantumFrameChangesAccumulateIntoWireThrottleWithoutBoost()
    {
        // A frame may be shorter than one network axis step; quantisation must not erase the local lever's progress.
        foreach (int direction in new[] { -1, 1 })
        {
            float delta = direction * LeverRate / 1000;
            float throttle = PilotSteering.AdjustThrottle(0, delta);
            ShipInput first = ShipConvert.ToShipInput(ShipConvert.ToControls(new ShipInput { Throttle = throttle }));
            Assert.Equal(0f, first.Throttle, "the first tiny frame is below half a wire quantum");

            for (int frame = 1; frame < 8; frame++)
                throttle = PilotSteering.AdjustThrottle(throttle, delta);

            ShipInput received = ShipConvert.ToShipInput(ShipConvert.ToControls(new ShipInput { Throttle = throttle }));
            Assert.True(received.Throttle * direction > 0, "small frame changes eventually reach the server in either direction");
            Assert.Near(throttle, received.Throttle, 0.5 / 127, "wire rounding stays within half an axis step");
            Assert.True(!received.Has(ShipButtons.Boost), "boost is never needed to produce nonzero throttle");
        }
    }
}
