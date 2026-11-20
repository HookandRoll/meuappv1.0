using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Mapmaker.Core;

namespace Mapmaker.GodotApp;

public partial class MapView
{
    private async void RunClimateReliefReview()
    {
        string output = "results/climate-relief-v30";
        Directory.CreateDirectory(output);
        try
        {
            var rows = new List<object>();
            foreach (var (width, height, seed) in new[] { (512, 256, 1234), (512, 256, 1), (512, 256, -77), (1024, 512, 1234) })
            {
                WorldData? reference = null;
                foreach (int version in new[] { 29, 30 })
                {
                    var manifest = TestManifest(width, height, TerrainAlgorithm.ContinentalSculpt) with { GeneratorVersion = version, Seed = seed };
                    var world = await Task.Run(() => WorldGenerator.Create(manifest));
                    if (version == 29) reference = world;
                    else
                    {
                        Require(reference!.Water.SequenceEqual(world.Water), "V30 alterou a distribuição de terra/água.");
                        Require(world.Elevation.Max() >= reference.Elevation.Max() * 0.80, "V30 apagou os cumes.");
                    }
                    var slopes = new List<int>();
                    long boundaries = 0, edges = 0;
                    double sumE = 0, sumR = 0, sumEE = 0, sumRR = 0, sumER = 0;
                    int land = 0;
                    for (int i = 0; i < world.Count; i++)
                    {
                        if (world.Water[i] != WaterType.None) continue;
                        double e = world.Elevation[i], r = world.Rainfall[i];
                        land++; sumE += e; sumR += r; sumEE += e * e; sumRR += r * r; sumER += e * r;
                        Hex hex = HexGrid.FromOffset(i % width, i / width);
                        for (int d = 0; d < 3; d++)
                        {
                            int n = HexGrid.NeighborIndex(hex, d, width, height, false);
                            if (n < 0 || world.Water[n] != WaterType.None) continue;
                            slopes.Add(Math.Abs(world.Elevation[i] - world.Elevation[n]));
                            edges++; if (world.Biomes[i] != world.Biomes[n]) boundaries++;
                        }
                    }
                    slopes.Sort();
                    double correlation = (land * sumER - sumE * sumR) /
                        Math.Sqrt(Math.Max(1, (land * sumEE - sumE * sumE) * (land * sumRR - sumR * sumR)));
                    int orphanEnds = WorldGenerator.ReadRivers(world).Count(r => !r.ParentId.HasValue && r.EndType == "Custom");
                    Require(orphanEnds == 0, "V30 gerou rio sem destino.");
                    int p95 = slopes[(int)(slopes.Count * 0.95)], max = slopes[^1];
                    rows.Add(new { width, height, seed, version, rainElevationCorrelation = correlation,
                        slopeP95 = p95, maximumLandStep = max, biomeBoundaryFraction = boundaries / (double)edges,
                        peakCells = world.Relief.Count(r => r == ReliefType.Peak), maxElevation = world.Elevation.Max(), orphanEnds });
                    GD.Print($"CLIMATE {width} seed={seed} v{version}: corr={correlation:F3}, slope95={p95}, maxStep={max}, biomeEdges={boundaries / (double)edges:F4}, peaks={world.Relief.Count(r => r == ReliefType.Peak)}, max={world.Elevation.Max()}");
                    InstallWorld(world); RefreshInterface();
                    foreach (int layer in new[] { 0, 1, 3, 4 })
                    {
                        SetLayer(layer); _layerSelect.Select(layer); await NextDraw(); await NextDraw();
                        using Image image = GetViewport().GetTexture().GetImage();
                        Require(image.SavePng(Path.Combine(output, $"{width}-seed-{seed}-v{version}-layer-{layer}.png")) == Error.Ok, "Captura climática falhou.");
                    }
                    if (version == 30) WorldFile.Save(Path.Combine(output, $"{width}-seed-{seed}-v30.world"), world, null);
                }
            }
            File.WriteAllText(Path.Combine(output, "metrics.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
