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
    private async void RunHadleyReplenishmentComparison(bool orographic = false)
    {
        string output = orographic ? "results/hadley-orographic-comparison" : "results/hadley-replenishment-comparison";
        string baselinePath = orographic ? "results/hadley-replenishment-comparison/summary.json"
            : "results/hadley-coupled-climate-comparison/summary.json";
        string label = orographic ? "OROGRAPHIC" : "REPLENISH";
        var trials = new List<object>();
        try
        {
            Directory.CreateDirectory(output);
            using var document = JsonDocument.Parse(File.ReadAllText(baselinePath));
            var baseline = document.RootElement;
            Require(baseline.GetProperty("completed").GetBoolean()
                && baseline.GetProperty("generatorVersion").GetInt32() == WorldGenerator.Version,
                "A referência anterior precisa estar completa e usar a mesma versão.");
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            foreach (int seed in new[] { 1234, -77 })
            {
                var entry = baseline.GetProperty("trials").EnumerateArray().Single(t => t.GetProperty("Seed").GetInt32() == seed);
                var previous = orographic ? JsonSerializer.SerializeToElement(new
                {
                    Seed = seed, Width = entry.GetProperty("Width"), Height = entry.GetProperty("Height"),
                    ActiveCount = entry.GetProperty("ActiveCount"), Hadley = entry.GetProperty("Current"),
                    GlobalIntensityScale = entry.GetProperty("FixedRainScale"), RawGlobalMeanRain = entry.GetProperty("RawGlobalMeanRain"),
                    InitialWater = entry.GetProperty("InitialWater"), Evaporation = entry.GetProperty("EvaporatedWater"),
                    Precipitation = entry.GetProperty("PrecipitatedWater"), FinalWater = entry.GetProperty("FinalWater"),
                    HadleyHashes = entry.GetProperty("CurrentHashes"), CoupledDiagnostics = entry.GetProperty("Diagnostics")
                }) : entry;
                double scale = previous.GetProperty("GlobalIntensityScale").GetDouble();
                var size = MapDomain.ExpandedSize(4096, 2048);
                var manifest = TestManifest(size.Width, size.Height, TerrainAlgorithm.ContinentalSculpt) with
                { Seed = seed, GeneratorVersion = WorldGenerator.Version };
                manifest = manifest with { Generation = manifest.Generation with
                {
                    Extent = MapExtent.World, ReferenceWidth = 4096, ReferenceHeight = 2048,
                    LandPercent = WorldCreationDialog.DefaultLandPercent, CoastDetail = WorldCreationDialog.DefaultCoastDetail
                } };
                var timer = Stopwatch.StartNew(); int lastStep = -1, lastBucket = -1;
                GD.Print($"{label} seed={seed} starting; fixed rain scale={scale:R}; previous rain/evaporation from {baselinePath}");
                var result = await Task.Run(() => WorldGenerator.CreateHadleyClimateExperiment(manifest, p =>
                {
                    int bucket = p.Percent / 10;
                    if ((int)p.Step == lastStep && bucket == lastBucket) return;
                    lastStep = (int)p.Step; lastBucket = bucket;
                    GD.Print($"{label} seed={seed} {p.Step} {p.Percent}% {timer.Elapsed.TotalSeconds:F2}s");
                }, fixedRainScale: scale));
                double seconds = timer.Elapsed.TotalSeconds;
                var world = result.World; var water = result.Moisture; var wind = result.Wind;
                Require(world.Width == previous.GetProperty("Width").GetInt32()
                    && world.Height == previous.GetProperty("Height").GetInt32()
                    && world.ActiveCount == previous.GetProperty("ActiveCount").GetInt32(), "Dimensões diferentes da referência.");
                Require(result.GlobalRainScale == scale && !wind.Settings.UseZonalPressureBands,
                    "O fator fixo ou o forçamento sem cinturões foi alterado.");
                var hashes = await Task.Run(() => HashWorld(world));
                string[] controls = { "Elevation", "Water", "Relief", "Landforms", "Temperature", "Tectonics", "RegionIds", "Regions" };
                foreach (string layer in controls)
                    Require(hashes[layer] == previous.GetProperty("HadleyHashes").GetProperty(layer).GetString(),
                        "A comparação alterou a variável de controle: " + layer);
                double airError = Math.Abs(wind.LowerMass.Sum(v => (double)v) + wind.UpperMass.Sum(v => (double)v)
                    - 2d * wind.LowerMass.Length) / (2d * wind.LowerMass.Length);
                Require(airError < 1e-5 && wind.LowerMass.All(v => float.IsFinite(v) && v > 0)
                    && wind.UpperMass.All(v => float.IsFinite(v) && v > 0), "Balanço de massa de ar inválido.");
                Require(water.Coupled is not null && water.RelativeBudgetError < 1e-5
                    && water.Coupled.TemperatureLimitApplications == 0, "Instabilidade no clima acoplado.");
                var stats = await Task.Run(() => MeasureClimate(world));
                await Capture(world, wind, seed);
                var d = water.Coupled!;
                trials.Add(new
                {
                    Seed = seed, world.Width, world.Height, world.ActiveCount, Manifest = manifest,
                    Previous = previous.Clone(), Current = stats, Seconds = seconds,
                    result.RawGlobalMeanRain, FixedRainScale = scale,
                    water.InitialWater, water.EvaporatedWater, water.PrecipitatedWater,
                    water.FinalWater, water.RelativeBudgetError, AirMassRelativeError = airError,
                    Diagnostics = new
                    {
                        d.SoilEvaporation, d.Runoff, d.AtmosphericWater, d.CloudWater, d.SoilWater,
                        d.TemperatureChangeRms, d.TemperatureLimitApplications,
                        d.LatitudeBands, d.CoastDistanceBands, d.Settings
                    },
                    CurrentHashes = hashes, CurrentWindHashes = HashWind(wind), ControlledLayers = controls
                });
                Save(false);
                GD.Print($"{label} seed={seed} PASSED: raw mean {previous.GetProperty("RawGlobalMeanRain").GetDouble():F2}->{result.RawGlobalMeanRain:F2}; land mean {previous.GetProperty("Hadley").GetProperty("LandMeanRain").GetDouble():F2}->{stats.LandMeanRain:F2}; deserts={stats.DesertLandPercent:F2}%; generated={seconds:F2}s; water error={water.RelativeBudgetError:E3}");
            }
            Save(true);
            GD.Print($"HADLEY {label} COMPARISON PASSED: two maximum atlases, archived coupled baseline, eight identical controls, fixed conversion factor, water and air conserved.");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "failure.txt"), error.ToString());
            GetTree().Quit(1);
        }

        void Save(bool completed) => File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new
        {
            completed, generatorVersion = WorldGenerator.Version, baselinePath,
            setup = (orographic ? "Advected lower dry static temperature T+Gamma*z versus archived instantaneous uphill cooling. Same 90/95/85/80% moisture targets and 2x replenishment. "
                : "Current orographic model with 90/95/85/80% targets and 2x aquatic replenishment versus archived coupled baseline (instantaneous cooling, 80% water, 75% soil, 1x). ")
                + "Same seeds and eight terrain/annual-temperature control hashes. Previous per-seed rain conversion factor frozen before running; no renormalization to the old global mean. Common 5000 cap retained.",
            moisture = new HadleyMoistureSettings(), trials,
            peakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64
        }, new JsonSerializerOptions { WriteIndented = true }));

        async Task Capture(WorldData world, HadleyWindField wind, int seed)
        {
            SetLayer(0);
            var buffers = await Task.Run(() => GlobalDataTextures.BuildBuffers(world));
            InstallWorld(world, buffers); RefreshInterface(); FitMap(); SelectHex(NoHex);
            _hadleyWind = wind; _hadleyWorld = world; _hadleyOverlay.Field = wind; _hadleyOverlay.Domain = world.Domain;
            foreach (var (layer, name) in new[] { (0, "terrain"), (3, "rain"), (4, "biomes"),
                (HadleyLayer, "surface-wind"), (HadleyUpperLayer, "upper-wind") })
            {
                SetLayer(layer); await NextDraw(); await NextDraw();
                using var picture = GetViewport().GetTexture().GetImage();
                Require(picture.SavePng(Path.Combine(output, $"seed-{seed}-{name}.png")) == Error.Ok,
                    "Falha ao capturar a comparação de reposição.");
            }
        }
    }
}
