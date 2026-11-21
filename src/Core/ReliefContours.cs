using System;

namespace Mapmaker.Core;

// Cartographic symbol placement, independent of saved relief classifications.
public static class ReliefContours
{
    public static byte Symbol(WorldData world, int cell)
    {
        if (world.Manifest.Generation.Stage < GenerationStage.Relief || !world.Domain.Contains(cell) ||
            world.Water[cell] != WaterType.None || world.Relief[cell] > ReliefType.Peak)
            return 0;
        var samples = new TerrainClassifier.Samples(world);
        var edge = Edge(samples, cell);
        byte contour = Contour(samples, cell, edge);
        if (contour != 0) return contour;

        uint rank = Rank(cell ^ world.Manifest.Seed);
        int height = samples.Height(cell);
        bool highland = height >= 2800 || (height >= 2300 && world.Biomes[cell] == BiomeType.Alpine) ||
            (height >= 1400 && world.Relief[cell] >= ReliefType.Mountains);
        if (highland)
        {
            float cluster = WorldGenerator.TerrainNoise((cell % world.Width + (cell / world.Width & 1) * 0.5f) / world.Height,
                cell / world.Width / (float)world.Height, 18, world.Width / (float)world.Height,
                world.Manifest.HorizontalWrap, unchecked((uint)world.Manifest.Seed) ^ 0xB175A39Du);
            // Broad patches concentrate mountain groups instead of sprinkling
            // them evenly. Ordinary ranges stay sparser than summit regions.
            float patch = Math.Clamp((cluster - 0.40f) / 0.25f, 0, 1);
            patch = patch * patch * (3 - 2 * patch);
            float density = height >= 4800 ? 0.13f + 0.16f * patch : 0.035f + 0.16f * patch;
            if ((rank & 65535) / 65535f < density)
                return (byte)(MapRenderFormat.ReliefSymbol | MapRenderFormat.MassifSymbol);
        }

        // Short, seeded spurs grow from the contour towards equal/higher land.
        // Shared anchor hashes make coherent fingers, rather than random dots.
        if (height < 700 || rank % 100 >= 48) return 0;
        var hex = HexGrid.FromOffset(cell % world.Width, cell / world.Width);
        int depth = Math.Clamp(world.Height / 512 + 2, 2, 4);
        for (int step = 1; step <= depth; step++)
        for (int d = 0; d < 6; d++)
        {
            int anchor = HexGrid.NeighborIndex(hex, d, world.Width, world.Height, world.Manifest.HorizontalWrap, step);
            if (!samples.IsLand(anchor) || samples.Height(anchor) > height + 20 || world.Relief[anchor] < ReliefType.Hills) continue;
            var anchorEdge = Edge(samples, anchor);
            int turn = (d - anchorEdge.Direction + 6) % 6;
            if (turn is not (0 or 1 or 5) || Rank(anchor ^ world.Manifest.Seed ^ 0x175A3) % 100 >= 65 - step * 12) continue;
            if (Contour(samples, anchor, anchorEdge) != 0)
                return (byte)(MapRenderFormat.ReliefSymbol | MapRenderFormat.MassifSymbol);
        }
        return 0;
    }

    public static ReliefType VisualRelief(WorldData world, int cell)
    {
        int h = world.Elevation[cell];
        if (h >= 4800 && Rank(cell ^ world.Manifest.Seed) % 5 == 0) return ReliefType.Peak;
        if (h >= 2300) return ReliefType.Mountains;
        return world.Relief[cell] < ReliefType.Hills ? ReliefType.Hills : world.Relief[cell];
    }

    private static byte Contour(in TerrainClassifier.Samples samples, int cell, (int Drop, int Direction, int NearDrop) edge)
    {
        var world = samples.World;
        if (world.Relief[cell] <= ReliefType.Flat) return 0;
        int threshold = world.Relief[cell] >= ReliefType.Mountains ? 250 : 100;
        if (edge.Drop < threshold) return 0;

        // Thin a broad inclined face to its strongest break of slope. Compare
        // across the contour, never along it, so an escarpment remains a line.
        var hex = HexGrid.FromOffset(cell % world.Width, cell / world.Width);
        int strength = edge.Drop + edge.NearDrop, minimum = strength;
        for (int step = 1; step <= samples.Radius; step++)
        for (int side = 0; side < 2; side++)
        {
            int n = HexGrid.NeighborIndex(hex, (edge.Direction + side * 3) % 6, world.Width, world.Height,
                world.Manifest.HorizontalWrap, step);
            var neighbor = samples.IsLand(n) ? Edge(samples, n) : default;
            int value = neighbor.Drop + neighbor.NearDrop;
            if (value > strength + 8) return 0;
            // On a uniform ramp, equal-strength samples choose its upper end;
            // they must not produce parallel rows all along the same face.
            if (Math.Abs(value - strength) <= 8 && samples.IsLand(n) && samples.Height(n) > samples.Height(cell) + 8)
                return 0;
            minimum = Math.Min(minimum, value);
        }
        if (strength <= minimum + 8) return 0;

        // Keep every point along the selected contour. Thinning happens only
        // across the slope, preserving a continuous line along the border.
        return (byte)(MapRenderFormat.ReliefSymbol | MapRenderFormat.ContourSymbol | (edge.Direction + 1));
    }

    private static uint Rank(int cell)
    {
        uint value = unchecked((uint)cell) * 0x9E3779B9u;
        value = (value ^ (value >> 16)) * 0x7FEB352Du;
        return value ^ (value >> 15);
    }

    private static (int Drop, int Direction, int NearDrop) Edge(in TerrainClassifier.Samples samples, int cell)
    {
        var world = samples.World;
        var hex = HexGrid.FromOffset(cell % world.Width, cell / world.Width);
        int h = samples.Height(cell), drop = 0, direction = 0, nearDrop = 0;
        for (int d = 0; d < 6; d++)
        {
            int near = HexGrid.NeighborIndex(hex, d, world.Width, world.Height, world.Manifest.HorizontalWrap);
            int far = samples.Radius == 1 ? near : HexGrid.NeighborIndex(hex, d, world.Width, world.Height,
                world.Manifest.HorizontalWrap, samples.Radius);
            int difference = samples.IsLand(near) ? h - samples.Height(near) : 0;
            nearDrop = Math.Max(nearDrop, difference);
            if (samples.IsLand(far)) difference = Math.Max(difference, h - samples.Height(far));
            if (difference > drop) { drop = difference; direction = d; }
        }
        return (drop, direction, nearDrop);
    }

}
