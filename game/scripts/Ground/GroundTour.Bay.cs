using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Hollow.Client;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

/// <summary>
/// The vehicle workbench's bay (<c>--tour-steps bay</c>; BayMarking): the bench's ghost with the bay's outline in front of
/// it, then the bench put down and its bay marked on the ground (paint and corner posts), seen from beside it. On the
/// boat step's beach when it ran (sand shows the paint), else in the open near the landing site.
/// </summary>
public partial class GroundTour
{
    private async Task BayTour()
    {
        (Num.Vector3 spot, Num.Vector3 towards) = _beach is { } beach
            ? (beach.Shore - beach.ToWater * 9f, -beach.ToWater)
            : OpenSpot(Offset(35, 20));
        await TeleportTo(spot);
        View.TurnToward(towards);
        await Settle();
        View.Console.Submit("time 11");
        Admin("give", "vehicle-workbench 1");
        await Until(() => SlotOf(Me.Inventory.Main, "vehicle-workbench") != null || SlotOf(Me.Inventory.Belt, "vehicle-workbench") != null, 5);
        await Hold("vehicle-workbench", 5);
        Num.Vector3 feet = Client.Character.Position;
        Num.Vector3 bench = Client.Placement().Ground?.Settle(feet + towards * 2.5f) ?? feet + towards * 2.5f;
        Face(bench);
        await Seconds(0.8);
        await Shot("bay-ghost");
        int things = Client.Survival().Things.Count;
        View.UseHeld();
        await Until(() => Client.Survival().Things.Count > things, 5);
        await Seconds(1.0);
        GD.Print($"[tour] bay: the bench {(Client.Survival().Things.Count > things ? "placed" : "not placed")}");
        Me.HeldSlot = -1;
        // From beside the bay, across it toward the bench.
        Num.Vector3 up = Client.Frame.UpAt(feet);
        Num.Vector3 side = Num.Vector3.Normalize(Num.Vector3.Cross(up, towards));
        Num.Vector3 middle = bench + towards * 5f;
        await TeleportTo(middle + side * 6f - towards * 1.5f);
        Face(middle - up * 0.6f);
        await Seconds(1.0);
        await Shot("bay-painted");
    }
}
