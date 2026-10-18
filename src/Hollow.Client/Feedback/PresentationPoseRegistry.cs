using System;
using System.Collections.Generic;
using Hollow.Simulation.Targets;

namespace Hollow.Client.Feedback;

/// <summary>
/// Renderer-owned target poses, queried only by presentation on the client thread. An absent registration permits
/// the simulation-view fallback; a registered provider returning null explicitly says its rendered target is gone.
/// </summary>
internal sealed class PresentationPoseRegistry
{
    private readonly Dictionary<TargetRef, Registration> _registrations = new();

    public IDisposable Register(TargetRef target, Func<TargetPose?> provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (_registrations.TryGetValue(target, out Registration? old)) old.Detach();
        var registration = new Registration(this, target, provider);
        _registrations[target] = registration;
        return registration;
    }

    /// <summary>True means an override exists, including an override whose current pose is null.</summary>
    public bool TryGet(in TargetRef target, out TargetPose? pose)
    {
        pose = null;
        if (!_registrations.TryGetValue(target, out Registration? registration)) return false;
        pose = registration.Current();
        return true;
    }

    public void Clear()
    {
        foreach (Registration registration in _registrations.Values) registration.Detach();
        _registrations.Clear();
    }

    private void Remove(Registration registration)
    {
        if (_registrations.TryGetValue(registration.Target, out Registration? current) && ReferenceEquals(current, registration))
            _registrations.Remove(registration.Target);
        registration.Detach();
    }

    private sealed class Registration(PresentationPoseRegistry owner, TargetRef target, Func<TargetPose?> provider) : IDisposable
    {
        private PresentationPoseRegistry? _owner = owner;
        private Func<TargetPose?>? _provider = provider;
        public TargetRef Target { get; } = target;
        public TargetPose? Current() => _provider?.Invoke();
        public void Detach() { _owner = null; _provider = null; }
        public void Dispose() => _owner?.Remove(this);
    }
}
