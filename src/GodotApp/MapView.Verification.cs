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
    private static WorldManifest TestManifest(int width, int height, TerrainAlgorithm algorithm)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new WorldManifest(WorldFile.CurrentVersion, "Mapa de teste", 1234, WorldGenerator.Version,
            width, height, false, 5, "km", now, now,
            new GenerationSettings(algorithm, 3, 38, 50, GenerationStage.Terrain));
    }

    private async void RunSmokeTest()
    {
        string path = Path.Combine(Path.GetTempPath(), $"mapmaker-test-{Guid.NewGuid():N}.world");
        _preferencesPath = Path.ChangeExtension(path, ".cfg");
        try
        {
            Require(_world is null && _emptyState.Visible, "A tela inicial deve abrir vazia.");
            WorldManifest manifest = TestManifest(1024, 512, TerrainAlgorithm.Fractal);
            var blank = WorldGenerator.Create(manifest with { Generation = manifest.Generation with { Stage = GenerationStage.Blank } });
            InstallWorld(blank);
            RefreshInterface();
            Require(!_emptyState.Visible && _textures is null && _layerSelect.IsItemDisabled(3), "A grade vazia tem estado próprio.");

            WorldData original = WorldGenerator.Create(manifest);
            VerifyBiomes(original);
            for (int row = 0; row < original.Height; row++)
            for (int col = 0; col < original.Width; col++)
            {
                Hex hex = HexGrid.FromOffset(col, row);
                Require(HexGrid.ToOffset(HexGrid.Pick(HexGrid.Center(hex))) == (col, row), "Seleção de hex incorreta.");
            }
            int west = HexGrid.NeighborIndex(HexGrid.FromOffset(0, 0), 3, 1024, 512, true);
            Require(west == 1023 && HexGrid.NeighborIndex(HexGrid.FromOffset(0, 0), 3, 1024, 512, false) == -1,
                "Vizinhança horizontal incorreta.");
            Require(HexGrid.Distance(HexGrid.FromOffset(0, 0), HexGrid.FromOffset(1023, 0), 1024, true) == 1,
                "Distância com wrap incorreta.");
            for (int direction = 0; direction < 6; direction++)
            {
                int index = 4 * 1024 + 4;
                int neighbor = HexGrid.NeighborIndex(HexGrid.FromOffset(4, 4), direction, 1024, 512, false);
                HexGrid.SetSharedEdge(original.RoadEdges, index, direction, 3, 1024, 512, false);
                Require(HexGrid.EdgeValue(original.RoadEdges[index], direction) == 3
                    && HexGrid.EdgeValue(original.RoadEdges[neighbor], (direction + 3) % 6) == 3, "Aresta compartilhada incorreta.");
            }
            original.LockMask[10] = 256;
            original.Flags[9] |= 2;

            var timer = Stopwatch.StartNew();
            WorldManifest saved = WorldFile.Save(path, original, null);
            WorldData loaded = WorldFile.Load(path);
            double seconds = timer.Elapsed.TotalSeconds;
            Require(saved == loaded.Manifest, "Manifesto diferente após abrir.");
            Require(original.Elevation.AsSpan().SequenceEqual(loaded.Elevation)
                && original.Temperature.AsSpan().SequenceEqual(loaded.Temperature)
                && original.Rainfall.AsSpan().SequenceEqual(loaded.Rainfall)
                && original.RoadEdges.AsSpan().SequenceEqual(loaded.RoadEdges)
                && original.RiverEdges.AsSpan().SequenceEqual(loaded.RiverEdges)
                && original.LockMask.AsSpan().SequenceEqual(loaded.LockMask)
                && original.Flags.AsSpan().SequenceEqual(loaded.Flags)
                && original.Water.SequenceEqual(loaded.Water)
                && original.Relief.SequenceEqual(loaded.Relief)
                && original.Biomes.SequenceEqual(loaded.Biomes)
                && JsonSerializer.Serialize(original.SparseEntities) == JsonSerializer.Serialize(loaded.SparseEntities),
                "Dados diferentes após reabrir.");
            using (var exclusive = new FileStream(path, FileMode.Open, System.IO.FileAccess.ReadWrite, FileShare.None))
            {
                if (OperatingSystem.IsWindows())
                {
                    bool failed = false;
                    try { WorldFile.Save(path, original, null); }
                    catch (IOException) { failed = true; }
                    Require(failed, "O teste deveria impedir a substituição do arquivo.");
                }
            }
            Require(WorldFile.Load(path).Manifest == saved, "O arquivo válido foi alterado durante a tentativa de gravação.");
            _ = WorldFile.Save(path, original, null);

            var small = TestManifest(128, 64, TerrainAlgorithm.LandmassCenters);
            WorldData centers = WorldGenerator.Create(small);
            Require(centers.Elevation.AsSpan().SequenceEqual(WorldGenerator.Create(small).Elevation), "O gerador não reproduziu a seed.");
            WorldData fractal = WorldGenerator.Create(small with { Generation = small.Generation with { Algorithm = TerrainAlgorithm.Fractal } });
            Require(!centers.Elevation.AsSpan().SequenceEqual(fractal.Elevation), "Os algoritmos produziram o mesmo terreno.");
            WorldData oneContinent = WorldGenerator.Create(small with { Generation = small.Generation with { Continents = 1 } });
            Require(!centers.Water.SequenceEqual(oneContinent.Water), "A quantidade de continentes não alterou o terreno.");

            InstallWorld(loaded);
            RefreshInterface();
            SelectHex(new Vector2I(513, 257));
            SetLayer(3);
            Require(_selected == new Vector2I(513, 257) && _inspection.Text.Contains("513, 257") && _layer == 3, "Inspector inválido.");
            Require(seconds < 5, "O round-trip ultrapassou 5 segundos.");
            await CreateWorldAsync(small, fillingBlank: false);
            Require(_world?.Width == small.Width && _dirty && _currentPath is null, "A criação não ativou o novo mapa.");
            await SaveWorldAsync(path);
            Require(!_dirty && _currentPath == path && File.Exists(_preferencesPath), "Salvar não atualizou o mapa recente.");
            InstallWorld(loaded);
            await OpenWorldAsync(path);
            Require(_world?.Manifest.Generation.Algorithm == TerrainAlgorithm.LandmassCenters && _world.Width == small.Width
                && _currentPath == path && !_dirty, "Abrir não restaurou o mapa salvo.");
            GD.Print($"M1 smoke test passed. 524288 hexes, save/load {seconds:F3} s; atomic overwrite and both algorithms verified.");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
        finally
        {
            File.Delete(path);
            File.Delete(_preferencesPath);
        }
    }

    private async void RunBenchmark(bool large)
    {
        try
        {
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            await NextDraw();
            Screenshot("benchmark-start.png");
            _creation.ShowForm();
            await NextDraw();
            await NextDraw();
            Screenshot("benchmark-new-map.png");
            _creation.Hide();

            int width = large ? 4096 : 1024;
            var timer = Stopwatch.StartNew();
            var world = await Task.Run(() => WorldGenerator.Create(TestManifest(width, width / 2, TerrainAlgorithm.Fractal)));
            InstallWorld(world);
            RefreshInterface();
            await NextDraw();
            double setup = timer.Elapsed.TotalSeconds;
            for (int i = 0; i < 30; i++)
                await NextDraw();
            Screenshot(large ? "benchmark-large.png" : "benchmark-m0.png");
            if (!large)
            {
                Vector2I detail = FindDetailCenter(world);
                ShowDetail(detail, 8);
                await NextDraw();
                Screenshot("benchmark-art-overview.png");
                ShowDetail(detail, 27);
                await NextDraw();
                Screenshot("benchmark-relief.png");

                int beach = Array.IndexOf(world.Biomes, BiomeType.Beach, world.Count * 2 / 5);
                if (beach >= 0)
                {
                    ShowDetail(new Vector2I(beach % world.Width, beach / world.Width), 18);
                    await NextDraw();
                    Screenshot("benchmark-coast.png");
                }
            }

            const int frameCount = 240;
            Vector2 viewportCenter = GetViewportRect().Size * 0.5f;
            timer.Restart();
            for (int i = 0; i < frameCount; i++)
            {
                float zoom = 3.5f + Mathf.Sin(i * 0.04f) * 0.45f;
                var center = new Vector2(_mapSize.X * 0.58f + Mathf.Sin(i * 0.025f) * 58f, _mapSize.Y * 0.51f);
                _map.Scale = Vector2.One * zoom;
                _map.Position = viewportCenter - center * zoom;
                UpdateArtworkZoom();
                await NextDraw();
            }
            double fps = frameCount / timer.Elapsed.TotalSeconds;
            double slowestLayer = 0;
            for (int i = 1; i <= Layers.Length; i++)
            {
                timer.Restart();
                SetLayer(i % Layers.Length);
                _layerSelect.Select(i % Layers.Length);
                await NextDraw();
                slowestLayer = Math.Max(slowestLayer, timer.Elapsed.TotalMilliseconds);
            }
            timer.Restart();
            _textures!.UploadAll();
            await NextDraw();
            double upload = timer.Elapsed.TotalMilliseconds;
            GD.Print($"{world.Width}x{world.Height}: setup {setup:F2} s, {fps:F1} FPS moving, slowest layer {slowestLayer:F2} ms, full upload {upload:F2} ms (through frame submission).");
            if (large)
            {
                byte[]? preview = CapturePreview();
                timer.Restart();
                await Task.Run(() => VerifyLargeSave(world, preview));
                GD.Print($"4096x2048: save/load {timer.Elapsed.TotalSeconds:F2} s.");
            }
            GetTree().Quit(fps >= 30 && slowestLayer < 100 ? 0 : 2);
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    private SignalAwaiter NextDraw() => ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

    private void ShowDetail(Vector2I hex, float zoom)
    {
        var center = HexGrid.Center(HexGrid.FromOffset(hex.X, hex.Y));
        _map.Scale = Vector2.One * zoom;
        _map.Position = GetViewportRect().Size * 0.5f - new Vector2(center.X, center.Y) * zoom;
        _statusTime = 0;
        if (_world is WorldData world)
            _performance.Text = $"{world.Width} × {world.Height}";
        UpdateArtworkZoom();
    }

    private static Vector2I FindDetailCenter(WorldData world)
    {
        var best = Vector2I.Zero;
        int bestScore = -1;
        for (int row = 35; row < world.Height - 35; row += 15)
        for (int column = 50; column < world.Width - 50; column += 15)
        {
            int forest = 0;
            int hills = 0;
            int mountain = 0;
            int plains = 0;
            for (int y = row - 13; y <= row + 13; y += 3)
            for (int x = column - 17; x <= column + 17; x += 3)
            {
                int i = y * world.Width + x;
                if (Biomes.IsForest(world.Biomes[i]))
                    forest++;
                if (world.Relief[i] == ReliefType.Hills)
                    hills++;
                if (world.Relief[i] >= ReliefType.Mountains)
                    mountain++;
                if (IsOpenPlain(world, i))
                    plains++;
            }
            int score = Math.Min(forest, plains) * 5 + Math.Min(hills, mountain) + Math.Min(forest, 25);
            if (score <= bestScore)
                continue;
            best = new Vector2I(column, row);
            bestScore = score;
        }
        return best;
    }

    private static void VerifyBiomes(WorldData world)
    {
        int beaches = 0;
        int coast = 0;
        int land = 0;
        int plains = 0;
        int plainRun = 0;
        int longestPlainRun = 0;
        for (int i = 0; i < world.Count; i++)
        {
            bool openPlain = IsOpenPlain(world, i);
            if (i % world.Width == 0 || !openPlain)
                plainRun = 0;
            if (openPlain)
            {
                plains++;
                if (plainRun == 0 || Math.Abs(world.Elevation[i] - world.Elevation[i - 1]) <= 20)
                    plainRun++;
                else
                    plainRun = 1;
                longestPlainRun = Math.Max(longestPlainRun, plainRun);
            }
            if (world.Water[i] != WaterType.None)
                continue;
            land++;
            bool coastal = (world.Flags[i] & WorldData.CoastFlag) != 0;
            if (coastal)
                coast++;
            if (Biomes.IsForest(world.Biomes[i]))
            {
                Hex hex = HexGrid.FromOffset(i % world.Width, i / world.Width);
                bool connected = false;
                for (int direction = 0; direction < 6; direction++)
                {
                    int neighbor = HexGrid.NeighborIndex(hex, direction, world.Width, world.Height, world.Manifest.HorizontalWrap);
                    if (neighbor < 0 || !Biomes.IsForest(world.Biomes[neighbor]))
                        continue;
                    connected = true;
                    break;
                }
                Require(connected, "Hex florestal isolado.");
            }
            if (world.Biomes[i] != BiomeType.Beach)
                continue;
            beaches++;
            Require(coastal && world.Elevation[i] <= 200, "Praia classificada fora do litoral baixo.");
        }
        Require(beaches > 0 && beaches < coast, "Esperava praias e costões no mapa de teste.");
        Require(plains >= land / 10 && longestPlainRun >= 12, "Esperava planícies abertas contínuas no mapa de teste.");
        GD.Print($"Litoral: {beaches:N0} hexes de praia em {coast:N0} hexes costeiros.");
        GD.Print($"Planícies abertas: {100f * plains / land:F1}% da terra; faixa plana com {longestPlainRun} hexes.");
    }

    private static bool IsOpenPlain(WorldData world, int i) =>
        world.Water[i] == WaterType.None && world.Relief[i] == ReliefType.Flat &&
        (world.Biomes[i] is BiomeType.Grassland or BiomeType.Steppe or BiomeType.Savanna);

    private static void VerifyLargeSave(WorldData world, byte[]? preview)
    {
        string path = Path.Combine(Path.GetTempPath(), $"mapmaker-large-{Guid.NewGuid():N}.world");
        try
        {
            WorldManifest manifest = WorldFile.Save(path, world, preview);
            WorldData loaded = WorldFile.Load(path);
            Require(loaded.Manifest == manifest && world.Elevation.AsSpan().SequenceEqual(loaded.Elevation)
                && world.Biomes.SequenceEqual(loaded.Biomes), "O save do mapa grande mudou os dados.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private void Screenshot(string filename)
    {
        using Image screenshot = GetViewport().GetTexture().GetImage();
        Require(screenshot.SavePng($"res://.godot/{filename}") == Error.Ok, "Falha ao capturar a tela.");
    }

    private static void Require(bool result, string detail)
    {
        if (!result)
            throw new InvalidOperationException(detail);
    }
}
