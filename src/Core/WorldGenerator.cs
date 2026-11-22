using System;
using System.Threading.Tasks;

namespace Mapmaker.Core;

public enum GenerationStep { Land, Elevation, Climate, Biomes, Rivers }

public readonly record struct GenerationProgress(GenerationStep Step, int Percent);

public static partial class WorldGenerator
{
    public const int Version = 63;
    internal const short SculptedStraitDepth = -137;
    private const int HistogramSize = 2048;
    // Two forest growth passes read up to two hexes away each. Cleanup reads one more.
    private const int ForestMargin = 5;
    private const int MinimumBeachSize = 3;

    public static WorldData Create(WorldManifest manifest, Action<GenerationProgress>? progress = null)
    {
        WorldData.Validate(manifest);
        if (manifest.Generation.PolarOceanRows > 0) return CreatePolarWorld(manifest, progress);
        var world = new WorldData(manifest);
        if (manifest.Generation.Stage == GenerationStage.Blank)
            return world;

        var area = new GenerationArea(world, new WorldRegion(0, 0, world.Width, world.Height), useWorldArrays: true);
        Generate(world, area, progress);
        if (manifest.GeneratorVersion >= 18 && manifest.Generation.Algorithm == TerrainAlgorithm.ContinentalSculpt && manifest.Generation.Stage == GenerationStage.Terrain)
        {
            GenerateRivers(world, progress);
            ApplyFluvialBiomes(world, new WorldRegion(0, 0, world.Width, world.Height));
        }
        return world;
    }

    /// <summary>Restores the generated terrain in an offset-coordinate rectangle using the world's seed and settings.</summary>
    public static void RegenerateRegion(WorldData world, WorldRegion region, Action<GenerationProgress>? progress = null)
        => RegenerateRegion(world, region, RegenerationMode.Everything, progress: progress);

    public static void RegenerateRegion(WorldData world, WorldRegion region, RegenerationMode mode,
        HexLocks field = HexLocks.None, Action<GenerationProgress>? progress = null)
    {
        HexLocks fields = HexLocks.Water | HexLocks.Elevation | HexLocks.Relief |
            HexLocks.Temperature | HexLocks.Rainfall | HexLocks.Biome | HexLocks.Rivers | HexLocks.Landform | HexLocks.Features;
        if (!Enum.IsDefined(mode) || (field & ~fields) != 0 || (field != 0 && ((int)field & ((int)field - 1)) != 0))
            throw new ArgumentException("Modo ou campo de regeneração inválido.");
        if (field != HexLocks.None)
            fields = field;
        else
        {
            if (world.Manifest.GeneratorVersion < 41) fields &= ~HexLocks.Landform;
            if (world.Manifest.GeneratorVersion < 46) fields &= ~HexLocks.Features;
            if (mode >= RegenerationMode.KeepWater)
                fields &= ~HexLocks.Water;
            if (mode == RegenerationMode.KeepWaterAndElevation)
                fields &= ~(HexLocks.Elevation | HexLocks.Relief | HexLocks.Landform);
        }
        if (field == HexLocks.Landform)
        {
            TerrainClassifier.ReclassifyRegion(world, region, updateRelief: false);
            return;
        }
        var regenerated = RegenerateRegion(world, region, fields, progress);
        world.GeographyDirty = true;
        if ((fields & HexLocks.Rivers) != 0 && world.Manifest.GeneratorVersion >= 18 &&
            world.Manifest.Generation.Stage == GenerationStage.Terrain && world.Manifest.Generation.Algorithm == TerrainAlgorithm.ContinentalSculpt)
            RegenerateRivers(world, region);
        if ((fields & HexLocks.Biome) != 0 && world.Manifest.GeneratorVersion >= 18)
        {
            Func<int, BiomeType>? baseBiome = null;
            // Evaluate contextual habitats from the same base biomes on both sides of the selection.
            if (world.Manifest.GeneratorVersion >= 56 && regenerated is not null)
                baseBiome = cell =>
                {
                    int local = regenerated.Neighbor(HexGrid.FromOffset(cell % world.Width, cell / world.Width), 0, 0);
                    return local >= 0 && !regenerated.Locked(cell, HexLocks.Biome)
                        ? regenerated.Biomes[local] : world.Biomes[cell];
                };
            ApplyFluvialBiomes(world, region, (fields & HexLocks.Features) != 0, baseBiome);
        }
        else if ((fields & HexLocks.Features) != 0 && world.Manifest.GeneratorVersion >= 46)
            ApplyContextBiomes(world, region, updateFeatures: true, updateBiomes: false);
    }

    private static GenerationArea? RegenerateRegion(WorldData world, WorldRegion region, HexLocks fields,
        Action<GenerationProgress>? progress)
    {
        if (region.Width <= 0 || region.Height <= 0 || region.Column < 0 || region.Row < 0 ||
            region.Column > world.Width - region.Width || region.Row > world.Height - region.Height)
            throw new ArgumentOutOfRangeException(nameof(region));
        if (world.Manifest.Generation.Stage == GenerationStage.Blank)
            return null;
        if (world.Manifest.Generation.PolarOceanRows > 0)
        {
            RegeneratePolarRegion(world, region, fields, progress);
            return null;
        }

        int margin = world.Manifest.Generation.Stage == GenerationStage.Terrain
            ? ForestMargin + Math.Max(1, world.Height / 96) : 1;
        if (world.Manifest.Generation.Stage == GenerationStage.Terrain && RefinesBiomeEdges(world.Manifest))
            margin++;
        if (world.Manifest.Generation.Stage == GenerationStage.Terrain && FinishesBiomeRibbons(world.Manifest))
            margin += 2;
        if (world.Manifest.Generation.Stage == GenerationStage.Terrain && world.Manifest.GeneratorVersion >= 15)
            margin += MinimumBeachSize - 1;
        if (world.Manifest.Generation.Stage == GenerationStage.Terrain && world.Manifest.GeneratorVersion >= 31)
            margin += 2 * SmallBiomeLimit(world.Height);
        if (world.Manifest.GeneratorVersion >= 41)
            margin = Math.Max(margin, TerrainClassifier.RegionalRadius(world.Manifest));
        if (world.Manifest.GeneratorVersion >= 44) margin += Math.Max(1, world.Height / 128);
        if (world.Manifest.GeneratorVersion >= 45)
        {
            int majorityRadius = Math.Clamp(world.Height / 96, 3, 24);
            margin += majorityRadius + Math.Max(2, (majorityRadius + 1) / 2) + 1;
        }
        var area = new GenerationArea(world, ExpandRegion(world, region, margin),
            selection: region, fields: fields);
        Generate(world, area, progress);
        area.CopyRegionTo(world, region);
        return area;
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
        bool variedTerrain = manifest.GeneratorVersion >= 14;
        float? reliefDensity = variedTerrain ? Math.Clamp((settings.LandPercent - 38) / 27f, 0f, 1f) : null;
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
        WaterType[]? water = null;
        if (sculpt)
        {
            TectonicReference? tectonics = manifest.GeneratorVersion >= 18
                ? world.Tectonics ?? TectonicReference.Create(manifest) : null;
            if (world.Tectonics is null) world.Tectonics = tectonics;
            (scores, ridges, seaLevel, water) = new ContinentalSculpture(manifest, tectonics).Build(progress);
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
            col = area.WorldColumn(col);
            row = area.WorldRow(row);
            WaterType type = water is not null ? water[row * world.Width + col]
                : ScoreAt(col, row) < seaLevel ? WaterType.Ocean : WaterType.None;
            int cell = row * world.Width + col;
            area.Water[i] = area.Writes(col, row, cell, HexLocks.Water) ? type : world.Water[cell];
            area.Elevation[i] = area.Writes(col, row, cell, HexLocks.Elevation)
                ? (area.Water[i] == WaterType.None ? (short)0 : (short)-1) : world.Elevation[cell];
        }, progress, GenerationStep.Land, scores is null ? 0 : manifest.GeneratorVersion >= 20 ? 99 : 85, settings.Stage == GenerationStage.Land ? 95 : 100);

        if (settings.Stage == GenerationStage.Land)
        {
            RebuildCoast(area, progress, GenerationStep.Land, 95);
            return;
        }

        bool terrain = settings.Stage == GenerationStage.Terrain;
        bool[] openPlains = terrain ? new bool[area.Count] : Array.Empty<bool>();
        short[]? gentleElevation = manifest.GeneratorVersion >= 30
            ? BuildGentleElevation(world.Width, world.Height, manifest.HorizontalWrap,
                cell => water?[cell] ?? (ScoreAt(cell % world.Width, cell / world.Width) < seaLevel ? WaterType.Ocean : WaterType.None),
                cell =>
                {
                    int x = cell % world.Width, y = cell / world.Width;
                    float altitude = ScoreAt(x, y) - seaLevel;
                    WaterType type = water?[cell] ?? (altitude < 0 ? WaterType.Ocean : WaterType.None);
                    return type != WaterType.None ? (short)Math.Min(-1, Math.Clamp(altitude * 2500, -6000f, 0f))
                        : manifest.GeneratorVersion >= 33 && sculpt
                            ? PlateLandElevation((float)x / world.Width, (float)y / world.Height, aspect, altitude, seed, ridges?[cell] ?? 0, manifest.HorizontalWrap).Elevation
                            : LandElevation((float)x / world.Width, (float)y / world.Height, aspect, altitude,
                            seed, ridges?[cell], reliefDensity).Elevation;
                }, manifest.GeneratorVersion >= 33 && sculpt ? ridges : null) : null;
        if (manifest.GeneratorVersion >= 43 && sculpt && gentleElevation is not null && ridges is not null)
            CalibrateTectonicSummits(gentleElevation, ridges, world.Domain, seed);
        var polarIce = PolarIceField.Enabled(manifest) ? new PolarIceField(manifest) : null;
        if (polarIce is not null && gentleElevation is not null && water is not null)
            for (int i = 0; i < world.Count; i++) if (water[i] == WaterType.None && polarIce.IsPolar(i)) gentleElevation[i] = polarIce.Elevation(i);
        if (manifest.GeneratorVersion >= 46 && gentleElevation is not null && water is not null && ridges is not null)
            for (int i = 0; i < world.Count; i++)
                if (water[i] == WaterType.None && ridges[i] < -0.3f && !(polarIce?.IsPolar(i) ?? false))
                    gentleElevation[i] = Math.Min(gentleElevation[i], (short)450);
        byte[]? volcanoes = null;
        if (manifest.GeneratorVersion >= 46 && gentleElevation is not null && water is not null)
        {
            var featureHeights = gentleElevation; var featureWater = water;
            if (!area.IsInitialGeneration)
            {
                featureHeights = (short[])gentleElevation.Clone(); featureWater = (WaterType[])water.Clone();
                for (int i = 0; i < world.Count; i++)
                {
                    int x = i % world.Width, y = i / world.Width;
                    if (!area.Writes(x, y, i, HexLocks.Elevation)) featureHeights[i] = world.Elevation[i];
                    if (!area.Writes(x, y, i, HexLocks.Water)) featureWater[i] = world.Water[i];
                }
            }
            volcanoes = BuildVolcanoes(manifest, featureHeights, featureWater, world.Domain);
        }
        progress?.Invoke(new(GenerationStep.Elevation, 0));
        VisitChunks(area.Chunks, (i, col, row) =>
        {
            col = area.WorldColumn(col);
            row = area.WorldRow(row);
            float altitude = ScoreAt(col, row) - seaLevel;
            float plain = 0f;
            short elevation;
            if (area.Water[i] != WaterType.None)
                elevation = sculpt && float.IsNegativeInfinity(scores![row * world.Width + col])
                    ? SculptedStraitDepth : (short)Math.Min(-1, Math.Clamp(altitude * 2500, -6000f, 0f));
            else
            {
                float? ridge = ridges?[row * world.Width + col];
                (elevation, plain) = manifest.GeneratorVersion >= 33 && sculpt
                    ? PlateLandElevation((float)col / world.Width, (float)row / world.Height, aspect, altitude, seed, ridge ?? 0, manifest.HorizontalWrap)
                    : LandElevation((float)col / world.Width, (float)row / world.Height, aspect, altitude,
                    seed, ridge, reliefDensity, manifest.GeneratorVersion == 27);
                if (manifest.GeneratorVersion is >= 24 and < 26 && sculpt) elevation = NarrowRidgeElevation(elevation, ridge ?? 0, manifest.GeneratorVersion);
            }

            int cell = row * world.Width + col;
            if (gentleElevation is not null) elevation = gentleElevation[cell];
            if (manifest.GeneratorVersion >= 33 && sculpt && area.Water[i] == WaterType.None && (ridges?[cell] ?? 0) < -0.3f)
                elevation = (short)Math.Min(elevation, (short)450);
            if (polarIce is not null && area.Water[i] == WaterType.None && polarIce.IsPolar(cell)) elevation = polarIce.Elevation(cell);
            area.Elevation[i] = area.Writes(col, row, cell, HexLocks.Elevation) ? elevation : world.Elevation[cell];
            area.Features[i] = world.Features[cell];
            if (volcanoes is not null && area.Writes(col, row, cell, HexLocks.Features))
                area.Features[i] = (area.Features[i] & ~GeographicFeatures.Volcano) | (volcanoes[cell] == 2 && area.Water[i] == WaterType.None && area.Elevation[i] > 2000 ? GeographicFeatures.Volcano : 0);
            area.Relief[i] = !area.Writes(col, row, cell, HexLocks.Relief) ? world.Relief[cell] : area.Elevation[i] switch
            {
                >= 3200 => ReliefType.Peak, >= 1850 => ReliefType.Mountains,
                >= 650 => ReliefType.Hills, >= 220 => ReliefType.Rolling, _ => ReliefType.Flat
            };
            if (terrain)
                openPlains[i] = manifest.GeneratorVersion < 30 && plain >= 0.73f && area.Relief[i] == ReliefType.Flat;
        }, progress, GenerationStep.Elevation, 0, 90);
        if (manifest.GeneratorVersion >= 41)
        {
            var samples = new TerrainClassifier.Samples(world, area.Bounds, area.Elevation, area.Water);
            VisitChunks(area.Chunks, (i, col, row) =>
            {
                col = area.WorldColumn(col); row = area.WorldRow(row);
                int cell = row * world.Width + col;
                var classification = TerrainClassifier.Classify(samples, cell);
                area.Relief[i] = area.Writes(col, row, cell, HexLocks.Relief) ? classification.Relief : world.Relief[cell];
                area.Landforms[i] = area.Writes(col, row, cell, HexLocks.Landform) ? classification.Landform : world.Landforms[cell];
            }, progress, GenerationStep.Elevation, 90, 95);
        }
        RebuildCoast(area, progress, GenerationStep.Elevation, manifest.GeneratorVersion >= 41 ? 95 : 90);
        if (!terrain)
            return;

        if (manifest.GeneratorVersion >= 55)
            ApplyAnnualClimate(world, area, seed, progress);
        else
            ApplyLegacyClimate();

        void ApplyLegacyClimate()
        {
            int climateRadius = Math.Max(1, world.Height / 24);
            float MaritimeScoreAt(int col, int row) => water?[row * world.Width + col] == WaterType.Lake
                ? seaLevel + 0.28f : ScoreAt(col, row);
            float EditedMaritimeScore(int col, int row)
            {
                int cell = row * world.Width + col;
                float score = water is null ? ScoreAt(col, row) : MaritimeScoreAt(col, row);
                if (area.Writes(col, row, cell, HexLocks.Water))
                    return score;
                WaterType generated = water is null ? (score < seaLevel ? WaterType.Ocean : WaterType.None) : water[cell];
                WaterType current = world.Water[cell];
                if (current == generated)
                    return score;
                return current == WaterType.Ocean ? Math.Min(score, seaLevel - 0.22f) : Math.Max(score, seaLevel + 0.28f);
            }
            Func<int, int, float> maritimeScore = EditedMaritimeScore;
            short ClimateElevation(int x, int y)
            {
                int cell = y * world.Width + x;
                if (manifest.GeneratorVersion >= 25)
                {
                    int local = area.Neighbor(HexGrid.FromOffset(x, y), 0, 0);
                    // Atmospheric uplift starts at the water surface, not the seabed.
                    return local >= 0 ? (area.Water[local] == WaterType.None ? area.Elevation[local] : (short)0)
                        : (world.Water[cell] == WaterType.None ? world.Elevation[cell] : (short)0);
                }
                if (!area.Writes(x, y, cell, HexLocks.Elevation)) return world.Elevation[cell];
                if (water?[cell] != WaterType.None) return 0;
                short sample = LandElevation((float)x / world.Width, (float)y / world.Height, aspect, ScoreAt(x, y) - seaLevel,
                    seed, ridges?[cell], reliefDensity, manifest.GeneratorVersion == 27).Elevation;
                return manifest.GeneratorVersion is >= 24 and < 26 && sculpt ? NarrowRidgeElevation(sample, ridges?[cell] ?? 0, manifest.GeneratorVersion) : sample;
            }
            WaterType ClimateWater(int x, int y) => area.Writes(x, y, y * world.Width + x, HexLocks.Water)
                ? water?[y * world.Width + x] ?? WaterType.None : world.Water[y * world.Width + x];
            progress?.Invoke(new(GenerationStep.Climate, 0));
            var regionalRain = manifest.GeneratorVersion >= 30
                ? BuildRegionalRain(world, area, ClimateElevation, ClimateWater, seed) : null;
            ushort[]? moisture = manifest.GeneratorVersion is >= 25 and < 30 && sculpt
                ? BuildOrographicMoisture(world, area, ClimateElevation, ClimateWater) : null;
            VisitChunks(area.Chunks, (i, col, row) =>
            {
                col = area.WorldColumn(col);
                row = area.WorldRow(row);
                float maritime = area.Water[i] == WaterType.Ocean ? 1f
                    : SeaExposure(maritimeScore, seaLevel, world.Width, world.Height, col, row, climateRadius, sculpt && manifest.HorizontalWrap);
                if (regionalRain is not null) maritime = regionalRain.Moisture[i] / 65535f;
                var (temperature, rainfall) = Climate((float)col / world.Width, (float)row / world.Height,
                    aspect, area.Elevation[i], maritime, seed, manifest.GeneratorVersion, manifest.HorizontalWrap);
                if (regionalRain is not null) rainfall = regionalRain.Rain[i];
                else if (moisture is not null)
                {
                    int wind = ClimateWind(row, world.Height);
                    int upwind = col - wind;
                    if (manifest.HorizontalWrap) upwind = (upwind + world.Width) % world.Width;
                    else upwind = Math.Clamp(upwind, 0, world.Width - 1);
                    rainfall = ContinuousOrographicRain(rainfall, moisture[i] / 65535f, ClimateElevation(upwind, row), area.Elevation[i],
                        area.Water[i], Math.Max(8, world.Height / 16f));
                }
                else if (manifest.GeneratorVersion >= 18 && sculpt)
                    rainfall = OrographicRain(col, row, world.Width, world.Height, manifest.HorizontalWrap, rainfall, ClimateElevation, ClimateWater, manifest.GeneratorVersion >= 20);
                int cell = row * world.Width + col;
                area.Temperature[i] = area.Writes(col, row, cell, HexLocks.Temperature) ? temperature : world.Temperature[cell];
                area.Rainfall[i] = area.Writes(col, row, cell, HexLocks.Rainfall) ? rainfall : world.Rainfall[cell];
            }, progress, GenerationStep.Climate, 0, 100);
        }

        progress?.Invoke(new(GenerationStep.Biomes, 0));
        int inlandSteps = Math.Max(1, world.Height / 96);
        bool refineBiomeEdges = RefinesBiomeEdges(manifest);
        bool[]? beachCandidates = manifest.GeneratorVersion >= 15 ? new bool[area.Count] : null;
        VisitChunks(area.Chunks, (i, col, row) =>
        {
            if (area.Water[i] != WaterType.None)
                return;
            short elevation = area.Elevation[i];
            short temperature = area.Temperature[i];
            ushort rainfall = area.Rainfall[i];
            openPlains[i] &= temperature > 45 && rainfall < (temperature >= 225 ? 1450 : 1700);
            if ((area.Flags[i] & WorldData.CoastFlag) != 0 && area.Elevation[i] <= 200 &&
                IsSandyCoast(area, i, col, row, inlandSteps, seed))
            {
                if (beachCandidates is null)
                {
                    area.Biomes[i] = BiomeType.Beach;
                    return;
                }
                beachCandidates[i] = true;
            }
            if (refineBiomeEdges)
                (elevation, temperature, rainfall) = BiomeClimateAt(area, i, col, row);
            if (manifest.GeneratorVersion >= 30)
            {
                float x = (area.WorldColumn(col) + (area.WorldRow(row) & 1) * 0.5f) / world.Height;
                float y = area.WorldRow(row) / (float)world.Height;
                rainfall = (ushort)Math.Max(0, rainfall + (int)((TerrainNoise(x, y, 4.0f, aspect,
                    manifest.HorizontalWrap, seed ^ 0xB82F1E49u) - 0.5f) * 90f));
            }
            else if (variedTerrain)
            {
                float x = (area.WorldColumn(col) + (area.WorldRow(row) & 1) * 0.5f) / world.Height;
                float y = area.WorldRow(row) / (float)world.Height;
                bool wrap = manifest.HorizontalWrap;
                float bend = TerrainNoise(x, y, 13f, aspect, wrap, seed ^ 0xB82F1E49u) - 0.5f;
                float detail = TerrainNoise(x, y, 43f, aspect, wrap, seed ^ 0x6DD15B57u) - 0.5f;
                float thermal = TerrainNoise(x, y, 21f, aspect, wrap, seed ^ 0xA24BAED5u) - 0.5f;
                rainfall = (ushort)Math.Max(0, rainfall + (int)(bend * 440f + detail * 190f));
                temperature += (short)(thermal * 44f + detail * 12f);
            }
            else if (manifest.GeneratorVersion >= 5)
            {
                // Bend climate thresholds locally, while keeping the underlying climate values intact.
                float x = area.WorldColumn(col) * 32f / world.Height;
                float y = area.WorldRow(row) * 32f / world.Height;
                float variation = Noise(x, y, seed ^ 0xB82F1E49u) + 0.35f * Noise(x * 2.1f, y * 2.1f, seed ^ 0x6DD15B57u) - 0.675f;
                rainfall = (ushort)Math.Max(0, rainfall + (int)(variation * 220f));
            }
            area.Biomes[i] = manifest.GeneratorVersion >= 46
                ? LimitColdDesert(ChooseExpandedBiome(area.Elevation[i], temperature, rainfall, area.WorldColumn(col),
                    area.WorldRow(row), world.Width, world.Height, manifest.HorizontalWrap, seed, !openPlains[i]),
                    area.Temperature[i], manifest.GeneratorVersion)
                : ChooseBiome(elevation, temperature, rainfall, forests: !openPlains[i]);
        }, progress, GenerationStep.Biomes, 0, beachCandidates is null ? 65 : 59);
        if (beachCandidates is not null)
            ApplyBeachMinimum(area, beachCandidates, progress);
        bool finishRibbons = FinishesBiomeRibbons(manifest);
        RefineLandBiomes(area, openPlains, progress, finishRibbons ? 97 : 100, manifest.GeneratorVersion, seed);
        if (finishRibbons)
            FinishBiomeRibbons(area, openPlains, manifest.GeneratorVersion >= 31 ? null : progress);
        if (manifest.GeneratorVersion >= 44) RoughenBiomeBorders(area, seed, manifest.GeneratorVersion);
        if (manifest.GeneratorVersion >= 45) PropagateBiomeMajority(area, openPlains);
        if (manifest.GeneratorVersion >= 31)
            ConsolidateSmallBiomes(area, openPlains, progress);
        if (manifest.GeneratorVersion >= 46)
            VisitChunks(area.Chunks, (i, col, row) =>
            {
                int x = area.WorldColumn(col), y = area.WorldRow(row), cell = y * world.Width + x;
                if (area.Water[i] != WaterType.None) return;
                if (polarIce is not null && polarIce.IsPolar(cell)) area.Biomes[i] = BiomeType.Glacier;
                else if (volcanoes is not null && volcanoes[cell] != 0) area.Biomes[i] = BiomeType.Volcanic;
                else area.Biomes[i] = LimitColdDesert(area.Biomes[i], area.Temperature[i], manifest.GeneratorVersion);
            }, null, GenerationStep.Biomes, 0, 100);
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
            if (area.Water[i] != WaterType.None)
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

    internal static float TerrainNoise(float x, float y, float frequency, float aspect, bool wrap, uint seed, bool angular = false)
    {
        if (!wrap)
            return Noise(x * frequency, y * frequency, seed, angular);
        x -= MathF.Floor(x / aspect) * aspect;
        return Interpolate(Noise(x * frequency, y * frequency, seed, angular),
            Noise((x - aspect) * frequency, y * frequency, seed, angular), Smooth(x / aspect));
    }

    private static short OrganicRidgeElevation(short generated, float ridge)
    {
        // Keep the v19 profile inside real ridges. Background continental domes
        // may form highlands but must not independently become alpine blobs.
        float influence = Smooth(Math.Clamp(ridge / 0.25f, 0, 1));
        float highland = generated <= 1800 ? generated : 1800 + 300 * (1 - MathF.Exp(-(generated - 1800) / 700f));
        return (short)Interpolate(highland, generated, influence);
    }

    private static short NarrowRidgeElevation(short generated, float ridge, int version)
    {
        // Broad uplift remains as hills and highland. Only the narrow collision
        // spine can support alpine elevation and peaks.
        float spine = Smooth(Math.Clamp((ridge - 0.12f) / 0.72f, 0, 1));
        float shoulder = generated <= 1000 ? generated
            : 1000 + 700 * (1 - MathF.Exp(-(generated - 1000) / 1100f));
        // A smooth monotonic compression keeps the surrounding drainage gradients;
        // a hard 1750 m cap would turn entire slopes into identical flat terraces.
        if (version >= 25)
            shoulder = generated <= 1200 ? generated : 1200 + 900 * (1 - MathF.Exp(-(generated - 1200) / 1400f));
        float elevation = Interpolate(shoulder, generated + (version >= 25 ? 650 : 950) * spine * spine * spine,
            version >= 25 ? spine : spine * spine);
        return (short)Math.Min(6800, elevation);
    }

    private static (short Elevation, float Plain) LandElevation(float x, float y, float aspect, float altitude, uint seed,
        float? sculptedRidge, float? reliefDensity, bool feathered = false)
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
            // The feathered field supplies the shoulders; reserve strong uplift for
            // its core instead of raising every collision to the same high crest.
            float uplift = feathered ? 1450f * mountain * mountain : reliefDensity.HasValue ? 1800f * mountain * MathF.Sqrt(mountain) : 2550f * mountain;
            rugged = Math.Min(7200f, rugged + uplift * Smooth(Math.Clamp(altitude / 0.27f, 0f, 1f)));
        }
        if (reliefDensity is float density)
        {
            rugged *= 0.57f;
            // A lower sea level exposes more of the high interior. Compress the broad
            // slopes more than the crests so dense maps still have spaced-out ranges.
            rugged -= 630f * density * Smooth(Math.Clamp(rugged / 1350f, 0f, 1f)) * (1f - 0.55f * ridge);
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


    private static void RefineLandBiomes(GenerationArea area, bool[] openPlains, Action<GenerationProgress>? progress, int end, int version = 0, uint seed = 0)
    {
        var grown = new BiomeType[area.Count];
        GrowForest(area, openPlains, area.Biomes, grown, progress, 65, 79, version, seed);
        GrowForest(area, openPlains, grown, area.Biomes, progress, 79, 93, version, seed);
        Array.Copy(area.Biomes, grown, area.Count);
        VisitChunks(area.Chunks, (i, col, row) =>
        {
            if (Biomes.IsForest(grown[i]) && ForestNeighbors(area, grown, area.WorldHex(col, row)) == 0)
                area.Biomes[i] = version >= 46 ? ChooseExpandedBiome(area.Elevation[i], area.Temperature[i], area.Rainfall[i],
                    area.WorldColumn(col), area.WorldRow(row), area.WorldWidth, area.WorldHeight, area.Wrap, seed, false)
                    : ChooseBiome(area.Elevation[i], area.Temperature[i], area.Rainfall[i], forests: false);
        }, progress, GenerationStep.Biomes, 93, end);
    }

    private static void GrowForest(GenerationArea area, bool[] openPlains, BiomeType[] source, BiomeType[] target,
        Action<GenerationProgress>? progress, int from, int to, int version = 0, uint seed = 0)
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

            target[i] = version >= 46 ? ChooseExpandedBiome(area.Elevation[i], temperature, rain,
                area.WorldColumn(col), area.WorldRow(row), area.WorldWidth, area.WorldHeight, area.Wrap, seed)
                : temperature <= 45 ? BiomeType.Taiga
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

    private static void ApplyBeachMinimum(GenerationArea area, bool[] candidates, Action<GenerationProgress>? progress)
    {
        int width = area.Chunks.Width;
        VisitChunks(area.Chunks, (i, col, row) =>
        {
            if (!candidates[i])
                return;

            // A component reaches the minimum within at most two edges. Keep the search
            // local so regional regeneration gives the same result across chunk borders.
            Span<int> members = stackalloc int[MinimumBeachSize];
            members[0] = i;
            int count = 1;
            for (int head = 0; head < count; head++)
            {
                int cell = members[head];
                Hex hex = head == 0 ? area.WorldHex(col, row) : area.WorldHex(cell % width, cell / width);
                for (int direction = 0; direction < 6; direction++)
                {
                    int neighbor = area.Neighbor(hex, direction);
                    if (neighbor < 0 || !candidates[neighbor] || members[..count].Contains(neighbor))
                        continue;
                    members[count++] = neighbor;
                    if (count == MinimumBeachSize)
                    {
                        area.Biomes[i] = BiomeType.Beach;
                        return;
                    }
                }
            }
        }, progress, GenerationStep.Biomes, 59, 65);
    }

    private static (short Temperature, ushort Rainfall) Climate(
        float x, float y, float aspect, short elevation, float maritime, uint seed, int version = 0, bool wrap = false)
    {
        if (version >= 43) return ModernClimate(x, y, aspect, elevation, maritime, seed, wrap);
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

    private static float Noise(float x, float y, uint seed, bool angular = false)
    {
        int left = (int)MathF.Floor(x);
        int top = (int)MathF.Floor(y);
        float u = angular ? x - left : Smooth(x - left);
        float v = angular ? y - top : Smooth(y - top);
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
