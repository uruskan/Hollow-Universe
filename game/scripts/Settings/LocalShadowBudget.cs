using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;
using Hollow.Client.Rendering;

namespace HollowUniverse.Settings;

/// <summary>
/// One registry for shadow-capable local lights from any presentation system. Owners register once (or update a
/// pooled light's range/priority); the current camera gets a bounded set of shadows. Illumination is never disabled.
/// </summary>
public static class LocalShadowBudget
{
    private sealed class Entry(Light3D light, float range, int priority)
    {
        public readonly Light3D Light = light;
        public float Range = range;
        public int Priority = priority;
        public bool Selected;
        public Action? OnExit;
    }

    private static readonly Dictionary<ulong, Entry> Lights = new();
    private static readonly List<LocalShadowCandidate> Candidates = new();
    private static readonly List<ulong> Expired = new();
    private static readonly List<Entry> VisibleViewport = new();

    public static int RegisteredCount => Lights.Count;
    public static int ShadowedCount { get; private set; }
    public static int CandidateCount => Candidates.Count;

    /// <summary>
    /// Registers a local light that is allowed to cast shadows. Larger priorities win before proximity (for example,
    /// the player's held light). Re-registering updates a pooled light without allocating or changing its shadow state.
    /// Call on the main thread; directional lights use their independent cascade settings.
    /// </summary>
    public static void Track(Light3D light, float range, int priority = 0)
    {
        if (light is DirectionalLight3D) throw new ArgumentException("Register only local lights", nameof(light));
        if (!float.IsFinite(range) || range <= 0) throw new ArgumentOutOfRangeException(nameof(range));
        ulong id = light.GetInstanceId();
        if (Lights.TryGetValue(id, out Entry? existing))
        {
            existing.Range = range;
            existing.Priority = priority;
            return;
        }
        light.ShadowEnabled = false;
        var entry = new Entry(light, range, priority) { OnExit = () => Untrack(light) };
        Lights.Add(id, entry);
        light.TreeExiting += entry.OnExit;
    }

    /// <summary>Withdraws shadow eligibility without removing the light or changing its illumination.</summary>
    public static void Untrack(Light3D light)
    {
        if (!GodotObject.IsInstanceValid(light)) return;
        if (Lights.Remove(light.GetInstanceId(), out Entry? entry))
        {
            if (entry.OnExit != null) light.TreeExiting -= entry.OnExit;
            if (entry.Selected) ShadowedCount = Math.Max(0, ShadowedCount - 1);
        }
        light.ShadowEnabled = false;
    }

    /// <summary>One shared pass per frame, after owners move their lights. Buffers retain capacity between frames.</summary>
    public static void Update(Viewport viewport)
    {
        GraphicsValues settings = GraphicsSettings.Current;
        int maximum = settings.Shadows > 0 ? settings.MaxShadowLights : 0;
        Camera3D? camera = viewport.GetCamera3D();
        Candidates.Clear();
        Expired.Clear();
        VisibleViewport.Clear();
        foreach ((ulong id, Entry entry) in Lights)
        {
            Light3D light = entry.Light;
            if (!GodotObject.IsInstanceValid(light)) { Expired.Add(id); continue; }
            if (!light.IsInsideTree() || light.GetViewport() != viewport) continue;
            VisibleViewport.Add(entry);
            if (maximum <= 0 || camera == null || !light.IsVisibleInTree() ||
                light.LightEnergy <= 0.001f || light.GetWorld3D() != camera.GetWorld3D()) continue;
            Candidates.Add(new LocalShadowCandidate(id, camera.GlobalPosition.DistanceTo(light.GlobalPosition),
                entry.Range, entry.Priority, entry.Selected));
        }
        foreach (ulong id in Expired) Lights.Remove(id);
        Span<ulong> selected = stackalloc ulong[LocalShadowSelector.Capacity];
        int count = LocalShadowSelector.Select(CollectionsMarshal.AsSpan(Candidates), selected, maximum, settings.LocalShadowDistance);
        foreach (Entry entry in VisibleViewport)
        {
            bool enabled = selected[..count].Contains(entry.Light.GetInstanceId());
            if (entry.Light.ShadowEnabled != enabled) entry.Light.ShadowEnabled = enabled;
            entry.Selected = enabled;
        }
        ShadowedCount = 0;
        foreach (Entry entry in Lights.Values) if (entry.Selected) ShadowedCount++;
    }

    public static void Clear()
    {
        foreach (Entry entry in Lights.Values)
        {
            if (!GodotObject.IsInstanceValid(entry.Light)) continue;
            if (entry.OnExit != null) entry.Light.TreeExiting -= entry.OnExit;
            entry.Light.ShadowEnabled = false;
        }
        Lights.Clear();
        Candidates.Clear();
        Expired.Clear();
        VisibleViewport.Clear();
        ShadowedCount = 0;
    }
}
