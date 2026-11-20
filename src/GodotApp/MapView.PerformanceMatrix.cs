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
    private async void RunSizeBenchmark(int width)
    {
        string output = "results/performance-all-sizes-v29";
        string savePath = Path.Combine(Path.GetTempPath(), "perf-" + Guid.NewGuid() + ".world");
        try
        {
            Require(WorldCreationDialog.Sizes.Any(s => s.X == width), "Tamanho fora dos presets.");
            Directory.CreateDirectory(output);
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            var manifest = TestManifest(width, width / 2, TerrainAlgorithm.ContinentalSculpt) with { GeneratorVersion = WorldGenerator.Version };
            // Each preset runs in a fresh process; warm the same small recipe first.
            // Return no world from the task: Task<WorldData> could keep its result
            // alive at the baseline and release it during the measured generation.
            await Task.Run(() => { _ = WorldGenerator.Create(manifest with { Width = 128, Height = 64 }); });
            long baselineManaged = GC.GetTotalMemory(true);
            var timer = Stopwatch.StartNew();
            int lastStep = -1;
            var world = await Task.Run(() => WorldGenerator.Create(manifest, p =>
            {
                if ((int)p.Step == lastStep) return;
                lastStep = (int)p.Step;
                GD.Print($"SIZE {width} {p.Step} {timer.Elapsed.TotalSeconds:F2}s");
            }));
            double generationSeconds = timer.Elapsed.TotalSeconds;
            long mapManagedBytes = GC.GetTotalMemory(true) - baselineManaged;
            GD.Print($"SIZE {width} generated {generationSeconds:F3}s map={mapManagedBytes / 1048576.0:F2}MiB");
            timer.Restart();
            var buffers = await Task.Run(() => GlobalDataTextures.BuildBuffers(world));
            InstallWorld(world, buffers); RefreshInterface(); SetLayer(0); FitMap();
            await NextDraw(); await NextDraw();
            double rendererSeconds = timer.Elapsed.TotalSeconds;
            for (int i = 0; i < 20; i++) await NextDraw();
            var fitScale = _map.Scale;
            var fitPosition = _map.Position;
            var viewportCenter = GetViewportRect().Size * 0.5f;
            async Task<(double Fps, double P95)> Moving(bool overview)
            {
                var samples = new double[180];
                var total = Stopwatch.StartNew();
                for (int i = 0; i < samples.Length; i++)
                {
                    var frame = Stopwatch.StartNew();
                    float zoom = overview ? fitScale.X * (1f + 0.12f * Mathf.Sin(i * 0.04f)) : 3.5f + 0.45f * Mathf.Sin(i * 0.04f);
                    _map.Scale = Vector2.One * zoom;
                    var center = new Vector2(_mapSize.X * (0.5f + 0.10f * Mathf.Sin(i * 0.025f)), _mapSize.Y * 0.5f);
                    _map.Position = viewportCenter - center * zoom;
                    UpdateArtworkZoom(); await NextDraw();
                    samples[i] = frame.Elapsed.TotalMilliseconds;
                }
                double fps = samples.Length / total.Elapsed.TotalSeconds;
                Array.Sort(samples);
                return (fps, samples[(int)Math.Ceiling(samples.Length * 0.95) - 1]);
            }
            var overview = await Moving(true);
            var detail = await Moving(false);
            _map.Scale = fitScale; _map.Position = fitPosition; UpdateArtworkZoom();
            double slowestLayer = 0;
            var layerTimes = new double[Layers.Length];
            for (int pass = 0; pass < 3; pass++)
            for (int layer = 0; layer < Layers.Length; layer++)
            {
                timer.Restart(); SetLayer(layer); _layerSelect.Select(layer); await NextDraw();
                layerTimes[layer] = Math.Max(layerTimes[layer], timer.Elapsed.TotalMilliseconds);
                slowestLayer = Math.Max(slowestLayer, layerTimes[layer]);
            }
            SetLayer(0); _layerSelect.Select(0); await NextDraw();
            timer.Restart(); _textures!.UploadAll(); await NextDraw();
            double uploadMs = timer.Elapsed.TotalMilliseconds;
            GD.Print($"SIZE {width} rendered overview={overview.Fps:F1}fps detail={detail.Fps:F1}fps layer={slowestLayer:F2}ms");

            timer.Restart();
            await Task.Run(() => WorldFile.Save(savePath, world, null));
            double saveSeconds = timer.Elapsed.TotalSeconds;
            timer.Restart();
            await Task.Run(() =>
            {
                var loaded = WorldFile.Load(savePath);
                Require(world.Elevation.AsSpan().SequenceEqual(loaded.Elevation) && world.Water.AsSpan().SequenceEqual(loaded.Water) &&
                    world.Relief.AsSpan().SequenceEqual(loaded.Relief) && world.Biomes.AsSpan().SequenceEqual(loaded.Biomes) &&
                    world.Temperature.AsSpan().SequenceEqual(loaded.Temperature) && world.Rainfall.AsSpan().SequenceEqual(loaded.Rainfall) &&
                    world.RoadEdges.AsSpan().SequenceEqual(loaded.RoadEdges) && world.RiverEdges.AsSpan().SequenceEqual(loaded.RiverEdges) &&
                    world.Flags.AsSpan().SequenceEqual(loaded.Flags) && world.LockMask.AsSpan().SequenceEqual(loaded.LockMask) &&
                    world.SparseEntities.GetRawText() == loaded.SparseEntities.GetRawText(), "Round-trip de desempenho perdeu dados.");
            });
            double loadAndVerifySeconds = timer.Elapsed.TotalSeconds;
            long saveBytes = new FileInfo(savePath).Length;
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();

            timer.Restart(); SetEditorPanelOpen(true); SetLayer(0); FitMap();
            await NextDraw(); await NextDraw();
            double editorOpenSeconds = timer.Elapsed.TotalSeconds;
            Descendants(_editorPanel).OfType<SpinBox>().First(box => box.Name == "BrushRadius").Value = 6;
            Descendants(_editorPanel).OfType<Button>().First(button => button.Text == "Subir").EmitSignal(BaseButton.SignalName.Pressed);
            var editing = new double[60];
            Vector2 At(int step)
            {
                var center = HexGrid.Center(HexGrid.FromOffset(width / 4 + step * Math.Max(1, width / 1024), world.Height / 2));
                return _map.ToGlobal(new Vector2(center.X, center.Y));
            }
            for (int i = 0; i < 15; i++) await NextDraw();
            StartMapEdit(At(0));
            for (int i = 0; i < editing.Length; i++)
            {
                timer.Restart(); ContinueGesture(At(i)); _editorPreview!.Refresh(1.0 / 30); await NextDraw();
                editing[i] = timer.Elapsed.TotalMilliseconds;
            }
            FinishGesture();
            Require(_history!.CanUndo, "O benchmark precisa produzir uma pincelada real.");
            Array.Sort(editing);
            long editorManagedBytes = GC.GetTotalMemory(true);
            long processWorkingSetBytes = Process.GetCurrentProcess().WorkingSet64;
            CancelEditorDraft();
            var result = new
            {
                manifest.GeneratorVersion, world.Width, world.Height, hexes = world.Count, manifest.Seed, manifest.Generation,
                generationSeconds, rendererSeconds, firstMapSeconds = generationSeconds + rendererSeconds, mapManagedBytes,
                overviewFps = overview.Fps, overviewP95Ms = overview.P95, detailFps = detail.Fps, detailP95Ms = detail.P95,
                slowestLayerMs = slowestLayer, layerTimesMs = layerTimes, fullUploadMs = uploadMs,
                saveSeconds, loadAndVerifySeconds, saveBytes, editorOpenSeconds,
                editorMedianMs = editing[editing.Length / 2], editorP95Ms = editing[(int)Math.Ceiling(editing.Length * 0.95) - 1],
                editorManagedBytes, processWorkingSetBytes, peakProcessWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64,
                rivers = WorldGenerator.ReadRivers(world).Length, roundTripPassed = true
            };
            File.WriteAllText(Path.Combine(output, $"{width}x{world.Height}.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"SIZE {width} PASSED: gen={generationSeconds:F3}s edit95={result.editorP95Ms:F2}ms openEditor={editorOpenSeconds:F3}s save={saveSeconds:F3}s load+verify={loadAndVerifySeconds:F3}s");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
        finally { File.Delete(savePath); }
    }
}
