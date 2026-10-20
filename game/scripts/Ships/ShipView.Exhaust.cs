using System.Collections.Generic;
using Godot;

namespace HollowUniverse.Ships;

/// <summary>
/// Where a vehicle's engine breathes out (VEHICLES.md §9.9): every <c>Exhaust_&lt;n&gt;</c> point of its model and its
/// modules' (the TOGGs' stacks, art/models/togg.py), found once. ShipsView puts the diesel's smoke on them.
/// </summary>
public partial class ShipView
{
    private List<Node3D>? _exhausts;

    /// <summary>The model's exhaust points (none for a kind whose engine has none).</summary>
    public IReadOnlyList<Node3D> Exhausts
    {
        get
        {
            if (_exhausts != null)
            {
                return _exhausts;
            }
            var found = new List<Node3D>();
            if (_model != null)
            {
                foreach (Node n in _model.FindChildren("Exhaust_*", "Node3D", true, false))
                {
                    found.Add((Node3D)n);
                }
            }
            // Kept once the model and its modules are in the tree (before that, they may not be mounted yet).
            if (IsInsideTree())
            {
                _exhausts = found;
            }
            return found;
        }
    }
}
