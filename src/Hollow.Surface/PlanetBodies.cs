using System;
using System.Linq;
using Hollow.Galaxy;
using Hollow.Galaxy.Systems;

namespace Hollow.Surface;

/// <summary>
/// Resolves a planet or moon's physical metadata without generating either kind of terrain. Results, including
/// missing addresses, are not retained here. The default universe only keeps the galaxy's existing bounded cell cache.
/// </summary>
public static class PlanetBodies
{
    public sealed record Entry(StarSystem System, Body Body);

    private static readonly Lazy<Galaxy.Galaxy> Universe = new(() => new Galaxy.Galaxy(null));

    internal static Galaxy.Galaxy DefaultUniverse => Universe.Value;

    /// <summary>The default universe's body, without depending on an optional star catalogue.</summary>
    public static Entry? Find(ulong systemAddress, uint bodyPath) => Find(DefaultUniverse, systemAddress, bodyPath);

    /// <summary>
    /// Resolves in an explicit universe without retaining a cache per seed. Repeated custom-universe queries can
    /// use the overload accepting a caller-owned galaxy instead.
    /// </summary>
    public static Entry? Find(ulong systemAddress, uint bodyPath, ulong universeSeed) =>
        Find(universeSeed == Galaxy.Galaxy.DefaultSeed ? DefaultUniverse : new Galaxy.Galaxy(null, universeSeed),
            systemAddress, bodyPath);

    /// <summary>
    /// Resolves through a caller-owned universe. Untrusted or missing addresses and bodies without planet state
    /// return null. Built-in Sol is available even without a catalogue, as in the established surface resolver.
    /// </summary>
    public static Entry? Find(Galaxy.Galaxy universe, ulong systemAddress, uint bodyPath)
    {
        ArgumentNullException.ThrowIfNull(universe);
        StarSystem? system;
        try
        {
            var address = new SystemAddress(systemAddress);
            system = address.Equals(SolSystem.Address) ? SolSystem.Create(SolSystem.Primary) : universe.GetSystem(address);
        }
        catch (ArgumentException)
        {
            return null;
        }
        Body? body = system?.Bodies.FirstOrDefault(b => b.Path == bodyPath);
        return system != null && body?.State != null ? new Entry(system, body) : null;
    }
}
