using System.Collections.Generic;
using System.Linq;
using Godot;
using HollowUniverse.Common;

namespace HollowUniverse.Fx;

/// <summary>
/// The effects' part of the renderer's warm-up (GROUND.md §4.3.1, §9.3): every effect of <c>fx.json</c> played once,
/// tiny, as the game plays it: one-shots (their particles, lights and decals), loops for a moment, tracers flying off,
/// muzzle flashes in third and in first person. A private director belongs to the temporary warm-up holder, so its
/// particles, delayed layers, lights and decals disappear with that holder instead of leaking into the live world.
/// </summary>
public static class FxWarm
{
    private const float Tiny = 0.003f;

    public static IEnumerable<WarmItem> Items() =>
        FxCatalog.All.Select(def => new WarmItem($"effect {def.Key}", spot => Play(def, spot)));

    private static Node3D? Play(FxDef def, WarmSpot spot)
    {
        FxDirector director = Director(spot);
        if (def.IsTracer)
        {
            director.TracerNow(def.Key, spot.World, -spot.Camera.GlobalBasis.Z * 40, Vector3.Zero, 0);
            return null;
        }
        if (def.IsLoop)
        {
            director.StartNow(def.Key, spot.World, Vector3.Up, new FxParams { Scale = Tiny, Duration = 1.5f });
            return null;
        }
        if (def.Layers.Any(l => l.Type == FxLayerType.Muzzle))
        {
            // A flash follows its muzzle: a node of its own in the holder, drawn both ways.
            var muzzle = new Node3D { Name = def.Key };
            spot.Holder.AddChild(muzzle);
            muzzle.Position = spot.Place;
            Vector3 along = muzzle.GlobalBasis.X.Normalized();
            // Muzzle transforms are orthonormalized by the flash renderer; the holder's tiny scale is not inherited.
            director.PlayNow(def.Key, muzzle.GlobalPosition, along,
                new FxParams { Follow = muzzle, Direction = along, Scale = Tiny });
            director.PlayNow(def.Key, muzzle.GlobalPosition, along,
                new FxParams { Follow = muzzle, Direction = along, Scale = Tiny, FirstPerson = true });
            return muzzle;
        }
        director.PlayNow(def.Key, spot.World, Vector3.Up, new FxParams { Scale = Tiny });
        return null;
    }

    private static FxDirector Director(WarmSpot spot)
    {
        const string name = "EffectsWarmup";
        if (spot.Holder.GetNodeOrNull<FxDirector>(name) is { } existing)
        {
            return existing;
        }
        var director = new FxDirector
        {
            Name = name,
            TopLevel = true,
            CameraOverride = spot.Camera,
            PreviewOnly = true,
        };
        // Do not Install this director: the game's singleton must keep its own persistent loops and handles.
        spot.Holder.AddChild(director);
        director.GlobalTransform = Transform3D.Identity;
        return director;
    }
}
