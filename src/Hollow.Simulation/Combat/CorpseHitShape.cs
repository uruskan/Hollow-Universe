using System.Numerics;

namespace Hollow.Simulation.Combat;

/// <summary>The solid shape of a harvestable player body, shared by authority traces and predicted attack targeting.</summary>
public static class CorpseHitShape
{
    public static float Raycast(Vector3 position, Vector3 up, float yaw, Vector3 origin, Vector3 direction, float length, float radius = 0)
    {
        (Vector3 right, Vector3 forward) = Geometry.Axes(up, yaw);
        return Geometry.RayBox(origin, direction, length, position + up * 0.3f,
            new Vector3(0.45f, 0.3f, 0.95f) + new Vector3(radius), right, up, forward);
    }
}
