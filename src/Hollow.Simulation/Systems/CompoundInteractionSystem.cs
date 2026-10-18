using System.Collections.Generic;
using System.Numerics;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.World;
using Hollow.Simulation.Building;

namespace Hollow.Simulation;

/// <summary>Authored compound interactions: gate state and climbable surfaces through the shared movement motor.</summary>
public sealed class CompoundInteractionSystem : ZoneSystem, IClimbSource
{
    private readonly List<Deployable> _near = new();
    private ThingSystem Things => Zone.Require<ThingSystem>();
    private float _reach = 1;

    protected internal override void Init()
    {
        foreach (var def in Content.Deployables)
            if (def.Climb is { } climb) _reach = System.MathF.Max(_reach, System.MathF.Max(climb.Bottom.Length(), climb.Top.Length()) + 1);
        Zone.Frames.Climbs = new Climbing(this);
    }

    public Outcome SetOpen(ulong player, Deployable gate, bool open, IReadOnlyCollection<int>? keys = null)
    {
        if (gate.Def.Gate == null || gate.Health <= 0 || Things.StaticOf(gate.Id) != gate) return OutcomeCode.NotFound;
        if (!BuildingWorld.CanUse(player, gate, keys)) return OutcomeCode.Locked;
        if (gate.Open == open) return Outcome.Done;
        gate.Open = open;
        gate.Revision++;
        Things.RefreshCollision(gate);
        return Outcome.Done;
    }

    public void Nearby(ulong frame, Vector3 position, List<ClimbSurface> into)
    {
        if (frame == 0)
        {
            _near.Clear();
            Things.DeployablesNear(position, _reach, _near);
            foreach (Deployable d in _near)
                if (!d.Rides && d.Health > 0 && d.Def.Climb is { } climb)
                    into.Add(ClimbSurface.At(d.Id.Value, climb, d.Position, Geometry.Upright(d.Up, d.Yaw)));
        }
        else
        {
            foreach (WorldObject thing in Things.Statics())
                if (thing is Deployable d && d.Link.Support.Id == frame && d.Health > 0 && d.Def.Climb is { } climb)
                    into.Add(ClimbSurface.At(d.Id.Value, climb, d.Link.Position, d.Link.Rotation));
        }
    }
}
