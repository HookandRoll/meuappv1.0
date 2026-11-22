using System;
using System.Collections.Generic;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    // 0 ordinary terrain, 1 volcanic apron, 2 independent volcanic center.
    private static byte[] BuildVolcanoes(WorldManifest m, short[] heights, WaterType[] water, MapDomain domain)
    {
        int count = heights.Length;
        var result = new byte[count]; var labels = new int[count]; var distance = new ushort[count];
        Array.Fill(distance, ushort.MaxValue);
        var sizes = new List<int> { 0 }; var oceans = new List<bool> { false }; var depths = new List<int> { 0 };
        var queue = new Queue<int>(); var coast = new Queue<int>();
        var ice = PolarIceField.Enabled(m) ? new PolarIceField(m) : null;
        int land = 0;
        int Neighbor(int i, int d) => HexGrid.NeighborIndex(HexGrid.FromOffset(i % m.Width, i / m.Width), d, m.Width, m.Height, m.HorizontalWrap);
        for (int start = 0; start < count; start++)
        {
            if (!domain.Contains(start) || water[start] != WaterType.None || labels[start] != 0 || (ice?.IsPolar(start) ?? false)) continue;
            int id = sizes.Count, size = 0; bool ocean = false, clipped = false;
            labels[start] = id; queue.Enqueue(start);
            while (queue.TryDequeue(out int i))
            {
                size++; bool shore = false;
                for (int d = 0; d < 6; d++)
                {
                    int n = Neighbor(i, d);
                    if (!domain.Contains(n)) { clipped = true; continue; }
                    if (water[n] != WaterType.None) { shore = true; ocean |= water[n] == WaterType.Ocean; continue; }
                    if (labels[n] == 0 && !(ice?.IsPolar(n) ?? false)) { labels[n] = id; queue.Enqueue(n); }
                }
                if (shore) { distance[i] = 1; coast.Enqueue(i); }
            }
            land += size; sizes.Add(size); oceans.Add(ocean && !clipped); depths.Add(0);
        }
        while (coast.TryDequeue(out int i))
        {
            depths[labels[i]] = Math.Max(depths[labels[i]], distance[i]);
            for (int d = 0; d < 6; d++)
            {
                int n = Neighbor(i, d);
                if (n < 0 || labels[n] != labels[i] || distance[n] != ushort.MaxValue) continue;
                distance[n] = (ushort)(distance[i] + 1); coast.Enqueue(n);
            }
        }
        int maxIsland = Math.Max(40, Math.Min(land / 8, m.Height * m.Height / 12));
        var candidates = new List<int>();
        for (int i = 0; i < count; i++)
        {
            int id = labels[i];
            if (id == 0 || !oceans[id] || sizes[id] > maxIsland || heights[i] <= 2000 || distance[i] < Math.Max(2, depths[id] / 3)) continue;
            bool summit = true, falls = false;
            for (int d = 0; d < 6; d++)
            {
                int n = Neighbor(i, d);
                if (n < 0 || labels[n] != id) continue;
                if (heights[n] > heights[i] || (heights[n] == heights[i] && n < i)) summit = false;
                falls |= heights[n] < heights[i] - 5;
            }
            if (summit && falls) candidates.Add(i);
        }
        candidates.Sort((a, b) => heights[a] != heights[b] ? heights[b].CompareTo(heights[a]) : a.CompareTo(b));
        var centers = new List<int>(); var perIsland = new int[sizes.Count];
        int spacing = Math.Max(4, m.Height / 96);
        var apronArea = new int[sizes.Count];
        foreach (int i in candidates)
        {
            int id = labels[i];
            if (perIsland[id] >= Math.Clamp(1 + sizes[id] / (spacing * spacing * 3), 1, 12)) continue;
            var hex = HexGrid.FromOffset(i % m.Width, i / m.Width);
            bool close = false;
            foreach (int other in centers)
                if (labels[other] == id && HexGrid.Distance(hex, HexGrid.FromOffset(other % m.Width, other / m.Width), m.Width, m.HorizontalWrap) < spacing) { close = true; break; }
            if (close) continue;
            centers.Add(i); perIsland[id]++; result[i] = 2;
            if (m.GeneratorVersion >= 47)
            {
                // Connected lava fields use physical coordinates, warped at two
                // scales. A hex-distance ball becomes a giant regular hexagon.
                float radius47 = Math.Clamp(MathF.Sqrt(sizes[id]) / 10f, 2.4f, Math.Max(3, m.Height / 80f));
                int budget = Math.Max(12, sizes[id] / 8);
                var frontier = new PriorityQueue<int, (float Score, int Cell)>();
                var offered = new HashSet<int> { i };
                frontier.Enqueue(i, (0, i));
                while (frontier.TryDequeue(out int cell, out var rank))
                {
                    if (rank.Score > 1 || (cell != i && apronArea[id] >= budget)) continue;
                    if (result[cell] == 0) { result[cell] = 1; apronArea[id]++; }
                    for (int d = 0; d < 6; d++)
                    {
                        int n = Neighbor(cell, d);
                        if (n < 0 || labels[n] != id || distance[n] <= 1 ||
                            heights[n] < Math.Max(300, heights[i] * 0.18f) || !offered.Add(n)) continue;
                        float dx = n % m.Width + (n / m.Width & 1) * 0.5f - (i % m.Width + (i / m.Width & 1) * 0.5f);
                        if (m.HorizontalWrap) dx -= MathF.Round(dx / m.Width) * m.Width;
                        float dy = (n / m.Width - i / m.Width) * 0.8660254f;
                        float Field(float f, uint salt) => TerrainNoise((n % m.Width + (n / m.Width & 1) * 0.5f) / m.Height,
                            n / m.Width / (float)m.Height, f, m.Width / (float)m.Height, m.HorizontalWrap, unchecked((uint)m.Seed) ^ salt, true);
                        float bend = Field(17, 0xB19735ACu) - 0.5f;
                        float rough = Field(71, 0x19A35C71u) - 0.5f;
                        float score = MathF.Sqrt(dx * dx * 0.8f + dy * dy * 1.25f) / radius47 + bend * 0.8f + rough * 0.35f;
                        frontier.Enqueue(n, (score, n));
                    }
                }
                continue;
            }
            int radius = Math.Clamp((int)Math.Sqrt(sizes[id]) / 5, 2, Math.Max(3, m.Height / 32));
            for (int y = Math.Max(0, i / m.Width - radius); y <= Math.Min(m.Height - 1, i / m.Width + radius); y++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                int x = i % m.Width + dx;
                if (m.HorizontalWrap) x = (x + m.Width) % m.Width;
                if ((uint)x >= m.Width) continue;
                int n = y * m.Width + x;
                if (labels[n] != id || result[n] == 2) continue;
                int dist = HexGrid.Distance(hex, HexGrid.FromOffset(x, y), m.Width, m.HorizontalWrap);
                float noise = TerrainNoise(x / (float)m.Height, y / (float)m.Height, 43, m.Width / (float)m.Height,
                    m.HorizontalWrap, unchecked((uint)m.Seed) ^ 0xB19735ACu);
                if (dist <= radius * (0.65f + noise * 0.5f) && distance[n] > 1) result[n] = 1;
            }
        }
        if (m.GeneratorVersion >= 48)
            return ContractVolcanicAprons(m, heights, labels, result, sizes.Count, distance);
        return result;
    }

    private static byte[] ContractVolcanicAprons(WorldManifest m, short[] heights, int[] labels, byte[] source, int islands, ushort[]? shore = null)
    {
        var result = new byte[source.Length]; var budget = new int[islands]; var used = new int[islands];
        var minimumHeight = new int[islands]; Array.Fill(minimumHeight, int.MaxValue);
        var front = new PriorityQueue<int, (float Cost, int Cell)>();
        var costs = new Dictionary<int, float>();
        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] == 1) budget[labels[i]]++;
            if (source[i] == 2)
            {
                result[i] = 2; costs[i] = 0; front.Enqueue(i, (0, i));
                minimumHeight[labels[i]] = Math.Min(minimumHeight[labels[i]], Math.Max(300, (int)(heights[i] * 0.18f)));
            }
        }
        for (int i = 0; i < islands; i++) budget[i] /= 2;
        while (front.TryDequeue(out int cell, out var rank))
        {
            if (costs[cell] != rank.Cost) continue;
            int id = labels[cell];
            if (source[cell] != 2)
            {
                if (used[id] >= budget[id] || result[cell] != 0) continue;
                result[cell] = 1; used[id]++;
            }
            var hex = HexGrid.FromOffset(cell % m.Width, cell / m.Width);
            for (int d = 0; d < 6; d++)
            {
                int n = HexGrid.NeighborIndex(hex, d, m.Width, m.Height, m.HorizontalWrap);
                // The old mask supplies only the area budget. New flows can
                // follow a valley beyond that circular outline, within the same
                // island and above its low/coastal terrain.
                if (n < 0 || labels[n] != id || result[n] != 0 || heights[n] < minimumHeight[id] ||
                    (shore is not null && shore[n] <= 1)) continue;
                float x = (n % m.Width + (n / m.Width & 1) * 0.5f) / m.Height, y = n / m.Width / (float)m.Height;
                float flow = TerrainNoise(x, y, 37, m.Width / (float)m.Height, m.HorizontalWrap, unchecked((uint)m.Seed) ^ 0x47A13C95u);
                float fine = TerrainNoise(x, y, m.Height / 1.8f, m.Width / (float)m.Height, m.HorizontalWrap, unchecked((uint)m.Seed) ^ 0x5731A9CDu);
                float rise = Math.Max(0, heights[n] - heights[cell]);
                float fall = Math.Max(0, heights[cell] - heights[n]);
                float travel = (0.18f + flow * flow * 2 + fine * fine * fine * 7) / (1 + Math.Min(6, fall / 120f));
                float cost = rank.Cost + travel + rise / 45f;
                if (costs.TryGetValue(n, out float old) && old <= cost) continue;
                costs[n] = cost; front.Enqueue(n, (cost, n));
            }
        }
        return result;
    }
}
