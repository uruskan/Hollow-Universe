# Continuous local flight

Implementation record, with design context updated **18 October 2026**: DESIGN.md TRAVEL-01–05 now requires normal
flight, supercruise, charged interstellar jumps and fuel scooping. This page describes only existing planetary flight;
its single motor and local boundary are not the complete future travel model. See TRAVEL_IMPLEMENTATION.md for the
proposed integration. The documentation revision does not change the running game's boundary.

The first travel foundation for DESIGN.md TRAVEL-01 removes the inherited orbit parking transition. A piloted ship
continues under the same flight motor as it climbs through the orbital-altitude marker, and descends with its ordinary
controls. Position, attitude, velocity, passenger frames, inventory, and ownership stay in the existing world.

`ShipDef.Ceiling` remains the data field for the marker, measured in radii above the mean surface. It is not a flight
ceiling. `ShipMotor` reports each upward crossing once; `ShipSystem.OrbitTransfer` remains an optional seam for a future
space transfer. Without a handler accepting the transfer, flight continues locally. `ShipPredictor` runs the same motor
without the former automatic parking, so prediction covers both directions across the marker.

`ShipFlags.InOrbit` and the `Descend` protocol action remain compatible with legacy parked states. Releasing such a
state preserves its position, orientation, and velocity. The old 250 m jump and automatic 40 m/s fall are removed.
Normal flights never set that parked flag.

This package is bounded to the current planet's local flight volume. Its existing outer boundary remains four world
radii from the centre. There, only outward movement is stopped, and the ship HUD says **LOCAL FLIGHT LIMIT · TURN OR
DESCEND**. The pilot can fly around the planet or descend. No interplanetary or interstellar transfer, final space
handling, atmospheric transition artwork, or new space map is implied by this foundation.

`LocalFlightTests` uses the live Earth's 1,200 m radius, gravity and air on level test terrain. It checks a complete
surface/ascent/return flight with bounded per-tick displacement, one upward
transfer attempt, exact prediction across ascent and descent, return from the outer boundary, and release of legacy
parking without a teleport. Run with `dotnet run --project tests/Hollow.Tests -- LocalFlight` after a coordinated build.
