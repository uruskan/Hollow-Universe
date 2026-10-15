using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollow.Client;
using Hollow.Simulation;
using Hollow.Simulation.Building;
using Hollow.Simulation.Content;
using Hollow.Simulation.Nature;
using Hollow.Simulation.World;
using HollowUniverse.Fx;
using HollowUniverse.Items;
using Num = System.Numerics;
using Hollow.Simulation.Targets;

namespace HollowUniverse.Ground;

/// <summary>
/// Shots, hits and guns in hand through the effects library and the model catalogue (GROUND.md §6.4, §9.3): which
/// muzzle effect a gun with its attachments plays and where its muzzle ends (past a silencer or a brake), the surface a
/// projectile stopped on, the attachments drawn on a gun, and the hits on bodies already shown (so a flinch doesn't
/// bleed twice).
/// </summary>
public static class GunEffects
{
    private static GameContent? _content;
    private static (byte Bit, string Key)[] _attachments = Array.Empty<(byte, string)>();
    private static readonly string[] Slots = { "sight", "muzzle", "under" };
    private static Vector3 _fleshAt;
    private static double _fleshTime = double.NegativeInfinity;

    private static void Know(GameContent content)
    {
        if (_content == content)
        {
            return;
        }
        _content = content;
        List<ItemDef> parts = content.Items.Where(i => i.Attachment != null).ToList();
        _attachments = parts.Select(i => ((byte)(1 << i.Attachment!.Index), i.Key)).ToArray();
    }

    /// <summary>
    /// The muzzle effect of a gun with these attachments (<c>GunMods.Bits</c>, <c>RemoteEntity.HeldMods</c>), from data
    /// (fx.json items, IW4): the gun's own for the part on its muzzle (<c>muzzle:muzzle-brake</c>), else that part's own on
    /// any gun (a silencer's), else the gun's own (<c>muzzle</c>).
    /// </summary>
    public static string MuzzleKey(GameContent content, string weapon, byte mods)
    {
        Know(content);
        foreach ((byte bit, string key) in _attachments)
        {
            if ((mods & bit) != 0 && (FxCatalog.ItemEffect(weapon, "muzzle:" + key) ?? FxCatalog.ItemEffect(key, "muzzle")) is { } fitted)
            {
                return fitted;
            }
        }
        return FxCatalog.ItemEffect(weapon, "muzzle") ?? "muzzle.rifle";
    }

    /// <summary>A model's socket ("muzzle", "exhaust", "flame", "fuse"), if it has one.</summary>
    public static Node3D? Socket(Node3D? model, string name) => model?.FindChild("socket_" + name, true, false) as Node3D;

    /// <summary>Where a gun's shot leaves it: the far end of what is mounted on its muzzle, else its muzzle socket (+X out).</summary>
    public static Node3D? MuzzleEnd(Node3D? gun)
    {
        if (Socket(gun, "muzzle") is not { } socket)
        {
            return null;
        }
        if (socket.GetNodeOrNull<Node3D>("muzzle") is not { } part)
        {
            return socket;
        }
        if (part.GetNodeOrNull<Node3D>("End") is { } end)
        {
            return end;
        }
        var made = new Node3D { Name = "End", Position = new Vector3(ModelCatalog.LocalBounds(part).End.X, 0, 0) };
        part.AddChild(made);
        return made;
    }

    /// <summary>
    /// Shows a gun's attachments (the bits of its mods) on its model, each on the weapon's socket for its slot through the
    /// model catalogue; those taken off go. <paramref name="prepare"/> readies each new part (the arms' layer and
    /// materials in first person).
    /// </summary>
    public static void Dress(GameContent content, Node3D gun, string weaponKey, byte mods, Action<Node3D>? prepare = null)
    {
        Know(content);
        if (gun.HasMeta("mods") && (int)gun.GetMeta("mods") == mods)
        {
            return;
        }
        gun.SetMeta("mods", (int)mods);
        foreach (Node socket in gun.FindChildren("socket_*", "", true, false))
        {
            foreach (string slot in Slots)
            {
                if (socket.GetNodeOrNull(slot) is { } old)
                {
                    socket.RemoveChild(old);
                    old.QueueFree();
                }
            }
        }
        foreach ((byte bit, string key) in _attachments)
        {
            if ((mods & bit) != 0 && ModelCatalog.Attach(gun, weaponKey, key) is { } part)
            {
                prepare?.Invoke(part);
            }
        }
    }

    /// <summary>A bullet's blood was shown on a body here just now (impacts note it; flinches then don't add their own).</summary>
    public static void NoteFlesh(Vector3 at, double time)
    {
        _fleshAt = at;
        _fleshTime = time;
    }

    public static bool FleshShown(Vector3 near, double time) => time - _fleshTime < 0.4 && near.DistanceTo(_fleshAt) < 1.5f;

    /// <summary>
    /// The surface a projectile stopped on, for its impact effect, and the way that surface faces there (the ground's up;
    /// back along the flight for the rest).
    /// </summary>
    public static FxSurface SurfaceOf(GameClient client, ClientProjectile p, Num.Vector3 end, out Num.Vector3 normal)
    {
        Num.Vector3 dir = p.DirectionAt(p.EndedTime);
        normal = -dir;
        switch (p.EndTarget)
        {
            case TargetFamily.Character:
                return FxSurface.Flesh;
            case TargetFamily.Natural:
                return NaturalAt(client, end) switch
                {
                    NaturalClass.Tree => FxSurface.Wood,
                    NaturalClass.Bush => FxSurface.Grass,
                    _ => FxSurface.Rock,
                };
            case TargetFamily.Thing:
            {
                RemoteThing? thing = null;
                float best = 2.5f;
                foreach (RemoteThing t in client.Survival().Things)
                {
                    float d = Num.Vector3.Distance(t.Position, end);
                    if (d < best && t.Kind is StaticKind.Deployable or StaticKind.Prop)
                    {
                        (best, thing) = (d, t);
                    }
                }
                return thing?.Deployable(client.Content)?.Key switch
                {
                    "furnace" => FxSurface.Rock,
                    "workbench-2" or "workbench-3" or "vehicle-workbench" or "engineering-workbench" or "research-table" => FxSurface.Metal,
                    "loot-barrel-1" or "loot-barrel-2" or "military-crate" or "recycler" => FxSurface.Metal,
                    "sleeping-bag" => FxSurface.Dirt,
                    _ => FxSurface.Wood,
                };
            }
            case TargetFamily.Piece or TargetFamily.Door or TargetFamily.Cupboard:
                return client.Building().AimBuilding(end - dir * 0.4f, dir) switch
                {
                    Piece piece => FxSurfaces.OfGrade(client.Content.Building.Grades[Math.Clamp(piece.Grade, 0, client.Content.Building.Grades.Count - 1)].Key),
                    Door door => door.Def.Key.Contains("wood", StringComparison.Ordinal) ? FxSurface.Wood : FxSurface.Metal,
                    _ => FxSurface.Wood,
                };
            default:
                normal = client.Frame.UpAt(end);
                return GroundAudio.WaterDepth(client, end) > 0.05 ? FxSurface.Water : FxSurfaces.Of(GroundAudio.MaterialAt(client, end));
        }
    }

    /// <summary>The class of the natural object nearest a point within 3 m (a tree, an outcrop, a bush), if any.</summary>
    public static NaturalClass? NaturalAt(GameClient client, Num.Vector3 point)
    {
        if (client.Survival().Nature == null)
        {
            return null;
        }
        var near = new List<NaturalObject>();
        client.Survival().Nature.Query(point, 3, near);
        NaturalObject? best = null;
        float bestDistance = float.MaxValue;
        foreach (NaturalObject o in near)
        {
            float d = Num.Vector3.Distance(o.Position, point);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = o;
            }
        }
        return best is { } b ? client.Content.Natural(b.Kind)?.Class : null;
    }
}
