using System;
using System.Collections.Generic;
using System.Linq;
using Hollow.Core;
using Hollow.Simulation;
using Hollow.Surface;

namespace Hollow.Client.Diagnostics;

public sealed record EnvironmentRouteStop(string Label, Vector3D Position, Vector3D LookAt,
    string Feature, double Height, double Grade, double Grass, double Woodland, int Trees);

public sealed record EnvironmentRouteLeg(IReadOnlyList<Vector3D> Points, double Metres);

public sealed record EnvironmentRoutePlan(IReadOnlyList<EnvironmentRouteStop> Stops,
    IReadOnlyList<EnvironmentRouteLeg> Legs, string Region, int CandidatesChecked)
{
    public double Metres => Legs.Sum(l => l.Metres);
}

/// <summary>
/// Diagnostic-only route selection on the same Earth used by the server. No generation parameters, objects or
/// collision are changed. Height/water samples establish a plausible walking corridor; the hosted run must still
/// prove it can traverse real tree/scan/building collision. Failure never substitutes disconnected scenic locations.
/// </summary>
public static class EarthEnvironmentRoute
{
    public const double Spacing = 10, MaximumGrade = 0.70, MaximumLength = 1200;
    private sealed record Cell(Vector3D Position, double Height, double Grade, double Grass, double Woodland,
        bool Walkable);
    private readonly record struct Candidate(MonumentPlacement Monument, Vector3D Beach, CliffPlacement Cliff, double Score);

    public static EnvironmentRoutePlan Find(WalkingWorld world)
    {
        if (Math.Abs(world.Radius - 1200) > 0.01)
            throw new ArgumentException("The environment route is for live Earth at radius1200, not legacy FirstWorld.");
        var beaches = Beaches.Find(world, ZoneFrame.World).Points.Select(p => new Vector3D(p.X, p.Y, p.Z)).ToArray();
        var cliffs = new List<CliffPlacement>();
        for (long i = 0; i < TileKey.CountAt(world.Cliffs.Level); i++)
            if (world.Cliffs.FormationAt(TileKey.FromIndex(world.Cliffs.Level, i)!.Value) is { Count: >= 3 } group)
                cliffs.Add(group[0]);
        var candidates = new List<Candidate>();
        foreach (MonumentPlacement monument in world.Monuments.All)
        {
            if (monument.Definition.Radius > 40) continue;
            foreach (Vector3D beach in beaches.OrderBy(b => Distance(b, monument.Position)).Take(3))
            foreach (CliffPlacement cliff in cliffs.OrderBy(c => Distance(c.Position, monument.Position)).Take(3))
            {
                double a = Distance(beach, monument.Position), b = Distance(cliff.Position, monument.Position),
                    c = Distance(beach, cliff.Position);
                if (Math.Max(a, Math.Max(b, c)) > 600) continue;
                candidates.Add(new(monument, beach, cliff, a + b + c));
            }
        }
        int checkedCount = 0;
        var failures = new List<string>();
        foreach (Candidate candidate in candidates.OrderBy(c => c.Score).Take(18))
        {
            checkedCount++;
            var grid = new Grid(world, candidate);
            if (grid.Plan(candidate, checkedCount, out EnvironmentRoutePlan? plan, out string reason)) return plan!;
            failures.Add(candidate.Monument.Definition.Key + ": " + reason);
        }
        throw new InvalidOperationException($"No connected environment route in {checkedCount} bounded candidates: " + string.Join("; ", failures));
    }

    public static double Latitude(Vector3D p) => DMath.Asin(Math.Clamp(p.Normalized.Y, -1, 1)) * DMath.RadToDeg;
    public static double Longitude(Vector3D p) => DMath.Atan2(-p.Z, p.X) * DMath.RadToDeg;
    public static double Distance(Vector3D a, Vector3D b) => (a.Normalized - b.Normalized).Length * 1200;
    public static Vector3D Ground(WalkingWorld world, Vector3D direction) =>
        direction.Normalized * (world.Radius + world.HeightAt(direction.Normalized));

    public static double Grade(WalkingWorld world, Vector3D direction)
    {
        Vector3D up = direction.Normalized, east = Vector3D.Cross(Vector3D.UnitY, up).Normalized,
            north = Vector3D.Cross(up, east), at = up * world.Radius;
        double x = (world.HeightAt((at + east * 2).Normalized) - world.HeightAt((at - east * 2).Normalized)) / 4;
        double y = (world.HeightAt((at + north * 2).Normalized) - world.HeightAt((at - north * 2).Normalized)) / 4;
        return Math.Sqrt(x * x + y * y);
    }

    /// <summary>Two-metre samples catch water and heightfield steps between grid vertices.</summary>
    public static bool Corridor(WalkingWorld world, Vector3D a, Vector3D b)
    {
        double length = Distance(a, b);
        int steps = Math.Max(1, (int)Math.Ceiling(length / 2));
        double previous = world.HeightAt(a.Normalized);
        for (int i = 0; i <= steps; i++)
        {
            Vector3D up = (a.Normalized * (1.0 - (double)i / steps) + b.Normalized * ((double)i / steps)).Normalized;
            double height = world.HeightAt(up);
            if (height < world.WaterLevelAt(up) - 0.10 || world.Monuments.At(up, 2) != null ||
                (i > 0 && Math.Abs(height - previous) / Math.Max(0.01, length / steps) > MaximumGrade)) return false;
            previous = height;
        }
        return true;
    }

    private sealed class Grid
    {
        private readonly WalkingWorld _world;
        private readonly Cell[] _cells;
        private readonly int _side;
        private readonly Dictionary<(int, int), bool> _edges = new();
        private readonly Dictionary<TileKey, IReadOnlyList<ScatterItem>> _scatter = new();

        public Grid(WalkingWorld world, Candidate candidate)
        {
            _world = world;
            Vector3D up = (candidate.Beach.Normalized + candidate.Monument.Up + candidate.Cliff.Up).Normalized;
            Vector3D origin = up * world.Radius, east = Vector3D.Cross(Vector3D.UnitY, up).Normalized,
                north = Vector3D.Cross(up, east);
            double extent = new[] { candidate.Beach, candidate.Monument.Position, candidate.Cliff.Position }
                .Max(p => Math.Max(Math.Abs(Vector3D.Dot(p - origin, east)), Math.Abs(Vector3D.Dot(p - origin, north)))) + 85;
            int half = Math.Min(45, (int)Math.Ceiling(extent / Spacing));
            _side = half * 2 + 1;
            _cells = new Cell[_side * _side];
            Span<double> biomes = stackalloc double[BiomeTable.Count], materials = stackalloc double[BiomeTable.MaterialCount];
            for (int y = 0; y < _side; y++)
            for (int x = 0; x < _side; x++)
            {
                Vector3D direction = (origin + east * ((x - half) * Spacing) + north * ((y - half) * Spacing)).Normalized;
                double h = world.HeightAt(direction), slope = Grade(world, direction);
                WalkingSample fields = world.Fields(direction);
                GroundCover cover = world.Cover(direction, world.Sample(direction), slope, biomes, materials);
                bool open = h >= world.WaterLevelAt(direction) - 0.1 && slope <= MaximumGrade &&
                    world.Monuments.At(direction, 2) == null;
                _cells[y * _side + x] = new(direction * (world.Radius + h), h, slope, cover.Grass,
                    world.ForestCover(direction, fields), open);
            }
        }

        public bool Plan(Candidate candidate, int checkedCount, out EnvironmentRoutePlan? plan, out string reason)
        {
            plan = null;
            reason = "no beach connection";
            int start = Nearest(candidate.Beach, c => c.Walkable && _world.BiomeAt(c.Position.Normalized) == Biome.Beach && Distance(c.Position, candidate.Beach) < 18 &&
                Corridor(_world, candidate.Beach, c.Position));
            if (start < 0) return false;
            var (reach, _) = Search(start);
            bool Reached(int i) => double.IsFinite(reach[i]);
            int[] available = Enumerable.Range(0, _cells.Length).Where(Reached).ToArray();
            int Choose(Func<int, bool> eligible, Func<int, double> score) => available
                .OrderBy(score).ThenBy(i => i).Where(eligible).DefaultIfEmpty(-1).First();
            int woods = Choose(i => _cells[i].Woodland >= 0.38 && _cells[i].Grass >= 0.08 &&
                Distance(_cells[i].Position, candidate.Beach) >= 25 && Trees(i) >= 3,
                i => Distance(_cells[i].Position, candidate.Beach));
            if (woods < 0) { reason = "no reachable actual woodland"; return false; }
            int clearing = Choose(i => _cells[i].Woodland < 0.30 && _cells[i].Grass >= 0.35 &&
                Distance(_cells[i].Position, _cells[woods].Position) >= 25 && Trees(i) <= 2,
                i => Distance(_cells[i].Position, _cells[woods].Position));
            if (clearing < 0) { reason = "no reachable grassy clearing"; return false; }
            int cliff = Choose(i => Distance(_cells[i].Position, candidate.Cliff.Position) is >= 22 and <= 65 &&
                Vector3D.Dot(_cells[i].Position - candidate.Cliff.Position, candidate.Cliff.Downslope) > 12,
                i => Distance(_cells[i].Position, _cells[clearing].Position));
            if (cliff < 0) { reason = "no reachable front of scanned cliff"; return false; }
            int monument = Choose(i => Distance(_cells[i].Position, candidate.Monument.Position) > candidate.Monument.Definition.Radius + 3 &&
                Distance(_cells[i].Position, candidate.Monument.Position) < candidate.Monument.Definition.Radius + 22,
                i => Distance(_cells[i].Position, _cells[cliff].Position));
            if (monument < 0) { reason = "no reachable small monument exterior"; return false; }
            int water = Choose(i => Distance(_cells[i].Position, _cells[clearing].Position) > 20 && WaterNear(i) != null,
                i => Distance(_cells[i].Position, _cells[clearing].Position) + Distance(_cells[i].Position, _cells[cliff].Position));
            if (water < 0) { reason = "no reachable water bank"; return false; }
            int[] indices = { start, woods, clearing, water, cliff, monument };
            string[] labels = { "beach", "woodland", "clearing", "water", "cliff", "monument" };
            var stops = new List<EnvironmentRouteStop>();
            var legs = new List<EnvironmentRouteLeg>();
            for (int s = 0; s < indices.Length; s++)
            {
                int i = indices[s]; Cell cell = _cells[i];
                Vector3D look = s == 3 ? WaterNear(i)!.Value : s == 4 ? candidate.Cliff.Position + candidate.Cliff.Up * 4 :
                    s == 5 ? candidate.Monument.Position + candidate.Monument.Up * 2 :
                    _cells[indices[Math.Min(s + 1, indices.Length - 1)]].Position;
                string feature = s == 4 ? CliffGeometry.All[candidate.Cliff.Model].Key :
                    s == 5 ? candidate.Monument.Definition.Key : _world.BiomeAt(cell.Position.Normalized).ToString();
                stops.Add(new(labels[s], cell.Position, look, feature, cell.Height, cell.Grade, cell.Grass, cell.Woodland, Trees(i)));
                if (s == 0) continue;
                var (_, parents) = Search(indices[s - 1]);
                var points = new List<Vector3D>();
                for (int p = i; p != -1; p = parents[p]) points.Add(_cells[p].Position);
                points.Reverse();
                double metres = 0;
                for (int p = 1; p < points.Count; p++) metres += Distance(points[p - 1], points[p]);
                legs.Add(new(points, metres));
            }
            if (legs.Sum(l => l.Metres) > MaximumLength) { reason = "connected walk exceeds1200m bound"; return false; }
            plan = new(stops, legs, candidate.Monument.Definition.Key + "/" + candidate.Monument.Tile, checkedCount);
            reason = "sampled connected heightfield corridor; real object collision pending hosted walk";
            return true;
        }

        private int Nearest(Vector3D point, Func<Cell, bool> predicate) => Enumerable.Range(0, _cells.Length)
            .Where(i => predicate(_cells[i])).OrderBy(i => Distance(_cells[i].Position, point)).DefaultIfEmpty(-1).First();

        private (double[] Distance, int[] Parents) Search(int start)
        {
            var distances = Enumerable.Repeat(double.PositiveInfinity, _cells.Length).ToArray();
            var parents = Enumerable.Repeat(-1, _cells.Length).ToArray();
            var queue = new PriorityQueue<int, (double, int)>();
            distances[start] = 0; queue.Enqueue(start, (0, start));
            while (queue.TryDequeue(out int a, out var priority))
            {
                if (priority.Item1 > distances[a]) continue;
                int ax = a % _side, ay = a / _side;
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                {
                    int x = ax + dx, y = ay + dy;
                    if ((dx == 0 && dy == 0) || x < 0 || x >= _side || y < 0 || y >= _side) continue;
                    int b = y * _side + x;
                    if (!_cells[b].Walkable) continue;
                    var key = (Math.Min(a, b), Math.Max(a, b));
                    if (!_edges.TryGetValue(key, out bool allowed))
                        _edges[key] = allowed = Corridor(_world, _cells[a].Position, _cells[b].Position);
                    if (!allowed) continue;
                    double next = distances[a] + Distance(_cells[a].Position, _cells[b].Position) * (1 + _cells[b].Grade);
                    if (next >= distances[b]) continue;
                    distances[b] = next; parents[b] = a; queue.Enqueue(b, (next, b));
                }
            }
            return (distances, parents);
        }

        private int Trees(int i)
        {
            Vector3D position = _cells[i].Position;
            var tiles = new List<TileKey>();
            TileKey.Around(position, 18, _world.Radius, _world.ScatterLevel, tiles);
            int count = 0;
            foreach (TileKey tile in tiles)
            {
                if (!_scatter.TryGetValue(tile, out var items)) _scatter[tile] = items = _world.ScatterFor(tile);
                foreach (ScatterItem item in items)
                    if ((byte)item.Kind < 4 && Distance(item.Direction, position) < 18) count++;
            }
            return count;
        }

        private Vector3D? WaterNear(int i)
        {
            Vector3D at = _cells[i].Position, up = at.Normalized, east = Vector3D.Cross(Vector3D.UnitY, up).Normalized,
                north = Vector3D.Cross(up, east);
            for (int k = 0; k < 8; k++)
            {
                double angle = k * DMath.TwoPi / 8;
                Vector3D direction = (at + (east * DMath.Cos(angle) + north * DMath.Sin(angle)) * 18).Normalized;
                double water = _world.WaterLevelAt(direction);
                if (water > _world.HeightAt(direction) + 0.3) return direction * (_world.Radius + water);
            }
            return null;
        }
    }
}
