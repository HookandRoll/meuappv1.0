using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    private sealed partial class ContinentalSculpture
    {
        // Split joined mainlands at their drainage saddles. The original plate
        // surface ranks every flood and regrowth; no equal area is assigned to a continent.
        private void SeparatePlateContinents(WaterType[] water, float sea)
        {
            int requested = _settings.Continents;
            if (requested <= 1) return;
            int land = water.Count(w => w == WaterType.None);
            int minimum = Math.Max(3, land / (requested * 12));
            var visited = new bool[water.Length];
            var componentIndex = new int[water.Length];
            var queue = new Queue<int>();
            var components = new List<(int Size, int Peak)>();
            for (int start = 0; start < water.Length; start++)
            {
                if (visited[start] || water[start] != WaterType.None) continue;
                visited[start] = true; componentIndex[start] = components.Count + 1; queue.Enqueue(start);
                int size = 0, peak = start;
                while (queue.TryDequeue(out int i))
                {
                    size++;
                    if (_scores[i] > _scores[peak]) peak = i;
                    Hex hex = HexAt(i);
                    for (int d = 0; d < 6; d++)
                    {
                        int n = Neighbor(hex, d);
                        if (n < 0 || visited[n] || water[n] != WaterType.None) continue;
                        visited[n] = true; componentIndex[n] = componentIndex[i]; queue.Enqueue(n);
                    }
                }
                components.Add((size, peak));
            }
            var mainlands = components.Where(c => c.Size >= minimum).OrderByDescending(c => c.Size).ToArray();
            if (mainlands.Length >= requested) return;
            var anchors = mainlands.Select(c => c.Peak).ToList();
            if (anchors.Count == 0) return;
            while (anchors.Count < requested)
            {
                int best = -1; float bestRank = -1;
                for (int i = 0; i < water.Length; i++)
                {
                    if (water[i] != WaterType.None || components[componentIndex[i] - 1].Size < minimum) continue;
                    Vector2 point = HexGrid.Center(HexAt(i));
                    float nearest = float.PositiveInfinity;
                    foreach (int anchor in anchors)
                        nearest = Math.Min(nearest, WrappedDelta(point, HexGrid.Center(HexAt(anchor))).LengthSquared());
                    if (nearest < 27f) continue;
                    float rank = nearest * (0.5f + 0.5f * Math.Clamp(_scores[i] - sea, 0, 1));
                    if (rank > bestRank) { best = i; bestRank = rank; }
                }
                if (best < 0) break;
                anchors.Add(best);
            }
            var owner = new byte[water.Length];
            var fixedCore = new bool[water.Length];
            for (int i = 0; i < owner.Length; i++) if (water[i] == WaterType.None) owner[i] = byte.MaxValue;
            var flood = new PriorityQueue<int, (float Level, int Cell)>();
            for (int j = 0; j < anchors.Count; j++)
            {
                int i = anchors[j];
                owner[i] = (byte)(j + 1); fixedCore[i] = true;
                flood.Enqueue(i, (0, i));
            }
            while (flood.TryDequeue(out int i, out var priority))
            {
                Hex hex = HexAt(i);
                for (int d = 0; d < 6; d++)
                {
                    int n = Neighbor(hex, d);
                    if (n < 0 || owner[n] != byte.MaxValue) continue;
                    owner[n] = owner[i];
                    // Bounded travel cost lets every core claim a broad area;
                    // low passes slow the front without trapping a lowland seed.
                    float cost = 1.8f - 0.8f * Math.Clamp(_scores[n] - sea, 0, 1);
                    flood.Enqueue(n, (priority.Level + cost, n));
                }
            }
            var channel = new bool[water.Length];
            for (int i = 0; i < owner.Length; i++)
            {
                if (water[i] != WaterType.None) continue;
                Hex hex = HexAt(i);
                for (int d = 0; d < 6; d++)
                {
                    int n = Neighbor(hex, d);
                    if (n < 0 || water[n] != WaterType.None || owner[i] == owner[n]) continue;
                    int cut = fixedCore[i] ? n : fixedCore[n] ? i : _scores[i] < _scores[n] ? i : n;
                    channel[cut] = true;
                }
            }
            // Give the passages visible sea width rather than a one-hex line
            // that reads as a river at world scale. Preserve every fixed core.
            int halfWidth = Math.Max(1, _height / 192);
            var reach = new byte[water.Length];
            for (int i = 0; i < channel.Length; i++)
                if (channel[i]) { reach[i] = 1; queue.Enqueue(i); }
            while (queue.TryDequeue(out int i))
            {
                if (reach[i] > halfWidth) continue;
                Hex hex = HexAt(i);
                for (int d = 0; d < 6; d++)
                {
                    int n = Neighbor(hex, d);
                    if (n < 0 || reach[n] != 0 || fixedCore[n]) continue;
                    reach[n] = (byte)(reach[i] + 1); channel[n] = true; queue.Enqueue(n);
                }
            }
            int removed = 0;
            for (int i = 0; i < channel.Length; i++)
                if (channel[i] && water[i] == WaterType.None) { water[i] = WaterType.Lake; removed++; }

            // Regrow only eligible coast cells with one neighboring mainland.
            // Recheck on dequeue so two fronts can never rebuild a removed bridge.
            int CoastOwner(int i)
            {
                int candidate = 0;
                Hex hex = HexAt(i);
                for (int d = 0; d < 6; d++)
                {
                    int n = Neighbor(hex, d);
                    if (n < 0 || water[n] != WaterType.None) continue;
                    if (candidate != 0 && candidate != owner[n]) return 0;
                    candidate = owner[n];
                }
                return candidate;
            }
            Array.Clear(visited);
            var coast = new PriorityQueue<int, (float Score, int Cell)>();
            void Offer(int i)
            {
                if (water[i] == WaterType.None || channel[i] || visited[i] || !AtlasLandEligible(i) || CoastOwner(i) == 0) return;
                visited[i] = true; coast.Enqueue(i, (-_scores[i], i));
            }
            for (int i = 0; i < water.Length; i++) if (water[i] != WaterType.None) Offer(i);
            while (removed > 0 && coast.TryDequeue(out int i, out _))
            {
                int label = CoastOwner(i);
                if (label == 0) continue;
                water[i] = WaterType.None; owner[i] = (byte)label; removed--;
                Hex hex = HexAt(i);
                for (int d = 0; d < 6; d++) { int n = Neighbor(hex, d); if (n >= 0) Offer(n); }
            }
            if (removed != 0) throw new InvalidOperationException("Não foi possível separar os continentes mantendo a área de terra.");
        }
    }
}
