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
    private async void RunMountainZoomReview()
    {
        string output = ProjectSettings.GlobalizePath("res://results/mountain-zoom-half-volcano");
        Directory.CreateDirectory(output);
        try
        {
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            var manifest = TestManifest(512, 256, TerrainAlgorithm.ContinentalSculpt) with { GeneratorVersion = WorldGenerator.Version };
            GD.Print("MOUNTAIN ZOOM: generating representative world...");
            var world = await Task.Run(() => WorldGenerator.Create(manifest));
            var buffers = await Task.Run(() => GlobalDataTextures.BuildBuffers(world));
            InstallWorld(world, buffers); RefreshInterface(); SetLayer(0);
            bool Mountain(int cell) => world.Water[cell] == WaterType.None && world.Relief[cell] >= ReliefType.Mountains &&
                (buffers.Surface[cell * 4 + 3] & MapRenderFormat.ReliefSymbol) != 0;
            int Score(int x, int y)
            {
                int score = 0;
                for (int row = Math.Max(0, y - 9); row < Math.Min(world.Height, y + 10); row++)
                for (int col = Math.Max(0, x - 13); col < Math.Min(world.Width, x + 14); col++)
                    if (Mountain(row * world.Width + col)) score++;
                return score;
            }
            var candidates = Enumerable.Range(0, world.Count).Where(Mountain)
                .Where(i => i % world.Width > 25 && i % world.Width < world.Width - 25 && i / world.Width > 20 && i / world.Width < world.Height - 20);
            int center = candidates.OrderByDescending(i => Score(i % world.Width, i / world.Width)).First();
            var focus = new Vector2I(center % world.Width, center / world.Width);
            GD.Print($"MOUNTAIN ZOOM: focus={focus}, symbols={Score(focus.X, focus.Y)}, volcanoes={_volcanoOverlay.CenterCount}");
            var metrics = new List<object>();
            foreach (float zoom in new[] { 4f, 12f, 17f, 22f, 44f })
            {
                foreach (bool after in new[] { false, true })
                {
                    _material.SetShaderParameter("mountain_zoom_scale", after ? 1f : 0f);
                    ShowDetail(focus, zoom);
                    for (int frame = 0; frame < 8; frame++) await NextDraw();
                    var position = _map.Position;
                    var timer = Stopwatch.StartNew();
                    for (int frame = 0; frame < 120; frame++)
                    {
                        _map.Position = position + new Vector2(Mathf.Sin(frame * 0.05f) * zoom * 2f, 0);
                        await NextDraw();
                    }
                    double fps = 120 / timer.Elapsed.TotalSeconds;
                    _map.Position = position;
                    await NextDraw(); await NextDraw();
                    using var image = GetViewport().GetTexture().GetImage();
                    string name = $"mountains-zoom-{zoom}-{(after ? "after" : "before")}.png";
                    Require(image.SavePng(Path.Combine(output, name)) == Error.Ok, "Falha ao salvar comparação de montanhas.");
                    metrics.Add(new { zoom, after, fps, detail = Mathf.SmoothStep(12f, 22f, zoom), screenshot = name });
                    GD.Print($"MOUNTAIN ZOOM {zoom} {(after ? "after" : "before")}: {fps:F1}fps");
                }
            }
            _material.SetShaderParameter("mountain_zoom_scale", 1f);
            var volcanoes = Enumerable.Range(0, world.Count).Where(i => (world.Features[i] & GeographicFeatures.Volcano) != 0 &&
                world.Water[i] == WaterType.None).ToArray();
            if (volcanoes.Length > 0)
            {
                int volcano = volcanoes.OrderByDescending(i => Score(i % world.Width, i / world.Width)).First();
                foreach (float zoom in new[] { 12f, 22f })
                {
                    ShowDetail(new Vector2I(volcano % world.Width, volcano / world.Width), zoom);
                    await NextDraw(); await NextDraw();
                    using var image = GetViewport().GetTexture().GetImage();
                    Require(image.SavePng(Path.Combine(output, $"volcano-comparison-zoom-{zoom}.png")) == Error.Ok, "Falha na captura do vulcão.");
                }
            }
            File.WriteAllText(Path.Combine(output, "metrics.json"), JsonSerializer.Serialize(new
            {
                manifest.GeneratorVersion, manifest.Seed, world.Width, world.Height,
                focusColumn = focus.X, focusRow = focus.Y, nominalVolcanoRatio = 0.5,
                volcanoes = volcanoes.Length, metrics
            }, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print("MOUNTAIN ZOOM REVIEW PASSED"); GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
