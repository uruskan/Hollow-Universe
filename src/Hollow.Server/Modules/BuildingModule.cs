using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Hollow.Core;
using Hollow.Net.Protocol;
using Hollow.Persistence;
using Hollow.Simulation;
using Hollow.Simulation.Building;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Players;
using Hollow.Simulation.Targets;
using Hollow.Simulation.Capabilities;
using Hollow.Simulation.Diagnostics;

namespace Hollow.Server;

/// <summary>
/// Building on the server (GROUND.md §4.4, §6.3, phase 2): players' building intents (on the same budget as every
/// intent, each answered with the rules' reason) applied to the zone, and the replication of pieces, doors and claim
/// devices as static entities. A 64 m area of pieces is queued for a player when it comes within range; queued parts
/// go out a few hundred bytes a snapshot, nearest areas first, so a big base streams in over a few seconds without a
/// hitch; after that only changes travel. Frames are numbered once for everyone, so bots can share one copy.
/// </summary>
public sealed class BuildingModule : ServerModule
{
    /// <summary>Bytes of building a player is sent per snapshot at most (about 190 kbit/s while a base streams in).</summary>
    public const int BuildBytesPerSnapshot = 1200;

    /// <summary>The side of the areas pieces are replicated by, in metres.</summary>
    public const float BuildCellSize = 64f;

    // Rough bytes on the wire, for the budget.
    private const int PieceBytes = 44, DoorBytes = 44, ClaimBytes = 52, FrameBytes = 100, RemovedBytes = 8;

    private readonly Dictionary<(int X, int Y, int Z), List<Piece>> _buildCells = new();
    private readonly Dictionary<BuildingFrame, uint> _frameIds = new();
    private readonly Dictionary<uint, BuildingFrame> _frames = new();
    private readonly List<object> _buildChanged = new();
    private readonly HashSet<object> _buildChangedSet = new();
    private readonly List<(object Part, bool Destroyed)> _buildRemoved = new();
    private readonly List<(int X, int Y, int Z)> _cellsNear = new();
    private readonly List<PieceNet> _pieceBatch = new();
    private readonly List<DoorNet> _doorBatch = new();
    private readonly List<ClaimNet> _claimBatch = new();
    private readonly List<BuildFrameNet> _frameBatch = new();
    private readonly List<BuildIdNet>[] _removedBatch = { new(), new(), new() };
    private readonly HashSet<object> _buildDirty = new();
    private readonly HashSet<EntityId> _buildDeleted = new();
    private readonly Dictionary<ClaimDevice, uint> _storeRevisions = new();
    private uint _nextFrameId = 1;

    /// <summary>Pieces placed, for the load test.</summary>
    public long PiecesPlaced => Server.Placement().PlacedOf("piece");

    protected internal override void Init(MessageHandlers<PlayerSession> handlers)
    {
        Zone.Building().World!.Changed += OnBuildingChanged;
        handlers.On<HammerIntent>((PlayerSession session, in HammerIntent message) =>
        {
            HammerIntent m = message;
            Entity me = session.Entity;
            Intent(session, m.Seq, () => m.Action == HammerAction.Repair
                ? Zone.Building().Repair(me, new TargetRef((TargetFamily)m.Family, m.Target, m.Part))
                : Zone.Building().Hammer(me, m.Action, new EntityId(m.Target), m.Grade));
        });
    }

    // ---------------------------------------------------------------- loading and saving (GROUND.md §4.7)

    // The body's building as it was saved: pieces (stability solved once), doors and locks, claim devices with their
    // stores; then upkeep and decay are caught up for the time the server was down, in one closed-form step.
    protected internal override void Restore(ZoneRecord stored, HashSet<EntityId> saved)
    {
        BuildingWorld b = Zone.Building().World!;
        int pieces = 0, doors = 0, claims = 0, lost = 0;
        bool Try(BuildingRecord record, Func<bool> restore)
        {
            try
            {
                if (restore())
                {
                    return true;
                }
            }
            catch (Exception e) when (e is EndOfStreamException or InvalidDataException or ArgumentException)
            {
                Log($"building {record.Kind} {record.Id} doesn't load ({e.Message}); left out");
            }
            _buildDeleted.Add(record.Id);
            lost++;
            return false;
        }
        b.BeginBatch();
        foreach (BuildingRecord record in stored.Building.Where(r => r.Kind == BuildPartKind.Piece))
        {
            pieces += Try(record, () => BuildingCodec.RestorePiece(b, record.Id, record.Data, Zone.Frame) != null) ? 1 : 0;
        }
        b.EndBatch(Zone.Building().BuildingNow);
        foreach (BuildingRecord record in stored.Building.Where(r => r.Kind == BuildPartKind.Door))
        {
            doors += Try(record, () => BuildingCodec.RestoreDoor(b, record.Id, record.Data) != null) ? 1 : 0;
        }
        foreach (BuildingRecord record in stored.Building.Where(r => r.Kind == BuildPartKind.Claim))
        {
            claims += Try(record, () => RestoreClaim(record)) ? 1 : 0;
        }
        _buildDirty.Clear();
        foreach (ClaimDevice c in b.Claims)
        {
            if (Zone.Building().DeployableOf(c)?.Storage is { } store)
            {
                _storeRevisions[c] = store.Revision;
            }
        }
        double advancedAt = BuildingCodec.DecodeZone(stored.Meta);
        double away = double.IsNaN(advancedAt) ? 0 : Math.Max(0, Zone.Building().BuildingNow - advancedAt);
        Zone.Building().BuildingAdvancedAt = advancedAt;
        Zone.Building().AdvanceBuilding();
        Log($"loaded the building: {pieces} pieces, {doors} doors, {claims} claim devices ({lost} left out); " +
            $"{away / 3600:0.0} h of upkeep and decay caught up, {b.PieceCount} pieces stand");
        // What stood on pieces that didn't come back goes now (GROUND_SYSTEMS.md §3.6).
        Zone.Placement().SettleSupports(ships: false);
    }

    private bool RestoreClaim(BuildingRecord record)
    {
        BuildingWorld b = Zone.Building().World!;
        StoredClaim stored = BuildingCodec.DecodeClaim(record.Id, record.Data, Content);
        ClaimRecord c = stored.Claim;
        if (b.PieceOf(c.Support) is not { } support ||
            b.RestoreClaim(record.Id, c.Owner, support, c.Position, c.Yaw, c.Health, c.Authorised) is not { } device)
        {
            return false;
        }
        if (Zone.Building().DeployableOf(device)?.Storage is { } store)
        {
            for (int i = 0; i < stored.Store.Count && i < store.Size; i++)
            {
                store.Set(i, stored.Store[i]);
            }
            store.ClearDirty();
        }
        foreach ((string material, double debt) in stored.Debt)
        {
            b.RestoreDebt(device.Structure, material, debt);
        }
        return true;
    }

    // What changed since the last save (only that is written), what is gone, and when upkeep was last applied.
    protected internal override void Save(ZoneSave save, HashSet<EntityId> current)
    {
        BuildingWorld b = Zone.Building().World!;
        foreach (ClaimDevice c in b.Claims)
        {
            if (Zone.Building().DeployableOf(c)?.Storage is { } store && (!_storeRevisions.TryGetValue(c, out uint revision) || revision != store.Revision))
            {
                _storeRevisions[c] = store.Revision;
                _buildDirty.Add(c);
            }
        }
        foreach (object part in _buildDirty)
        {
            switch (part)
            {
                case Piece { Gone: false } p:
                    save.Building.Add(new BuildingRecord(p.Id, BuildPartKind.Piece, BuildingCodec.EncodePiece(p, Zone.Frame)));
                    break;
                case Door { Gone: false } d:
                    save.Building.Add(new BuildingRecord(d.Id, BuildPartKind.Door, BuildingCodec.EncodeDoor(d)));
                    break;
                case ClaimDevice { Gone: false } c:
                    save.Building.Add(new BuildingRecord(c.Id, BuildPartKind.Claim,
                        BuildingCodec.EncodeClaim(c, Zone.Building().DeployableOf(c)?.Storage, b.DebtOf(c.Structure))));
                    break;
            }
        }
        save.RemovedBuilding.AddRange(_buildDeleted);
        _buildDirty.Clear();
        _buildDeleted.Clear();
        foreach (ClaimDevice gone in _storeRevisions.Keys.Where(c => c.Gone).ToList())
        {
            _storeRevisions.Remove(gone);
        }
        save.Meta = BuildingCodec.EncodeZone(Zone.Building().BuildingAdvancedAt);
    }

    private static (int X, int Y, int Z) BuildCell(Vector3 p) =>
        ((int)MathF.Floor(p.X / BuildCellSize), (int)MathF.Floor(p.Y / BuildCellSize), (int)MathF.Floor(p.Z / BuildCellSize));

    private static (int X, int Y, int Z) CellOf(Piece piece) => BuildCell(piece.ZoneTransform.Position);

    // Everything the rules change goes on this snapshot's lists; pieces are indexed by area.
    private void OnBuildingChanged(BuildingEvent e)
    {
        switch (e.Change)
        {
            case BuildingChange.PieceAdded when e.Piece is { } piece:
            {
                _buildDirty.Add(piece);
                (int, int, int) cell = CellOf(piece);
                if (!_buildCells.TryGetValue(cell, out List<Piece>? list))
                {
                    list = new List<Piece>();
                    _buildCells.Add(cell, list);
                }
                list.Add(piece);
                Changed(piece);
                break;
            }
            case BuildingChange.PieceRemoved when e.Piece is { } piece:
            {
                Deleted(piece, piece.Id);
                (int, int, int) cell = CellOf(piece);
                if (_buildCells.TryGetValue(cell, out List<Piece>? list))
                {
                    list.Remove(piece);
                    if (list.Count == 0)
                    {
                        _buildCells.Remove(cell);
                    }
                }
                _buildRemoved.Add((piece, true));
                break;
            }
            case BuildingChange.PieceChanged when e.Piece is { } piece:
                Changed(piece);
                break;
            case BuildingChange.DoorAdded or BuildingChange.DoorChanged when e.Door is { } door:
                Changed(door);
                break;
            case BuildingChange.DoorRemoved when e.Door is { } door:
                Deleted(door, door.Id);
                _buildRemoved.Add((door, true));
                break;
            case BuildingChange.ClaimAdded or BuildingChange.ClaimChanged when e.Claim is { } claim:
                Changed(claim);
                break;
            case BuildingChange.ClaimRemoved when e.Claim is { } claim:
                Deleted(claim, claim.Id);
                _buildRemoved.Add((claim, true));
                break;
        }

        void Changed(object part)
        {
            _buildDirty.Add(part);
            if (_buildChangedSet.Add(part))
            {
                _buildChanged.Add(part);
            }
        }

        void Deleted(object part, EntityId id)
        {
            _buildDirty.Remove(part);
            _buildDeleted.Add(id);
        }
    }

    // ---------------------------------------------------------------- replication

    private uint FrameId(in BuildingFrame frame)
    {
        if (!_frameIds.TryGetValue(frame, out uint id))
        {
            id = _nextFrameId++;
            _frameIds.Add(frame, id);
            _frames.Add(id, frame);
        }
        return id;
    }

    // One player's building for this snapshot: what is gone, what changed, the areas that came into or out of range,
    // then as many queued parts as the budget allows.
    protected internal override void Replicate(PlayerSession session, Vector3 centre)
    {
        using (Profile.Time(ProfileSection.ReplicateBuilding))
        {
            ReplicateBuilding(session, centre);
        }
    }

    private void ReplicateBuilding(PlayerSession session, Vector3 centre)
    {
        BuildView view = session.State<BuildView>();
        for (int kind = 0; kind < _removedBatch.Length; kind++)
        {
            _removedBatch[kind].Clear();
            _destroyedBatch[kind].Clear();
        }
        int budget = BuildBytesPerSnapshot;

        // Gone: what this player had.
        foreach ((object part, bool _) in _buildRemoved)
        {
            view.Queued.Remove(part);
            if (view.Sent.Remove(part))
            {
                Removed(part);
            }
        }

        // What this player is told of doors and claim devices changed with them (a team, a key): all they have go again.
        if (session.ViewerChanged)
        {
            foreach (object part in view.Sent)
            {
                if (part is Door or ClaimDevice && view.Queued.Add(part))
                {
                    view.Updates.Add(part);
                }
            }
        }

        // Changed: what this player has, or would get now.
        foreach (object part in _buildChanged)
        {
            switch (part)
            {
                case Piece { Gone: false } piece when view.Sent.Contains(piece) || view.Cells.Contains(CellOf(piece)):
                case Door { Gone: false } door when view.Sent.Contains(door.Frame):
                case ClaimDevice { Gone: false } claim when view.Sent.Contains(claim.Support):
                    if (view.Queued.Add(part))
                    {
                        view.Updates.Add(part);
                    }
                    break;
            }
        }

        // Areas coming into range (nearest first), and going out of it.
        float radius = Options.RelevanceRadius, near = radius + BuildCellSize * 0.87f, far = near + BuildCellSize;
        _cellsNear.Clear();
        foreach ((int x, int y, int z) in _buildCells.Keys)
        {
            if (!view.Cells.Contains((x, y, z)) && CellDistance(x, y, z, centre) <= near)
            {
                _cellsNear.Add((x, y, z));
            }
        }
        _cellsNear.Sort((a, b) => CellDistance(a.X, a.Y, a.Z, centre).CompareTo(CellDistance(b.X, b.Y, b.Z, centre)));
        foreach ((int, int, int) cell in _cellsNear)
        {
            view.Cells.Add(cell);
            foreach (Piece piece in _buildCells[cell])
            {
                if (!view.Sent.Contains(piece) && view.Queued.Add(piece))
                {
                    view.Stream.Enqueue(piece);
                }
            }
        }
        view.Leaving.Clear();
        foreach ((int x, int y, int z) in view.Cells)
        {
            if (CellDistance(x, y, z, centre) > far)
            {
                view.Leaving.Add((x, y, z));
            }
        }
        foreach ((int, int, int) cell in view.Leaving)
        {
            view.Cells.Remove(cell);
            if (!_buildCells.TryGetValue(cell, out List<Piece>? pieces))
            {
                continue;
            }
            foreach (Piece piece in pieces)
            {
                view.Queued.Remove(piece);
                if (view.Sent.Remove(piece))
                {
                    budget -= RemovedBytes;
                    _removedBatch[(int)BuildPartKind.Piece].Add(new BuildIdNet { Id = piece.Id.Value });
                    // The client forgets the door and device with their piece.
                    if (piece.Door != null)
                    {
                        view.Sent.Remove(piece.Door);
                        view.Queued.Remove(piece.Door);
                    }
                    if (piece.Structure.Claim is { } claim && claim.Support == piece)
                    {
                        view.Sent.Remove(claim);
                        view.Queued.Remove(claim);
                    }
                }
            }
        }
        // Removals go before anything new: a mirror may give a freed body ID to a new piece.
        SendRemoved(session, destroyed: true, _destroyedBatch);
        SendRemoved(session, destroyed: false, _removedBatch);

        // Changes first, then the stream, within the budget.
        _pieceBatch.Clear();
        _doorBatch.Clear();
        _claimBatch.Clear();
        _frameBatch.Clear();
        int updates = 0;
        while (budget > 0)
        {
            object? part = null;
            if (updates < view.Updates.Count)
            {
                part = view.Updates[updates++];
            }
            else if (view.Stream.Count > 0)
            {
                part = view.Stream.Dequeue();
            }
            else
            {
                break;
            }
            if (!view.Queued.Remove(part))
            {
                continue;
            }
            budget -= Write(session, view, part);
        }
        view.Updates.RemoveRange(0, updates);
        FlushBuild(session);
    }

    private readonly List<BuildIdNet>[] _destroyedBatch = { new(), new(), new() };

    private void Removed(object part)
    {
        switch (part)
        {
            case Piece piece:
                _destroyedBatch[(int)BuildPartKind.Piece].Add(new BuildIdNet { Id = piece.Id.Value });
                break;
            case Door door:
                _destroyedBatch[(int)BuildPartKind.Door].Add(new BuildIdNet { Id = door.Id.Value });
                break;
            case ClaimDevice claim:
                _destroyedBatch[(int)BuildPartKind.Claim].Add(new BuildIdNet { Id = claim.Id.Value });
                break;
        }
    }

    private void SendRemoved(PlayerSession session, bool destroyed, List<BuildIdNet>[] batches)
    {
        for (int kind = 0; kind < batches.Length; kind++)
        {
            List<BuildIdNet> ids = batches[kind];
            for (int start = 0; start < ids.Count; start += 255)
            {
                Writer.Reset();
                new BuildRemoved { Kind = (BuildPartKind)kind, Destroyed = destroyed, Ids = ids.GetRange(start, Math.Min(255, ids.Count - start)).ToArray() }
                    .WriteMessage(Writer);
                Send(session);
            }
            ids.Clear();
        }
    }

    private static float CellDistance(int x, int y, int z, Vector3 centre) =>
        Vector3.Distance(new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) * BuildCellSize, centre);

    // Puts one part on this snapshot's batches; returns its bytes.
    private int Write(PlayerSession session, BuildView view, object part)
    {
        int bytes = 0;
        switch (part)
        {
            case Piece { Gone: false } piece:
            {
                uint frame = FrameId(piece.Structure.Frame);
                if (view.Frames.Add(frame))
                {
                    _frameBatch.Add(NetOf(frame, _frames[frame]));
                    bytes += FrameBytes;
                }
                _pieceBatch.Add(NetOf(piece, frame));
                bytes += PieceBytes;
                view.Sent.Add(piece);
                // A door or device follows its piece.
                if (piece.Door is { Gone: false } door && !view.Sent.Contains(door) && view.Queued.Add(door))
                {
                    view.Updates.Add(door);
                }
                if (piece.Structure.Claim is { Gone: false } claim && claim.Support == piece && !view.Sent.Contains(claim) && view.Queued.Add(claim))
                {
                    view.Updates.Add(claim);
                }
                break;
            }
            case Door { Gone: false } door when view.Sent.Contains(door.Frame):
                _doorBatch.Add(NetOf(door, session));
                view.Sent.Add(door);
                bytes += DoorBytes;
                break;
            case ClaimDevice { Gone: false } claim when view.Sent.Contains(claim.Support):
                _claimBatch.Add(NetOf(claim, session));
                view.Sent.Add(claim);
                bytes += ClaimBytes;
                break;
        }
        return bytes;
    }

    // Frames, then pieces, then doors and devices (each needs what comes before it).
    private void FlushBuild(PlayerSession session)
    {
        for (int start = 0; start < _frameBatch.Count; start += 16)
        {
            Writer.Reset();
            new BuildFrames { Frames = _frameBatch.GetRange(start, Math.Min(16, _frameBatch.Count - start)).ToArray() }.WriteMessage(Writer);
            Send(session);
        }
        for (int start = 0; start < _pieceBatch.Count; start += 30)
        {
            Writer.Reset();
            new BuildPieces { Pieces = _pieceBatch.GetRange(start, Math.Min(30, _pieceBatch.Count - start)).ToArray() }.WriteMessage(Writer);
            Send(session);
        }
        for (int start = 0; start < _doorBatch.Count; start += 32)
        {
            Writer.Reset();
            new BuildDoors { Doors = _doorBatch.GetRange(start, Math.Min(32, _doorBatch.Count - start)).ToArray() }.WriteMessage(Writer);
            Send(session);
        }
        for (int start = 0; start < _claimBatch.Count; start += 16)
        {
            Writer.Reset();
            new BuildClaims { Claims = _claimBatch.GetRange(start, Math.Min(16, _claimBatch.Count - start)).ToArray() }.WriteMessage(Writer);
            Send(session);
        }
    }

    protected internal override void Finish()
    {
        _buildChanged.Clear();
        _buildChangedSet.Clear();
        _buildRemoved.Clear();
    }

    private static BuildFrameNet NetOf(uint id, in BuildingFrame f) => new()
    {
        Id = id,
        OriginX = f.Origin.X, OriginY = f.Origin.Y, OriginZ = f.Origin.Z,
        XX = f.AxisX.X, XY = f.AxisX.Y, XZ = f.AxisX.Z,
        YX = f.AxisY.X, YY = f.AxisY.Y, YZ = f.AxisY.Z,
        ZX = f.AxisZ.X, ZY = f.AxisZ.Y, ZZ = f.AxisZ.Z,
    };

    private static ushort Fraction(double value, double max) => (ushort)Math.Clamp(Math.Round(value / Math.Max(1e-9, max) * 65535), 0, 65535);

    private PieceNet NetOf(Piece p, uint frame)
    {
        BuildingData data = Zone.Building().World!.Data;
        double editLeft = data.EditWindow - (Zone.Building().BuildingNow - p.PlacedAt);
        return new PieceNet
        {
            Id = p.Id.Value,
            Frame = frame,
            Piece = (byte)p.Def.Id,
            Grade = (byte)p.Grade,
            Yaw = (byte)p.Pose.Yaw,
            X = p.Pose.Position.X,
            Y = p.Pose.Position.Y,
            Z = p.Pose.Position.Z,
            Health = Fraction(p.Health, p.MaxHealth),
            Body = p.BodyId,
            EditLeft = (uint)Math.Clamp(Math.Ceiling(editLeft), 0, data.EditWindow + 1),
            Collapsing = p.Collapsing,
        };
    }

    private DoorNet NetOf(Door d, PlayerSession session)
    {
        BuildingData data = Zone.Building().World!.Data;
        return new DoorNet
        {
            Id = d.Id.Value,
            Frame = d.Frame.Id.Value,
            Door = (byte)Math.Max(0, data.IndexOf(d.Def)),
            Flip = d.Flip != 0,
            Open = d.Open,
            Health = Fraction(d.Health, d.Def.Health),
            Lock = d.Lock?.Kind ?? LockKind.None,
            LockId = d.Lock?.Id.Value ?? 0,
            Flags = Zone.Targets().FlagsFor(d.Ref, session.Viewer),
            Body0 = d.BodyIds.Count > 0 ? d.BodyIds[0] : 0,
            Body1 = d.BodyIds.Count > 1 ? d.BodyIds[1] : 0,
        };
    }

    private ClaimNet NetOf(ClaimDevice c, PlayerSession session)
    {
        double protectedSeconds = Zone.Building().World!.ProtectedSeconds(c.Structure);
        return new ClaimNet
        {
            Id = c.Id.Value,
            Support = c.Support.Id.Value,
            X = c.Position.X,
            Y = c.Position.Y,
            Z = c.Position.Z,
            Yaw = (byte)c.Yaw,
            Health = Fraction(c.Health, Zone.Building().World.Data.ClaimHealth),
            Flags = Zone.Targets().FlagsFor(c.Ref, session.Viewer),
            Body = c.BodyId,
            ProtectedMinutes = double.IsInfinity(protectedSeconds) ? uint.MaxValue : (uint)Math.Clamp(protectedSeconds / 60, 0, uint.MaxValue - 1),
        };
    }

    /// <summary>What one player has been sent of the building, and what waits to go.</summary>
    private sealed class BuildView
    {
        /// <summary>Areas within range: their pieces are sent or queued.</summary>
        public readonly HashSet<(int X, int Y, int Z)> Cells = new();

        public readonly List<(int X, int Y, int Z)> Leaving = new();

        /// <summary>Pieces, doors and devices the client has.</summary>
        public readonly HashSet<object> Sent = new();

        /// <summary>Parts waiting in <see cref="Updates"/> or <see cref="Stream"/> (a part leaves this set when it goes, or is cancelled).</summary>
        public readonly HashSet<object> Queued = new();

        /// <summary>Changes and new parts near the player: they go first.</summary>
        public readonly List<object> Updates = new();

        /// <summary>Parts of areas that came into range, nearest first.</summary>
        public readonly Queue<object> Stream = new();

        public readonly HashSet<uint> Frames = new();
    }
}

/// <summary>The accessor on the server (GROUND_SYSTEMS.md §12.5): the building's replication and storage.</summary>
public static class BuildingModuleAccess
{
    public static BuildingModule Building(this GameServer server) => server.Require<BuildingModule>();
}
