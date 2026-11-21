using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using Mapmaker.Core;
namespace Mapmaker.GodotApp;
public partial class MapView
{
    private async void RunGentleRidgeReview()
    {
        string output = ProjectSettings.GlobalizePath("res://results/gentle-ridges-v29");
        Directory.CreateDirectory(output);
        try
        {
            var stats = new System.Collections.Generic.List<object>();
            foreach (var (width, height, land) in new[] { (512, 256, 40), (512, 256, 50), (1024, 512, 38) })
            {
                WorldData? reference = null;
                foreach (int version in new[] { 28, 29 })
                {
                    var manifest = TestManifest(width, height, TerrainAlgorithm.ContinentalSculpt) with { GeneratorVersion = version };
                    manifest = manifest with { Generation = manifest.Generation with { LandPercent = land } };
                    var world = await System.Threading.Tasks.Task.Run(() => WorldGenerator.Create(manifest));
                    if (version == 28) reference = world;
                    else
                    {
                        Require(reference!.Water.SequenceEqual(world.Water), "Suavização alterou as costas.");
                        Require(reference.Elevation.Where((e, i) => world.Elevation[i] < e).Count() == 0,
                            "Suavização apagou a altitude das cordilheiras.");
                    }
                    // Measure slopes alongside existing mountain axes, including
                    // their shoulders but excluding cliffs at the coastline.
                    var slopes = new System.Collections.Generic.List<int>();
                    for (int cell = 0; cell < world.Count; cell++)
                    {
                        if (reference!.Elevation[cell] < 1850 || world.Water[cell] != WaterType.None) continue;
                        Hex hex = HexGrid.FromOffset(cell % width, cell / width);
                        for (int d = 0; d < 3; d++)
                        {
                            int n = HexGrid.NeighborIndex(hex, d, width, height, false);
                            if (n >= 0 && world.Water[n] == WaterType.None)
                                slopes.Add(Math.Abs(world.Elevation[cell] - world.Elevation[n]));
                        }
                    }
                    slopes.Sort();
                    int p95 = slopes[(int)(slopes.Count * 0.95)];
                    int peaks = world.Relief.Count(r => r == ReliefType.Peak);
                    int orphanEnds = WorldGenerator.ReadRivers(world).Count(r => !r.ParentId.HasValue && r.EndType == "Custom");
                    Require(orphanEnds == 0, "Suavização gerou rios sem destino.");
                    stats.Add(new { Width = width, Height = height, LandPercent = land, Version = version, Peaks = peaks,
                        SlopeP95 = p95, MeanSlope = slopes.Average(), UnconnectedEnds = orphanEnds });
                    GD.Print($"GENTLE {width}x{height} land={land} v{version}: peaks={peaks}, slope95={p95}, mean={slopes.Average():F1}, unconnected={orphanEnds}");
                    InstallWorld(world); RefreshInterface();
                    foreach (int layer in new[] { 0, 1 })
                    {
                        SetLayer(layer); _layerSelect.Select(layer);
                        await NextDraw(); await NextDraw();
                        using Image image = GetViewport().GetTexture().GetImage();
                        Require(image.SavePng(Path.Combine(output, $"{width}-land-{land}-v{version}-layer-{layer}.png")) == Error.Ok,
                            "Falha ao salvar comparação de encostas.");
                    }
                    if (version == 29) WorldFile.Save(Path.Combine(output, $"{width}-land-{land}-v29.world"), world, null);
                }
            }
            File.WriteAllText(Path.Combine(output, "metrics.json"), JsonSerializer.Serialize(stats, new JsonSerializerOptions { WriteIndented = true }));
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private async void RunRestoredRidgeReview()
    {
        string output = ProjectSettings.GlobalizePath("res://results/restored-ridges-v28");
        Directory.CreateDirectory(output);
        try
        {
            var stats = new System.Collections.Generic.List<object>();
            foreach (var (width, height, land) in new[] { (512, 256, 40), (512, 256, 50), (1024, 512, 38) })
            {
                WorldData? reference = null;
                foreach (int version in new[] { 26, 24, 27, 28 })
                {
                    var manifest = TestManifest(width, height, TerrainAlgorithm.ContinentalSculpt) with { GeneratorVersion = version };
                    manifest = manifest with { Generation = manifest.Generation with { LandPercent = land } };
                    var world = await System.Threading.Tasks.Task.Run(() => WorldGenerator.Create(manifest));
                    if (version == 26) reference = world;
                    if (version == 28)
                    {
                        // Compare the spatial fields, not just totals: disconnected
                        // peaks or displaced ranges must fail this regression check.
                        Require(reference!.Water.SequenceEqual(world.Water) &&
                            reference.Elevation.SequenceEqual(world.Elevation) &&
                            reference.Relief.SequenceEqual(world.Relief) &&
                            reference.Biomes.SequenceEqual(world.Biomes) &&
                            reference.Rainfall.SequenceEqual(world.Rainfall),
                            "A restauração perdeu o perfil contínuo das cordilheiras v26.");
                    }
                    int orphanEnds = WorldGenerator.ReadRivers(world).Count(r => !r.ParentId.HasValue && r.EndType == "Custom");
                    Require(orphanEnds == 0, "A restauração reintroduziu rios sem destino.");
                    int peaks = world.Relief.Count(r => r == ReliefType.Peak);
                    int alpine = world.Biomes.Count(b => b == BiomeType.Alpine);
                    stats.Add(new { Width = width, Height = height, LandPercent = land, Version = version,
                        Peaks = peaks, Alpine = alpine, UnconnectedEnds = orphanEnds });
                    GD.Print($"RESTORED {width}x{height} land={land} v{version}: peaks={peaks}, alpine={alpine}, unconnected={orphanEnds}");
                    if (version == 26) continue;
                    InstallWorld(world); RefreshInterface();
                    foreach (int layer in new[] { 0, 1, 6 })
                    {
                        SetLayer(layer); _layerSelect.Select(layer);
                        await NextDraw(); await NextDraw();
                        using Image image = GetViewport().GetTexture().GetImage();
                        Require(image.SavePng(Path.Combine(output, $"{width}-land-{land}-v{version}-layer-{layer}.png")) == Error.Ok,
                            "Falha ao salvar a comparação das cordilheiras.");
                    }
                    if (version == 28)
                        WorldFile.Save(Path.Combine(output, $"{width}-land-{land}-v28.world"), world, null);
                }
            }
            File.WriteAllText(Path.Combine(output, "metrics.json"), JsonSerializer.Serialize(stats, new JsonSerializerOptions { WriteIndented = true }));
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private async void RunFeatherReview()
    {
        try
        {
            Directory.CreateDirectory("results/elevation-feather-v27");
            var stats = new System.Collections.Generic.List<object>();
            WorldData? reference = null;
            foreach (int version in new[] { 26, 27 })
            {
                var m = TestManifest(1024, 512, TerrainAlgorithm.ContinentalSculpt) with { GeneratorVersion = version };
                var world = await System.Threading.Tasks.Task.Run(() => WorldGenerator.Create(m));
                if (version == 26) reference = world;
                else Require(reference!.Water.SequenceEqual(world.Water), "Feathering alterou a distribuição de terra/água.");
                var histogram = Enum.GetValues<ReliefType>().Select(t => new { Type = t.ToString(), Count = world.Relief.Where((r, i) => r == t && world.Water[i] == WaterType.None).Count() }).ToArray();
                GD.Print($"FEATHER v{version}: {JsonSerializer.Serialize(histogram)}, alpine={world.Biomes.Count(b => b == BiomeType.Alpine)}");
                stats.Add(new { Version = version, Relief = histogram, Alpine = world.Biomes.Count(b => b == BiomeType.Alpine) });
                InstallWorld(world); RefreshInterface();
                foreach (int layer in new[] { 0, 1, 6 })
                {
                    SetLayer(layer); _layerSelect.Select(layer);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    using Image image = GetViewport().GetTexture().GetImage();
                    image.SavePng($"res://results/elevation-feather-v27/v{version}-layer-{layer}.png");
                }
                WorldFile.Save($"results/elevation-feather-v27/world-v{version}.world", world, null);
            }
            File.WriteAllText("results/elevation-feather-v27/metrics.json", JsonSerializer.Serialize(stats));
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private async void RunOrganicRidgeReview()
    {
        try
        {
            Directory.CreateDirectory("results/ridges-v26");
            var metrics = new System.Collections.Generic.List<object>();
            var references = new System.Collections.Generic.Dictionary<int, WorldData>();
            foreach (int land in new[] { 40, 50 })
            foreach (int version in new[] { 19, 25, 26 })
            {
                var manifest = TestManifest(512, 256, TerrainAlgorithm.ContinentalSculpt) with { GeneratorVersion = version };
                manifest = manifest with { Generation = manifest.Generation with { LandPercent = land } };
                var world = await System.Threading.Tasks.Task.Run(() => WorldGenerator.Create(manifest));
                int alpine = world.Biomes.Count(b => b == BiomeType.Alpine);
                int peaks = world.Relief.Count(r => r == ReliefType.Peak);
                if (version == 19) references[land] = world;
                if (version == 26)
                {
                    int intersection = 0, union = 0;
                    var reference = references[land];
                    for (int i = 0; i < world.Count; i++)
                    {
                        bool oldAlpine = reference.Biomes[i] == BiomeType.Alpine;
                        bool newAlpine = world.Biomes[i] == BiomeType.Alpine;
                        if (oldAlpine && newAlpine) intersection++;
                        if (oldAlpine || newAlpine) union++;
                    }
                    float overlap = intersection / (float)Math.Max(1, union);
                    GD.Print($"REFERENCE MATCH land={land}: {overlap:P2}");
                    Require(overlap >= 0.95f, "Cordilheiras se afastaram da referência aprovada v19.");
                }
                GD.Print($"ORGANIC v{version} land={land}: alpine={alpine}, peaks={peaks}");
                metrics.Add(new { Version = version, LandPercent = land, Alpine = alpine, Peaks = peaks });
                InstallWorld(world); RefreshInterface(); SetLayer(6); _layerSelect.Select(6);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using Image image = GetViewport().GetTexture().GetImage();
                image.SavePng($"res://results/ridges-v26/land-{land}-v{version}.png");
                if (version == 26) WorldFile.Save($"results/ridges-v26/land-{land}-v26.world", world, null);
            }
            File.WriteAllText("results/ridges-v26/metrics.json", JsonSerializer.Serialize(metrics));
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private async void RunRidgeReview()
    {
        try
        {
            Directory.CreateDirectory("results/ridges-climate-v25");
            var rows = new System.Collections.Generic.List<object>();
            foreach (int version in new[] { 23, 24, 25 })
            {
                var manifest = TestManifest(1024, 512, TerrainAlgorithm.ContinentalSculpt) with { GeneratorVersion = version };
                var world = await System.Threading.Tasks.Task.Run(() => WorldGenerator.Create(manifest));
                var rivers = WorldGenerator.ReadRivers(world);
                int orphanEnds = rivers.Count(r => !r.ParentId.HasValue && r.EndType == "Custom");
                int alpine = world.Biomes.Count(b => b == BiomeType.Alpine), peaks = world.Relief.Count(r => r == ReliefType.Peak);
                GD.Print($"RIDGE v{version}: alpine={alpine}, peaks={peaks}, unconnected ends={orphanEnds}");
                if (version == 24) Require(orphanEnds == 0, "A revisão contém rios sem destino.");
                rows.Add(new { Version = version, Alpine = alpine, Peaks = peaks, UnconnectedEnds = orphanEnds });
                InstallWorld(world); RefreshInterface();
                foreach (int layer in new[] { 0, 6 })
                {
                    SetLayer(layer); _layerSelect.Select(layer);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    using Image image = GetViewport().GetTexture().GetImage();
                    image.SavePng($"res://results/ridges-climate-v25/world-v{version}-layer-{layer}.png");
                }
                WorldFile.Save($"results/ridges-climate-v25/world-v{version}.world", world, null);
            }
            File.WriteAllText("results/ridges-climate-v25/metrics.json", JsonSerializer.Serialize(rows));
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private async void RunBasinReview()
    {
        try
        {
            Directory.CreateDirectory("results/river-basins-v23");
            var m = TestManifest(1024, 512, TerrainAlgorithm.ContinentalSculpt) with { GeneratorVersion = WorldGenerator.Version };
            var after = await System.Threading.Tasks.Task.Run(() => WorldGenerator.Create(m));
            var before = after.CreateEditCopy(); before.Manifest = before.Manifest with { GeneratorVersion = 21 };
            await System.Threading.Tasks.Task.Run(() => WorldGenerator.GenerateRivers(before));
            GD.Print($"ROUTING joins={after.RiverDiagnostics.HeadwaterJoins}, curves={after.RiverDiagnostics.CurvedReaches}");
            Require(WorldGenerator.ReadRivers(after).SelectMany(r => r.Tiers).Distinct().OrderBy(x => x).SequenceEqual(new byte[] { 1, 2, 3, 4, 5 }),
                "O caso de revisão deve exercitar os cinco níveis.");
            var riversAfter = WorldGenerator.ReadRivers(after);
            var junctions = after.HexToRivers.Where(kv => kv.Value.Count >= 3).Select(kv => kv.Key).ToArray();
            int anchor = junctions.Length > 0 ? junctions.OrderByDescending(i => after.HexToRivers[i].Count).First() : riversAfter[0].Path[0];
            // Choose a confluence with many neighboring branches for close-up QA.
            int best = -1;
            foreach (int i in junctions)
            {
                int density = 0;
                for (int y = Math.Max(0, i / after.Width - 14); y <= Math.Min(after.Height - 1, i / after.Width + 14); y++)
                for (int x = Math.Max(0, i % after.Width - 14); x <= Math.Min(after.Width - 1, i % after.Width + 14); x++)
                    if (after.HexToRivers.TryGetValue(y * after.Width + x, out var crossing) && crossing.Count >= 3) density++;
                if (density > best) { best = density; anchor = i; }
            }
            var stats = new System.Collections.Generic.List<object>();
            foreach (var world in new[] { before, after })
            {
                var rivers = WorldGenerator.ReadRivers(world);
                int mouths = rivers.Count(r => r.EndType == "Ocean"), tributaries = rivers.Count(r => r.ParentId.HasValue);
                GD.Print($"BASIN v{world.Manifest.GeneratorVersion}: segments={rivers.Length}, mouths={mouths}, tributaries={tributaries}");
                stats.Add(new { Version = world.Manifest.GeneratorVersion, Segments = rivers.Length, Mouths = mouths, Tributaries = tributaries });
                InstallWorld(world); RefreshInterface(); SetLayer(0);
                _map.Scale = Vector2.One * 15;
                var center = HexGrid.Center(HexGrid.FromOffset(anchor % world.Width, anchor / world.Width));
                _map.Position = GetViewportRect().Size * 0.5f - new Vector2(center.X, center.Y) * 15;
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using Image image = GetViewport().GetTexture().GetImage();
                image.SavePng($"res://results/river-basins-v23/basin-v{world.Manifest.GeneratorVersion}.png");
            }
            WorldFile.Save("results/river-basins-v23/world-v23.world", after, null);
            File.WriteAllText("results/river-basins-v23/metrics.json", JsonSerializer.Serialize(stats));
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private async void RunRiverVisualCheck()
    {
        try
        {
            System.IO.Directory.CreateDirectory("results/river-fix-v20");
            var manifest = TestManifest(64, 64, TerrainAlgorithm.ContinentalSculpt) with { GeneratorVersion = 20 };
            var world = new WorldData(manifest);
            Array.Fill(world.Biomes, BiomeType.Grassland);
            Array.Fill(world.Elevation, (short)100);
            for (int y = 0; y < 64; y++)
            for (int x = 32; x < 64; x++)
            {
                int cell = y * 64 + x; world.Water[cell] = y < 32 ? WaterType.Ocean : WaterType.Lake;
                world.Elevation[cell] = -100; world.Biomes[cell] = BiomeType.None;
            }
            var rivers = new System.Collections.Generic.List<RiverRecord>();
            foreach (int y in new[] { 20, 40 })
            {
                int[] path = Enumerable.Range(24, 16).Select(x => y * 64 + x).ToArray();
                rivers.Add(new(rivers.Count, path, Enumerable.Repeat((byte)3, 15).ToArray(), Enumerable.Repeat(1000000f, 15).ToArray(), y < 32 ? "Ocean" : "Lake", null));
            }
            // A source and confluence wholly inside a lake must not make floating strokes.
            rivers.Add(new(2, new[] { 43 * 64 + 40, 43 * 64 + 41 }, new byte[] { 2 }, new float[] { 1000000 }, "Lake", null));
            WorldGenerator.WriteRivers(world, rivers.ToArray()); world.RebuildCoast();
            InstallWorld(world); RefreshInterface(); SetLayer(0);
            _map.Scale = Vector2.One * 20;
            var p = HexGrid.Center(HexGrid.FromOffset(32, 30));
            _map.Position = GetViewportRect().Size * 0.5f - new Vector2(p.X, p.Y) * 20;
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using Image image = GetViewport().GetTexture().GetImage();
            image.SavePng("res://results/river-fix-v20/mouth-and-lake.png");
            foreach (int version in new[] { 19, 20 })
            {
                var smallManifest = TestManifest(256, 128, TerrainAlgorithm.ContinentalSculpt) with { GeneratorVersion = version };
                var small = await System.Threading.Tasks.Task.Run(() => WorldGenerator.Create(smallManifest));
                int thin = 0;
                for (int cell = 0; cell < small.Count; cell++)
                {
                    if (small.Water[cell] != WaterType.None) continue;
                    int neighbors = 0; Hex hex = HexGrid.FromOffset(cell % small.Width, cell / small.Width);
                    for (int d = 0; d < 6; d++)
                    {
                        int n = HexGrid.NeighborIndex(hex, d, small.Width, small.Height, false);
                        if (n >= 0 && small.Water[n] == WaterType.None) neighbors++;
                    }
                    if (neighbors <= 2) thin++;
                }
                GD.Print($"Small 256x128 v{version}: {thin} thin land cells");
                InstallWorld(small); RefreshInterface(); SetLayer(0);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using Image frame = GetViewport().GetTexture().GetImage();
                frame.SavePng($"res://results/river-fix-v20/small-v{version}.png");
            }
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private async void RunMaximumGeneration()
    {
        try
        {
            var manifest = TestManifest(4096, 2048, TerrainAlgorithm.ContinentalSculpt) with { GeneratorVersion = WorldGenerator.Version };
            if (OS.GetCmdlineUserArgs().Contains("--world-map"))
            {
                var size = MapDomain.ExpandedSize(manifest.Width, manifest.Height);
                int referenceWidth = manifest.Width, referenceHeight = manifest.Height;
                manifest = manifest with { Width = size.Width, Height = size.Height,
                    Generation = manifest.Generation with { Extent = MapExtent.World,
                        ReferenceWidth = referenceWidth, ReferenceHeight = referenceHeight } };
            }
            var timer = System.Diagnostics.Stopwatch.StartNew();
            int last = -1;
            var world = await System.Threading.Tasks.Task.Run(() => WorldGenerator.Create(manifest, p =>
            {
                int current = (int)p.Step * 101 + p.Percent;
                if (current == last) return;
                last = current;
                GD.Print($"MAX {p.Step} {p.Percent}% {timer.Elapsed.TotalSeconds:F1}s");
            }));
            Require(world.RiverEdges.Any(x => x != 0), "Mundo máximo sem rios.");
            if (world.Manifest.GeneratorVersion >= 24)
            {
                int orphanEnds = WorldGenerator.ReadRivers(world).Count(r => !r.ParentId.HasValue && r.EndType == "Custom");
                Require(orphanEnds == 0, "Rio do mapa máximo sem destino.");
                GD.Print($"MAX alpine={world.Biomes.Count(b => b == BiomeType.Alpine)}, mountains={world.Relief.Count(r => r >= ReliefType.Mountains)}, maxElevation={world.Elevation.Max()}, orphanEnds={orphanEnds}");
            }
            GD.Print($"MAX ROUTING joins={world.RiverDiagnostics.HeadwaterJoins}, curves={world.RiverDiagnostics.CurvedReaches}");
            if (OS.GetCmdlineUserArgs().Contains("--compare-rivers"))
            {
                Directory.CreateDirectory("results/river-network-v23");
                var before = world.CreateEditCopy();
                before.Manifest = before.Manifest with { GeneratorVersion = 20 };
                await System.Threading.Tasks.Task.Run(() => WorldGenerator.GenerateRivers(before));
                int[] density = new int[world.Chunks.Count];
                for (int i = 0; i < before.Count; i++)
                    if (before.RiverEdges[i] != 0) density[i / before.Width / 32 * before.Chunks.Columns + i % before.Width / 32]++;
                int densest = Array.IndexOf(density, density.Max());
                var region = world.Chunks.GetChunk(densest);
                var metrics = new System.Collections.Generic.List<object>();
                foreach (var sample in new[] { before, world })
                {
                    var rivers = WorldGenerator.ReadRivers(sample);
                    int active = sample.RiverEdges.Count(x => x != 0);
                    metrics.Add(new { Version = sample.Manifest.GeneratorVersion, Segments = rivers.Length,
                        RiverHexes = active, Steps = rivers.Sum(r => r.Tiers.Length) });
                    GD.Print($"RIVER COMPARE v{sample.Manifest.GeneratorVersion}: segments={rivers.Length}, hexes={active}");
                    if (DisplayServer.GetName() == "headless") continue;
                    InstallWorld(sample); RefreshInterface(); SetLayer(0);
                    _map.Scale = Vector2.One * 8;
                    var p = HexGrid.Center(HexGrid.FromOffset(region.Column + 16, region.Row + 16));
                    _map.Position = GetViewportRect().Size * 0.5f - new Vector2(p.X, p.Y) * 8;
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    using Image image = GetViewport().GetTexture().GetImage();
                    image.SavePng($"res://results/river-network-v23/dense-v{sample.Manifest.GeneratorVersion}.png");
                }
                File.WriteAllText("results/river-network-v23/metrics.json", JsonSerializer.Serialize(metrics));
            }
            if (DisplayServer.GetName() != "headless")
            {
                GD.Print("MAX preparing renderer");
                var buffers = await System.Threading.Tasks.Task.Run(() => GlobalDataTextures.BuildBuffers(world));
                InstallWorld(world, buffers); RefreshInterface(); SetLayer(0);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using Image image = GetViewport().GetTexture().GetImage();
                string folder = $"results/generator-v{world.Manifest.GeneratorVersion}";
                Directory.CreateDirectory(folder);
                image.SavePng($"res://{folder}/maximum-map.png");
                GD.Print($"MAX renderer ready {timer.Elapsed.TotalSeconds:F2}s");
            }
            GD.Print($"MAX PASSED {timer.Elapsed.TotalSeconds:F2}s; land={world.Water.Count(w => w == WaterType.None)}; rivers={WorldGenerator.ReadRivers(world).Length}");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private async void RunNaturalPreview()
    {
        try
        {
            System.IO.Directory.CreateDirectory("results/natural-v23");
            foreach (int land in new[] { 30, 40, 50 })
            {
                var manifest = TestManifest(512, 256, TerrainAlgorithm.ContinentalSculpt) with { GeneratorVersion = WorldGenerator.Version };
                manifest = manifest with { Generation = manifest.Generation with { LandPercent = land } };
                var timer = System.Diagnostics.Stopwatch.StartNew();
                var world = await System.Threading.Tasks.Task.Run(() => WorldGenerator.Create(manifest));
                GD.Print($"v23 {land}%: {timer.Elapsed.TotalSeconds:F3}s, {WorldGenerator.ReadRivers(world).Length} rios");
                InstallWorld(world); RefreshInterface();
                foreach (int layer in new[] { 0, 6, 3 })
                {
                    SetLayer(layer); _layerSelect.Select(layer);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    using Image image = GetViewport().GetTexture().GetImage();
                    image.SavePng($"res://results/natural-v23/land-{land}-layer-{layer}.png");
                }
                WorldFile.Save($"results/natural-v23/land-{land}.world", world, null);
            }
            var largeManifest = TestManifest(1024, 512, TerrainAlgorithm.ContinentalSculpt) with { GeneratorVersion = WorldGenerator.Version };
            var largeTimer = System.Diagnostics.Stopwatch.StartNew();
            var large = await System.Threading.Tasks.Task.Run(() => WorldGenerator.Create(largeManifest));
            GD.Print($"v23 1024x512: {largeTimer.Elapsed.TotalSeconds:F3}s, {WorldGenerator.ReadRivers(large).Length} trechos fluviais");
            InstallWorld(large); RefreshInterface(); SetLayer(0); _layerSelect.Select(0);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using (Image image = GetViewport().GetTexture().GetImage()) image.SavePng("res://results/natural-v23/world-1024x512.png");
            WorldFile.Save("results/natural-v23/world-1024x512.world", large, null);
            var loaded = WorldFile.Load("results/natural-v23/world-1024x512.world");
            Require(ReadDense(large).SequenceEqual(ReadDense(loaded)), "Round-trip grande perdeu dados.");
            Require(JsonSerializer.Serialize(large.Tectonics) == JsonSerializer.Serialize(loaded.Tectonics), "Round-trip grande perdeu referência tectônica.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private static void VerifyNaturalGeneration()
    {
        int previousCount = 0;
        foreach (var size in new[] { (128, 64), (512, 256), (1024, 512), (2048, 1024), (4096, 2048) })
        {
            var m = TestManifest(size.Item1, size.Item2, TerrainAlgorithm.ContinentalSculpt) with { GeneratorVersion = 19 };
            var tectonics = TectonicReference.Create(m);
            tectonics.Validate(m.Width, m.Height);
            Require(tectonics.NormalPlateCount > previousCount, "Placas comuns não crescem com o mapa.");
            previousCount = tectonics.NormalPlateCount;
            GD.Print($"Placas v19 {m.Width}x{m.Height}: {previousCount}");
        }
        // A single narrow ridge must leave a continuous rain shadow, not a comb
        // whose period depends on the map's sampling stride.
        var profile = new short[256]; var surface = new WaterType[256]; var humidity = new ushort[256];
        for (int x = 0; x < 16; x++) surface[x] = WaterType.Ocean;
        profile[64] = 3500;
        WorldGenerator.TransportMoistureRow(profile, surface, humidity, 256, false, 1, 32);
        Require(humidity[66] < humidity[63], "Barreira não reduziu umidade a sotavento.");
        for (int x = 67; x < 256; x++) Require(humidity[x] >= humidity[x - 1], "Sombra de chuva oscilando em faixas.");
        foreach (int wind in new[] { -1, 1 })
        {
            WorldGenerator.TransportMoistureRow(profile, surface, humidity, 256, true, wind, 32);
            var shiftedHeight = new short[256]; var shiftedWater = new WaterType[256]; var shiftedHumidity = new ushort[256];
            for (int x = 0; x < 256; x++) { shiftedHeight[(x + 11) % 256] = profile[x]; shiftedWater[(x + 11) % 256] = surface[x]; }
            WorldGenerator.TransportMoistureRow(shiftedHeight, shiftedWater, shiftedHumidity, 256, true, wind, 32);
            for (int x = 0; x < 256; x++) Require(Math.Abs(shiftedHumidity[(x + 11) % 256] - humidity[x]) <= 1, "Costura na umidade periódica.");
        }
        var dryManifest = TestManifest(64, 64, TerrainAlgorithm.ContinentalSculpt) with { GeneratorVersion = 18 };
        var dry = new WorldData(dryManifest);
        for (int i = 0; i < dry.Count; i++)
        {
            dry.Elevation[i] = (short)(100 + (63 - i % 64) * 15);
            dry.Temperature[i] = -50;
            if (i % 64 == 63) { dry.Water[i] = WaterType.Ocean; dry.Elevation[i] = -1; }
            if (i % 64 is >= 30 and <= 38 && i / 64 is >= 25 and <= 35)
            { dry.Water[i] = WaterType.Lake; dry.Elevation[i] = -20; }
        }
        WorldGenerator.GenerateRivers(dry);
        Require(dry.RiverEdges.All(edge => edge == 0), "Clima seco criou rios por degelo artificial.");
        Array.Fill(dry.Rainfall, (ushort)1500);
        Array.Fill(dry.Temperature, (short)150);
        WorldGenerator.GenerateRivers(dry);
        Require(dry.RiverEdges.Any(edge => edge != 0), "Bacia úmida não gerou rios.");
        var desertManifest = TestManifest(128, 64, TerrainAlgorithm.ContinentalSculpt) with { GeneratorVersion = 22 };
        var desert = new WorldData(desertManifest);
        for (int i = 0; i < desert.Count; i++)
        {
            int col = i % desert.Width;
            desert.Elevation[i] = (short)((127 - col) * 5);
            desert.Temperature[i] = 250;
            desert.Rainfall[i] = col < 40 ? (ushort)1800 : (ushort)100;
            if (col == 127) { desert.Water[i] = WaterType.Ocean; desert.Elevation[i] = -1; }
        }
        WorldGenerator.GenerateRivers(desert);
        var desertRivers = WorldGenerator.ReadRivers(desert);
        var incomingEnds = desertRivers.Select(r => r.Path[^1]).ToHashSet();
        Require(desertRivers.Any(r => r.Path.Any(i => desert.Rainfall[i] == 100)), "Rio importado não atravessou zona árida.");
        Require(desertRivers.Where(r => !incomingEnds.Contains(r.Path[0])).All(r => desert.Rainfall[r.Path[0]] >= 450),
            "Nascente visível foi criada no deserto.");
        Array.Fill(desert.Rainfall, (ushort)100);
        WorldGenerator.GenerateRivers(desert);
        Require(WorldGenerator.ReadRivers(desert).Length == 0, "Deserto uniforme criou rede perene de riachos.");
        int[] versions = OS.GetCmdlineUserArgs().Contains("--current-generator-only")
            ? new[] { WorldGenerator.Version }
            : new[] { 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45 };
        foreach (int version in versions)
        foreach (int seed in new[] { 1234, 1, -77 })
        foreach (int land in new[] { 30, 40, 50 })
        foreach (bool wrap in new[] { false, true })
        {
            var manifest = TestManifest(128, 64, TerrainAlgorithm.ContinentalSculpt) with { Seed = seed, GeneratorVersion = version, HorizontalWrap = wrap };
            manifest = manifest with { Generation = manifest.Generation with { LandPercent = land } };
            var a = WorldGenerator.Create(manifest);
            var b = WorldGenerator.Create(manifest);
            Require(Math.Abs(a.Water.Count(w => w == WaterType.None) - a.Count * land / 100) <= 1, "Nova geração perdeu a quota de terra.");
            Require(a.Tectonics is not null && a.Tectonics.NormalPlateCount >= 7, "Referência tectônica ausente.");
            Require(a.Elevation.SequenceEqual(b.Elevation) && a.Rainfall.SequenceEqual(b.Rainfall) &&
                JsonSerializer.Serialize(a.SparseEntities) == JsonSerializer.Serialize(b.SparseEntities), "Nova geração não determinística.");
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".world");
            try
            {
                WorldFile.Save(path, a, null);
                var loaded = WorldFile.Load(path);
                Require(JsonSerializer.Serialize(a.Tectonics) == JsonSerializer.Serialize(loaded.Tectonics) &&
                    a.RiverEdges.SequenceEqual(loaded.RiverEdges) &&
                    JsonSerializer.Serialize(WorldGenerator.ReadRivers(a)) == JsonSerializer.Serialize(WorldGenerator.ReadRivers(loaded)),
                    "Referência tectônica ou níveis completos de rios perdidos no save.");
            }
            finally { File.Delete(path); }
            foreach (var river in WorldGenerator.ReadRivers(a))
            {
                if (version >= 24)
                {
                    int last = river.Path[^1];
                    if (river.ParentId is int downstream)
                        Require(a.RiversById[downstream].Path[0] == last, "Confluência sem âncora compartilhada.");
                    else
                    {
                        Require(river.EndType is "Ocean" or "Lake" or "Wetland" or "Boundary", "Rio gerado termina sem destino.");
                        Require(river.EndType == "Boundary" || a.Water[last] != WaterType.None, "Foz foi classificada em terra seca.");
                    }
                }
                Require(river.Path.Distinct().Count() == river.Path.Length, "Ciclo em rio.");
                for (int j = 1; j < river.AnnualFlow.Length; j++)
                    Require(a.Water[river.Path[j]] == WaterType.Lake || river.AnnualFlow[j] >= river.AnnualFlow[j - 1], "Fluxo diminui sem perda declarada.");
            }
            for (int temp = -200; temp <= 400; temp += 25)
            {
                double runoff = WorldGenerator.AnnualRunoff(1000, temp * 0.1, 0.8f);
                Require(runoff >= 0 && runoff <= 1000 && WorldGenerator.AnnualRunoff(0, temp * 0.1, 0.8f) == 0,
                    "Degelo criou água sem precipitação.");
            }
            var region = new WorldRegion(29, 29, 12, 12);
            int protectedCell = 32 * a.Width + 32;
            a.LockMask[protectedCell] = (ushort)HexLocks.Rivers;
            ushort protectedEdges = a.RiverEdges[protectedCell];
            var history = new UndoManager(a);
            var dense = ReadDense(a);
            string sparse = a.SparseEntities.GetRawText();
            history.Capture(region);
            WorldGenerator.RegenerateRegion(a, region, RegenerationMode.KeepWaterAndElevation);
            history.Commit();
            var after = ReadDense(a);
            Require(a.RiverEdges[protectedCell] == protectedEdges, "Regeneração alterou arestas fluviais travadas.");
            Require(after.Where((c, i) => !region.Contains(i % a.Width, i / a.Width) && c != dense[i]).Count() == 0,
                "Nova regeneração alterou exterior da seleção.");
            if (history.CanUndo)
            {
                history.Undo();
                Require(ReadDense(a).SequenceEqual(dense) && a.SparseEntities.GetRawText() == sparse, "Undo perdeu entidades de rio.");
                history.Redo();
                Require(ReadDense(a).SequenceEqual(after), "Redo perdeu caches de rio.");
            }
            GD.Print($"Nova geração: seed={seed}, terra={land}%, wrap={wrap}, placas comuns={a.Tectonics!.NormalPlateCount}, rios={WorldGenerator.ReadRivers(a).Length}.");
        }
    }
}
