using System;
using System.Linq;
using System.Threading.Tasks;

namespace Mapmaker.Core;

internal sealed record HadleyClimateExperimentResult(WorldData World, HadleyWindField Wind, HadleyMoistureResult Moisture,
    double RawGlobalMeanRain, double GlobalRainScale, float? ReferenceGlobalMeanRain);

public static partial class WorldGenerator
{
    // Only diagnostic commands call this entry point; normal Create/regeneration keep their climate model.
    internal static HadleyClimateExperimentResult CreateHadleyClimateExperiment(WorldManifest manifest,
        Action<GenerationProgress>? progress = null, float? referenceGlobalMeanRain = null, double? fixedRainScale = null)
    {
        WorldData.Validate(manifest);
        if (manifest.GeneratorVersion != Version || manifest.Generation.Stage != GenerationStage.Terrain
            || manifest.Generation.Algorithm != TerrainAlgorithm.ContinentalSculpt || manifest.Generation.PolarOceanRows != 0)
            throw new ArgumentException("O experimento requer terreno completo do gerador atual.");
        if (referenceGlobalMeanRain.HasValue && (!float.IsFinite(referenceGlobalMeanRain.Value) || referenceGlobalMeanRain <= 0))
            throw new ArgumentOutOfRangeException(nameof(referenceGlobalMeanRain));
        if (fixedRainScale.HasValue && (!double.IsFinite(fixedRainScale.Value) || fixedRainScale <= 0
            || referenceGlobalMeanRain.HasValue))
            throw new ArgumentOutOfRangeException(nameof(fixedRainScale));
        var world = new WorldData(manifest);
        var area = new GenerationArea(world, new WorldRegion(0, 0, world.Width, world.Height), useWorldArrays: true);
        HadleyClimateExperimentResult? result = null;
        Generate(world, area, progress, experimentalClimate: value => result = value,
            experimentalRainTarget: referenceGlobalMeanRain, experimentalRainScale: fixedRainScale);
        GenerateRivers(world, progress);
        ApplyFluvialBiomes(world, new WorldRegion(0, 0, world.Width, world.Height));
        return result ?? throw new InvalidOperationException("Clima experimental não foi calculado.");
    }

    private static HadleyClimateExperimentResult ApplyHadleyClimateExperiment(WorldData world, GenerationArea area,
        uint seed, Action<GenerationProgress>? progress, float? referenceGlobalMeanRain, double? fixedRainScale)
    {
        if (!area.IsInitialGeneration) throw new InvalidOperationException("Experimento disponível apenas na geração inicial.");
        progress?.Invoke(new(GenerationStep.Climate, 0));
        var regions = GeographicRegions.ClassifyClimateSurface(world, world.Water, world.Elevation, cacheGeography: true);
        var temperature = BuildAnnualTemperature(world, world.Water, world.Elevation, regions, seed);
        temperature.CopyTo(area.Temperature, 0);
        progress?.Invoke(new(GenerationStep.Climate, 10));
        var settings = world.Width >= 2048 ? new HadleyWindSettings(MaxGridWidth: 512, MaxGridHeight: 256, UseZonalPressureBands: false)
            : world.Width >= 512 ? new HadleyWindSettings(MaxGridWidth: 256, MaxGridHeight: 128, UseZonalPressureBands: false)
            : new HadleyWindSettings(UseZonalPressureBands: false);
        var (wind, moisture) = HadleyCoupledClimateSimulation.Simulate(world, settings,
            progress: percent => progress?.Invoke(new(GenerationStep.Climate, 25 + percent * 65 / 100)));
        var rowTotals = new double[world.Height];
        Parallel.For(0, world.Height, row =>
        {
            var (left, right) = world.Domain.IsAtlas ? world.Domain.RowBounds(row) : (0, world.Width);
            float y = (row + 0.5f) / world.Height * wind.GridHeight - 0.5f;
            double sum = 0;
            for (int col = left; col < right; col++)
            {
                float x = (col - left + 0.5f) / (right - left) * wind.GridWidth - 0.5f;
                sum += HadleyWindSimulation.SampleGrid(moisture.AnnualRain, wind.GridWidth, wind.GridHeight, wind.WrapHorizontal, x, y);
            }
            rowTotals[row] = sum;
        });
        double rawMean = rowTotals.Sum() / world.ActiveCount;
        double rainScale = fixedRainScale ?? (referenceGlobalMeanRain.HasValue ? referenceGlobalMeanRain.Value / Math.Max(rawMean, 1e-9) : 1);
        Parallel.For(0, world.Height, row =>
        {
            var (left, right) = world.Domain.IsAtlas ? world.Domain.RowBounds(row) : (0, world.Width);
            float y = (row + 0.5f) / world.Height * wind.GridHeight - 0.5f;
            for (int col = left; col < right; col++)
            {
                float x = (col - left + 0.5f) / (right - left) * wind.GridWidth - 0.5f;
                float rain = HadleyWindSimulation.SampleGrid(moisture.AnnualRain, wind.GridWidth, wind.GridHeight, wind.WrapHorizontal, x, y);
                rain = (float)Math.Min(5000, rain * rainScale) * (1 + world.Manifest.Generation.PrecipitationOffsetPercent / 100f);
                area.Rainfall[row * world.Width + col] = (ushort)Math.Clamp(MathF.Round(rain), 0, ushort.MaxValue);
            }
        });
        progress?.Invoke(new(GenerationStep.Climate, 100));
        return new(world, wind, moisture, rawMean, rainScale, referenceGlobalMeanRain);
    }
}
