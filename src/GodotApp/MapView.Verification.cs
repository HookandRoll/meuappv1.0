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
            VerifyDetailIcons(original);
            VerifyRegionalGeneration();
            VerifyShorelineFinish();
            VerifyShallowInlets();
            VerifyBiomeRibbonFinish();
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
            int coast = Array.FindIndex(loaded.Flags, flags => (flags & WorldData.CoastFlag) != 0);
            Hex coastHex = HexGrid.FromOffset(coast % loaded.Width, coast / loaded.Width);
            int coastalOcean = Enumerable.Range(0, 6)
                .Select(direction => HexGrid.NeighborIndex(coastHex, direction, loaded.Width, loaded.Height, false))
                .First(index => index >= 0 && loaded.Water[index] == WaterType.Ocean);
            SelectHex(new Vector2I(coastalOcean % loaded.Width, coastalOcean / loaded.Width));
            Require(_inspection.Text.Contains("Oceano costeiro"), "A faixa costeira deve continuar identificada como oceano.");
            SelectHex(new Vector2I(513, 257));
            SetLayer(3);
            Require(_selected == new Vector2I(513, 257) && _inspection.Text.Contains("513, 257") && _layer == 3, "Inspector inválido.");
            Require(seconds < 5, "O round-trip ultrapassou 5 segundos.");
            Task creation = CreateWorldAsync(small, fillingBlank: false);
            Require(_generationStatus.Visible && !_footer.Visible, "A criação deve exibir a barra de progresso.");
            await creation;
            Require(!_generationStatus.Visible && _footer.Visible, "A geração deve devolver o rodapé ao concluir.");
            Require(_world?.Width == small.Width && _dirty && _currentPath is null, "A criação não ativou o novo mapa.");
            await SaveWorldAsync(path);
            Require(!_dirty && _currentPath == path && File.Exists(_preferencesPath), "Salvar não atualizou o mapa recente.");
            InstallWorld(loaded);
            await OpenWorldAsync(path);
            Require(_world?.Manifest.Generation.Algorithm == TerrainAlgorithm.LandmassCenters && _world.Width == small.Width
                && _currentPath == path && !_dirty, "Abrir não restaurou o mapa salvo.");
            VerifyContinentalSculpture(path);
            GD.Print($"Smoke test passed. 524288 hexes, save/load {seconds:F3} s; regional seams and continental sculpture verified.");
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

    private async void RunBenchmark(bool large, bool sculpt)
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
            TerrainAlgorithm algorithm = sculpt ? TerrainAlgorithm.ContinentalSculpt : TerrainAlgorithm.Fractal;
            var world = await Task.Run(() => WorldGenerator.Create(TestManifest(width, width / 2, algorithm)));
            int shared = -1;
            if (sculpt && !large)
            {
                GlobalDataTextures.Buffers buffers = GlobalDataTextures.BuildBuffers(world);
                shared = FindSharedDetail(buffers);
                InstallWorld(world, buffers);
            }
            else
            {
                InstallWorld(world);
            }
            RefreshInterface();
            await NextDraw();
            double setup = timer.Elapsed.TotalSeconds;
            for (int i = 0; i < 30; i++)
                await NextDraw();
            if (sculpt)
            {
                Screenshot(large ? "benchmark-sculpt-large.png" : "benchmark-sculpt.png");
                SetLayer(1);
                _layerSelect.Select(1);
                await NextDraw();
                Screenshot(large ? "benchmark-sculpt-elevation-large.png" : "benchmark-sculpt-elevation.png");
                SetLayer(0);
                _layerSelect.Select(0);
                if (!large)
                {
                    ShowDetail(FindDetailCenter(world), 24);
                    await NextDraw();
                    Screenshot("benchmark-sculpt-icons.png");
                    if (shared >= 0)
                    {
                        var hex = new Vector2I(shared % world.Width, shared / world.Width);
                        ShowDetail(hex, 42);
                        SelectHex(hex);
                        await NextDraw();
                        Screenshot("benchmark-sculpt-shared-icons.png");
                        SelectHex(new Vector2I(-1, -1));
                    }
                    int shore = Array.IndexOf(world.Biomes, BiomeType.Beach, world.Count * 2 / 5);
                    if (shore >= 0)
                    {
                        ShowDetail(new Vector2I(shore % world.Width, shore / world.Width), 18);
                        await NextDraw();
                        Screenshot("benchmark-sculpt-shore.png");
                    }
                }
            }
            else if (!large)
            {
                Screenshot("benchmark-m0.png");
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
            else
            {
                Screenshot("benchmark-large.png");
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

    private static void VerifyRegionalGeneration()
    {
        WorldManifest manifest = TestManifest(193, 223, TerrainAlgorithm.Fractal);
        var reports = new List<GenerationProgress>();
        WorldData reference = WorldGenerator.Create(manifest, reports.Add);
        GenerationStep[] steps = Enum.GetValues<GenerationStep>();
        Require(reports.Select(report => report.Step).Distinct().SequenceEqual(steps), "Ordem de geração incorreta.");
        foreach (GenerationStep step in steps)
        {
            int[] percentages = reports.Where(report => report.Step == step).Select(report => report.Percent).ToArray();
            Require(percentages[0] == 0 && percentages[^1] == 100 && percentages.SequenceEqual(percentages.Order()),
                $"Percentual inválido na etapa {step}.");
        }

        Require(reference.Chunks.Columns == 7 && reference.Chunks.Rows == 7 &&
            reference.Chunks.GetChunk(reference.Chunks.Count - 1) == new WorldRegion(192, 192, 1, 31),
            "A grade não inclui os chunks parciais.");
        var world = WorldGenerator.Create(manifest);
        // Verify the first regional operation on a world without cached sea-level calibration.
        world.GeneratedSeaLevel = float.NaN;
        int transitions = VerifyRegion(reference, world, new WorldRegion(29, 25, 75, 79));
        transitions += VerifyRegion(reference, world, reference.Chunks.GetChunk(10));
        transitions += VerifyRegion(reference, world, reference.Chunks.GetChunk(reference.Chunks.Count - 1));
        Require(transitions > 0, "As regiões de teste precisam cruzar transições entre biomas terrestres.");

        WorldManifest wrapped = manifest with { HorizontalWrap = true, GeneratorVersion = 4,
            Generation = manifest.Generation with { Algorithm = TerrainAlgorithm.LandmassCenters } };
        WorldData wrapReference = WorldGenerator.Create(wrapped);
        WorldData wrapWorld = WorldGenerator.Create(wrapped);
        VerifyRegion(wrapReference, wrapWorld, new WorldRegion(0, 0, 14, 65));
        VerifyRegion(wrapReference, wrapWorld, new WorldRegion(185, 158, 8, 65));

        foreach (GenerationStage stage in new[] { GenerationStage.Land, GenerationStage.Relief })
        {
            var partial = manifest with { Generation = manifest.Generation with { Stage = stage } };
            VerifyRegion(WorldGenerator.Create(partial), WorldGenerator.Create(partial), new WorldRegion(28, 27, 37, 34));
        }
    }

    private static void VerifyContinentalSculpture(string path)
    {
        WorldManifest manifest = TestManifest(512, 256, TerrainAlgorithm.ContinentalSculpt);
        var reports = new List<GenerationProgress>();
        WorldData reference = WorldGenerator.Create(manifest, reports.Add);
        int target = (int)((long)reference.Count * manifest.Generation.LandPercent / 100);
        int actual = reference.Water.Count(type => type == WaterType.None);
        Require(Math.Abs(actual - target) <= reference.Count / 500, "A escultura não atingiu a área de terra alvo.");
        Require(reports.First() == new GenerationProgress(GenerationStep.Land, 0)
            && reports.Last() == new GenerationProgress(GenerationStep.Biomes, 100), "Progresso incompleto na escultura.");
        var landOnly = manifest with { Generation = manifest.Generation with { Stage = GenerationStage.Land } };
        Require(reference.Water.SequenceEqual(WorldGenerator.Create(landOnly).Water),
            "A máscara de terra depende da etapa de geração solicitada.");

        Require(CountCoastalTips(reference) is >= 8 and <= 12,
            "A costa da seed de teste deve manter entre 8 e 12 pontas de um hex.");
        foreach (int seed in new[] { 2, 9917 })
        {
            int tips = CountCoastalTips(WorldGenerator.Create(landOnly with { Seed = seed }));
            Require(tips is >= 8 and <= 12, $"A costa da seed {seed} tem {tips} pontas de um hex.");
        }

        Require(HasConnectedFeature(reference, i => reference.Relief[i] >= ReliefType.Mountains, 250),
            "O mapa de teste precisa conter uma cadeia de montanhas contínua.");
        // Esta seed forma um estreito com fundo raso entre duas massas continentais.
        Require(HasConnectedFeature(reference, i => reference.Water[i] == WaterType.Ocean &&
            reference.Elevation[i] == WorldGenerator.SculptedStraitDepth, 80),
            "A calibração do nível do mar precisa manter o canal contínuo.");

        WorldManifest saved = WorldFile.Save(path, reference, null);
        WorldData loaded = WorldFile.Load(path);
        Require(loaded.Manifest == saved, "O manifesto perdeu o algoritmo de escultura.");
        VerifyRegion(reference, loaded, new WorldRegion(119, 59, 137, 134));

        WorldManifest wrapped = TestManifest(193, 95, TerrainAlgorithm.ContinentalSculpt) with { HorizontalWrap = true };
        WorldData wrappedReference = WorldGenerator.Create(wrapped);
        WorldData regenerated = WorldGenerator.Create(wrapped);
        VerifyRegion(wrappedReference, regenerated, new WorldRegion(0, 29, 40, 49));
        VerifyRegion(wrappedReference, regenerated, new WorldRegion(183, 30, 10, 65));
    }

    private static void VerifyShorelineFinish()
    {
        const int size = 32;
        int Cell(int column, int row) => row * size + column;
        var pockets = new float[size * size];
        Array.Fill(pockets, 0.3f);
        pockets[Cell(5, 5)] = -0.02f;
        pockets[Cell(6, 5)] = -0.03f;
        pockets[Cell(15, 8)] = -0.20f;
        pockets[Cell(16, 8)] = -0.01f;
        pockets[Cell(18, 18)] = float.NegativeInfinity;
        pockets[Cell(19, 18)] = -0.01f;
        for (int row = 0; row < 3; row++)
            pockets[Cell(24, row)] = -0.01f;

        WorldGenerator.FinishShoreline(pockets, size, size, false, 0f);
        Require(pockets[Cell(5, 5)] > 0 && pockets[Cell(6, 5)] > 0, "A poça rasa isolada deve fechar.");
        Require(pockets[Cell(15, 8)] < 0 && pockets[Cell(16, 8)] < 0 &&
            float.IsNegativeInfinity(pockets[Cell(18, 18)]) && pockets[Cell(19, 18)] < 0 && pockets[Cell(24, 2)] < 0,
            "Água profunda, canal protegido ou margem aberta foram preenchidos.");

        var coast = new float[size * size];
        Array.Fill(coast, -0.3f);
        for (int row = 8; row <= 16; row++)
        for (int column = 18; column <= 25; column++)
            coast[Cell(column, row)] = 0.3f;
        for (int column = 13; column <= 17; column++)
            coast[Cell(column, 12)] = 0.04f;
        coast[Cell(7, 6)] = 0.03f;
        coast[Cell(8, 6)] = 0.04f;
        for (int column = 8; column <= 19; column++)
        {
            coast[Cell(column, 22)] = 0.04f;
            coast[Cell(column, 23)] = 0.05f;
        }
        for (int row = 26; row <= 28; row++)
        for (int column = 8; column <= 12; column++)
            coast[Cell(column, row)] = 0.04f;

        WorldGenerator.FinishShoreline(coast, size, size, false, 0f);
        Require(coast[Cell(13, 12)] < 0 && coast[Cell(14, 12)] < 0 && coast[Cell(17, 12)] > 0,
            "O esporão deve encurtar preservando a base.");
        Require(coast[Cell(7, 6)] > 0 && coast[Cell(8, 6)] > 0 &&
            coast[Cell(18, 12)] == 0.3f, "A ilhota e a costa larga devem ser preservadas.");
        Require(coast[Cell(8, 22)] < 0 && coast[Cell(19, 23)] < 0 && coast[Cell(10, 27)] > 0,
            "A fita rasa deve desaparecer e a ilha compacta deve ser preservada.");
    }

    private static void VerifyShallowInlets()
    {
        const int size = 40;
        int Cell(int column, int row) => row * size + column;
        var coast = new float[size * size];
        Array.Fill(coast, 0.26f);
        for (int row = 0; row < size; row++)
        for (int col = 20; col < size; col++)
            coast[Cell(col, row)] = -0.3f;
        for (int col = 10; col < 20; col++)
        {
            coast[Cell(col, 5)] = -0.03f;
            coast[Cell(col, 12)] = -0.04f;
            coast[Cell(col, 13)] = -0.04f;
            coast[Cell(col, 20)] = -0.03f;
            coast[Cell(col, 25)] = float.NegativeInfinity;
        }
        for (int col = 0; col < 20; col++)
            coast[Cell(col, 30)] = -0.03f;
        coast[Cell(10, 20)] = -0.2f;

        WorldGenerator.RoundShallowInlets(coast, size, size, false, 0f);
        Require(coast[Cell(10, 5)] > 0f && coast[Cell(11, 5)] > 0f && coast[Cell(12, 5)] < 0f,
            "A reentrância rasa deve encurtar a partir do fundo.");
        Require(coast[Cell(10, 12)] > 0f && coast[Cell(10, 13)] > 0f && coast[Cell(17, 13)] < 0f,
            "A reentrância de dois hexes precisa manter a boca aberta.");
        Require(coast[Cell(10, 20)] < 0f && coast[Cell(11, 20)] < 0f &&
            float.IsNegativeInfinity(coast[Cell(10, 25)]) && coast[Cell(9, 30)] < 0f && coast[Cell(20, 5)] < 0f,
            "Água funda, canais protegidos, canais abertos e o mar devem permanecer submersos.");
    }

    private static void VerifyBiomeRibbonFinish()
    {
        const BiomeType forest = BiomeType.TemperateForest;
        const BiomeType field = BiomeType.Grassland;
        Require(WorldGenerator.FindBiomeRibbonReplacement(forest, [forest, field, field, forest, field, field],
            out int middle) == field && middle < 0, "Uma faixa diagonal deve ceder ao bioma dos dois lados.");
        Require(WorldGenerator.FindBiomeRibbonReplacement(forest, [forest, field, field, field, field, field],
            out int end) == field && end == 0, "A ponta precisa verificar a continuidade da faixa.");
        Require(WorldGenerator.FindBiomeRibbonReplacement(forest, [forest, forest, field, field, field, field],
            out _) == BiomeType.None, "Uma borda curva de floresta deve ser preservada.");
    }

    private static int CountCoastalTips(WorldData world)
    {
        int tips = 0;
        for (int i = 0; i < world.Count; i++)
        {
            if ((world.Flags[i] & WorldData.CoastFlag) == 0)
                continue;
            Hex hex = HexGrid.FromOffset(i % world.Width, i / world.Width);
            int dryNeighbors = 0;
            for (int direction = 0; direction < 6; direction++)
            {
                int neighbor = HexGrid.NeighborIndex(hex, direction, world.Width, world.Height, world.Manifest.HorizontalWrap);
                if (neighbor >= 0 && world.Water[neighbor] == WaterType.None)
                    dryNeighbors++;
            }
            if (dryNeighbors <= 1)
                tips++;
        }
        return tips;
    }

    private static bool HasConnectedFeature(WorldData world, Func<int, bool> contains, int minimum)
    {
        var visited = new bool[world.Count];
        var frontier = new Queue<int>();
        for (int start = 0; start < world.Count; start++)
        {
            if (visited[start] || !contains(start))
                continue;
            visited[start] = true;
            frontier.Enqueue(start);
            int length = 0;
            while (frontier.TryDequeue(out int index))
            {
                if (++length >= minimum)
                    return true;
                Hex hex = HexGrid.FromOffset(index % world.Width, index / world.Width);
                for (int direction = 0; direction < 6; direction++)
                {
                    int neighbor = HexGrid.NeighborIndex(hex, direction, world.Width, world.Height, world.Manifest.HorizontalWrap);
                    if (neighbor < 0 || visited[neighbor] || !contains(neighbor))
                        continue;
                    visited[neighbor] = true;
                    frontier.Enqueue(neighbor);
                }
            }
        }
        return false;
    }

    private static int VerifyRegion(WorldData reference, WorldData world, WorldRegion region)
    {
        bool Inside(int col, int row) => col >= region.Column && col < region.Column + region.Width
            && row >= region.Row && row < region.Row + region.Height;
        GenerationStage stage = world.Manifest.Generation.Stage;
        int outside = region.Column == 0 ? world.Width - 1 : 0;
        world.Elevation[outside] = short.MaxValue;
        for (int row = region.Row; row < region.Row + region.Height; row++)
        for (int col = region.Column; col < region.Column + region.Width; col++)
        {
            int index = row * world.Width + col;
            world.Elevation[index] = short.MinValue;
            world.Water[index] = WaterType.Lake;
            if (stage >= GenerationStage.Relief)
                world.Relief[index] = (ReliefType)byte.MaxValue;
            if (stage == GenerationStage.Terrain)
            {
                world.Biomes[index] = (BiomeType)byte.MaxValue;
                world.Temperature[index] = short.MinValue;
                world.Rainfall[index] = ushort.MaxValue;
            }
            world.Flags[index] ^= WorldData.CoastFlag;
            world.Flags[index] |= 2;
            world.RoadEdges[index] = 3;
        }

        WorldGenerator.RegenerateRegion(world, region);
        int transitions = 0;
        for (int index = 0; index < world.Count; index++)
        {
            int col = index % world.Width;
            int row = index / world.Width;
            bool selected = Inside(col, row);
            Require(world.Elevation[index] == (index == outside ? short.MaxValue : reference.Elevation[index])
                && world.Water[index] == reference.Water[index]
                && world.Relief[index] == reference.Relief[index]
                && world.Biomes[index] == reference.Biomes[index]
                && world.Temperature[index] == reference.Temperature[index]
                && world.Rainfall[index] == reference.Rainfall[index]
                && world.Flags[index] == (selected ? reference.Flags[index] | 2 : reference.Flags[index])
                && world.RoadEdges[index] == (selected ? 3 : 0), $"Emenda ou escrita indevida em ({col}, {row}).");

            if (selected && world.Water[index] == WaterType.None)
            {
                Hex hex = HexGrid.FromOffset(col, row);
                for (int direction = 0; direction < 6; direction++)
                {
                    int neighbor = HexGrid.NeighborIndex(hex, direction, world.Width, world.Height, world.Manifest.HorizontalWrap);
                    if (neighbor >= 0 && !Inside(neighbor % world.Width, neighbor / world.Width) &&
                        world.Water[neighbor] == WaterType.None && world.Biomes[index] != world.Biomes[neighbor])
                        transitions++;
                }
            }
            if (selected)
            {
                world.Flags[index] = reference.Flags[index];
                world.RoadEdges[index] = 0;
            }
        }
        world.Elevation[outside] = reference.Elevation[outside];
        return transitions;
    }

    private static void VerifyDetailIcons(WorldData world)
    {
        GlobalDataTextures.Buffers data = GlobalDataTextures.BuildBuffers(world);
        Require(data.Details.AsSpan().SequenceEqual(MapDetailIcons.Build(world, data.Terrain)),
            "O posicionamento dos ícones deve reproduzir a seed.");
        var used = new HashSet<byte>();
        int primary = 0;
        int close = 0;
        for (int i = 0; i < data.Details.Length; i++)
        {
            byte packed = data.Details[i];
            byte icon = (byte)(packed & MapDetailIcons.IconIdMask);
            if (icon == 0)
            {
                Require(packed == 0, "Ícone ausente com estado de zoom.");
                continue;
            }
            Require(icon <= MapDetailIcons.FileNames.Length, "Ícone inválido.");
            if ((packed & MapDetailIcons.CloseZoomFlag) != 0)
                close++;
            else
            {
                primary++;
                Require((data.Terrain[i * 4 + 3] & 0xc0) == 0, "A vista afastada precisa manter o hex livre.");
            }
            used.Add(icon);
        }
        Require(used.Count >= 16, "O mapa de teste precisa mostrar variedade de ícones.");
        Require(primary > 0 && close > 0 && FindSharedDetail(data) >= 0,
            "O zoom deve revelar mais ícones, inclusive em hexes com relevo e vegetação.");
    }

    private static int FindSharedDetail(GlobalDataTextures.Buffers data)
    {
        for (int i = 0; i < data.Details.Length; i++)
        {
            if ((data.Details[i] & MapDetailIcons.CloseZoomFlag) != 0 && (data.Terrain[i * 4 + 3] & 0xc0) == 0xc0)
                return i;
        }
        return -1;
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
