using System;
using Godot;

namespace HollowUniverse.Characters;

public partial class ViewmodelView
{
    // Cosmetic convergence for the camera-attached arrow. Projectile trajectory still belongs
    // to the simulation; this point only keeps the drawn shaft looking toward the crosshair.
    private static readonly Vector3 BowAimPoint = new(0, 0, -10);

    private Transform3D AlignBowAim(Transform3D item, Transform3D toSkeleton)
    {
        if (_bowRig == null || _bowAim <= 0) return item;
        float amount = Smooth(_bowAim);
        Vector3 shelf = HeldItems.Socket(_item!, "arrow_rest").Origin;
        float reachCorrection = AuthoredReachCorrection;
        // The string hand and holding hand remain reachable after turning the bow. Refine
        // full aim only: repeating a partial blend would accelerate the authored raising pose.
        int passes = amount >= 0.999f ? 4 : 1;
        for (int pass = 0; pass < passes; pass++)
        {
            Vector3 rest = item * shelf;
            Vector3 forward = (BowAimPoint - rest).Normalized();
            Vector3 up = item.Basis.Y - forward * item.Basis.Y.Dot(forward);
            if (up.LengthSquared() < 0.000001f) up = Vector3.Up - forward * Vector3.Up.Dot(forward);
            up = up.Normalized();
            var aimed = new Basis(forward, up, forward.Cross(up));
            item.Basis = new Basis(item.Basis.GetRotationQuaternion().Slerp(aimed.GetRotationQuaternion(), amount));
            // Turn about the arrow shelf, preserving its position and the artist's cant.
            item.Origin = rest - item.Basis * shelf;
            item = ConstrainReach(item, toSkeleton);
            reachCorrection += AuthoredReachCorrection;
        }
        AuthoredReachCorrection = reachCorrection;
        return item;
    }
}
