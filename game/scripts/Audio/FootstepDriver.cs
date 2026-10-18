using Godot;
using Hollow.Client.Audio;

namespace HollowUniverse.Audio;

/// <summary>
/// One character's footsteps (GROUND.md §4.9). The ground mode gives it the character's movement every frame with
/// <see cref="Update"/>; it plays a step each stride (walk, run or crouch by speed and stance), a landing after a fall,
/// swim strokes, on the surface under the feet. The player's own feet play flat and a little quieter
/// (<see cref="Local"/>); everyone else's are placed at their feet.
/// <para>The surface comes from <see cref="Footsteps.FromGround"/> (the terrain's strongest ground material at the
/// feet), <see cref="Footsteps.FromBiome"/> when only the biome is known, or <see cref="Footsteps.FromGrade"/> on a
/// building floor.</para>
/// </summary>
public partial class FootstepDriver : Node3D
{
    private readonly FootstepCadence _cadence = new();

    /// <summary>The player's own feet: flat (not placed), <see cref="LocalVolumeDb"/> quieter.</summary>
    public bool Local { get; set; }

    public float LocalVolumeDb { get; set; } = -3f;

    /// <summary>The director to play through; <see cref="AudioDirector.Instance"/> when not set.</summary>
    public AudioDirector? Director { get; set; }

    /// <summary>The last sound played, for debugging and the viewer.</summary>
    public string LastKey { get; private set; } = "";

    public void Reset()
    {
        _cadence.Reset();
        LastKey = "";
    }

    /// <summary>Call every frame with the character's movement; <paramref name="feet"/> in render space.</summary>
    public void Update(double delta, in FootInput input, Vector3 feet)
    {
        GlobalPosition = feet;
        FootEvent? step = _cadence.Update(delta, input);
        if (step is not FootEvent e)
        {
            return;
        }
        AudioDirector? director = Director ?? AudioDirector.Instance;
        if (director == null)
        {
            return;
        }
        string key = SoundLibrary.Footwear.ResolveEvent(e.Key, input.FootwearProfile, SoundLibrary.Data);
        LastKey = key;
        if (Local)
        {
            director.Play2D(key, (float)e.VolumeDb + LocalVolumeDb);
        }
        else
        {
            director.Play(key, feet, (float)e.VolumeDb);
        }
    }
}
