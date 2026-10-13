using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hollow.Client;
using Hollow.Core;
using Hollow.Simulation.Building;
using Hollow.Simulation.Content;
using Hollow.Simulation.Items;

namespace Hollow.Bots;

/// <summary>
/// A bot that builds a small base (GROUND.md §5.3) through the client's building API, as a player would: it walks to a
/// plot of its own (a 10 × 10 grid of plots 31 m apart round the zone's origin), places a foundation on the ground and
/// a second beside it, walls round them with a doorway, two floors on top, fits a wooden door with a code lock and sets
/// the code, upgrades the foundations to stone and the walls to wood, and places its claim device; then it opens and
/// shuts its door now and then. Each step is checked with the same rules first (the ghost), sent as an intent and
/// waited for; a refused one is counted and skipped. With the <c>build-kit</c> loot table it has all it needs.
/// </summary>
public sealed class BuildBrain
{
    /// <summary>
    /// Plots are this far apart: a claim device covers 16 m round every piece and 25 m round itself, so a neighbour
    /// nearer than about 30 m couldn't finish.
    /// </summary>
    public const float PlotSpacing = 31f;

    private enum Stage
    {
        WaitForKit,
        Walk,
        Foundation,
        Build,
        Done,
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

    /// <summary>Doors opened or shut once the base stands.</summary>
    public int Toggles { get; private set; }

    public InputFrame Think(double time, double delta, GameClient client)
    {
        var input = new InputFrame { Yaw = _yaw };
        LocalPlayer me = client.Local;
        if (!_subscribed)
        {
            _subscribed = true;
            me.BuildAnswered += a =>
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
                    _stage = Stage.Walk;
                }
                break;
            case Stage.Walk:
                if (Steer(ref input, feet, stand, 0.6f, delta) || time - _sentAt > 40)
                {
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
                    _stage = Stage.Build;
                    Plan(client);
                    break;
                }
                if (_waiting != 0 && _answered && time - _sentAt < 1.5)
                {
                    break; // placed: wait for it to arrive
                }
                if (_tries >= 4)
                {
                    _stage = Stage.Done;
                    break;
                }
                _tries++;
                Vector3 eye = Eye(client);
                PieceDef foundation = client.Content.Building.Piece("foundation")!;
                PlacementPlan? plan = client.Building.FoundationAt(foundation, _site, 0);
                if (plan != null)
                {
                    client.Building.Check(plan, client.EntityId.Value, eye, new ItemWallet(client.Content, me.Inventory.Carried));
                }
                if (plan is not { Valid: true })
                {
                    Log?.Invoke($"{client.Name}: first foundation {(plan?.ToString() ?? "has no ground")}");
                    // A tree or someone in the way: the plot moves a little.
                    _site += new Vector3(0, 0, 4);
                    _stage = Stage.Walk;
                    _sentAt = time;
                    Refused++;
                    break;
                }
                Send(me.PlacePiece(plan), time);
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

    private static Vector3 Eye(GameClient client) => client.Character.Position + client.Frame.UpAt(client.Character.Position) * client.EyeHeight;

    // The rest of the base, as steps that wait for what they stand on (uint.MaxValue: not yet).
    private void Plan(GameClient client)
    {
        BuildingData data = client.Content.Building;
        const uint NotYet = uint.MaxValue;
        var walls = new List<Piece>();
        uint Place(GameClient c, Func<Piece?> support, SocketId socket, string key, Func<PlacementPlan, bool>? pick = null)
        {
            if (support() is not { Gone: false } on)
            {
                return NotYet;
            }
            PieceDef def = data.Piece(key)!;
            PlacementPlan? plan = null;
            int orientations = PieceGeometry.Orientations(on.Def, socket, def);
            for (int o = 0; o < Math.Max(1, orientations) && plan == null; o++)
            {
                PlacementPlan? candidate = c.Building!.FromIntent(def, on, socket, o);
                if (candidate != null && (pick == null || pick(candidate)))
                {
                    plan = candidate;
                }
            }
            if (plan == null)
            {
                return 0;
            }
            c.Building!.Check(plan, c.EntityId.Value, Eye(c), new ItemWallet(c.Content, c.Local.Inventory.Carried));
            if (!plan.Valid)
            {
                Refused++;
                Log?.Invoke($"{c.Name}: {plan}");
                return 0;
            }
            return c.Local.PlacePiece(plan);
        }
        Piece? Found(GameClient c, Func<Piece, bool> what) => c.Building!.AllPieces.FirstOrDefault(p => p.Structure == _f1!.Structure && what(p));
        static bool Near(Pose a, Vector3D at) => Math.Abs(a.Position.X - at.X) < 0.2 && Math.Abs(a.Position.Z - at.Z) < 0.2;

        _tasks.Enqueue(c => Place(c, () => _f1, Facing(_f1!, 1, 0), "foundation"));
        _tasks.Enqueue(c =>
        {
            _f2 = Found(c, p => p.Def.Family == PieceFamily.Foundation && p != _f1);
            return _f2 != null ? 0 : NotYet;
        });
        // Walls round the two: the doorway on the first's south side, facing the bot.
        (Func<Piece?> On, double X, double Z, string Key)[] sides =
        {
            (() => _f1, -1, 0, "wall"), (() => _f1, 0, 1, "wall"), (() => _f1, 0, -1, "doorway"),
            (() => _f2, 1, 0, "wall"), (() => _f2, 0, 1, "wall"), (() => _f2, 0, -1, "wall"),
        };
        foreach ((Func<Piece?> on, double x, double z, string key) in sides)
        {
            _tasks.Enqueue(c => on() is { } slab ? Place(c, on, Facing(slab, x, z), key) : NotYet);
        }
        _tasks.Enqueue(c =>
        {
            _doorway = Found(c, p => p.Def.Key == "doorway");
            walls.Clear();
            walls.AddRange(c.Building!.AllPieces.Where(p => p.Structure == _f1!.Structure && p.Def.Key == "wall"));
            return _doorway != null ? 0 : NotYet;
        });
        // A floor over each foundation, on its north wall.
        foreach (Func<Piece?> slab in new Func<Piece?>[] { () => _f1, () => _f2 })
        {
            _tasks.Enqueue(c =>
            {
                Piece? s = slab();
                Piece? north = s == null ? null : walls.FirstOrDefault(w => Near(w.Pose, s.Pose.Position + new Vector3D(0, 0, 1.5)));
                return north == null ? 0 : Place(c, () => north, new SocketId(SocketKind.Top, 0), "floor", p => Near(p.Pose, s!.Pose.Position));
            });
        }
        // The door, its lock and code.
        _tasks.Enqueue(c => c.Local.Inventory.Count(c.Content.ItemByKey("wooden-door")) > 0 && _doorway != null
            ? c.Local.PlaceDoor(ContainerRef.Main, SlotOf(c, "wooden-door"), _doorway) : 0);
        _tasks.Enqueue(c => _doorway?.Door is { } door
            ? c.Local.Inventory.Count(c.Content.ItemByKey("code-lock")) > 0 ? c.Local.AttachLock(ContainerRef.Main, SlotOf(c, "code-lock"), door) : 0
            : NotYet);
        _tasks.Enqueue(c => _doorway?.Door is { Lock: CodeLock } door ? c.Local.SetCode(door, "1357") : NotYet);
        // Upgrades: the foundations to stone, the walls to wood.
        _tasks.Enqueue(c => _f1 != null ? c.Local.Upgrade(_f1, 2) : 0);
        _tasks.Enqueue(c => _f2 != null ? c.Local.Upgrade(_f2, 2) : 0);
        for (int i = 0; i < 5; i++)
        {
            int n = i;
            _tasks.Enqueue(c => n < walls.Count ? c.Local.Upgrade(walls[n], 1) : 0);
        }
        // Last, the claim device in the middle of the first foundation.
        _tasks.Enqueue(c => c.Local.Inventory.Count(c.Content.ItemByKey("claim-device")) > 0 && _f1 != null
            ? c.Local.PlaceClaim(ContainerRef.Main, SlotOf(c, "claim-device"), _f1, _f1.Pose.Position.X - 0.4, _f1.Pose.Position.Z + 0.4) : 0);
    }

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
