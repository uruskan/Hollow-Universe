using System;
using System.Numerics;
using Hollow.Net.Bits;
using Hollow.Simulation.Animals;
using Hollow.Simulation.Content;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;

namespace Hollow.Net.Protocol;

/// <summary>An animal as a client sees it (unpacked from its snapshot state).</summary>
public readonly record struct AnimalLook(
    byte Species,
    AnimalState State,
    bool Dead,
    bool Alert,
    float Health,
    bool Warning,
    byte AttackCount,
    AttackKind Attack,
    byte HurtCount);

/// <summary>
/// How an animal travels in snapshots (G7, GROUND.md §6.9). Animals are entities of kind <see cref="EntityKind.Animal"/>
/// in the same delta-coded, prioritised stream as characters, so they reuse <see cref="CharacterNetState"/> and every
/// part of replication as it is; this is what its fields mean for an animal:
/// <list type="table">
/// <item><term>X, Y, Z, Yaw</term><description>its feet on the ground and its heading, as a character's.</description></item>
/// <item><term>Flags</term><description><see cref="MoveFlags.Dead"/> for a corpse, else <see cref="MoveFlags.OnGround"/>;
/// <see cref="MoveFlags.Sprinting"/> while it warns someone off (<see cref="Animal.Warning"/>).</description></item>
/// <item><term>Health</term><description>its health as a fraction of 255; a corpse's, the harvest left.</description></item>
/// <item><term>Held</term><description>its species' number (animals.json <c>id</c>).</description></item>
/// <item><term>ActionCount, Action</term><description>attacks begun (4 bits, wrapping) and the last one's
/// <see cref="AttackKind"/>: the client animates each new one.</description></item>
/// <item><term>HeldMods</term><description>its <see cref="AnimalState"/> in the high four bits, hits taken (4 bits, wrapping) in the low.</description></item>
/// <item><term>Aiming</term><description>alert: aware of a threat (head up, ears forward).</description></item>
/// <item><term>Pitch, Stance, Sleeping</term><description>unused (0), so they cost a bit each in a delta.</description></item>
/// </list>
/// The species' key also comes with the animal's <c>Spawn</c> (its name).
/// </summary>
public static class AnimalNet
{
    public static CharacterNetState Pack(Animal a) => new()
    {
        X = Quantize.Fixed(a.Position.X, Quantize.PositionScale),
        Y = Quantize.Fixed(a.Position.Y, Quantize.PositionScale),
        Z = Quantize.Fixed(a.Position.Z, Quantize.PositionScale),
        Yaw = Quantize.Angle16(a.Yaw),
        Flags = (a.IsDead ? MoveFlags.Dead : MoveFlags.OnGround) | (a.Warning ? MoveFlags.Sprinting : MoveFlags.None),
        Health = (byte)Math.Clamp((int)MathF.Ceiling((a.IsDead ? a.HarvestFraction : a.HealthFraction) * 255), 0, 255),
        Held = a.Species.Id,
        ActionCount = (byte)(a.AttackCount & 15),
        Action = (ActionKind)(byte)a.LastAttack,
        HeldMods = (byte)(((byte)a.State & 15) << 4 | (a.HurtCount & 15)),
        Aiming = a.Alert && !a.IsDead,
    };

    public static AnimalLook Unpack(in CharacterNetState s) => new(
        (byte)Math.Min(s.Held, (ushort)255),
        (AnimalState)Math.Min(s.HeldMods >> 4, (int)AnimalState.Dead),
        (s.Flags & MoveFlags.Dead) != 0,
        s.Aiming,
        s.Health / 255f,
        (s.Flags & MoveFlags.Sprinting) != 0,
        s.ActionCount,
        (AttackKind)Math.Min((byte)s.Action, (byte)AttackKind.Swipe),
        (byte)(s.HeldMods & 15));
}
