using System;
using Godot;
using Hollow.Core;
using Hollow.Simulation.Building;

namespace HollowUniverse.Build;

public partial class BuildSandbox
{
    // A small visual fixture using the exact server preset and normal building renderer.
    private void ClanBase(BuildingFrame frame)
    {
        string json = FileAccess.GetFileAsString("res://data/ground/admin-bases/clan-3x3.json");
        BuildingBlueprint blueprint = BuildingBlueprint.Parse(_data, json);
        int grade = Options.TryGetValue("grade", out string? value) && int.TryParse(value, out int parsed)
            ? Math.Clamp(parsed, 0, _data.Grades.Count - 1) : 2;
        blueprint.Place(_world, frame, grade, Player, 0, _nextBuildId);
        Look(frame, new Vector3D(18, 15, 23), new Vector3D(0, 4.8, 0));
        GD.Print($"[clan-base] {blueprint.Name}: {_world.PieceCount} pieces, grade {grade}, collapsing {_world.Collapsing}");
    }
}
