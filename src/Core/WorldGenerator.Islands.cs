using System;
using System.Collections.Generic;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    private sealed partial class ContinentalSculpture
    {
        private int AddIslandChains(WaterType[] water, int budget, out bool[] reserved, Action<GenerationProgress>? progress = null)
        {
            var random = new Random(unchecked((int)(_seed ^ 0xD249BC31u)));
            int coastGap = _spacedIslands ? Math.Max(4, _height / 32) : 3;
            int islandGap = _spacedIslands ? Math.Max(4, _height / 48) : 2;
            int edgeMargin = Math.Max(3, _height / 32);
            var distance = new int[water.Length];
            Array.Fill(distance, int.MaxValue);
            var queue = new Queue<int>();
            for (int i = 0; i < water.Length; i++)
            {
                if (water[i] != WaterType.None)
                    continue;
                distance[i] = 0;
                queue.Enqueue(i);
            }
            while (queue.TryDequeue(out int cell))
            {
                Hex hex = HexAt(cell);
                for (int direction = 0; direction < 6; direction++)
                {
                    int neighbor = Neighbor(hex, direction);
                    if (neighbor < 0 || distance[neighbor] <= distance[cell] + 1)
                        continue;
                    distance[neighbor] = distance[cell] + 1;
                    queue.Enqueue(neighbor);
                }
            }

            if (_compactCoasts) progress?.Invoke(new(GenerationStep.Land, 86));
            // Evaluate tectonic arcs on a bounded lattice, not millions of ocean cells.
            float ArcAt(int col, int row)
            {
                if (_tectonics is not null)
                {
                    var point = HexGrid.Center(HexAt(row * _width + col));
                    float arc = 0;
                    foreach (var contact in _tectonics.Contacts)
                    {
                        if (contact.Compression <= 0.25f || (_tectonics.Plates[contact.A].Continental && _tectonics.Plates[contact.B].Continental)) continue;
                        var a = new System.Numerics.Vector2(contact.X, contact.Y);
                        var b = new System.Numerics.Vector2(contact.EndX, contact.EndY);
                        var delta = WrappedDelta(a, point);
                        var segment = b - a;
                        float t = Math.Clamp(System.Numerics.Vector2.Dot(delta, segment) / Math.Max(0.001f, segment.LengthSquared()), 0, 1);
                        float distanceToArc = (delta - segment * t).Length();
                        if (_strongTectonics)
                        {
                            var pa = _tectonics.Plates[contact.A];
                            var pb = _tectonics.Plates[contact.B];
                            var normal = System.Numerics.Vector2.Normalize(WrappedDelta(new(pa.X, pa.Y), new(pb.X, pb.Y)));
                            bool upperA = pa.Continental || (!pb.Continental && contact.A < contact.B);
                            float signed = System.Numerics.Vector2.Dot(delta - segment * t, normal) * (upperA ? -1 : 1);
                            float plateScale = MathF.Sqrt((float)_width * _height / _tectonics.Plates.Length);
                            if (signed < 0) continue;
                            arc = Math.Max(arc, Math.Max(0, 1 - Math.Abs(distanceToArc - plateScale * 0.12f) / (plateScale * 0.18f)));
                        }
                        else arc = Math.Max(arc, Math.Max(0, 1 - Math.Abs(distanceToArc - _scale * 0.06f) / (_scale * 0.08f)));
                    }
                    return arc;
                }
                return 0;
            }
            int arcColumns = Math.Min(128, _width), arcRows = Math.Min(64, _height);
            float[]? arcs = _compactCoasts ? new float[arcColumns * arcRows] : null;
            if (arcs is not null)
                System.Threading.Tasks.Parallel.For(0, arcRows, y =>
                {
                    for (int x = 0; x < arcColumns; x++)
                        arcs[y * arcColumns + x] = ArcAt(x * _width / arcColumns, y * _height / arcRows);
                });
            float SampleArc(int col, int row)
            {
                if (arcs is null) return ArcAt(col, row);
                float x = col * arcColumns / (float)_width, y = row * arcRows / (float)_height;
                int ix = (int)x, iy = (int)y;
                int nx = _wrap ? (ix + 1) % arcColumns : Math.Min(ix + 1, arcColumns - 1);
                int ny = Math.Min(iy + 1, arcRows - 1);
                return float.Lerp(float.Lerp(arcs[iy * arcColumns + ix], arcs[iy * arcColumns + nx], x - ix),
                    float.Lerp(arcs[ny * arcColumns + ix], arcs[ny * arcColumns + nx], x - ix), y - iy);
            }
            var candidates = new List<(int Cell, float Rank)>();
            int candidateStep = _compactCoasts ? Math.Max(1, _height / 128) : 1;
            for (int row = edgeMargin; row < _height - edgeMargin; row += candidateStep)
            for (int col = _tectonics is not null && _wrap ? 0 : edgeMargin; col < _width - (_tectonics is not null && _wrap ? 0 : edgeMargin); col += candidateStep)
            {
                int cell = row * _width + col;
                if (water[cell] != WaterType.Ocean || distance[cell] < (_spacedIslands ? coastGap + 1 : 4) ||
                    distance[cell] > Math.Max(10, _height / (_spacedIslands ? 3 : 8))) continue;
                float rank = FieldNoise(col, row, 7f, _seed ^ 0xB17A93C5u) + random.NextSingle() * 0.10f;
                rank += SampleArc(col, row) * (_strongTectonics ? 2.4f : 0.8f);
                candidates.Add((cell, rank));
            }
            if (_compactCoasts) progress?.Invoke(new(GenerationStep.Land, 88));
            candidates.Sort((a, b) => b.Rank != a.Rank ? b.Rank.CompareTo(a.Rank) : a.Cell.CompareTo(b.Cell));
            reserved = new bool[water.Length];
            var islandDistance = new int[water.Length];
            Array.Fill(islandDistance, int.MaxValue);
            var clusters = new Dictionary<(int Column, int Row), int>();
            int clusterSize = Math.Max(8, _height / 3);
            var frontier = new PriorityQueue<int, (float Distance, int Cell)>();
            var island = new HashSet<int>();
            int desiredIslands = Math.Max(4, _settings.Continents * 4);
            int islandSize = Math.Max(3, budget / desiredIslands);
            int initialBudget = budget;
            foreach (var (start, _) in candidates)
            {
                if (budget == 0)
                    break;
                if (reserved[start])
                    continue;
                var cluster = (start % _width / clusterSize, start / _width / clusterSize);
                clusters.TryGetValue(cluster, out int clusterCount);
                if (clusterCount >= 4)
                    continue;
                int target = Math.Min(budget, Math.Max(3, islandSize / 2 + random.Next(islandSize + 1)));
                int radius = (int)Math.Ceiling(Math.Sqrt(target) * 1.3);
                if (_spacedIslands && (distance[start] < coastGap + radius || islandDistance[start] < islandGap + radius))
                    continue;
                int seedMargin = edgeMargin + (int)Math.Ceiling(Math.Sqrt(target));
                int startRow = start / _width;
                int startColumn = start % _width;
                if (startRow < seedMargin || startRow >= _height - seedMargin ||
                    (!_wrap && (startColumn < seedMargin || startColumn >= _width - seedMargin)))
                    continue;
                frontier.Clear();
                island.Clear();
                Hex origin = HexAt(start);
                float stretch = 0.45f + random.NextSingle() * 0.9f;
                float angle = random.NextSingle() * MathF.Tau;
                float lobeRadius = MathF.Sqrt(target) * (0.35f + random.NextSingle() * 0.35f);
                float lobeX = MathF.Cos(angle) * lobeRadius;
                float lobeY = MathF.Sin(angle) * lobeRadius;
                frontier.Enqueue(start, (0, start));
                var visited = new HashSet<int> { start };
                while (island.Count < target && frontier.TryDequeue(out int cell, out _))
                {
                    int column = cell % _width;
                    int row = cell / _width;
                    if (reserved[cell] || water[cell] != WaterType.Ocean || distance[cell] < coastGap ||
                        row < edgeMargin || row >= _height - edgeMargin ||
                        (!_wrap && (column < edgeMargin || column >= _width - edgeMargin)))
                        continue;
                    island.Add(cell);
                    Hex hex = HexAt(cell);
                    for (int direction = 0; direction < 6; direction++)
                    {
                        int neighbor = Neighbor(hex, direction);
                        if (neighbor < 0 || !visited.Add(neighbor))
                            continue;
                        Hex candidate = HexAt(neighbor);
                        float dx = candidate.Q - origin.Q + (candidate.R - origin.R) * 0.5f;
                        float dy = (candidate.R - origin.R) * 0.8660254f;
                        float main = dx * dx * stretch + dy * dy / stretch;
                        float sideX = dx - lobeX;
                        float sideY = dy - lobeY;
                        float lobe = (sideX * sideX / stretch + sideY * sideY * stretch) * 1.45f;
                        float warp = FieldNoise(neighbor % _width, neighbor / _width, 113f, _seed ^ 0x6E2471D9u);
                        float metric = Math.Min(main, lobe) + (warp - 0.5f) * target * 0.32f;
                        frontier.Enqueue(neighbor, (metric, neighbor));
                    }
                }
                if (island.Count == 0 || (_spacedIslands && island.Count < target))
                    continue;
                clusters[cluster] = clusterCount + 1;
                foreach (int cell in island)
                {
                    water[cell] = WaterType.None;
                    _scores[cell] = Math.Max(0.012f, 0.13f * FieldNoise(cell % _width, cell / _width, 29f, _seed ^ 0xFA1427B3u));
                    reserved[cell] = true;
                    if (_spacedIslands)
                    {
                        islandDistance[cell] = 0;
                        queue.Enqueue(cell);
                        continue;
                    }
                    Hex hex = HexAt(cell);
                    for (int direction = 0; direction < 6; direction++)
                    {
                        int near = Neighbor(hex, direction);
                        if (near < 0)
                            continue;
                        reserved[near] = true;
                        Hex nearHex = HexAt(near);
                        for (int side = 0; side < 6; side++)
                        {
                            int far = Neighbor(nearHex, side);
                            if (far >= 0)
                                reserved[far] = true;
                        }
                    }
                }
                if (_spacedIslands)
                {
                    while (queue.TryDequeue(out int cell))
                    {
                        int steps = islandDistance[cell] + 1;
                        Hex hex = HexAt(cell);
                        for (int direction = 0; direction < 6; direction++)
                        {
                            int neighbor = Neighbor(hex, direction);
                            if (neighbor < 0 || islandDistance[neighbor] <= steps)
                                continue;
                            islandDistance[neighbor] = steps;
                            if (steps <= islandGap)
                                reserved[neighbor] = true;
                            queue.Enqueue(neighbor);
                        }
                    }
                }
                budget -= island.Count;
                if (_compactCoasts) progress?.Invoke(new(GenerationStep.Land, 89 + (initialBudget - budget) * 9 / Math.Max(1, initialBudget)));
            }
            if (_spacedIslands)
                for (int i = 0; i < reserved.Length; i++)
                    reserved[i] |= islandDistance[i] <= coastGap;
            if (_compactCoasts) progress?.Invoke(new(GenerationStep.Land, 99));
            return budget;
        }
    }
}
