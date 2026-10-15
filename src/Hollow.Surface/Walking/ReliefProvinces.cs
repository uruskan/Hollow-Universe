using System;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>The kinds of land a walking-scale world is made of (docs/WALKING_GROUND.md §3).</summary>
public enum Relief : byte
{
    /// <summary>Low, open country a few metres above the shore, with kettle hollows that hold lakes.</summary>
    Plains = 0,

    /// <summary>Rolling hills of tens of metres.</summary>
    Hills = 1,

    /// <summary>Flat-topped tablelands in terraces, their edges cliffs, cut by canyons.</summary>
    Tableland = 2,

    /// <summary>Ridged mountains of a few hundred metres, the highest with snow.</summary>
    Mountains = 3,
}

/// <summary>
/// The relief provinces of a walking-scale world (docs/WALKING_GROUND.md §3): points spread evenly over the sphere, each
/// the centre of a province of one <see cref="Relief"/>, picked from the shares and an uplift field so that mountains and
/// tablelands gather into ranges and plains into lowlands. At a point, every province near it weighs in by a Gaussian of
/// its distance, normalised: smooth everywhere, with no crease along the borders (a nearest-province rule would leave
/// one), and about a blend's width wide. Deterministic: Hash64, Rng, DMath.
/// </summary>
internal sealed class ReliefProvinces
{
    public const int TypeCount = 4;

    private readonly Vector3D[] _centre;
    private readonly Relief[] _type;
    private readonly double[] _amplitude;
    private readonly double _radius;
    private readonly double _sigma2; // the Gaussian's width², in squared chord units of the unit sphere
    private readonly double _spacing; // chord between neighbouring centres, about

    public ReliefProvinces(ulong seed, double radius, WalkingParameters p)
    {
        int count = Math.Max(4, p.Provinces);
        _radius = radius;
        _spacing = Math.Sqrt(4 * DMath.Pi / count);
        // Across a border the squared chords to the two centres part by about spacing × 2x / R at x metres from it: the
        // weights' ratio falls to e⁻³ a third of the blend's width from the border.
        _sigma2 = _spacing * 2 * p.ProvinceBlend / (9 * radius);
        _centre = new Vector3D[count];
        _type = new Relief[count];
        _amplitude = new double[count];
        Vector3D[] lattice = PlanetSurface.FibonacciSphere(count);
        var rng = new Rng(Hash64.Of(seed, 0x50524F56)); // "PROV"
        Span<double> shares = stackalloc double[TypeCount];
        for (int i = 0; i < count; i++)
        {
            // Jitter each lattice point along the ground by up to a third of the spacing, so the provinces don't line up.
            Vector3D up = lattice[i];
            Vector3D east = Vector3D.Cross(Vector3D.UnitY, up);
            east = east.LengthSquared > 1e-12 ? east.Normalized : Vector3D.UnitX;
            Vector3D north = Vector3D.Cross(up, east);
            double a = rng.Range(0, DMath.TwoPi), r = rng.Range(0, _spacing / 3);
            Vector3D c = (up + east * (r * DMath.Cos(a)) + north * (r * DMath.Sin(a))).Normalized;
            _centre[i] = c;
            double uplift = Noise.Fbm(c, radius, p.UpliftWavelength, 2, 0.5, Hash64.Of(seed, 0x55504C46), NoiseBasis.Corners); // "UPLF"
            shares[(int)Relief.Plains] = p.PlainsShare * (0.5 + 1.3 * DMath.SmoothStep(0.1, -0.35, uplift));
            shares[(int)Relief.Hills] = p.HillsShare;
            shares[(int)Relief.Tableland] = p.TablelandShare * (0.7 + 0.9 * DMath.SmoothStep(-0.25, 0.15, uplift));
            shares[(int)Relief.Mountains] = p.MountainShare * (0.4 + 2.6 * DMath.SmoothStep(-0.1, 0.3, uplift));
            int pick = rng.PickWeighted(shares);
            _type[i] = (Relief)Math.Max(0, pick);
            _amplitude[i] = rng.Range(0.75, 1.25);
        }
    }

    public int Count => _centre.Length;

    public Vector3D Centre(int i) => _centre[i];

    public Relief Type(int i) => _type[i];

    /// <summary>
    /// The weights of the four relief types at a unit direction (adding up to 1), their provinces' mean amplitude
    /// (about 0.75–1.25), and how central the point lies in them (1 at a centre, 0 from about three quarters of the
    /// spacing out).
    /// </summary>
    public void Weights(Vector3D q, Span<double> weight, Span<double> amplitude, Span<double> centrality)
    {
        weight.Clear();
        amplitude.Clear();
        centrality.Clear();
        double best = -2;
        int count = _centre.Length;
        Span<double> dots = stackalloc double[count];
        for (int k = 0; k < count; k++)
        {
            double d = Vector3D.Dot(q, _centre[k]);
            dots[k] = d;
            if (d > best)
            {
                best = d;
            }
        }
        // Squared chords from the dot products: |q − c|² = 2 − 2 q·c. Weights exp(−(c² − c²nearest) / σ²): the nearest
        // only scales them all alike (normalising takes it out), and keeps the numbers in range.
        double nearest2 = Math.Max(0, 2 - 2 * best);
        double cutoff = 1 - (nearest2 + 12 * _sigma2) / 2;
        double sum = 0;
        for (int k = 0; k < count; k++)
        {
            if (dots[k] < cutoff)
            {
                continue;
            }
            double chord2 = Math.Max(0, 2 - 2 * dots[k]);
            double w = DMath.Exp(-(chord2 - nearest2) / _sigma2);
            int t = (int)_type[k];
            weight[t] += w;
            amplitude[t] += w * _amplitude[k];
            double c = 1 - chord2 / (0.6 * _spacing * _spacing);
            centrality[t] += w * (c > 0 ? c * c : 0);
            sum += w;
        }
        for (int t = 0; t < TypeCount; t++)
        {
            if (weight[t] > 0)
            {
                amplitude[t] /= weight[t];
                centrality[t] /= weight[t];
                weight[t] /= sum;
            }
        }
    }
}
