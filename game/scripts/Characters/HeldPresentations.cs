using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using HollowUniverse.Items;

namespace HollowUniverse.Characters;

/// <summary>Editable metres and Euler YXZ degrees. FirstPerson places the grip in camera space;
/// ThirdPerson is an offset on the existing whole-body holding pose.</summary>
public sealed class HeldPresentationPose
{
    public Vector3 Position { get; set; }
    public Vector3 Rotation { get; set; }
    [JsonIgnore] public Transform3D Transform => new(Basis.FromEuler(Rotation * (MathF.PI / 180f)), Position);
}

/// <summary>GripOffset is local to the item socket, WristOffset local to the resulting hand.
/// Fingers contains thumb, index, middle, ring, little finger, three curl angles each. Empty uses the measured wrap.</summary>
public sealed class HeldPresentationHand
{
    public HeldPresentationPose GripOffset { get; set; } = new();
    public HeldPresentationPose WristOffset { get; set; } = new();
    public Vector3 Pole { get; set; }
    public Vector3 Shoulder { get; set; }
    /// <summary>Additional camera-space shoulder placement for procedural first-person arms only.
    /// The full-body pose and source-bound skeletal actions keep their existing shoulder placement.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Vector3 FirstPersonShoulderOffset { get; set; }
    public float Radius { get; set; } = 0.018f;
    public float Seat { get; set; } = 0.018f;
    public float[] Fingers { get; set; } = Array.Empty<float>();
}

/// <summary>One artist-authored pose. Item offsets/rotations are camera-space, about the holding grip.
/// Hand channels offset the solved wrists in camera space; third-person channels are body-relative.</summary>
public sealed class HeldPresentationKey
{
    public float At { get; set; }
    public Vector3 Position { get; set; }
    public Vector3 Rotation { get; set; }
    public Vector3 ThirdPersonPosition { get; set; }
    public Vector3 ThirdPersonRotation { get; set; }
    public Vector3 RightHandPosition { get; set; }
    public Vector3 RightHandRotation { get; set; }
    public Vector3 LeftHandPosition { get; set; }
    public Vector3 LeftHandRotation { get; set; }
    public float[] RightFingers { get; set; } = Array.Empty<float>();
    public float[] LeftFingers { get; set; } = Array.Empty<float>();
    public string Ease { get; set; } = "smooth";
}

/// <summary>A normalized segment of an existing, body-retargeted skeletal animation. Equal endpoints hold a
/// source pose. The action's marker maps gameplay phase into this segment; this binding never changes game timing.</summary>
public sealed class HeldSkeletalClip
{
    public string Clip { get; set; } = "";
    public float Start { get; set; }
    public float End { get; set; } = 1f;
    /// <summary>Cross the source loop boundary. Equal endpoints then play one complete cycle.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Wrap { get; set; }
}

public sealed class HeldPresentationClip
{
    public float Duration { get; set; } = 1f;
    /// <summary>First-person fire motion while aiming. Camera/simulation recoil is independent.</summary>
    public float AimPositionScale { get; set; } = 1f;
    public float AimRotationScale { get; set; } = 1f;
    /// <summary>Authored contact/release point, remapped to the simulation's strike timing for melee; -1 for none.</summary>
    public float Marker { get; set; } = -1f;
    /// <summary>When present, source skeleton motion drives the arms and the held item follows the hand.
    /// Item position/rotation curves remain available as a fallback for missing source assets.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HeldSkeletalClip? Skeletal { get; set; }
    public List<HeldPresentationKey> Keys { get; set; } = new();

    public HeldPresentationKey Sample(float time, float[]? rightRest = null, float[]? leftRest = null)
    {
        if (Keys.Count == 0) return new HeldPresentationKey();
        float t = Math.Clamp(time, 0f, 1f);
        if (t <= Keys[0].At) return Keys[0];
        for (int i = 1; i < Keys.Count; i++)
        {
            if (t > Keys[i].At) continue;
            HeldPresentationKey a = Keys[i - 1], b = Keys[i];
            float u = Math.Clamp((t - a.At) / Math.Max(0.0001f, b.At - a.At), 0f, 1f);
            u = b.Ease switch { "in" => u * u * u, "out" => 1f - (1f - u) * (1f - u),
                "linear" => u, _ => u * u * (3f - 2f * u) };
            return new HeldPresentationKey
            {
                At = t, Position = a.Position.Lerp(b.Position, u), Rotation = a.Rotation.Lerp(b.Rotation, u),
                ThirdPersonPosition = a.ThirdPersonPosition.Lerp(b.ThirdPersonPosition, u),
                ThirdPersonRotation = a.ThirdPersonRotation.Lerp(b.ThirdPersonRotation, u),
                RightHandPosition = a.RightHandPosition.Lerp(b.RightHandPosition, u),
                RightHandRotation = a.RightHandRotation.Lerp(b.RightHandRotation, u),
                LeftHandPosition = a.LeftHandPosition.Lerp(b.LeftHandPosition, u),
                LeftHandRotation = a.LeftHandRotation.Lerp(b.LeftHandRotation, u),
                RightFingers = Fingers(a.RightFingers, b.RightFingers, rightRest, u),
                LeftFingers = Fingers(a.LeftFingers, b.LeftFingers, leftRest, u), Ease = b.Ease,
            };
        }
        return Keys[^1];
    }

    private static float[] Fingers(float[] a, float[] b, float[]? rest, float t)
    {
        float[] start = a.Length == 15 ? a : rest ?? Array.Empty<float>();
        float[] end = b.Length == 15 ? b : rest ?? Array.Empty<float>();
        if (start.Length != 15 || end.Length != 15) return Array.Empty<float>();
        var result = new float[15];
        for (int i = 0; i < result.Length; i++) result[i] = Mathf.Lerp(start[i], end[i], t);
        return result;
    }

    /// <summary>Align an artist's marker with the simulation contact phase without changing action duration.</summary>
    public float AlignMarker(float phase, float contactPhase)
    {
        if (Marker <= 0f || Marker >= 1f) return Math.Clamp(phase, 0f, 1f);
        float c = Math.Clamp(contactPhase, 0.001f, 0.999f), t = Math.Clamp(phase, 0f, 1f);
        return t <= c ? t * Marker / c : Marker + (t - c) * (1f - Marker) / (1f - c);
    }
}

public sealed class HeldPresentationProfile
{
    public string Item { get; set; } = "";
    public string Family { get; set; } = "";
    public HeldPresentationPose FirstPerson { get; set; } = new();
    /// <summary>Camera-space composition offset applied before the body's default eye-centred first-person rig.
    /// It moves the complete authored rig, preserving its shoulder, elbow and wrist relationships.
    /// Procedural poses apply it after solving, fading it out during aim to retain optical alignment.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HeldPresentationPose? FirstPersonRig { get; set; }
    public HeldPresentationPose ThirdPerson { get; set; } = new();
    public HeldPresentationPose AimOffset { get; set; } = new();
    public float BowDrawDistance { get; set; } = 0.42f;
    public HeldPresentationHand RightHand { get; set; } = new();
    public HeldPresentationHand LeftHand { get; set; } = new();
    public Vector3 SwingPivot { get; set; }
    public string ContactSocket { get; set; } = "impact";
    public string UseAction { get; set; } = "use";
    public Dictionary<string, HeldPresentationClip> Actions { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>One source of editable held art for gameplay and all previewers. No item-specific code fallbacks.
/// Replacing a profile bumps Revision, so already-equipped models see edits immediately.</summary>
public static class HeldPresentations
{
    public const string ResourcePath = "res://data/ground/held-presentations.json";
    private sealed class Document
    {
        public Document() { }
        public int Version { get; set; } = 1;
        public string Units { get; set; } = "metres; Euler YXZ degrees; normalized action time; fingers thumb/index/middle/ring/little, 3 joints each";
        public List<HeldPresentationProfile> Profiles { get; set; } = new();
    }
    private sealed class VectorConverter : JsonConverter<Vector3>
    {
        public override Vector3 Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("A vector must be [x,y,z].");
            reader.Read(); float x = reader.GetSingle(); reader.Read(); float y = reader.GetSingle();
            reader.Read(); float z = reader.GetSingle(); reader.Read();
            if (reader.TokenType != JsonTokenType.EndArray) throw new JsonException("A vector requires exactly three numbers.");
            return new Vector3(x, y, z);
        }
        public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options)
        {
            writer.WriteStartArray(); writer.WriteNumberValue(value.X); writer.WriteNumberValue(value.Y);
            writer.WriteNumberValue(value.Z); writer.WriteEndArray();
        }
    }
    private static readonly JsonSerializerOptions Json = Options();

    /// <summary>Stable authored data for capture provenance, using the catalogue's vector encoding.</summary>
    public static string Serialize(HeldPresentationProfile profile) => JsonSerializer.Serialize(profile, Json);
    private static Dictionary<string, HeldPresentationProfile>? _profiles;
    public static int Revision { get; private set; }
    public static string FilePath => ProjectSettings.GlobalizePath(ResourcePath);
    public static IReadOnlyDictionary<string, HeldPresentationProfile> All { get { Ensure(); return _profiles!; } }

    private static JsonSerializerOptions Options()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        options.Converters.Add(new VectorConverter());
        return options;
    }

    public static HeldPresentationProfile Get(string item)
    {
        Ensure();
        return _profiles!.TryGetValue(item, out HeldPresentationProfile? profile) ? profile
            : throw new InvalidDataException($"Held presentation missing for '{item}'.");
    }
    public static HeldPresentationProfile Clone(HeldPresentationProfile profile) =>
        JsonSerializer.Deserialize<HeldPresentationProfile>(JsonSerializer.Serialize(profile, Json), Json)!;

    public static void Replace(HeldPresentationProfile profile)
    {
        ThrowIfInvalid(Validate(profile));
        Ensure();
        if (!_profiles!.ContainsKey(profile.Item)) throw new InvalidDataException($"Unknown held item '{profile.Item}'.");
        _profiles[profile.Item] = Clone(profile);
        Revision++;
    }

    private static void Ensure() { if (_profiles == null) Reload(); }

    /// <summary>Validation completes before the live catalogue changes. An invalid file leaves working poses intact.</summary>
    public static void Reload(string? path = null)
    {
        string source = path == null ? Godot.FileAccess.GetFileAsString(ResourcePath) : File.ReadAllText(path);
        Document document = JsonSerializer.Deserialize<Document>(source, Json) ?? throw new InvalidDataException("Empty held presentation document.");
        if (document.Version != 1) throw new InvalidDataException($"Unsupported held presentation version {document.Version}.");
        var profiles = new Dictionary<string, HeldPresentationProfile>(StringComparer.Ordinal);
        if (document.Profiles == null) throw new InvalidDataException("The held presentation profiles list is required.");
        foreach (HeldPresentationProfile profile in document.Profiles)
        {
            ThrowIfInvalid(Validate(profile));
            if (!profiles.TryAdd(profile.Item, profile)) throw new InvalidDataException($"Duplicate held presentation '{profile.Item}'.");
        }
        ThrowIfInvalid(ValidateCoverage(profiles));
        _profiles = profiles;
        Revision++;
    }

    /// <summary>Writes beside the target, flushes, then atomically replaces it, preserving the previous file as .bak.
    /// Optional paths let the authoring smoke test prove round trips without writing production data.</summary>
    public static void Save(string? path = null)
    {
        Ensure();
        ThrowIfInvalid(ValidateAll());
        string target = Path.GetFullPath(path ?? FilePath);
        string directory = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, ".held-" + Guid.NewGuid().ToString("N") + ".tmp");
        string json = JsonSerializer.Serialize(new Document { Profiles = _profiles!.Values.OrderBy(p => p.Item, StringComparer.Ordinal).ToList() }, Json);
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccessMode, FileShare.None))
            {
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json + System.Environment.NewLine);
                stream.Write(bytes); stream.Flush(flushToDisk: true);
            }
            if (File.Exists(target)) File.Replace(temporary, target, target + ".bak", ignoreMetadataErrors: true);
            else File.Move(temporary, target);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private const System.IO.FileAccess FileAccessMode = System.IO.FileAccess.Write;

    public static IReadOnlyList<string> ValidateAll()
    {
        Ensure();
        var errors = _profiles!.Values.SelectMany(Validate).ToList();
        errors.AddRange(ValidateCoverage(_profiles));
        return errors;
    }

    private static IEnumerable<string> ValidateCoverage(IReadOnlyDictionary<string, HeldPresentationProfile> profiles)
    {
        var expected = ModelCatalog.Entries.Where(m => m.IsItem && m.Held != null).Select(m => m.Key).ToHashSet(StringComparer.Ordinal);
        foreach (string item in expected) if (!profiles.ContainsKey(item)) yield return $"Missing held presentation: {item}.";
        foreach (string item in profiles.Keys) if (!expected.Contains(item)) yield return $"Unknown held presentation: {item}.";
    }

    public static IReadOnlyList<string> Validate(HeldPresentationProfile profile)
    {
        var errors = new List<string>();
        if (profile == null) { errors.Add("A held presentation profile cannot be null."); return errors; }
        void Error(string text) => errors.Add($"{profile.Item}: {text}");
        bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
        void Pose(HeldPresentationPose? p, string name)
        {
            if (p == null || !Finite(p.Position) || !Finite(p.Rotation) || p.Position.Length() > 5f)
                Error($"{name} must contain finite positions within 5 m and rotations.");
        }
        if (string.IsNullOrWhiteSpace(profile.Item) || string.IsNullOrWhiteSpace(profile.Family)) Error("Item and family are required.");
        Pose(profile.FirstPerson, "firstPerson"); Pose(profile.ThirdPerson, "thirdPerson"); Pose(profile.AimOffset, "aimOffset");
        if (profile.FirstPersonRig != null) Pose(profile.FirstPersonRig, "firstPersonRig");
        if (!float.IsFinite(profile.BowDrawDistance) || profile.BowDrawDistance <= 0 || profile.BowDrawDistance > 1) Error("Bow draw distance must be in (0,1] m.");
        foreach ((HeldPresentationHand? hand, string name) in new[] { (profile.RightHand, "rightHand"), (profile.LeftHand, "leftHand") })
        {
            if (hand == null) { Error($"{name} is required."); continue; }
            Pose(hand.GripOffset, name + ".gripOffset"); Pose(hand.WristOffset, name + ".wristOffset");
            if (!Finite(hand.Pole) || hand.Pole.LengthSquared() < 0.001f || !Finite(hand.Shoulder)) Error($"{name} needs a finite nonzero elbow pole and shoulder.");
            if (!Finite(hand.FirstPersonShoulderOffset)) Error($"{name}.firstPersonShoulderOffset must be finite.");
            if (!float.IsFinite(hand.Radius) || hand.Radius < 0 || hand.Radius > 0.2f || !float.IsFinite(hand.Seat) || hand.Seat < 0 || hand.Seat > 0.2f) Error($"{name} radius/seat must be 0–0.2 m.");
            if (hand.Fingers == null || (hand.Fingers.Length != 0 && hand.Fingers.Length != 15) || hand.Fingers.Any(x => !float.IsFinite(x) || x < -30 || x > 140)) Error($"{name} fingers require 0 or 15 finite curls in -30–140 degrees.");
        }
        if (!Finite(profile.SwingPivot) || profile.SwingPivot.Length() > 2f) Error("Swing pivot must be finite and within 2 m.");
        if (profile.ContactSocket == null || profile.ContactSocket.Contains('/') || profile.ContactSocket.Length > 64) Error("Contact socket must be a simple socket name.");
        if (profile.Actions == null || !profile.Actions.ContainsKey("idle")) { Error("Actions must include idle."); return errors; }
        if (profile.UseAction == null || !profile.Actions.ContainsKey(profile.UseAction)) Error($"Use action '{profile.UseAction}' is missing.");
        foreach ((string name, HeldPresentationClip clip) in profile.Actions)
        {
            if (clip == null || !float.IsFinite(clip.Duration) || clip.Duration <= 0 || clip.Duration > 30f ||
                !float.IsFinite(clip.Marker) || clip.Marker < -1 || clip.Marker > 1 || clip.Keys == null || clip.Keys.Count < 2)
            { Error($"Action {name} requires duration (0,30], marker [-1,1], and at least two keys."); continue; }
            if (clip.Skeletal is { } skeletal && !HeldSkeletalMotion.ValidBinding(skeletal))
                Error($"Action {name} skeletal binding requires a simple clip name and valid start/end/wrap in [0,1].");
            if (!float.IsFinite(clip.AimPositionScale) || clip.AimPositionScale < 0 || clip.AimPositionScale > 1 ||
                !float.IsFinite(clip.AimRotationScale) || clip.AimRotationScale < 0 || clip.AimRotationScale > 1)
                Error($"Action {name} aimed visual motion scales must be in [0,1].");
            if (clip.Keys[0] == null || clip.Keys[^1] == null || clip.Keys[0].At != 0 || clip.Keys[^1].At != 1) Error($"Action {name} needs keys at 0 and 1.");
            float previous = -1;
            foreach (HeldPresentationKey key in clip.Keys)
            {
                if (key == null) { Error($"Action {name} contains a null key."); continue; }
                if (!float.IsFinite(key.At) || key.At <= previous || key.At < 0 || key.At > 1) Error($"Action {name} key times must strictly increase in [0,1].");
                previous = key.At;
                if (key.Ease is not ("smooth" or "linear" or "in" or "out")) Error($"Action {name} has unknown easing '{key.Ease}'.");
                foreach (float[]? fingers in new[] { key.RightFingers, key.LeftFingers })
                    if (fingers == null || (fingers.Length != 0 && fingers.Length != 15) || fingers.Any(x => !float.IsFinite(x) || x < -30 || x > 140))
                        Error($"Action {name} finger channels require 0 or 15 curls in -30–140 degrees.");
                foreach (Vector3 v in new[] { key.Position, key.Rotation, key.ThirdPersonPosition, key.ThirdPersonRotation,
                    key.RightHandPosition, key.RightHandRotation, key.LeftHandPosition, key.LeftHandRotation })
                    if (!Finite(v)) Error($"Action {name} has a non-finite channel.");
                if (key.Position.Length() > 5 || key.ThirdPersonPosition.Length() > 5 || key.RightHandPosition.Length() > 5 || key.LeftHandPosition.Length() > 5)
                    Error($"Action {name} translation exceeds 5 m.");
            }
        }
        return errors;
    }

    private static void ThrowIfInvalid(IEnumerable<string> errors)
    {
        string[] all = errors.ToArray();
        if (all.Length > 0) throw new InvalidDataException(string.Join(System.Environment.NewLine, all));
    }
}
