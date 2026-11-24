using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Mapmaker.Core;

namespace Mapmaker.GodotApp;

public partial class MapView
{
    private sealed record ClimateBand(string Latitude, long LandCells, double MeanLandRain, double DesertPercent, double DryPercent);
    private sealed record ClimateStats(long LandCells, long WarmLandCells, double GlobalMeanRain, double LandMeanRain,
        int MedianLandRain, double DesertLandPercent, double WarmLandDesertPercent, double DryLandPercent,
        double ForestLandPercent, double CappedRainPercent, SortedDictionary<string, long> Biomes, ClimateBand[] Bands);
    private sealed record ClimateTrial(int Seed, int Width, int Height, int ActiveCount, double OldSeconds, double ExperimentalSeconds,
        ClimateStats Old, ClimateStats Hadley, double RawGlobalMeanRain, double GlobalIntensityScale,
        double WaterBudgetRelativeError, double InitialWater, double Evaporation, double Precipitation, double FinalWater,
        bool SubtropicalRainPenaltyEnabled, bool ZonalPressureBandsEnabled, double ChangedRainLandPercent,
        double ChangedBiomeLandPercent, double MeanAbsoluteLandRainDifference,
        SortedDictionary<string, string> OldHashes, SortedDictionary<string, string> HadleyHashes,
        object CoupledDiagnostics);

    private async void RunHadleyClimateComparison()
    {
        const string output = "results/hadley-coupled-humid-climate-comparison";
        var trials = new List<ClimateTrial>();
        try
        {
            Directory.CreateDirectory(output);
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            foreach (int seed in new[] { 1234, -77 })
            {
                var size = MapDomain.ExpandedSize(4096, 2048);
                var manifest = TestManifest(size.Width, size.Height, TerrainAlgorithm.ContinentalSculpt) with
                { Seed = seed, GeneratorVersion = WorldGenerator.LegacyAnnualVersion };
                manifest = manifest with { Generation = manifest.Generation with
                {
                    Extent = MapExtent.World, ReferenceWidth = 4096, ReferenceHeight = 2048,
                    LandPercent = WorldCreationDialog.DefaultLandPercent, CoastDetail = WorldCreationDialog.DefaultCoastDetail
                } };
                var timer = Stopwatch.StartNew();
                var oldWorld = await Task.Run(() => WorldGenerator.Create(manifest, Progress("old")));
                double oldSeconds = timer.Elapsed.TotalSeconds;
                GD.Print($"CLIMATE seed={seed} old generated {oldSeconds:F2}s");
                var oldStats = await Task.Run(() => MeasureClimate(oldWorld));
                var oldHashes = await Task.Run(() => HashWorld(oldWorld));
                await Capture(oldWorld, null, seed, "old");
                timer.Restart();
                var experimental = await Task.Run(() => WorldGenerator.CreateHadleyClimateExperiment(manifest,
                    Progress("hadley-no-bands"), (float)oldStats.GlobalMeanRain));
                double experimentalSeconds = timer.Elapsed.TotalSeconds;
                var world = experimental.World;
                Require(!experimental.Wind.Settings.UseZonalPressureBands, "Os cinturões de pressão não foram desligados.");
                var hashes = await Task.Run(() => HashWorld(world));
                foreach (string layer in new[] { "Elevation", "Water", "Relief", "Landforms", "Temperature", "Tectonics", "RegionIds", "Regions" })
                    Require(oldHashes[layer] == hashes[layer], "Comparação mudou uma variável de controle: " + layer);
                Require(oldHashes["Rainfall"] != hashes["Rainfall"], "A comparação não alterou a chuva.");
                var stats = await Task.Run(() => MeasureClimate(world));
                long changedRain = 0, changedBiome = 0; double absoluteDifference = 0;
                for (int i = 0; i < world.Count; i++)
                {
                    if (!world.Domain.Contains(i) || world.Water[i] != WaterType.None) continue;
                    if (world.Rainfall[i] != oldWorld.Rainfall[i]) changedRain++;
                    if (world.Biomes[i] != oldWorld.Biomes[i]) changedBiome++;
                    absoluteDifference += Math.Abs(world.Rainfall[i] - oldWorld.Rainfall[i]);
                }
                await Capture(world, experimental.Wind, seed, "hadley-no-bands");
                var water = experimental.Moisture;
                trials.Add(new(seed, world.Width, world.Height, world.ActiveCount, oldSeconds, experimentalSeconds,
                    oldStats, stats, experimental.RawGlobalMeanRain, experimental.GlobalRainScale,
                    water.RelativeBudgetError, water.InitialWater, water.EvaporatedWater, water.PrecipitatedWater, water.FinalWater,
                    false, experimental.Wind.Settings.UseZonalPressureBands,
                    changedRain * 100d / stats.LandCells, changedBiome * 100d / stats.LandCells,
                    absoluteDifference / stats.LandCells, oldHashes, hashes, new
                    {
                        water.Coupled!.SoilEvaporation, water.Coupled.Runoff, water.Coupled.AtmosphericWater,
                        water.Coupled.SoilWater, water.Coupled.CloudWater, water.Coupled.TemperatureChangeRms,
                        water.Coupled.TemperatureLimitApplications, water.Coupled.LatitudeBands,
                        water.Coupled.CoastDistanceBands, water.Coupled.Settings
                    }));
                Save(completed: false);
                GD.Print($"CLIMATE seed={seed} PASSED: identical terrain/temperature; land rain {oldStats.LandMeanRain:F1}->{stats.LandMeanRain:F1}; deserts {oldStats.DesertLandPercent:F2}->{stats.DesertLandPercent:F2}%; changed biomes={changedBiome * 100d / stats.LandCells:F2}%; raw mean={experimental.RawGlobalMeanRain:F2}, scale={experimental.GlobalRainScale:F2}, water error={water.RelativeBudgetError:E3}");

                Action<GenerationProgress> Progress(string model)
                {
                    int lastStep = -1;
                    return p =>
                    {
                        if ((int)p.Step == lastStep) return;
                        lastStep = (int)p.Step;
                        GD.Print($"CLIMATE seed={seed} {model} {p.Step} {timer.Elapsed.TotalSeconds:F2}s");
                    };
                }
            }
            Save(completed: true);
            GD.Print("HADLEY CLIMATE COMPARISON PASSED: old vs experimental moisture transport, no subtropical rain penalty or three-cell pressure bands, controlled terrain/temperature, two maximum atlases.");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            File.WriteAllText(Path.Combine(output, "failure.txt"), error.ToString());
            GetTree().Quit(1);
        }

        void Save(bool completed)
        {
            File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new
            {
                completed, generatorVersion = WorldGenerator.LegacyAnnualVersion, trials,
                setup = "Coupled two-layer winds, transported heat/vapor/clouds, delayed rain and soil recycling. No subtropical rain penalty or prescribed three-cell pressure bands. Initial annual surface temperature and terrain are controlled; atmospheric temperatures evolve. One global intensity scale targets the old mean before the 5000 mm cap.",
                calibration = "Raw rain is uncalibrated. The global factor changes intensity, not latitude distribution; the cap may lower the final mean. No latitude-specific tuning was applied.",
                moisture = new HadleyMoistureSettings(), peakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64
            }, new JsonSerializerOptions { WriteIndented = true }));
        }

        async Task Capture(WorldData world, HadleyWindField? wind, int seed, string model)
        {
            SetLayer(0);
            var buffers = await Task.Run(() => GlobalDataTextures.BuildBuffers(world));
            InstallWorld(world, buffers); RefreshInterface(); FitMap(); SelectHex(NoHex);
            if (wind is not null)
            {
                _hadleyWind = wind; _hadleyWorld = world; _hadleyOverlay.Field = wind; _hadleyOverlay.Domain = world.Domain;
            }
            foreach (var (layer, name) in new[] { (0, "terrain"), (3, "rain"), (4, "biomes"),
                (wind is null ? WindLayer : HadleyLayer, "wind") })
            {
                SetLayer(layer); await NextDraw(); await NextDraw();
                using var picture = GetViewport().GetTexture().GetImage();
                Require(picture.SavePng(Path.Combine(output, $"seed-{seed}-{model}-{name}.png")) == Error.Ok,
                    "Falha na captura do experimento climático.");
            }
        }
    }

    private static ClimateStats MeasureClimate(WorldData world)
    {
        long active = 0, land = 0, warmLand = 0, desert = 0, warmDesert = 0, dry = 0, forest = 0, capped = 0;
        double allRain = 0, landRain = 0;
        var histogram = new long[65536]; var bandCount = new long[4]; var bandDry = new long[4];
        var bandDesert = new long[4]; var bandRain = new double[4];
        var biomes = new SortedDictionary<string, long>();
        for (int row = 0; row < world.Height; row++)
        {
            double latitude = Math.Abs((row + 0.5) / world.Height * 2 - 1) * 90;
            int band = latitude < 15 ? 0 : latitude < 35 ? 1 : latitude < 60 ? 2 : 3;
            for (int col = 0; col < world.Width; col++)
            {
                int i = row * world.Width + col;
                if (!world.Domain.Contains(i)) continue;
                active++; allRain += world.Rainfall[i];
                if (world.Rainfall[i] >= 5000) capped++;
                if (world.Water[i] != WaterType.None) continue;
                land++; landRain += world.Rainfall[i]; histogram[world.Rainfall[i]]++;
                bandCount[band]++; bandRain[band] += world.Rainfall[i];
                bool hot = world.Temperature[i] >= 50, isDesert = world.Biomes[i] == BiomeType.Desert;
                if (hot) warmLand++;
                if (isDesert) { desert++; bandDesert[band]++; if (hot) warmDesert++; }
                if (world.Rainfall[i] < 260) { dry++; bandDry[band]++; }
                if (Mapmaker.Core.Biomes.IsForest(world.Biomes[i])) forest++;
                string name = world.Biomes[i].ToString(); biomes[name] = biomes.GetValueOrDefault(name) + 1;
            }
        }
        long accumulated = 0; int median = 0;
        for (; median < histogram.Length; median++) { accumulated += histogram[median]; if (accumulated * 2 >= land) break; }
        string[] labels = { "0 a 15 graus", "15 a 35 graus", "35 a 60 graus", "60 a 90 graus" };
        var bands = Enumerable.Range(0, 4).Select(i => new ClimateBand(labels[i], bandCount[i],
            bandRain[i] / Math.Max(1, bandCount[i]), bandDesert[i] * 100d / Math.Max(1, bandCount[i]),
            bandDry[i] * 100d / Math.Max(1, bandCount[i]))).ToArray();
        return new(land, warmLand, allRain / Math.Max(1, active), landRain / Math.Max(1, land), median,
            desert * 100d / Math.Max(1, land), warmDesert * 100d / Math.Max(1, warmLand), dry * 100d / Math.Max(1, land),
            forest * 100d / Math.Max(1, land), capped * 100d / Math.Max(1, active), biomes, bands);
    }
}
