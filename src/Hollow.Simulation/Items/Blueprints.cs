using System.Collections.Generic;
using Hollow.Simulation.Content;

namespace Hollow.Simulation.Items;

/// <summary>
/// The blueprints a character has learnt (GROUND.md §6.8, Rust's): recipes not known from the start, unlocked in a
/// workbench's tech tree, researched at a research table or read off a blueprint item. They last through death and
/// are saved with the character. The client keeps a copy the server sends.
/// </summary>
public sealed class BlueprintBook
{
    private readonly HashSet<ushort> _learnt = new();

    /// <summary>The recipes learnt (by their output item's ID), in no particular order.</summary>
    public IReadOnlyCollection<ushort> Learnt => _learnt;

    /// <summary>Raised on every change, for replication.</summary>
    public uint Revision { get; private set; }

    /// <summary>Whether the recipe can be crafted: known from the start, or learnt.</summary>
    public bool Knows(Recipe recipe) => recipe.Known || _learnt.Contains(recipe.Id);

    public bool HasLearnt(ushort recipe) => _learnt.Contains(recipe);

    /// <summary>Learns a recipe; false when it was known already (from the start or learnt).</summary>
    public bool Learn(Recipe recipe)
    {
        if (recipe.Known || !_learnt.Add(recipe.Id))
        {
            return false;
        }
        Revision++;
        return true;
    }

    /// <summary>Replaces what is learnt (a save loaded, or the client taking the server's copy): recipes the game has, not known from the start.</summary>
    public void Set(GameContent content, IEnumerable<ushort> recipes)
    {
        _learnt.Clear();
        foreach (ushort id in recipes)
        {
            if (content.Recipe(id) is { Known: false })
            {
                _learnt.Add(id);
            }
        }
        Revision++;
    }

    /// <summary>Forgets everything (a blueprint wipe).</summary>
    public void Clear()
    {
        if (_learnt.Count > 0)
        {
            _learnt.Clear();
            Revision++;
        }
    }
}
