using System;
using System.Collections.Generic;
using System.Linq;

namespace Mapmaker.Core;

internal static class NetworkEditor
{
    internal static int Direction(WorldData world, int from, int to)
    {
        if (!world.Domain.Contains(from) || !world.Domain.Contains(to)) return -1;
        Hex hex = HexGrid.FromOffset(from % world.Width, from / world.Width);
        for (int d = 0; d < 6; d++)
            if (HexGrid.NeighborIndex(hex, d, world.Width, world.Height, world.Manifest.HorizontalWrap) == to) return d;
        return -1;
    }

    private static bool Editable(WorldData world, int a, int b, HexLocks field, WorldRegion? selection)
    {
        bool Inside(int i) => selection is not WorldRegion region ||
            (i % world.Width >= region.Column && i % world.Width < region.Column + region.Width &&
             i / world.Width >= region.Row && i / world.Width < region.Row + region.Height);
        return Direction(world, a, b) >= 0 && Inside(a) && Inside(b) &&
            (((HexLocks)(world.LockMask[a] | world.LockMask[b])) & field) == 0;
    }

    internal static bool Road(WorldData world, int from, int to, int tier, WorldRegion? selection, Action<int> capture)
    {
        if ((uint)tier > 3) throw new ArgumentOutOfRangeException(nameof(tier));
        if (!Editable(world, from, to, HexLocks.Roads, selection)) return false;
        int direction = Direction(world, from, to);
        if (HexGrid.EdgeValue(world.RoadEdges[from], direction) == tier) return false;
        capture(from); capture(to);
        HexGrid.SetSharedEdge(world.RoadEdges, from, direction, tier, world.Width, world.Height, world.Manifest.HorizontalWrap);
        return true;
    }

    private static (int, int) Edge(int a, int b) => a < b ? (a, b) : (b, a);

    // Cut only the requested edges. Surviving pieces retain their original flow and widths.
    internal static bool River(WorldData world, IReadOnlyList<int> path, int level, WorldRegion? selection, Action<int> capture)
    {
        int maxLevel = world.Manifest.GeneratorVersion >= 23 ? 5 : 3;
        if (level < 0 || level > maxLevel) throw new ArgumentOutOfRangeException(nameof(level));
        if (path.Distinct().Count() != path.Count)
            throw new ArgumentException("O caminho manual não pode conter ciclos.", nameof(path));
        var edges = new HashSet<(int, int)>();
        for (int i = 1; i < path.Count; i++)
            if (Editable(world, path[i - 1], path[i], HexLocks.Rivers, selection))
                edges.Add(Edge(path[i - 1], path[i]));
        if (edges.Count == 0) return false;
        RiverRecord[] original = WorldGenerator.ReadRivers(world);
        if (level == 0 && !original.Any(r => Enumerable.Range(0, r.Tiers.Length).Any(j => edges.Contains(Edge(r.Path[j], r.Path[j + 1])))))
            return false;
        foreach (var (a, b) in edges) { capture(a); capture(b); }
        int nextId = original.Length == 0 ? 1 : checked(original.Max(r => r.Id) + 1);
        var result = new List<RiverRecord>();
        var pieces = new Dictionary<int, List<RiverRecord>>();
        foreach (RiverRecord river in original)
        {
            var parts = new List<RiverRecord>();
            int start = 0;
            for (int j = 0; j <= river.Tiers.Length; j++)
            {
                if (j < river.Tiers.Length && !edges.Contains(Edge(river.Path[j], river.Path[j + 1]))) continue;
                if (j > start)
                {
                    var part = river with
                    {
                        Id = parts.Count == 0 ? river.Id : nextId++,
                        Path = river.Path[start..(j + 1)], Tiers = river.Tiers[start..j], AnnualFlow = river.AnnualFlow[start..j],
                        ParentId = j == river.Tiers.Length ? river.ParentId : null,
                        EndType = j == river.Tiers.Length ? river.EndType : "Custom"
                    };
                    parts.Add(part);
                }
                start = j + 1;
            }
            pieces.Add(river.Id, parts);
            result.AddRange(parts);
        }
        for (int i = 0; i < result.Count; i++)
        {
            RiverRecord river = result[i];
            if (river.ParentId is not int parent) continue;
            RiverRecord? destination = pieces[parent].FirstOrDefault(r => r.Path.Contains(river.Path[^1]));
            result[i] = river with { ParentId = destination?.Id,
                EndType = destination is null ? "Custom" : river.EndType };
        }
        if (level > 0)
        {
            int start = 0;
            for (int j = 1; j <= path.Count; j++)
            {
                bool connected = j < path.Count && edges.Contains(Edge(path[j - 1], path[j]));
                if (connected) continue;
                if (j - start >= 2)
                {
                    int[] segment = path.Skip(start).Take(j - start).ToArray();
                    RiverRecord? parent = result.FirstOrDefault(r => r.Path.Contains(segment[^1]) && !r.Path.Take(r.Path.Length - 1).Any(segment.Take(segment.Length - 1).Contains));
                    string end = world.Water[segment[^1]] switch
                    {
                        WaterType.Ocean => "Ocean", WaterType.Lake => "Lake", WaterType.Wetland => "Wetland", _ => "Custom"
                    };
                    result.Add(new RiverRecord(nextId++, segment, Enumerable.Repeat((byte)level, segment.Length - 1).ToArray(),
                        new float[segment.Length - 1], end, parent?.Id));
                }
                start = j;
            }
        }
        WorldGenerator.WriteRivers(world, result.ToArray());
        return true;
    }
}
