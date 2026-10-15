using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Hollow.Core;
using Hollow.Net.Bits;
using Hollow.Net.Protocol;
using Hollow.Net.Replication;
using Hollow.Persistence;
using Hollow.Simulation;
using Hollow.Simulation.Capabilities;
using Hollow.Simulation.Diagnostics;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Nature;
using Hollow.Simulation.Physics;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

namespace Hollow.Server;

/// <summary>
/// Static things and natural objects on the server (GROUND.md §4.2, §6.1): every static thing gets a net ID and is
/// replicated by relevance (a few new ones a snapshot, so a big base streams in; sent again when it, its contents or what
/// the viewer is told of it changed; said gone when it goes or leaves the range); the natural objects' overrides go by
/// 64 m area, each area when it comes into range and each change since; things and changed natural objects are loaded
/// with the zone and saved with it. Explosives and animals' corpses are stored among the things but are their own
/// modules' (<see cref="RaidModule"/>, <see cref="AnimalModule"/>).
/// </summary>
public sealed class ThingModule : ServerModule
{
    /// <summary>A player hears of at most this many new static things per snapshot, so a big base streams in.</summary>
    public const int ThingSpawnsPerSnapshot = 48;

    private RelevanceGrid _thingGrid = null!;
    private readonly Dictionary<EntityId, uint> _thingNetIds = new();
    private readonly Dictionary<uint, WorldObject> _things = new();
    private readonly HashSet<uint> _thingsPresent = new();
    private readonly List<uint> _thingsGone = new();
    private readonly List<int> _thingIndices = new();
    private readonly HashSet<uint> _thingsRelevant = new();
    private readonly List<uint> _thingsLeaving = new();
    private readonly HashSet<ulong> _savedNaturals = new();
    private readonly HashSet<ulong> _naturalsToSave = new();
    private readonly HashSet<ulong> _naturalsToSend = new();
    private readonly Dictionary<(int X, int Y, int Z), HashSet<ulong>> _overrideCells = new();
    private readonly List<NaturalNet> _naturalBatch = new();
    private readonly List<(int X, int Y, int Z)> _cellsLeaving = new();

    /// <summary>What this player has of the things and natural objects: the things by net ID with the revision sent, the areas whose overrides they have.</summary>
    private sealed class ThingView
    {
        /// <summary>Static things this player was told about, by net ID, with the revision sent.</summary>
        public readonly Dictionary<uint, ThingSent> ThingsSent = new();

        /// <summary>The 64 m cells whose natural overrides this player has.</summary>
        public readonly HashSet<(int X, int Y, int Z)> NatureCells = new();
    }

    protected internal override void Init(MessageHandlers<PlayerSession> handlers)
    {
        _thingGrid = new RelevanceGrid(Options.RelevanceRadius);
    }

    // ---------------------------------------------------------------- loading and saving the world

    // Things where they were (explosives and corpses are the raid and animal modules'), natural objects as players left
    // them.
    protected internal override void Restore(ZoneRecord stored, HashSet<EntityId> saved)
    {
        int things = 0;
        foreach (ThingRecord t in stored.Things)
        {
            if (t.Kind is ExplosiveCodec.ThingKind or AnimalCodec.ThingKind)
            {
                continue;
            }
            try
            {
                WorldObject? thing = WorldCodec.DecodeThing(t.Id, (StaticKind)t.Kind, t.Data, Content, Zone.Frame.ToLocal, Zone.Frame.UpAt, Zone.Tick);
                if (thing != null && !Zone.Entities.TryFind(thing.Id, out _))
                {
                    Zone.Things().AddStatic(thing);
                    things++;
                }
                saved.Add(t.Id);
            }
            catch (Exception e) when (e is EndOfStreamException or InvalidDataException or ArgumentException)
            {
                Log($"thing {t.Id} doesn't load ({e.Message}); left out");
            }
        }
        foreach (NaturalRecord n in stored.Naturals)
        {
            var loaded = new NaturalOverride { Health = n.Health, Depleted = n.Depleted, RespawnAtUnixMs = n.RespawnAtUnixMilliseconds };
            loaded.UnpackWeakSpot(n.WeakSpot);
            Zone.Natural!.Apply(n.ObjectId, loaded);
            _savedNaturals.Add(n.ObjectId);
            IndexOverride(n.ObjectId);
        }
        Log($"loaded {things} things and {stored.Naturals.Count} changed natural objects");
    }

    // Every static thing (a deployable claimed by a building is saved with it), and the natural objects that changed.
    protected internal override void Save(ZoneSave save, HashSet<EntityId> current)
    {
        foreach (WorldObject thing in Zone.Things().Statics())
        {
            if (thing is Deployable deployable && Zone.Building().ClaimOf(deployable) != null)
            {
                continue; // saved with the building
            }
            save.Things.Add(new ThingRecord
            {
                Id = thing.Id,
                SystemAddress = Spec.SystemAddress,
                BodyPath = Spec.BodyPath,
                Kind = (byte)thing.Kind,
                Data = WorldCodec.EncodeThing(thing, Zone.Frame.ToBody(thing.Position), Zone.Tick),
            });
            current.Add(thing.Id);
        }
        foreach (ulong id in _naturalsToSave)
        {
            if (Zone.Natural!.TryGetOverride(id, out NaturalOverride o))
            {
                save.Naturals.Add(new NaturalRecord(id, o.Health, o.PackedWeakSpot, o.Depleted, o.RespawnAtUnixMs));
                _savedNaturals.Add(id);
            }
            else if (_savedNaturals.Remove(id))
            {
                save.RemovedNaturals.Add(id);
            }
        }
        _naturalsToSave.Clear();
    }

    // ---------------------------------------------------------------- after each step

    protected internal override void AfterStep()
    {
        NaturalState natural = Zone.Natural!;
        foreach (ulong id in natural.Changed)
        {
            _naturalsToSave.Add(id);
            _naturalsToSend.Add(id);
            IndexOverride(id);
        }
        natural.Changed.Clear();
    }

    // ---------------------------------------------------------------- static things

    protected internal override void Prepare() => PrepareThings();

    // Once per snapshot: every static thing gets a net ID and goes into the relevance grid; those gone are noted.
    private void PrepareThings()
    {
        _thingGrid.Clear();
        _thingsPresent.Clear();
        foreach (WorldObject thing in Zone.Things().Statics())
        {
            if (!_thingNetIds.TryGetValue(thing.Id, out uint netId))
            {
                netId = NextNetId();
                _thingNetIds.Add(thing.Id, netId);
            }
            _things[netId] = thing;
            _thingsPresent.Add(netId);
            _thingGrid.Add(netId, thing.Position);
        }
        _thingsGone.Clear();
        foreach ((uint netId, WorldObject thing) in _things)
        {
            if (!_thingsPresent.Contains(netId))
            {
                _thingsGone.Add(netId);
                _thingNetIds.Remove(thing.Id);
            }
        }
        foreach (uint netId in _thingsGone)
        {
            _things.Remove(netId);
        }
    }

    protected internal override void Replicate(PlayerSession session, Vector3 centre)
    {
        using (Profile.Time(ProfileSection.ReplicateThings))
        {
            ReplicateThings(session, centre);
        }
        using (Profile.Time(ProfileSection.ReplicateNature))
        {
            ReplicateNature(session, centre);
        }
    }

    private void ReplicateThings(PlayerSession session, Vector3 centre)
    {
        ThingView view = session.State<ThingView>();
        foreach (uint netId in _thingsGone)
        {
            if (view.ThingsSent.Remove(netId))
            {
                Despawned(session, netId);
            }
        }
        _thingsLeaving.Clear();
        foreach (uint netId in view.ThingsSent.Keys)
        {
            _thingsLeaving.Add(netId);
        }
        _thingsRelevant.Clear();
        _thingsRelevant.UnionWith(_thingsLeaving);
        _thingGrid.Query(centre, _thingsRelevant, _thingIndices);
        _thingsRelevant.Clear();
        int spawned = 0;
        foreach (int index in _thingIndices)
        {
            uint netId = _thingGrid[index].NetId;
            WorldObject thing = _things[netId];
            uint contents = thing.Container?.Revision ?? 0;
            // Put somewhere else (it fell when the ship it lay on went): sent anew, as a spawn.
            if (view.ThingsSent.TryGetValue(netId, out ThingSent moved) && moved.Moved != thing.Moved)
            {
                view.ThingsSent.Remove(netId);
            }
            if (view.ThingsSent.TryGetValue(netId, out ThingSent sent))
            {
                _thingsRelevant.Add(netId);
                // Sent again when it changed, or when what the player is told of it did (its contents, the viewer).
                if (sent.Revision != thing.Revision || sent.Contents != contents || session.ViewerChanged)
                {
                    ViewerFlags flags = Zone.Targets().FlagsFor(thing.Ref, session.Viewer);
                    view.ThingsSent[netId] = new ThingSent(thing.Revision, contents, flags, thing.Moved);
                    if (sent.Revision != thing.Revision || flags != sent.Flags)
                    {
                        (byte health, uint count) = Look(thing);
                        Writer.Reset();
                        new ThingUpdate { NetId = netId, Health = health, Count = count, Flags = flags }.WriteMessage(Writer);
                        Send(session);
                    }
                }
            }
            else if (spawned < ThingSpawnsPerSnapshot)
            {
                spawned++;
                _thingsRelevant.Add(netId);
                ViewerFlags flags = Zone.Targets().FlagsFor(thing.Ref, session.Viewer);
                view.ThingsSent[netId] = new ThingSent(thing.Revision, contents, flags, thing.Moved);
                (byte health, uint count) = Look(thing);
                // Aboard a ship (IRON_WAKE.md §8), its pose in the ship's frame: the client carries it where it draws the ship.
                (Vector3 at, float yaw) = thing.Rides && thing.Support.Family == TargetFamily.Ship
                    ? (thing.Link.Position, Facing.YawOf(Vector3.Transform(-Vector3.UnitZ, thing.Link.Rotation), Vector3.UnitY))
                    : (thing.Position, thing.Yaw);
                Writer.Reset();
                new ThingSpawn
                {
                    NetId = netId,
                    EntityId = thing.Id.Value,
                    Kind = thing.Kind,
                    Look = thing.Look,
                    X = at.X,
                    Y = at.Y,
                    Z = at.Z,
                    Yaw = Quantize.Angle16(yaw),
                    Health = health,
                    Count = count,
                    Flags = flags,
                    Name = thing.Name,
                    SupportFamily = thing.Support.Family,
                    Support = thing.Support.Id,
                    Capabilities = thing.CapabilitiesIn(Content.Capabilities).Bits,
                }.WriteMessage(Writer);
                Send(session);
            }
        }
        foreach (uint netId in _thingsLeaving)
        {
            if (!_thingsRelevant.Contains(netId) && view.ThingsSent.Remove(netId))
            {
                Despawned(session, netId);
            }
        }
    }

    // What a client is told of a thing besides its look and its viewer flags: health, a count (a bag's stacks).
    private static (byte Health, uint Count) Look(WorldObject thing) =>
        ((byte)Math.Clamp(Math.Ceiling(thing.HealthFraction * 255), 0, 255), (uint)Math.Max(0, thing.ShownCount));

    protected internal override void Finish()
    {
        _thingsGone.Clear();
        _naturalsToSend.Clear();
    }

    // ---------------------------------------------------------------- natural objects

    // Overrides are indexed by cubes of the world's frame (docs/WHOLE_WORLD.md §3.3).
    private static (int X, int Y, int Z) NatureCell(Vector3 p) =>
        ((int)MathF.Floor(p.X / PhysicsWorld.TileSize), (int)MathF.Floor(p.Y / PhysicsWorld.TileSize), (int)MathF.Floor(p.Z / PhysicsWorld.TileSize));

    private static float CellDistance((int X, int Y, int Z) cell, Vector3 centre) =>
        Vector3.Distance(new Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f) * PhysicsWorld.TileSize, centre);

    // Keeps the index of overrides by area up to date for an object that changed.
    private void IndexOverride(ulong id)
    {
        if (!Zone.Nature!.TryGet(id, out NaturalObject o))
        {
            return;
        }
        (int, int, int) cell = NatureCell(o.Position);
        if (Zone.Natural!.TryGetOverride(id, out _))
        {
            if (!_overrideCells.TryGetValue(cell, out HashSet<ulong>? set))
            {
                set = new HashSet<ulong>();
                _overrideCells.Add(cell, set);
            }
            set.Add(id);
        }
        else if (_overrideCells.TryGetValue(cell, out HashSet<ulong>? set))
        {
            set.Remove(id);
        }
    }

    // The changed natural objects of every area that came into range, then those that changed since the last snapshot.
    private void ReplicateNature(PlayerSession session, Vector3 centre)
    {
        ThingView view = session.State<ThingView>();
        _naturalBatch.Clear();
        float radius = Options.RelevanceRadius, near = radius + PhysicsWorld.TileSize, far = radius + 3 * PhysicsWorld.TileSize;
        // The cubes with changed objects that came into range: their changes, once (what changed this tick goes below).
        foreach (((int X, int Y, int Z) cell, HashSet<ulong> set) in _overrideCells)
        {
            if (set.Count == 0 || CellDistance(cell, centre) > near || !view.NatureCells.Add(cell))
            {
                continue;
            }
            foreach (ulong id in set)
            {
                if (!_naturalsToSend.Contains(id))
                {
                    _naturalBatch.Add(NaturalOf(id));
                }
            }
        }
        _cellsLeaving.Clear();
        foreach ((int X, int Y, int Z) cell in view.NatureCells)
        {
            if (CellDistance(cell, centre) > far)
            {
                _cellsLeaving.Add(cell);
            }
        }
        foreach ((int, int, int) cell in _cellsLeaving)
        {
            view.NatureCells.Remove(cell);
        }
        // What changed since the last snapshot, where the player knows the ground or is close.
        foreach (ulong id in _naturalsToSend)
        {
            if (!Zone.Nature!.TryGet(id, out NaturalObject o))
            {
                continue;
            }
            (int, int, int) cell = NatureCell(o.Position);
            if (view.NatureCells.Contains(cell) || Vector3.Distance(o.Position, centre) <= near)
            {
                view.NatureCells.Add(cell);
                _naturalBatch.Add(NaturalOf(id));
            }
        }
        for (int start = 0; start < _naturalBatch.Count; start += 64)
        {
            int count = Math.Min(64, _naturalBatch.Count - start);
            Writer.Reset();
            new NaturalStates { States = _naturalBatch.GetRange(start, count).ToArray() }.WriteMessage(Writer);
            Send(session);
        }
    }

    private NaturalNet NaturalOf(ulong id)
    {
        if (Zone.Natural!.TryGetOverride(id, out NaturalOverride o) && Zone.Nature!.TryGet(id, out NaturalObject obj) &&
            Content.Natural(obj.Kind) is { } kind)
        {
            float max = kind.IsHarvested ? NaturalShapes.MaxHealth(obj, kind) : 1;
            byte health = kind.IsHarvested ? (byte)Math.Clamp(Math.Round(o.Health / max * 255), 0, 255) : (byte)(o.Depleted ? 0 : 255);
            // How fresh the last hit is when this goes out (an area coming into range sends old hits: no effects for them).
            long age = o.LastHitTick > 0 ? Zone.Tick - o.LastHitTick : long.MaxValue;
            var net = new NaturalNet
            {
                Id = id, Health = health, HasWeakSpot = o.HasWeakSpot, WeakSpotYaw = o.WeakSpotYaw, WeakSpotLift = o.WeakSpotLift, Depleted = o.Depleted,
                Streak = o.Streak, HitAge = (byte)Math.Clamp(age, 0, NaturalNet.OldHit),
            };
            net.SetHitOffset(o.LastHit);
            return net;
        }
        return new NaturalNet { Id = id, Health = 255 };
    }
}

/// <summary>What a player was last sent of a static thing: its revision, its contents' revision, what they were told of it.</summary>
public readonly record struct ThingSent(uint Revision, uint Contents, ViewerFlags Flags, uint Moved = 0);

/// <summary>The accessor on the server (GROUND_SYSTEMS.md §12.5): the static things and natural objects as replicated.</summary>
public static class ThingModuleAccess
{
    public static ThingModule Things(this GameServer server) => server.Require<ThingModule>();
}
