using System;
using System.Text.Json;
using Hollow.Simulation.Content.Readers;

namespace Hollow.Simulation.Content;

/// <summary>Damage on physical contact with an assembly's authored hulls, independent of item names.</summary>
public sealed class ContactHazardStats
{
    public float Damage { get; init; } = 5;
    public float Interval { get; init; } = 1;
    public float Range { get; init; } = 0.14f;
    public float Bleed { get; init; } = 0.1f;

    internal static ContactHazardStats Read(ContentReading r, JsonElement e, string where)
    {
        JsonFields f = r.Fields;
        f.OnlyKnown(e, where, "damage", "interval", "range", "bleed");
        return new ContactHazardStats
        {
            Damage = f.Number(e, "damage", where, 5), Interval = f.Number(e, "interval", where, 1),
            Range = f.Number(e, "range", where, 0.14f), Bleed = f.Number(e, "bleed", where, 0.1f),
        };
    }

    internal static void Check(ContentChecking c, DeployableDef def)
    {
        if (def.ContactHazard is not { } h) return;
        if (!float.IsFinite(h.Damage + h.Interval + h.Range + h.Bleed) || h.Damage is <= 0 or > 100 ||
            h.Interval is < 0.1f or > 60 || h.Range is < 0 or > 0.5f || h.Bleed is < 0 or > 1 || string.IsNullOrWhiteSpace(def.Assembly))
            c.Problem($"deployables.json: \"{def.Key}\".contactHazard", "requires an authored assembly, damage 0–100, interval 0.1–60 s, range 0–0.5 m, bleed 0–1");
    }
}
