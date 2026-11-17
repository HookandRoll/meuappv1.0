using System;
using System.Threading.Tasks;

namespace Mapmaker.Core;

public static class WorldGenerator
{
    public const int Version = 4;
    private const int HistogramSize = 2048;

    public static WorldData Create(WorldManifest manifest)
    {
        var world = new WorldData(manifest);
        GenerationSettings settings = manifest.Generation;
        if (settings.Stage == GenerationStage.Blank)
            return world;
        if (manifest.GeneratorVersion != Version)
            throw new ArgumentException($"Versão do gerador não suportada: {manifest.GeneratorVersion}.", nameof(manifest));

        var scores = new float[world.Count];
        float aspect = world.Width / (float)world.Height;
        LandLobe[] lobes = PlaceLandmasses(settings.Continents, manifest.Seed, aspect);
        uint seed = unchecked((uint)manifest.Seed);
        float coast = settings.CoastDetail * 0.01f;
        bool fractal = settings.Algorithm == TerrainAlgorithm.Fractal;

        Parallel.For(0, world.Height, row =>
        {
            for (int col = 0; col < world.Width; col++)
            {
                float x = (col + (row & 1) * 0.5f) / world.Width;
                float y = (float)row / world.Height;
                scores[row * world.Width + col] = LandScore(x, y, aspect, lobes, seed, coast, fractal);
            }
        });

        float seaLevel = FindSeaLevel(scores, settings.LandPercent);
        int climateRadius = Math.Max(1, world.Height / 24);
        bool[] openPlains = settings.Stage == GenerationStage.Terrain ? new bool[world.Count] : Array.Empty<bool>();
        Parallel.For(0, world.Height, row =>
        {
            for (int col = 0; col < world.Width; col++)
            {
                int i = row * world.Width + col;
                float altitude = scores[i] - seaLevel;
                bool ocean = altitude < 0f;
                world.Water[i] = ocean ? WaterType.Ocean : WaterType.None;
                if (settings.Stage == GenerationStage.Land)
                {
                    world.Elevation[i] = ocean ? (short)-1 : (short)0;
                    continue;
                }

                float x = (float)col / world.Width;
                float y = (float)row / world.Height;
                float plain = 0f;
                short elevation;
                if (ocean)
                {
                    elevation = (short)Math.Min(-1, Math.Clamp(altitude * 2500, -6000f, 0f));
                }
                else
                {
                    (elevation, plain) = LandElevation(x, y, aspect, altitude, seed);
                }
                world.Elevation[i] = elevation;
                world.Relief[i] = elevation switch
                {
                    >= 3200 => ReliefType.Peak, >= 1850 => ReliefType.Mountains,
                    >= 650 => ReliefType.Hills, >= 220 => ReliefType.Rolling, _ => ReliefType.Flat
                };
                if (settings.Stage != GenerationStage.Terrain)
                    continue;

                float maritime = ocean ? 1f : SeaExposure(scores, seaLevel, world.Width, world.Height, col, row, climateRadius);
                var (temperature, rainfall) = Climate(x, y, aspect, elevation, maritime, seed);
                world.Temperature[i] = temperature;
                world.Rainfall[i] = rainfall;
                if (!ocean)
                {
                    openPlains[i] = plain >= 0.73f && world.Relief[i] == ReliefType.Flat &&
                        temperature > 45 && rainfall < (temperature >= 225 ? 1450 : 1700);
                    world.Biomes[i] = ChooseBiome(elevation, temperature, rainfall, forests: !openPlains[i]);
                }
            }
        });
        world.RebuildCoast();
        if (settings.Stage == GenerationStage.Terrain)
            RefineLandBiomes(world, openPlains, seed);
        return world;
    }

    private static (short Elevation, float Plain) LandElevation(float x, float y, float aspect, float altitude, uint seed)
    {
        float ridge = 1f - Math.Abs(2f * Noise(x * 22f, y * 11f, seed ^ 0xC56F1D4Bu) - 1f);
        float rugged = Math.Clamp(altitude * (1100f + 3300f * ridge * ridge), 0, 7200);
        float regionX = x * aspect;
        uint plainSeed = seed ^ 0x5DC9A4F3u;
        float region = 0.81f * Noise(regionX * 4.3f, y * 4.3f, plainSeed)
            + 0.19f * Noise(regionX * 10f, y * 10f, plainSeed + 11u);
        float plain = Smooth(Math.Clamp((region - 0.37f) / 0.27f, 0f, 1f))
            * (1f - Smooth(Math.Clamp((altitude - 0.45f) / 0.43f, 0f, 1f)));
        float lowland = (90f + 55f * Noise(regionX * 3.1f, y * 3.1f, plainSeed + 29u))
            * Smooth(Math.Clamp(altitude / 0.20f, 0f, 1f));
        return ((short)Interpolate(rugged, Math.Min(rugged, lowland), plain), plain);
    }

    private static BiomeType ChooseBiome(short elevation, short temperature, ushort rainfall, bool forests = true) => (elevation, temperature, rainfall) switch
    {
        (>= 2300, <= 35, _) => BiomeType.Alpine,
        (_, <= -55, _) => BiomeType.Tundra,
        (_, <= 45, >= 400) when forests => BiomeType.Taiga,
        (_, >= 110, < 260) => BiomeType.Desert,
        (_, _, < 480) => BiomeType.Steppe,
        (_, >= 225, >= 1550) when forests => BiomeType.Rainforest,
        (_, >= 225, < 1050) => BiomeType.Savanna,
        (_, _, >= 1000) when forests => BiomeType.TemperateForest,
        _ => BiomeType.Grassland
    };

    private static void RefineLandBiomes(WorldData world, bool[] openPlains, uint seed)
    {
        int width = world.Width;
        int height = world.Height;
        int inlandSteps = Math.Max(1, height / 96);
        bool wrap = world.Manifest.HorizontalWrap;
        Parallel.For(0, height, row =>
        {
            for (int column = 0; column < width; column++)
            {
                int i = row * width + column;
                if ((world.Flags[i] & WorldData.CoastFlag) != 0 && world.Water[i] == WaterType.None &&
                    world.Elevation[i] <= 200 && IsSandyCoast(world, HexGrid.FromOffset(column, row), i, column, row, inlandSteps, seed))
                    world.Biomes[i] = BiomeType.Beach;
            }
        });

        var grown = new BiomeType[world.Count];
        GrowForest(world, openPlains, world.Biomes, grown);
        GrowForest(world, openPlains, grown, world.Biomes);
        Array.Copy(world.Biomes, grown, world.Count);
        Parallel.For(0, height, row =>
        {
            for (int column = 0; column < width; column++)
            {
                int i = row * width + column;
                if (Biomes.IsForest(grown[i]) && ForestNeighbors(grown, HexGrid.FromOffset(column, row), width, height, wrap) == 0)
                    world.Biomes[i] = ChooseBiome(world.Elevation[i], world.Temperature[i], world.Rainfall[i], forests: false);
            }
        });
    }

    private static void GrowForest(WorldData world, bool[] openPlains, BiomeType[] source, BiomeType[] target)
    {
        int width = world.Width;
        int height = world.Height;
        bool wrap = world.Manifest.HorizontalWrap;
        Parallel.For(0, height, row =>
        {
            for (int column = 0; column < width; column++)
            {
                int i = row * width + column;
                BiomeType type = source[i];
                target[i] = type;
                if (openPlains[i] || !CanExtendForest(world, i, type))
                    continue;

                short temperature = world.Temperature[i];
                ushort rain = world.Rainfall[i];
                Hex hex = HexGrid.FromOffset(column, row);
                int mask = ForestNeighbors(source, hex, width, height, wrap);
                // Opposite neighbors can close a gap. Three surrounding neighbors can join a concave edge.
                if ((mask & (mask >> 3)) == 0 && System.Numerics.BitOperations.PopCount((uint)mask) < 3 &&
                    !BridgesForestGap(world, openPlains, source, hex, mask, width, height, wrap))
                    continue;

                target[i] = temperature <= 45 ? BiomeType.Taiga
                    : temperature >= 225 && rain >= 1360 ? BiomeType.Rainforest : BiomeType.TemperateForest;
            }
        });
    }

    private static bool CanExtendForest(WorldData world, int i, BiomeType type)
    {
        short temperature = world.Temperature[i];
        return (type is BiomeType.Grassland or BiomeType.Savanna) &&
            world.Water[i] == WaterType.None && temperature >= -30 && world.Elevation[i] <= 2850 &&
            world.Rainfall[i] >= (temperature <= 45 ? 360 : 710);
    }

    private static bool BridgesForestGap(WorldData world, bool[] openPlains, BiomeType[] source, Hex hex,
        int mask, int width, int height, bool wrap)
    {
        for (int direction = 0; direction < 6; direction++)
        {
            if ((mask & (1 << direction)) != 0)
                continue;
            int neighbor = HexGrid.NeighborIndex(hex, direction, width, height, wrap);
            if (neighbor < 0 || openPlains[neighbor] || !CanExtendForest(world, neighbor, source[neighbor]))
                continue;
            int farther = HexGrid.NeighborIndex(hex, direction, width, height, wrap, steps: 2);
            if (farther >= 0 && Biomes.IsForest(source[farther]))
                mask |= 1 << direction;
        }
        return (mask & (mask >> 3)) != 0;
    }

    private static int ForestNeighbors(BiomeType[] biomes, Hex hex, int width, int height, bool wrap)
    {
        int mask = 0;
        for (int direction = 0; direction < 6; direction++)
        {
            int neighbor = HexGrid.NeighborIndex(hex, direction, width, height, wrap);
            if (neighbor >= 0 && Biomes.IsForest(biomes[neighbor]))
                mask |= 1 << direction;
        }
        return mask;
    }

    private static bool IsSandyCoast(WorldData world, Hex hex, int i, int column, int row, int steps, uint seed)
    {
        float sediment = Noise(16f * column / world.Height, 16f * row / world.Height, seed ^ 0xA24BAED5u);
        if (sediment < 0.32f || world.Temperature[i] <= -65)
            return false;

        int width = world.Width;
        int height = world.Height;
        bool wrap = world.Manifest.HorizontalWrap;
        for (int direction = 0; direction < 6; direction++)
        {
            int water = HexGrid.NeighborIndex(hex, direction, width, height, wrap);
            if (water < 0 || world.Water[water] != WaterType.Ocean)
                continue;

            int inland = HexGrid.NeighborIndex(hex, (direction + 3) % 6, width, height, wrap, steps);
            int shelf = HexGrid.NeighborIndex(hex, direction, width, height, wrap, steps);
            int rise = inland < 0 ? 0 : Math.Max(0, world.Elevation[inland] - world.Elevation[i]);
            int depth = shelf < 0 ? 0 : Math.Max(0, -(int)world.Elevation[shelf]);
            if (rise + depth * 0.45f < 115f + 320f * sediment)
                return true;
        }
        return false;
    }

    private static (short Temperature, ushort Rainfall) Climate(
        float x, float y, float aspect, short elevation, float maritime, uint seed)
    {
        float latitude = Math.Abs(y - 0.5f) * 2f;
        float latitudeSquared = latitude * latitude;
        // Keep a broad temperate belt while letting the outer latitudes cool more steeply.
        float seaLevelTemperature = 310f - 455f * latitudeSquared * (0.40f + 0.60f * latitudeSquared);
        float temperatureVariation = (Noise(x * aspect * 3.1f, y * 3.1f, seed ^ 0x4CF5AD43u) - 0.5f) * 90f;
        int landElevation = Math.Max(0, (int)elevation);
        short temperature = (short)MathF.Round(Interpolate(seaLevelTemperature, 120f, maritime * 0.19f)
            + temperatureVariation - landElevation * 0.065f);

        float wetEquator = 1f - Smooth(Math.Min(1f, latitude / 0.52f));
        float wetTemperate = Smooth(Math.Max(0, 1f - Math.Abs(latitude - 0.65f) / 0.28f));
        uint rainSeed = seed ^ 0x85EBCA6Bu;
        float moisture = 0.72f * Noise(x * aspect * 4.2f, y * 4.2f, rainSeed)
            + 0.28f * Noise(x * aspect * 12f, y * 12f, rainSeed + 17u);
        ushort rainfall = (ushort)Math.Clamp(355f + 1090f * wetEquator + 620f * wetTemperate
            + 395f * maritime + 1840f * (moisture - 0.5f) - landElevation * 0.09f, 0f, 5000f);

        return (temperature, rainfall);
    }

    private static float SeaExposure(float[] scores, float level, int width, int height, int x, int y, int radius)
    {
        float west = scores[y * width + Math.Max(0, x - radius)];
        float east = scores[y * width + Math.Min(width - 1, x + radius)];
        float north = scores[Math.Max(0, y - radius) * width + x];
        float south = scores[Math.Min(height - 1, y + radius) * width + x];
        float lowest = Math.Min(Math.Min(west, east), Math.Min(north, south));
        return Smooth(Math.Clamp((level - lowest + 0.06f) / 0.28f, 0f, 1f));
    }

    private static float LandScore(float x, float y, float aspect, LandLobe[] lobes, uint seed, float coast, bool fractal)
    {
        float nx = x * aspect;
        float warp = fractal ? 0.13f + 0.16f * coast : 0.08f + 0.12f * coast;
        float detail = (fractal ? 0.040f : 0.019f) + coast * (fractal ? 0.115f : 0.055f);
        // Warp the whole outline at two scales so detail can move shorelines into bays and capes.
        float warpX = ((Noise(nx * 2.9f + 13f, y * 2.9f, seed ^ 0xB5297A4Du) - 0.5f) * warp
            + (Noise(nx * 9.4f + 31f, y * 9.4f, seed ^ 0x1B56C4E9u) - 0.5f) * detail) / aspect;
        float warpY = (Noise(nx * 2.9f, y * 2.9f + 17f, seed ^ 0x68E31DA4u) - 0.5f) * warp
            + (Noise(nx * 9.4f, y * 9.4f + 25f, seed ^ 0x165667B1u) - 0.5f) * detail;
        float shape = -2f;
        foreach (LandLobe lobe in lobes)
        {
            float dx = x + warpX - lobe.X;
            float dy = y + warpY - lobe.Y;
            float along = dx * lobe.AlongX + dy * lobe.AlongY;
            float across = dx * lobe.AcrossX + dy * lobe.AcrossY;
            shape = Math.Max(shape, lobe.Strength - along * along - across * across);
        }

        float variation = fractal
            ? (Fractal((x + warpX) * aspect * 4.6f, (y + warpY) * 4.6f, seed) - 0.5f) * (1f + 1.7f * coast)
            : (Noise(nx * 7f, y * 7f, seed) - 0.5f) * 0.24f
                + (Noise(nx * 24f, y * 24f, seed + 19u) - 0.5f) * (0.08f + 0.48f * coast);
        return shape + variation;
    }

    private readonly record struct LandLobe(float X, float Y, float AlongX, float AlongY,
        float AcrossX, float AcrossY, float Strength);

    private static LandLobe CreateLobe(float x, float y, float rx, float ry, float angle, float strength)
    {
        var (sine, cosine) = MathF.SinCos(angle);
        return new LandLobe(x, y, cosine / rx, sine / ry, -sine / rx, cosine / ry, strength);
    }

    private static LandLobe[] PlaceLandmasses(int count, int seed, float aspect)
    {
        int columns = Math.Clamp((int)MathF.Round(MathF.Sqrt(count * aspect * 0.83f)), 1, count);
        int rows = (count + columns - 1) / columns;
        const int lobesPerContinent = 3;
        var lobes = new LandLobe[count * lobesPerContinent];
        var random = new Random(seed);
        for (int i = 0; i < count; i++)
        {
            int row = i / columns;
            int rowCount = Math.Min(columns, count - row * columns);
            float x = ((i % columns) + 0.32f + random.NextSingle() * 0.36f) / rowCount;
            float y = (row + 0.28f + random.NextSingle() * 0.44f) / rows;
            float rx = (0.30f + random.NextSingle() * 0.13f) / columns;
            float ry = (0.26f + random.NextSingle() * 0.16f) / rows;
            float angle = random.NextSingle() * MathF.Tau;
            var (sine, cosine) = MathF.SinCos(angle);
            int start = i * lobesPerContinent;
            lobes[start] = CreateLobe(x, y, rx, ry, angle, 1.12f);

            float reach = 0.76f + random.NextSingle() * 0.37f;
            float offset = 0.18f + random.NextSingle() * 0.31f;
            lobes[start + 1] = CreateLobe(x + rx * (cosine * reach - sine * offset),
                y + ry * (sine * reach + cosine * offset),
                rx * (0.57f + random.NextSingle() * 0.20f), ry * 0.40f, angle + 0.66f,
                0.84f + random.NextSingle() * 0.15f);
            lobes[start + 2] = CreateLobe(x - rx * (cosine * 0.74f + sine * 0.37f),
                y - ry * (sine * 0.74f - cosine * 0.37f),
                rx * 0.52f, ry * (0.55f + random.NextSingle() * 0.28f), angle - 0.49f,
                0.84f + random.NextSingle() * 0.22f);
        }
        return lobes;
    }

    private static float FindSeaLevel(float[] scores, int landPercent)
    {
        const float min = -4f;
        const float range = 8f;
        Span<int> histogram = stackalloc int[HistogramSize];
        histogram.Clear();
        foreach (float score in scores)
        {
            int bin = Math.Clamp((int)((score - min) * (HistogramSize / range)), 0, HistogramSize - 1);
            histogram[bin]++;
        }
        int remaining = (int)((long)scores.Length * landPercent / 100);
        for (int bin = HistogramSize - 1; bin >= 0; bin--)
        {
            if (histogram[bin] >= remaining)
                return min + (bin + 1f - remaining / (float)histogram[bin]) * (range / HistogramSize);
            remaining -= histogram[bin];
        }
        return min;
    }

    private static float Fractal(float x, float y, uint seed) =>
        Noise(x, y, seed) * 0.57f + Noise(x * 2.1f, y * 2.1f, seed + 11u) * 0.27f
        + Noise(x * 4.5f, y * 4.5f, seed + 23u) * 0.11f + Noise(x * 9.3f, y * 9.3f, seed + 37u) * 0.05f;

    private static float Noise(float x, float y, uint seed)
    {
        int left = (int)MathF.Floor(x);
        int top = (int)MathF.Floor(y);
        float u = Smooth(x - left);
        float v = Smooth(y - top);
        float a = Interpolate(Sample(left, top, seed), Sample(left + 1, top, seed), u);
        float b = Interpolate(Sample(left, top + 1, seed), Sample(left + 1, top + 1, seed), u);
        return Interpolate(a, b, v);
    }

    private static float Smooth(float t) => t * t * (3f - 2f * t);
    private static float Interpolate(float a, float b, float t) => a + (b - a) * t;

    private static float Sample(int x, int y, uint seed)
    {
        uint h = (uint)x * 0x9E3779B9u ^ (uint)y * 0x85EBCA6Bu ^ seed * 0xC2B2AE35u;
        h = (h ^ (h >> 16)) * 0x7FEB352Du;
        h = (h ^ (h >> 15)) * 0x846CA68Bu;
        return (h >> 8) * (1f / 16777216f);
    }
}
