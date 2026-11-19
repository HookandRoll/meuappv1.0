using System;
using System.Buffers;
using System.Threading.Tasks;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    private static int ClimateWind(int row, int height)
    {
        float latitude = Math.Abs(2f * row / height - 1);
        return latitude is > 0.3f and < 0.7f ? 1 : -1;
    }

    private static (float A, float B, float Condensed) MoistureCoefficients(short previous, short elevation, WaterType water, float length)
    {
        float condensed = 1 - MathF.Exp(-Math.Max(0, elevation - previous) / 2200f);
        float retention = MathF.Exp(-1 / length);
        return water switch
        {
            WaterType.Ocean => (0, 1, 0),
            WaterType.Lake => (0.25f, 0.75f * 0.85f, 0),
            _ => (retention * (1 - condensed), (1 - retention) * 0.55f, condensed)
        };
    }

    internal static void TransportMoistureRow(short[] heights, WaterType[] water, ushort[] result,
        int width, bool wrap, int wind, float recoveryLength)
    {
        float aTotal = 1, bTotal = 0;
        int first = wind > 0 ? 0 : width - 1;
        if (wrap)
        {
            for (int step = 0; step < width; step++)
            {
                int x = first + step * wind, previous = (x - wind + width) % width;
                var (a, b, _) = MoistureCoefficients(heights[previous], heights[x], water[x], recoveryLength);
                aTotal *= a; bTotal = a * bTotal + b;
            }
        }
        // Solve h_end = A*h_start+B for a periodic row, without warmup sweeps.
        float humidity = wrap ? bTotal / Math.Max(0.000001f, 1 - aTotal) : 0.65f;
        for (int step = 0; step < width; step++)
        {
            int x = first + step * wind, previous = x - wind;
            previous = wrap ? (previous + width) % width : Math.Clamp(previous, 0, width - 1);
            result[x] = (ushort)Math.Clamp((int)MathF.Round(humidity * 65535), 0, 65535);
            var (a, b, _) = MoistureCoefficients(heights[previous], heights[x], water[x], recoveryLength);
            humidity = a * humidity + b;
        }
    }

    private static ushort ContinuousOrographicRain(ushort baseline, float before, short previous, short elevation,
        WaterType water, float recoveryLength)
    {
        var (a, b, condensed) = MoistureCoefficients(previous, elevation, water, recoveryLength);
        float after = a * before + b;
        return (ushort)Math.Clamp((int)MathF.Round(baseline * (0.45f + after * 0.9f) + before * condensed * 2400), 0, 5000);
    }

    private static ushort[] BuildOrographicMoisture(WorldData world, GenerationArea area,
        Func<int, int, short> elevation, Func<int, int, WaterType> water)
    {
        var result = new ushort[area.Count];
        int top = area.WorldRow(0), rows = area.Count / area.Chunks.Width;
        Parallel.For(top, top + rows, row =>
        {
            short[] heights = ArrayPool<short>.Shared.Rent(world.Width);
            WaterType[] surface = ArrayPool<WaterType>.Shared.Rent(world.Width);
            ushort[] humidity = ArrayPool<ushort>.Shared.Rent(world.Width);
            try
            {
                for (int x = 0; x < world.Width; x++) { heights[x] = elevation(x, row); surface[x] = water(x, row); }
                TransportMoistureRow(heights, surface, humidity, world.Width, world.Manifest.HorizontalWrap,
                    ClimateWind(row, world.Height), Math.Max(8, world.Height / 16f));
                for (int x = 0; x < area.Chunks.Width; x++)
                    result[(row - top) * area.Chunks.Width + x] = humidity[area.WorldColumn(x)];
            }
            finally
            {
                ArrayPool<short>.Shared.Return(heights);
                ArrayPool<WaterType>.Shared.Return(surface);
                ArrayPool<ushort>.Shared.Return(humidity);
            }
        });
        return result;
    }
}
