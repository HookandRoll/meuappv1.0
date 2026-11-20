using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    private static short[] BuildGentleElevation(int width, int height, bool wrap,
        Func<int, WaterType> water, Func<int, short> elevation)
    {
        int count = width * height;
        var original = new short[count];
        var smooth = new float[count];
        var scratch = new float[count];
        Parallel.For(0, count, i => { original[i] = elevation(i); smooth[i] = original[i]; });
        int radius = Math.Max(2, (int)MathF.Round(height / 160f));
        float divisor = 2 * radius + 1;
        for (int pass = 0; pass < 2; pass++)
        {
            Parallel.For(0, height, row =>
            {
                int start = row * width;
                int X(int x) => wrap ? (x % width + width) % width : Math.Clamp(x, 0, width - 1);
                float sum = 0;
                for (int x = -radius; x <= radius; x++) sum += smooth[start + X(x)];
                for (int x = 0; x < width; x++)
                {
                    scratch[start + x] = sum / divisor;
                    sum += smooth[start + X(x + radius + 1)] - smooth[start + X(x - radius)];
                }
            });
            Parallel.For(0, width, x =>
            {
                float sum = 0;
                for (int y = -radius; y <= radius; y++) sum += scratch[Math.Clamp(y, 0, height - 1) * width + x];
                for (int y = 0; y < height; y++)
                {
                    smooth[y * width + x] = sum / divisor;
                    sum += scratch[Math.Clamp(y + radius + 1, 0, height - 1) * width + x]
                        - scratch[Math.Clamp(y - radius, 0, height - 1) * width + x];
                }
            });
        }
        // Ease every land/water boundary, including quota-created coast cliffs.
        // This changes height only; the exact water mask and land quota survive.
        int shoreWidth = Math.Max(4, height / 32);
        var shoreDistance = new byte[count];
        Array.Fill(shoreDistance, byte.MaxValue);
        var queue = new Queue<int>();
        for (int i = 0; i < count; i++)
        {
            if (water(i) != WaterType.None) continue;
            Hex hex = HexGrid.FromOffset(i % width, i / width);
            for (int d = 0; d < 6; d++)
            {
                int n = HexGrid.NeighborIndex(hex, d, width, height, wrap);
                if (n >= 0 && water(n) != WaterType.None)
                { shoreDistance[i] = 1; queue.Enqueue(i); break; }
            }
        }
        while (queue.TryDequeue(out int cell))
        {
            if (shoreDistance[cell] >= shoreWidth) continue;
            Hex hex = HexGrid.FromOffset(cell % width, cell / width);
            for (int d = 0; d < 6; d++)
            {
                int n = HexGrid.NeighborIndex(hex, d, width, height, wrap);
                if (n < 0 || water(n) != WaterType.None || shoreDistance[n] != byte.MaxValue) continue;
                shoreDistance[n] = (byte)(shoreDistance[cell] + 1); queue.Enqueue(n);
            }
        }
        Parallel.For(0, count, i =>
        {
            if (water(i) != WaterType.None) { original[i] = (short)Math.Clamp(smooth[i], -6000, -1); return; }
            float softened = 0.15f * original[i] + 0.85f * smooth[i];
            float coast = Smooth(Math.Clamp(shoreDistance[i] / (float)shoreWidth, 0, 1));
            original[i] = (short)Math.Clamp(softened * coast, 1, 7200);
        });
        return original;
    }

    private sealed record RegionalRainField(ushort[] Rain, ushort[] Moisture);

    // The regional baseline deliberately has no elevation input. Topography only
    // modulates moisture transport and bounded windward enhancement afterward.
    internal static ushort RegionalRainBaseline(float x, float y, float aspect, float maritime, uint seed, bool wrap)
    {
        float latitude = Math.Abs(y - 0.5f) * 2;
        float equator = 1 - Smooth(Math.Clamp(latitude / 0.52f, 0, 1));
        float temperate = Smooth(Math.Max(0, 1 - Math.Abs(latitude - 0.65f) / 0.28f));
        float region = 0.80f * TerrainNoise(x * aspect, y, 2.1f, aspect, wrap, seed ^ 0xD46A89C3u)
            + 0.20f * TerrainNoise(x * aspect, y, 5.7f, aspect, wrap, seed ^ 0x71F28E5Bu);
        return (ushort)Math.Clamp(300 + 1200 * equator + 650 * temperate + 250 * maritime + 1150 * (region - 0.5f), 60, 4000);
    }

    private static RegionalRainField BuildRegionalRain(WorldData world, GenerationArea area,
        Func<int, int, short> elevation, Func<int, int, WaterType> water, uint seed)
    {
        var result = new RegionalRainField(new ushort[area.Count], new ushort[area.Count]);
        int top = area.WorldRow(0), rows = area.Count / area.Chunks.Width;
        int width = world.Width, radius = Math.Max(1, world.Height / 128);
        Parallel.For(top, top + rows, row =>
        {
            var heights = new short[width]; var smooth = new short[width];
            var surface = new WaterType[width];
            var east = new ushort[width]; var west = new ushort[width];
            for (int x = 0; x < width; x++) { surface[x] = water(x, row); heights[x] = surface[x] == WaterType.None ? elevation(x, row) : (short)0; }
            int X(int x) => world.Manifest.HorizontalWrap ? (x % width + width) % width : Math.Clamp(x, 0, width - 1);
            int sum = 0;
            for (int x = -radius; x <= radius; x++) sum += heights[X(x)];
            for (int x = 0; x < width; x++)
            {
                smooth[x] = (short)(sum / (2 * radius + 1));
                sum += heights[X(x + radius + 1)] - heights[X(x - radius)];
            }
            float length = Math.Max(8, world.Height / 12f);
            TransportMoistureRow(smooth, surface, east, width, world.Manifest.HorizontalWrap, 1, length);
            TransportMoistureRow(smooth, surface, west, width, world.Manifest.HorizontalWrap, -1, length);
            float latitude = Math.Abs(2f * row / world.Height - 1);
            float westerly = Smooth(Math.Clamp((latitude - 0.25f) / 0.12f, 0, 1))
                * (1 - Smooth(Math.Clamp((latitude - 0.65f) / 0.12f, 0, 1)));
            for (int localX = 0; localX < area.Chunks.Width; localX++)
            {
                int x = area.WorldColumn(localX), i = (row - top) * area.Chunks.Width + localX;
                float humidity = Interpolate(west[x], east[x], westerly) / 65535f;
                float ascent = Interpolate(Math.Max(0, smooth[x] - smooth[X(x + 1)]), Math.Max(0, smooth[x] - smooth[X(x - 1)]), westerly);
                ushort baseline = RegionalRainBaseline(x / (float)width, row / (float)world.Height,
                    width / (float)world.Height, humidity, seed, world.Manifest.HorizontalWrap);
                float enhancement = humidity * (1 - MathF.Exp(-ascent / 1800f)) * 400;
                result.Rain[i] = (ushort)Math.Clamp(MathF.Round(baseline * (0.75f + 0.40f * humidity) + enhancement), 0, 5000);
                result.Moisture[i] = (ushort)Math.Clamp(MathF.Round(humidity * 65535), 0, 65535);
            }
        });
        return result;
    }
}
