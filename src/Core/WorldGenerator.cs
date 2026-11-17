using System;
using System.Threading.Tasks;

namespace Mapmaker.Core;

public enum GenerationStep { Land, Elevation, Climate, Biomes }

public readonly record struct GenerationProgress(GenerationStep Step, int Percent);

public static partial class WorldGenerator
{
    public const int Version = 12;
    internal const short SculptedStraitDepth = -137;
    private const int HistogramSize = 2048;
    // Two forest growth passes read up to two hexes away each. Cleanup reads one more.
    private const int ForestMargin = 5;

    public static WorldData Create(WorldManifest manifest, Action<GenerationProgress>? progress = null)
    {
        var world = new WorldData(manifest);
        if (manifest.Generation.Stage == GenerationStage.Blank)
            return world;

        var area = new GenerationArea(world, new WorldRegion(0, 0, world.Width, world.Height), useWorldArrays: true);
        Generate(world, area, progress);
        return world;
    }

    /// <summary>Restores the generated terrain in an offset-coordinate rectangle using the world's seed and settings.</summary>
    public static void RegenerateRegion(WorldData world, WorldRegion region, Action<GenerationProgress>? progress = null)
    {
        if (region.Width <= 0 || region.Height <= 0 || region.Column < 0 || region.Row < 0 ||
            region.Column > world.Width - region.Width || region.Row > world.Height - region.Height)
            throw new ArgumentOutOfRangeException(nameof(region));
        if (world.Manifest.Generation.Stage == GenerationStage.Blank)
            return;

        int margin = world.Manifest.Generation.Stage == GenerationStage.Terrain
            ? ForestMargin + Math.Max(1, world.Height / 96) : 1;
        if (world.Manifest.Generation.Stage == GenerationStage.Terrain && RefinesBiomeEdges(world.Manifest))
            margin++;
        if (world.Manifest.Generation.Stage == GenerationStage.Terrain && FinishesBiomeRibbons(world.Manifest))
            margin += 2;
        var area = new GenerationArea(world, ExpandRegion(world, region, margin));
        Generate(world, area, progress);
        area.CopyRegionTo(world, region);
    }

    private static WorldRegion ExpandRegion(WorldData world, WorldRegion region, int margin)
    {
        int top = Math.Max(0, region.Row - margin);
        int bottom = Math.Min(world.Height, region.Row + region.Height + margin);
        if (world.Manifest.HorizontalWrap)
        {
            int width = region.Width + 2 * margin;
            return width >= world.Width
                ? new WorldRegion(0, top, world.Width, bottom - top)
                : new WorldRegion(region.Column - margin, top, width, bottom - top);
        }
        int left = Math.Max(0, region.Column - margin);
        int right = Math.Min(world.Width, region.Column + region.Width + margin);
        return new WorldRegion(left, top, right - left, bottom - top);
    }

    private static void Generate(WorldData world, GenerationArea area, Action<GenerationProgress>? progress)
    {
        WorldManifest manifest = world.Manifest;
        if (manifest.GeneratorVersion is < 4 or > Version)
            throw new ArgumentException($"Versão do gerador não suportada: {manifest.GeneratorVersion}.", nameof(manifest));

        GenerationSettings settings = manifest.Generation;
        float aspect = world.Width / (float)world.Height;
        bool sculpt = settings.Algorithm == TerrainAlgorithm.ContinentalSculpt;
        LandLobe[] lobes = sculpt ? Array.Empty<LandLobe>() : PlaceLandmasses(settings.Continents, manifest.Seed, aspect);
        uint seed = unchecked((uint)manifest.Seed);
        float coast = settings.CoastDetail * 0.01f;
        bool fractal = settings.Algorithm == TerrainAlgorithm.Fractal;
        float seaLevel = world.GeneratedSeaLevel;
        float[]? scores = !sculpt && float.IsNaN(seaLevel) ? new float[world.Count] : null;
        float SampleScore(int col, int row)
        {
            float x = (col + (row & 1) * 0.5f) / world.Width;
            return LandScore(x, (float)row / world.Height, aspect, lobes, seed, coast, fractal);
        }
        float ScoreAt(int col, int row)
        {
            float score = scores is null ? SampleScore(col, row) : scores[row * world.Width + col];
            return float.IsNegativeInfinity(score) ? seaLevel + SculptedStraitDepth / 2500f : score;
        }

        progress?.Invoke(new(GenerationStep.Land, 0));
        float[]? ridges = null;
        if (sculpt)
        {
            (scores, ridges, seaLevel) = new ContinentalSculpture(manifest).Build(progress);
        }
        else if (scores is not null)
        {
            VisitChunks(world.Chunks, (index, col, row) => scores[index] = SampleScore(col, row),
                progress, GenerationStep.Land, 0, 85);
            seaLevel = FindSeaLevel(scores, settings.LandPercent);
            world.GeneratedSeaLevel = seaLevel;
        }
        VisitChunks(area.Chunks, (i, col, row) =>
        {
            bool ocean = ScoreAt(area.WorldColumn(col), area.WorldRow(row)) < seaLevel;
            area.Water[i] = ocean ? WaterType.Ocean : WaterType.None;
            area.Elevation[i] = ocean ? (short)-1 : (short)0;
        }, progress, GenerationStep.Land, scores is null ? 0 : 85, settings.Stage == GenerationStage.Land ? 95 : 100);

        if (settings.Stage == GenerationStage.Land)
        {
            RebuildCoast(area, progress, GenerationStep.Land, 95);
            return;
        }

        bool terrain = settings.Stage == GenerationStage.Terrain;
        bool[] openPlains = terrain ? new bool[area.Count] : Array.Empty<bool>();
        progress?.Invoke(new(GenerationStep.Elevation, 0));
        VisitChunks(area.Chunks, (i, col, row) =>
        {
            col = area.WorldColumn(col);
            row = area.WorldRow(row);
            float altitude = ScoreAt(col, row) - seaLevel;
            float plain = 0f;
            short elevation;
            if (area.Water[i] == WaterType.Ocean)
                elevation = sculpt && float.IsNegativeInfinity(scores![row * world.Width + col])
                    ? SculptedStraitDepth : (short)Math.Min(-1, Math.Clamp(altitude * 2500, -6000f, 0f));
            else
            {
                float? ridge = ridges?[row * world.Width + col];
                (elevation, plain) = LandElevation((float)col / world.Width, (float)row / world.Height, aspect, altitude, seed, ridge);
            }

            area.Elevation[i] = elevation;
            area.Relief[i] = elevation switch
            {
                >= 3200 => ReliefType.Peak, >= 1850 => ReliefType.Mountains,
                >= 650 => ReliefType.Hills, >= 220 => ReliefType.Rolling, _ => ReliefType.Flat
            };
            if (terrain)
                openPlains[i] = plain >= 0.73f && area.Relief[i] == ReliefType.Flat;
        }, progress, GenerationStep.Elevation, 0, 90);
        RebuildCoast(area, progress, GenerationStep.Elevation, 90);
        if (!terrain)
            return;

        int climateRadius = Math.Max(1, world.Height / 24);
        progress?.Invoke(new(GenerationStep.Climate, 0));
        VisitChunks(area.Chunks, (i, col, row) =>
        {
            col = area.WorldColumn(col);
            row = area.WorldRow(row);
            float maritime = area.Water[i] == WaterType.Ocean ? 1f
                : SeaExposure(ScoreAt, seaLevel, world.Width, world.Height, col, row, climateRadius, sculpt && manifest.HorizontalWrap);
            (area.Temperature[i], area.Rainfall[i]) = Climate((float)col / world.Width, (float)row / world.Height,
                aspect, area.Elevation[i], maritime, seed);
        }, progress, GenerationStep.Climate, 0, 100);

        progress?.Invoke(new(GenerationStep.Biomes, 0));
        int inlandSteps = Math.Max(1, world.Height / 96);
        bool refineBiomeEdges = RefinesBiomeEdges(manifest);
        VisitChunks(area.Chunks, (i, col, row) =>
        {
            if (area.Water[i] == WaterType.Ocean)
                return;
            short elevation = area.Elevation[i];
            short temperature = area.Temperature[i];
            ushort rainfall = area.Rainfall[i];
            openPlains[i] &= temperature > 45 && rainfall < (temperature >= 225 ? 1450 : 1700);
            if ((area.Flags[i] & WorldData.CoastFlag) != 0 && area.Elevation[i] <= 200 &&
                IsSandyCoast(area, i, col, row, inlandSteps, seed))
            {
                area.Biomes[i] = BiomeType.Beach;
                return;
            }
            if (refineBiomeEdges)
                (elevation, temperature, rainfall) = BiomeClimateAt(area, i, col, row);
            if (manifest.GeneratorVersion >= 5)
            {
                // Bend climate thresholds locally, while keeping the underlying climate values intact.
                float x = area.WorldColumn(col) * 32f / world.Height;
                float y = area.WorldRow(row) * 32f / world.Height;
                float variation = Noise(x, y, seed ^ 0xB82F1E49u) + 0.35f * Noise(x * 2.1f, y * 2.1f, seed ^ 0x6DD15B57u) - 0.675f;
                rainfall = (ushort)Math.Max(0, rainfall + (int)(variation * 220f));
            }
            area.Biomes[i] = ChooseBiome(elevation, temperature, rainfall, forests: !openPlains[i]);
        }, progress, GenerationStep.Biomes, 0, 65);
        bool finishRibbons = FinishesBiomeRibbons(manifest);
        RefineLandBiomes(area, openPlains, progress, finishRibbons ? 97 : 100);
        if (finishRibbons)
            FinishBiomeRibbons(area, openPlains, progress);
    }

    private static bool RefinesBiomeEdges(WorldManifest manifest) =>
        manifest.GeneratorVersion >= 8 && manifest.Generation.Algorithm == TerrainAlgorithm.ContinentalSculpt;

    private static bool FinishesBiomeRibbons(WorldManifest manifest) =>
        manifest.GeneratorVersion >= 11 && manifest.Generation.Algorithm == TerrainAlgorithm.ContinentalSculpt;

    private static (short Elevation, short Temperature, ushort Rainfall) BiomeClimateAt(GenerationArea area, int i, int col, int row)
    {
        // Use the nearby land climate for classification. Keep the actual relief and climate in the map.
        int elevation = area.Elevation[i] * 3;
        int temperature = area.Temperature[i] * 3;
        int rainfall = area.Rainfall[i] * 3;
        int weight = 3;
        Hex hex = area.WorldHex(col, row);
        for (int direction = 0; direction < 6; direction++)
        {
            int neighbor = area.Neighbor(hex, direction);
            if (neighbor < 0 || area.Water[neighbor] != WaterType.None)
                continue;
            elevation += area.Elevation[neighbor];
            temperature += area.Temperature[neighbor];
            rainfall += area.Rainfall[neighbor];
            weight++;
        }
        return ((short)(elevation / weight), (short)(temperature / weight), (ushort)(rainfall / weight));
    }

    private static void VisitChunks(ChunkGrid chunks, Action<int, int, int> visit,
        Action<GenerationProgress>? progress, GenerationStep step, int from, int to)
    {
        // Report in order from the caller. Parallel workers never touch UI or call progress handlers.
        int batches = progress is null || chunks.Count < 32 ? 1 : Math.Min(20, (chunks.Count + 15) / 16);
        for (int batch = 0; batch < batches; batch++)
        {
            int start = batch * chunks.Count / batches;
            int end = (batch + 1) * chunks.Count / batches;
            Parallel.For(start, end, index =>
            {
                WorldRegion chunk = chunks.GetChunk(index);
                for (int row = chunk.Row; row < chunk.Row + chunk.Height; row++)
                for (int col = chunk.Column; col < chunk.Column + chunk.Width; col++)
                    visit(row * chunks.Width + col, col, row);
            });
            progress?.Invoke(new(step, from + (to - from) * end / chunks.Count));
        }
    }

    private static void RebuildCoast(GenerationArea area, Action<GenerationProgress>? progress, GenerationStep step, int from)
    {
        VisitChunks(area.Chunks, (i, col, row) =>
        {
            if (area.Water[i] == WaterType.Ocean)
                return;
            Hex hex = area.WorldHex(col, row);
            for (int direction = 0; direction < 6; direction++)
            {
                int neighbor = area.Neighbor(hex, direction);
                if (neighbor < 0 || area.Water[neighbor] != WaterType.Ocean)
                    continue;
                area.Flags[i] |= WorldData.CoastFlag;
                break;
            }
        }, progress, step, from, 100);
    }

    private static (short Elevation, float Plain) LandElevation(float x, float y, float aspect, float altitude, uint seed, float? sculptedRidge)
    {
        float ridge = 1f - Math.Abs(2f * Noise(x * 22f, y * 11f, seed ^ 0xC56F1D4Bu) - 1f);
        if (sculptedRidge is float crest)
        {
            ridge = Math.Max(ridge * 0.58f, crest);
            altitude *= 0.62f;
        }
        float rugged = Math.Clamp(altitude * (1100f + 3300f * ridge * ridge), 0, 7200);
        float regionX = x * aspect;
        uint plainSeed = seed ^ 0x5DC9A4F3u;
        float region = 0.81f * Noise(regionX * 4.3f, y * 4.3f, plainSeed)
            + 0.19f * Noise(regionX * 10f, y * 10f, plainSeed + 11u);
        float plain = Smooth(Math.Clamp((region - 0.37f) / 0.27f, 0f, 1f))
            * (1f - Smooth(Math.Clamp((altitude - 0.45f) / 0.43f, 0f, 1f)));
        if (sculptedRidge is float mountain)
        {
            plain *= 1f - mountain;
            rugged = Math.Min(7200f, rugged + 2550f * mountain * Smooth(Math.Clamp(altitude / 0.27f, 0f, 1f)));
        }
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


    private static void RefineLandBiomes(GenerationArea area, bool[] openPlains, Action<GenerationProgress>? progress, int end)
    {
        var grown = new BiomeType[area.Count];
        GrowForest(area, openPlains, area.Biomes, grown, progress, 65, 79);
        GrowForest(area, openPlains, grown, area.Biomes, progress, 79, 93);
        Array.Copy(area.Biomes, grown, area.Count);
        VisitChunks(area.Chunks, (i, col, row) =>
        {
            if (Biomes.IsForest(grown[i]) && ForestNeighbors(area, grown, area.WorldHex(col, row)) == 0)
                area.Biomes[i] = ChooseBiome(area.Elevation[i], area.Temperature[i], area.Rainfall[i], forests: false);
        }, progress, GenerationStep.Biomes, 93, end);
    }

    private static void GrowForest(GenerationArea area, bool[] openPlains, BiomeType[] source, BiomeType[] target,
        Action<GenerationProgress>? progress, int from, int to)
    {
        VisitChunks(area.Chunks, (i, col, row) =>
        {
            BiomeType type = source[i];
            target[i] = type;
            if (openPlains[i] || !CanExtendForest(area, i, type))
                return;

            short temperature = area.Temperature[i];
            ushort rain = area.Rainfall[i];
            Hex hex = area.WorldHex(col, row);
            int mask = ForestNeighbors(area, source, hex);
            // Opposite neighbors can close a gap. Three surrounding neighbors can join a concave edge.
            if ((mask & (mask >> 3)) == 0 && System.Numerics.BitOperations.PopCount((uint)mask) < 3 &&
                !BridgesForestGap(area, openPlains, source, hex, mask))
                return;

            target[i] = temperature <= 45 ? BiomeType.Taiga
                : temperature >= 225 && rain >= 1360 ? BiomeType.Rainforest : BiomeType.TemperateForest;
        }, progress, GenerationStep.Biomes, from, to);
    }

    private static bool CanExtendForest(GenerationArea area, int i, BiomeType type)
    {
        short temperature = area.Temperature[i];
        return (type is BiomeType.Grassland or BiomeType.Savanna) &&
            area.Water[i] == WaterType.None && temperature >= -30 && area.Elevation[i] <= 2850 &&
            area.Rainfall[i] >= (temperature <= 45 ? 360 : 710);
    }

    private static bool BridgesForestGap(GenerationArea area, bool[] openPlains, BiomeType[] source, Hex hex, int mask)
    {
        for (int direction = 0; direction < 6; direction++)
        {
            if ((mask & (1 << direction)) != 0)
                continue;
            int neighbor = area.Neighbor(hex, direction);
            if (neighbor < 0 || openPlains[neighbor] || !CanExtendForest(area, neighbor, source[neighbor]))
                continue;
            int farther = area.Neighbor(hex, direction, steps: 2);
            if (farther >= 0 && Biomes.IsForest(source[farther]))
                mask |= 1 << direction;
        }
        return (mask & (mask >> 3)) != 0;
    }

    private static int ForestNeighbors(GenerationArea area, BiomeType[] biomes, Hex hex)
    {
        int mask = 0;
        for (int direction = 0; direction < 6; direction++)
        {
            int neighbor = area.Neighbor(hex, direction);
            if (neighbor >= 0 && Biomes.IsForest(biomes[neighbor]))
                mask |= 1 << direction;
        }
        return mask;
    }

    private static bool IsSandyCoast(GenerationArea area, int i, int column, int row, int steps, uint seed)
    {
        float sediment = Noise(16f * area.WorldColumn(column) / area.WorldHeight,
            16f * area.WorldRow(row) / area.WorldHeight, seed ^ 0xA24BAED5u);
        if (sediment < 0.32f || area.Temperature[i] <= -65)
            return false;

        Hex hex = area.WorldHex(column, row);
        for (int direction = 0; direction < 6; direction++)
        {
            int water = area.Neighbor(hex, direction);
            if (water < 0 || area.Water[water] != WaterType.Ocean)
                continue;

            int inland = area.Neighbor(hex, (direction + 3) % 6, steps);
            int shelf = area.Neighbor(hex, direction, steps);
            int rise = inland < 0 ? 0 : Math.Max(0, area.Elevation[inland] - area.Elevation[i]);
            int depth = shelf < 0 ? 0 : Math.Max(0, -(int)area.Elevation[shelf]);
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

    private static float SeaExposure(Func<int, int, float> scoreAt, float level, int width, int height, int x, int y, int radius, bool wrap)
    {
        int left = x - radius;
        int right = x + radius;
        float west = scoreAt(wrap ? (left % width + width) % width : Math.Max(0, left), y);
        float east = scoreAt(wrap ? right % width : Math.Min(width - 1, right), y);
        float north = scoreAt(x, Math.Max(0, y - radius));
        float south = scoreAt(x, Math.Min(height - 1, y + radius));
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
