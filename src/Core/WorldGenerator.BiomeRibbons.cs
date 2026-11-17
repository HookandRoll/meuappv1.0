using System;
using System.Numerics;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    private static void FinishBiomeRibbons(GenerationArea area, bool[] openPlains, Action<GenerationProgress>? progress)
    {
        var source = (BiomeType[])area.Biomes.Clone();
        VisitChunks(area.Chunks, (i, col, row) =>
        {
            BiomeType current = source[i];
            if (current is BiomeType.None or BiomeType.Beach or BiomeType.Alpine)
                return;
            Hex hex = area.WorldHex(col, row);
            Span<BiomeType> neighbors = stackalloc BiomeType[6];
            for (int direction = 0; direction < 6; direction++)
            {
                int neighbor = area.Neighbor(hex, direction);
                if (neighbor < 0 || source[neighbor] is BiomeType.None or BiomeType.Beach)
                    return;
                neighbors[direction] = source[neighbor];
            }

            BiomeType replacement = FindBiomeRibbonReplacement(current, neighbors, out int extendDirection);
            if (replacement == BiomeType.None)
                replacement = FindDoubleBiomeRibbon(area, source, hex, current, neighbors);
            if (replacement == BiomeType.None || (openPlains[i] && Biomes.IsForest(replacement)))
                return;
            if (extendDirection >= 0)
            {
                int farther = area.Neighbor(hex, extendDirection, steps: 2);
                if (farther < 0 || source[farther] != current)
                    return;
            }
            area.Biomes[i] = replacement;
        }, progress, GenerationStep.Biomes, 97, 100);
    }

    private static BiomeType FindDoubleBiomeRibbon(GenerationArea area, BiomeType[] source, Hex hex,
        BiomeType current, ReadOnlySpan<BiomeType> neighbors)
    {
        for (int axis = 0; axis < 6; axis++)
        {
            int innerA = (axis + 1) % 6;
            int innerB = (axis + 2) % 6;
            if (neighbors[axis] != current || neighbors[(axis + 3) % 6] != current ||
                neighbors[innerA] != current || neighbors[innerB] != current)
                continue;
            BiomeType outside = neighbors[(axis + 4) % 6];
            if (outside == current || outside == BiomeType.Alpine || neighbors[(axis + 5) % 6] != outside)
                continue;
            int farA = area.Neighbor(hex, innerA, steps: 2);
            int farB = area.Neighbor(hex, innerB, steps: 2);
            if (farA >= 0 && farB >= 0 && source[farA] == outside && source[farB] == outside)
                return outside;
        }
        return BiomeType.None;
    }

    internal static BiomeType FindBiomeRibbonReplacement(BiomeType current, ReadOnlySpan<BiomeType> neighbors,
        out int extendDirection)
    {
        extendDirection = -1;
        uint matching = 0;
        for (int direction = 0; direction < 6; direction++)
        {
            if (neighbors[direction] == current)
                matching |= 1u << direction;
        }
        int along = BitOperations.PopCount(matching);
        // A ribbon has one or two neighbors along an axis and a different biome on both flanks.
        if (along == 0 || along > 2 || (along == 2 && (matching & (matching >> 3)) == 0))
            return BiomeType.None;
        int axis = BitOperations.TrailingZeroCount(matching);
        int opposite = (axis + 3) % 6;
        for (int direction = 0; direction < 6; direction++)
        {
            if (direction == axis || direction == opposite || neighbors[direction] == BiomeType.Alpine)
                continue;
            BiomeType candidate = neighbors[direction];
            int flanks = 0;
            for (int other = 0; other < 6; other++)
            {
                if (other != axis && other != opposite && neighbors[other] == candidate)
                    flanks++;
            }
            if (flanks >= 3)
            {
                if (along == 1)
                    extendDirection = axis;
                return candidate;
            }
        }
        return BiomeType.None;
    }
}
