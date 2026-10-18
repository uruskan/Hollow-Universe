using System;
using System.Threading;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>
/// Authored contact-chain validation only. The frozen recipe has no generated connector permission or contact
/// pairing rule, so this slice never manufactures a cap/apron or silently substitutes FitPoints.
/// </summary>
public static class FormationTransitions
{
    /// <summary>
    /// Visits every declared chain vertex and every edge midpoint. A positive minimum burial requires burial;
    /// otherwise the declared gap is allowed. All tolerances scale with the qualified physical geometry.
    /// The caller supplies a single aggregate sample budget; no chain or alternative resets it.
    /// </summary>
    internal static bool ContactsMatch(CliffSupportDefinition definition, FormationGeometryChoice choice,
        FormationTransform transform, Func<Vector3D, double> signedClearance, Action chargeSample,
        CancellationToken cancellation)
    {
        bool matches = true;
        foreach (string key in choice.ContactKeys)
        {
            CliffContactDomain contact = definition.RequireContact(key);
            double low = -contact.MaximumBurialMetres * transform.Scale;
            double high = (contact.MinimumBurialMetres > 0 ? -contact.MinimumBurialMetres : contact.MaximumGapMetres) * transform.Scale;
            for (int i = 0; i < contact.VertexIndices.Length; i++)
            {
                Check(definition.Geometry.Vertices[contact.VertexIndices[i]]);
                if (i > 0)
                    Check((definition.Geometry.Vertices[contact.VertexIndices[i - 1]] +
                        definition.Geometry.Vertices[contact.VertexIndices[i]]) * 0.5);
            }
            void Check(Vector3D local)
            {
                cancellation.ThrowIfCancellationRequested(); chargeSample();
                double clearance = signedClearance(transform.Transform(local));
                if (!double.IsFinite(clearance) || clearance < low || clearance > high) matches = false;
            }
        }
        return matches;
    }
}
