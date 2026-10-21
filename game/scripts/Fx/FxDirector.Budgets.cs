using System;
using System.Collections.Generic;
using Godot;
using Hollow.Client.Effects;
using HollowUniverse.Characters;
using HollowUniverse.Settings;

namespace HollowUniverse.Fx;

public partial class FxDirector
{
    public EffectBudgetSettings BudgetSettings { get; init; } = FxBudgetCatalog.Settings;
    private readonly EffectParticleBudget _particleBudget = new();
    private int? _frameBudgetOverride;
    private readonly EffectLoopBudget _loopBudget = new();
    private readonly List<EffectLoopCandidate> _loopCandidates = new();
    private readonly List<FxLoop> _renderedLoops = new();
    private readonly Stack<MeshInstance3D> _freeFlames = new();
    private readonly Stack<OmniLight3D> _freeLoopLights = new();
    private int _allocatedFlames, _allocatedLoopLights, _activeLoopCount, _loopEmitCursor;

    public int ParticlesThisFrame => _particleBudget.Used;
    public int AllocatedLoopFlames => _allocatedFlames;
    public int AllocatedLoopLights => _allocatedLoopLights;
    public int ActiveLoopFlames => _allocatedFlames - _freeFlames.Count;
    public int ActiveLoopLights => _allocatedLoopLights - _freeLoopLights.Count;
    internal bool LoopIsRendered(FxHandle handle) => _loops.TryGetValue(handle.Id, out FxLoop? loop) && loop.Rendered;
    internal int RenderedOf(string key)
    {
        int count = 0;
        foreach (FxLoop loop in _loops.Values) if (loop.Rendered && loop.Def.Key == key) count++;
        return count;
    }

    private void BeginParticleFrame()
    {
        _particleBudget.Limit = FrameBudget;
        if (!_particleBudget.BeginFrame(Engine.GetProcessFrames())) return;
        foreach (FxBatch batch in _batches.Values) batch.EmittedThisFrame = 0;
    }

    private void UpdateBudgetedLoops(float dt)
    {
        _loopCandidates.Clear();
        _done.Clear();
        foreach (FxLoop loop in _loops.Values)
        {
            if (_time >= loop.StopAt || loop.Params.Follow is { } owner &&
                (!IsInstanceValid(owner) || !owner.IsInsideTree() || owner.IsQueuedForDeletion())) loop.Stopping = true;
            FxLoopDef def = loop.Def.Loop!;
            float rate = 1f / Math.Max(0.05f, loop.Stopping ? def.FadeOut : def.FadeIn);
            loop.Strength = Mathf.MoveToward(loop.Strength, loop.Stopping ? 0 : 1, rate * dt);
            if (loop.Stopping && (loop.Strength <= 0 || !loop.Rendered))
            {
                _done.Add(loop.Id);
                continue;
            }
            if (loop.Intensity <= 0.001f) continue;
            float distance = loop.Params.FirstPerson ? 0 : CameraDistance(loop.Frame().Origin);
            if (Culling && distance > loop.Def.Cull) continue;
            _loopCandidates.Add(new EffectLoopCandidate(loop.Id, loop.Def.Key, Capped ? loop.Def.Cap : int.MaxValue,
                distance, loop.Params.FirstPerson ? 10 : 0, def.Flames.Count > 0, def.LightEnergy > 0,
                loop.Rendered, loop.ActivatedAt));
        }
        foreach (int id in _done)
        {
            FreeLoop(_loops[id]);
            _loops.Remove(id);
        }
        _loopBudget.Select(_loopCandidates, BudgetSettings, _time);
        // Retire old assignments first: acquisition then reuses those slots, even when the pool is full.
        foreach (FxLoop loop in _loops.Values)
            if (loop.Rendered && !_loopBudget.Selected.Contains(loop.Id)) ReleaseLoopResources(loop);
        _renderedLoops.Clear();
        foreach (int id in _loopBudget.Selected)
        {
            FxLoop loop = _loops[id];
            if (!loop.Rendered) AcquireLoopResources(loop);
            _renderedLoops.Add(loop);
        }
        _activeLoopCount = _renderedLoops.Count;
        // Equal active loops share particle starvation over time rather than dictionary order deciding forever.
        if (_activeLoopCount > 0)
        {
            int first = _loopEmitCursor % _activeLoopCount;
            for (int i = 0; i < _activeLoopCount; i++) UpdateLoop(_renderedLoops[(first + i) % _activeLoopCount], dt);
            _loopEmitCursor = (first + 1) % _activeLoopCount;
        }
    }

    private void AcquireLoopResources(FxLoop loop)
    {
        FxLoopDef def = loop.Def.Loop!;
        FxParams p = loop.Params;
        if (def.Flames.Count > 0)
        {
            MeshInstance3D flames;
            if (_freeFlames.Count > 0) flames = _freeFlames.Pop();
            else
            {
                if (_allocatedFlames >= BudgetSettings.LoopFlames) throw new InvalidOperationException("Loop flame admission exceeded its pool.");
                flames = new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, ExtraCullMargin = 3 };
                AddChild(flames);
                _allocatedFlames++;
            }
            flames.Name = $"Flames{loop.Id}";
            flames.Mesh = FxFlameMeshes.For(loop.Def);
            flames.MaterialOverride = FxFlameMeshes.Material(p.FirstPerson);
            flames.Layers = p.FirstPerson ? ViewmodelView.RenderLayer : RenderLayer;
            flames.SetInstanceShaderParameter("fx_speed", def.FlameSpeed);
            flames.SetInstanceShaderParameter("fx_strength", 0f);
            flames.SetInstanceShaderParameter("fx_foot", def.FlameFoot);
            flames.Visible = true;
            loop.Flames = flames;
        }
        if (def.LightEnergy > 0)
        {
            OmniLight3D light;
            if (_freeLoopLights.Count > 0) light = _freeLoopLights.Pop();
            else
            {
                if (_allocatedLoopLights >= BudgetSettings.LoopLights) throw new InvalidOperationException("Loop light admission exceeded its pool.");
                light = new OmniLight3D { OmniAttenuation = 1.4f, DistanceFadeEnabled = true, DistanceFadeBegin = 70, DistanceFadeLength = 30 };
                AddChild(light);
                _allocatedLoopLights++;
            }
            light.Name = $"LoopLight{loop.Id}";
            light.LightColor = def.LightColor;
            light.OmniRange = def.LightRange * p.ScaleOr1;
            light.LightEnergy = 0;
            light.Visible = true;
            light.ShadowEnabled = false;
            if (def.LightShadows) LocalShadowBudget.Track(light, light.OmniRange, p.FirstPerson ? 10 : 0);
            loop.Light = light;
        }
        loop.Rendered = true;
        loop.ActivatedAt = _time;
        loop.RenderStrength = 0;
        loop.HasLastEmit = false;
        Array.Clear(loop.Accumulators);
    }

    private void ReleaseLoopResources(FxLoop loop)
    {
        if (loop.Flames is { } flames)
        {
            flames.Visible = false;
            _freeFlames.Push(flames);
            loop.Flames = null;
        }
        if (loop.Light is { } light)
        {
            LocalShadowBudget.Untrack(light);
            light.Visible = false;
            light.LightEnergy = 0;
            _freeLoopLights.Push(light);
            loop.Light = null;
        }
        loop.Rendered = false;
        loop.RenderStrength = 0;
        loop.HasLastEmit = false;
        Array.Clear(loop.Accumulators);
    }
}
