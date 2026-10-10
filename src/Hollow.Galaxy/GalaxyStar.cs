using Hollow.Core;

namespace Hollow.Galaxy;

/// <summary>
/// The primary star of one star system, as the galaxy knows it: enough to draw it on the map and to
/// generate its system. Procedural stars are computed on demand; catalogue stars come from real data.
/// </summary>
public readonly struct GalaxyStar
{
    public readonly SystemAddress Address;

    /// <summary>Position in the galactic frame (light-years from Sol).</summary>
    public readonly Vector3D Position;

    public readonly StarClass Class;
    public readonly StarPhysics Physics;

    /// <summary>[Fe/H] metallicity.</summary>
    public readonly float Metallicity;

    public readonly GalaxyComponent Component;

    /// <summary>Level of the galaxy octree this star lives in (by its absolute magnitude).</summary>
    public readonly byte Level;

    public GalaxyStar(SystemAddress address, Vector3D position, StarClass starClass, StarPhysics physics,
        float metallicity, GalaxyComponent component, int level)
    {
        Address = address;
        Position = position;
        Class = starClass;
        Physics = physics;
        Metallicity = metallicity;
        Component = component;
        Level = (byte)level;
    }

    public bool IsCatalogue => Address.IsCatalogue;

    public override string ToString() => $"{Address} {Class} at {Position}";
}
