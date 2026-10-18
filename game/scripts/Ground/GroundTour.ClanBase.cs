using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Hollow.Client;
using Hollow.Core;
using Hollow.Simulation;
using Hollow.Simulation.Building;
using Hollow.Simulation.Nature;
using HollowUniverse.UI;
using HollowUniverse.UI.Model;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

public partial class GroundTour
{
    private async Task ClanBaseTour()
    {
        await Until(() => Client.Console().IsAdmin, 10);
        if (!Client.Console().IsAdmin) throw new InvalidOperationException("Clan-base tour needs the hosted admin.");
        BuildingBlueprint blueprint = BuildingBlueprint.Parse(Client.Content.Building,
            FileAccess.GetFileAsString("res://data/ground/admin-bases/clan-3x3.json"));
        GroundProbe ground = Client.Placement().Ground!.Value;
        Num.Vector3 site = await ClanBaseSite(blueprint, ground);
        await RepairGod(true);
        await TeleportTo(ground.Offset(site, 0, 26));
        Face(site + Client.Frame.UpAt(site) * Client.EyeHeight);
        await Settle();
        var before = Client.Building().World!.AllPieces.Select(p => p.Id).ToHashSet();
        View.Console.Run("spawn raidbase 2");
        await Until(() => Client.Building().World!.AllPieces.Count(p => !before.Contains(p.Id)) >= blueprint.Nodes.Count, 15);
        Piece[] pieces = Client.Building().World!.AllPieces.Where(p => !before.Contains(p.Id)).ToArray();
        if (pieces.Length != blueprint.Nodes.Count || pieces.Any(p => p.Grade != 2 || p.Collapsing))
            throw new InvalidOperationException("Clan-base command did not replicate the complete stone preset; inspect F1 output.");
        await Until(() => pieces.Count(p => p.Door != null) == 4 && pieces[0].Structure.Claim != null, 5);
        if (pieces.Count(p => p.Door != null) != 4 || pieces[0].Structure.Claim == null)
            throw new InvalidOperationException("Clan-base doors/cupboard did not replicate.");
        BuildingFrame frame = pieces[0].Structure.Frame;
        Admin("noclip", "on");
        await Seconds(0.3);
        try
        {
            await ClanBaseShot("clan-base-exterior", frame, new Vector3D(18, 15, 23), new Vector3D(0, 4.8, 0));
            await ClanBaseShot("clan-base-shooting-floor", frame, new Vector3D(0, 7.6, 1.6), new Vector3D(-1.5, 6.3, 6.3));
            await ClanBaseShot("clan-base-roof", frame, new Vector3D(12, 14, 14), new Vector3D(0, 9.8, 0));
            View.CameraLift = 0;
            await TeleportTo(F(frame.ToZone(new Vector3D(0, 0, 14))));
            Face(F(frame.ToZone(new Vector3D(0, 5, 0))));
            View.Interface.ToggleConsole();
            View.Interface.ConsoleTab = ConsoleTab.Tools;
            View.Interface.HoldPointer(new Vector2(-10000, -10000));
            await Seconds(0.3);
            await Shot("clan-base-tools");
            View.Interface.Open(GroundScreen.Hud);
            GD.Print($"[clan-base-check] PASS F1 spawn replicated {pieces.Length} pieces, four doors and one cupboard; four views captured.");
        }
        finally
        {
            View.CameraLift = 0;
            Admin("noclip", "off");
        }
    }

    private async Task ClanBaseShot(string name, BuildingFrame frame, Vector3D eye, Vector3D target)
    {
        Num.Vector3 wantedEye = F(frame.ToZone(eye)), wantedTarget = F(frame.ToZone(target));
        await TeleportTo(wantedEye);
        await Settle();
        View.CameraLift = Num.Vector3.Dot(wantedEye - Client.RenderEye, Client.RenderUp);
        View.LookToward(wantedTarget - (Client.RenderEye + Client.RenderUp * View.CameraLift));
        await Seconds(0.3);
        await Shot(name);
    }

    // Find a dry, quiet site analytically before loading it. Yield between candidates so the client keeps ticking.
    private async Task<Num.Vector3> ClanBaseSite(BuildingBlueprint blueprint, GroundProbe ground)
    {
        var nature = new List<NaturalObject>();
        for (int ring = 1; ring <= 7; ring++)
        {
            for (int side = 0; side < 16; side++)
            {
                float angle = side * MathF.Tau / 16;
                Num.Vector3 site = ground.Offset(Landing, MathF.Cos(angle) * ring * 24, MathF.Sin(angle) * ring * 24);
                Num.Vector3 up = Client.Frame.UpAt(site);
                BuildingFrame frame = BuildingFrame.At(new Vector3D(site.X, site.Y, site.Z), new Vector3D(up.X, up.Y, up.Z), 0);
                double low = double.PositiveInfinity, high = double.NegativeInfinity;
                bool wet = false;
                foreach (Vector3D sample in blueprint.GroundSamples)
                {
                    Num.Vector3 point = F(frame.ToZone(sample));
                    double height = -ground.HeightAbove(point);
                    low = Math.Min(low, height);
                    high = Math.Max(high, height);
                    wet |= ground.InWater(ground.Settle(point));
                }
                double entry = ground.HeightAbove(F(frame.ToZone(blueprint.Entrance))) + high + Client.Content.Building.FoundationClearance + 0.02;
                nature.Clear();
                Client.Survival().Nature?.Query(site, 15, nature);
                bool blocked = nature.Any(n => Client.Content.Natural(n.Kind)!.IsSolid)
                    || Client.Building().World!.AllPieces.Any(p => Num.Vector3.Distance(p.ZoneTransform.Position, site) < 18);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (!wet && !blocked && high - low <= 1.1 && entry <= 0.6) return site;
            }
        }
        throw new InvalidOperationException("No clear, level clan-base site found near this landing point.");
    }

    private static Num.Vector3 F(Vector3D p) => new((float)p.X, (float)p.Y, (float)p.Z);
}
