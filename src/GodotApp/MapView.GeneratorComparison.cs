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
    private async void RunGeneratorComparison()
    {
        string output = ProjectSettings.GlobalizePath("res://results/generator-comparison-v17");
        Directory.CreateDirectory(output);
        var results = new List<object>();
        var algorithms = new[]
        {
            (TerrainAlgorithm.ContinentalSculpt, "continental-sculpt", "Escultura continental"),
            (TerrainAlgorithm.Fractal, "fractal", "Ruído fractal (fBm)"),
            (TerrainAlgorithm.LandmassCenters, "centros", "Centros de massas de terra")
        };
        try
        {
            foreach (var (algorithm, _, _) in algorithms)
                await Task.Run(() => WorldGenerator.Create(TestManifest(128, 64, algorithm)));

            foreach (var (algorithm, slug, label) in algorithms)
            foreach (int land in new[] { 50, 40, 30 })
            {
                string name = $"{slug}-{land}";
                WorldManifest manifest = TestManifest(1024, 512, algorithm) with
                {
                    WorldName = $"{label} — {land}% de terra"
                };
                manifest = manifest with { Generation = manifest.Generation with { LandPercent = land } };
                var (world, seconds, buffers) = await Task.Run(() =>
                {
                    var timer = Stopwatch.StartNew();
                    WorldData generated = WorldGenerator.Create(manifest);
                    double elapsed = timer.Elapsed.TotalSeconds;
                    return (generated, elapsed, GlobalDataTextures.BuildBuffers(generated));
                });
                InstallWorld(world, buffers);
                RefreshInterface();
                SetLayer(0);
                _layerSelect.Select(0);
                _gridToggle.ButtonPressed = false;
                await NextDraw();
                await NextDraw();

                using Image frame = GetViewport().GetTexture().GetImage();
                Vector2 origin = _map.ToGlobal(_surface.Polygon[0]);
                Vector2 end = _map.ToGlobal(_surface.Polygon[2]);
                var rectangle = new Rect2I((Vector2I)origin.Round(), (Vector2I)(end - origin).Round());
                using Image map = frame.GetRegion(rectangle);
                string imageFile = name + ".png";
                Require(map.SavePng(Path.Combine(output, imageFile)) == Error.Ok, "Falha ao salvar a imagem da comparação.");
                string worldFile = name + ".world";
                await Task.Run(() =>
                {
                    world.Manifest = WorldFile.Save(Path.Combine(output, worldFile), world, map.SavePngToBuffer());
                    WorldData reopened = WorldFile.Load(Path.Combine(output, worldFile));
                    Require(ReadDense(world).SequenceEqual(ReadDense(reopened)), "O mundo da comparação mudou ao reabrir.");
                });
                int landCells = world.Water.Count(water => water == WaterType.None);
                double actual = landCells * 100.0 / world.Count;
                int components = CountLandComponents(world);
                results.Add(new
                {
                    algorithm = label, slug, targetLandPercent = land, actualLandPercent = actual,
                    landCells, generationSeconds = seconds, landComponents = components,
                    imageFile, worldFile
                });
                GD.Print($"{name}: {seconds:F3} s, terra {actual:F4}%, {components} massas.");
            }
            string json = JsonSerializer.Serialize(new
            {
                generatorVersion = WorldGenerator.Version, seed = 1234, width = 1024, height = 512,
                hexDistanceKm = 5, mainContinents = 3, coastDetail = 50, horizontalWrap = false,
                stage = "Terreno completo", timing = "Uma geração por configuração, após aquecimento por algoritmo. Exclui renderização e salvamento.",
                results
            }, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(Path.Combine(output, "metrics.json"), json);
            GD.Print("Comparação concluída em " + output);
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    private static int CountLandComponents(WorldData world)
    {
        var visited = new bool[world.Count];
        var queue = new Queue<int>();
        int count = 0;
        for (int start = 0; start < world.Count; start++)
        {
            if (visited[start] || world.Water[start] != WaterType.None)
                continue;
            count++;
            visited[start] = true;
            queue.Enqueue(start);
            while (queue.TryDequeue(out int cell))
            {
                Hex hex = HexGrid.FromOffset(cell % world.Width, cell / world.Width);
                for (int direction = 0; direction < 6; direction++)
                {
                    int next = HexGrid.NeighborIndex(hex, direction, world.Width, world.Height, world.Manifest.HorizontalWrap);
                    if (next < 0 || visited[next] || world.Water[next] != WaterType.None)
                        continue;
                    visited[next] = true;
                    queue.Enqueue(next);
                }
            }
        }
        return count;
    }
}
