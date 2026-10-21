using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;

namespace HollowUniverse.Characters;

/// <summary>Camera-space motion of a complete source-bound held rig. Distances are metres and rotations degrees.
/// Families opt in through data; these offsets never change a source bone, grip or gameplay action clock.</summary>
public sealed class HeldLocomotionProfile
{
    public string Name { get; set; } = "";
    public string[] Families { get; set; } = Array.Empty<string>();
    public float[] Pivot { get; set; } = new float[3];
    public float[] WalkPosition { get; set; } = new float[3];
    public float[] WalkRotation { get; set; } = new float[3];
    public float[] SprintPosition { get; set; } = new float[3];
    public float[] SprintRotation { get; set; } = new float[3];
    public float[] LookPosition { get; set; } = new float[3];
    public float[] LookRotation { get; set; } = new float[3];
    public float ReferenceSpeed { get; set; } = 3f;
    public float MinimumSpeed { get; set; } = 0.25f;
    public float SprintMinimumSpeed { get; set; } = 3f;
    public float SprintGaitScale { get; set; } = 1.5f;
    public float CrouchGaitScale { get; set; } = 0.55f;
    public float GaitResponseSeconds { get; set; } = 0.12f;
    public float SprintResponseSeconds { get; set; } = 0.16f;
    public float LookResponseSeconds { get; set; } = 0.08f;
    public float MaximumLookRate { get; set; } = 3f;
    public float ActionFadeOutSeconds { get; set; } = 0.06f;
    public float ActionFadeInSeconds { get; set; } = 0.18f;
    public Dictionary<string, float> ActionWeights { get; set; } = new(StringComparer.Ordinal);

    internal static Vector3 Vector(float[] values) => new(values[0], values[1], values[2]);

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Families == null || Families.Length == 0 || ActionWeights == null)
            throw new InvalidDataException("A held locomotion profile needs a name and at least one family.");
        void Scalar(float value, float minimum, float maximum, string name)
        {
            if (!float.IsFinite(value) || value < minimum || value > maximum)
                throw new InvalidDataException($"Held locomotion {Name}/{name} must be within [{minimum}, {maximum}].");
        }
        void VectorCheck(float[] value, float maximum, string name)
        {
            if (value == null || value.Length != 3) throw new InvalidDataException($"Held locomotion {Name}/{name} needs three components.");
            foreach (float component in value) Scalar(component, -maximum, maximum, name);
        }
        VectorCheck(Pivot, 1, nameof(Pivot));
        VectorCheck(WalkPosition, 0.05f, nameof(WalkPosition));
        VectorCheck(WalkRotation, 5, nameof(WalkRotation));
        VectorCheck(SprintPosition, 0.15f, nameof(SprintPosition));
        VectorCheck(SprintRotation, 15, nameof(SprintRotation));
        VectorCheck(LookPosition, 0.025f, nameof(LookPosition));
        VectorCheck(LookRotation, 3, nameof(LookRotation));
        Scalar(ReferenceSpeed, 0.25f, 15, nameof(ReferenceSpeed));
        Scalar(MinimumSpeed, 0, ReferenceSpeed, nameof(MinimumSpeed));
        Scalar(SprintMinimumSpeed, 0, 15, nameof(SprintMinimumSpeed));
        Scalar(SprintGaitScale, 0, 3, nameof(SprintGaitScale));
        Scalar(CrouchGaitScale, 0, 1, nameof(CrouchGaitScale));
        Scalar(GaitResponseSeconds, 0.02f, 1, nameof(GaitResponseSeconds));
        Scalar(SprintResponseSeconds, 0.02f, 1, nameof(SprintResponseSeconds));
        Scalar(LookResponseSeconds, 0.02f, 1, nameof(LookResponseSeconds));
        Scalar(MaximumLookRate, 0.1f, 6, nameof(MaximumLookRate));
        Scalar(ActionFadeOutSeconds, 0.02f, 0.2f, nameof(ActionFadeOutSeconds));
        Scalar(ActionFadeInSeconds, 0.02f, 1, nameof(ActionFadeInSeconds));
        foreach ((string action, float weight) in ActionWeights) Scalar(weight, 0, 1, action);
    }
}

/// <summary>Optional art tuning. An absent document keeps the original exact source presentation.
/// A malformed document reports an error once and disables this presentation-only layer.</summary>
public static class HeldLocomotion
{
    public const string ResourcePath = "res://data/ground/held-locomotion.json";
    private sealed class Document
    {
        public Document() { }
        public int Version { get; set; } = 1;
        public List<HeldLocomotionProfile> Profiles { get; set; } = new();
    }
    private static Dictionary<string, HeldLocomotionProfile>? _families;
    public static string? LoadError { get; private set; }

    public static HeldLocomotionProfile? ForFamily(string family)
    {
        Ensure();
        return _families!.GetValueOrDefault(family);
    }

    private static void Ensure()
    {
        if (_families != null) return;
        _families = new(StringComparer.Ordinal);
        if (!Godot.FileAccess.FileExists(ResourcePath)) return;
        try
        {
            Document document = JsonSerializer.Deserialize<Document>(Godot.FileAccess.GetFileAsString(ResourcePath),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })
                ?? throw new InvalidDataException("Held locomotion document is empty.");
            if (document.Version != 1) throw new InvalidDataException("Unsupported held locomotion version.");
            if (document.Profiles == null) throw new InvalidDataException("Held locomotion profiles cannot be null.");
            var families = new Dictionary<string, HeldLocomotionProfile>(StringComparer.Ordinal);
            foreach (HeldLocomotionProfile profile in document.Profiles)
            {
                if (profile == null) throw new InvalidDataException("A held locomotion profile cannot be null.");
                profile.Validate();
                foreach (string family in profile.Families)
                    if (string.IsNullOrWhiteSpace(family) || !families.TryAdd(family, profile))
                        throw new InvalidDataException("Empty or duplicate held locomotion family: " + family);
            }
            _families = families;
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or ArgumentException)
        {
            LoadError = error.Message;
            GD.PushError("[held-locomotion] Disabled: " + error.Message);
        }
    }
}
