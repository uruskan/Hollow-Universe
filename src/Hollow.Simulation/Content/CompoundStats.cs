using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Hollow.Simulation.Content.Readers;

namespace Hollow.Simulation.Content;

public readonly record struct CompoundGatePart(string Part, float OpenYaw);
public sealed record CompoundLockSocket(string Part, string Name);

/// <summary>Articulated assembly parts; angles are degrees about each authored part's Y-axis pivot.</summary>
public sealed class CompoundGateStats
{
    public float Seconds { get; init; } = 1;
    public IReadOnlyList<CompoundGatePart> Parts { get; init; } = Array.Empty<CompoundGatePart>();
    public CompoundLockSocket? LockSocket { get; init; }

    internal static CompoundGateStats Read(ContentReading r, JsonElement e, string where)
    {
        JsonFields f = r.Fields;
        f.OnlyKnown(e, where, "seconds", "parts", "lockSocket");
        var parts = new List<CompoundGatePart>();
        foreach (JsonElement p in f.List(e, "parts", where))
        {
            f.OnlyKnown(p, where + ".parts", "part", "openYaw");
            parts.Add(new CompoundGatePart(f.Text(p, "part", where), f.Number(p, "openYaw", where)));
        }
        CompoundLockSocket? socket = null;
        if (f.TryChild(e, "lockSocket", out JsonElement mount))
        {
            f.OnlyKnown(mount, where + ".lockSocket", "part", "name");
            socket = new CompoundLockSocket(f.Text(mount, "part", where), f.Text(mount, "name", where));
        }
        return new CompoundGateStats { Seconds = f.Number(e, "seconds", where, 1), Parts = parts, LockSocket = socket };
    }

    internal static void Check(ContentChecking c, DeployableDef def)
    {
        if (def.Gate is not { } g) return;
        string where = $"deployables.json: \"{def.Key}\".gate";
        CompoundAssembly? assembly = c.Loaded.Compound?.Find(def.Assembly);
        if (assembly == null || g.Parts.Count == 0 || !float.IsFinite(g.Seconds) || g.Seconds is <= 0 or > 30)
            c.Problem(where, "needs an assembly, moving parts and a duration of 0–30 seconds");
        c.Duplicates(where, g.Parts.Select(p => p.Part));
        foreach (CompoundGatePart p in g.Parts)
            if (assembly?.Part(p.Part) == null || !float.IsFinite(p.OpenYaw) || p.OpenYaw == 0 || Math.Abs(p.OpenYaw) > 180)
                c.Problem(where, $"part '{p.Part}' must exist with a nonzero openYaw within ±180 degrees");
        if (g.LockSocket is { } socket && (assembly == null ||
            !g.Parts.Any(p => p.Part == socket.Part) ||
            !assembly.Sockets.Any(s => s.Part == socket.Part && s.Name == socket.Name)))
            c.Problem(where + ".lockSocket", "must name a socket on one of the gate's moving parts");
    }
}

/// <summary>The climbable span and movement rates in the deployable's local frame.</summary>
public sealed class CompoundClimbStats
{
    public Vector3 Bottom { get; init; }
    public Vector3 Top { get; init; }
    public float Width { get; init; } = 0.6f;
    public float Speed { get; init; } = 2;

    internal static CompoundClimbStats Read(ContentReading r, JsonElement e, string where)
    {
        JsonFields f = r.Fields;
        f.OnlyKnown(e, where, "bottom", "top", "width", "speed");
        return new CompoundClimbStats { Bottom = f.Vector(e, "bottom", where), Top = f.Vector(e, "top", where),
            Width = f.Number(e, "width", where, 0.6f), Speed = f.Number(e, "speed", where, 2) };
    }

    internal static void Check(ContentChecking c, DeployableDef def)
    {
        if (def.Climb is not { } climb) return;
        float height = Vector3.Distance(climb.Bottom, climb.Top);
        if (!float.IsFinite(height + climb.Width + climb.Speed) || height is < 0.5f or > 30 || climb.Width is <= 0 or > 4 || climb.Speed is <= 0 or > 10)
            c.Problem($"deployables.json: \"{def.Key}\".climb", "needs a finite span 0.5–30 m, width 0–4 m and speed 0–10 m/s");
    }
}
