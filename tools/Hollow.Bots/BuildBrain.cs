using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hollow.Client;
using Hollow.Core;
using Hollow.Simulation;
using Hollow.Simulation.Building;
using Hollow.Simulation.Placement;
using Hollow.Simulation.Content;
using Hollow.Simulation.Items;
using Hollow.Simulation.Nature;

namespace Hollow.Bots;

/// <summary>
/// A bot that builds a small base (GROUND.md §5.3) through the client's building API, as a player would: it walks to a
/// plot of its own (a 10 × 10 grid of plots 36 m apart round the zone's origin), places a foundation on the ground and
/// a second beside it, walls round them with a doorway, two floors on top, fits a wooden door with a code lock and sets
/// the code, upgrades the foundations to stone and the walls to wood, and places its claim device; then it opens and
/// shuts its door now and then. Each step is checked with the same rules first (the ghost), sent as an intent and
/// waited for; a refused one is counted and skipped. With the <c>build-kit</c> loot table it has all it needs.
/// </summary>
public sealed class BuildBrain
{
    /// <summary>
    /// Plots are this far apart: a claim device covers 16 m round every piece and 25 m round itself (with 2.25 m of
    /// padding for the hammer), so a neighbour's base must end more than 27 m from it.
    /// </summary>
    public const float PlotSpacing = 36f;

    private enum Stage
    {
        WaitForKit,
        Walk,
        Foundation,
        Approach,
        Build,
        Done,
        GaveUp,
    }

    private readonly Rng _rng;
    private readonly Queue<Func<GameClient, uint>> _tasks = new();
    private Vector3 _site;
    private Stage _stage = Stage.WaitForKit;
    private uint _waiting;
    private double _sentAt;
    private double _nextTask;
    private double _nextToggle;
    private double _taskSince;
    private Vector3 _walkFrom = new(float.NaN);
    private float _closest;
    private double _progressAt;
    private int _tries;
    private bool _answered;
    private bool _subscribed;
    private float _yaw;
    private Piece? _f1, _f2, _doorway;

    public BuildBrain(int index)
    {
        _rng = new Rng(Hash64.Of((ulong)index, 0xB01D));
        _site = new Vector3((index % 10 - 4.5f) * PlotSpacing, 0, (index / 10 % 10 - 4.5f) * PlotSpacing);
    }

    /// <summary>Where the bot's own refusals are told (the first bot's).</summary>
    public Action<string>? Log { get; init; }

    /// <summary>Intents sent and answered as done, and refused.</summary>
    public int Done { get; private set; }

    public int Refused { get; private set; }

    /// <summary>Where the bot is in building its base (for diagnostics).</summary>
    public string Progress => $"{_stage}, {_tries} tries, {_tasks.Count} steps left";

    /// <summary>Doors opened or shut once the base stands.</summary>
    public int Toggles { get; private set; }

    public InputFrame Think(double time, double delta, GameClient client)
    {
        var input = new InputFrame { Yaw = _yaw };
        LocalPlayer me = client.Local;
        if (!_subscribed)
        {
            _subscribed = true;
            me.IntentAnswered += a =>
            {
                if (a.Seq == _waiting)
                {
                    _answered = true;
                    if (a.Done)
                    {
                        Done++;
                    }
                    else
                    {
                        Refused++;
                    }
                }
            };
        }
        if (me.IsDead || client.Building == null)
        {
            return input;
        }
        Vector3 feet = client.Character.Position;
        // The bot stands south of its base's middle, 2 m from its south walls, within reach of everything.
        Vector3 stand = _site + new Vector3(1.5f, 0, -3.5f);
        switch (_stage)
        {
            case Stage.WaitForKit:
                if (me.Carries(BuildingRole.Plan) && me.Carries(BuildingRole.Hammer) && me.Inventory.Count(client.Content.ItemByKey("wood")) > 0)
                {
                    _site = ClearSite(client, _site);
                    _stage = Stage.Walk;
                }
                break;
            case Stage.Walk:
                if (Steer(ref input, feet, stand, 0.6f, delta))
                {
                    _stage = Stage.Foundation;
                }
                else if (Stuck(time, feet, stand))
                {
                    // Stuck behind a tree or a rock: the base goes near where the bot stands.
                    _site = feet - new Vector3(1.5f, 0, -3.5f);
                    _stage = Stage.Foundation;
                }
                break;
            case Stage.Foundation:
                if (time < _nextTask || !Answered(time))
                {
                    break;
                }
                _f1 ??= client.Building.AllPieces.FirstOrDefault(p => p.Def.Family == PieceFamily.Foundation && Flat(p.ZoneTransform.Position - _site) < 1.5f);
                if (_f1 != null)
                {
                    // Where the bot stands to build the rest: south of the base's middle.
                    _site = _f1.ZoneTransform.Position;
                    _stage = Stage.Approach;
                    break;
                }
                if (_waiting != 0 && _answered && time - _sentAt < 1.5)
                {
                    break; // placed: wait for it to arrive
                }
                if (_tries >= 6)
                {
                    _stage = Stage.GaveUp;
                    break;
                }
                _tries++;
                // Judged from here, on the ground around: the first spot near the plot, or near where the bot stands,
                // where the rules take a foundation now.
                PlacementPlan? plan = null;
                foreach (Vector3 spot in ClearSpots(client, _site, 2).Concat(ClearSpots(client, feet - new Vector3(1.5f, 0, -3.5f), 2)))
                {
                    if (SiteWorks(client, spot, out PlacementPlan? works))
                    {
                        _site = spot;
                        plan = works;
                        break;
                    }
                }
                if (plan == null)
                {
                    Log?.Invoke($"{client.Name}: no spot for the base near {_site}");
                    _site = ClearSite(client, _site + new Vector3(0, 0, 10));
                    _stage = Stage.Walk;
                    _sentAt = time;
                    break;
                }
                Send(PlacePiece(client, plan), time);
                break;
            case Stage.Approach:
                if (Steer(ref input, feet, stand, 0.8f, delta) || Stuck(time, feet, stand))
                {
                    _stage = Stage.Build;
                    Plan(client);
                }
                break;
            case Stage.Build:
                if (time < _nextTask || !Answered(time))
                {
                    break;
                }
                if (_tasks.Count == 0)
                {
                    _stage = Stage.Done;
                    _nextToggle = time + _rng.Range(3, 8);
                    break;
                }
                uint seq = _tasks.Peek()(client);
                _nextTask = time + 0.25;
                if (seq == uint.MaxValue && time - _taskSince < 3)
                {
                    break; // what it stands on hasn't arrived yet (or was refused: then, after a while, it's skipped)
                }
                _tasks.Dequeue();
                _taskSince = time;
                if (seq != 0 && seq != uint.MaxValue)
                {
                    Send(seq, time);
                }
                break;
            case Stage.Done:
                if (time >= _nextToggle && _doorway?.Door is { Gone: false } door)
                {
                    _nextToggle = time + _rng.Range(4, 10);
                    me.ToggleDoor(door);
                    Toggles++;
                }
                break;
        }
        if (_stage != Stage.Walk)
        {
            input.Forward = 0;
        }
        _yaw = input.Yaw;
        return input;
    }

    private void Send(uint seq, double time)
    {
        _waiting = seq;
        _answered = seq == 0;
        _sentAt = time;
    }

    // The last intent was answered (or given up on after five seconds).
    private bool Answered(double time) => _waiting == 0 || _answered || time - _sentAt > 5;

    private static float Flat(Vector3 v) => new Vector2(v.X, v.Z).Length();

    // No closer to where it walks for four seconds.
    private bool Stuck(double time, Vector3 feet, Vector3 to)
    {
        float left = Flat(to - feet);
        if (_walkFrom != to)
        {
            _walkFrom = to;
            _closest = left;
            _progressAt = time;
        }
        if (left < _closest - 0.5f)
        {
            _closest = left;
            _progressAt = time;
        }
        return time - _progressAt > 4;
    }

    private static Vector3 Eye(GameClient client) => client.Character.Position + client.Frame.UpAt(client.Character.Position) * client.EyeHeight;

    // Spots near a point, nearest first, with no tree or outcrop where the base and the bot's standing place go.
    private static IEnumerable<Vector3> ClearSpots(GameClient client, Vector3 around, int rings)
    {
        if (client.Nature is not { } nature || client.Natural is not { } state)
        {
            yield return around;
            yield break;
        }
        var found = new List<NaturalObject>();
        for (int ring = 0; ring < rings; ring++)
        {
            for (int k = 0; k < (ring == 0 ? 1 : 8); k++)
            {
                double angle = k * Math.PI / 4;
                Vector3 site = around + new Vector3((float)(ring * 4 * Math.Cos(angle)), 0, (float)(ring * 4 * Math.Sin(angle)));
                found.Clear();
                nature.Query(site.X - 4, site.Z - 6, site.X + 7, site.Z + 4, found);
                if (!found.Any(o => client.Content.Natural(o.Kind) is { IsSolid: true } && !state.IsDepleted(o.Id)))
                {
                    yield return site;
                }
            }
        }
    }

    private static Vector3 ClearSite(GameClient client, Vector3 plot) => ClearSpots(client, plot, 6).DefaultIfEmpty(plot).First();

    // Whether the rules would take a foundation on the ground here, placed from where the bot stands now.
    private static bool SiteWorks(GameClient client, Vector3 site, out PlacementPlan? plan)
    {
        BuildingWorld building = client.Building!;
        // On the plot's ground, looked for from above the bot's feet (the plot's own height is unknown), so the
        // foundation's top is the lowest the ground allows: a point above the ground lifts it, as a raised aim does.
        site.Y = client.Character.Position.Y;
        Vector3 up = client.Frame.UpAt(site);
        site = client.Ground?.Settle(site) ?? site;
        PieceDef foundation = client.Content.Building.Piece("foundation")!;
        plan = building.FoundationAt(foundation, site, 0);
        // The server's own verdict on it (the placement pipeline, GROUND_SYSTEMS.md §6).
        return plan != null && client.Content.Placement.Of(foundation) is { } placeable && client.Settle(placeable, plan.Claim, out _) != null;
    }

    // Places a piece the building rules planned, through the placement intent, paid from the plan's slot.
    private static uint PlacePiece(GameClient client, PlacementPlan plan) =>
        client.Content.Placement.Of(plan.Piece) is { } placeable && client.Settle(placeable, plan.Claim, out _) is { } ghost
            ? client.Local.Place(ghost, ContainerRef.Main, SlotOf(client, "building-plan"))
            : 0;

    // Places what an item places where a claim says (a door in its doorway, a lock on its door, the claim device on its
    // foundation), the server's verdict checked first; logs a refusal.
    private uint PlaceItem(GameClient client, string item, in PlacementClaim claim)
    {
        if (client.Content.ItemByKey(item).Places is not { } placeable)
        {
            return 0;
        }
        if (client.Settle(placeable, claim, out Outcome refused) is not { } ghost)
        {
            Refused++;
            Log?.Invoke($"{client.Name}: {item} refused: {refused}");
            return 0;
        }
        return client.Local.Place(ghost, ContainerRef.Main, SlotOf(client, item));
    }

    // The rest of the base, as steps that wait for what they stand on (uint.MaxValue: not yet).
    private void Plan(GameClient client)
    {
        BuildingData data = client.Content.Building;
        const uint NotYet = uint.MaxValue;
        var walls = new List<Piece>();
        var shared = new List<Vector3D>();
        // The first allowed placement among some candidates, sent; 0 when none is.
        uint PlaceFirst(GameClient c, IEnumerable<PlacementPlan?> candidates)
        {
            PlacementPlan? refused = null;
            Outcome why = Outcome.Done;
            foreach (PlacementPlan? plan in candidates)
            {
                if (plan == null || c.Content.Placement.Of(plan.Piece) is not { } placeable)
                {
                    continue;
                }
                // The server's own verdict (the placement pipeline, GROUND_SYSTEMS.md §6), then the intent.
                if (c.Settle(placeable, plan.Claim, out Outcome refusal) is { } ghost)
                {
                    return c.Local.Place(ghost, ContainerRef.Main, SlotOf(c, "building-plan"));
                }
                if (refused == null)
                {
                    (refused, why) = (plan, refusal);
                }
            }
            if (refused != null)
            {
                Refused++;
                Log?.Invoke($"{c.Name}: {refused.Piece.Key}: {why}");
            }
            return 0;
        }
        IEnumerable<PlacementPlan?> Orientations(GameClient c, Piece on, SocketId socket, string key)
        {
            PieceDef def = data.Piece(key)!;
            int count = Math.Max(1, PieceGeometry.Orientations(on.Def, socket, def));
            for (int o = 0; o < count; o++)
            {
                yield return c.Building!.FromIntent(def, on, socket, o);
            }
        }
        Piece? Found(GameClient c, Func<Piece, bool> what) => c.Building!.AllPieces.FirstOrDefault(p => p.Structure == _f1!.Structure && what(p));
        static bool Near(Pose a, Vector3D at) => Math.Abs(a.Position.X - at.X) < 0.2 && Math.Abs(a.Position.Z - at.Z) < 0.2;

        // The second foundation beside the first: east, west or north (never south, where the bot stands), level or
        // half a storey up where the ground rises.
        _tasks.Enqueue(c =>
        {
            var candidates = new List<PlacementPlan?>();
            foreach ((double x, double z) in new[] { (1.0, 0.0), (-1.0, 0.0), (0.0, 1.0) })
            {
                SocketId edge = Facing(_f1!, x, z);
                candidates.AddRange(Orientations(c, _f1!, edge, "foundation"));
                candidates.AddRange(Orientations(c, _f1!, new SocketId(SocketKind.EdgeUp, edge.Edge), "foundation"));
            }
            return PlaceFirst(c, candidates);
        });
        _tasks.Enqueue(c =>
        {
            _f2 = Found(c, p => p.Def.Family == PieceFamily.Foundation && p != _f1);
            if (_f2 == null)
            {
                return NotYet;
            }
            // The edge the two share takes no wall.
            shared.Add((_f1!.Pose.Position + _f2.Pose.Position) / 2);
            return 0;
        });
        // Walls round each foundation but on the edge they share, the far sides first and the near (south) side last,
        // so nothing built stands between the bot and the next; the floors go on before the south side closes. The
        // first foundation's south wall is the doorway.
        void Wall(double x, double z)
        {
            foreach (Func<Piece?> slab in new Func<Piece?>[] { () => _f1, () => _f2 })
            {
                _tasks.Enqueue(c =>
                {
                    if (slab() is not { } s)
                    {
                        return 0;
                    }
                    int edge = Facing(s, x, z).Edge;
                    PlacementPlan? wall = c.Building!.FromIntent(data.Piece("wall")!, s, new SocketId(SocketKind.Edge, edge), 0);
                    if (wall == null || shared.Any(m => Math.Abs(m.X - wall.Pose.Position.X) < 0.3 && Math.Abs(m.Z - wall.Pose.Position.Z) < 0.3) ||
                        Found(c, p => p.Def.Family == PieceFamily.Wall && Near(p.Pose, wall.Pose.Position) && Math.Abs(p.Pose.Position.Y - wall.Pose.Position.Y) < 0.5) != null)
                    {
                        return 0;
                    }
                    bool door = s == _f1 && z < 0;
                    return PlaceFirst(c, Orientations(c, s, new SocketId(SocketKind.Edge, edge), door ? "doorway" : "wall"));
                });
            }
        }
        Wall(0, 1);
        Wall(-1, 0);
        Wall(1, 0);
        // A floor over each foundation, on one of its walls.
        foreach (Func<Piece?> slab in new Func<Piece?>[] { () => _f1, () => _f2 })
        {
            _tasks.Enqueue(c =>
            {
                if (slab() is not { } s)
                {
                    return 0;
                }
                var candidates = new List<PlacementPlan?>();
                foreach (Piece wall in c.Building!.AllPieces.Where(p => p.Structure == s.Structure && p.Def.Family == PieceFamily.Wall &&
                    Math.Abs(p.Pose.Position.Y - s.Pose.Position.Y) < 0.5 && (p.Pose.Position - s.Pose.Position).LengthSquared < 2.5 * 2.5))
                {
                    candidates.AddRange(Orientations(c, wall, new SocketId(SocketKind.Top, 0), "floor").Where(p => p != null && Near(p.Pose, s.Pose.Position)));
                }
                return candidates.Count == 0 ? NotYet : PlaceFirst(c, candidates);
            });
        }
        Wall(0, -1);
        _tasks.Enqueue(c =>
        {
            _doorway = Found(c, p => p.Def.Key == "doorway");
            return _doorway != null ? 0 : NotYet;
        });
        // The door, its lock and code.
        _tasks.Enqueue(c => c.Local.Inventory.Count(c.Content.ItemByKey("wooden-door")) > 0 && _doorway != null
            ? PlaceItem(c, "wooden-door", new PlacementClaim(_doorway.Ref, 0, _doorway.ZoneTransform.Position, 0, 0)) : 0);
        _tasks.Enqueue(c => _doorway == null ? 0 : _doorway.Door is { } door
            ? c.Local.Inventory.Count(c.Content.ItemByKey("code-lock")) > 0
                ? PlaceItem(c, "code-lock", new PlacementClaim(door.Ref with { Part = 1 }, 0, door.LockSpot.Position, 0, 0))
                : 0
            : NotYet);
        _tasks.Enqueue(c => _doorway == null ? 0 : _doorway.Door is { Lock: CodeLock } door ? c.Local.SetCode(door, "1357") : NotYet);
        // Upgrades: the foundations to stone, the walls to wood.
        _tasks.Enqueue(c =>
        {
            walls.Clear();
            walls.AddRange(c.Building!.AllPieces.Where(p => p.Structure == _f1!.Structure && p.Def.Key == "wall"));
            return _f1 != null ? c.Local.Upgrade(_f1, 2) : 0;
        });
        _tasks.Enqueue(c => _f2 != null ? c.Local.Upgrade(_f2, 2) : 0);
        for (int i = 0; i < 6; i++)
        {
            int n = i;
            _tasks.Enqueue(c => n < walls.Count ? c.Local.Upgrade(walls[n], 1) : 0);
        }
        // Last, the claim device in the middle of the first foundation.
        _tasks.Enqueue(c => c.Local.Inventory.Count(c.Content.ItemByKey("claim-device")) > 0 && _f1 != null
            ? PlaceItem(c, "claim-device", new PlacementClaim(_f1.Ref, 0,
                Zone(_f1.Structure.Frame.ToZone(new Vector3D(_f1.Pose.Position.X - 0.4, _f1.Pose.Position.Y, _f1.Pose.Position.Z + 0.4))), 0,
                _f1.Pose.Yaw))
            : 0);
    }

    private static Vector3 Zone(Vector3D v) => new((float)v.X, (float)v.Y, (float)v.Z);

    private static int SlotOf(GameClient client, string key)
    {
        ushort id = client.Content.ItemByKey(key).Id;
        Container main = client.Local.Inventory.Main;
        for (int i = 0; i < main.Size; i++)
        {
            if (main[i].Item == id)
            {
                return i;
            }
        }
        return 0;
    }

    /// <summary>The edge of a slab whose middle points furthest along a frame direction.</summary>
    private static SocketId Facing(Piece slab, double x, double z)
    {
        int best = 0;
        double most = double.MinValue;
        for (int e = 0; e < slab.Def.EdgeCount; e++)
        {
            Vector3D middle = PieceGeometry.Rotate(PieceGeometry.Edge(slab.Def.Shape, e, 3).Middle, slab.Pose.Yaw);
            double along = middle.X * x + middle.Z * z;
            if (along > most)
            {
                most = along;
                best = e;
            }
        }
        return new SocketId(SocketKind.Edge, best);
    }

    // Walks toward a point until within reach of it; true once there.
    private bool Steer(ref InputFrame input, Vector3 feet, Vector3 to, float reach, double delta)
    {
        var flat = new Vector2(to.X - feet.X, to.Z - feet.Z);
        if (flat.Length() <= reach)
        {
            return true;
        }
        float wanted = MathF.Atan2(-flat.X, -flat.Y);
        float turn = wanted - _yaw;
        while (turn > MathF.PI)
        {
            turn -= 2 * MathF.PI;
        }
        while (turn < -MathF.PI)
        {
            turn += 2 * MathF.PI;
        }
        input.Yaw = _yaw + Math.Clamp(turn, -(float)delta * 5, (float)delta * 5);
        input.Forward = MathF.Abs(turn) < 1.2f ? Math.Min(1, flat.Length()) : 0;
        input.Sprint = flat.Length() > 12;
        return false;
    }
}
