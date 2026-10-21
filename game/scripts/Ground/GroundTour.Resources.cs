using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Hollow.Client;
using Hollow.Core;
using Hollow.Simulation.Content;
using Hollow.Simulation.Nature;
using Hollow.Surface;
using HollowUniverse.Items;
using HollowUniverse.UI;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

public partial class GroundTour
{
    // Uses ordinary generated walking-world objects and the actual hosted renderer/attack path.
    // --tour <isolated-evidence-folder> --tour-steps resources --db <isolated-fresh-save>
    private async Task Resources()
    {
        RequireResource(Client.Surface is WalkingWorld && Client.Survival().Nature is ScatterNaturalObjects,
            "normal walking-world scatter supplies the resource objects");
        await RepairGod(true);
        View.Interface.Open(GroundScreen.Hud);
        NatureView renderer = View.GetNode<NatureView>("Nature");
        await Until(() => renderer.Plants is { Ready: true }, 30);
        RequireResource(renderer.Plants is { Ready: true }, "the actual flora registry is ready");
        string[] required = { "stone-node", "metal-node", "sulphur-node", "high-grade-node" };
        foreach (string key in required)
        {
            NaturalKind kind = Client.Content.Natural(key) ?? throw new InvalidOperationException("Missing natural kind " + key);
            RequireResource(!renderer.Plants!.Draws(kind.Index), key + " is owned by the stage-model renderer");
            RequireResource(ModelCatalog.Find(key)?.Stages == kind.Stages, key + " has every configured stage model");
        }
        foreach (NaturalKind kind in Client.Content.NaturalKinds)
        {
            if (kind.ScatterKinds.Count == 0) continue;
            bool supported = View.World!.Plants!.Library!.For(kind.ScatterKinds[0]) is { Variants.Count: > 0 };
            RequireResource(renderer.Plants!.Draws(kind.Index) == supported,
                kind.Key + " renderer ownership agrees with real flora geometry");
        }

        Dictionary<string, NaturalObject> found = await FindGeneratedResources(required);
        Admin("give", "stone-pickaxe 1");
        await Until(() => Count("stone-pickaxe") > 0, 5);
        RequireResource(Count("stone-pickaxe") > 0, "the mining tool is in the replicated inventory");
        await Hold("stone-pickaxe", 3);
        try
        {
            foreach (string key in required)
            {
                NaturalObject resource = found[key];
                NaturalKind kind = Client.Content.Natural(resource.Kind)!;
                NaturalState state = Client.Survival().Natural!;
                RequireResource(!state.IsDepleted(resource.Id) && state.Stage(resource, kind) == 0,
                    key + " starts intact in the generated world");
                await TeleportTo(Away(resource.Position, kind.Radius * resource.Scale + 0.75f));
                Face(resource.Position + resource.Up * 0.65f);
                await Settle();
                await Until(() => ResourceStageVisible(renderer, resource, 0), 8);
                RequireResource(ResourceStageVisible(renderer, resource, 0), key + " has visible mesh geometry at its generated position");
                await Shot("resource-" + key + "-intact");
                var before = kind.Yield.ToDictionary(y => y.Item.Key, y => Count(y.Item.Key));
                var stages = new HashSet<int> { 0 };
                double started = Time.GetTicksMsec() / 1000.0;
                bool hotspot = false;
                View.Scripted = new InputFrame { Primary = true };
                while (!state.IsDepleted(resource.Id) && Time.GetTicksMsec() / 1000.0 - started < 60)
                {
                    if (state.WeakSpot(resource, kind) is { } point)
                    {
                        Face(point);
                        hotspot = true;
                    }
                    int stage = state.Stage(resource, kind);
                    if (stages.Add(stage))
                    {
                        View.Scripted = new InputFrame();
                        await Until(() => ResourceStageVisible(renderer, resource, stage), 4);
                        RequireResource(ResourceStageVisible(renderer, resource, stage), key + " actually draws depletion stage " + stage);
                        await Shot("resource-" + key + "-stage-" + stage);
                        View.Scripted = new InputFrame { Primary = true };
                    }
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
                View.Scripted = new InputFrame();
                RequireResource(hotspot && state.IsDepleted(resource.Id), key + " is mined through normal attacks and hotspot feedback");
                RequireResource(Enumerable.Range(0, kind.Stages).All(stages.Contains), key + " rendered every authored depletion stage");
                await Until(() => !renderer.TryRenderedNatural(resource.Id, out _, out _), 4);
                RequireResource(!renderer.TryRenderedNatural(resource.Id, out _, out _), key + " removes its depleted world model");
                foreach (ItemAmount yield in kind.Yield)
                {
                    int total = NaturalShapes.Total(resource, kind, yield);
                    int expected = total + (int)Math.Round(total * (double)kind.Gather!.Last.Bonus);
                    await Until(() => Count(yield.Item.Key) - before[yield.Item.Key] >= expected, 4);
                    int gained = Count(yield.Item.Key) - before[yield.Item.Key];
                    RequireResource(gained == expected, $"{key} pays exactly {expected} {yield.Item.Key} ({gained} received)");
                }
                RequireResource(state.TryGetOverride(resource.Id, out NaturalOverride depleted) && depleted.Depleted,
                    key + " receives authoritative depletion (respawn deadlines remain server-side)");
                await Shot("resource-" + key + "-depleted");
                GD.Print($"[resource-check] mined {key} id={resource.Id:X16} position={resource.Position} scale={resource.Scale:0.000} stages={stages.Count}");
            }
            GD.Print("[resource-check] PASS four naturally generated node families: actual visibility, all stages, hotspot mining, exact yields and depletion. Respawn/restart are separate checks.");
        }
        finally
        {
            View.Scripted = null;
        }
    }

    private async Task<Dictionary<string, NaturalObject>> FindGeneratedResources(IReadOnlyList<string> required)
    {
        var found = new Dictionary<string, NaturalObject>();
        var nature = Client.Survival().Nature!;
        var state = Client.Survival().Natural!;
        var seen = new HashSet<TileKey>();
        Vector3D origin = Client.Frame.ToBody(Client.Character.Position).Normalized;
        IEnumerable<Vector3D> directions = new[] { origin }.Concat(PlanetSurface.FibonacciSphere(360)
            .OrderBy(d => Vector3D.Distance(d, origin)));
        foreach (Vector3D direction in directions)
        {
            TileKey tile = TileKey.At(direction, nature.Level);
            if (!seen.Add(tile)) continue;
            IReadOnlyList<NaturalObject> objects = nature.InTile(tile);
            foreach (NaturalObject item in objects)
            {
                NaturalKind kind = Client.Content.Natural(item.Kind)!;
                if (!required.Contains(kind.Key) || found.ContainsKey(kind.Key) || state.IsDepleted(item.Id) ||
                    state.Stage(item, kind) != 0) continue;
                // Keep the first capture/attack clear of overlapping natural solids; don't alter generation.
                bool blocked = objects.Any(other => other.Id != item.Id &&
                    Client.Content.Natural(other.Kind) is { IsSolid: true } &&
                    Num.Vector3.Distance(item.Position, other.Position) < 4);
                if (!blocked) found.Add(kind.Key, item);
            }
            if (found.Count == required.Count) break;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        RequireResource(found.Count == required.Count, "all four intact resources are found in ordinary generated scatter; found " + string.Join(", ", found.Keys));
        return found;
    }

    private static bool ResourceStageVisible(NatureView renderer, NaturalObject resource, int expectedStage)
    {
        if (!renderer.TryRenderedNatural(resource.Id, out Node3D? model, out int stage) || model == null || stage != expectedStage ||
            model.Position.DistanceTo(GroundView.ToGodot(resource.Position)) > 0.01f) return false;
        return VisibleGeometry(model);
    }

    private static bool VisibleGeometry(Node node)
    {
        if (node is MeshInstance3D mesh && mesh.Mesh != null && mesh.IsVisibleInTree()) return true;
        foreach (Node child in node.GetChildren())
            if (VisibleGeometry(child)) return true;
        return false;
    }

    private static void RequireResource(bool value, string requirement)
    {
        if (!value) throw new InvalidOperationException("resource-check: " + requirement);
        GD.Print("[resource-check] " + requirement);
    }
}
