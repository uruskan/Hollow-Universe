using System;
using System.Collections.Generic;
using Godot;
using Hollow.Client.Effects;
using Hollow.Simulation;
using Hollow.Simulation.Ships;
using HollowUniverse.Fx;
using HollowUniverse.Ships;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

/// <summary>
/// A ground vehicle's own effects (VEHICLES.md §9.9): its diesel's haze at idle, black soot under load and a puff on
/// starting, at a gear change and on a stab of the throttle, out of every <c>Exhaust_&lt;n&gt;</c> point of its model
/// (<see cref="VehicleSmoke"/>); dust behind each tyre on the ground, tinted by the ground under it, with grit and gravel,
/// by its speed and its spin pulling away; and spray where a tyre runs through shallow water. Drawn within
/// <see cref="VehicleFxReach"/> of the camera only.
/// </summary>
public partial class ShipsView
{
    private sealed class VehicleFx
    {
        public readonly VehicleSmoke Smoke = new();
        public FxHandle[] Haze = Array.Empty<FxHandle>(), Soot = Array.Empty<FxHandle>();
        public FxHandle[] Dust = Array.Empty<FxHandle>(), Spray = Array.Empty<FxHandle>();
        public string?[] Surface = Array.Empty<string?>();
        public float SurfaceAge;
    }

    /// <summary>A vehicle's effects are drawn within this distance of the camera (metres).</summary>
    private const float VehicleFxReach = 220f;

    /// <summary>A tyre's dust is full at this speed (m/s); none under <see cref="DustFrom"/>.</summary>
    private const float DustFull = 14f, DustFrom = 1.2f;

    /// <summary>Under this speed (m/s), a hard throttle spins the tyres and throws dust as they pull away.</summary>
    private const float SpinBelow = 6f;

    /// <summary>The ground under each tyre is looked at again after this many seconds (its dust's tint).</summary>
    private const float SurfaceEvery = 0.5f;

    /// <summary>The tyre of the TOGG, the radius its dust is made for (a bigger tyre's is scaled up).</summary>
    private const float DustTyre = 0.48f;

    // Every frame for a vehicle on wheels: its throttle is its thrust along its nose (GroundMotor), its load the throttle
    // until the shared revs model gives one (and its gear changes).
    private void VehicleEffects(Drawn d, ShipDef def, in ShipLook look, Num.Vector3 velocity, bool shown)
    {
        if (def.Ground == null)
        {
            return;
        }
        VehicleFx fx = d.Vehicle ??= new VehicleFx();
        float throttle = look.Thrust.Z;
        fx.Smoke.Update(look.Engines, throttle, MathF.Abs(throttle), shifted: false, (float)_delta);
        Camera3D? camera = GetViewport()?.GetCamera3D();
        bool near = shown && (camera == null || camera.GlobalPosition.DistanceTo(d.View.GlobalPosition) < VehicleFxReach);
        Exhaust(fx, d.View, near);
        Tyres(fx, d.View, def, velocity, throttle, near);
    }

    private static void Exhaust(VehicleFx fx, ShipView view, bool near)
    {
        IReadOnlyList<Node3D> stacks = view.Exhausts;
        if (fx.Haze.Length != stacks.Count)
        {
            StopAll(fx.Haze);
            StopAll(fx.Soot);
            fx.Haze = new FxHandle[stacks.Count];
            fx.Soot = new FxHandle[stacks.Count];
        }
        VehicleSmoke smoke = fx.Smoke;
        for (int i = 0; i < stacks.Count; i++)
        {
            Node3D at = stacks[i];
            Loop(ref fx.Haze[i], "exhaust.diesel", near && smoke.Haze > 0.02f, at.GlobalPosition, at.GlobalBasis.Y, smoke.Haze, new FxParams { Follow = at });
            Loop(ref fx.Soot[i], "exhaust.soot", near && smoke.Soot > 0.03f, at.GlobalPosition, at.GlobalBasis.Y, smoke.Soot, new FxParams { Follow = at });
            if (near && smoke.Puff > 0)
            {
                FxDirector.Play("exhaust.puff", at.GlobalPosition, at.GlobalBasis.Y.Normalized(), new FxParams { Scale = smoke.Puff });
            }
        }
    }

    private void Tyres(VehicleFx fx, ShipView view, ShipDef def, Num.Vector3 velocity, float throttle, bool near)
    {
        IReadOnlyList<WheelDef> wheels = def.Wheels;
        if (fx.Dust.Length != wheels.Count)
        {
            StopAll(fx.Dust);
            StopAll(fx.Spray);
            fx.Dust = new FxHandle[wheels.Count];
            fx.Spray = new FxHandle[wheels.Count];
            fx.Surface = new string?[wheels.Count];
        }
        GroundProbe? probe = Client.Surface is { } world ? new GroundProbe(world, Client.Frame) : null;
        fx.SurfaceAge += (float)_delta;
        bool lookAgain = fx.SurfaceAge >= SurfaceEvery;
        if (lookAgain)
        {
            fx.SurfaceAge = 0;
        }
        float speed = velocity.Length();
        float spin = speed < SpinBelow ? Math.Clamp(MathF.Abs(throttle) - 0.5f, 0f, 0.5f) * (1f - speed / SpinBelow) : 0f;
        float k = MathF.Min(1f, Math.Clamp((speed - DustFrom) / DustFull, 0f, 1f) + spin);
        Transform3D pose = view.GlobalTransform;
        Vector3 nose = pose.Basis.Z.Normalized();
        for (int i = 0; i < wheels.Count; i++)
        {
            WheelDef w = wheels[i];
            bool dust = false, spray = false;
            Vector3 dustAt = default, sprayAt = default, behind = default, up = default;
            if (near && probe is { } ground && k > 0.02f)
            {
                Vector3 hubAt = pose * new Vector3(w.Hub.X, w.Hub.Y, w.Hub.Z);
                var hub = new Num.Vector3(hubAt.X, hubAt.Y, hubAt.Z);
                float height = ground.HeightAbove(hub);
                Num.Vector3 upN = Client.Frame.UpAt(hub);
                up = GroundView.ToGodot(upN);
                Num.Vector3 contact = hub - upN * MathF.Max(height, 0);
                // Thrown back from the tyre (along its way, or off its nose spinning on the spot), along the ground and a little up.
                Vector3 back = speed > 0.5f ? -GroundView.ToGodot(velocity) / speed : -nose;
                back -= up * back.Dot(up);
                behind = (back.LengthSquared() > 1e-6f ? back.Normalized() : -nose) + up * 0.35f;
                behind = behind.Normalized();
                float depth = -ground.HeightAboveWater(contact);
                bool touching = height < w.Radius + w.Travel * 0.5f + 0.15f;
                if (touching && depth > 0.02f && depth < w.Radius * 1.5f)
                {
                    spray = true;
                    sprayAt = GroundView.ToGodot(contact + upN * depth);
                }
                else if (touching && depth <= 0.02f)
                {
                    dust = true;
                    dustAt = GroundView.ToGodot(contact);
                    if (lookAgain || fx.Surface[i] == null)
                    {
                        string surface = GroundAudio.PhysicalSurfaceAt(Client, contact);
                        if (surface != fx.Surface[i])
                        {
                            fx.Surface[i] = surface;
                            FxDirector.SetTint(fx.Dust[i], ContactFeedback.Dust(surface));
                        }
                    }
                }
            }
            float scale = Math.Clamp(w.Radius / DustTyre, 0.7f, 2f);
            Color tint = fx.Surface[i] is { } s ? ContactFeedback.Dust(s) : Colors.White;
            Loop(ref fx.Dust[i], "dust.tyre", dust, dustAt, up, k, new FxParams { Tint = tint, Scale = scale, Direction = behind });
            Loop(ref fx.Spray[i], "spray.tyre", spray, sprayAt, up, k, new FxParams { Scale = scale, Direction = behind });
            if (dust)
            {
                FxDirector.Move(fx.Dust[i], dustAt);
                FxDirector.SetDirection(fx.Dust[i], behind);
            }
            if (spray)
            {
                FxDirector.Move(fx.Spray[i], sprayAt);
                FxDirector.SetDirection(fx.Spray[i], behind);
            }
        }
    }

    // A loop on while wanted (started with its details), at its intensity; stopped (fading) when not.
    private static void Loop(ref FxHandle handle, string key, bool on, Vector3 at, Vector3 normal, float intensity, FxParams p)
    {
        if (!on)
        {
            if (handle.IsValid)
            {
                FxDirector.Stop(handle);
                handle = default;
            }
            return;
        }
        if (!handle.IsValid)
        {
            handle = FxDirector.Start(key, at, normal, p);
        }
        FxDirector.SetIntensity(handle, intensity);
    }

    private static void StopAll(FxHandle[] handles)
    {
        for (int i = 0; i < handles.Length; i++)
        {
            FxDirector.Stop(handles[i]);
            handles[i] = default;
        }
    }

    private static void StopVehicle(VehicleFx? fx)
    {
        if (fx == null)
        {
            return;
        }
        StopAll(fx.Haze);
        StopAll(fx.Soot);
        StopAll(fx.Dust);
        StopAll(fx.Spray);
    }
}
