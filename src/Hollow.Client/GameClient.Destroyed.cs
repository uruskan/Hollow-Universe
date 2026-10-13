using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Net.Protocol;
using Hollow.Simulation;
using Hollow.Simulation.Entities;

namespace Hollow.Client;

/// <summary>
/// Something broke near the player (GROUND.md §9.4): what kind of thing and which, its model as it stood (catalogue key,
/// kit part, place in the zone frame, turn, size), the blow's direction (zero for none), what broke it and when (the
/// client's clock). The visual layer breaks the model into its gibs.
/// </summary>
public readonly record struct DestructionEvent(HitTarget Receiver, EntityId Id, ModelPlacement Model, Vector3 Blow, DestroyCause Cause,
    double ClientSeconds);

// Things breaking apart in the client (GROUND.md §9.4).
public sealed partial class GameClient
{
    private readonly List<DestructionEvent> _destructions = new();

    /// <summary>Things broken in the last two seconds, newest last (for views that poll rather than listen).</summary>
    public IReadOnlyList<DestructionEvent> Destructions => _destructions;

    /// <summary>Something broke: break its model into gibs, with dust and its sound.</summary>
    public event Action<DestructionEvent>? Destroyed;

    private void UpdateDestructions()
    {
        double now = ClockSeconds;
        int keep = 0;
        while (keep < _destructions.Count && now - _destructions[keep].ClientSeconds > 2)
        {
            keep++;
        }
        if (keep > 0)
        {
            _destructions.RemoveRange(0, keep);
        }
    }

    private bool ReceiveDestruction(uint type)
    {
        if (type != Destruction.TypeId)
        {
            return false;
        }
        Destruction m = Destruction.Read(_reader);
        if (_reader.Failed || string.IsNullOrEmpty(m.Model) || !float.IsFinite(m.X) || !float.IsFinite(m.Y) || !float.IsFinite(m.Z))
        {
            return true;
        }
        var rotation = new Quaternion(m.RotationX, m.RotationY, m.RotationZ, m.RotationW);
        rotation = rotation.LengthSquared() > 0.25f ? Quaternion.Normalize(rotation) : Quaternion.Identity;
        var blow = new Vector3(m.BlowX, m.BlowY, m.BlowZ);
        blow = blow.LengthSquared() > 0.25f ? Vector3.Normalize(blow) : Vector3.Zero;
        var model = new ModelPlacement(m.Model, m.Part ?? "", new Vector3(m.X, m.Y, m.Z), rotation, m.Scale > 0 ? m.Scale : 1);
        var e = new DestructionEvent(m.Receiver, new EntityId(m.Id), model, blow, m.Cause, ClockSeconds);
        _destructions.Add(e);
        Destroyed?.Invoke(e);
        return true;
    }
}
