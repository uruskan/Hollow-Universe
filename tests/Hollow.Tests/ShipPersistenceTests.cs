using System;
using System.Linq;
using System.Numerics;
using Hollow.Client;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Ships;

namespace Hollow.Tests;

/// <summary>Ships in storage (GROUND.md §4.7, §6.7).</summary>
public static class ShipPersistenceTests
{
    private static string Fingerprint(Ship s) => string.Create(System.Globalization.CultureInfo.InvariantCulture,
        $"{s.Def.Key} owner {s.Owner} locked {s.Locked} hull {s.State.Health:0.000} fuel {s.State.Fuel:0.000} hatch {s.HatchOpen} " +
        $"cargo {string.Join(",", Enumerable.Range(0, s.Cargo.Size).Select(i => s.Cargo[i].IsEmpty ? "-" : $"{s.Cargo[i].Item}x{s.Cargo[i].Count}"))}");

    /// <summary>
    /// A parked ship with its pilot in the seat survives a restart: where it was on the planet, its turn, hull, fuel, lock,
    /// owner, cargo (a loaded rifle among it); the pilot comes back asleep in the seat, wakes there on logging in, and
    /// their client flies it again. A destroyed ship's row goes.
    /// </summary>
    [Test]
    public static void AShipAndItsPilotSurviveARestart()
    {
        using var file = new TempDatabase();
        GameContent c = ContentFixture.Content;
        EntityId shipId;
        string before;
        Vector3 position;
        Quaternion rotation;
        EntityId adaId;
        using (var rig = new LoopbackRig(file.Path))
        {
            GameClient ada = rig.Join("Ada");
            rig.WaitUntilPlaying();
            adaId = ada.EntityId;
            rig.Server.Execute("ship Ada");
            rig.Run(0.7, (_, _) => default);
            RemoteShip seen = ada.Ships().All.Single();
            Vector3 hatch = seen.PointOf(seen.Def!.Hatch);
            Vector3 at = hatch + Vector3.Normalize(Vector3.Transform(-Vector3.UnitX, seen.Rotation)) * 1.6f;
            rig.Server.Execute(FormattableString.Invariant($"teleport Ada {at.X} {at.Z}"));
            rig.Run(0.5, (_, _) => default);
            ada.Local.Interact(new Hollow.Simulation.Targets.TargetRef(Hollow.Simulation.Targets.TargetFamily.Ship, seen.Id.Value, ShipParts.Hatch), "board");
            rig.Run(0.7, (_, _) => default);
            Assert.True(ada.Ships().Piloting, "Ada is in the seat");
            (shipId, before, position, rotation) = rig.Server.Inspect(sv =>
            {
                Ship ship = sv.Zone.Ships().All.Single();
                ItemStack rifle = ItemStack.Of(c.ItemByKey("rifle"), 1);
                rifle.Loaded = 17;
                rifle.LoadedAmmo = c.ItemByKey("rifle-ammo").Id;
                ship.Cargo.Set(0, rifle);
                ship.Cargo.Set(3, ItemStack.Of(c.ItemByKey("metal-fragments"), 777));
                ship.Locked = true;
                ship.State.Fuel = 321.5f;
                sv.Zone.Ships().DamageShip(ship, 300, ShipDamage.Bullet, null);
                return (ship.Id, Fingerprint(ship), ship.State.Position, ship.State.Rotation);
            });
            rig.Run(0.2, (_, _) => default);
            rig.Server.Inspect(sv => sv.SaveAll()).Wait(TimeSpan.FromSeconds(10));
        }

        using (var rig = new LoopbackRig(file.Path))
        {
            (string after, Vector3 p, Quaternion q, bool seated, int ships) = rig.Server.Inspect(sv =>
            {
                Ship ship = sv.Zone.Ships().ShipOf(shipId)!;
                bool inSeat = sv.Zone.Entities.TryFind(adaId, out Entity pilot) && sv.Zone.Ships().SeatOf(pilot) == ship && ship.Pilot == adaId;
                return (Fingerprint(ship), ship.State.Position, ship.State.Rotation, inSeat, sv.Zone.Ships().All.Count);
            });
            Fixtures.Report($"before: {before}");
            Fixtures.Report($"after:  {after}");
            Fixtures.Report(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"moved {Vector3.Distance(p, position) * 1000:0.000} mm, turned {MathF.Acos(Math.Clamp(MathF.Abs(Quaternion.Dot(q, rotation)), 0, 1)) * 2 * 180 / MathF.PI:0.0000}°"));
            Assert.Equal(1, ships, "one ship");
            Assert.Equal(before, after, "the ship comes back as it was");
            Assert.True(Vector3.Distance(p, position) < 0.002f, "where it was");
            Assert.True(MathF.Abs(Quaternion.Dot(q, rotation)) > 0.99999f, "turned as it was");
            Assert.True(seated, "Ada asleep in its seat");
            Assert.True(rig.Server.Inspect(sv => sv.Zone.Ships().ShipOf(shipId)!.Cargo[0].Loaded) == 17, "the rifle in the cargo keeps its magazine");

            GameClient ada = rig.Join("Ada");
            rig.WaitUntilPlaying();
            rig.Run(0.5, (_, _) => default);
            Assert.True(ada.Ships().Piloting, "Ada wakes in the seat and flies it again");

            // A wreck's row goes.
            rig.Server.Inspect(sv => sv.Zone.Ships().DamageShip(sv.Zone.Ships().ShipOf(shipId)!, 10_000, ShipDamage.Explosive, null));
            rig.Run(3.0, (_, _) => default);
            rig.Server.Inspect(sv => sv.SaveAll()).Wait(TimeSpan.FromSeconds(10));
        }
        using (var rig = new LoopbackRig(file.Path))
        {
            Assert.Equal(0, rig.Server.Inspect(sv => sv.Zone.Ships().All.Count), "the wrecked ship is gone from storage");
        }
    }
}
