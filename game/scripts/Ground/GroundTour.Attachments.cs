using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Hollow.Simulation.Content;
using Hollow.Simulation.Items;
using HollowUniverse.Characters;
using HollowUniverse.Items;
using HollowUniverse.UI;
using HollowUniverse.UI.Model;

namespace HollowUniverse.Ground;

// Hosted-client optical geometry and light captures. Register repair-attachments in GroundTour's step list.
public partial class GroundTour
{
    private async Task RepairAttachments()
    {
        await RepairGod(true);
        await Base(); // A real server-built stone hut gives the optics/light a visible, colliding target.
        RequireRepair(_hut != null, "attachment capture has an actual hut target");
        foreach (string item in new[] { "rifle 1", "holo-sight 1", "scope 1", "weapon-light 1" }) Admin("give", item);
        await Until(() => Count("rifle") > 0 && Count("holo-sight") > 0 && Count("scope") > 0 && Count("weapon-light") > 0, 5);
        MoveToBelt("rifle", 4);
        Me.HeldSlot = 4;
        await Until(() => Me.Gun.Ready, 5);
        FirstPersonView hands = View.GetNode<FirstPersonView>("FirstPerson");
        View.Interface.Open(GroundScreen.Hud);
        await RepairDeviceInput();

        async Task Fit(string key, AttachmentSlot slot)
        {
            int from = SlotOf(Me.Inventory.Main, key) ?? throw new InvalidOperationException("repair-attachments: missing " + key);
            ushort id = Client.Content.ItemByKey(key).Id;
            Me.MoveAttachment(ContainerRef.Main, from, -1, ContainerRef.Belt, 4, (int)slot);
            await Until(() => Me.PendingCount == 0 && Me.Inventory.Belt[4].Mod(slot) == id, 5);
            RequireRepair(Me.Inventory.Belt[4].Mod(slot) == id, key + " installed through the real attachment transaction");
            await Seconds(.2);
        }

        async Task AimShot(string key)
        {
            await RepairDeviceInput();
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
            await Until(() => Me.Gun.Aimed, 3);
            RequireRepair(Me.Gun.Aimed, "RMB press enters ADS through the actual input handler");
            await Seconds(.9);
            Node3D gun = hands.HeldModel ?? throw new InvalidOperationException("repair-attachments: held model absent");
            RequireRepair(GunAttachmentPresentation.TryEye(gun, "rifle", out Transform3D eye), "ADS resolves the fitted native optic eye");
            float error = (gun.GlobalTransform * eye).Origin.DistanceTo(View.Camera!.GlobalPosition);
            float alignment = (gun.GlobalBasis * eye.Basis.X).Normalized().Dot(-View.Camera.GlobalBasis.Z.Normalized());
            var viewmodel = (ViewmodelView)gun.GetParent();
            GD.Print($"[attachment-check] {key}: eye error {error:0.0000} m, axis alignment {alignment:0.000000}, worldFov {View.Camera.Fov:0.00}, zoom {Me.Gun.Zoom:0.0}, " +
                $"hands {viewmodel.PrimaryHandAttachmentError:0.0000}/{viewmodel.SecondaryHandAttachmentError:0.0000} m, shoulder accommodation {viewmodel.OpticShoulderAccommodation:0.0000} m");
            await Shot(key); // Preserve the actual render even if a geometry assertion below finds a regression.
            RequireRepair(error < .015f && alignment > .999f, "fitted native eye and optical axis align with the camera after normal viewmodel motion");
            RequireRepair(viewmodel.PrimaryHandAttachmentError <= .025f && viewmodel.SecondaryHandAttachmentError <= .025f,
                "both hands remain attached within 25 mm while preserving the native optical eye");
            var lenses = gun.FindChildren("OpticLens_lod0", "MeshInstance3D", true, false).OfType<MeshInstance3D>().ToArray();
            RequireRepair(lenses.Length == 1 && lenses[0].GetActiveMaterial(0) is ShaderMaterial material &&
                material.Shader.ResourcePath.EndsWith("optic_lens_viewmodel.gdshader", StringComparison.Ordinal),
                "actual lens uses the first-person optical shader");
            Node3D sight = lenses[0];
            while (!sight.HasMeta("attachment_key") && sight.GetParent() is Node3D parent) sight = parent;
            RequireRepair(sight.HasMeta("attachment_key"), "optical geometry belongs to the fitted attachment");
            Vector3 rayOrigin = View.Camera.GlobalPosition, rayDirection = -View.Camera.GlobalBasis.Z.Normalized();
            bool lensHit = false;
            foreach (MeshInstance3D mesh in ModelCatalog.Meshes(sight).Where(mesh => ModelCatalog.LodOf(mesh) == 0 && mesh.Mesh != null))
            {
                Transform3D inverse = mesh.GlobalTransform.AffineInverse();
                Vector3 origin = inverse * rayOrigin, direction = inverse.Basis * rayDirection;
                Vector3[] faces = mesh.Mesh!.GetFaces();
                bool hit = false;
                for (int triangle = 0; triangle < faces.Length; triangle += 3)
                    if (AttachmentRayHit(origin, direction, faces[triangle], faces[triangle + 1], faces[triangle + 2])) { hit = true; break; }
                if (mesh == lenses[0]) lensHit = hit;
                else RequireRepair(!hit, "camera centre ray passes the actual opaque optic geometry: " + mesh.Name);
            }
            RequireRepair(lensHit, "camera centre ray intersects the actual transparent optic aperture");
            ScopeLensView? optical = sight.GetNodeOrNull<ScopeLensView>("ScopeImage");
            bool magnified = ModelCatalog.Find((string)sight.GetMeta("attachment_key"))!.Optic!.Magnified;
            if (magnified)
            {
                RequireRepair(optical is { Rendering: true }, "magnified ADS actively renders the world into its rear aperture");
                RequireRepair(optical!.OpticalViewport.Size == new Vector2I(ScopeLensView.Resolution, ScopeLensView.Resolution) &&
                    optical.OpticalViewport.World3D == View.Camera.GetWorld3D(), "one bounded optical viewport shares the actual world");
                RequireRepair((optical.OpticalCamera.CullMask & ViewmodelView.RenderLayer) == 0,
                    "optical world render excludes the gun, hands and front sight");
                RequireRepair(optical.OpticalCamera.Fov > 1 && optical.OpticalCamera.Fov < View.Camera.Fov,
                    "scope image matches the projected aperture at the existing handheld magnification");
                using Image opticalImage = optical.OpticalViewport.GetTexture().GetImage();
                RequireRepair(!opticalImage.IsEmpty() && opticalImage.GetWidth() == ScopeLensView.Resolution,
                    "scope viewport has actually produced an optical image");
                float energy = 0;
                for (int y = 64; y <= 448; y += 64)
                    for (int x = 64; x <= 448; x += 64)
                    {
                        Color sample = opticalImage.GetPixel(x, y);
                        energy += sample.R + sample.G + sample.B;
                    }
                RequireRepair(float.IsFinite(energy) && energy > .00001f, "actual scope image contains finite nonblack world radiance");
                GD.Print($"[attachment-check] optical image {ScopeLensView.Resolution}², fov {optical.OpticalCamera.Fov:0.00}, sampled radiance {energy:0.000000}");
            }
            else RequireRepair(optical == null, "unmagnified transparent holo creates no additional render viewport");
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
            await Until(() => !Me.Gun.Aimed, 3);
            RequireRepair(!Me.Gun.Aimed, "RMB release leaves ADS");
            await Seconds(.2);
            if (optical != null) RequireRepair(!optical.Rendering, "leaving ADS disables the extra world render");
        }

        await Fit("holo-sight", AttachmentSlot.Sight);
        await AimShot("repair-holo-ads");
        await Fit("scope", AttachmentSlot.Sight);
        await AimShot("repair-scope-ads");

        if (View.World is { Sky: { } sky } world)
        {
            Hollow.Core.Vector3D site = Client.Frame.ToBody(Client.Character.Position).Normalized;
            world.SetTime(world.ShownTime, true);
            sky.Ephemeris.SetLocalSolarHours(site, sky.Sun.Index, world.ShownTime, 0);
        }
        await Seconds(1.2);
        RequireRepair(View.World?.Sky is { SunElevation: < -6 }, "actual rendered camera sky is below civil twilight for all night light captures");
        GD.Print($"[attachment-check] verified night: local solar hour {View.World!.Sky!.LocalHours:0.00}, sun elevation {View.World.Sky.SunElevation:0.00} degrees");
        RequireRepair(!Me.Gun.Mods.Light, "night baseline has no fitted weapon light");
        await Shot("repair-weapon-light-off");
        await Fit("weapon-light", AttachmentSlot.Rail);
        await Seconds(.8);
        SpotLight3D[] lamps = hands.HeldModel!.FindChildren("Light", "SpotLight3D", true, false).OfType<SpotLight3D>().ToArray();
        RequireRepair(lamps.Length == 1 && lamps[0].IsVisibleInTree() && lamps[0].LightEnergy > 0,
            "one real spotlight is active at the fitted beam socket");
        await Shot("repair-weapon-light-on");
        await AimShot("repair-scope-night-light");
        int empty = Enumerable.Range(0, Me.Inventory.Main.Size).First(i => Me.Inventory.Main[i].IsEmpty);
        Me.MoveAttachment(ContainerRef.Belt, 4, (int)AttachmentSlot.Rail, ContainerRef.Main, empty, -1);
        await Until(() => Me.PendingCount == 0 && !Me.Gun.Mods.Light, 4);
        await Seconds(.2);
        RequireRepair(hands.HeldModel!.FindChildren("Light", "SpotLight3D", true, false).Count == 0,
            "detaching the attachment removes its actual spotlight");
        await Shot("repair-weapon-light-detached");
        Me.HeldSlot = -1;
        await Seconds(.3);
        RequireRepair(hands.HeldModel == null, "holster removes the held optics model");
        GD.Print("[attachment-check] PASS hosted transactions, native eyes/hands, real optical world image, ADS render lifecycle, verified night, light creation and cleanup; inspect screenshots for optical/lighting quality");
    }

    private static bool AttachmentRayHit(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a, ac = c - a, p = direction.Cross(ac);
        float determinant = ab.Dot(p);
        if (MathF.Abs(determinant) < 1e-10f) return false;
        float inverse = 1f / determinant;
        Vector3 t = origin - a;
        float u = t.Dot(p) * inverse;
        if (u < -1e-5f || u > 1 + 1e-5f) return false;
        Vector3 q = t.Cross(ab);
        float v = direction.Dot(q) * inverse;
        return v >= -1e-5f && u + v <= 1 + 1e-5f && ac.Dot(q) * inverse > 1e-4f;
    }
}
