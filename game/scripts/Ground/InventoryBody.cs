using System;
using System.Linq;
using Godot;
using Hollow.Client;
using HollowUniverse.Characters;
using HollowUniverse.UI;

namespace HollowUniverse.Ground;

/// <summary>
/// The player's own body on the inventory screen (GROUND.md §8): the character package's <see cref="CharacterView"/>
/// in what the player wears, holding the item in hand, standing in the screen's little world facing out, in place of
/// the interface's wooden mannequin.
/// </summary>
public partial class InventoryBody : Node3D
{
    private static long _nextOwner, _owner;
    private readonly GameClient _client;
    private readonly string? _held;

    private InventoryBody(GameClient client, string? held)
    {
        _client = client;
        _held = held;
    }

    /// <summary>Makes the inventory screen show the player's body (until <see cref="Release"/>).</summary>
    public static long Use(GameClient client)
    {
        _owner = System.Threading.Interlocked.Increment(ref _nextOwner);
        CharacterPreview.Body = held => new InventoryBody(client, held);
        CharacterPreview.BodyLook = () => string.Join(",", client.Local.Inventory.Wear.Slots.ToArray().Select(s => s.Item));
        return _owner;
    }

    public static void Release(long owner)
    {
        if (owner == 0 || owner != _owner) return;
        _owner = 0;
        CharacterPreview.Body = null;
        CharacterPreview.BodyLook = null;
    }

    public override void _Ready()
    {
        var view = new CharacterView { Name = "Body", FootPlacement = false, ForcedLod = 0, Body = (BodyType)_client.Body };
        AddChild(view);
        view.Wear(ItemLooks.Outfit(_client.Content, _client.Local.Inventory.Wear.Slots.ToArray().Select(s => s.Item)));
        (HeldKind kind, string? model) = _held != null && ItemLooks.Held(_held) is { } h ? h : (HeldKind.Unarmed, (string?)null);
        CharacterFrame frame = CharacterFrame.Standing(Vector3.Zero, Vector3.Up, MathF.PI + 0.25f);
        frame.Held = kind;
        frame.HeldItem = model;
        view.Apply(frame);
    }
}
