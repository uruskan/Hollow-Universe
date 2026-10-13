using System.Numerics;
using Hollow.Simulation.Building;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Nature;
using Hollow.Simulation.World;

namespace Hollow.Simulation;

// Things breaking (GROUND.md §9.4): one place reports every destruction as ZoneEventKind.Destroyed, what broke as a model
// of the catalogue, so the client breaks it into its gibs the same way whatever it was. The network carries only the fact.
public sealed partial class Zone
{
    // The blow the building rules are taking now: what a piece, door or claim device it breaks was broken by.
    private Blow _blow;

    /// <summary>Things destroyed and reported (statistics).</summary>
    public long DestroyedCount { get; private set; }

    /// <summary>
    /// Reports that something broke (GROUND.md §9.4): to everyone near, <see cref="ZoneEventKind.Destroyed"/> with what kind
    /// of thing it was, which one, its model as it stood, and the blow. The one place destruction is announced.
    /// </summary>
    public void ReportDestroyed(HitTarget receiver, EntityId id, in ModelPlacement model, in Blow blow)
    {
        if (string.IsNullOrEmpty(model.Model))
        {
            return;
        }
        DestroyedCount++;
        GameEvents.Add(new ZoneEvent
        {
            Kind = ZoneEventKind.Destroyed, Target = receiver, Other = id, Player = blow.By, Model = model, Cause = blow.Cause,
            Position = model.Position, Velocity = blow.Direction,
        });
    }

    /// <summary>A natural object used up by a blow (an outcrop's last stage; a tree felled): its kind's model.</summary>
    private void ReportDepleted(in NaturalObject o, Vector3 from, Vector3 point, EntityId by)
    {
        if (Content!.Natural(o.Kind) is not { } kind)
        {
            return;
        }
        Blow blow = Blow.Along(DestroyCause.Melee, point - from, by);
        ReportDestroyed(HitTarget.Natural, new EntityId(o.Id), new ModelPlacement(kind.Key, "", o.Position, ModelPlacement.Upright(o.Up, o.Yaw), o.Scale), blow);
    }

    /// <summary>
    /// What a removal from the building was broken by: the blow under way when damage broke it, else the removal itself
    /// (a collapse, decay, the hammer). Null when it was taken away whole.
    /// </summary>
    private Blow? BlowOf(BuildingRemoval removal) => removal switch
    {
        BuildingRemoval.Destroyed => _blow,
        BuildingRemoval.Collapsed => new Blow(DestroyCause.Collapse, Vector3.Zero, EntityId.None),
        BuildingRemoval.Decayed => new Blow(DestroyCause.Decay, Vector3.Zero, EntityId.None),
        BuildingRemoval.Demolished => new Blow(DestroyCause.Demolish, Vector3.Zero, EntityId.None),
        _ => null,
    };
}
