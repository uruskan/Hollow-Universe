using System.Numerics;
using Hollow.Client.Streaming;
using Hollow.Simulation.Content;
using Hollow.Simulation.Nature;

namespace Hollow.Tests;

public static class NaturalRenderTests
{
    private static NaturalObject Object(NaturalKind kind, ulong id = 1001) =>
        new() { Id = id, Kind = kind.Index, Scale = 1, Up = Vector3.UnitY };

    private static NaturalRenderChange Observe(NaturalRenderState view, NaturalState state, NaturalObject obj,
        NaturalKind kind, bool falling = false) => view.Observe(obj.Id, state.IsDepleted(obj.Id), falling, state.Stage(obj, kind));

    [Test]
    public static void HealthAndWeakSpotUpdatesKeepCuesWithoutRebuildingPlants()
    {
        using var rig = new GameRig();
        NaturalKind kind = rig.C.Natural("pine-tree")!;
        NaturalObject tree = Object(kind);
        NaturalState state = rig.Zone.Natural!;
        var view = new NaturalRenderState();
        for (byte hit = 1; hit <= 7; hit++)
        {
            state.Apply(tree.Id, new NaturalOverride
            {
                Health = kind.Health * (1 - hit * .1f), SeenHits = hit, Streak = hit,
                HasWeakSpot = true, WeakSpotYaw = (ushort)(hit * 1000), WeakSpotLift = hit,
            });
            Assert.Equal(default(NaturalRenderChange), Observe(view, state, tree, kind), "same drawn tree for each health-only hit");
            Assert.True(state.TryGetOverride(tree.Id, out NaturalOverride current) && current.SeenHits == hit &&
                current.Streak == hit && current.HasWeakSpot, "render tracking preserves the accepted cue/weak-spot state");
        }
        state.Apply(tree.Id, new NaturalOverride { Depleted = true, Health = 0, SeenHits = 8, RespawnAtUnixMs = 1000 });
        Assert.True(Observe(view, state, tree, kind).Plants, "terminal hit removes the standing plant");
        Assert.Equal(default(NaturalRenderChange), Observe(view, state, tree, kind), "repeated depleted packet doesn't rebuild");
        Assert.Equal(1, state.Respawn(1000), "authoritative respawn removes the override");
        Assert.True(Observe(view, state, tree, kind).Plants, "respawn restores the plant");
    }

    [Test]
    public static void StreamedModelStagesAndRespawnInvalidateOnlyTheirVisibleModel()
    {
        using var rig = new GameRig();
        NaturalKind kind = rig.C.Natural("metal-node")!;
        NaturalObject node = Object(kind);
        NaturalState state = rig.Zone.Natural!;
        var view = new NaturalRenderState();
        // An already damaged node arrives without a fresh hit. Its first snapshot must still replace stage zero.
        state.Apply(node.Id, new NaturalOverride { Health = kind.Health * .5f, SeenHits = 0 });
        NaturalRenderChange first = Observe(view, state, node, kind);
        Assert.True(first.Models && !first.Plants, "streamed stage override rebuilds model only");
        state.Apply(node.Id, new NaturalOverride { Health = kind.Health * .45f, SeenHits = 1 });
        Assert.Equal(default(NaturalRenderChange), Observe(view, state, node, kind), "health within the same model stage");
        state.Apply(node.Id, new NaturalOverride { Health = kind.Health * .2f, SeenHits = 2 });
        Assert.True(Observe(view, state, node, kind) is { Models: true, Plants: false }, "next stage rebuilds model only");
        Assert.Equal(default(NaturalRenderChange), view.Observe(node.Id, depleted: false), "uncached far tile retains last known stage");
        state.Apply(node.Id, new NaturalOverride { Health = 0, Depleted = true, RespawnAtUnixMs = 2000 });
        Assert.True(Observe(view, state, node, kind).Models, "depletion removes model");
        state.Respawn(2000);
        Assert.True(Observe(view, state, node, kind).Models, "respawn restores intact model");
        Assert.Equal(default(NaturalRenderChange), Observe(view, state, node, kind), "intact state becomes the generated default again");
    }

    [Test]
    public static void DepletedStreamedOverridesAndPickedBushesKeepTheirPresenceChanges()
    {
        using var rig = new GameRig();
        NaturalState state = rig.Zone.Natural!;
        var view = new NaturalRenderState();
        state.Apply(44, new NaturalOverride { Depleted = true });
        Assert.True(view.Observe(44, state.IsDepleted(44)).Plants, "first far override hides its existing impostor without cached geometry");
        state.Apply(44, null);
        Assert.True(view.Observe(44, state.IsDepleted(44)).Plants, "streamed override removal restores it");
        state.Apply(44, new NaturalOverride { Depleted = true });
        state.Apply(44, null);
        Assert.Equal(default(NaturalRenderChange), view.Observe(44, state.IsDepleted(44)), "coalesced updates with unchanged final appearance need no redraw");
        NaturalKind bush = rig.C.Natural("red-berry-bush")!;
        NaturalObject obj = Object(bush, 45);
        Assert.True(state.Pick(obj, 1000).Depleted, "real picking rule depletes bush");
        Assert.True(Observe(view, state, obj, bush).Plants, "picked berry bush redraws bare");
        state.Respawn(long.MaxValue);
        Assert.True(Observe(view, state, obj, bush).Plants, "bush regrowth redraws berries");
    }

    [Test]
    public static void FallingPresentationHidesTheStandingTreeUntilItEnds()
    {
        var view = new NaturalRenderState();
        Assert.True(view.Observe(8, depleted: true, falling: true).Plants, "fall starts by replacing standing plant");
        Assert.Equal(default(NaturalRenderChange), view.Observe(8, depleted: true, falling: false), "normal fall ending stays depleted");
        Assert.True(view.Observe(8, depleted: false).Plants, "later respawn returns standing plant");
        Assert.True(view.Observe(8, depleted: true, falling: true).Plants, "next fall starts");
        Assert.Equal(default(NaturalRenderChange), view.Observe(8, depleted: false, falling: true), "early respawn doesn't duplicate active falling presentation");
        Assert.True(view.Observe(8, depleted: false, falling: false).Plants, "fall end reveals the already respawned plant");
        // Changes coalesced before the next rendered frame need no upload if the final appearance is unchanged.
        Assert.Equal(default(NaturalRenderChange), view.Observe(8, depleted: false), "coalesced state remains intact");
    }
}
