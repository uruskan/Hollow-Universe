using System;
using System.Linq;
using System.Numerics;
using Hollow.Client;
using Hollow.Core;
using Hollow.Net.Protocol;
using Hollow.Simulation.Ships;

namespace Hollow.Bots;

/// <summary>
/// A bot that flies (G6, GROUND.md §6.7): it walks to the hatch of the ship the server gave it (<c>ShipOnJoin</c>), boards
/// it, lifts off and flies loops a few hundred metres round, climbing and sinking between 50 and 200 m over the ground,
/// boosting now and then, and steering home (where it arrived and boarded) when it strays. Deterministic per bot (seeded
/// by its number).
/// </summary>
public sealed class FlyBrain
{
    private const float HomeRadius = 900f;

    private readonly int _index;
    private Rng _rng;
    private double _nextIntent;
    private double _boardedAt = -1;
    private double _nextBoost;
    private float _turn;
    private Vector3 _home;

    public FlyBrain(int index)
    {
        _index = index;
        _rng = new Rng(Hash64.Of((ulong)index, 0xF1E5));
        _turn = (index % 2 == 0 ? 1 : -1) * (float)_rng.Range(0.25, 0.55);
        _nextBoost = _rng.Range(10, 30);
    }

    /// <summary>Times it climbed into its seat, and how long it has been flying.</summary>
    public int Boardings { get; private set; }

    public double Airborne { get; private set; }

    public InputFrame Think(double time, double delta, GameClient client)
    {
        if (!client.Ships().Piloting)
        {
            _boardedAt = -1;
            return Walk(time, client);
        }
        if (_boardedAt < 0)
        {
            _boardedAt = time;
            Boardings++;
            _home = BotGround.Home(client); // where it arrived: its ship was set down beside it
        }
        return Fly(time, delta, client);
    }

    // On foot: to the hatch of its own ship, open it if it must, and board.
    private InputFrame Walk(double time, GameClient client)
    {
        RemoteShip? ship = client.Ships().All.FirstOrDefault(s => s.Mine && s.Def != null && !s.Has(ShipNetFlags.Destroyed));
        if (ship == null || client.Local.IsDead)
        {
            if (client.Local.IsDead && time >= _nextIntent)
            {
                _nextIntent = time + 2;
                client.Local.Respawn();
            }
            return default;
        }
        Vector3 feet = client.Character.Position;
        Vector3 up = client.Frame.UpAt(feet);
        Vector3 hatch = ship.PointOf(ship.Def!.Hatch);
        Vector3 outward = Vector3.Normalize(Vector3.Transform(-Vector3.UnitX, ship.Rotation));
        Vector3 nose = Vector3.Normalize(Vector3.Transform(Vector3.UnitZ, ship.Rotation));
        Vector3 spot = hatch + outward * 1.4f;
        // On the far side of the hull: round its nose first.
        Vector3 fromShip = feet - ship.Position;
        if (Vector3.Dot(fromShip, outward) < 0.5f)
        {
            spot = ship.Position + nose * 4.5f + outward * (Vector3.Dot(fromShip, outward) < -0.5f ? 0 : 2.5f);
        }
        Vector3 to = spot - feet;
        Vector3 flat = to - up * Vector3.Dot(to, up);
        var input = new InputFrame();
        if (flat.Length() > 0.6f)
        {
            input.Yaw = BotGround.YawTo(feet, spot);
            input.Forward = 1;
            return input;
        }
        input.Yaw = BotGround.YawTo(feet + up * 1.6f, hatch);
        if (time >= _nextIntent)
        {
            _nextIntent = time + 1.5;
            // A tap of E at the hatch: opened when shut, boarded when open.
            if (!ship.Has(ShipNetFlags.HatchOpen) || ship.Hatch > 0.6f)
            {
                Interacting.Tap(client, new Hollow.Simulation.Targets.TargetRef(Hollow.Simulation.Targets.TargetFamily.Ship, ship.Id.Value,
                    Hollow.Simulation.Ships.ShipParts.Hatch));
            }
        }
        return input;
    }

    // In the seat: up, then loops.
    private InputFrame Fly(double time, double delta, GameClient client)
    {
        ShipState s = client.Ships().OwnShip;
        double flying = time - _boardedAt;
        Airborne += delta;
        var ship = new ShipInput();
        float height = Height(client, s.Position);
        if (flying < 5)
        {
            ship.Vertical = 1;
            return new InputFrame { Ship = ship };
        }
        Vector3 up = client.Frame.UpAt(s.Position);
        Vector3 forward = s.Forward;
        ship.Throttle = 0.45f + 0.15f * (float)Math.Sin(flying / 7 + _index);
        ship.Yaw = _turn;
        // Home if strayed: turn toward where it arrived.
        if (Vector3.Distance(s.Position, _home) > HomeRadius)
        {
            Vector3 home = _home - s.Position;
            Vector3 flatHome = Vector3.Normalize(home - up * Vector3.Dot(home, up));
            Vector3 flatForward = forward - up * Vector3.Dot(forward, up);
            float side = Vector3.Dot(Vector3.Cross(flatForward, flatHome), up);
            ship.Yaw = side > 0 ? -0.8f : 0.8f; // + yaw is to the right
        }
        // Between 50 and 200 m over the ground: climb, sink; and level the nose.
        ship.Vertical = height < 50 ? 0.8f : height > 200 ? -0.6f : (float)Math.Sin(flying / 11 + _index) * 0.3f;
        float pitch = MathF.Asin(Math.Clamp(Vector3.Dot(forward, up), -1, 1));
        ship.Pitch = Math.Clamp(-pitch * 2, -0.5f, 0.5f);
        Vector3 right = s.Right;
        float roll = MathF.Asin(Math.Clamp(Vector3.Dot(right, up), -1, 1));
        ship.Roll = Math.Clamp(roll * 2, -0.5f, 0.5f);
        if (flying >= _nextBoost)
        {
            _nextBoost = flying + _rng.Range(15, 40);
            ship.Buttons |= ShipButtons.Boost;
        }
        return new InputFrame { Ship = ship };
    }

    private static float Height(GameClient client, Vector3 point)
    {
        if (client.Surface == null)
        {
            return 0;
        }
        Vector3D body = client.Frame.ToBody(point);
        double r = body.Length;
        return (float)(r - (client.Surface.Radius + client.Surface.HeightAt(body / r)));
    }
}
