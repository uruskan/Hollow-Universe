using System;
using System.Threading.Tasks;
using Godot;
using Hollow.Client;
using HollowUniverse.Characters;
using HollowUniverse.UI;

namespace HollowUniverse.Ground;

public partial class GroundTour
{
    // Real mouse/key coverage on an isolated hosted save: --tour-steps bow-alignment.
    private async Task BowAlignmentTour()
    {
        await Until(() => Client.Console().IsAdmin, 10);
        RequireBow(Client.Console().IsAdmin, "the hosted fixture has admin rights for item setup");
        await RepairGod(true);
        var (spot, towards) = OpenSpot(Offset(35, 20));
        await TeleportTo(spot);
        Face(spot + towards * 12 + Client.RenderUp * Client.EyeHeight);
        await Settle();
        Admin("give", "bow 1");
        Admin("give", "arrow 8");
        await Until(() => Count("bow") > 0 && Count("arrow") >= 8, 5);
        RequireBow(Count("bow") > 0 && Count("arrow") >= 8, "the fixture receives a bow and ammunition");
        await Hold("bow", 5);
        await Until(() => Me.HeldItem?.Key == "bow" && Me.PendingCount == 0, 5);
        RequireBow(Me.HeldItem?.Key == "bow", "the actual held slot contains the bow");
        View.Interface.Open(GroundScreen.Hud);
        await RepairDeviceInput();

        FirstPersonView hands = View.GetNode<FirstPersonView>("FirstPerson");
        ViewmodelView Arms() => hands.HeldModel?.GetParent() as ViewmodelView
            ?? throw new InvalidOperationException("bow-check: no held viewmodel");
        Node3D Arrow() => hands.HeldModel?.GetNodeOrNull<Node3D>("Arrow")
            ?? throw new InvalidOperationException("bow-check: no nocked arrow model");
        int accepted = 0;
        bool releaseObserved = false;
        void Accepted(ClientProjectile projectile)
        {
            if (projectile.Shooter != Client.NetId || projectile.Ammo != Client.Content.ItemByKey("arrow").Id) return;
            accepted++;
            releaseObserved |= Arms().ActiveAction == "release";
        }

        async Task Drawn(string context)
        {
            await Until(() => Arms().ActiveAction == "draw" && Arms().ActivePhase >= 0.999f && Arrow().Visible, 5);
            RequireBow(Arms().ActiveAction == "draw" && Arms().ActivePhase >= 0.999f && Arrow().Visible,
                context + ": RMB reaches a complete draw with a visible arrow");
            // Allow the authoritative draw tick to catch up before a real attack click.
            await Seconds(0.15);
        }

        void Alignment(string context)
        {
            ViewmodelView arms = Arms();
            Camera3D camera = View.Camera ?? throw new InvalidOperationException("bow-check: no player camera");
            Transform3D arrow = camera.GlobalTransform.AffineInverse() * Arrow().GlobalTransform;
            Vector3 direction = arrow.Basis.X.Normalized();
            float angle = Mathf.RadToDeg(MathF.Acos(Math.Clamp(direction.Dot(Vector3.Forward), -1f, 1f)));
            RequireBow(direction.Z < -0.99f, context + ": the rendered arrow points forward");
            Vector3 intersection = arrow.Origin + direction * ((-10f - arrow.Origin.Z) / direction.Z);
            float miss = new Vector2(intersection.X, intersection.Y).Length();
            GD.Print($"[bow-check] {context}: forward angle={angle:0.0000} deg, centre miss at 10 m={miss:0.000000} m, " +
                $"wrist errors={arms.PrimaryHandAttachmentError:0.000000}/{arms.SecondaryHandAttachmentError:0.000000} m; {arms.PresentationState}");
            RequireBow(angle < 1f && miss < 0.02f,
                context + ": the actual arrow axis converges on screen centre after normal gameplay motion");
            RequireBow(arms.PrimaryHandShouldAttach && arms.SecondaryHandShouldAttach &&
                arms.PrimaryHandAttachmentError < 0.025f && arms.SecondaryHandAttachmentError < 0.025f,
                context + ": both wrists remain attached within 25 mm");
        }

        async Task Fire(string context)
        {
            int beforeAmmo = Count("arrow"), beforeAccepted = accepted;
            releaseObserved = false;
            BowMouse(MouseButton.Left, true);
            await Seconds(0.08);
            BowMouse(MouseButton.Left, false);
            await Until(() => accepted > beforeAccepted && Count("arrow") < beforeAmmo, 5);
            RequireBow(accepted == beforeAccepted + 1 && Count("arrow") == beforeAmmo - 1,
                context + ": one real LMB click launches one accepted arrow and consumes exactly one");
            RequireBow(releaseObserved, context + ": the accepted shot plays the release presentation");
        }

        Client.Survival().ProjectileLaunched += Accepted;
        try
        {
            await Until(() => Arms().ActiveAction == "idle", 4);
            BowMouse(MouseButton.Right, true);
            await Drawn("first draw");
            await Shot("bow-drawn", frames: 0);
            Alignment("first draw");
            await Fire("first shot");

            // RMB remains down: the server's draw restart must feed the same presentation alignment.
            await Drawn("continued-RMB redraw");
            Alignment("continued-RMB redraw");
            await Fire("second shot");
            BowMouse(MouseButton.Right, false);
            await Until(() => Arms().ActiveAction == "idle" && !Arrow().Visible, 4);
            RequireBow(Arms().ActiveAction == "idle" && !Arrow().Visible,
                "releasing RMB returns to idle and removes the nocked arrow");

            await AdmireKey(Key.N);
            RequireBow(View.Admiring, "N starts admiring the idle bow through its normal binding");
            await Seconds(0.2);
            BowMouse(MouseButton.Right, true);
            await Until(() => !View.Admiring && Arms().ActiveAction == "draw", 2);
            RequireBow(!View.Admiring && Arms().ActiveAction == "draw", "RMB cancels admiration and starts a normal draw");
            await Drawn("draw after admire");
            Alignment("draw after admire");
            await Shot("bow-after-admire", frames: 0);
            GD.Print("[bow-check] PASS complete real-input bow alignment, shooting, redraw and admire interruption tour");
        }
        finally
        {
            Client.Survival().ProjectileLaunched -= Accepted;
            BowMouse(MouseButton.Left, false);
            BowMouse(MouseButton.Right, false);
            AdmireRelease(Key.N);
            View.Scripted = null;
            View.Interface.Open(GroundScreen.Hud);
        }
    }

    private static void BowMouse(MouseButton button, bool pressed) =>
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = button, Pressed = pressed });

    private static void RequireBow(bool condition, string requirement)
    {
        if (!condition) throw new InvalidOperationException("bow-check: " + requirement);
        GD.Print("[bow-check] PASS " + requirement);
    }
}
