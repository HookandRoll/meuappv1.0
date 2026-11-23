using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    private sealed partial class ContinentalSculpture
    {
        private void AddSmallIslands(WaterType[] water, float[] uplift, float sea)
        {
            var owner = new int[water.Length];
            var sizes = new List<int> { 0 };
            var queue = new Queue<int>();
            for (int start = 0; start < water.Length; start++)
            {
                if (water[start] != WaterType.None || owner[start] != 0) continue;
                int id = sizes.Count, size = 0;
                owner[start] = id; queue.Enqueue(start);
                while (queue.TryDequeue(out int cell))
                {
                    size++;
                    for (int d = 0; d < 6; d++)
                    {
                        int n = Neighbor(HexAt(cell), d);
                        if (n < 0 || water[n] != WaterType.None || owner[n] != 0) continue;
                        owner[n] = id; queue.Enqueue(n);
                    }
                }
                sizes.Add(size);
            }
            int land = sizes.Sum(), mainlandMin = Math.Max(32, (int)Math.Ceiling(land * 0.05));
            bool atlas = _manifest.GeneratorVersion >= 53 && _domain.IsAtlas;
            int budget = atlas ? Math.Min(512, (int)(land * 0.006)) : Math.Min(192, (int)(land * 0.003));
            int maxGroups = atlas ? Math.Clamp(_height / 2, 24, 128) : 24;
            if (budget < 3) return;
            var marine = (WaterType[])water.Clone();
            LabelOpenSea(marine, queue);
            var distance = new byte[water.Length];
            Array.Fill(distance, byte.MaxValue);
            var nearest = (int[])owner.Clone();
            for (int cell = 0; cell < water.Length; cell++)
                if (owner[cell] != 0) { distance[cell] = 0; queue.Enqueue(cell); }
            while (queue.TryDequeue(out int cell))
            {
                if (distance[cell] >= 10) continue;
                for (int d = 0; d < 6; d++)
                {
                    int n = Neighbor(HexAt(cell), d);
                    if (n < 0 || distance[n] != byte.MaxValue || !AtlasLandEligible(n)) continue;
                    distance[n] = (byte)(distance[cell] + 1); nearest[n] = nearest[cell]; queue.Enqueue(n);
                }
            }
            uint Rank(int cell)
            {
                uint h = unchecked((uint)cell * 0x9E3779B9u ^ _seed ^ 0x79C315A9u);
                h = unchecked((h ^ (h >> 16)) * 0x7FEB352Du);
                return h ^ (h >> 15);
            }
            bool Clear(int cell) => marine[cell] == WaterType.Ocean && AtlasLandEligible(cell) &&
                Enumerable.Range(0, 6).All(d =>
                {
                    int n = Neighbor(HexAt(cell), d);
                    return n >= 0 && water[n] != WaterType.None;
                });
            IEnumerable<int> Candidates()
            {
                int capacity = Math.Max(2048, maxGroups * 16);
                var eligible = new BitArray(water.Length);
                for (int i = 0; i < water.Length; i++)
                    eligible[i] = distance[i] is >= 3 and <= 9 && marine[i] == WaterType.Ocean &&
                        (sizes[nearest[i]] < mainlandMin || uplift[i] >= 0.12f || _scores[i] > sea - 0.20f);
                (float Rank, int NegCell)? ceiling = null;
                while (true)
                {
                    var best = new PriorityQueue<int, (float Rank, int NegCell)>();
                    for (int i = 0; i < water.Length; i++)
                    {
                        if (!eligible[i]) continue;
                        var key = (uplift[i] + (sizes[nearest[i]] < mainlandMin ? 0.3f : 0) + (Rank(i) & 255) * 0.001f, -i);
                        if (ceiling.HasValue && key.CompareTo(ceiling.Value) >= 0) continue;
                        if (best.Count < capacity) best.Enqueue(i, key);
                        else best.EnqueueDequeue(i, key);
                    }
                    if (best.Count == 0) yield break;
                    // Refill below the last rank if rejected patches exhaust this batch.
                    // A fixed Top-K would silently drop later viable island centers.
                    foreach (var item in best.UnorderedItems.OrderByDescending(item => item.Priority))
                    {
                        ceiling = item.Priority;
                        yield return item.Element;
                    }
                }
            }
            var candidates = Candidates();
            var centers = new List<int>();
            var eroded = new int[sizes.Count];
            int radius = Math.Clamp(_height / 16, 16, 32);
            foreach (int center in candidates)
            {
                if (budget < 1 || centers.Count >= maxGroups) break;
                int Distance(int a, int b) => HexGrid.Distance(HexAt(a), HexAt(b), _width, _wrap);
                if (!Clear(center) || centers.Any(c => Distance(c, center) < 5)) continue;
                bool companion = sizes[nearest[center]] < mainlandMin || centers.Any(c => Distance(c, center) <= 10);
                int goal = companion && Rank(center) % 3 == 0 ? 1 : 3 + (int)(Rank(center) % 7);
                if (goal > budget) continue;
                var patch = new HashSet<int>();
                var front = new PriorityQueue<int, (float Cost, int Cell)>();
                var offered = new HashSet<int> { center };
                front.Enqueue(center, (0, center));
                while (patch.Count < goal && front.TryDequeue(out int cell, out _))
                {
                    if (!Clear(cell)) continue;
                    patch.Add(cell);
                    for (int d = 0; d < 6; d++)
                    {
                        int n = Neighbor(HexAt(cell), d);
                        if (n < 0 || !offered.Add(n)) continue;
                        front.Enqueue(n, (Distance(center, n) + (Rank(n) & 255) / 512f, n));
                    }
                }
                if (patch.Count != goal) continue;

                // Only continental shore cells can fund an addition. Original islands
                // are immutable; simple-point erosion cannot sever land or open holes.
                var donors = new HashSet<int>();
                for (int dy = -radius; dy <= radius; dy++)
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int y = center / _width + dy, x = center % _width + dx;
                    if (_wrap) x = (x + _width) % _width;
                    if ((uint)y >= _height || (uint)x >= _width) continue;
                    int cell = y * _width + x, id = owner[cell];
                    if (id == 0 || sizes[id] < mainlandMin || water[cell] != WaterType.None ||
                        !AtlasLandEligible(cell) || uplift[cell] > 0.35f || Distance(center, cell) > radius) continue;
                    donors.Add(cell);
                }
                var cuts = new List<int>();
                foreach (int cell in donors.OrderBy(i => Distance(center, i) + Math.Abs(_scores[i] - sea) * 30).ThenBy(i => i))
                {
                    int id = owner[cell];
                    if (eroded[id] >= sizes[id] / 100 || !SimpleWaterMargin(water, cell)) continue;
                    int wet = Enumerable.Range(0, 6).Count(d => water[Neighbor(HexAt(cell), d)] != WaterType.None);
                    if (wet is < 2 or > 4) continue;
                    cuts.Add(cell); eroded[id]++; water[cell] = WaterType.Ocean;
                    if (cuts.Count == goal) break;
                }
                if (cuts.Count != goal)
                {
                    foreach (int cell in cuts) { water[cell] = WaterType.None; eroded[owner[cell]]--; }
                    continue;
                }
                foreach (int cell in cuts) _scores[cell] = sea - 0.03f;
                foreach (int cell in patch) { water[cell] = WaterType.None; _scores[cell] = sea + 0.03f; }
                centers.Add(center); budget -= goal;
            }
        }
    }
}
