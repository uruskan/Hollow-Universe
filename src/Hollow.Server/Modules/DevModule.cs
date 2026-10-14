using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Hollow.Net.Protocol;
using Hollow.Simulation;
using Hollow.Simulation.Animals;
using Hollow.Simulation.Building;
using Hollow.Simulation.Diagnostics;
using Hollow.Simulation.Players;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;
using LiteNetLib;

namespace Hollow.Server;

/// <summary>
/// The F2 developer panel's server side (GROUND.md §8.5): who watches; every half second the stats (the tick's profile
/// window, what the zone holds, the game's counters, the process, everyone on); a target described when asked; the
/// animals' nav grid and spawn areas around a watcher; the server's log as it is written. Only an admin is answered
/// (GROUND.md §8.2.2), and nothing is sent while nobody asks.
/// </summary>
public sealed class DevModule : ServerModule
{
    private const int StatsEveryTicks = TickRate / 2;
    private const int NavEveryTicks = TickRate;
    private const int AreasEveryTicks = TickRate * 2;
    private const float AreasRadius = 300f;
    private const int LogKept = 200;
    private const int LogLinesAMessage = 32;
    private const int InspectsASecond = 8;

    /// <summary>The nav window: this many cells of <see cref="NavGrid.CellSize"/> a side around the watcher (96 m).</summary>
    public const int NavCells = 48;

    private readonly ConcurrentQueue<DevLineNet> _logged = new();
    private readonly List<DevLineNet> _log = new();
    private readonly List<DevSectionNet> _sections = new();
    private readonly List<DevPlayerNet> _players = new();
    private readonly List<DevLineNet> _lines = new();
    private readonly List<SpawnArea> _nearAreas = new();
    private DevCellNet[]? _cells;
    private float _maxSlope = -1;

    /// <summary>What a player asked for, and their connection's rates (kept for everyone, for the PLAYERS tab).</summary>
    private sealed class Watch
    {
        public bool On, Nav, Areas, Log;
        public long BytesSent, BytesReceived;
        public float KilobitsDown, KilobitsUp;
        public long LastNav = long.MinValue, LastAreas = long.MinValue;
        public double InspectTokens = InspectsASecond;
        public long InspectTick;
    }

    protected internal override void Init(MessageHandlers<PlayerSession> handlers)
    {
        handlers.On<DevWatch>((PlayerSession session, in DevWatch m) => OnWatch(session, m));
        handlers.On<DevInspect>((PlayerSession session, in DevInspect m) => OnInspect(session, m));
        // On the logging thread: queued, taken on the tick's.
        Server.Logged += line => _logged.Enqueue(new DevLineNet
        {
            Label = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            Value = Clip(line, 190),
        });
    }

    protected internal override void Joined(PlayerSession session, bool newCharacter)
    {
        Watch w = session.State<Watch>();
        w.BytesSent = session.Peer.Statistics.BytesSent;
        w.BytesReceived = session.Peer.Statistics.BytesReceived;
    }

    /// <summary>Whether the server answers this player at all: in the world, and an admin.</summary>
    private bool Answered(PlayerSession session) => session.Playing && Server.Console().IsAdmin(session);

    private void OnWatch(PlayerSession session, in DevWatch m)
    {
        Watch w = session.State<Watch>();
        bool wasOn = w.On, hadLog = w.Log;
        w.On = m.On;
        w.Nav = m.Nav;
        w.Areas = m.Areas;
        w.Log = m.Log;
        w.LastNav = w.LastAreas = long.MinValue;
        if (!Answered(session))
        {
            return;
        }
        if (w.On != wasOn)
        {
            Log($"[dev] {session.Name} {(w.On ? "watches the server" : "stopped watching")}");
        }
        if (w.Log && !hadLog)
        {
            SendLog(session, _log);
        }
    }

    private void OnInspect(PlayerSession session, in DevInspect m)
    {
        Watch w = session.State<Watch>();
        if (!w.On || !Answered(session))
        {
            return;
        }
        w.InspectTokens = Math.Min(InspectsASecond, w.InspectTokens + (Zone.Tick - w.InspectTick) * (double)InspectsASecond / TickRate);
        w.InspectTick = Zone.Tick;
        if (w.InspectTokens < 1)
        {
            return;
        }
        w.InspectTokens -= 1;
        _lines.Clear();
        DevInspector.Describe(Zone, Content, session, new TargetRef(m.Target.Family, m.Target.Id, m.Target.Part), _lines);
        Writer.Reset();
        new DevInspected { Target = m.Target, Lines = _lines.ToArray() }.WriteMessage(Writer);
        session.Peer.Send(Writer.Data, DeliveryMethod.ReliableOrdered);
    }

    protected internal override void AfterStep()
    {
        FlushLog();
        long tick = Zone.Tick;
        if (tick % StatsEveryTicks == 0)
        {
            SendStats();
        }
        if (Zone.Get<AnimalSystem>()?.World is not { } animals)
        {
            return;
        }
        foreach (PlayerSession s in Players)
        {
            Watch w = s.State<Watch>();
            if (!(w.Nav || w.Areas) || !Answered(s))
            {
                continue;
            }
            Vector3 at = Zone.Entities.Get<Character>(s.Entity).State.Position;
            if (w.Nav && tick - w.LastNav >= NavEveryTicks)
            {
                w.LastNav = tick;
                SendNav(s, animals.Nav, at);
            }
            if (w.Areas && tick - w.LastAreas >= AreasEveryTicks)
            {
                w.LastAreas = tick;
                SendAreas(s, animals, at);
            }
        }
    }

    // ---------------------------------------------------------------- the stats

    private void SendStats()
    {
        TickProfile profile = Profile;
        double interval = profile.WindowTicks > 0 ? (double)profile.WindowTicks / TickRate : (double)StatsEveryTicks / TickRate;
        bool anyone = false;
        foreach (PlayerSession s in Players)
        {
            Watch w = s.State<Watch>();
            NetStatistics stats = s.Peer.Statistics;
            long sent = stats.BytesSent, received = stats.BytesReceived;
            w.KilobitsDown = (float)((sent - w.BytesSent) * 8 / 1000.0 / interval);
            w.KilobitsUp = (float)((received - w.BytesReceived) * 8 / 1000.0 / interval);
            w.BytesSent = sent;
            w.BytesReceived = received;
            anyone |= w.On && Answered(s);
        }
        if (anyone)
        {
            Writer.Reset();
            Stats(profile, (float)interval).WriteMessage(Writer);
            foreach (PlayerSession s in Players)
            {
                if (s.State<Watch>().On && Answered(s))
                {
                    s.Peer.Send(Writer.Data, DeliveryMethod.ReliableOrdered);
                }
            }
        }
        profile.ResetWindow();
    }

    private DevStats Stats(TickProfile profile, float interval)
    {
        Zone zone = Zone;
        int characters = 0, asleep = 0, dead = 0, wounded = 0;
        foreach (PlayerLink link in zone.Entities.Pool<PlayerLink>().Values)
        {
            Player p = link.Player;
            characters++;
            asleep += p.Sleeping ? 1 : 0;
            dead += p.Dead ? 1 : 0;
            wounded += p.Wounded ? 1 : 0;
        }
        int alive = 0, awake = 0, lying = 0;
        if (zone.Get<AnimalSystem>()?.World is { } animals)
        {
            foreach (Animal a in animals.All)
            {
                if (a.Removed)
                {
                    continue;
                }
                alive += a.IsDead ? 0 : 1;
                lying += a.IsDead ? 1 : 0;
            }
            awake = animals.Awake;
        }
        int deployables = 0, props = 0, bags = 0, corpses = 0;
        foreach (WorldObject o in zone.Things().Statics())
        {
            deployables += o.Kind == StaticKind.Deployable ? 1 : 0;
            props += o.Kind == StaticKind.Prop ? 1 : 0;
            bags += o.Kind == StaticKind.DroppedBag ? 1 : 0;
            corpses += o.Kind == StaticKind.Corpse ? 1 : 0;
        }
        BuildingWorld? building = zone.Building().World;
        GameCounters c = Server.Survival().Counters;
        _sections.Clear();
        foreach ((ProfileSection section, int _) in TickProfile.Tree)
        {
            _sections.Add(new DevSectionNet
            {
                Section = section,
                AverageMs = (float)profile.WindowAverageMs(section),
                WorstMs = (float)profile.WindowWorstMs(section),
            });
        }
        _players.Clear();
        foreach (PlayerSession s in Players.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).Take(256))
        {
            Watch w = s.State<Watch>();
            byte state = 0;
            Vector3 at = default;
            if (s.Playing)
            {
                at = zone.Entities.Get<Character>(s.Entity).State.Position;
                state = zone.Players().PlayerOf(s.Entity) is { } p ? (byte)(p.Dead ? 3 : p.Wounded ? 2 : 1) : (byte)1;
            }
            long applied = s.InputsApplied, missing = s.InputsMissing;
            _players.Add(new DevPlayerNet
            {
                Name = s.Name,
                Admin = Server.Console().IsAdmin(s),
                State = state,
                PingMs = (uint)Math.Max(0, s.Peer.RoundTripTime),
                X = at.X,
                Y = at.Y,
                Z = at.Z,
                OnTimePercent = (byte)(applied + missing == 0 ? 100 : Math.Clamp(100 * applied / (applied + missing), 0, 100)),
                InView = (uint)s.Relevant.Count,
                KilobitsDown = w.KilobitsDown,
                KilobitsUp = w.KilobitsUp,
            });
        }
        return new DevStats
        {
            Tick = (uint)zone.Tick,
            TickRate = TickRate,
            IntervalSeconds = interval,
            AverageMs = (float)profile.WindowAverageMs(ProfileSection.Tick),
            WorstMs = (float)profile.WindowWorstMs(ProfileSection.Tick),
            Overruns = Clamp(Server.Ticks.Overruns),
            GcPauseMs = (float)profile.WindowGcPauseMs,
            AllocatedKbPerTick = Clamp(profile.WindowTicks == 0 ? 0 : profile.WindowAllocatedBytes / 1024 / profile.WindowTicks),
            UpSeconds = Clamp((long)Server.UpSeconds),
            WorkingSetMb = Clamp(Environment.WorkingSet / (1024 * 1024)),
            HeapMb = Clamp(GC.GetTotalMemory(false) / (1024 * 1024)),
            Saves = Persistence != null,
            SinceSaveSeconds = Clamp((long)Server.SecondsSinceSave),
            RelevanceRadius = Options.RelevanceRadius,
            Characters = (uint)characters,
            Asleep = (uint)asleep,
            Dead = (uint)dead,
            Wounded = (uint)wounded,
            AnimalsAlive = (uint)alive,
            AnimalsAwake = (uint)awake,
            AnimalsDead = (uint)lying,
            Deployables = (uint)deployables,
            Props = (uint)props,
            Bags = (uint)bags,
            Corpses = (uint)corpses,
            Pieces = Clamp(building?.PieceCount ?? 0),
            Doors = Clamp(building?.Doors.Count() ?? 0),
            Claims = Clamp(building?.Claims.Count() ?? 0),
            Collapsing = Clamp(building?.Collapsing ?? 0),
            Ships = (uint)zone.Ships().All.Count,
            Explosives = (uint)zone.Explosives().All.Count,
            Arrows = (uint)zone.Arrows().All.Count,
            NaturalChanged = Clamp(zone.Natural?.Count ?? 0),
            Tiles = (uint)zone.Physics.TileCount,
            TilesOnDemand = Clamp(zone.Physics.TilesBuiltOnDemand),
            NetEntities = Clamp(Server.NetEntityCount),
            GatherHits = Clamp(c.GatherHits),
            Gathered = Clamp(c.Gathered),
            PickedUp = Clamp(c.PickedUp),
            Crafted = Clamp(c.Crafted),
            ArrowsShot = Clamp(c.Arrows),
            Shots = Clamp(c.Shots),
            BulletHits = Clamp(c.BulletHits),
            CharacterHits = Clamp(c.CharacterHits),
            Kills = Clamp(c.Kills),
            Downed = Clamp(c.Downed),
            Recovered = Clamp(c.Recovered),
            Deaths = Clamp(c.Deaths),
            AnimalHits = Clamp(c.AnimalHits),
            AnimalKills = Clamp(c.AnimalKills),
            Sections = _sections.ToArray(),
            Players = _players.ToArray(),
        };
    }

    private static uint Clamp(long value) => (uint)Math.Clamp(value, 0, uint.MaxValue);

    // ---------------------------------------------------------------- the nav grid and the spawn areas (GROUND.md §6.9)

    private void SendNav(PlayerSession session, NavGrid nav, Vector3 at)
    {
        if (_maxSlope < 0)
        {
            _maxSlope = Content.Animals.Count == 0 ? 0.6f : Content.Animals.Max(s => s.MaxSlope);
        }
        _cells ??= new DevCellNet[NavCells * NavCells];
        float half = NavCells * NavGrid.CellSize / 2;
        // The areas under the window, built ahead on worker threads and taken in by the animals' step; a cell whose
        // area isn't in yet says so.
        nav.Prefetch(at, half + NavGrid.AreaSize / 2);
        float ox = MathF.Floor((at.X - half) / NavGrid.CellSize) * NavGrid.CellSize;
        float oz = MathF.Floor((at.Z - half) / NavGrid.CellSize) * NavGrid.CellSize;
        for (int j = 0; j < NavCells; j++)
        {
            for (int i = 0; i < NavCells; i++)
            {
                float x = ox + (i + 0.5f) * NavGrid.CellSize, z = oz + (j + 0.5f) * NavGrid.CellSize;
                byte flags = 8;
                if (nav.IsReady(x, z))
                {
                    NavFlags f = nav.Flags(x, z);
                    flags = (byte)(((f & NavFlags.Water) != 0 ? 1 : 0) | ((f & NavFlags.Blocked) != 0 ? 2 : 0));
                    if (flags == 0 && !nav.Walkable(x, z, _maxSlope))
                    {
                        flags = 4;
                    }
                }
                _cells[j * NavCells + i] = new DevCellNet { Flags = flags };
            }
        }
        Writer.Reset();
        new DevNav { OriginX = ox, OriginZ = oz, Cells = NavCells, Flags = _cells }.WriteMessage(Writer);
        session.Peer.Send(Writer.Data, DeliveryMethod.ReliableOrdered);
    }

    private void SendAreas(PlayerSession session, AnimalWorld animals, Vector3 at)
    {
        _nearAreas.Clear();
        foreach (SpawnArea area in animals.Areas)
        {
            if (Vector3.DistanceSquared(area.Centre, at) <= AreasRadius * AreasRadius)
            {
                _nearAreas.Add(area);
            }
        }
        _nearAreas.Sort((a, b) => Vector3.DistanceSquared(a.Centre, at).CompareTo(Vector3.DistanceSquared(b.Centre, at)));
        var areas = new DevAreaNet[Math.Min(32, _nearAreas.Count)];
        for (int i = 0; i < areas.Length; i++)
        {
            SpawnArea area = _nearAreas[i];
            areas[i] = new DevAreaNet
            {
                CentreX = area.Centre.X,
                CentreY = area.Centre.Y,
                CentreZ = area.Centre.Z,
                Active = area.Active,
                Slots = area.Slots.Take(16).Select(s => new DevSlotNet
                {
                    Species = s.Species.Key,
                    Size = (uint)Math.Max(0, s.Size),
                    Living = (uint)Math.Max(0, s.Living),
                    HomeX = s.Home.X,
                    HomeY = s.Home.Y,
                    HomeZ = s.Home.Z,
                    Range = s.Range,
                }).ToArray(),
            };
        }
        Writer.Reset();
        new DevAreas { Areas = areas }.WriteMessage(Writer);
        session.Peer.Send(Writer.Data, DeliveryMethod.ReliableOrdered);
    }

    // ---------------------------------------------------------------- the log

    private void FlushLog()
    {
        int before = _log.Count;
        while (_logged.TryDequeue(out DevLineNet line))
        {
            _log.Add(line);
        }
        int added = _log.Count - before;
        if (added == 0)
        {
            return;
        }
        List<DevLineNet> fresh = _log.GetRange(_log.Count - added, added);
        if (_log.Count > LogKept)
        {
            _log.RemoveRange(0, _log.Count - LogKept);
        }
        foreach (PlayerSession s in Players)
        {
            if (s.State<Watch>().Log && Answered(s))
            {
                SendLog(s, fresh);
            }
        }
    }

    private void SendLog(PlayerSession session, IReadOnlyList<DevLineNet> lines)
    {
        for (int i = 0; i < lines.Count; i += LogLinesAMessage)
        {
            int n = Math.Min(LogLinesAMessage, lines.Count - i);
            var batch = new DevLineNet[n];
            for (int k = 0; k < n; k++)
            {
                batch[k] = lines[i + k];
            }
            Writer.Reset();
            new DevLog { Lines = batch }.WriteMessage(Writer);
            session.Peer.Send(Writer.Data, DeliveryMethod.ReliableOrdered);
        }
    }

    private static string Clip(string text, int most) => text.Length <= most ? text : text[..(most - 1)] + "…";
}

/// <summary>The accessor on the server (GROUND_SYSTEMS.md §12.5): the F2 panel's watchers, stats, descriptions and log.</summary>
public static class DevModuleAccess
{
    public static DevModule Dev(this GameServer server) => server.Require<DevModule>();
}
