using System.Collections.Generic;
using System.Numerics;
using Hollow.Simulation.Authorization;
using Hollow.Simulation.Building;
using Hollow.Simulation.Entities;
using Hollow.Simulation.World;

namespace Hollow.Simulation;

/// <summary>One live cupboard binding per registered device; local friendship and administrative rights are independent.</summary>
public sealed class DeviceAuthorizationSystem : ZoneSystem
{
    private sealed class Binding(Deployable device)
    {
        public readonly Deployable Device = device;
        public readonly List<ClaimDevice> Claims = new();
        public Vector3 Position;
        public ulong GeometryRevision = ulong.MaxValue;
    }

    private readonly Dictionary<ulong, Binding> _bindings = new();
    private BuildingWorld _building = null!;
    private ulong _geometryRevision;

    public override int InitOrder => 25;

    protected internal override void Init()
    {
        _building = Zone.Building().World;
        _building.Changed += BuildingChanged;
    }

    public override void Dispose() => _building.Changed -= BuildingChanged;

    public void Register(Deployable device)
    {
        if (device.Def.Defense == null || _bindings.ContainsKey(device.Id.Value)) return;
        var binding = new Binding(device);
        _bindings.Add(device.Id.Value, binding);
        Refresh(binding);
        if (!device.Authorization.Initialized)
        {
            // A claimed placement follows the cupboard only. An unclaimed placer receives a revocable explicit grant.
            if (binding.Claims.Count == 0 && !device.Owner.IsNone) device.Authorization.Grants.Add(device.Owner.Value);
            device.Authorization.Initialized = true;
            device.Authorization.Revision++;
            device.Revision++;
        }
    }

    public void Unregister(Deployable device) => _bindings.Remove(device.Id.Value);

    public void Changed(Deployable device)
    {
        if (_bindings.TryGetValue(device.Id.Value, out Binding? binding)) binding.GeometryRevision = ulong.MaxValue;
    }

    private void BuildingChanged(BuildingEvent change)
    {
        // Membership is read from the live claims, so a list edit does not rebuild geometric bindings.
        if (change.Change is BuildingChange.ClaimChanged) return;
        if (change.Change is BuildingChange.ClaimAdded or BuildingChange.ClaimRemoved or BuildingChange.PieceAdded or
            BuildingChange.PieceRemoved or BuildingChange.PieceChanged or BuildingChange.Stability)
            _geometryRevision++;
    }

    private void Refresh(Binding binding)
    {
        if (binding.GeometryRevision == _geometryRevision && binding.Position == binding.Device.Position) return;
        binding.Position = binding.Device.Position;
        _building.ClaimsCovering(binding.Position, binding.Claims);
        binding.GeometryRevision = _geometryRevision;
    }

    private Binding? Live(Deployable device)
    {
        if (device.Health <= 0 || device.Def.Defense == null ||
            !_bindings.TryGetValue(device.Id.Value, out Binding? binding) || binding.Device != device) return null;
        Refresh(binding);
        return binding;
    }

    private static bool ListedByAll(Binding binding, ulong player)
    {
        if (player == 0 || binding.Claims.Count == 0) return false;
        foreach (ClaimDevice claim in binding.Claims)
            if (claim.Gone || !claim.IsAuthorised(player)) return false;
        return true;
    }

    public bool IsFriendly(Deployable device, EntityId player)
    {
        Binding? binding = Live(device);
        return binding != null && DeviceAuthorizationPolicies.IsFriendly(device.Def.Defense!.AuthorizationPolicy,
            device.Authorization, player.Value, ListedByAll(binding, player.Value));
    }

    public bool CanManage(Deployable device, EntityId player)
    {
        Binding? binding = Live(device);
        return binding != null && !player.IsNone && (binding.Claims.Count == 0
            ? device.Owner == player : ListedByAll(binding, player.Value));
    }

    public DeviceAccessFacts Facts(Deployable device, EntityId player) => Live(device) == null ? default : new(
        true, IsFriendly(device, player), CanManage(device, player), device.PowerEnabled,
        device.Authorization.Mode == DeviceAuthorizationMode.FollowCupboard, device.Authorization.Revision);

    public Outcome AuthorizeSelf(Deployable device, EntityId player)
    {
        if (!CanManage(device, player)) return OutcomeCode.NotAuthorised;
        DeviceAuthorizationState state = device.Authorization;
        if (!state.Grants.Contains(player.Value) && state.Grants.Count >= DeviceAuthorizationState.MaximumEntries)
            return OutcomeCode.ListFull;
        bool changed = state.Exclusions.Remove(player.Value);
        changed |= state.Grants.Add(player.Value);
        if (changed) Mutated(device);
        return Outcome.Done;
    }

    public Outcome ClearSelf(Deployable device, EntityId player)
    {
        if (!CanManage(device, player) && !IsFriendly(device, player)) return OutcomeCode.NotAuthorised;
        DeviceAuthorizationState state = device.Authorization;
        if (!state.Exclusions.Contains(player.Value) && state.Exclusions.Count >= DeviceAuthorizationState.MaximumEntries)
            return OutcomeCode.ListFull;
        bool changed = state.Grants.Remove(player.Value);
        changed |= state.Exclusions.Add(player.Value);
        if (changed) Mutated(device);
        return Outcome.Done;
    }

    public Outcome ClearAll(Deployable device, EntityId player)
    {
        if (!CanManage(device, player)) return OutcomeCode.NotAuthorised;
        DeviceAuthorizationState state = device.Authorization;
        if (state.Mode == DeviceAuthorizationMode.ManualOnly && state.Grants.Count == 0 && state.Exclusions.Count == 0)
            return Outcome.Done;
        state.Mode = DeviceAuthorizationMode.ManualOnly;
        state.Grants.Clear();
        state.Exclusions.Clear();
        Mutated(device);
        return Outcome.Done;
    }

    private static void Mutated(Deployable device)
    {
        device.Authorization.Initialized = true;
        device.Authorization.Revision++;
        device.Revision++;
    }
}

public static class DeviceAuthorizationSystemAccess
{
    public static DeviceAuthorizationSystem DeviceAuthorization(this Zone zone) => zone.Require<DeviceAuthorizationSystem>();
}
