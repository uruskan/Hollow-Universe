using System;
using Godot;

namespace HollowUniverse.Characters;

/// <summary>How a character stands (GROUND.md §4.8).</summary>
public enum CharacterStance
{
    Stand,
    Crouch,
    Swim,
}

/// <summary>What kind of thing a character holds; picks the upper-body pose and the first-person grip.</summary>
public enum HeldKind
{
    Unarmed,
    Tool,
    Pistol,
    Rifle,
}

/// <summary>One-off things a character does, sent to <see cref="CharacterView.Trigger"/>.</summary>
public enum CharacterEvent
{
    /// <summary>A melee swing or a tool strike (a punch when unarmed).</summary>
    Swing,
    Fire,
    Reload,
    /// <summary>Took a hit; the direction (where the hit travels) picks the flinch.</summary>
    Hit,
    /// <summary>Dies: the body goes limp (ragdoll), pushed along the direction.</summary>
    Death,
}

/// <summary>
/// The state a <see cref="CharacterView"/> draws, given every frame (GROUND.md §4.8). Positions and directions are in
/// the view's parent space (the client's render space); nothing assumes +Y is up.
/// </summary>
public struct CharacterFrame
{
    /// <summary>Feet.</summary>
    public Vector3 Position;

    /// <summary>Away from the planet's centre, unit length.</summary>
    public Vector3 Up;

    /// <summary>
    /// Facing, radians about <see cref="Up"/>: 0 faces north (the parent's −Z, laid flat on the ground plane), growing
    /// anticlockwise seen from above. The same convention as the movement code's view yaw.
    /// </summary>
    public float Yaw;

    /// <summary>View pitch, radians; positive looks up.</summary>
    public float Pitch;

    /// <summary>Metres per second.</summary>
    public Vector3 Velocity;

    public CharacterStance Stance;

    public bool Grounded;

    /// <summary>What the hands hold. <see cref="HeldItem"/> names the model; null shows the kind's default.</summary>
    public HeldKind Held;

    public string? HeldItem;

    public static CharacterFrame Standing(Vector3 position, Vector3 up, float yaw = 0f) => new()
    {
        Position = position,
        Up = up,
        Yaw = yaw,
        Grounded = true,
        Stance = CharacterStance.Stand,
    };

    /// <summary>The facing direction on the ground plane for an up vector and a yaw (north is −Z laid flat).</summary>
    public static Vector3 Facing(Vector3 up, float yaw)
    {
        up = up.Normalized();
        Vector3 north = Vector3.Forward - up * Vector3.Forward.Dot(up);
        if (north.LengthSquared() < 1e-8f)
        {
            // At a pole of the parent frame: fall back to −X ... any horizontal reference works as long as it is steady.
            north = Vector3.Left - up * Vector3.Left.Dot(up);
        }
        north = north.Normalized();
        return north.Rotated(up, yaw);
    }

    /// <summary>A basis with Y along <paramref name="up"/> and −Z along the facing direction.</summary>
    public static Basis BasisOf(Vector3 up, float yaw)
    {
        Vector3 y = up.Normalized();
        Vector3 forward = Facing(y, yaw);
        Vector3 x = forward.Cross(y).Normalized();
        Vector3 z = x.Cross(y).Normalized();
        return new Basis(x, y, z);
    }
}

/// <summary>
/// Parts of the body (art/lib/hollow_art.py REGIONS, same order). Clothing hides the regions it covers fully; the
/// shaders collapse hidden triangles, so skin never pokes through.
/// </summary>
[Flags]
public enum BodyRegions
{
    None = 0,
    Face = 1 << 0,
    Scalp = 1 << 1,
    Neck = 1 << 2,
    Chest = 1 << 3,
    Belly = 1 << 4,
    Pelvis = 1 << 5,
    UpperArm = 1 << 6,
    Forearm = 1 << 7,
    Hand = 1 << 8,
    Thigh = 1 << 9,
    Calf = 1 << 10,
    Ankle = 1 << 11,
    Foot = 1 << 12,
    All = (1 << 13) - 1,

    /// <summary>What the first-person view shows of the body.</summary>
    Arms = UpperArm | Forearm | Hand,
}
