using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Hollow.Client;
using Hollow.Simulation.Building;
using HollowUniverse.UI;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

public partial class GroundTour
{
    // Normal client placement intents and authoritative replication, one clear view per supplied assembly.
    private async Task FittedTour()
    {
        await RepairGod(true);
        Admin("give", "building-plan 1");
        Admin("give", "wood 4000");
        await Until(() => Count("building-plan") > 0 && Count("wood") >= 3000, 5);
        var content = Client.Content;
        var fittings = content.Items.Where(i => i.Door?.Opening == Opening.Window).ToList();
        fittings.Add(content.ItemByKey("wooden-double-door"));
        fittings.Add(content.ItemByKey("armoured-double-door"));
        if (fittings.Count != 7) throw new InvalidOperationException("Expected five windows and both newly admitted double doors.");
        for (int index = 0; index < fittings.Count; index++)
        {
            var item = fittings[index];
            Admin("give", item.Key + " 1");
            await Until(() => Count(item.Key) > 0, 5);
            (Num.Vector3 spot, Num.Vector3 towards) = OpenSpot(Offset(70 + index * 16, -50));
            await TeleportTo(spot);
            View.TurnToward(towards);
            View.Pitch = -0.3f;
            await Settle();
            await Hold("building-plan", 5);
            Num.Vector3 ahead = Forward();
            Piece floor = await PlaceAimed("foundation", p => p.Support == null)
                ?? throw new InvalidOperationException(item.Key + ": foundation placement refused");
            Num.Vector3 centre = Centre(floor);
            string piece = item.Door!.Opening == Opening.Window ? "window" : "wall-frame";
            Piece aperture = await PlaceAimed(piece, p => p.Support == floor && Num.Vector3.Dot(At(p) - centre, ahead) < -1)
                ?? throw new InvalidOperationException(item.Key + ": aperture placement refused");
            Num.Vector3 at = Centre(aperture), up = Client.Frame.UpAt(at);
            Num.Vector3 front = FlatUnit(Client.Character.Position - at);
            await TeleportTo(at + front * 2.8f);
            Face(at + up * 1.5f);
            await Hold(item.Key, 5);
            await Seconds(0.4);
            View.UseHeld();
            await Until(() => aperture.Door != null, 5);
            Door fitted = aperture.Door ?? throw new InvalidOperationException(item.Key + ": fitted placement did not replicate");
            if (fitted.Def != item.Door || Count(item.Key) != 0) throw new InvalidOperationException(item.Key + ": wrong definition or item not consumed");
            Me.HeldSlot = -1;
            View.Interface.Open(GroundScreen.Hud);
            await Seconds(0.5);
            await Shot("fitted-" + item.Key);
            if (fitted.Def.Opens)
            {
                Me.Interact(fitted.Ref, "door-open");
                await Until(() => fitted.Open, 3);
                if (!fitted.Open) throw new InvalidOperationException(item.Key + ": open interaction refused");
                await Seconds(1.1);
                await Shot("fitted-" + item.Key + "-open");
                Me.Interact(fitted.Ref, "door-close");
                await Until(() => !fitted.Open, 3);
                if (fitted.Open) throw new InvalidOperationException(item.Key + ": close interaction refused");
            }
            GD.Print($"[fitted-check] {item.Key}: ordinary place consumed item, replicated {fitted.Id}, correct fitted mesh and {(fitted.Def.Opens ? "open/close" : "fixed attachment")}");
        }
        GD.Print("[fitted-check] PASS five windows and two double doors placed through the live client/server pipeline; nine views captured.");
    }
}
