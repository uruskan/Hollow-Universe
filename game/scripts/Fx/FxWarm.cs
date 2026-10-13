using System.Collections.Generic;
using System.Linq;
using Godot;
using HollowUniverse.Common;

namespace HollowUniverse.Fx;

/// <summary>
/// The effects' part of the renderer's warm-up (GROUND.md §4.3.1, §9.3): every effect of <c>fx.json</c> played once,
/// tiny, as the game plays it: one-shots (their particles, lights and decals), loops for a moment, tracers flying off,
/// muzzle flashes in third and in first person.
/// </summary>
public static class FxWarm
{
    private const float Tiny = 0.003f;

    public static IEnumerable<WarmItem> Items() =>
        FxCatalog.All.Select(def => new WarmItem($"effect {def.Key}", spot => Play(def, spot)));

    private static Node3D? Play(FxDef def, WarmSpot spot)
    {
        if (def.IsTracer)
        {
            FxDirector.Tracer(def.Key, spot.World, -spot.Camera.GlobalBasis.Z * 40, Vector3.Zero, 0);
            return null;
        }
        if (def.IsLoop)
        {
            FxDirector.Start(def.Key, spot.World, Vector3.Up, new FxParams { Scale = Tiny, Duration = 1.5f });
            return null;
        }
        if (def.Layers.Any(l => l.Type == FxLayerType.Muzzle))
        {
            // A flash follows its muzzle: a node of its own in the holder, drawn both ways.
            var muzzle = new Node3D { Name = def.Key };
            spot.Holder.AddChild(muzzle);
            muzzle.Position = spot.Place;
            FxDirector.Muzzle(def.Key, muzzle, firstPerson: false);
            FxDirector.Muzzle(def.Key, muzzle, firstPerson: true);
            return muzzle;
        }
        FxDirector.Play(def.Key, spot.World, Vector3.Up, new FxParams { Scale = Tiny });
        return null;
    }
}
