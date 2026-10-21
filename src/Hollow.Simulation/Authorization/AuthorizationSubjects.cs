using System;
using System.Collections.Generic;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Targets;

namespace Hollow.Simulation.Authorization;

/// <summary>Maps a target to the player currently controlling it. Passengers and reciprocal crew lists grant no immunity.</summary>
public sealed class AuthorizationSubjects
{
    private readonly Dictionary<TargetFamily, Func<TargetRef, EntityId>> _providers = new();

    public AuthorizationSubjects(Zone zone)
    {
        _providers.Add(TargetFamily.Character, target => new EntityId(target.Id));
        _providers.Add(TargetFamily.Ship, target => zone.Ships().ShipOf(new EntityId(target.Id)) is { } ship
            ? Of(ship) : EntityId.None);
    }

    public EntityId Of(in TargetRef target) => _providers.TryGetValue(target.Family, out var provider)
        ? provider(target) : EntityId.None;

    public static EntityId Of(Ship ship) => ship.Pilot.IsNone ? ship.Owner : ship.Pilot;
}
