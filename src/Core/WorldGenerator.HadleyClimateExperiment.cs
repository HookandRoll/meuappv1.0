using System;
using System.Linq;
using System.Threading.Tasks;

namespace Mapmaker.Core;

internal sealed record HadleyClimateExperimentResult(WorldData World, HadleyWindField Wind, HadleyMoistureResult Moisture,
    double RawGlobalMeanRain, double GlobalRainScale, float? ReferenceGlobalMeanRain);

public static partial class WorldGenerator
{
    // Diagnostic entry point for v63 comparisons; generator v64 invokes the same solver from normal creation.
    internal static HadleyClimateExperimentResult CreateHadleyClimateExperiment(WorldManifest manifest,
        Action<GenerationProgress>? progress = null, float? referenceGlobalMeanRain = null, double? fixedRainScale = null)
    {
        WorldData.Validate(manifest);
        if (manifest.GeneratorVersion != LegacyAnnualVersion || manifest.Generation.Stage != GenerationStage.Terrain
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
        progress?.Invoke(new(GenerationStep.Climate, 0));
        var water = world.Water; var elevation = world.Elevation;
        if (!area.IsInitialGeneration)
        {
            water = (WaterType[])water.Clone(); elevation = (short[])elevation.Clone();
            VisitChunks(area.Chunks, (i, col, row) =>
            {
                int cell = area.WorldRow(row) * world.Width + area.WorldColumn(col);
                water[cell] = area.Water[i]; elevation[cell] = area.Elevation[i];
            }, null, GenerationStep.Climate, 0, 0);
        }
        var regions = GeographicRegions.ClassifyClimateSurface(world, water, elevation, cacheGeography: area.IsInitialGeneration);
        var temperature = BuildAnnualTemperature(world, water, elevation, regions, seed);
        var climateWorld = world.ClimateView(water, elevation, temperature);
        progress?.Invoke(new(GenerationStep.Climate, 10));
        var (wind, moisture) = HadleyCoupledClimateSimulation.Simulate(climateWorld, HadleySettingsFor(world),
            progress: percent => progress?.Invoke(new(GenerationStep.Climate, 25 + percent * 65 / 100)));
        var rowTotals = new double[world.Height];
        var hexRain = world.Manifest.GeneratorVersion >= HexRainSpreadVersion ? new float[world.Count] : null;
        Parallel.For(0, world.Height, row =>
        {
            var (left, right) = world.Domain.IsAtlas ? world.Domain.RowBounds(row) : (0, world.Width);
            float y = (row + 0.5f) / world.Height * wind.GridHeight - 0.5f;
            double sum = 0;
            for (int col = left; col < right; col++)
            {
                float x = (col - left + 0.5f) / (right - left) * wind.GridWidth - 0.5f;
                float sampled = HadleyWindSimulation.SampleGrid(moisture.AnnualRain, wind.GridWidth, wind.GridHeight, wind.WrapHorizontal, x, y);
                sum += sampled;
                if (hexRain is not null) hexRain[row * world.Width + col] = sampled;
            }
            rowTotals[row] = sum;
        });
        double rawMean = rowTotals.Sum() / world.ActiveCount;
        double rainScale = fixedRainScale ?? (referenceGlobalMeanRain.HasValue ? referenceGlobalMeanRain.Value / Math.Max(rawMean, 1e-9) : 1);
        if (hexRain is not null)
            Parallel.For(0, world.Height, row =>
            {
                var (left, right) = world.Domain.RowBounds(row);
                for (int col = left; col < right; col++)
                {
                    int cell = row * world.Width + col;
                    hexRain[cell] = (float)Math.Min(5000, hexRain[cell] * rainScale);
                }
            });
        Parallel.For(0, area.Bounds.Height, localRow =>
        {
            int row = area.WorldRow(localRow);
            var (left, right) = world.Domain.IsAtlas ? world.Domain.RowBounds(row) : (0, world.Width);
            float y = (row + 0.5f) / world.Height * wind.GridHeight - 0.5f;
            for (int localCol = 0; localCol < area.Bounds.Width; localCol++)
            {
                int col = area.WorldColumn(localCol), cell = row * world.Width + col;
                int local = localRow * area.Bounds.Width + localCol;
                if (!world.Domain.Contains(col, row)) continue;
                float x = (col - left + 0.5f) / (right - left) * wind.GridWidth - 0.5f;
                float rain = hexRain is null
                    ? HadleyWindSimulation.SampleGrid(moisture.AnnualRain, wind.GridWidth, wind.GridHeight, wind.WrapHorizontal, x, y)
                    : SpreadHexRain(world, hexRain, cell);
                rain = (float)Math.Min(5000, hexRain is null ? rain * rainScale : rain)
                    * (1 + world.Manifest.Generation.PrecipitationOffsetPercent / 100f);
                area.Temperature[local] = area.Writes(col, row, cell, HexLocks.Temperature) ? temperature[cell] : world.Temperature[cell];
                area.Rainfall[local] = area.Writes(col, row, cell, HexLocks.Rainfall)
                    ? (ushort)Math.Clamp(MathF.Round(rain), 0, ushort.MaxValue) : world.Rainfall[cell];
            }
        });
        world.ClimateWind = wind;
        world.ClimateHumidity = moisture.Coupled!.RelativeHumidity;
        progress?.Invoke(new(GenerationStep.Climate, 100));
        return new(world, wind, moisture, rawMean, rainScale, referenceGlobalMeanRain);
    }

    internal static HadleyWindSettings HadleySettingsFor(WorldData world)
    {
        var settings = world.Width >= 2048
        ? new HadleyWindSettings(MaxGridWidth: 512, MaxGridHeight: 256, UseZonalPressureBands: false)
        : world.Width >= 512 ? new HadleyWindSettings(MaxGridWidth: 256, MaxGridHeight: 128, UseZonalPressureBands: false)
        : new HadleyWindSettings(UseZonalPressureBands: false);
        return UsesThreeCellCirculation(world.Manifest)
            ? settings with { UseZonalPressureBands = true, Cells = new AtmosphericCellSettings() } : settings;
    }
}
