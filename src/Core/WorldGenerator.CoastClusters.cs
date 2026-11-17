using System;
using System.Numerics;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    internal static void RefineCoastClusters(float[] scores, int width, int height, bool wrap, float seaLevel)
    {
        const float coastalBand = 0.09f;
        var chunks = new ChunkGrid(width, height);
        var pattern = new byte[scores.Length];
        VisitChunks(chunks, (i, col, row) =>
        {
            if (Math.Abs(scores[i] - seaLevel) > coastalBand)
                return;
            Hex hex = HexGrid.FromOffset(col, row);
            uint dry = 0;
            for (int d = 0; d < 6; d++)
            {
                int neighbor = HexGrid.NeighborIndex(hex, d, width, height, wrap);
                if (neighbor < 0)
                    return;
                if (scores[neighbor] >= seaLevel)
                    dry |= 1u << d;
            }
            uint matching = scores[i] >= seaLevel ? dry : 63u ^ dry;
            if (matching == 63u)
                return;
            uint rotated = ((dry << 1) | (dry >> 5)) & 63u;
            pattern[i] = BitOperations.PopCount(dry ^ rotated) >= 4 ? (byte)2 : (byte)1;
        }, null, GenerationStep.Land, 85, 85);

        var finished = new float[scores.Length];
        VisitChunks(chunks, (i, col, row) =>
        {
            finished[i] = scores[i];
            if (pattern[i] == 0)
                return;
            Hex hex = HexGrid.FromOffset(col, row);
            int mixed = 0, broken = 0, land = 0;
            float sum = 0f;
            for (int q = -2; q <= 2; q++)
            for (int r = Math.Max(-2, -q - 2); r <= Math.Min(2, -q + 2); r++)
            {
                var (x, y) = HexGrid.ToOffset(new Hex(hex.Q + q, hex.R + r));
                if (wrap)
                    x = (x + width) % width;
                if ((uint)x >= (uint)width || (uint)y >= (uint)height)
                    return;
                int index = y * width + x;
                float score = scores[index];
                if (float.IsNegativeInfinity(score))
                    return;
                if (score >= seaLevel)
                    land++;
                if (pattern[index] != 0)
                    mixed++;
                if (pattern[index] == 2)
                    broken++;
                sum += Math.Clamp(score - seaLevel, -coastalBand, coastalBand);
            }
            // A normal shore generally crosses this disk once. Work where several
            // small fragments and channels share the same patch of shallow coast.
            if (land is < 5 or > 14 || mixed < 14 || (broken < 4 && mixed < 18))
                return;

            float anchors = 0f;
            int count = 0;
            for (int d = 0; d < 6; d++)
            {
                int neighbor = HexGrid.NeighborIndex(hex, d, width, height, wrap, steps: 4);
                if (neighbor < 0)
                    continue;
                if (float.IsNegativeInfinity(scores[neighbor]))
                    return;
                anchors += Math.Clamp(scores[neighbor] - seaLevel, -coastalBand, coastalBand);
                count++;
            }
            float local = sum / 19f;
            float shape = count == 0 ? local : Interpolate(local, anchors / count, 0.44f);
            finished[i] = seaLevel + Interpolate(scores[i] - seaLevel, shape, 0.75f);
        }, null, GenerationStep.Land, 85, 85);
        // Favor connected banks. Added land needs support from land that survives
        // the pass, and a cut needs to leave enough land beside surviving cells.
        VisitChunks(chunks, (i, col, row) =>
        {
            pattern[i] = 1;
            bool wasLand = scores[i] >= seaLevel;
            if (wasLand == (finished[i] >= seaLevel))
                return;
            Hex hex = HexGrid.FromOffset(col, row);
            int support = 0;
            for (int d = 0; d < 6; d++)
            {
                int neighbor = HexGrid.NeighborIndex(hex, d, width, height, wrap);
                if (neighbor < 0 || scores[neighbor] < seaLevel || finished[neighbor] < seaLevel)
                    continue;
                support++;
                if (!wasLand)
                    continue;

                Hex shore = HexGrid.FromOffset(neighbor % width, neighbor / width);
                int stableNeighbors = 0;
                for (int side = 0; side < 6; side++)
                {
                    int next = HexGrid.NeighborIndex(shore, side, width, height, wrap);
                    if (next >= 0 && scores[next] >= seaLevel && finished[next] >= seaLevel)
                        stableNeighbors++;
                }
                if (stableNeighbors < 2)
                {
                    pattern[i] = 0;
                    return;
                }
            }
            if (!wasLand && support < 2)
                pattern[i] = 0;
        }, null, GenerationStep.Land, 85, 85);
        for (int i = 0; i < scores.Length; i++)
        {
            if (pattern[i] != 0)
                scores[i] = finished[i];
        }
    }
}
