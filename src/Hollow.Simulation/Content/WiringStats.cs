using System;
using System.Text.Json;
using Hollow.Simulation.Content.Readers;

namespace Hollow.Simulation.Content;

/// <summary>Shared held wiring behavior. Endpoint cable span belongs to the electrical graph.</summary>
public sealed class WiringStats
{
    public float Reach { get; init; } = 4;
    public int MaxAnchors { get; init; } = 32;
    public int DraftSeconds { get; init; } = 120;

    internal static WiringStats Read(JsonFields f, JsonElement e, string where)
    {
        f.OnlyKnown(e, where, "reach", "maxAnchors", "draftSeconds");
        float reach = f.Number(e, "reach", where, 4);
        int anchors = f.Integer(e, "maxAnchors", where, 32), seconds = f.Integer(e, "draftSeconds", where, 120);
        if (!float.IsFinite(reach) || reach is <= 0 or > 8 || anchors is < 0 or > 32 || seconds is < 1 or > 300)
            f.Problem(where, "reach must be 0–8 m, anchors 0–32, draftSeconds 1–300");
        return new WiringStats { Reach = reach, MaxAnchors = anchors, DraftSeconds = seconds };
    }
}
