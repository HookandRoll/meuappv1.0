using System;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    internal static void FinishShoreline(float[] scores, int width, int height, bool wrap, float seaLevel,
        bool removeIslandRibbons = true, Action<GenerationProgress>? progress = null)
    {
        var changes = new sbyte[scores.Length];
        var chunks = new ChunkGrid(width, height);
        VisitChunks(chunks, (i, col, row) =>
        {
            float altitude = scores[i] - seaLevel;
            if (altitude < 0f)
            {
                if (altitude >= -0.05f)
                    FillShallowPocket(scores, changes, i, width, height, wrap, seaLevel);
            }
            else
            {
                if (altitude <= 0.065f)
                    TrimNarrowSpit(scores, changes, i, width, height, wrap, seaLevel);
                if (removeIslandRibbons && altitude <= 0.14f)
                    RemoveShallowIslandRibbon(scores, changes, i, width, height, wrap, seaLevel);
            }
        }, progress, GenerationStep.Land, 84, 85);

        // Every decision used the same coast. Applying flips together avoids directional erosion.
        VisitChunks(chunks, (i, _, _) =>
        {
            if (changes[i] != 0)
                scores[i] = seaLevel + 0.012f * changes[i];
        }, null, GenerationStep.Land, 85, 85);
    }

    private static void RemoveShallowIslandRibbon(float[] scores, sbyte[] changes, int start,
        int width, int height, bool wrap, float seaLevel)
    {
        // The lowest cell index owns the decision, so a component is edited only once.
        Span<int> island = stackalloc int[96];
        island[0] = start;
        Hex origin = HexGrid.FromOffset(start % width, start / width);
        int minQ = origin.Q, maxQ = origin.Q;
        int minR = origin.R, maxR = origin.R;
        int minS = -origin.Q - origin.R, maxS = minS;
        int count = 1;
        int shallow = 0;
        for (int head = 0; head < count; head++)
        {
            int index = island[head];
            Hex hex = HexGrid.FromOffset(index % width, index / width);
            int q = hex.Q;
            if (wrap)
            {
                float x = q - origin.Q + (hex.R - origin.R) * 0.5f;
                q -= (int)MathF.Round(x / width) * width;
            }
            int s = -q - hex.R;
            minQ = Math.Min(minQ, q); maxQ = Math.Max(maxQ, q);
            minR = Math.Min(minR, hex.R); maxR = Math.Max(maxR, hex.R);
            minS = Math.Min(minS, s); maxS = Math.Max(maxS, s);
            if (Math.Min(maxQ - minQ, Math.Min(maxR - minR, maxS - minS)) >= 3)
                return;
            if (scores[index] <= seaLevel + 0.075f)
                shallow++;
            for (int direction = 0; direction < 6; direction++)
            {
                int neighbor = HexGrid.NeighborIndex(hex, direction, width, height, wrap);
                if (neighbor < 0 || float.IsNegativeInfinity(scores[neighbor]))
                    return;
                float score = scores[neighbor];
                if (score < seaLevel)
                    continue;
                if (neighbor < start || score > seaLevel + 0.14f)
                    return;
                if (island[..count].Contains(neighbor))
                    continue;
                if (count == island.Length)
                    return;
                island[count++] = neighbor;
            }
        }

        int length = 1 + Math.Max(maxQ - minQ, Math.Max(maxR - minR, maxS - minS));
        int thickness = 1 + Math.Min(maxQ - minQ, Math.Min(maxR - minR, maxS - minS));
        if (length < Math.Max(6, 4 * thickness) || shallow * 4 < count * 3 ||
            (wrap && length >= width / 2))
            return;
        foreach (int index in island[..count])
            changes[index] = -1;
    }

    private static void FillShallowPocket(float[] scores, sbyte[] changes, int start,
        int width, int height, bool wrap, float seaLevel)
    {
        Span<int> pocket = stackalloc int[3];
        pocket[0] = start;
        int count = 1;
        for (int k = 0; k < count; k++)
        {
            int index = pocket[k];
            Hex hex = HexGrid.FromOffset(index % width, index / width);
            for (int direction = 0; direction < 6; direction++)
            {
                int neighbor = HexGrid.NeighborIndex(hex, direction, width, height, wrap);
                if (neighbor < 0)
                    return;
                float score = scores[neighbor];
                if (score >= seaLevel)
                    continue;

                // Deeper water, the sea and carved straits must stay connected and submerged.
                if (neighbor < start || score < seaLevel - 0.05f)
                    return;
                if (pocket[..count].Contains(neighbor))
                    continue;
                if (count == pocket.Length)
                    return;
                pocket[count++] = neighbor;
            }
        }
        foreach (int index in pocket[..count])
            changes[index] = 1;
    }

    private static void TrimNarrowSpit(float[] scores, sbyte[] changes, int tip,
        int width, int height, bool wrap, float seaLevel)
    {
        Span<int> run = stackalloc int[12];
        int count = 0;
        int previous = -1;
        int index = tip;
        while (count < run.Length)
        {
            Hex hex = HexGrid.FromOffset(index % width, index / width);
            int next = -1;
            int dry = 0;
            for (int direction = 0; direction < 6; direction++)
            {
                int neighbor = HexGrid.NeighborIndex(hex, direction, width, height, wrap);
                if (neighbor < 0 || float.IsNegativeInfinity(scores[neighbor]))
                    return;
                if (scores[neighbor] < seaLevel)
                    continue;
                dry++;
                if (neighbor != previous)
                    next = neighbor;
            }
            if (dry >= 3)
                break;
            // Start at a tip and follow a one-hex-wide run. Islands and through-bridges stay intact.
            if (dry != (count == 0 ? 1 : 2) || run[..count].Contains(next))
                return;
            run[count++] = index;
            previous = index;
            index = next;
        }
        if (count < 2 || count == run.Length)
            return;

        // Leave the attached base of the spit in place and preserve substantial relief.
        int trim = Math.Min(4, (count + 1) / 2);
        for (int i = 0; i < trim && scores[run[i]] <= seaLevel + 0.065f; i++)
            changes[run[i]] = -1;
    }
}
