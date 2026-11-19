using System;
using System.Collections.Generic;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    private sealed partial class ContinentalSculpture
    {
        private (float[] Scores, float[]? Ridges, float SeaLevel, WaterType[] Water)
            BuildSeparatedContinents(int[] centers, Action<GenerationProgress>? progress)
        {
            var owners = new byte[_scores.Length];
            var clearance = new byte[_scores.Length];
            var territory = new byte[_scores.Length];
            var visited = new bool[_scores.Length];
            var water = new WaterType[_scores.Length];
            float scale = Math.Min(_scale, _width);

            VisitChunks(_chunks, (i, col, row) =>
            {
                float x = col + (row & 1) * 0.5f
                    + (FieldNoise(col, row, 6.2f, _seed ^ 0x1E35A7BDu) - 0.5f) * scale * 0.32f;
                float y = row + (FieldNoise(col, row, 6.2f, _seed ^ 0x9E3779B9u) - 0.5f) * scale * 0.32f;
                float nearest = float.PositiveInfinity;
                float nextNearest = float.PositiveInfinity;
                for (int j = 0; j < centers.Length; j++)
                {
                    int centerRow = centers[j] / _width;
                    float dx = Math.Abs(x - (centers[j] % _width + (centerRow & 1) * 0.5f));
                    if (_wrap)
                        dx = Math.Min(dx, Math.Abs(_width - dx));
                    float dy = y - centerRow;
                    float distance = dx * dx + dy * dy * 0.75f;
                    if (distance < nearest)
                    {
                        nextNearest = nearest;
                        nearest = distance;
                        owners[i] = (byte)(j + 1);
                    }
                    else
                        nextNearest = Math.Min(nextNearest, distance);
                }

                // Lower the seabed between territories before selecting land. The warped
                // boundary and the terrain field vary the width of the passages together.
                float gap = MathF.Sqrt(nextNearest) - MathF.Sqrt(nearest);
                float width = 0.72f + FieldNoise(col, row, 8.4f, _seed ^ 0x68E31DA4u) * 0.62f;
                float distanceFromPassage = gap / (scale * width);
                int edge = Math.Min(row, _height - 1 - row);
                if (!_wrap)
                    edge = Math.Min(edge, Math.Min(col, _width - 1 - col));
                float distanceFromEdge = edge / (scale * width);
                float reserve = Math.Min(distanceFromPassage, distanceFromEdge * 2.1f);
                clearance[i] = (byte)Math.Min(255f, reserve * 255f);
                float shelf = 1f - Smooth(Math.Clamp(distanceFromPassage / 0.34f, 0f, 1f));
                float outerShelf = 1f - Smooth(Math.Clamp(distanceFromEdge / 0.14f, 0f, 1f));
                _scores[i] -= Math.Max(shelf * 2.45f, outerShelf * 2f);
            }, progress, GenerationStep.Land, 80, 81);

            for (int j = 0; j < centers.Length; j++)
                owners[centers[j]] = (byte)(j + 1);
            VisitChunks(_chunks, (i, col, row) =>
            {
                byte owner = owners[i];
                if (centers[owner - 1] != i)
                {
                    Hex hex = HexGrid.FromOffset(col, row);
                    for (int direction = 0; direction < 6; direction++)
                    {
                        int neighbor = Neighbor(hex, direction);
                        if (neighbor >= 0 && owners[neighbor] < owner)
                            return;
                    }
                }
                territory[i] = owner;
            }, progress, GenerationStep.Land, 81, 82);

            var capacity = new int[centers.Length];
            var queue = new Queue<int>();
            int target = (int)((long)_scores.Length * _settings.LandPercent / 100);
            int available;
            // Prefer wide sea passages. Compact maps may need a thinner reserve to fit
            // the requested land area. The one-cell separation is kept in every case.
            for (byte minimum = 24; ; minimum -= 8)
            {
                Array.Clear(visited);
                available = 0;
                for (int j = 0; j < centers.Length; j++)
                {
                    byte owner = (byte)(j + 1);
                    capacity[j] = 0;
                    visited[centers[j]] = true;
                    queue.Enqueue(centers[j]);
                    while (queue.TryDequeue(out int cell))
                    {
                        capacity[j]++;
                        Hex hex = HexAt(cell);
                        for (int direction = 0; direction < 6; direction++)
                        {
                            int neighbor = Neighbor(hex, direction);
                            if (neighbor < 0 || visited[neighbor] || territory[neighbor] != owner || clearance[neighbor] < minimum)
                                continue;
                            visited[neighbor] = true;
                            queue.Enqueue(neighbor);
                        }
                    }
                    available += capacity[j];
                }
                if (available >= target || minimum == 0)
                {
                    for (int i = 0; i < territory.Length; i++)
                    {
                        if (!visited[i])
                            territory[i] = 0;
                    }
                    break;
                }
            }

            target = Math.Min(available, target);
            int islandBudget = _islandCoasts ? Math.Max(7, target / 60) : 0;
            target -= islandBudget;
            Array.Clear(visited);
            Array.Fill(water, WaterType.Lake);
            var frontier = new PriorityQueue<int, (float Score, int Cell)>();
            var levels = new float[centers.Length];
            int cumulativeCapacity = 0;
            int assigned = 0;
            for (int j = 0; j < centers.Length; j++)
            {
                cumulativeCapacity += capacity[j] - 1;
                int nextAssigned = j + 1 + (int)((long)cumulativeCapacity * (target - centers.Length)
                    / Math.Max(1, available - centers.Length));
                byte owner = (byte)(j + 1);
                int start = centers[j];
                frontier.Clear();
                frontier.Enqueue(start, (-_scores[start], start));
                visited[start] = true;
                for (int count = nextAssigned - assigned; count > 0; count--)
                {
                    int cell = frontier.Dequeue();
                    water[cell] = WaterType.None;
                    if (count == 1)
                        levels[j] = _scores[cell];
                    Hex hex = HexAt(cell);
                    for (int direction = 0; direction < 6; direction++)
                    {
                        int neighbor = Neighbor(hex, direction);
                        if (neighbor < 0 || visited[neighbor] || territory[neighbor] != owner)
                            continue;
                        visited[neighbor] = true;
                        frontier.Enqueue(neighbor, (-_scores[neighbor], neighbor));
                    }
                }
                assigned = nextAssigned;
            }
            if (_compactCoasts) CompactMainland(water, territory, centers);
            progress?.Invoke(new(GenerationStep.Land, 83));

            LabelOpenSea(water, queue);
            // Normalize every territory against its own shoreline so a lower continent
            // keeps its relief even when a neighboring continent sits higher in the field.
            VisitChunks(_chunks, (i, _, _) =>
            {
                float altitude = _scores[i] - levels[owners[i] - 1];
                _scores[i] = water[i] == WaterType.None
                    ? Math.Max(0.002f, altitude)
                    : Math.Min(-0.002f, altitude);
            }, progress, GenerationStep.Land, 84, 85);
            if (_islandCoasts)
            {
                bool[]? mainland = null;
                if (_spacedIslands)
                {
                    mainland = new bool[water.Length];
                    for (int i = 0; i < water.Length; i++)
                        mainland[i] = water[i] == WaterType.None;
                }
                int remaining = AddIslandChains(water, islandBudget, out bool[] islandReserve, progress);
                // Compact, land-heavy maps can run out of offshore room. Return that
                // part of the budget to the existing territories without joining them.
                frontier.Clear();
                Array.Clear(visited);
                for (int i = 0; i < water.Length; i++)
                    if (water[i] == WaterType.None && territory[i] != 0 && (mainland is null || mainland[i]))
                        AddFrontier(i);
                while (remaining > 0 && frontier.TryDequeue(out int cell, out _))
                {
                    water[cell] = WaterType.None;
                    _scores[cell] = Math.Max(0.002f, _scores[cell]);
                    remaining--;
                    AddFrontier(cell);
                }
                if (remaining != 0)
                    throw new InvalidOperationException("A reserva de terra não pôde ser distribuída.");
                for (int i = 0; i < water.Length; i++)
                    if (water[i] != WaterType.None)
                        water[i] = WaterType.Lake;
                LabelOpenSea(water, queue);

                void AddFrontier(int cell)
                {
                    Hex hex = HexAt(cell);
                    for (int direction = 0; direction < 6; direction++)
                    {
                        int neighbor = Neighbor(hex, direction);
                        if (neighbor < 0 || visited[neighbor] || water[neighbor] == WaterType.None ||
                            territory[neighbor] != territory[cell] || (_spacedIslands && islandReserve[neighbor]))
                            continue;
                        visited[neighbor] = true;
                        frontier.Enqueue(neighbor, (-_scores[neighbor], neighbor));
                    }
                }
            }
            return (_scores, _ridges, 0f, water);
        }

        private void CompactMainland(WaterType[] water, byte[] territory, int[] centers)
        {
            var anchor = new HashSet<int>(centers);
            var removed = new List<int>();
            int LandNeighbors(int cell)
            {
                int count = 0; Hex hex = HexAt(cell);
                for (int d = 0; d < 6; d++) { int n = Neighbor(hex, d); if (n >= 0 && water[n] == WaterType.None) count++; }
                return count;
            }
            // Remove long one-cell ridges exposed as land by connected quota growth.
            // Regrow the same area only where it has broad mainland support.
            for (int pass = 0; pass < 3; pass++)
            {
                removed.Clear();
                for (int i = 0; i < water.Length; i++)
                    if (water[i] == WaterType.None && !anchor.Contains(i) && LandNeighbors(i) <= 2) removed.Add(i);
                foreach (int i in removed) water[i] = WaterType.Lake;
                int remaining = removed.Count;
                var frontier = new PriorityQueue<int, (float Score, int Cell)>();
                var seen = new HashSet<int>();
                void Offer(int cell)
                {
                    if (water[cell] == WaterType.None || territory[cell] == 0 || LandNeighbors(cell) < 3 || !seen.Add(cell)) return;
                    Hex hex = HexAt(cell);
                    for (int d = 0; d < 6; d++)
                    {
                        int n = Neighbor(hex, d);
                        if (n >= 0 && water[n] == WaterType.None && territory[n] != territory[cell]) return;
                    }
                    frontier.Enqueue(cell, (-_scores[cell], cell));
                }
                for (int i = 0; i < water.Length; i++) if (water[i] != WaterType.None) Offer(i);
                while (remaining > 0 && frontier.TryDequeue(out int cell, out _))
                {
                    water[cell] = WaterType.None; remaining--;
                    Hex hex = HexAt(cell);
                    for (int d = 0; d < 6; d++) { int n = Neighbor(hex, d); if (n >= 0) Offer(n); }
                }
                // Unusual cramped maps retain the unmatched original cells to conserve area.
                foreach (int i in removed)
                    if (remaining > 0 && water[i] != WaterType.None) { water[i] = WaterType.None; remaining--; }
            }
        }

        private void LabelOpenSea(WaterType[] water, Queue<int> queue)
        {
            void Enter(int cell)
            {
                if (cell < 0 || water[cell] != WaterType.Lake)
                    return;
                water[cell] = WaterType.Ocean;
                queue.Enqueue(cell);
            }

            for (int col = 0; col < _width; col++)
            {
                Enter(col);
                Enter((_height - 1) * _width + col);
            }
            if (!_wrap)
            {
                for (int row = 1; row < _height - 1; row++)
                {
                    Enter(row * _width);
                    Enter(row * _width + _width - 1);
                }
            }

            while (queue.TryDequeue(out int cell))
            {
                Hex hex = HexAt(cell);
                for (int direction = 0; direction < 6; direction++)
                    Enter(Neighbor(hex, direction));
            }
        }
    }
}
