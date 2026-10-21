using System;
using Hollow.Net.Transport;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Physics;
using Hollow.Surface;

namespace Hollow.Client;

/// <summary>How a client runs.</summary>
public sealed record ClientOptions
{
    /// <summary>Session environment owner shared by local clients; null gives this client its own bounded store.</summary>
    public WorldStore? WorldStore { get; init; }
    public string Name { get; init; } = "Player";

    /// <summary>
    /// Keep transport receive/send and keepalives running during graphical frame stalls. Gameplay callbacks still
    /// run only when Update polls on its caller's thread. False keeps bots/tests on their existing manual transport.
    /// </summary>
    public bool BackgroundTransport { get; init; }

    /// <summary>The body the player chose (0 male, 1 female; GROUND.md §4.8), sent at connect.</summary>
    public byte Body { get; init; }

    /// <summary>Bad-network simulation (through a local relay), or none.</summary>
    public NetConditions Conditions { get; init; } = NetConditions.None;

    /// <summary>A relay shared by many clients in one process (bots), instead of one of its own.</summary>
    public ConditionedRelay? SharedRelay { get; init; }

    /// <summary>Ground shared by many clients in one process (bots); the owner releases unused tiles.</summary>
    public Func<ZoneSpec, ISurface, PhysicsWorld>? SharedPhysics { get; init; }

    /// <summary>
    /// The natural objects' state shared by many clients with <see cref="SharedPhysics"/> (bots): its owner keeps the
    /// shared ground's trees in step with it. Null: an own one on the client's own ground.
    /// </summary>
    public Func<ZoneSpec, ISurface, Simulation.Nature.NaturalState>? SharedNature { get; init; }

    /// <summary>
    /// The replicated building shared by many clients with <see cref="SharedPhysics"/> (bots), on that physics world.
    /// Null: an own one on the client's own ground.
    /// </summary>
    public Func<ZoneSpec, ISurface, PhysicsWorld, BuildingReplica>? SharedBuilding { get; init; }

    /// <summary>The game's data (the server must have the same); null finds and loads game/data/ground.</summary>
    public GameContent? Content { get; init; }

    public Action<string> Log { get; init; } = _ => { };
}

