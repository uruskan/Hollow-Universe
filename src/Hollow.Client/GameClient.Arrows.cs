using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Net.Protocol;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Targets;

namespace Hollow.Client;

/// <summary>
/// A spent arrow as this client knows it (GROUND_SYSTEMS.md §5): what it is, what holds it and its tip's place there, and
/// where it is drawn now (its holder's pose this frame: it rides a moving body, swings with a door's leaf).
/// </summary>
public sealed class RemoteArrow
{
    internal RemoteArrow(EntityId id)
    {
        Id = id;
    }

    public EntityId Id { get; }

    public ItemDef Item { get; internal set; } = null!;

    /// <summary>What holds it and where its tip is there (the shaft along its +X).</summary>
    public SupportLink Link { get; internal set; }

    /// <summary>Where it is now, in the zone frame (its tip; the shaft along its +X): the holder's pose this frame.</summary>
    public TargetPose Pose { get; internal set; }

    /// <summary>Its holder is known here this frame, so it has a pose (one in this player's own body has none: never drawn).</summary>
    public bool Placed { get; internal set; }

    /// <summary>What holds it: its family and ID.</summary>
    public TargetRef Holder => Link.Support;
}

// Spent arrows in the client (GROUND_SYSTEMS.md §5): those within range as the server tells them, each placed every frame
// where its holder is now, for drawing and for the interact key.
public sealed partial class GameClient
{
    private readonly Dictionary<EntityId, RemoteArrow> _arrows = new();

    /// <summary>The spent arrows in range.</summary>
    public IReadOnlyCollection<RemoteArrow> Arrows => _arrows.Values;

    public RemoteArrow? ArrowOf(EntityId id) => _arrows.GetValueOrDefault(id);

    // Every frame: each arrow where its holder is now (none while its holder isn't known here).
    private void UpdateArrows()
    {
        foreach (RemoteArrow arrow in _arrows.Values)
        {
            arrow.Placed = TryPose(arrow.Link, out TargetPose pose);
            arrow.Pose = arrow.Placed ? pose : arrow.Pose;
        }
    }

    private bool ReceiveArrows(uint type)
    {
        switch (type)
        {
            case ArrowStates.TypeId:
            {
                ArrowStates m = ArrowStates.Read(_reader);
                if (_reader.Failed)
                {
                    return true;
                }
                foreach (ArrowNet a in m.Arrows ?? Array.Empty<ArrowNet>())
                {
                    if (Content.Item((int)Math.Min(a.Item, ushort.MaxValue)) is not { } item || !float.IsFinite(a.X) || !float.IsFinite(a.Y) || !float.IsFinite(a.Z))
                    {
                        continue;
                    }
                    var id = new EntityId(a.Id);
                    if (!_arrows.TryGetValue(id, out RemoteArrow? arrow))
                    {
                        arrow = new RemoteArrow(id);
                        _arrows.Add(id, arrow);
                    }
                    var turn = new Quaternion(a.RotationX, a.RotationY, a.RotationZ, a.RotationW);
                    arrow.Item = item;
                    arrow.Link = new SupportLink(NetConvert.ToRef(a.Support), new Vector3(a.X, a.Y, a.Z),
                        turn.LengthSquared() > 1e-6f ? Quaternion.Normalize(turn) : Quaternion.Identity);
                    arrow.Placed = TryPose(arrow.Link, out TargetPose pose);
                    arrow.Pose = pose;
                }
                return true;
            }
            case ArrowsGone.TypeId:
            {
                ArrowsGone m = ArrowsGone.Read(_reader);
                if (!_reader.Failed)
                {
                    foreach (BuildIdNet gone in m.Ids ?? Array.Empty<BuildIdNet>())
                    {
                        _arrows.Remove(new EntityId(gone.Id));
                    }
                }
                return true;
            }
            default:
                return false;
        }
    }
}
