using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Mapmaker.Core;

namespace Mapmaker.GodotApp;

public partial class MapView
{
    private sealed record WindComparison(int Samples, double MeanDegrees, double MedianDegrees,
        double P90Degrees, double Over15Percent, double Over45Percent, double OpposedPercent);

    private sealed record HadleySeedRun(int Seed, int Run, int Width, int Height, int ActiveCount,
        int GridWidth, int GridHeight, double GenerationSeconds, double HadleySeconds, double TextureSeconds,
        double MassRelativeError, float MinimumLayerMass, WindComparison OldVsSurface, WindComparison SurfaceVsUpper,
        SortedDictionary<string, string> WorldHashes, SortedDictionary<string, string> WindHashes,
        SortedDictionary<string, string> TextureHashes);

    private async void RunHadleySeedComparison()
    {
        const string output = "results/hadley-seed-comparison";
        var runs = new List<HadleySeedRun>();
        try
        {
            Directory.CreateDirectory(output);
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            foreach (int seed in new[] { 1234, -77 })
            {
                var size = MapDomain.ExpandedSize(4096, 2048);
                var manifest = TestManifest(size.Width, size.Height, TerrainAlgorithm.ContinentalSculpt) with
                {
                    Seed = seed, GeneratorVersion = WorldGenerator.Version
                };
                manifest = manifest with { Generation = manifest.Generation with
                {
                    Extent = MapExtent.World, ReferenceWidth = 4096, ReferenceHeight = 2048,
                    LandPercent = WorldCreationDialog.DefaultLandPercent, CoastDetail = WorldCreationDialog.DefaultCoastDetail
                } };
                for (int run = 1; run <= 2; run++)
                {
                    var result = await Capture(manifest, run);
                    runs.Add(result);
                    if (run == 2)
                    {
                        var first = runs[^2];
                        Require(first.WorldHashes.SequenceEqual(result.WorldHashes), $"Mapa mudou na repetição da seed {seed}.");
                        Require(first.WindHashes.SequenceEqual(result.WindHashes), $"Hadley mudou na repetição da seed {seed}.");
                        Require(first.TextureHashes.SequenceEqual(result.TextureHashes), $"Texturas mudaram na repetição da seed {seed}.");
                        GD.Print($"SAME SEED {seed} PASSED: world, all 11 Hadley arrays and all texture buffers bitwise identical.");
                    }
                    WriteSummary(completed: false);
                }
            }
            Require(!runs[0].WindHashes.SequenceEqual(runs[2].WindHashes), "Hadley não reagiu aos mundos de seeds diferentes.");
            WriteSummary(completed: true);
            GD.Print("HADLEY SEED COMPARISON PASSED: two maximum atlases, two generations per seed, unchanged source data, deterministic worlds/winds/textures, distinct seed responses.");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            File.WriteAllText(Path.Combine(output, "failure.txt"), error.ToString());
            GetTree().Quit(1);
        }

        void WriteSummary(bool completed)
        {
            using var process = Process.GetCurrentProcess();
            File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new
            {
                completed, generatorVersion = WorldGenerator.Version, runs,
                peakWorkingSetBytes = process.PeakWorkingSet64,
                note = "Direction statistics sample the 512x256 atmosphere uniformly; screenshots are illustrative, not pixel-diff assertions."
            }, new JsonSerializerOptions { WriteIndented = true }));
        }

        async Task<HadleySeedRun> Capture(WorldManifest manifest, int run)
        {
            var timer = Stopwatch.StartNew();
            int lastStep = -1;
            var world = await Task.Run(() => WorldGenerator.Create(manifest, progress =>
            {
                if ((int)progress.Step == lastStep) return;
                lastStep = (int)progress.Step;
                GD.Print($"COMPARE seed={manifest.Seed} run={run} {progress.Step} {timer.Elapsed.TotalSeconds:F2}s");
            }));
            double generationSeconds = timer.Elapsed.TotalSeconds;
            GD.Print($"COMPARE seed={manifest.Seed} run={run} generated {generationSeconds:F2}s");
            var before = await Task.Run(() => HashWorld(world));
            timer.Restart();
            var field = await Task.Run(() => HadleyWindSimulation.Simulate(world));
            double hadleySeconds = timer.Elapsed.TotalSeconds;
            GD.Print($"COMPARE seed={manifest.Seed} run={run} Hadley {field.GridWidth}x{field.GridHeight} {hadleySeconds:F2}s");
            Require(field.GridWidth == 512 && field.GridHeight == 256, "Atlas máximo não utilizou 512×256.");
            var after = await Task.Run(() => HashWorld(world));
            Require(before.SequenceEqual(after), "Simular Hadley alterou o mapa ou o clima.");
            var windHashes = HashWind(field);
            foreach (float[] values in WindArrays(field).Values)
                Require(values.All(float.IsFinite), "Hadley produziu valor não finito no Atlas real.");
            float minimum = Math.Min(field.LowerMass.Min(), field.UpperMass.Min());
            Require(minimum > 0, "Hadley produziu massa negativa no Atlas real.");
            double mass = field.LowerMass.Sum(value => (double)value) + field.UpperMass.Sum(value => (double)value);
            double massError = Math.Abs(mass - field.Vx.Length * 2d) / (field.Vx.Length * 2d);
            Require(massError < 1e-5, "Erro de conservação de massa no Atlas real.");
            var oldVsSurface = CompareDirections(world, field, upper: false);
            var surfaceVsUpper = CompareDirections(world, field, upper: true);
            timer.Restart();
            var buffers = await Task.Run(() => GlobalDataTextures.BuildBuffers(world));
            var textureHashes = new SortedDictionary<string, string>
            {
                ["Terrain"] = Hash(buffers.Terrain), ["Surface"] = Hash(buffers.Surface),
                ["Geography"] = Hash(buffers.Geography), ["Climate"] = Hash(buffers.Climate), ["Details"] = Hash(buffers.Details)
            };
            if (buffers.FeatureLow is not null) textureHashes["FeatureLow"] = Hash(buffers.FeatureLow);
            if (buffers.FeatureHigh is not null) textureHashes["FeatureHigh"] = Hash(buffers.FeatureHigh);
            double textureSeconds = timer.Elapsed.TotalSeconds;
            SetLayer(0); InstallWorld(world, buffers); RefreshInterface(); FitMap();
            _hadleyWind = field; _hadleyWorld = world;
            _hadleyOverlay.Field = field; _hadleyOverlay.Domain = world.Domain;
            SelectHex(NoHex);
            if (run == 1)
            {
                foreach (var (layer, name) in new[] { (0, "terrain"), (WindLayer, "old-wind"),
                    (HadleyLayer, "surface"), (HadleyUpperLayer, "upper") })
                {
                    SetLayer(layer); await NextDraw(); await NextDraw();
                    using var screenshot = GetViewport().GetTexture().GetImage();
                    Require(screenshot.SavePng(Path.Combine(output, $"seed-{manifest.Seed}-{name}.png")) == Error.Ok,
                        "Falha ao capturar comparação Hadley.");
                }
            }
            GD.Print($"COMPARE seed={manifest.Seed} run={run} old/surface mean={oldVsSurface.MeanDegrees:F2}deg, >45={oldVsSurface.Over45Percent:F2}%; surface/upper opposed={surfaceVsUpper.OpposedPercent:F2}%; mass error={massError:E3}");
            return new(manifest.Seed, run, world.Width, world.Height, world.ActiveCount, field.GridWidth, field.GridHeight,
                generationSeconds, hadleySeconds, textureSeconds, massError, minimum, oldVsSurface, surfaceVsUpper,
                before, windHashes, textureHashes);
        }
    }

    private static string Hash<T>(T[] values) where T : unmanaged =>
        Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(values.AsSpan())));

    private static SortedDictionary<string, string> HashWorld(WorldData world)
    {
        var regions = GeographicRegions.Get(world);
        return new()
        {
            ["Elevation"] = Hash(world.Elevation), ["Water"] = Hash(world.Water), ["Relief"] = Hash(world.Relief),
            ["Biomes"] = Hash(world.Biomes), ["Landforms"] = Hash(world.Landforms), ["Features"] = Hash(world.Features),
            ["Temperature"] = Hash(world.Temperature), ["Rainfall"] = Hash(world.Rainfall), ["RoadEdges"] = Hash(world.RoadEdges),
            ["RiverEdges"] = Hash(world.RiverEdges), ["Flags"] = Hash(world.Flags), ["LockMask"] = Hash(world.LockMask),
            ["SparseEntities"] = Hash(JsonSerializer.SerializeToUtf8Bytes(world.SparseEntities)),
            ["Tectonics"] = Hash(JsonSerializer.SerializeToUtf8Bytes(world.Tectonics)),
            ["RegionIds"] = Hash(regions.CellIds), ["Regions"] = Hash(JsonSerializer.SerializeToUtf8Bytes(regions.Regions))
        };
    }

    private static SortedDictionary<string, float[]> WindArrays(HadleyWindField field) => new()
    {
        ["Vx"] = field.Vx, ["Vy"] = field.Vy, ["UpperVx"] = field.UpperVx, ["UpperVy"] = field.UpperVy,
        ["Pressure"] = field.Pressure, ["UpperPressure"] = field.UpperPressure, ["LowerMass"] = field.LowerMass,
        ["UpperMass"] = field.UpperMass, ["VerticalFlow"] = field.VerticalFlow,
        ["Vorticity"] = field.Vorticity, ["UpperVorticity"] = field.UpperVorticity
    };

    private static SortedDictionary<string, string> HashWind(HadleyWindField field) =>
        new(WindArrays(field).ToDictionary(pair => pair.Key, pair => Hash(pair.Value)));

    private static WindComparison CompareDirections(WorldData world, HadleyWindField field, bool upper)
    {
        var angles = new List<double>(field.Vx.Length);
        for (int y = 0; y < field.GridHeight; y++)
        {
            int row = Math.Min(world.Height - 1, (int)((y + 0.5f) / field.GridHeight * world.Height));
            var (east, south) = AnnualWind.DirectionWeights(row, world.Height);
            for (int x = 0; x < field.GridWidth; x++)
            {
                int i = y * field.GridWidth + x;
                double ax = upper ? field.Vx[i] : AnnualWind.AlongLatitude * (2 * east - 1);
                double ay = upper ? field.Vy[i] : AnnualWind.AcrossLatitude * (2 * south - 1);
                double bx = upper ? field.UpperVx[i] : field.Vx[i], by = upper ? field.UpperVy[i] : field.Vy[i];
                double a = Math.Sqrt(ax * ax + ay * ay), b = Math.Sqrt(bx * bx + by * by);
                if (a < 0.02 || b < 0.02) continue;
                angles.Add(Math.Acos(Math.Clamp((ax * bx + ay * by) / (a * b), -1, 1)) * 180 / Math.PI);
            }
        }
        angles.Sort();
        Require(angles.Count > 0, "Nenhum vetor de vento comparável.");
        return new(angles.Count, angles.Average(), angles[angles.Count / 2], angles[(int)((angles.Count - 1) * 0.9)],
            100d * angles.Count(angle => angle > 15) / angles.Count,
            100d * angles.Count(angle => angle > 45) / angles.Count,
            100d * angles.Count(angle => angle > 90) / angles.Count);
    }
}
