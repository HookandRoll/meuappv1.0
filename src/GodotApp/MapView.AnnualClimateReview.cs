using System;
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
    private async void RunAnnualClimateReview()
    {
        try
        {
            const int referenceWidth = 512, referenceHeight = 256;
            var (width, height, _) = MapDomain.ExpandedSize(referenceWidth, referenceHeight);
            var manifest = TestManifest(width, height, TerrainAlgorithm.ContinentalSculpt) with
                { GeneratorVersion = WorldGenerator.Version, WorldName = "Clima anual · Seed 1234" };
            manifest = manifest with { Generation = manifest.Generation with
            {
                Extent = MapExtent.World, ReferenceWidth = referenceWidth, ReferenceHeight = referenceHeight,
                LandPercent = WorldCreationDialog.DefaultLandPercent, CoastDetail = WorldCreationDialog.DefaultCoastDetail
            } };
            string output = ProjectSettings.GlobalizePath($"res://results/clima-v{manifest.GeneratorVersion}");
            Directory.CreateDirectory(output);
            WorldData? baseline = null;
            int[] land = Array.Empty<int>();
            foreach (int temperature in new[] { 0, 5, -5 })
            {
                WorldData? sameTemperature = null;
                foreach (int precipitation in new[] { 0, 10, -10 })
                {
                    string id = $"t{temperature}-p{precipitation}";
                    var scenario = manifest with { Generation = manifest.Generation with
                    {
                        TemperatureOffsetC = temperature, PrecipitationOffsetPercent = precipitation
                    } };
                    var timer = Stopwatch.StartNew();
                    var world = await Task.Run(() => WorldGenerator.Create(scenario));
                    double seconds = timer.Elapsed.TotalSeconds;
                    if (baseline is null)
                    {
                        baseline = world;
                        land = Enumerable.Range(0, world.Count).Where(i =>
                            world.Domain.CountsTowardLandQuota(i) && world.Water[i] == WaterType.None).ToArray();
                    }
                    sameTemperature ??= world;
                    Require(baseline.Water.SequenceEqual(world.Water) && baseline.Elevation.SequenceEqual(world.Elevation),
                        $"O cenário {id} alterou a geografia.");
                    for (int i = 0; i < world.Count; i++)
                    {
                        if (!world.Domain.Contains(i)) continue;
                        Require(world.Temperature[i] - baseline.Temperature[i] == temperature * 10 &&
                            Math.Abs(world.Rainfall[i] - sameTemperature.Rainfall[i] * (1 + precipitation / 100.0)) <= 1.1,
                            $"O cenário {id} aplicou um ajuste inesperado no hex {i}.");
                    }

                    InstallWorld(world); RefreshInterface(); FitMap();
                    foreach (var (layer, name) in new[] { (2, "temperatura"), (3, "chuva"), (4, "biomas") })
                    {
                        SetLayer(layer); _layerSelect.Select(layer);
                        await NextDraw(); await NextDraw();
                        using var screenshot = GetViewport().GetTexture().GetImage();
                        Rect2 canvas = MapCanvasRect();
                        using var crop = screenshot.GetRegion(new Rect2I((Vector2I)canvas.Position, (Vector2I)canvas.Size));
                        Require(crop.SavePng(Path.Combine(output, $"{name}-{id}.png")) == Error.Ok, "Falha ao salvar a camada.");
                    }
                    var rainfall = land.Select(i => world.Rainfall[i]).OrderBy(value => value).ToArray();
                    var metrics = new
                    {
                        id, temperature, precipitation, world.Width, world.Height, world.ActiveCount,
                        landCells = land.Length, seconds,
                        meanTemperature = land.Average(i => world.Temperature[i] / 10.0),
                        minTemperature = land.Min(i => world.Temperature[i]) / 10.0,
                        maxTemperature = land.Max(i => world.Temperature[i]) / 10.0,
                        meanRain = rainfall.Average(value => value),
                        medianRain = rainfall[rainfall.Length / 2],
                        p90Rain = rainfall[(int)Math.Ceiling(rainfall.Length * 0.90) - 1],
                        changedBiome = land.Count(i => world.Biomes[i] != baseline.Biomes[i]),
                        rainScaleClipped = land.Count(i => world.Rainfall[i] >= 3000),
                        biomes = land.GroupBy(i => world.Biomes[i]).ToDictionary(group => group.Key.ToString(), group => group.Count()),
                        transitions = land.Where(i => world.Biomes[i] != baseline.Biomes[i])
                            .GroupBy(i => $"{baseline.Biomes[i]} -> {world.Biomes[i]}").ToDictionary(group => group.Key, group => group.Count())
                    };
                    File.WriteAllText(Path.Combine(output, $"{id}.json"), JsonSerializer.Serialize(metrics,
                        new JsonSerializerOptions { WriteIndented = true }));
                    GD.Print($"CLIMATE {id} PASSED: {seconds:F2}s, {metrics.meanTemperature:F2} °C, {metrics.meanRain:F1} mm, {metrics.changedBiome} biomes changed");
                }
            }
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
