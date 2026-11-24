using System;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    private static bool MajorityBiome(BiomeType biome) => !ProtectedHabitat(biome) && Enum.IsDefined(biome);

    private static void PropagateBiomeMajority(GenerationArea area, bool[] openPlains)
    {
        int radius = Math.Clamp(area.WorldHeight / 96, 3, 24);
        var changed = new bool[area.Count];
        foreach (int step in new[] { radius, Math.Max(2, (radius + 1) / 2), 1 })
        {
            // Simultaneous votes: traversal order must not propagate a biome
            // across a continent in one sweep or create regional seams.
            var source = (BiomeType[])area.Biomes.Clone();
            VisitChunks(area.Chunks, (i, col, row) =>
            {
                var current = source[i];
                int cell = area.WorldRow(row) * area.WorldWidth + area.WorldColumn(col);
                if (changed[i] || area.Water[i] != WaterType.None || !MajorityBiome(current) || area.Locked(cell, HexLocks.Biome)) return;
                var hex = area.WorldHex(col, row);
                Span<BiomeType> neighbors = stackalloc BiomeType[6];
                neighbors.Clear();
                int valid = 0, own = 0;
                for (int d = 0; d < 6; d++)
                {
                    int n = area.Neighbor(hex, d, step);
                    if (n < 0 || area.Water[n] != WaterType.None || !MajorityBiome(source[n])) continue;
                    neighbors[d] = source[n]; valid++;
                    if (source[n] == current && ++own >= 3) return;
                }
                if (valid < 5) return;
                BiomeType winner = BiomeType.None;
                for (int d = 0; d < 6; d++)
                {
                    var candidate = neighbors[d];
                    if (candidate == current || !MajorityBiome(candidate)) continue;
                    int votes = 0;
                    for (int n = 0; n < 6; n++) if (neighbors[n] == candidate) votes++;
                    if (votes >= 4) { winner = candidate; break; }
                }
                if (winner == BiomeType.None || !FitsBiomeClimate(winner, area.Temperature[i], area.Rainfall[i], area.GeneratorVersion) ||
                    (openPlains[i] && Biomes.IsForest(winner))) return;
                // The sampled neighbors may lie across a lake or coastline.
                // Every winning ray must be connected through eligible land.
                for (int d = 0; d < 6; d++)
                {
                    if (neighbors[d] != winner) continue;
                    for (int distance = 1; distance < step; distance++)
                    {
                        int n = area.Neighbor(hex, d, distance);
                        if (n < 0 || area.Water[n] != WaterType.None || !MajorityBiome(source[n])) return;
                    }
                }
                area.Biomes[i] = winner;
                changed[i] = true; // Do not flip the absorbed center back on the next scale.
            }, null, GenerationStep.Biomes, 0, 100);
        }
    }
}
