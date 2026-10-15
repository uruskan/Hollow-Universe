using System;
using Godot;

namespace HollowUniverse.Characters;

/// <summary>Moves the model's actual magazine between its seated transform and the solved support hand.
/// Both character views use the same extraction/insertion phases as ReloadHand; no duplicate prop remains in the gun.</summary>
public sealed class HeldReloadParts
{
    private readonly Node3D _magazine;
    private readonly Transform3D _restLocal;
    public Transform3D RestInItem { get; }
    public bool Carrying { get; private set; }
    public Transform3D MagazineGlobal => _magazine.GlobalTransform;

    private HeldReloadParts(Node3D item, Node3D magazine)
    {
        _magazine = magazine;
        _restLocal = magazine.Transform;
        RestInItem = item.GlobalTransform.AffineInverse() * magazine.GlobalTransform;
    }

    public static HeldReloadParts? Attach(Node3D item) => FindPart(item, "magazine") is { } part ? new(item, part) : null;

    public static Node3D? FindPart(Node3D item, string part)
    {
        Node3D? meshFallback = null;
        Node3D? Search(Node node)
        {
            foreach (Node child in node.GetChildren())
            {
                if (child is Node3D spatial)
                {
                    string name = child.Name.ToString();
                    if (name.Equals(part, StringComparison.OrdinalIgnoreCase) || name.Equals("part_" + part, StringComparison.OrdinalIgnoreCase))
                        return spatial;
                    if (name.Equals(part + "_lod0", StringComparison.OrdinalIgnoreCase) || name.Equals("part_" + part + "_lod0", StringComparison.OrdinalIgnoreCase))
                        meshFallback ??= spatial;
                }
                if (Search(child) is { } nested) return nested;
            }
            return null;
        }
        return Search(item) ?? meshFallback;
    }

    public void Restore()
    {
        _magazine.Transform = _restLocal;
        _magazine.Visible = true;
        Carrying = false;
    }

    /// <param name="handAtWellInItem">The support wrist holding the seated magazine, in item space; includes the
    /// static authored wrist offset. Action hand channels belong only in actualHandGlobal, so the prop follows edits.</param>
    public void Update(float phase, Transform3D actualHandGlobal, Transform3D handAtWellInItem)
    {
        if (phase < 0.28f || phase >= 0.62f) { Restore(); return; }
        Carrying = true;
        _magazine.Visible = true;
        // The binding is measured against the seated magazine, then driven by the actual post-IK hand. This remains
        // attached under body variants, authored hand offsets and whole-item recoil/roll.
        _magazine.GlobalTransform = actualHandGlobal * handAtWellInItem.AffineInverse() * RestInItem;
    }
}
