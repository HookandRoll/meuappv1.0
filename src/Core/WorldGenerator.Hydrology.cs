using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mapmaker.Core;

public sealed record RiverRecord(int Id, int[] Path, byte[] Tiers, float[] AnnualFlow, string EndType, int? ParentId);

public static partial class WorldGenerator
{
    // Orographic transport samples the upstream terrain rather than a tectonic mask.
    private static ushort OrographicRain(int col, int row, int width, int height, bool wrap,
        ushort baseline, Func<int, int, short> elevation, Func<int, int, WaterType> water, bool lakeMoisture = false)
    {
        float latitude = Math.Abs(2f * row / height - 1);
        int wind = latitude is > 0.3f and < 0.7f ? 1 : -1;
        float humidity = 0.65f, rain = 0;
        int stride = Math.Max(1, height / 128);
        float previous = 0;
        for (int step = 16; step >= 0; step--)
        {
            int x = col - wind * step * stride;
            if (wrap) x = (x % width + width) % width;
            else if (x < 0 || x >= width) continue;
            float altitude = Math.Max(0, (int)elevation(x, row));
            WaterType type = water(x, row);
            if (type == WaterType.Ocean) humidity = 1;
            else if (lakeMoisture && type == WaterType.Lake) humidity = Math.Max(humidity, 0.85f);
            float ascent = Math.Max(0, altitude - previous);
            float condensed = humidity * Math.Clamp(ascent / 3000f, 0, 0.7f);
            humidity = Math.Clamp(humidity - condensed + 0.008f, 0, 1);
            if (step == 0) rain = baseline * (0.35f + humidity * 0.8f) + condensed * 2200;
            previous = altitude;
        }
        return (ushort)Math.Clamp((int)MathF.Round(rain), 0, 5000);
    }

    public static RiverRecord[] ReadRivers(WorldData world)
    {
        if (!world.SparseEntities.TryGetProperty("rivers", out var rivers)) return Array.Empty<RiverRecord>();
        return rivers.Deserialize<RiverRecord[]>() ?? Array.Empty<RiverRecord>();
    }

    internal static void WriteRivers(WorldData world, RiverRecord[] rivers)
    {
        JsonObject sparse = JsonNode.Parse(world.SparseEntities.GetRawText())!.AsObject();
        sparse["rivers"] = JsonSerializer.SerializeToNode(rivers);
        world.SparseEntities = JsonSerializer.SerializeToElement(sparse);
        RebuildRivers(world);
    }

    public static void RebuildRivers(WorldData world)
    {
        Array.Clear(world.RiverEdges);
        world.HexToRivers.Clear();
        world.RiversById.Clear();
        var ids = new HashSet<int>();
        RiverRecord[] rivers = ReadRivers(world);
        var byId = rivers.ToDictionary(r => r.Id);
        foreach (var river in rivers) world.RiversById.Add(river.Id, river);
        var complete = new HashSet<int>();
        foreach (var river in rivers)
        {
            var chain = new HashSet<int>();
            RiverRecord current = river;
            while (!complete.Contains(current.Id))
            {
                if (!chain.Add(current.Id)) throw new System.IO.InvalidDataException("Ciclo entre afluentes.");
                if (current.ParentId is not int parent) break;
                if (!byId.TryGetValue(parent, out var downstream)) throw new System.IO.InvalidDataException("Destino de afluente ausente.");
                current = downstream;
            }
            complete.UnionWith(chain);
        }
        foreach (var river in rivers)
        {
            if (!ids.Add(river.Id) || river.Path is null || river.Tiers is null || river.AnnualFlow is null ||
                river.Path.Length < 2 || river.Tiers.Length != river.Path.Length - 1 || river.AnnualFlow.Length != river.Tiers.Length)
                throw new System.IO.InvalidDataException("Entidade fluvial inválida.");
            for (int j = 0; j < river.Path.Length; j++)
                if ((uint)river.Path[j] >= world.Count) throw new System.IO.InvalidDataException("Rio fora do mapa.");
            foreach (int cell in river.Path)
            {
                if (!world.HexToRivers.TryGetValue(cell, out var crossing)) world.HexToRivers[cell] = crossing = new();
                crossing.Add(river.Id);
            }
            for (int j = 0; j < river.Tiers.Length; j++)
            {
                int cell = river.Path[j], next = river.Path[j + 1];
                int direction = RiverDirection(world, cell, next);
                if (direction < 0 || (river.Tiers[j] < 1 || river.Tiers[j] > (world.Manifest.GeneratorVersion >= 23 ? 5 : 3)) || !float.IsFinite(river.AnnualFlow[j]) || river.AnnualFlow[j] < 0)
                    throw new System.IO.InvalidDataException("Aresta fluvial inválida.");
                // RiverEdges remains the existing 2-bit connectivity cache. Exact
                // five-level sizes live in the persistent river entity.
                int compactTier = world.Manifest.GeneratorVersion >= 23 ? (river.Tiers[j] <= 2 ? 1 : river.Tiers[j] <= 4 ? 2 : 3) : river.Tiers[j];
                int tier = Math.Max(compactTier, HexGrid.EdgeValue(world.RiverEdges[cell], direction));
                HexGrid.SetSharedEdge(world.RiverEdges, cell, direction, tier, world.Width, world.Height, world.Manifest.HorizontalWrap);
            }
        }
    }

    internal static int RiverLevelAt(WorldData world, int cell)
    {
        int result = 0;
        if (!world.HexToRivers.TryGetValue(cell, out var ids)) return 0;
        foreach (int id in ids)
        {
            var river = world.RiversById[id];
            int index = Array.IndexOf(river.Path, cell);
            if (index < 0) continue;
            int level = river.Tiers[Math.Min(index, river.Tiers.Length - 1)];
            if (index > 0) level = Math.Max(level, river.Tiers[index - 1]);
            if (world.Manifest.GeneratorVersion < 23) level = level == 1 ? 2 : level == 2 ? 4 : 5;
            result = Math.Max(result, level);
        }
        return result;
    }

    private static int RiverDirection(WorldData world, int cell, int next)
    {
        Hex hex = HexGrid.FromOffset(cell % world.Width, cell / world.Width);
        for (int d = 0; d < 6; d++)
            if (HexGrid.NeighborIndex(hex, d, world.Width, world.Height, world.Manifest.HorizontalWrap) == next) return d;
        return -1;
    }

    private static void ApplyFluvialBiomes(WorldData world, WorldRegion region, bool updateFeatures = true)
    {
        if (world.Manifest.GeneratorVersion >= 46) { ApplyContextBiomes(world, region, updateFeatures); return; }
        bool lakeMargins = world.Manifest.GeneratorVersion >= 20;
        foreach (int cell in region.Cells(world.Width))
        {
            if (world.Water[cell] != WaterType.None || ((HexLocks)world.LockMask[cell] & HexLocks.Biome) != 0) continue;
            bool wet = world.RiverEdges[cell] != 0;
            if (lakeMargins && !wet)
            {
                Hex hex = HexGrid.FromOffset(cell % world.Width, cell / world.Width);
                for (int d = 0; d < 6; d++)
                {
                    int n = HexGrid.NeighborIndex(hex, d, world.Width, world.Height, world.Manifest.HorizontalWrap);
                    if (n >= 0 && world.Water[n] == WaterType.Lake && Math.Abs(world.Elevation[cell]) < 650) { wet = true; break; }
                }
            }
            if (wet && world.Biomes[cell] is BiomeType.Desert or BiomeType.Steppe && world.Temperature[cell] > 45)
                world.Biomes[cell] = BiomeType.Grassland;
            if (world.Manifest.GeneratorVersion >= 44 && IsSwampMargin(world, cell))
                world.Biomes[cell] = BiomeType.Swamp;
        }
    }

    private static void RegenerateRivers(WorldData world, WorldRegion selection)
    {
        bool Editable(int i) => selection.Contains(i % world.Width, i / world.Width) &&
            ((HexLocks)world.LockMask[i] & HexLocks.Rivers) == 0;
        RiverRecord[] old = ReadRivers(world);
        var fixedRivers = old.Where(r => !r.Path.All(Editable)).ToArray();
        var fixedList = fixedRivers.ToList();
        var retainedIds = fixedRivers.Select(r => r.Id).ToHashSet();
        for (int j = 0; j < fixedList.Count; j++)
            if (fixedList[j].ParentId is int id && retainedIds.Add(id))
            {
                var parent = old.FirstOrDefault(r => r.Id == id);
                if (parent is not null) fixedList.Add(parent);
            }
        fixedRivers = fixedList.ToArray();
        var occupancy = old.SelectMany(r => r.Path).GroupBy(i => i).ToDictionary(g => g.Key, g => g.Count());
        var reserved = old.SelectMany(r => r.Path).ToHashSet();
        for (int j = 0; j < fixedRivers.Length; j++)
        {
            RiverRecord source = fixedRivers[j];
            var own = source.Path.ToHashSet();
            bool Free(int i) => Editable(i) && (!occupancy.TryGetValue(i, out int uses) || uses == 1) &&
                (!reserved.Contains(i) || own.Contains(i));
            fixedRivers[j] = RerouteRiver(world, source, Free);
            foreach (int i in fixedRivers[j].Path) reserved.Add(i);
        }
        var fixedCells = new HashSet<int>(fixedRivers.SelectMany(r => r.Path));
        var candidate = world.CreateEditCopy();
        GenerateRivers(candidate);
        // Crossing edges and confluences remain fixed; unlocked internal runs can
        // reroute. New independent courses must fit entirely inside the selection.
        var added = ReadRivers(candidate).Where(r => r.Path.All(i => Editable(i) && !fixedCells.Contains(i))).ToArray();
        int nextId = old.Length == 0 ? 0 : old.Max(r => r.Id) + 1;
        var remap = added.ToDictionary(r => r.Id, _ => nextId++);
        var result = fixedRivers.Concat(added.Select(r => r with
        {
            Id = remap[r.Id], ParentId = r.ParentId is int parent && remap.TryGetValue(parent, out int mapped) ? mapped : null
        })).ToArray();
        var present = result.Select(r => r.Id).ToHashSet();
        // A retained tributary also retains its downstream network.
        var missing = new Queue<int>(result.Where(r => r.ParentId.HasValue && !present.Contains(r.ParentId.Value)).Select(r => r.ParentId!.Value));
        var all = result.ToList();
        while (missing.TryDequeue(out int id))
        {
            var river = old.FirstOrDefault(r => r.Id == id);
            if (river is null || !present.Add(id)) continue;
            all.Add(river);
            if (river.ParentId is int parent && !present.Contains(parent)) missing.Enqueue(parent);
        }
        WriteRivers(world, all.ToArray());
    }

    private static RiverRecord RerouteRiver(WorldData world, RiverRecord source, Func<int, bool> editable)
    {
        var path = new List<int> { source.Path[0] };
        var tiers = new List<byte>(); var flows = new List<float>();
        int index = 0;
        while (index < source.Path.Length - 1)
        {
            int end = index;
            if (editable(source.Path[index]))
                while (end + 1 < source.Path.Length && editable(source.Path[end + 1])) end++;
            if (end - index < 2)
            {
                tiers.Add(source.Tiers[index]); flows.Add(source.AnnualFlow[index]);
                path.Add(source.Path[++index]); continue;
            }
            int startCell = source.Path[index], endCell = source.Path[end];
            var cost = new Dictionary<int, float> { [startCell] = 0 };
            var previous = new Dictionary<int, int>();
            var queue = new PriorityQueue<int, (float Cost, int Cell)>();
            queue.Enqueue(startCell, (0, startCell));
            var used = path.Take(path.Count - 1).Concat(source.Path.Skip(end + 1)).ToHashSet();
            while (queue.TryDequeue(out int cell, out var priority))
            {
                if (priority.Cost > cost[cell]) continue;
                if (cell == endCell) break;
                Hex hex = HexGrid.FromOffset(cell % world.Width, cell / world.Width);
                for (int direction = 0; direction < 6; direction++)
                {
                    int n = HexGrid.NeighborIndex(hex, direction, world.Width, world.Height, world.Manifest.HorizontalWrap);
                    if (n < 0 || used.Contains(n) || !editable(n)) continue;
                    float next = priority.Cost + 1 + Math.Max(0, world.Elevation[n] - world.Elevation[cell]) * 0.01f;
                    if (cost.TryGetValue(n, out float current) && current <= next) continue;
                    cost[n] = next; previous[n] = cell; queue.Enqueue(n, (next, n));
                }
            }
            var replacement = new List<int>();
            if (previous.ContainsKey(endCell))
            {
                for (int cell = endCell; cell != startCell; cell = previous[cell]) replacement.Add(cell);
                replacement.Reverse();
            }
            else replacement.AddRange(source.Path.Skip(index + 1).Take(end - index));
            for (int j = 0; j < replacement.Count; j++)
            {
                float t = replacement.Count == 1 ? 0 : j / (float)(replacement.Count - 1);
                tiers.Add((byte)Math.Clamp((int)MathF.Round(source.Tiers[index] * (1 - t) + source.Tiers[end - 1] * t), 1, world.Manifest.GeneratorVersion >= 23 ? 5 : 3));
                flows.Add(source.AnnualFlow[index] * (1 - t) + source.AnnualFlow[end - 1] * t);
                path.Add(replacement[j]);
            }
            index = end;
        }
        return source with { Path = path.ToArray(), Tiers = tiers.ToArray(), AnnualFlow = flows.ToArray() };
    }

    internal static double AnnualRunoff(double rainMm, double meanC, float latitude)
    {
        double snow = 0, runoff = 0;
        double amplitude = 3 + 18 * Math.Abs(latitude);
        for (int month = 0; month < 12; month++)
        {
            double temperature = meanC + amplitude * Math.Cos(month * Math.PI / 6);
            double precipitation = rainMm / 12;
            double liquid = temperature <= 0 ? 0 : precipitation;
            if (temperature <= 0) snow += precipitation;
            double melt = Math.Min(snow, Math.Max(0, temperature) * 90);
            snow -= melt;
            double loss = Math.Clamp(0.25 + Math.Max(0, temperature) * 0.012, 0.25, 0.8);
            runoff += (liquid + melt) * (1 - loss);
        }
        return runoff;
    }

    internal static void GenerateRivers(WorldData world, Action<GenerationProgress>? progress = null)
    {
        progress?.Invoke(new(GenerationStep.Rivers, 0));
        int count = world.Count;
        var parent = new int[count]; Array.Fill(parent, -1);
        var visited = new bool[count];
        var level = new short[count];
        var order = new List<int>(count);
        uint Tie(int cell)
        {
            uint value = unchecked((uint)(cell ^ world.Manifest.Seed));
            value ^= value >> 16; value *= 0x7feb352d; value ^= value >> 15; value *= 0x846ca68b; return value ^ (value >> 16);
        }
        bool naturalDrainage = world.Manifest.GeneratorVersion >= 21;
        // A coherent traversal cost breaks the hex-distance wavefront on flats.
        // It is auxiliary routing resistance, not an edit to terrain elevation.
        float RoutingCost(int cell)
        {
            if (!naturalDrainage) return 1;
            float scale = Math.Max(5, world.Height / 96f);
            float x = cell % world.Width / scale, y = cell / world.Width / scale;
            uint seed = unchecked((uint)world.Manifest.Seed) ^ 0x79B34C21u;
            float n = Noise(x, y, seed);
            if (world.Manifest.HorizontalWrap)
            {
                float t = cell % world.Width / (float)world.Width;
                n = float.Lerp(n, Noise(x - world.Width / scale, y, seed), t);
            }
            return 0.25f + n * n * 5;
        }
        short DrainageHeight(int cell)
        {
            if (!naturalDrainage || world.Water[cell] != WaterType.None) return world.Elevation[cell];
            float scale = Math.Max(5, world.Height / 96f);
            uint seed = unchecked((uint)world.Manifest.Seed) ^ 0xB67F814Du;
            float SampleRelief(float x)
            {
                float y = cell / world.Width / scale;
                return Noise(x / scale, y, seed) * 0.65f + Noise(x / (scale * 0.43f), y / 0.43f, seed ^ 0x93A7Du) * 0.35f;
            }
            float col = cell % world.Width;
            float noise = SampleRelief(col);
            if (world.Manifest.HorizontalWrap) noise = float.Lerp(noise, SampleRelief(col - world.Width), col / world.Width);
            // Sub-hex valley relief only in the temporary drainage surface. This
            // resolves the quantized broad plains without changing editable elevation.
            return (short)Math.Clamp(world.Elevation[cell] + (noise - 0.5f) * 90, short.MinValue, short.MaxValue);
        }
        var queue = new PriorityQueue<int, (short Height, float Distance, uint Tie, int Cell)>();
        void Seed(int cell, short h)
        {
            if (visited[cell]) return;
            visited[cell] = true; level[cell] = h; queue.Enqueue(cell, (h, 0, Tie(cell), cell));
        }
        // Open physical edges and oceans are outlets. The wrap seam is not an edge.
        for (int i = 0; i < count; i++)
        {
            if (world.Water[i] != WaterType.Ocean) continue;
            visited[i] = true;
            Hex hex = HexGrid.FromOffset(i % world.Width, i / world.Width);
            for (int d = 0; d < 6; d++)
            {
                int n = HexGrid.NeighborIndex(hex, d, world.Width, world.Height, world.Manifest.HorizontalWrap);
                if (n >= 0 && world.Water[n] != WaterType.Ocean)
                { queue.Enqueue(i, (0, 0, Tie(i), i)); break; }
            }
        }
        for (int x = 0; x < world.Width; x++) { Seed(x, DrainageHeight(x)); Seed(count - world.Width + x, DrainageHeight(count - world.Width + x)); }
        if (!world.Manifest.HorizontalWrap)
            for (int y = 0; y < world.Height; y++) { Seed(y * world.Width, DrainageHeight(y * world.Width)); Seed(y * world.Width + world.Width - 1, DrainageHeight(y * world.Width + world.Width - 1)); }
        while (queue.TryDequeue(out int cell, out var priority))
        {
            order.Add(cell);
            Hex hex = HexGrid.FromOffset(cell % world.Width, cell / world.Width);
            for (int d = 0; d < 6; d++)
            {
                int n = HexGrid.NeighborIndex(hex, d, world.Width, world.Height, world.Manifest.HorizontalWrap);
                if (n < 0 || visited[n]) continue;
                visited[n] = true; parent[n] = cell;
                level[n] = (short)Math.Max(priority.Height, DrainageHeight(n));
                queue.Enqueue(n, (level[n], priority.Distance + RoutingCost(n), Tie(n), n));
            }
        }
        if (naturalDrainage)
        {
            var floodRank = new int[count]; Array.Fill(floodRank, -1);
            for (int j = 0; j < order.Count; j++) floodRank[order[j]] = j;
            foreach (int cell in order)
            {
                if (world.Water[cell] != WaterType.None || parent[cell] < 0) continue;
                Hex hex = HexGrid.FromOffset(cell % world.Width, cell / world.Width);
                int best = parent[cell];
                // The flood tree guarantees an outlet but is not the steepest-descent
                // flow graph. Route towards the lowest admissible adjacent valley.
                for (int d = 0; d < 6; d++)
                {
                    int n = HexGrid.NeighborIndex(hex, d, world.Width, world.Height, world.Manifest.HorizontalWrap);
                    if (n < 0 || (level[n] > level[cell]) ||
                        (level[n] == level[cell] && floodRank[n] >= floodRank[cell])) continue;
                    if (level[n] < level[best] || (level[n] == level[best] &&
                        (DrainageHeight(n) < DrainageHeight(best) ||
                        (DrainageHeight(n) == DrainageHeight(best) && Tie(n) < Tie(best))))) best = n;
                }
                parent[cell] = best;
            }
        }
        progress?.Invoke(new(GenerationStep.Rivers, 35));
        // Collapse each existing lake to a single spill outlet. Its earliest flooded
        // cell has a parent outside the component; rerooting within the lake cannot
        // introduce a cycle with the surrounding drainage graph.
        var lakeSeen = new bool[count];
        var lakeLoss = new Dictionary<int, double>();
        var lakeQueue = new Queue<int>();
        double area = Math.Sqrt(3) * 0.5 * world.Manifest.HexDistance * world.Manifest.HexDistance;
        foreach (int root in order)
        {
            if (world.Water[root] != WaterType.Lake || lakeSeen[root]) continue;
            lakeSeen[root] = true; lakeQueue.Enqueue(root);
            double evaporation = 0;
            while (lakeQueue.TryDequeue(out int cell))
            {
                evaporation += (600 + Math.Max(0, world.Temperature[cell] * 0.1) * 30) * area * 1000;
                Hex hex = HexGrid.FromOffset(cell % world.Width, cell / world.Width);
                for (int d = 0; d < 6; d++)
                {
                    int n = HexGrid.NeighborIndex(hex, d, world.Width, world.Height, world.Manifest.HorizontalWrap);
                    if (n < 0 || lakeSeen[n] || world.Water[n] != WaterType.Lake) continue;
                    lakeSeen[n] = true; parent[n] = cell; lakeQueue.Enqueue(n);
                }
            }
            lakeLoss[root] = evaporation;
        }
        var runoff = new double[count];
        for (int i = 0; i < count; i++)
        {
            if (world.Water[i] == WaterType.Ocean) continue;
            double rain = world.Rainfall[i];
            double temp = world.Temperature[i] * 0.1;
            float latitude = 2f * (i / world.Width) / world.Height - 1;
            double localRunoff = world.Water[i] == WaterType.Lake ? rain : AnnualRunoff(rain, temp, latitude);
            if (world.Manifest.GeneratorVersion >= 22 && world.Water[i] == WaterType.None && temp > 10 && rain < 450)
                localRunoff *= Math.Clamp((rain - 100) / 500, 0.03, 0.6);
            runoff[i] = localRunoff * area * 1000;
        }
        var flux = new double[count];
        void Accumulate()
        {
        // A topological pass includes lake interiors and applies evaporation exactly
        // once at each outlet after all contributing branches have arrived.
        Array.Copy(runoff, flux, count);
        var dependencies = new int[count];
        for (int i = 0; i < count; i++) if (parent[i] >= 0) dependencies[parent[i]]++;
        var ready = new Queue<int>();
        for (int i = 0; i < count; i++) if (dependencies[i] == 0) ready.Enqueue(i);
        int processed = 0;
        if (naturalDrainage) order.Clear();
        while (ready.TryDequeue(out int cell))
        {
            processed++;
            if (naturalDrainage) order.Add(cell);
            int next = parent[cell];
            if (lakeLoss.TryGetValue(cell, out double evaporation))
                flux[cell] = Math.Max(0, flux[cell] - evaporation);
            if (next < 0) continue;
            flux[next] += flux[cell];
            if (--dependencies[next] == 0) ready.Enqueue(next);
        }
        if (processed != count) throw new InvalidOperationException("Ciclo na drenagem lacustre.");
        }
        Accumulate();
        if (world.Manifest.GeneratorVersion >= 22)
        {
            var rank = new int[count];
            for (int j = 0; j < order.Count; j++) rank[order[j]] = j;
            // Propagate the attraction of established valleys backwards over the
            // admissible downstream graph. Distance decay makes a nearby strong trunk
            // preferable to many parallel isolated channels without crossing a divide.
            var attraction = new float[count];
            double referenceFlow = area * 1000 * 9000;
            for (int pass = 0; pass < 3; pass++)
            {
                for (int j = order.Count - 1; j >= 0; j--)
                {
                    int cell = order[j];
                    attraction[cell] = (float)Math.Log(1 + flux[cell] / referenceFlow);
                    if (world.Water[cell] != WaterType.None || parent[cell] < 0) continue;
                    int best = parent[cell];
                    float bestScore = world.Water[best] == WaterType.None ? attraction[best] - 0.16f : attraction[cell];
                    Hex hex = HexGrid.FromOffset(cell % world.Width, cell / world.Width);
                    for (int d = 0; d < 6; d++)
                    {
                        int n = HexGrid.NeighborIndex(hex, d, world.Width, world.Height, world.Manifest.HorizontalWrap);
                        if (n < 0 || world.Water[n] != WaterType.None || rank[n] <= rank[cell] ||
                            level[n] > level[cell] || world.Elevation[n] > world.Elevation[cell] + 25) continue;
                        float climb = Math.Max(0, world.Elevation[n] - world.Elevation[cell]) * 0.02f;
                        float score = attraction[n] - 0.16f - climb;
                        if (score > bestScore + 0.005f) { best = n; bestScore = score; }
                    }
                    parent[cell] = best;
                    attraction[cell] = Math.Max(attraction[cell], bestScore);
                }
                Accumulate();
                for (int j = 0; j < order.Count; j++) rank[order[j]] = j;
            }
        }
        progress?.Invoke(new(GenerationStep.Rivers, 65));
        double threshold = area * 1000 * 9000 * (naturalDrainage ? Math.Max(1, Math.Sqrt(world.Height / 256.0)) : 1);
        int joins = 0, curves = 0;
        if (world.Manifest.GeneratorVersion >= 23)
        {
            joins = JoinNearbyHeadwaters(world, parent, level, order, flux, threshold);
            if (joins > 0) Accumulate();
            curves = CurveStraightReaches(world, parent, order, flux, threshold);
            if (curves > 0) Accumulate();
        }
        world.RiverDiagnostics = (joins, curves);
        var incoming = new int[count];
        bool Arid(int i) => world.Temperature[i] > 100 && world.Rainfall[i] < 450;
        // Strong imported flow can cross a desert, but small local streams disappear.
        bool Eligible(int i) => parent[i] >= 0 && world.Water[i] != WaterType.Ocean &&
            flux[i] >= threshold * (world.Manifest.GeneratorVersion >= 22 && Arid(i) ? 4 : 1);
        bool[]? visible = null;
        if (naturalDrainage)
        {
            var downstreamLength = new int[count];
            for (int j = order.Count - 1; j >= 0; j--)
            {
                int cell = order[j], next = parent[cell];
                if (Eligible(cell)) downstreamLength[cell] = 1 + (next >= 0 ? downstreamLength[next] : 0);
            }
            for (int i = 0; i < count; i++) if (Eligible(i)) incoming[parent[i]]++;
            int spacing = Math.Max(3, world.Height / 256);
            int minimumLength = Math.Max(6, world.Height / 128);
            var sources = new List<int>();
            for (int i = 0; i < count; i++)
                if (Eligible(i) && incoming[i] == 0 && downstreamLength[i] >= minimumLength &&
                    (world.Manifest.GeneratorVersion < 24 || world.Water[i] == WaterType.None) &&
                    !(world.Manifest.GeneratorVersion >= 22 && Arid(i))) sources.Add(i);
            sources.Sort((a, b) => downstreamLength[b] != downstreamLength[a]
                ? downstreamLength[b].CompareTo(downstreamLength[a]) : a.CompareTo(b));
            visible = new bool[count];
            var blocked = new bool[count];
            // Lake spillways are valid starts even where a desert would forbid a
            // local spring. Courses entering a lake stop there; the outlet is separate.
            if (world.Manifest.GeneratorVersion >= 24)
                foreach (int outlet in lakeLoss.Keys.OrderBy(i => i))
                    if (Eligible(outlet) && parent[outlet] >= 0 && world.Water[parent[outlet]] == WaterType.None)
                        sources.Add(outlet);
            foreach (int source in sources)
            {
                if (blocked[source]) continue;
                if (world.Manifest.GeneratorVersion >= 24)
                {
                    bool survives = true;
                    for (int cell = source; parent[cell] >= 0 && !visible[cell]; cell = parent[cell])
                    {
                        if (cell != source && world.Water[cell] != WaterType.None) break;
                        if (world.Water[cell] == WaterType.None && Arid(cell) && flux[cell] < threshold * 4)
                        { survives = false; break; }
                    }
                    if (!survives) continue;
                    for (int cell = source; parent[cell] >= 0 && !visible[cell]; cell = parent[cell])
                    {
                        if (cell != source && world.Water[cell] != WaterType.None) break;
                        visible[cell] = true;
                    }
                }
                else
                // Keep complete downstream courses, including confluences. Runoff from
                // invisible headwaters was already included in the accumulation above.
                for (int cell = source; Eligible(cell) && !visible[cell]; cell = parent[cell]) visible[cell] = true;
                Hex origin = HexGrid.FromOffset(source % world.Width, source / world.Width);
                for (int r = -spacing; r <= spacing; r++)
                for (int q = Math.Max(-spacing, -r - spacing); q <= Math.Min(spacing, -r + spacing); q++)
                {
                    var (col, row) = HexGrid.ToOffset(new Hex(origin.Q + q, origin.R + r));
                    if ((uint)row >= world.Height) continue;
                    if (world.Manifest.HorizontalWrap) col = (col % world.Width + world.Width) % world.Width;
                    if ((uint)col < world.Width) blocked[row * world.Width + col] = true;
                }
            }
            Array.Clear(incoming);
        }
        bool Active(int i) => world.Manifest.GeneratorVersion >= 24
            ? parent[i] >= 0 && visible is not null && visible[i]
            : Eligible(i) && (visible is null || visible[i]);
        for (int i = 0; i < count; i++) if (Active(i)) incoming[parent[i]]++;
        var rivers = new List<RiverRecord>();
        var startIds = new Dictionary<int, int>();
        for (int i = 0; i < count; i++)
        {
            if (!Active(i) || incoming[i] == 1) continue;
            var path = new List<int> { i }; var tiers = new List<byte>(); var flows = new List<float>();
            int cell = i;
            do
            {
                tiers.Add(world.Manifest.GeneratorVersion >= 23
                    ? (byte)(flux[cell] >= threshold * 50 ? 5 : flux[cell] >= threshold * 15 ? 4 : flux[cell] >= threshold * 5 ? 3 : flux[cell] >= threshold * 2 ? 2 : 1)
                    : (byte)(flux[cell] >= threshold * 10 ? 3 : flux[cell] >= threshold * 3 ? 2 : 1));
                flows.Add((float)flux[cell]);
                cell = parent[cell]; path.Add(cell);
            } while (Active(cell) && incoming[cell] == 1);
            string end = world.Water[cell] == WaterType.Ocean ? "Ocean" : world.Water[cell] == WaterType.Lake ? "Lake"
                : world.Water[cell] == WaterType.Wetland && world.Manifest.GeneratorVersion >= 24 ? "Wetland"
                : world.Manifest.GeneratorVersion >= 24 && parent[cell] < 0 &&
                    (cell / world.Width == 0 || cell / world.Width == world.Height - 1 ||
                    (!world.Manifest.HorizontalWrap && (cell % world.Width == 0 || cell % world.Width == world.Width - 1))) ? "Boundary" : "Custom";
            startIds[i] = rivers.Count;
            rivers.Add(new(rivers.Count, path.ToArray(), tiers.ToArray(), flows.ToArray(), end, null));
        }
        for (int i = 0; i < rivers.Count; i++)
            if (startIds.TryGetValue(rivers[i].Path[^1], out int downstream)) rivers[i] = rivers[i] with { ParentId = downstream };
        progress?.Invoke(new(GenerationStep.Rivers, 90));
        WriteRivers(world, rivers.ToArray());
        progress?.Invoke(new(GenerationStep.Rivers, 100));
    }
}

