using System;
using System.Collections.Generic;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    private sealed partial class ContinentalSculpture
    {
        // A spatial field, not one random radius per dab/segment. Ridges in the
        // field give branching inlets; positive patches leave headlands.
        private float WaterMarginField(int col, int row)
        {
            float x = (col + (row & 1) * 0.5f) / _height, y = row / (float)_height;
            float aspect = _width / (float)_height;
            float NoiseAt(float px, float py, float f, uint salt, bool angular = false) =>
                TerrainNoise(px, py, f, aspect, _wrap, _seed ^ salt, angular);
            float wx = x + (NoiseAt(x, y, 9f, 0x28C715A3u) - 0.5f) * 0.035f;
            float wy = y + (NoiseAt(x, y, 9f, 0x4E193B87u) - 0.5f) * 0.035f;
            float broad = NoiseAt(wx, wy, 13f, 0x7A3185C9u) * 2 - 1;
            float branch = Math.Abs(NoiseAt(wx, wy, 31f, 0xA5C73149u, true) * 2 - 1) * 2 - 0.65f;
            float fine = NoiseAt(wx, wy, 79f, 0x91D4B26Fu, true) * 2 - 1;
            return Math.Clamp(broad * 0.45f + branch * 0.40f + fine * 0.15f, -1, 1);
        }

        private float WaterWidthFactor(int col, int row)
        {
            float broad = FieldNoise(col, row, 9.7f, _seed ^ 0xD7A53189u);
            float detail = FieldNoise(col, row, 31f, _seed ^ 0x52C19E73u);
            return Math.Clamp(1 + ((broad - 0.5f) * 1.35f + (detail - 0.5f) * 0.45f) * (0.4f + _coastDetail * 0.6f), 0.5f, 1.8f);
        }

        private void WidenNaturalChannels(bool[] channel, bool[] fixedCore, int halfWidth)
        {
            var distance = new ushort[channel.Length]; Array.Fill(distance, ushort.MaxValue);
            var flood = new PriorityQueue<int, (int Distance, int Cell)>();
            int budget = (int)MathF.Round((halfWidth + 0.4f) * 256);
            for (int i = 0; i < channel.Length; i++)
                if (channel[i]) { distance[i] = 0; flood.Enqueue(i, (0, i)); }
            while (flood.TryDequeue(out int i, out var priority))
            {
                if (priority.Distance != distance[i]) continue;
                Hex hex = HexAt(i);
                for (int d = 0; d < 6; d++)
                {
                    int n = Neighbor(hex, d);
                    if (n < 0 || fixedCore[n] || !_domain.Contains(n)) continue;
                    // Every addition is reached from the connected channel core.
                    float factor = Math.Max(1f / (halfWidth + 0.4f), WaterWidthFactor(n % _width, n / _width));
                    int cost = priority.Distance + (int)MathF.Round(256 / factor);
                    if (cost > budget || cost >= distance[n]) continue;
                    distance[n] = (ushort)cost; channel[n] = true;
                    flood.Enqueue(n, (cost, n));
                }
            }
        }

        // Hex simple-point flips preserve the connected components of BOTH
        // land and water. A narrow outlet/bridge cannot be sealed or severed.
        private bool SimpleWaterMargin(WaterType[] water, int cell)
        {
            Hex hex = HexAt(cell);
            int transitions = 0;
            bool first = false, previous = false;
            for (int d = 0; d < 6; d++)
            {
                int n = Neighbor(hex, d);
                if (n < 0 || !_domain.Contains(n)) return false;
                bool land = water[n] == WaterType.None;
                if (d == 0) first = land;
                else if (land != previous) transitions++;
                previous = land;
            }
            if (previous != first) transitions++;
            return transitions == 2;
        }

        private void SculptWaterMargins(WaterType[] water, float sea)
        {
            int radius = Math.Clamp(_height / 128, 1, 24);
            var distance = new byte[water.Length];
            var pending = new Queue<int>();
            int boundary = 0;
            for (int i = 0; i < water.Length; i++)
            {
                if (!AtlasLandEligible(i) || !float.IsFinite(_scores[i]) || Math.Abs(_scores[i] - sea) > 0.5f) continue;
                Hex hex = HexAt(i);
                bool land = water[i] == WaterType.None;
                for (int d = 0; d < 6; d++)
                {
                    int n = Neighbor(hex, d);
                    if (n < 0 || !_domain.Contains(n) || (water[n] == WaterType.None) == land) continue;
                    distance[i] = 1; pending.Enqueue(i); boundary++; break;
                }
            }
            if (boundary == 0) return;
            // Pair edits on the same original mainland/island, so even a small
            // continent keeps its area instead of being eroded down to a pixel.
            var owner = new int[water.Length];
            int labels = 0;
            var labelQueue = new Queue<int>();
            for (int start = 0; start < water.Length; start++)
            {
                if (water[start] != WaterType.None || owner[start] != 0) continue;
                owner[start] = ++labels; labelQueue.Enqueue(start);
                while (labelQueue.TryDequeue(out int i))
                {
                    Hex hex = HexAt(i);
                    for (int d = 0; d < 6; d++)
                    {
                        int n = Neighbor(hex, d);
                        if (n < 0 || water[n] != WaterType.None || owner[n] != 0) continue;
                        owner[n] = labels; labelQueue.Enqueue(n);
                    }
                }
            }
            int ShoreOwner(int i)
            {
                int label = 0; Hex hex = HexAt(i);
                for (int d = 0; d < 6; d++)
                {
                    int n = Neighbor(hex, d);
                    if (n < 0 || water[n] != WaterType.None) continue;
                    if (label != 0 && label != owner[n]) return 0;
                    label = owner[n];
                }
                return label;
            }
            var states = new byte[water.Length]; // queued erosion/accretion, then permanently changed
            var erosion = new PriorityQueue<int, (float Cost, int Cell)>();
            var accretion = new Dictionary<int, PriorityQueue<int, (float Cost, int Cell)>>();
            void Offer(int i)
            {
                if (distance[i] == 0 || (states[i] & 4) != 0) return;
                bool land = water[i] == WaterType.None;
                byte flag = land ? (byte)1 : (byte)2;
                if ((states[i] & flag) != 0) return;
                int label = land ? owner[i] : ShoreOwner(i);
                if (label == 0) return;
                float field = WaterMarginField(i % _width, i / _width);
                float cost = Math.Abs(_scores[i] - sea) * 1.5f + (land ? field : -field) * (0.20f + _coastDetail * 0.25f)
                    + (distance[i] - 1) * 0.08f;
                states[i] |= flag;
                if (land) erosion.Enqueue(i, (cost, i));
                else
                {
                    if (!accretion.TryGetValue(label, out var coast)) accretion[label] = coast = new();
                    coast.Enqueue(i, (cost, i));
                }
            }
            // A bounded band around the ORIGINAL shore prevents erosion from
            // wandering into mountain interiors or across a whole continent.
            while (pending.TryDequeue(out int i))
            {
                if (distance[i] == 1) Offer(i);
                if (distance[i] >= radius) continue;
                Hex hex = HexAt(i);
                for (int d = 0; d < 6; d++)
                {
                    int n = Neighbor(hex, d);
                    if (n < 0 || distance[n] != 0 || !AtlasLandEligible(n) || !float.IsFinite(_scores[n]) || Math.Abs(_scores[n] - sea) > 0.5f) continue;
                    distance[n] = (byte)(distance[i] + 1); pending.Enqueue(n);
                }
            }
            bool Take(bool land, int label, out int cell)
            {
                cell = -1;
                if (!land && !accretion.ContainsKey(label)) return false;
                var queue = land ? erosion : accretion[label];
                while (queue.TryDequeue(out cell, out var priority))
                {
                    states[cell] &= land ? (byte)0xfe : (byte)0xfd;
                    if ((states[cell] & 4) != 0 || (water[cell] == WaterType.None) != land || priority.Cost > 0.22f) continue;
                    if (!land && ShoreOwner(cell) != label) continue;
                    // Avoid growing one-cell fingers or drilling single-cell
                    // bites: work along a supported stretch of the shoreline.
                    int support = 0; Hex hex = HexAt(cell);
                    for (int d = 0; d < 6; d++)
                    {
                        int n = Neighbor(hex, d);
                        if (n >= 0 && (water[n] == WaterType.None) != land) support++;
                    }
                    if (support < (land ? 2 : 3)) continue;
                    if (SimpleWaterMargin(water, cell)) return true;
                }
                cell = -1; return false;
            }
            void OfferNeighbors(int i)
            {
                Hex hex = HexAt(i);
                for (int d = 0; d < 6; d++) { int n = Neighbor(hex, d); if (n >= 0 && distance[n] != 0) Offer(n); }
            }
            int budgetPairs = (int)Math.Min(_domain.ActiveCount * 0.012f, boundary * radius * (0.10f + _coastDetail * 0.20f));
            for (int pair = 0; pair < budgetPairs && Take(true, 0, out int cut); pair++)
            {
                float original = _scores[cut];
                int label = owner[cut];
                water[cut] = WaterType.Lake; states[cut] |= 4;
                owner[cut] = 0;
                // Test the opposite operation AFTER the first edit: two individually
                // safe candidates can interfere if committed without rechecking.
                if (!Take(false, label, out int fill))
                { water[cut] = WaterType.None; owner[cut] = label; continue; }
                water[fill] = WaterType.None; states[fill] |= 4;
                owner[fill] = label;
                _scores[cut] = sea - Math.Clamp(0.03f + Math.Abs(original - sea) * 0.25f, 0.03f, 0.18f);
                _scores[fill] = sea + Math.Clamp(0.03f + Math.Abs(_scores[fill] - sea) * 0.25f, 0.03f, 0.18f);
                OfferNeighbors(cut); OfferNeighbors(fill);
            }
        }
    }
}
