using System;
using System.Numerics;
using Hollow.Core;
using Hollow.Net.Bits;
using Hollow.Simulation;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Surface;

namespace Hollow.Net.Protocol;

/// <summary>
/// Between simulation values and their wire form. The client predicts with <c>ToMoveInput(ToCommand(...))</c>, the
/// same values the server gets, so both step identically (GROUND.md §4.5).
/// </summary>
public static class NetConvert
{
    /// <summary>Uses the descriptor owned by the session, never a digest as a procedural seed.</summary>
    public static EnvironmentIdentity ToEnvironment(PlanetEnvironmentDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var identity = new EnvironmentIdentity
        {
            DescriptorSchema = descriptor.CanonicalSchema,
            DescriptorIdentity = descriptor.Identity,
            ProfileId = descriptor.ProfileId,
            QualificationIdentity = descriptor.QualificationIdentity,
        };
        if (!descriptor.IsLegacyMultiplayerCompatible || !ValidEnvironment(identity))
            throw new ArgumentException("The acquired environment cannot be represented by this protocol.", nameof(descriptor));
        return identity;
    }

    /// <summary>Metadata-only validation before any world acquisition, physics or player publication.</summary>
    public static bool TryResolveEnvironment(in ZoneInfo info, in EnvironmentIdentity identity,
        out ZoneSpec? zone, out PlanetEnvironmentDescriptor? descriptor, out string? rejection)
    {
        zone = null;
        descriptor = null;
        rejection = null;
        if (!ValidEnvironment(identity)) { rejection = "Malformed or unknown environment identity."; return false; }
        // The inherited FromInfo fallback maps zero to one; do not negotiate a clock the peers would interpret differently.
        if (info.Ground > (byte)GroundKind.Walking || !float.IsFinite(info.RotationTimeScale) || info.RotationTimeScale <= 0)
        { rejection = "Unknown terrain generator or invalid zone clock."; return false; }
        try
        {
            ZoneSpec resolvedZone = FromInfo(info);
            PlanetEnvironmentDescriptor resolved = resolvedZone.EnvironmentDescriptor();
            if (!resolved.IsLegacyMultiplayerCompatible || !SameEnvironment(identity, ToEnvironment(resolved)))
            { rejection = "The server's physical environment or qualification contract differs from this build."; return false; }
            zone = resolvedZone;
            descriptor = resolved;
            return true;
        }
        catch (ArgumentException) { rejection = "Invalid world description."; return false; }
        catch (InvalidOperationException) { rejection = "Unsupported world description."; return false; }
        catch (NotSupportedException) { rejection = "Unsupported environment profile."; return false; }
    }

    public static bool ValidEnvironment(in EnvironmentIdentity identity) =>
        identity.DescriptorSchema == PlanetEnvironmentDescriptor.DescriptorSchema &&
        IsDigest(identity.DescriptorIdentity) && IsDigest(identity.QualificationIdentity) &&
        identity.ProfileId is { Length: > 0 and <= 64 } && IsProfile(identity.ProfileId);

    public static bool SameEnvironment(in EnvironmentIdentity a, in EnvironmentIdentity b) =>
        ValidEnvironment(a) && ValidEnvironment(b) && a.DescriptorSchema == b.DescriptorSchema &&
        string.Equals(a.DescriptorIdentity, b.DescriptorIdentity, StringComparison.Ordinal) &&
        string.Equals(a.ProfileId, b.ProfileId, StringComparison.Ordinal) &&
        string.Equals(a.QualificationIdentity, b.QualificationIdentity, StringComparison.Ordinal);

    /// <summary>Welcome must repeat the exact wire offer, including physics values outside the terrain descriptor.</summary>
    public static bool SameZone(in ZoneInfo a, in ZoneInfo b) =>
        a.SystemAddress == b.SystemAddress && a.BodyPath == b.BodyPath && a.Radius == b.Radius &&
        a.SurfaceGravity == b.SurfaceGravity && a.SurfaceSeed == b.SurfaceSeed &&
        a.LatitudeDegrees == b.LatitudeDegrees && a.LongitudeDegrees == b.LongitudeDegrees &&
        a.StandIn == b.StandIn && a.Ground == b.Ground && a.RotationTimeScale == b.RotationTimeScale &&
        a.AirDensity == b.AirDensity && a.AirScaleHeight == b.AirScaleHeight;

    private static bool IsDigest(string? value)
    {
        if (value is not { Length: 64 }) return false;
        foreach (char c in value) if (!(c is >= '0' and <= '9' or >= 'A' and <= 'F')) return false;
        return true;
    }

    private static bool IsProfile(string value)
    {
        foreach (char c in value) if (!(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-')) return false;
        return true;
    }

    /// <summary>A thing named on the wire (GROUND_SYSTEMS.md §3.1).</summary>
    public static TargetNet ToNet(in Simulation.Targets.TargetRef target) => new() { Family = target.Family, Id = target.Id, Part = target.Part };

    /// <summary>A thing named from the wire (untrusted: a family that doesn't exist names nothing).</summary>
    public static Simulation.Targets.TargetRef ToRef(in TargetNet target) => new(target.Family, target.Id, target.Part);

    public static InputCommand ToCommand(in MoveInput input)
    {
        (ushort u, ushort v) = Quantize.Octahedral(input.View);
        return new InputCommand
        {
            Forward = Axis(input.Forward),
            Right = Axis(input.Right),
            ViewU = u,
            ViewV = v,
            Buttons = input.Buttons,
            Frame = FrameOnWire(input.Frame),
            FlySpeed = float.IsFinite(input.NoclipSpeed) && input.NoclipSpeed > 0
                ? (byte)Math.Clamp((int)MathF.Round(Math.Clamp(input.NoclipSpeed, 1, 200)), 1, 200) : (byte)0,
        };
    }

    /// <summary>
    /// A frame's key on the wire: a ship's net ID (a client's prediction keys its frames by them, IRON_WAKE.md §4.11; the
    /// server turns its own keys, the ships' IDs, into them), 0 the zone's.
    /// </summary>
    public static uint FrameOnWire(ulong key) => key <= uint.MaxValue ? (uint)key : 0;

    /// <summary>A view direction from the wire (octahedral: always a unit direction, docs/WHOLE_WORLD.md §5).</summary>
    public static Vector3 View(ushort u, ushort v) => Quantize.FromOctahedral(u, v);

    /// <summary>A view direction for the wire.</summary>
    public static (ushort U, ushort V) ToView(Vector3 view) => Quantize.Octahedral(view);

    /// <summary>A tick's movement and actions as one command (the actions ride along: G2).</summary>
    public static InputCommand ToCommand(in MoveInput input, in ActionInput actions)
    {
        InputCommand command = ToCommand(input);
        command.Held = (byte)Math.Min(actions.HeldSlot, ActionInput.NoSlot);
        command.Actions = actions.Buttons & (ActionButtons.Primary | ActionButtons.Secondary | ActionButtons.PrimaryPressed);
        command.ViewDelay = actions.ViewDelay;
        return command;
    }

    /// <summary>The actions of a command (untrusted: a slot past the belt is "none").</summary>
    public static ActionInput ToActions(in InputCommand command) => new()
    {
        HeldSlot = command.Held < 6 ? command.Held : ActionInput.NoSlot,
        Buttons = command.Actions & (ActionButtons.Primary | ActionButtons.Secondary | ActionButtons.PrimaryPressed),
        ViewDelay = command.ViewDelay,
    };

    /// <summary>A command's movement; its view's frame is the wire's (a ship's net ID: the server maps it to the ship's own key).</summary>
    public static MoveInput ToMoveInput(in InputCommand command) => new()
    {
        Forward = Math.Clamp(command.Forward / 127f, -1f, 1f),
        Right = Math.Clamp(command.Right / 127f, -1f, 1f),
        View = Quantize.FromOctahedral(command.ViewU, command.ViewV),
        Buttons = command.Buttons & (MoveButtons.Jump | MoveButtons.JumpPressed | MoveButtons.Crouch | MoveButtons.Sprint),
        Frame = command.Frame,
        NoclipSpeed = Math.Min(command.FlySpeed, (byte)200),
    };

    /// <summary>The exact state, for the player's own reconciliation.</summary>
    public static OwnState ToOwn(in CharacterState s) => new()
    {
        PositionX = s.Position.X,
        PositionY = s.Position.Y,
        PositionZ = s.Position.Z,
        VelocityX = s.Velocity.X,
        VelocityY = s.Velocity.Y,
        VelocityZ = s.Velocity.Z,
        Stance = s.Stance,
        Health = s.Health,
        Flags = s.Flags,
        Cheats = s.Cheats,
        Frame = FrameOnWire(s.Frame),
    };

    /// <summary>The state an own-state message carries; its frame is the wire's, a ship's net ID (a client's prediction's key).</summary>
    public static CharacterState FromOwn(in OwnState o) => new()
    {
        Position = new Vector3(o.PositionX, o.PositionY, o.PositionZ),
        Velocity = new Vector3(o.VelocityX, o.VelocityY, o.VelocityZ),
        Stance = o.Stance,
        Health = o.Health,
        Flags = o.Flags,
        Cheats = o.Cheats & (MoveCheats.God | MoveCheats.Noclip),
        Frame = o.Frame,
    };

    /// <summary>
    /// A character as others see it; the view and the head's free look are those of its last input (the free look
    /// bounded again: the input is untrusted).
    /// </summary>
    public static CharacterNetState ToNet(in CharacterState s, Vector3 view, sbyte lookYaw = 0, sbyte lookPitch = 0)
    {
        (ushort u, ushort v) = Quantize.Octahedral(view);
        return ToNet(s, u, v, lookYaw, lookPitch);
    }

    /// <summary>A character as others see it, with its view as it came on the wire (octahedral).</summary>
    public static CharacterNetState ToNet(in CharacterState s, ushort viewU, ushort viewV, sbyte lookYaw = 0, sbyte lookPitch = 0) => new()
    {
        X = Quantize.Fixed(s.Position.X, Quantize.PositionScale),
        Y = Quantize.Fixed(s.Position.Y, Quantize.PositionScale),
        Z = Quantize.Fixed(s.Position.Z, Quantize.PositionScale),
        ViewU = viewU,
        ViewV = viewV,
        LookYaw = BoundLook(lookYaw, FreeLook.MaxYaw),
        LookPitch = BoundLook(lookPitch, FreeLook.MaxPitch),
        Flags = s.Flags,
        Stance = s.Stance,
        Health = s.Health,
    };

    /// <summary>Free look on the wire (inputs and characters' state): steps of 1.5°, so a byte holds ±190°.</summary>
    public const float LookStep = 1.5f * MathF.PI / 180f;

    /// <summary>A free look turn (radians) in steps, within ±<paramref name="limit"/>.</summary>
    public static sbyte ToLook(float radians, float limit) =>
        float.IsFinite(radians) ? BoundLook((sbyte)Math.Clamp(Math.Round(radians / LookStep, MidpointRounding.ToEven), -127, 127), limit) : (sbyte)0;

    public static float FromLook(sbyte steps) => steps * LookStep;

    /// <summary>A received turn held within ±<paramref name="limit"/> (RustMVP's server: yaw 120°, pitch 170°).</summary>
    public static sbyte BoundLook(sbyte steps, float limit)
    {
        int most = (int)(limit / LookStep + 1e-3f);
        return (sbyte)Math.Clamp((int)steps, -most, most);
    }

    /// <summary>A character's view from the wire.</summary>
    public static Vector3 ViewOf(in CharacterNetState s) => Quantize.FromOctahedral(s.ViewU, s.ViewV);

    public static Vector3 PositionOf(in CharacterNetState s) => new(
        Quantize.FromFixed(s.X, Quantize.PositionScale),
        Quantize.FromFixed(s.Y, Quantize.PositionScale),
        Quantize.FromFixed(s.Z, Quantize.PositionScale));

    public static ZoneInfo ToInfo(ZoneSpec spec) => new()
    {
        SystemAddress = spec.SystemAddress,
        BodyPath = spec.BodyPath,
        Radius = spec.Radius,
        SurfaceGravity = spec.SurfaceGravity,
        SurfaceSeed = spec.SurfaceSeed,
        LatitudeDegrees = spec.LatitudeDegrees,
        LongitudeDegrees = spec.LongitudeDegrees,
        StandIn = spec.StandIn,
        Ground = (byte)spec.Ground,
        RotationTimeScale = (float)spec.RotationTimeScale,
        AirDensity = (float)spec.AirDensity,
        AirScaleHeight = (float)spec.AirScaleHeight,
    };

    public static ZoneSpec FromInfo(in ZoneInfo info) => new()
    {
        SystemAddress = info.SystemAddress,
        BodyPath = info.BodyPath,
        Radius = info.Radius,
        SurfaceGravity = info.SurfaceGravity,
        SurfaceSeed = info.SurfaceSeed,
        LatitudeDegrees = info.LatitudeDegrees,
        LongitudeDegrees = info.LongitudeDegrees,
        StandIn = info.StandIn,
        // Untrusted: a kind this build doesn't know stands on the stand-in.
        Ground = info.Ground == (byte)GroundKind.Walking ? GroundKind.Walking : GroundKind.StandIn,
        RotationTimeScale = info.RotationTimeScale > 0 && float.IsFinite(info.RotationTimeScale) ? info.RotationTimeScale : 1,
        AirDensity = info.AirDensity,
        AirScaleHeight = info.AirScaleHeight,
    };

    private static sbyte Axis(float value) => (sbyte)Math.Round(Math.Clamp(value, -1f, 1f) * 127f, MidpointRounding.ToEven);
}
