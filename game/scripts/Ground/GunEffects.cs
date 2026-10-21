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
    public static void Dress(GameContent content, Node3D gun, string weaponKey, byte mods, Action<Node3D>? prepare = null,
        bool firstPerson = false)
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
                if (ModelCatalog.Find(key) is { } entry)
                    GunAttachmentPresentation.Held(part, entry, content.ItemByKey(key).Attachment is { Light: true }, firstPerson);
            }
        }
    }

}
