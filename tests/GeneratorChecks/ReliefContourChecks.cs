using System;
using System.Linq;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    internal static void CheckReliefContours()
    {
        foreach (bool wrap in new[] { false, true })
        foreach (int height in new[] { 64, 512 })
        {
            int width = height * 2;
            var world = new WorldData(CheckManifest(width, height, wrap));
            Array.Fill(world.Elevation, (short)100);
            // Deliberately stale/painted mountain classifications: geometry must
            // still keep the flat interior and surrounding plain free of art.
            Array.Fill(world.Relief, ReliefType.Mountains);
            int left = width / 3, right = width * 2 / 3;
            for (int row = 0; row < height; row++)
            for (int col = left; col < right; col++) world.Elevation[row * width + col] = 1200;
            var original = (short[])world.Elevation.Clone();
            int count = 0;
            int radius = TerrainClassifier.RegionalRadius(world.Manifest);
            var symbols = new byte[world.Count];
            for (int i = 0; i < world.Count; i++)
            {
                byte bits = symbols[i] = ReliefContours.Symbol(world, i);
                if (bits == 0) continue;
                count++;
                int col = i % width;
                int borderDistance = Math.Min(col - left, right - 1 - col);
                bool contour = (bits & MapRenderFormat.ContourSymbol) != 0;
                if (world.Elevation[i] != 1200 || borderDistance > (contour ? 1 : Math.Clamp(height / 512 + 2, 2, 4) + 1))
                    throw new Exception("Relief symbol filled a plateau interior or the low side of a cliff.");
                if (!contour) continue; // Uphill spurs are permitted behind the contour.
                int direction = (bits & 7) - 1;
                int n = HexGrid.NeighborIndex(HexGrid.FromOffset(col, i / width), direction, width, height, wrap, radius);
                int near = HexGrid.NeighborIndex(HexGrid.FromOffset(col, i / width), direction, width, height, wrap);
                bool lower = (n >= 0 && world.Elevation[n] < world.Elevation[i]) ||
                    (near >= 0 && world.Elevation[near] < world.Elevation[i]);
                if (!lower || (bits & MapRenderFormat.ContourSymbol) == 0)
                    throw new Exception("Relief symbol does not point towards its elevation boundary.");
            }
            if (count < height / 8 || !original.SequenceEqual(world.Elevation))
                throw new Exception("Contour placement removed the escarpment or altered terrain.");
            if (!symbols.SequenceEqual(Enumerable.Range(0, world.Count).Select(i => ReliefContours.Symbol(world, i))))
                throw new Exception("Relief contours are not deterministic.");

            if (height == 512)
            {
                for (int i = 0; i < world.Count; i++)
                    world.Elevation[i] = (short)(100 + Math.Clamp(i % width - left, 0, 9) * 200);
                // A uniform wide slope has no successive breaks inside it.
                // Regional sampling must not turn every row of that face into art.
                for (int row = 10; row < height - 10; row++)
                for (int col = left + 3; col <= left + 6; col++)
                    if ((ReliefContours.Symbol(world, row * width + col) & MapRenderFormat.ContourSymbol) != 0)
                        throw new Exception("Contour symbols filled a uniform inclined face.");
                int rimSymbols = 0;
                for (int row = 10; row < height - 10; row++)
                for (int col = left + 7; col <= left + 10; col++)
                    if (ReliefContours.Symbol(world, row * width + col) != 0) rimSymbols++;
                if (rimSymbols == 0) throw new Exception("Broad slope lost its upper contour.");
            }

            // High interiors now retain sparse mountain groups as requested;
            // they must not become either an empty field or a solid carpet.
            Array.Fill(world.Elevation, (short)4000);
            int inside = Enumerable.Range(0, world.Count).Count(i => ReliefContours.Symbol(world, i) != 0);
            if (inside < world.Count / 20 || inside > world.Count / 2)
                throw new Exception("High interior mountain density is empty or excessive.");
            if (height == 512)
            {
                int denseCells = 0, denseArt = 0, sparseCells = 0, sparseArt = 0;
                for (int i = 0; i < world.Count; i++)
                {
                    float field = TerrainNoise((i % width + (i / width & 1) * 0.5f) / height, i / width / (float)height,
                        18, width / (float)height, wrap, unchecked((uint)world.Manifest.Seed) ^ 0xB175A39Du);
                    if (field > 0.58f) { denseCells++; if (ReliefContours.Symbol(world, i) != 0) denseArt++; }
                    if (field < 0.43f) { sparseCells++; if (ReliefContours.Symbol(world, i) != 0) sparseArt++; }
                }
                if (denseCells == 0 || sparseCells == 0 || denseArt / (float)denseCells < 2 * sparseArt / (float)sparseCells)
                    throw new Exception("Mountain symbols are scattered uniformly instead of clustered.");
                Array.Fill(world.Elevation, (short)1500);
                int ordinary = Enumerable.Range(0, world.Count).Count(i => ReliefContours.Symbol(world, i) != 0);
                Array.Fill(world.Elevation, (short)5000);
                int summits = Enumerable.Range(0, world.Count).Count(i => ReliefContours.Symbol(world, i) != 0);
                if (ordinary == 0 || ordinary >= summits * 0.8f) throw new Exception("Ordinary mountains are not sparser than summit groups.");
                Console.WriteLine($"PASS: clustered ordinary mountains={ordinary}, summit region={summits}; dense/sparse patch ratio={(denseArt / (float)denseCells) / (sparseArt / (float)sparseCells):F1}");
                Array.Fill(world.Elevation, (short)4000);
            }
            byte beforeSea = ReliefContours.Symbol(world, width + 2);
            world.Water[width + 1] = WaterType.Ocean; world.Elevation[width + 1] = -6000;
            if (ReliefContours.Symbol(world, width + 2) != beforeSea)
                throw new Exception("Ocean depth created a false escarpment.");
            Console.WriteLine($"PASS: relief contour {width}x{height}, wrap={wrap}: {count} symbols on high edges, clear flat interiors/low sides, downhill anchors, legacy classifications, determinism, unchanged altitude");
        }
    }
}
