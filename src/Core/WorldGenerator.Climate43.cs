using System;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    private static float ModernSeaTemperature(float latitude) =>
        265f - 545f * (0.65f * latitude * latitude + 0.35f * MathF.Pow(latitude, 1.55f));

    private static (short Temperature, ushort Rainfall) ModernClimate(float x, float y, float aspect,
        short elevation, float maritime, uint seed, bool wrap)
        => (ModernTemperature(x, y, aspect, elevation, maritime, seed, wrap),
            ModernRainBaseline(x, y, aspect, maritime, seed, wrap));

    private static short ModernTemperature(float x, float y, float aspect,
        short elevation, float maritime, uint seed, bool wrap)
    {
        float latitude = Math.Clamp(Math.Abs(y - 0.5f) * 2, 0, 1);
        float sea = ModernSeaTemperature(latitude);
        float oceanTarget = Math.Clamp(sea * 0.82f, -45f, 245f);
        float variation = (TerrainNoise(x * aspect, y, 3.1f, aspect, wrap, seed ^ 0x4CF5AD43u) - 0.5f)
            * 58f * (1 - maritime * 0.55f);
        return (short)MathF.Round(Interpolate(sea, oceanTarget, maritime * 0.22f)
            + variation - Math.Max(0, (int)elevation) * 0.065f);
    }

    private static ushort ModernRainBaseline(float x, float y, float aspect, float maritime, uint seed, bool wrap)
    {
        float latitude = Math.Clamp(Math.Abs(y - 0.5f) * 2, 0, 1);
        float equator = 1 - Smooth(Math.Clamp(latitude / 0.24f, 0, 1));
        float subtropical = Smooth(Math.Max(0, 1 - Math.Abs(latitude - 0.28f) / 0.14f));
        float temperate = Smooth(Math.Max(0, 1 - Math.Abs(latitude - 0.54f) / 0.18f));
        float polar = Smooth(Math.Clamp((latitude - 0.66f) / 0.26f, 0, 1));
        float region = 0.80f * TerrainNoise(x * aspect, y, 2.1f, aspect, wrap, seed ^ 0xD46A89C3u)
            + 0.20f * TerrainNoise(x * aspect, y, 5.7f, aspect, wrap, seed ^ 0x71F28E5Bu);
        float rain = (340 + 1320 * equator - 260 * subtropical + 640 * temperate + 280 * maritime
            + 980 * (region - 0.5f)) * Interpolate(1, 0.32f, polar);
        return (ushort)Math.Clamp(MathF.Round(rain), 45, 4200);
    }

    private static void CalibrateTectonicSummits(short[] elevation, float[] ridges, MapDomain domain, uint seed)
    {
        int maximum = 0;
        for (int i = 0; i < elevation.Length; i++)
            if (domain.Contains(i) && ridges[i] >= 0.15f) maximum = Math.Max(maximum, elevation[i]);
        if (maximum < 1200) return; // A world without a developed range need not have an Everest.
        uint hash = seed ^ 0xA71C39D5u;
        hash = (hash ^ (hash >> 16)) * 0x7FEB352Du;
        hash = (hash ^ (hash >> 15)) * 0x846CA68Bu;
        hash ^= hash >> 16;
        float target = 6000 + hash % 2001;
        for (int i = 0; i < elevation.Length; i++)
        {
            if (elevation[i] <= 800 || ridges[i] < 0.15f || !domain.Contains(i)) continue;
            float relative = Math.Clamp((elevation[i] - 800f) / (maximum - 800f), 0, 1);
            // Stretch only the upper tail; plains stay unchanged and the very
            // highest summits occupy progressively smaller parts of each range.
            float weight = relative * relative * relative * relative;
            elevation[i] = (short)Math.Clamp(MathF.Round(elevation[i] + (target - maximum) * weight), 1, 8000);
        }
    }
}
