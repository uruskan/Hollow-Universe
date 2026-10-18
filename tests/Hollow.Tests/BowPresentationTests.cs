using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Hollow.Client;
using Hollow.Simulation;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;

namespace Hollow.Tests;

public static class BowPresentationTests
{
    [Test]
    public static void DrawAndAcceptedReleaseReachTheClients()
    {
        using var rig = new LoopbackRig();
        GameClient archer = rig.Join("Archer");
        GameClient observer = rig.Join("Observer");
        rig.WaitUntilPlaying();
        rig.Server.Execute("teleport Archer 0 0");
        rig.Server.Execute("teleport Observer 3 0");
        rig.Server.Execute("give Archer bow 1");
        rig.Server.Execute("give Archer arrow 4");
        void Until(Func<bool> done, string label, Func<GameClient, double, InputFrame>? input = null)
        {
            var watch = Stopwatch.StartNew();
            while (!done())
            {
                Assert.True(watch.Elapsed.TotalSeconds < 8, "waiting for " + label);
                rig.Run(0.05, input ?? ((_, _) => default));
            }
        }
        ushort bow = archer.Content.ItemByKey("bow").Id;
        Until(() => archer.Local.Inventory.Count(archer.Content.ItemByKey("arrow")) == 4 && archer.Local.Inventory.Count(archer.Content.ItemByKey("bow")) == 1,
            "bow and arrows");
        int slot = Enumerable.Range(0, 24).First(i => archer.Local.Inventory.Main[i].Item == bow);
        archer.Local.MoveItem(ContainerRef.Main, slot, ContainerRef.Belt, 2);
        archer.Local.HeldSlot = 2;
        Until(() => observer.Entities.Any(e => e.NetId == archer.NetId && e.Held == bow), "equipped bow");
        var accepted = new List<ClientProjectile>();
        archer.Survival().ProjectileLaunched += accepted.Add;
        // Use a fixed zone-space view with nonzero yaw/pitch: neither local +X nor an assumed world-forward
        // direction can accidentally satisfy the launch-direction check.
        Vector3 requestedView = CharacterMotor.ViewDirection(0.7f, 0.5f, archer.Frame.UpAt(archer.Character.Position));
        InputFrame Draw(GameClient who, double _) => who == archer
            ? new InputFrame { Secondary = true, View = requestedView, ViewFrame = 0 } : default;
        // Equipping and the first delivered draw input each take their own ticks. Wait for the real
        // draw duration, rather than starting a wall-clock timer when the remote held item appears.
        Until(() => rig.Server.Inspect(server =>
        {
            server.Zone.Players().TryFindPlayer(archer.EntityId, out _, out var player);
            return player.DrawStartTick >= 0 && server.Zone.Tick - player.DrawStartTick >= archer.Content.ItemByKey("bow").Bow!.DrawTicks;
        }), "fully drawn bow", Draw);
        Until(() => observer.Entities.Single(e => e.NetId == archer.NetId).Aiming, "replicated draw", Draw);
        Assert.True(observer.Entities.Single(e => e.NetId == archer.NetId).Aiming, "bow drawing is replicated to other characters");
        Assert.Equal(0, accepted.Count, "drawing alone does not play a release");
        Vector3 carried = rig.Server.Inspect(server =>
        {
            server.Zone.Players().TryFindPlayer(archer.EntityId, out var entity, out _);
            ref Character character = ref server.Zone.Entities.Get<Character>(entity);
            Assert.Equal(0UL, character.State.Frame, "this direction fixture remains ashore, with no changing carrier velocity");
            return server.Zone.AttackLine(character, server.Zone.Tick).Carried;
        });
        rig.Run(0.1, (who, time) => who == archer
            ? new InputFrame { Secondary = true, Primary = true, PrimaryPressed = true, View = requestedView, ViewFrame = 0 } : default);
        Until(() => accepted.Count != 0, "accepted arrow release", Draw);
        Until(() => archer.Local.Inventory.Count(archer.Content.ItemByKey("arrow")) == 3, "one consumed arrow", Draw);
        Assert.Equal(1, accepted.Count, "one accepted shot produces one release event");
        Assert.Equal(archer.NetId, accepted[0].Shooter, "the release identifies the local archer");
        Assert.Equal(archer.Content.ItemByKey("arrow").Id, accepted[0].Ammo, "the release identifies the actual arrow");
        Vector3 initialVelocity = accepted[0].Velocity - carried;
        float expectedSpeed = archer.Content.ItemByKey("arrow").Ammo!.Speed * archer.Content.ItemByKey("bow").Bow!.SpeedScale;
        Assert.Near(expectedSpeed, initialVelocity.Length(), 0.01, "the replicated launch preserves the bow's arrow speed");
        Assert.True(Vector3.Dot(Vector3.Normalize(initialVelocity), requestedView) > 0.99999f,
            $"the authoritative arrow follows the submitted camera view, independently of held art: velocity {initialVelocity}, view {requestedView}");
        Until(() => !observer.Entities.Single(e => e.NetId == archer.NetId).Aiming, "draw cancellation");
    }
}
