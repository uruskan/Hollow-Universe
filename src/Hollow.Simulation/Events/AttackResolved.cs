using Hollow.Simulation.Combat;
using Hollow.Simulation.Entities;

namespace Hollow.Simulation;

public readonly record struct AttackResolved(EntityId Player, AttackResolution Resolution) : IZoneEvent;
