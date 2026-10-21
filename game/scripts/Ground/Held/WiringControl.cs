using System;
using System.Collections.Generic;
using Godot;
using Hollow.Client;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Electricity;
using Hollow.Simulation.Targets;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

/// <summary>One controller for every data-declared wiring tool; all committed changes remain server authoritative.</summary>
public sealed class WiringControl : IHeldControl
{
    private LocalPlayer? _player;
    private uint _pending;
    private Action? _accept;
    private Num.Vector3? _source;
    private readonly List<Num.Vector3> _anchors = new();
    private RemoteThing? _target;
    private PowerPort? _port;
    private Num.Vector3 _point;
    private AimHit _surface = AimHit.None;
    private WiringPreview? _preview;

    public void Aim(HeldContext ctx, Num.Vector3 eye, Num.Vector3 forward)
    {
        if (_player != ctx.Me)
        {
            if (_player != null) _player.IntentAnswered -= Answer;
            _player = ctx.Me;
            _player.IntentAnswered += Answer;
        }
        float reach = ctx.Item.Wiring!.Reach;
        _surface = Targeting.Aim(ctx.Client.Targets().TargetViews, new AimRay(eye, forward, reach, ctx.Client.EntityId.Value));
        _point = _surface.Hit ? _surface.Point : eye + forward * reach;
        _target = null; _port = null;
        float best = reach;
        foreach (RemoteThing thing in ctx.Client.Survival().Things)
        {
            if (thing.Deployable(ctx.Content)?.Power is not { } power || Num.Vector3.DistanceSquared(thing.Position, eye) > 225) continue;
            foreach (PowerPort port in power.Ports)
            {
                Num.Vector3 at = PowerPorts.Position(thing.Position, thing.Up, thing.Yaw, port);
                float along = PowerPorts.Ray(eye, forward, best, thing.Position, thing.Up, thing.Yaw, port);
                if (along < 0 || along > best) continue;
                if (_surface.Hit && _surface.Target.Id != thing.Id.Value && _surface.Distance + .1f < along) continue;
                _target = thing; _port = port; _point = at; best = along;
            }
        }
        if (_preview == null || !GodotObject.IsInstanceValid(_preview))
        {
            _preview = new WiringPreview { Client = ctx.Client, Inactive = Reset, Released = Release };
            ctx.Arms.GetParent().AddChild(_preview);
        }
        _preview.Draw(_source, _anchors, _point, ctx.Client.Frame.UpAt(_point));
    }

    public void Hands(HeldContext ctx, ref InputFrame input, in HandButtons buttons)
    {
        if (buttons.AimClicked)
        {
            ctx.Me.Wire(WireOperation.Cancel);
            _pending = 0; _accept = null; _source = null; _anchors.Clear();
            ctx.Say("Wiring cancelled");
            return;
        }
        if (_pending != 0) return;
        if (buttons.Reload && _target != null && _port != null)
        {
            _pending = ctx.Me.Wire(WireOperation.Disconnect, new TargetRef(TargetFamily.Thing, _target.Id.Value), _port.Id, _point);
            _accept = () => ctx.Say("Cable disconnected");
            return;
        }
        if (!buttons.Clicked) return;
        if (_target != null && _port != null)
        {
            Num.Vector3 clicked = _point;
            WireOperation op = _source == null ? WireOperation.Begin : WireOperation.Complete;
            _pending = ctx.Me.Wire(op, new TargetRef(TargetFamily.Thing, _target.Id.Value), _port.Id, clicked);
            _accept = () =>
            {
                if (op == WireOperation.Begin)
                {
                    _source = clicked; _anchors.Clear();
                    ctx.Say("Output selected · 20 m endpoint range · click surfaces or an input");
                }
                else { _source = null; _anchors.Clear(); ctx.Say("Cable connected"); }
            };
        }
        else if (_source != null && _surface.Hit)
        {
            Num.Vector3 clicked = _surface.Point;
            _pending = ctx.Me.Wire(WireOperation.Anchor, _surface.Target, point: clicked);
            _accept = () => { _anchors.Add(clicked); ctx.Say("Routing point added · right-click to cancel"); };
        }
        else ctx.Say("Aim at an electrical output cube");
    }

    private void Answer(IntentAnswer answer)
    {
        if (answer.Seq != _pending) return;
        Action? accepted = _accept;
        _pending = 0; _accept = null;
        if (answer.Done) accepted?.Invoke();
        else if (answer.Code is OutcomeCode.Stale or OutcomeCode.NoTool or OutcomeCode.NotFound)
        { _source = null; _anchors.Clear(); }
    }

    private void Reset() { _pending = 0; _accept = null; _source = null; _anchors.Clear(); }
    private void Release()
    {
        if (_player != null) _player.IntentAnswered -= Answer;
        _player = null; Reset();
    }
}

public partial class WiringPreview : MeshInstance3D
{
    public GameClient Client { get; set; } = null!;
    public Action? Inactive { get; set; }
    public Action? Released { get; set; }
    private readonly ImmediateMesh _lines = new();
    private readonly StandardMaterial3D _valid = new() { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(.1f,.9f,.7f) };
    private readonly StandardMaterial3D _invalid = new() { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1,.25f,.12f) };
    public override void _Ready() { Mesh = _lines; TopLevel = true; CastShadow = ShadowCastingSetting.Off; }
    public override void _Process(double delta)
    {
        Visible = Client.Local.HeldItem?.Held == HeldUse.Wiring && !Client.Local.IsDead;
        if (!Visible) Inactive?.Invoke();
    }
    public override void _ExitTree() => Released?.Invoke();
    public void Draw(Num.Vector3? source, List<Num.Vector3> anchors, Num.Vector3 end, Num.Vector3 up)
    {
        _lines.ClearSurfaces();
        if (source is not { } start) return;
        bool valid = Num.Vector3.DistanceSquared(start, end) <= 400;
        _lines.SurfaceBegin(Mesh.PrimitiveType.Lines, valid ? _valid : _invalid);
        Vector3 previous = GroundView.ToGodot(start);
        foreach (Num.Vector3 anchor in anchors)
        { Vector3 next = GroundView.ToGodot(anchor); _lines.SurfaceAddVertex(previous); _lines.SurfaceAddVertex(next); previous = next; }
        _lines.SurfaceAddVertex(previous); _lines.SurfaceAddVertex(GroundView.ToGodot(end));
        // A ground-plane range ring is presentation only; actual validation uses 3D port distance.
        Basis basis = NatureView.Upright(up, 0);
        for (int i = 0; i < 64; i++)
        {
            float a = i * Mathf.Tau / 64, b = (i + 1) * Mathf.Tau / 64;
            _lines.SurfaceAddVertex(GroundView.ToGodot(start) + basis * new Vector3(Mathf.Cos(a)*20,0,Mathf.Sin(a)*20));
            _lines.SurfaceAddVertex(GroundView.ToGodot(start) + basis * new Vector3(Mathf.Cos(b)*20,0,Mathf.Sin(b)*20));
        }
        _lines.SurfaceEnd();
    }
}
