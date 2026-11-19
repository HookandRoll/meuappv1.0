using System;
using System.Collections.Generic;
using System.Linq;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    private static int CurveStraightReaches(WorldData world, int[] parent,
        List<int> order, double[] flux, double threshold)
    {
        int count = world.Count;
        bool Active(int i) => i >= 0 && parent[i] >= 0 && world.Water[i] == WaterType.None && flux[i] >= threshold;
        var incoming = new byte[count];
        for (int i = 0; i < count; i++) if (Active(i) && incoming[parent[i]] < 2) incoming[parent[i]]++;
        var touched = new bool[count];
        int changed = 0;
        foreach (int source in order)
        {
            if (!Active(source) || touched[source] || world.Relief[source] >= ReliefType.Mountains) continue;
            int direction = RiverDirection(world, source, parent[source]);
            var original = new List<int> { source };
            int current = source;
            for (int step = 0; step < 14; step++)
            {
                int next = parent[current];
                if (!Active(next) || touched[next] || incoming[next] > 1 ||
                    world.Relief[next] >= ReliefType.Mountains || RiverDirection(world, current, next) != direction) break;
                original.Add(next); current = next;
            }
            if (original.Count < 8) continue;
            foreach (int cell in original) touched[cell] = true;
            var first = HexGrid.Center(HexGrid.FromOffset(source % world.Width, source / world.Width));
            var last = HexGrid.Center(HexGrid.FromOffset(current % world.Width, current / world.Width));
            float span = world.Width * HexGrid.Root3;
            if (world.Manifest.HorizontalWrap) last.X -= MathF.Round((last.X - first.X) / span) * span;
            var forward = System.Numerics.Vector2.Normalize(last - first);
            var lateral = new System.Numerics.Vector2(-forward.Y, forward.X);
            var originals = original.ToHashSet();
            for (int side = 0; side < 2; side++)
            {
                float sign = ((source ^ world.Manifest.Seed) & 1) == side ? 1 : -1;
                var path = new List<int> { source }; var unique = new HashSet<int> { source };
                bool valid = true, joined = false;
                int samples = (original.Count - 1) * 5;
                for (int step = 1; step <= samples; step++)
                {
                    float t = step / (float)samples;
                    var point = System.Numerics.Vector2.Lerp(first, last, t) + lateral * (MathF.Sin(MathF.PI * t) * sign * HexGrid.Root3 * 1.4f);
                    var (col, row) = HexGrid.ToOffset(HexGrid.Pick(point));
                    if (world.Manifest.HorizontalWrap) col = (col % world.Width + world.Width) % world.Width;
                    if ((uint)col >= world.Width || (uint)row >= world.Height) { valid = false; break; }
                    int cell = row * world.Width + col, prior = path[^1];
                    if (cell == prior) continue;
                    if (!unique.Add(cell) || world.Water[cell] != WaterType.None ||
                        world.Elevation[cell] > world.Elevation[prior] + 2 || world.Elevation[cell] > world.Elevation[source] + 2 ||
                        RiverDirection(world, prior, cell) < 0)
                    { valid = false; break; }
                    path.Add(cell);
                    if (!originals.Contains(cell) && (Active(cell) || touched[cell]))
                    { joined = true; break; }
                }
                if (!valid || (!joined && path[^1] != current) || path.SequenceEqual(original)) continue;
                int endpoint = path[^1];
                // The downstream ancestry must not return into the proposed arc.
                // This permits lateral bends that the original flood visit rank would
                // forbid, while preserving an acyclic network exactly.
                for (int downstream = parent[endpoint]; downstream >= 0; downstream = parent[downstream])
                    if (unique.Contains(downstream)) { valid = false; break; }
                if (!valid) continue;
                for (int j = 0; j < path.Count - 1; j++) { parent[path[j]] = path[j + 1]; touched[path[j]] = true; }
                changed++; break;
            }
        }
        return changed;
    }

    // Join headwaters with an explicit bounded search. A river approaches the other
    // spring's downstream course, not an uphill source on the far side of a divide.
    private static int JoinNearbyHeadwaters(WorldData world, int[] parent, short[] level,
        List<int> order, double[] flux, double threshold)
    {
        int count = world.Count, radius = Math.Max(6, world.Height / 128);
        var rank = new int[count];
        for (int i = 0; i < order.Count; i++) rank[order[i]] = i;
        bool Candidate(int i) => parent[i] >= 0 && world.Water[i] == WaterType.None && flux[i] >= threshold;
        var incoming = new byte[count];
        for (int i = 0; i < count; i++) if (Candidate(i)) incoming[parent[i]] = 1;
        var sources = new List<int>();
        var bins = new Dictionary<(int X, int Y), List<int>>();
        int columns = (world.Width + radius - 1) / radius;
        for (int i = 0; i < count; i++)
        {
            if (!Candidate(i) || incoming[i] != 0 || (world.Temperature[i] > 100 && world.Rainfall[i] < 450)) continue;
            sources.Add(i);
            var key = (i % world.Width / radius, i / world.Width / radius);
            if (!bins.TryGetValue(key, out var list)) bins[key] = list = new();
            list.Add(i);
        }
        sources.Sort((a, b) => rank[a].CompareTo(rank[b]));
        var costs = new Dictionary<int, float>();
        var previous = new Dictionary<int, int>();
        var queue = new PriorityQueue<int, (float Cost, int Cell)>();
        int merged = 0;
        foreach (int source in sources)
        {
            Hex origin = HexGrid.FromOffset(source % world.Width, source / world.Width);
            int bx = source % world.Width / radius, by = source / world.Width / radius;
            int nearest = -1, nearestDistance = radius + 1;
            for (int y = by - 1; y <= by + 1; y++)
            for (int x = bx - 1; x <= bx + 1; x++)
            {
                int col = world.Manifest.HorizontalWrap ? (x % columns + columns) % columns : x;
                if (!bins.TryGetValue((col, y), out var list)) continue;
                foreach (int other in list)
                {
                    if (rank[other] <= rank[source] || other == source) continue;
                    int distance = HexGrid.Distance(origin, HexGrid.FromOffset(other % world.Width, other / world.Width), world.Width, world.Manifest.HorizontalWrap);
                    if (distance < nearestDistance || (distance == nearestDistance && other < nearest))
                    { nearest = other; nearestDistance = distance; }
                }
            }
            if (nearest < 0 || nearestDistance > radius) continue;
            int target = nearest;
            for (int step = 0; step < radius * 2 && target >= 0; step++, target = parent[target])
            {
                if (world.Water[target] != WaterType.None) { target = -1; break; }
                if (rank[target] > rank[source] && level[target] <= level[source] && world.Elevation[target] <= world.Elevation[source]) break;
            }
            if (target < 0 || rank[target] <= rank[source] || level[target] > level[source] || world.Elevation[target] > world.Elevation[source]) continue;
            Hex targetHex = HexGrid.FromOffset(target % world.Width, target / world.Width);
            if (HexGrid.Distance(origin, targetHex, world.Width, world.Manifest.HorizontalWrap) > radius * 2) continue;
            costs.Clear(); previous.Clear(); queue.Clear();
            costs[source] = 0; queue.Enqueue(source, (0, source));
            int expanded = 0;
            bool found = false;
            while (queue.TryDequeue(out int cell, out var priority) && ++expanded <= 256)
            {
                if (cell == target) { found = true; break; }
                float heuristic = HexGrid.Distance(HexGrid.FromOffset(cell % world.Width, cell / world.Width), targetHex, world.Width, world.Manifest.HorizontalWrap);
                if (priority.Cost > costs[cell] + heuristic + 0.001f) continue;
                Hex hex = HexGrid.FromOffset(cell % world.Width, cell / world.Width);
                for (int d = 0; d < 6; d++)
                {
                    int n = HexGrid.NeighborIndex(hex, d, world.Width, world.Height, world.Manifest.HorizontalWrap);
                    if (n < 0 || world.Water[n] != WaterType.None || rank[n] <= rank[cell] ||
                        level[n] > level[cell] || world.Elevation[n] > world.Elevation[cell] + 6 ||
                        world.Elevation[n] > world.Elevation[source] + 6) continue;
                    if (HexGrid.Distance(origin, HexGrid.FromOffset(n % world.Width, n / world.Width), world.Width, world.Manifest.HorizontalWrap) > radius * 2) continue;
                    float valley = Noise(n % world.Width * 0.17f, n / world.Width * 0.17f, unchecked((uint)world.Manifest.Seed) ^ 0x1386ACu);
                    float next = costs[cell] + 1 + valley * 2 + Math.Max(0, world.Elevation[n] - world.Elevation[cell]) * 0.3f;
                    if (costs.TryGetValue(n, out float old) && old <= next) continue;
                    costs[n] = next; previous[n] = cell;
                    int distance = HexGrid.Distance(HexGrid.FromOffset(n % world.Width, n / world.Width), targetHex, world.Width, world.Manifest.HorizontalWrap);
                    queue.Enqueue(n, (next + distance, n));
                }
            }
            if (!found) continue;
            int current = target;
            while (current != source)
            {
                int prior = previous[current]; parent[prior] = current; current = prior;
            }
            merged++;
        }
        return merged;
    }
}
