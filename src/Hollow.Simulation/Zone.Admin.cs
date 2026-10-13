using System.Collections.Generic;
using System.Numerics;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Nature;
using Hollow.Simulation.World;

namespace Hollow.Simulation;

// What an admin's F1 console does to the zone (GROUND.md §8.2): world props and deployables placed by hand, and god mode
// and noclip on a character.
public sealed partial class Zone
{
    // Props an admin placed: gone for good once broken or emptied (Zone.World.cs, PropGone).
    private readonly HashSet<ulong> _oneOffProps = new();

    /// <summary>
    /// A world prop placed by an admin: a barrel, a crate or a recycler, with fresh loot, on the ground at a point and
    /// turned by <paramref name="yaw"/>. To everything else it is one of the world's (hits, looting, replication); broken
    /// or emptied it doesn't come back, and a restart doesn't keep it (its spot isn't one the world generates).
    /// </summary>
    public WorldProp SpawnProp(PropDef def, Vector3 ground, float yaw)
    {
        var spot = new LootSpot(NextId().Value, def, ground, Frame.UpAt(ground), yaw, LootSite.Open);
        _spots[spot.Id] = spot;
        _oneOffProps.Add(spot.Id);
        return Spawn(spot, null);
    }

    /// <summary>
    /// A deployable placed by an admin, without the placing rules (reach, slope, room): on the ground at a point, turned
    /// by <paramref name="yaw"/>, owned by <paramref name="owner"/> (whose bag it is), a bag or bed ready at once.
    /// </summary>
    public Deployable SpawnDeployable(DeployableDef def, Vector3 ground, float yaw, EntityId owner)
    {
        var placed = new Deployable(NextId(), def, ground, Frame.UpAt(ground), yaw, owner);
        AddStatic(placed);
        return placed;
    }

    /// <summary>
    /// Sets a character's god mode and noclip (only the server does; its client's prediction follows them from the
    /// player's own state). Leaving noclip, the character stops where it is, and comes up onto the ground if it was
    /// under it.
    /// </summary>
    public void SetCheats(Entity entity, MoveCheats cheats)
    {
        ref Character c = ref Entities.Get<Character>(entity);
        if (c.State.Has(MoveCheats.Noclip) && (cheats & MoveCheats.Noclip) == 0)
        {
            c.State.Velocity = Vector3.Zero;
            if (HeightAboveGround(c.State.Position) < 0)
            {
                Vector3 ground = GroundAt(c.State.Position.X, c.State.Position.Z);
                c.State.Position = ground + Frame.UpAt(ground) * 0.05f;
            }
            Physics.Prefetch(c.State.Position, 1);
            Physics.EnsureGround(c.State.Position);
        }
        c.State.Cheats = cheats;
    }
}
