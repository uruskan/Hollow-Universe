using System;
using System.Collections.Generic;
using Godot;

namespace HollowUniverse.Fx;

/// <summary>
/// Short lights for flashes and blasts (GROUND.md §9.3): a pool of shadowless omni lights that rise over an attack time
/// and fall away over the rest of their duration. When all are lit, the dimmest is taken.
/// </summary>
public sealed class FxLights
{
    private sealed class Entry
    {
        public OmniLight3D Light = null!;
        public double Start;
        public float Duration;
        public float Attack;
        public float Energy;
        public Node3D? Follow;
        public Vector3 Offset;
        public bool Active;
    }

    private readonly List<Entry> _pool = new();

    public int Active { get; private set; }

    public FxLights(Node parent, int count)
    {
        for (int i = 0; i < count; i++)
        {
            var light = new OmniLight3D
            {
                Name = $"FxLight{i}",
                Visible = false,
                ShadowEnabled = false,
                OmniAttenuation = 1.6f,
                LightSpecular = 0.4f,
                DistanceFadeEnabled = true,
                DistanceFadeBegin = 60f,
                DistanceFadeLength = 25f,
            };
            parent.AddChild(light);
            _pool.Add(new Entry { Light = light });
        }
    }

    public void Flash(double now, Vector3 position, Color color, float energy, float range, float duration, float attack, Node3D? follow)
    {
        Entry? pick = null;
        float dimmest = float.MaxValue;
        foreach (Entry e in _pool)
        {
            if (!e.Active)
            {
                pick = e;
                break;
            }
            float left = e.Energy * (1f - (float)((now - e.Start) / e.Duration));
            if (left < dimmest)
            {
                dimmest = left;
                pick = e;
            }
        }
        if (pick == null)
        {
            return;
        }
        pick.Start = now;
        pick.Duration = Math.Max(0.01f, duration);
        pick.Attack = Math.Clamp(attack, 0f, pick.Duration * 0.5f);
        pick.Energy = energy;
        pick.Follow = follow;
        pick.Offset = follow != null && GodotObject.IsInstanceValid(follow) ? position - follow.GlobalPosition : Vector3.Zero;
        pick.Light.GlobalPosition = position;
        pick.Light.LightColor = color;
        pick.Light.OmniRange = range;
        pick.Light.LightEnergy = attack > 0 ? 0 : energy * FxDirector.Brightness;
        pick.Light.Visible = true;
        pick.Active = true;
    }

    public void Update(double now)
    {
        int active = 0;
        foreach (Entry e in _pool)
        {
            if (!e.Active)
            {
                continue;
            }
            float t = (float)(now - e.Start);
            if (t >= e.Duration)
            {
                e.Active = false;
                e.Light.Visible = false;
                continue;
            }
            active++;
            float k;
            if (t < e.Attack)
            {
                k = t / e.Attack;
            }
            else
            {
                // A fast fall that eases into nothing.
                float u = (t - e.Attack) / Math.Max(1e-4f, e.Duration - e.Attack);
                k = (1f - u) * (1f - u) * (1f - 0.35f * u);
            }
            e.Light.LightEnergy = e.Energy * k * FxDirector.Brightness;
            if (e.Follow != null && GodotObject.IsInstanceValid(e.Follow))
            {
                e.Light.GlobalPosition = e.Follow.GlobalPosition + e.Offset;
            }
        }
        Active = active;
    }
}
