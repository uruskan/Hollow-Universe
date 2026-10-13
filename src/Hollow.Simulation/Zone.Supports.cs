using System;
using System.Collections.Generic;
using Hollow.Simulation.Capabilities;
using Hollow.Simulation.Targets;

namespace Hollow.Simulation;

// Support links (GROUND_SYSTEMS.md §3.6): what rests on or is stuck in what. A dependent keeps its own SupportLink (its
// support and its pose there); the zone keeps who depends on whom, so that when a support goes (the Destroyed event, a
// removal: a piece picked up or coming down, a box taken, a tree felled, an animal or a body gone) its dependents go with it
// by one rule: each by its own capabilities' consequence (IDependentCapability: an explosive falls). A link to a moving
// support (a door's leaf, a body's bone) is followed by its dependent through TryPose.
public sealed partial class Zone
{
    private readonly Dictionary<TargetRef, List<TargetRef>> _dependents = new();
    private readonly Dictionary<TargetRef, TargetRef> _supportOf = new();
    private readonly Stack<List<TargetRef>> _spareDependents = new();

    /// <summary>The link that holds a thing at a pose on a support as it is now (none when the support doesn't exist).</summary>
    public SupportLink LinkTo(in TargetRef support, in TargetPose at) =>
        TryPose(support, out TargetPose pose) ? SupportLink.Of(support, pose, at) : SupportLink.None;

    /// <summary>Where a linked thing is now: its support's pose (or part's) and its own pose there.</summary>
    public bool TryPose(in SupportLink link, out TargetPose pose)
    {
        pose = default;
        if (link.IsNone || !TryPose(link.Support, out TargetPose support))
        {
            return false;
        }
        pose = link.In(support);
        return true;
    }

    /// <summary>Whether a support's things follow it as it moves (a door's leaf, a body).</summary>
    public bool Moves(in TargetRef support) => FamilyOf(support.Family)?.Moves == true;

    /// <summary>
    /// Records that a thing (<paramref name="dependent"/>) rests on or is stuck in <paramref name="support"/>, in place of
    /// what held it before; none, or the ground (which never goes), holds nothing to go with.
    /// </summary>
    public void Hold(in TargetRef dependent, in TargetRef support)
    {
        Release(dependent);
        if (support.IsNone || support.Family == TargetFamily.Ground)
        {
            return;
        }
        TargetRef holder = support.Whole;
        if (!_dependents.TryGetValue(holder, out List<TargetRef>? list))
        {
            list = _spareDependents.Count > 0 ? _spareDependents.Pop() : new List<TargetRef>();
            _dependents.Add(holder, list);
        }
        list.Add(dependent.Whole);
        _supportOf[dependent.Whole] = holder;
    }

    /// <summary>A thing no longer rests on anything (it flies, or it went).</summary>
    public void Release(in TargetRef dependent)
    {
        if (!_supportOf.Remove(dependent.Whole, out TargetRef holder) || !_dependents.TryGetValue(holder, out List<TargetRef>? list))
        {
            return;
        }
        list.Remove(dependent.Whole);
        if (list.Count == 0)
        {
            _dependents.Remove(holder);
            _spareDependents.Push(list);
        }
    }

    /// <summary>The things resting on or stuck in a support now.</summary>
    public IReadOnlyList<TargetRef> DependentsOf(in TargetRef support) =>
        _dependents.TryGetValue(support.Whole, out List<TargetRef>? list) ? list : Array.Empty<TargetRef>();

    /// <summary>
    /// The one rule (GROUND_SYSTEMS.md §3.6): a support went (destroyed or removed), so everything on it or in it goes with
    /// it, each by its own capabilities' consequence.
    /// </summary>
    private void SupportGone(in TargetRef support)
    {
        if (_dependents.Count == 0 || !_dependents.Remove(support.Whole, out List<TargetRef>? list))
        {
            return;
        }
        // (A consequence may take a support of its own away: this list is out of the index already.)
        foreach (TargetRef dependent in list)
        {
            _supportOf.Remove(dependent);
            if (FamilyOf(dependent.Family) is not { } family || family.Resolve(dependent.Id) is not { } thing)
            {
                continue;
            }
            foreach (int capability in family.Capabilities(dependent.Id))
            {
                if (Capabilities.Find(capability) is IDependentCapability handler)
                {
                    handler.SupportLost(this, dependent, thing);
                }
            }
        }
        list.Clear();
        _spareDependents.Push(list);
    }
}
