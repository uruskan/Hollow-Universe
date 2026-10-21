using Godot;
using Hollow.Client.Feedback;

namespace HollowUniverse.Ground;

/// <summary>Godot resource adapter for the shared, engine-free contact response table.</summary>
public static class ContactFeedback
{
    private static ContactResponseData? _data;
    private static ActionCueData? _actions;
    public static ActionCueData Actions => _actions ??= ActionCueData.Parse(
        FileAccess.GetFileAsString("res://data/ground/" + ActionCueData.FileName));
    public static ContactResponseData Data => _data ??= ContactResponseData.Parse(
        FileAccess.GetFileAsString("res://data/ground/" + ContactResponseData.FileName));

    public static Color Dust(string surface)
    {
        System.Numerics.Vector3 c = Data.Surface(surface).Dust;
        return new Color(c.X, c.Y, c.Z);
    }
}
