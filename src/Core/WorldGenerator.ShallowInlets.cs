using System;
using System.Numerics;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    internal static void RoundShallowInlets(float[] scores, int width, int height, bool wrap, float seaLevel)
    {
        var fill = new bool[scores.Length];
        var chunks = new ChunkGrid(width, height);
        VisitChunks(chunks, (i, _, _) =>
        {
            if (scores[i] < seaLevel && scores[i] >= seaLevel - 0.065f)
                MarkInletEnd(scores, fill, i, width, height, wrap, seaLevel);
        }, null, GenerationStep.Land, 85, 85);
        VisitChunks(chunks, (i, _, _) =>
        {
            if (fill[i])
                scores[i] = seaLevel + 0.012f;
        }, null, GenerationStep.Land, 85, 85);
    }

    private static void MarkInletEnd(float[] scores, bool[] fill, int start,
        int width, int height, bool wrap, float seaLevel)
    {
        const int radius = 4;
        const int maxCrossSection = 3;
        const float shallowDepth = 0.065f;
        Hex origin = HexGrid.FromOffset(start % width, start / width);
        uint directions = 0;
        int firstWater = -1;
        float altitude = scores[start] - seaLevel;
        float elevations = altitude;
        for (int direction = 0; direction < 6; direction++)
        {
            int neighbor = HexGrid.NeighborIndex(origin, direction, width, height, wrap);
            if (neighbor < 0 || scores[neighbor] < seaLevel - shallowDepth)
                return;
            float adjacent = scores[neighbor] - seaLevel;
            elevations += Math.Min(0.30f, adjacent);
            if (adjacent < 0f)
            {
                firstWater = neighbor;
                directions |= 1u << direction;
            }
        }

        int opening = BitOperations.PopCount(directions);
        // A cap has one small arc of wet neighbors. A middle cell of a through-channel has two.
        float smoothed = (2f * altitude + elevations / 7f) / 3f;
        if (opening is < 1 or > 3 || CountWetArcs(directions) != 1 || smoothed <= 0f)
            return;

        Span<int> water = stackalloc int[11];
        Span<int> mouth = stackalloc int[maxCrossSection];
        water[0] = start;
        int count = 1;
        int mouthCount = 0;
        for (int head = 0; head < count; head++)
        {
            int index = water[head];
            Hex hex = HexGrid.FromOffset(index % width, index / width);
            for (int direction = 0; direction < 6; direction++)
            {
                int neighbor = HexGrid.NeighborIndex(hex, direction, width, height, wrap);
                if (neighbor < 0 || float.IsNegativeInfinity(scores[neighbor]))
                    return;
                if (scores[neighbor] >= seaLevel)
                    continue;
                Hex next = HexGrid.FromOffset(neighbor % width, neighbor / width);
                if (HexGrid.Distance(origin, next, width, wrap) == radius)
                {
                    if (mouth[..mouthCount].Contains(neighbor))
                        continue;
                    if (mouthCount == mouth.Length)
                        return;
                    mouth[mouthCount++] = neighbor;
                }
                else if (!water[..count].Contains(neighbor))
                {
                    if (count == water.Length || scores[neighbor] < seaLevel - shallowDepth)
                        return;
                    water[count++] = neighbor;
                }
            }
        }

        if (!HasSingleMouth(mouth[..mouthCount], width, wrap))
            return;
        fill[start] = true;
        if (opening == 1)
        {
            Hex inner = HexGrid.FromOffset(firstWater % width, firstWater / width);
            int wet = 0;
            for (int d = 0; d < 6; d++)
            {
                int neighbor = HexGrid.NeighborIndex(inner, d, width, height, wrap);
                if (neighbor >= 0 && scores[neighbor] < seaLevel)
                    wet++;
            }
            if (wet == 2)
                fill[firstWater] = true;
        }
    }

    private static int CountWetArcs(uint directions)
    {
        uint previous = ((directions << 1) | (directions >> 5)) & 63u;
        return BitOperations.PopCount(directions & ~previous);
    }

    private static bool HasSingleMouth(ReadOnlySpan<int> mouth, int width, bool wrap)
    {
        if (mouth.IsEmpty)
            return false;
        int links = 0;
        for (int i = 0; i < mouth.Length; i++)
        for (int j = i + 1; j < mouth.Length; j++)
        {
            Hex a = HexGrid.FromOffset(mouth[i] % width, mouth[i] / width);
            Hex b = HexGrid.FromOffset(mouth[j] % width, mouth[j] / width);
            if (HexGrid.Distance(a, b, width, wrap) == 1)
                links++;
        }
        return links >= mouth.Length - 1;
    }
}
