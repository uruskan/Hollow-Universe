using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Hollow.Simulation.Combat;
using HollowUniverse.Gibs;
using HollowUniverse.Items;

namespace HollowUniverse.Fx;

/// <summary>
/// Gibs in the effects viewer (GROUND.md §9.4). <c>--gibs</c> lays out every model that breaks (props, deployables, the
/// outcrops' remnants, doors and each grade's wall, doorway and foundation) in a grid and breaks them all at once, again
/// with Space; C changes the cause. <c>--gibs raid</c> is a stone wall blown in with ten barrels shot before it.
/// <c>--stress gibs --measure &lt;s&gt;</c> breaks that scene every 2.5 s with gibs off and on in turn and prints what they
/// cost. With <c>--shot</c> the moment is held <c>--at &lt;s&gt;</c> after the break (0.6 s by default).
/// </summary>
public partial class FxViewer
{
    private sealed record GibCell(string Model, string Part, Vector3 At, float Yaw, Node3D? Shown);

    private readonly List<GibCell> _gibCells = new();
    private GibWorld _gibs = null!;
    private double _gibBreakAt = -1;
    private DestroyCause _gibCause = DestroyCause.Melee;
    private bool _gibRaid;

    // Every set the catalogue names: whole models first, then the kits' parts (a building grade's: those of --gib-parts,
    // by default its wall, doorway and foundation; a door kit's: all), so the sheet stays legible.
    private IEnumerable<(string Model, string Part)> GibSets()
    {
        var wanted = (Options.TryGetValue("gib-parts", out string? list) ? list : "wall,doorway,foundation")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);
        var kits = new List<(string, string)>();
        foreach (ModelEntry e in ModelCatalog.Entries)
        {
            if (e.Gibs == null)
            {
                continue;
            }
            if (!e.IsKit)
            {
                yield return (e.Key, "");
                continue;
            }
            IReadOnlyList<string> parts = GibLibrary.Parts(e.Key);
            bool all = !parts.Any(wanted.Contains);
            kits.AddRange(parts.Where(p => all || wanted.Contains(p)).Select(p => (e.Key, p)));
        }
        foreach ((string, string) k in kits)
        {
            yield return k;
        }
    }

    private void StartGibs(string scene)
    {
        _gibs = GibDirector.Install(this);
        _gibs.Floor = FlatFloor;
        _gibRaid = scene == "raid";
        _title.Text = _gibRaid ? "Gibs · a raid" : "Gibs";
        if (_gibRaid)
        {
            BuildRaid(showModels: true);
            _detail.Text = "A stone wall blown in, ten barrels shot before it.";
        }
        else if (scene != "true" && scene.Length > 0)
        {
            // One model close up: --gibs <key> or <kit>/<part>; --yaw turns the camera about it, --distance moves it.
            string[] names = scene.Split('/', 2);
            _gibCells.Add(new GibCell(names[0], names.Length > 1 ? names[1] : "", Vector3.Zero, 0.4f, null));
            Aabb box = GibLibrary.Get(names[0], names.Length > 1 ? names[1] : "")?.Bounds ?? new Aabb(-Vector3.One, Vector3.One * 2);
            float size = MathF.Max(0.6f, box.Size.Length());
            float yaw = Mathf.DegToRad(Options.TryGetValue("yaw", out string? y) ? float.Parse(y, CultureInfo.InvariantCulture) : 30f);
            float distance = Options.TryGetValue("distance", out string? d) ? float.Parse(d, CultureInfo.InvariantCulture) : size * 1.6f;
            var look = new Vector3(0, size * 0.18f, 0);
            _camera.Fov = 55;
            _camera.Position = look + new Vector3(MathF.Sin(yaw), 0.55f, -MathF.Cos(yaw)).Normalized() * distance;
            _camera.LookAt(look, Vector3.Up);
            _title.Text = $"Gibs · {scene}";
            _detail.Text = "C cause · Space again · P pause";
        }
        else
        {
            BuildGibGrid();
            _detail.Text = $"{_gibCells.Count} models breaking: props, deployables, outcrops, doors, each grade's wall, doorway and foundation.";
        }
        _gibBreakAt = _clock + 1.2;
        if (Options.ContainsKey("shot"))
        {
            float at = Options.TryGetValue("at", out string? s) ? float.Parse(s, CultureInfo.InvariantCulture) : 0.6f;
            _shotAt = _gibBreakAt + at;
        }
    }

    // A plane at the ground's height (y = 0), the viewer's floor.
    private static bool FlatFloor(Vector3 from, Vector3 down, float length, out Vector3 point, out Vector3 normal)
    {
        normal = Vector3.Up;
        point = new Vector3(from.X, 0, from.Z);
        return from.Y >= 0 && down.Y < 0;
    }

    private void BuildGibGrid()
    {
        var sets = GibSets().ToList();
        const int columns = 9;
        const float cell = 4.2f;
        int rows = (sets.Count + columns - 1) / columns;
        for (int i = 0; i < sets.Count; i++)
        {
            (string model, string part) = sets[i];
            int row = i / columns, column = i % columns;
            var at = new Vector3((column - (columns - 1) * 0.5f) * cell, 0, (row - (rows - 1) * 0.5f) * cell * 1.15f);
            _gibCells.Add(new GibCell(model, part, at, 0.35f, null));
        }
        // From the south, looking down on the grid.
        _camera.Fov = 62;
        _camera.Position = new Vector3(0, 15, -22.5f);
        _camera.LookAt(new Vector3(0, 0, 1.2f), Vector3.Up);
    }

    // The raid: a stone wall, ten barrels in an arc before it; the camera where the raider stands.
    private void BuildRaid(bool showModels)
    {
        FxStation wall = FxStage.Build(this, "wall", new Vector3(0, 0, 9), MathF.PI);
        _gibCells.Add(new GibCell("building-stone", "wall", wall.Root.Position, MathF.PI, wall.Root));
        for (int i = 0; i < 10; i++)
        {
            float a = (i - 4.5f) * 0.16f;
            var at = new Vector3(MathF.Sin(a) * 6.5f, 0, 6.5f - MathF.Cos(a) * 6.5f + 4.2f);
            string key = i % 3 == 2 ? "loot-barrel-2" : "loot-barrel-1";
            Node3D? shown = null;
            if (showModels && ModelCatalog.Instantiate(key) is { } barrel)
            {
                shown = new Node3D { Position = at, Rotation = new Vector3(0, i * 0.7f, 0) };
                shown.AddChild(barrel);
                AddChild(shown);
            }
            _gibCells.Add(new GibCell(key, "", at, i * 0.7f, shown));
        }
        _camera.Fov = 70;
        _camera.Position = new Vector3(1.2f, 1.7f, -4.5f);
        _camera.LookAt(new Vector3(0, 1.0f, 7.5f), Vector3.Up);
    }

    /// <summary>Breaks every cell's model where it stands; the barrels and the wall shot or blown from the camera's side.</summary>
    private void BreakGibCells()
    {
        Vector3 from = _camera.GlobalPosition;
        foreach (GibCell c in _gibCells)
        {
            if (GibLibrary.Get(c.Model, c.Part) is not { } set)
            {
                continue;
            }
            // Standing on the floor: the set's lowest point on the ground (a foundation's top is its origin).
            var turn = new Basis(Vector3.Up, c.Yaw);
            Aabb box = new Transform3D(turn, Vector3.Zero) * set.Bounds;
            var at = new Transform3D(turn, c.At + Vector3.Up * MathF.Max(0, -box.Position.Y));
            Vector3 toward = at * set.Bounds.GetCenter() - from;
            toward.Y = 0;
            DestroyCause cause = _gibRaid ? c.Part == "wall" ? DestroyCause.Explosion : DestroyCause.Bullet : _gibCause;
            GibBreak broke = GibDirector.Break(set, at, toward.Normalized(), cause, Vector3.Up, 0, (ulong)(c.At.X * 131 + c.At.Z * 17 + 7));
            if (c.Shown != null)
            {
                c.Shown.Visible = !broke.Shown;
            }
        }
        if (_gibRaid && !_gibStress)
        {
            // The charge's own blast on the wall's face (not in the measurement: it is the explosion's cost, not the gibs').
            FxDirector.Play("explosion.timed-charge", new Vector3(0, 1.4f, 8.85f), Vector3.Back, default);
        }
    }

    private void UpdateGibs(double delta)
    {
        if (_gibBreakAt >= 0 && _clock >= _gibBreakAt)
        {
            _gibBreakAt = -1;
            BreakGibCells();
        }
        if (_shotAt > 0 && _clock >= _shotAt && !_gibs.Paused)
        {
            _gibs.Paused = true;
            _fx.Paused = true;
            ReadyForShot = true;
        }
        _stats.Text = $"{Engine.GetFramesPerSecond():0} fps · cause {_gibCause.ToString().ToLowerInvariant()} · pieces {_gibs.Live} · batches {_gibs.ActiveBatches}" +
            $" · gibs {_gibs.LastMicroseconds:0} µs · fx {_fx.LastFrameMicroseconds:0} µs";
    }

    private bool GibKey(Key key)
    {
        switch (key)
        {
            case Key.Space:
                _gibs.Clear();
                _fx.ClearAll();
                foreach (GibCell c in _gibCells)
                {
                    if (c.Shown != null)
                    {
                        c.Shown.Visible = true;
                    }
                }
                _gibBreakAt = _clock + 0.6;
                return true;
            case Key.C:
                _gibCause = (DestroyCause)(((int)_gibCause + 1) % Enum.GetValues<DestroyCause>().Length);
                return true;
            case Key.P:
                _gibs.Paused = !_gibs.Paused;
                _fx.Paused = _gibs.Paused;
                return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- the measurement

    private bool _gibStress;
    private double _nextGibBreak;
    private readonly List<double> _gibUs = new();
    private int _gibPeak;
    private double _gibBreakMax, _gibUploadMax, _gibStepMax;
    private int _gibCollected;
    private double _gibUploadSum, _gibBreakSum;
    private long _gibUploads;
    private long _gibMoving, _gibLive;

    private void StartGibStress()
    {
        _gibStress = true;
        _gibs = GibDirector.Install(this);
        _gibs.Floor = FlatFloor;
        _gibRaid = true;
        BuildRaid(showModels: false);
        _nextGibBreak = 0.5;
        GD.Print($"[gibs-measure] a stone wall blown in and ten barrels shot, every 2.5 s; warm-up 4 s, then {_measureSeconds:0} s each: gibs off, on, off, on");
    }

    // Every 2.5 s of a phase with gibs on: the wall and the barrels break at once.
    private void UpdateGibStress()
    {
        double t = _clock - _phaseStart;
        if (EffectsOn && t >= _nextGibBreak)
        {
            _nextGibBreak = t + 2.5;
            BreakGibCells();
        }
        if (_phase > 0)
        {
            _gibUs.Add(_gibs.LastMicroseconds + _gibs.LastBreakMicroseconds);
            _gibPeak = Math.Max(_gibPeak, _gibs.Live);
            _gibBreakMax = Math.Max(_gibBreakMax, _gibs.LastBreakMicroseconds);
            _gibUploadSum += _gibs.LastUploadMicroseconds;
            _gibBreakSum += _gibs.LastBreakMicroseconds;
            _gibUploads += _gibs.LastUploads;
            _gibMoving += _gibs.Moving;
            _gibLive += _gibs.Live;
            _gibUploadMax = Math.Max(_gibUploadMax, _gibs.LastUploadMicroseconds);
            if (_gibs.LastCollections == 0)
            {
                _gibStepMax = Math.Max(_gibStepMax, _gibs.LastMicroseconds - _gibs.LastUploadMicroseconds);
            }
            else
            {
                _gibCollected++;
            }
        }
    }

    private void EndGibPhase()
    {
        _nextGibBreak = 0.5;
        if (!EffectsOn)
        {
            _gibs.Clear();
        }
    }

    private string GibPhaseLine()
    {
        int n = Math.Max(1, _gibUs.Count);
        string line = $", gibs {(_gibUs.Count > 0 ? _gibUs.Average() : 0):0} µs a frame (breaks {_gibBreakSum / n:0}, handing over {_gibUploadSum / n:0} " +
            $"in {(double)_gibUploads / n:0.0} calls, {(double)_gibLive / n:0} pieces of which {(double)_gibMoving / n:0} moving; most {(_gibUs.Count > 0 ? _gibUs.Max() : 0):0}: " +
            $"a break {_gibBreakMax:0}, a step {_gibStepMax:0}, handing over {_gibUploadMax:0}; {_gibCollected} frames with a collection), pieces at most {_gibPeak}";
        _gibUs.Clear();
        _gibPeak = 0;
        _gibBreakMax = _gibUploadMax = _gibStepMax = 0;
        _gibCollected = 0;
        _gibUploadSum = _gibBreakSum = 0;
        _gibUploads = 0;
        _gibMoving = _gibLive = 0;
        return line;
    }
}
