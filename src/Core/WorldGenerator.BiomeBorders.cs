using System;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    private static void RoughenBiomeBorders(GenerationArea area, uint seed)
    {
        var source = (BiomeType[])area.Biomes.Clone();
        int radius = Math.Max(1, area.WorldHeight / 128);
        VisitChunks(area.Chunks, (i, col, row) =>
        {
            int x = area.WorldColumn(col), y = area.WorldRow(row), cell = y * area.WorldWidth + x;
            // Rebuild the halo too: consolidation reads its neighbors before
            // CopyRegionTo applies the selection and locks to the real world.
            if (area.Water[i] != WaterType.None || source[i] == BiomeType.Beach || area.Locked(cell, HexLocks.Biome)) return;
            float aspect = area.WorldWidth / (float)area.WorldHeight;
            float field = TerrainNoise(x / (float)area.WorldHeight, y / (float)area.WorldHeight, 31, aspect,
                area.Wrap, seed ^ 0xA39D71C5u);
            int direction = Math.Min(5, (int)(field * 6));
            int steps = Math.Min(radius, 1 + (int)(TerrainNoise(x / (float)area.WorldHeight, y / (float)area.WorldHeight, 73, aspect,
                area.Wrap, seed ^ 0x65C1B39Du) * radius));
            int n = area.Neighbor(area.WorldHex(col, row), direction, steps);
            if (n < 0 || area.Water[n] != WaterType.None || source[n] == source[i] || source[n] == BiomeType.Beach) return;
            var biome = source[n];
            bool fits = biome == BiomeType.Alpine ? area.Elevation[i] >= 2100 && area.Temperature[i] <= 55
                : FitsBiomeClimate(biome, area.Temperature[i], area.Rainfall[i]);
            if (fits) area.Biomes[i] = biome;
        }, null, GenerationStep.Biomes, 0, 100);
    }
}
