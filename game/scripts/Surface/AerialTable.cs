using System;
using System.Threading.Tasks;
using Godot;

namespace HollowUniverse.Surface;

/// <summary>
/// The aerial-perspective table of the surface view (GROUND.md §4.6): the air's in-scattered light and transmittance
/// for every direction and distance from the camera, recomputed on a worker thread a few times a second as the sun,
/// the moon and the camera's height change. Everything under the sky (ground, sea, plants, clouds) reads it with one
/// texture fetch instead of marching through the air per pixel. Presentation only.
/// </summary>
public sealed class AerialTable
{
    private const double MinInterval = 0.15;

    private readonly AtmosphereModel _air;
    private readonly int _count = AtmosphereModel.ApAzimuths * AtmosphereModel.ApZeniths * AtmosphereModel.ApDistances * 4;
    private readonly ImageTexture3D _inscatter = new();
    private readonly ImageTexture3D _transmittance = new();
    private Task<(float[] L, float[] T, Vector3 Up, Vector3 East)>? _job;
    private double _lastStart = -1;
    private Vector3 _lastSun;
    private Vector3 _lastMoon;
    private double _lastAltitude = -1;
    private bool _created;

    private readonly double _range;

    /// <summary>The table of an air out to a distance (metres).</summary>
    public AerialTable(AtmosphereModel air, double range = AtmosphereModel.ApRange)
    {
        _air = air;
        _range = range;
    }

    public Texture3D Inscatter => _inscatter;
    public Texture3D Transmittance => _transmittance;

    /// <summary>
    /// Starts a recomputation when the light or the camera's height changed enough, and uploads a finished one
    /// (setting the table's axes as shader globals). <paramref name="wait"/> computes synchronously (the first frame).
    /// </summary>
    public void Update(double now, double altitude, Vector3 up, Vector3 east, Vector3 sun, Vector3 sunE, Vector3 moon,
        Vector3 moonE, bool wait = false)
    {
        if (_job != null && (_job.IsCompleted || wait))
        {
            var (l, t, u, e) = _job.Result;
            Upload(l, t, u, e);
            _job = null;
        }
        bool changed = !_created || sun.AngleTo(_lastSun) > 0.002f || moon.AngleTo(_lastMoon) > 0.004f
            || Math.Abs(altitude - _lastAltitude) > Math.Max(2, _lastAltitude * 0.03);
        if (_job == null && changed && (now - _lastStart > MinInterval || wait))
        {
            _lastStart = now;
            _lastSun = sun;
            _lastMoon = moon;
            _lastAltitude = altitude;
            _job = Task.Run(() =>
            {
                var l = new float[_count];
                var t = new float[_count];
                _air.AerialPerspective(altitude, up, east, sun, sunE, moon, moonE, l, t, _range);
                return (l, t, up, east);
            });
            if (wait)
            {
                var (l, t, u, e) = _job.Result;
                Upload(l, t, u, e);
                _job = null;
            }
        }
    }

    private void Upload(float[] l, float[] t, Vector3 up, Vector3 east)
    {
        var inscatter = Slices(l);
        var transmittance = Slices(t);
        if (!_created)
        {
            _inscatter.Create(Image.Format.Rgbah, AtmosphereModel.ApAzimuths, AtmosphereModel.ApZeniths, AtmosphereModel.ApDistances, false, inscatter);
            _transmittance.Create(Image.Format.Rgbah, AtmosphereModel.ApAzimuths, AtmosphereModel.ApZeniths, AtmosphereModel.ApDistances, false, transmittance);
            _created = true;
        }
        else
        {
            _inscatter.Update(inscatter);
            _transmittance.Update(transmittance);
        }
        RenderingServer.GlobalShaderParameterSet("hu_ap_up", up);
        RenderingServer.GlobalShaderParameterSet("hu_ap_east", east);
    }

    // One RGBA-half image per distance slice.
    private static Godot.Collections.Array<Image> Slices(float[] data)
    {
        int w = AtmosphereModel.ApAzimuths, h = AtmosphereModel.ApZeniths, slice = w * h * 4;
        var images = new Godot.Collections.Array<Image>();
        for (int k = 0; k < AtmosphereModel.ApDistances; k++)
        {
            var bytes = new byte[slice * 2];
            for (int i = 0; i < slice; i++)
            {
                ushort half = BitConverter.HalfToUInt16Bits((Half)data[k * slice + i]);
                bytes[2 * i] = (byte)half;
                bytes[2 * i + 1] = (byte)(half >> 8);
            }
            images.Add(Image.CreateFromData(w, h, false, Image.Format.Rgbah, bytes));
        }
        return images;
    }
}
