using System;
using System.Collections.Generic;
using System.Threading;

namespace Hollow.Surface;

/// <summary>Direct construction from frozen descriptor inputs. The caller owns the result; no process-wide world cache exists.</summary>
public static class EnvironmentWorldFactory
{
    private static readonly IReadOnlyDictionary<EnvironmentSurfaceKind, Func<PlanetEnvironmentDescriptor, ISurface>> Builders =
        new Dictionary<EnvironmentSurfaceKind, Func<PlanetEnvironmentDescriptor, ISurface>>
        {
            [EnvironmentSurfaceKind.Walking] = descriptor => new WalkingWorld(descriptor.TerrainSeed, descriptor.Radius,
                descriptor.Metadata.State.Liquid != Galaxy.Systems.LiquidKind.None && descriptor.Metadata.State.OceanCover > 0.001
                    ? descriptor.Metadata.State.OceanCover : 0,
                descriptor.Metadata.State.IceCover, descriptor.Metadata.Temperature, descriptor.WalkingParameters),
            [EnvironmentSurfaceKind.StandIn] = descriptor => new StandInWorld(descriptor.Radius, descriptor.SurfaceSeed, descriptor.Landing),
            [EnvironmentSurfaceKind.RealRadius] = descriptor => new PlanetSurface(descriptor.TerrainSeed, descriptor.Radius,
                descriptor.Metadata.State, descriptor.Metadata.Temperature),
        };

    /// <summary>
    /// Legacy constructors do not support interruption midway. Cancellation is checked before and after construction;
    /// the store retains its build reservation until this call actually returns or throws.
    /// </summary>
    public static ISurface Create(PlanetEnvironmentDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        cancellationToken.ThrowIfCancellationRequested();
        if (descriptor.Formations != null)
            throw new NotSupportedException("The selected formation world factory is not installed.");
        if (!Builders.TryGetValue(descriptor.Kind, out Func<PlanetEnvironmentDescriptor, ISurface>? build))
            throw new NotSupportedException("No environment factory for the selected kind.");
        ISurface surface = build(descriptor);
        if (cancellationToken.IsCancellationRequested)
        {
            // The caller has not received ownership yet. Current generators are managed-only, but a later
            // owned resource must also be released when cancellation arrives during its constructor.
            (surface as IDisposable)?.Dispose();
            cancellationToken.ThrowIfCancellationRequested();
        }
        return surface;
    }

    /// <summary>
    /// Explicit qualification path, disabled in the default factory. Constructs one complete selected world; callers
    /// must supply complete selected consumers before using it for gameplay. No legacy cliff/collision fallback exists.
    /// </summary>
    public static WalkingWorld CreateSelected(PlanetEnvironmentDescriptor descriptor, SelectedWalkingWorldLimits limits,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(limits);
        SelectedWalkingWorldPlan plan = limits.Prepare(descriptor, cancellationToken);
        return WalkingWorld.CreateSelected(plan, cancellationToken);
    }
}
