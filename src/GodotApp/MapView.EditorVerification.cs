using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Mapmaker.Core;

namespace Mapmaker.GodotApp;

public partial class MapView
{
    private async Task MeasureEditorPreview(WorldData source)
    {
        InstallWorld(source);
        RefreshInterface();
        SetEditorPanelOpen(true);
        SetLayer(0);
        FitMap();
        Descendants(_editorPanel).OfType<SpinBox>().First(box => box.Name == "BrushRadius").Value = 6;
        Button raise = Descendants(_editorPanel).OfType<Button>().First(button => button.Text == "Subir");
        raise.EmitSignal(BaseButton.SignalName.Pressed);
        Vector2 At(int column)
        {
            var center = HexGrid.Center(HexGrid.FromOffset(column, source.Height / 2));
            return _map.ToGlobal(new Vector2(center.X, center.Y));
        }

        for (int frame = 0; frame < 15; frame++)
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var times = new double[60];
        int start = source.Width / 4;
        StartMapEdit(At(start));
        for (int i = 0; i < times.Length; i++)
        {
            var timer = Stopwatch.StartNew();
            ContinueGesture(At(start + i * 3));
            _editorPreview!.Refresh(1.0 / 30);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            times[i] = timer.Elapsed.TotalMilliseconds;
        }
        FinishGesture();
        Array.Sort(times);
        GD.Print($"Prévia de edição em {source.Width}×{source.Height}, 60 amostras com pincel ativo. " +
            $"Mediana {times[times.Length / 2]:F1} ms, p95 {times[56]:F1} ms até o fim do frame.");
    }

    private async Task VerifyEditing(string path, int generatorVersion = 17)
    {
        WorldManifest manifest = TestManifest(128, 64, TerrainAlgorithm.ContinentalSculpt) with { GeneratorVersion = generatorVersion };
        WorldData world = WorldGenerator.Create(manifest);
        var region = new WorldRegion(29, 29, 8, 8);
        foreach (int i in region.Cells(world.Width))
        {
            world.Water[i] = WaterType.None;
            world.Biomes[i] = BiomeType.Grassland;
            world.Elevation[i] = 500;
            world.Rainfall[i] = ushort.MaxValue;
        }
        world.RebuildCoast();
        InstallWorld(world);
        WorldData source = world;
        GlobalDataTextures? fullTextures = _textures;
        RefreshInterface();
        SetEditorPanelOpen(true);
        world = _world!;
        Require(!ReferenceEquals(world, source) && _editorPreview is not null, "Editar precisa abrir uma cópia com prévia leve.");
        FitMap();

        Button FindButton(string label) => Descendants(_editorPanel).OfType<Button>()
            .First(button => button is not OptionButton && button.Text == label);
        void Click(string label)
        {
            Button button = FindButton(label);
            Require(!button.Disabled, $"{label} deveria estar habilitado.");
            if (button.ToggleMode)
                button.ButtonPressed = true;
            button.EmitSignal(BaseButton.SignalName.Pressed);
        }
        Vector2 PositionAt(int column, int row)
        {
            var p = HexGrid.Center(HexGrid.FromOffset(column, row));
            return _map.ToGlobal(new Vector2(p.X, p.Y));
        }
        void Drag(Vector2 start, Vector2 end)
        {
            _UnhandledInput(new InputEventMouseButton { Position = start, ButtonIndex = MouseButton.Left, Pressed = true });
            _Input(new InputEventMouseMotion { Position = end, ButtonMask = MouseButtonMask.Left });
            _Input(new InputEventMouseButton { Position = end, ButtonIndex = MouseButton.Left, Pressed = false });
        }
        var initial = ReadDense(world);
        _editorPanel.ShowPage(2);
        Drag(PositionAt(36, 36), PositionAt(29, 29));
        Require(_selection == region && _selected == NoHex, "O arrasto precisa selecionar o retângulo em quatro chunks.");
        SetLayer(0);
        bool rendered = DisplayServer.GetName() != "headless";
        Vector2I paintPixel = (Vector2I)PositionAt(32, 32);
        Color beforePaint = default;
        if (rendered)
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using Image frame = GetViewport().GetTexture().GetImage();
            beforePaint = frame.GetPixel(paintPixel.X, paintPixel.Y);
        }
        Click("Bioma");
        Descendants(_editorPanel).OfType<SpinBox>().First(box => box.Name == "BrushRadius").Value = 2;
        Descendants(_editorPanel).OfType<SpinBox>().First(box => box.Name == "BrushMeters").Value = 100;
        Descendants(_editorPanel).OfType<OptionButton>().First(option => option.Name == "BrushShape").Select((int)BrushShape.Hexagon);
        Descendants(_editorPanel).OfType<HSlider>().Last().Value = 30;
        Click("Floresta");
        Require(_selection is null, "Terraformar precisa abrir com pintura livre.");
        SetSelection(region);
        Vector2 first = PositionAt(29, 32);
        Vector2 last = PositionAt(36, 32);
        _UnhandledInput(new InputEventMouseButton { Position = first, ButtonIndex = MouseButton.Left, Pressed = true });
        _Input(new InputEventMouseMotion { Position = last, ButtonMask = MouseButtonMask.Left });
        if (rendered)
        {
            _editorPreview!.Refresh(1.0 / 30);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using Image frame = GetViewport().GetTexture().GetImage();
            Require(beforePaint.R > frame.GetPixel(paintPixel.X, paintPixel.Y).R + 0.07f,
                "A prévia precisa mostrar a pintura antes de soltar o botão.");
            if (OS.GetCmdlineUserArgs().Contains("--capture-editor"))
                frame.SavePng($"res://.godot/editor-preview-{frame.GetWidth()}x{frame.GetHeight()}.png");
        }
        _Input(new InputEventMouseButton { Position = last, ButtonIndex = MouseButton.Left, Pressed = false });
        var painted = ReadDense(world);
        Require(ReadDense(source).SequenceEqual(initial) && ReferenceEquals(_textures, fullTextures),
            "Pintar na prévia deve manter intactos o mapa original e suas texturas completas.");
        Require(_history!.Revision == 1 && world.Biomes[32 * world.Width + 32] == BiomeType.TemperateForest &&
            painted.Where((cell, i) => cell.Biome != initial[i].Biome).Count() >= 8,
            "A pincelada deve ser contínua, cruzar chunks e entrar no histórico uma vez.");
        EditHistory(false);
        Require(ReadDense(world).SequenceEqual(initial), "Desfazer deve restaurar a pincelada inteira.");
        EditHistory(true);
        Require(ReadDense(world).SequenceEqual(painted), "Refazer deve restaurar a pincelada inteira.");

        Click("Subir");
        SetSelection(region);
        Drag(PositionAt(29, 32), PositionAt(36, 32));
        Require(world.Elevation[32 * world.Width + 32] == 600, "Subir deve agir uma vez por hex na pincelada.");
        EditHistory(false);
        Require(ReadDense(world).SequenceEqual(painted), "Desfazer elevação deve restaurar os chunks.");

        SpinBox[] numeric = { Descendants(_editorPanel).OfType<SpinBox>().First(box => box.Name == "BrushRadius"), Descendants(_editorPanel).OfType<SpinBox>().First(box => box.Name == "BrushMeters") };
        numeric[0].Value = 0;
        int center = 32 * world.Width + 32;
        Vector2 point = PositionAt(32, 32);
        Click("Baixar");
        Drag(point, point);
        Require(world.Elevation[center] == 400, "Baixar deve subtrair a quantidade em metros.");
        Click("Definir");
        numeric[1].Value = -120;
        Drag(point, point);
        Require(world.Elevation[center] == -120, "Definir deve aceitar uma altitude negativa.");
        Click("Suavizar");
        Drag(point, point);
        Require(world.Elevation[center] == 411, "Suavizar deve usar a média com os vizinhos.");
        Click("Nivelar");
        Drag(point, PositionAt(33, 32));
        Require(world.Elevation[center + 1] == 411, "Nivelar deve copiar a altitude inicial.");
        for (int i = 0; i < 4; i++)
            EditHistory(false);

        HSlider strength = Descendants(_editorPanel).OfType<HSlider>().Last();
        numeric[0].Value = 2;
        strength.Value = 0;
        Click("Subir");
        numeric[1].Value = 100;
        Drag(point, point);
        Require(world.Elevation[center] == 600 && world.Elevation[center + 1] == 550 &&
            world.Elevation[center + 2] == 500, "A força deve cair do centro até a borda.");
        EditHistory(false);
        numeric[0].Value = 3;
        strength.Value = 100;
        OptionButton shape = Descendants(_editorPanel).OfType<OptionButton>().First(option => option.Name == "BrushShape");
        int corner = 35 * world.Width + 34;
        shape.Select((int)BrushShape.Square);
        Drag(point, point);
        Require(world.Elevation[corner] == 600, "O formato quadrado precisa alcançar seus cantos.");
        EditHistory(false);
        shape.Select((int)BrushShape.Diamond);
        Drag(point, point);
        Require(world.Elevation[center] == 600 && world.Elevation[corner] == 500, "O losango precisa recortar os cantos.");
        EditHistory(false);
        numeric[0].Value = 0;
        strength.Value = 30;
        shape.Select((int)BrushShape.Hexagon);

        Click("Relevo");
        OptionButton brushValue = Descendants(_editorPanel).OfType<OptionButton>().First(option => option.Name == "BrushValue");
        brushValue.Select(brushValue.GetItemIndex((int)ReliefType.Peak));
        Drag(point, point);
        Require(world.Relief[center] == ReliefType.Peak, "O pincel de relevo deve aplicar a opção escolhida.");
        Click("Água");
        brushValue.Select(brushValue.GetItemIndex((int)WaterType.Ocean));
        Drag(point, point);
        Require(world.Water[center] == WaterType.Ocean && world.Water[center + 1] == WaterType.None,
            "Raio zero deve pintar água somente no hex apontado.");
        EditHistory(false);
        EditHistory(false);
        Require(ReadDense(world).SequenceEqual(painted), "Desfazer relevo e água deve restaurar os chunks.");

        _editorPanel.ShowPage(0);
        Drag(point, point);
        int oldBiome = (int)world.Biomes[center];
        EditSingleHex(HexLocks.Biome, (int)BiomeType.Desert);
        Require(world.Biomes[center] == BiomeType.Desert && _selected == new Vector2I(32, 32),
            "Editar precisa alterar somente o hex inspecionado.");
        EditHistory(false);
        Require((int)world.Biomes[center] == oldBiome, "A edição individual precisa entrar no histórico.");
        Vector2I locked = new(31, 31);
        int lockedCell = locked.Y * world.Width + locked.X;
        Drag(PositionAt(locked.X, locked.Y), PositionAt(locked.X, locked.Y));
        var biomeLock = Descendants(_editorPanel).OfType<CheckBox>().First(box => box.Text == "Bioma");
        _editorPanel.ShowPage(2);
        biomeLock.ButtonPressed = true;
        Require(world.LockMask[lockedCell] == (ushort)HexLocks.Biome && _inspection.Text.Contains("Locks: Bioma"),
            "O lock por campo precisa aparecer no inspetor.");
        EditHistory(false);
        Require(world.LockMask[lockedCell] == 0, "O lock precisa entrar no histórico.");
        EditHistory(true);
        Require(world.LockMask[lockedCell] == (ushort)HexLocks.Biome, "Refazer precisa restaurar o lock.");

        _editorPanel.ShowPage(2);
        Drag(PositionAt(29, 29), PositionAt(36, 36));
        if (rendered)
        {
            Click("Bioma");
            Require(Descendants(_editorPanel).OfType<Label>().Any(label => label.Visible && label.Text == "Floresta temperada"),
                "A paleta precisa identificar o bioma escolhido.");
            _layerSelect.Select(5);
            SetLayer(5);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using Image frame = GetViewport().GetTexture().GetImage();
            Vector2I lockedPixel = (Vector2I)PositionAt(locked.X, locked.Y);
            Vector2I unlockedPixel = (Vector2I)PositionAt(locked.X + 2, locked.Y + 2);
            Color lockedColor = frame.GetPixel(lockedPixel.X, lockedPixel.Y);
            Color unlockedColor = frame.GetPixel(unlockedPixel.X, unlockedPixel.Y);
            Require(lockedColor.R > unlockedColor.R + 0.15f, "O shader deve diferenciar hexes travados na camada de locks.");
            if (OS.GetCmdlineUserArgs().Contains("--capture-editor"))
                frame.SavePng($"res://.godot/m3-editor-{frame.GetWidth()}x{frame.GetHeight()}.png");
            if (OS.GetCmdlineUserArgs().Contains("--capture-editor"))
            {
                for (int page = 0; page < 4; page++)
                {
                    _editorPanel.ShowPage(page);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    using Image pageFrame = GetViewport().GetTexture().GetImage();
                    pageFrame.SavePng($"res://.godot/editor-page-{page}-{pageFrame.GetWidth()}x{pageFrame.GetHeight()}.png");
                }
            }
            _layerSelect.Select(0);
            SetLayer(0);
        }
        _editorPanel.ShowPage(3);
        Drag(PositionAt(29, 29), PositionAt(36, 36));
        Require(_selection == region && _editorPanel.RegionTool, "Gerar precisa usar seleção regional por padrão.");
        var before = ReadDense(world);
        await RegenerateAsync(region, RegenerationMode.KeepWaterAndElevation, HexLocks.None);
        var after = ReadDense(world);
        Require(_editorPanel.Visible && _selection == region && _dirty, "Regenerar precisa manter o editor e a seleção.");
        Require(after[lockedCell].Biome == before[lockedCell].Biome &&
            after[lockedCell].Locks == before[lockedCell].Locks &&
            after[lockedCell].Rainfall != before[lockedCell].Rainfall, "Regenerar precisa preservar apenas o campo travado.");
        for (int i = 0; i < world.Count; i++)
        {
            if (!region.Contains(i % world.Width, i / world.Width))
                Require(after[i] == initial[i], "O fluxo de edição alterou dados fora da seleção.");
            else
                Require(after[i].Elevation == before[i].Elevation && after[i].Water == before[i].Water &&
                    after[i].Relief == before[i].Relief, "O modo com elevação deve manter água e relevo.");
        }
        EditHistory(false);
        Require(ReadDense(world).SequenceEqual(before), "Desfazer regeneração precisa restaurar todos os campos.");
        EditHistory(true);
        Require(ReadDense(world).SequenceEqual(after), "Refazer regeneração precisa restaurar todos os campos.");

        _currentPath = path;
        RequestEditorFileAction(2);
        Require(_editorConfirmation.Visible, "Cancelar deve pedir confirmação.");
        _editorConfirmation.EmitSignal(ConfirmationDialog.SignalName.Canceled);
        _editorConfirmation.Hide();
        Require(_editorPanel.Visible && ReferenceEquals(_world, world), "Responder Não deve manter a sessão de edição aberta.");
        var saveTimer = Stopwatch.StartNew();
        Task saving = FinishEditorAsync();
        Require(_saving && _busy && _editorPanel.Visible, "Salvar deve exibir progresso e manter a prévia até concluir.");
        if (rendered && OS.GetCmdlineUserArgs().Contains("--capture-editor"))
        {
            await Task.Delay(1200);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using Image progress = GetViewport().GetTexture().GetImage();
            progress.SavePng($"res://.godot/editor-saving-{progress.GetWidth()}x{progress.GetHeight()}.png");
        }
        await saving;
        Require(saveTimer.Elapsed.TotalSeconds >= 3 && !_saving, "Salvar precisa mostrar progresso por pelo menos três segundos.");
        Require(!_dirty && _editorPreview is null && !_editorPanel.Visible &&
            ReferenceEquals(_world, world) && !ReferenceEquals(_textures, fullTextures) && ReadDense(source).SequenceEqual(initial),
            "Terminar a edição deve aplicar e salvar a cópia, atualizar a renderização e preservar a fonte.");
        EditHistory(false);
        Require(_dirty, "Desfazer após salvar deve marcar a cópia como alterada.");
        EditHistory(true);
        Require(!_dirty, "Refazer até a revisão salva deve limpar o estado de alterações.");
        UndoManager savedHistory = _history!;
        int savedRevision = savedHistory.Revision;
        SetEditorPanelOpen(true);
        EditHistory(false);
        Require(_history!.Revision != savedRevision, "A sessão precisa exercitar desfazer antes do cancelamento.");
        RequestEditorFileAction(2);
        _editorConfirmation.EmitSignal(ConfirmationDialog.SignalName.Confirmed);
        _editorConfirmation.Hide();
        Require(ReferenceEquals(_world, world) && ReferenceEquals(_history, savedHistory) &&
            _history.Revision == savedRevision && !_dirty && !_editorPanel.Visible && ReadDense(world).SequenceEqual(after),
            "Cancelar precisa restaurar os dados e o histórico anteriores à sessão.");

        string copyPath = path + ".copy.world";
        try
        {
            SetEditorPanelOpen(true);
            SelectHex(new Vector2I(33, 33));
            EditSingleHex(HexLocks.Rainfall, 123);
            RequestEditorFileAction(1);
            _editorConfirmation.EmitSignal(ConfirmationDialog.SignalName.Confirmed);
            _editorConfirmation.Hide();
            Require(_saveDialog.Visible && _saveAsNewFile, "Salvar como precisa abrir a escolha de um novo arquivo.");
            _saveDialog.Hide();
            OnSaveFileSelected(path);
            Require(_editorPreview is not null && !_busy && _message.Visible, "Salvar como deve exigir outro caminho.");
            _message.Hide();
            _saveAsNewFile = false;
            await SaveWorldAsync(copyPath);
            Require(_currentPath == copyPath && WorldFile.Load(copyPath).Rainfall[33 * world.Width + 33] == 123 &&
                ReadDense(WorldFile.Load(path)).SequenceEqual(after), "Salvar como precisa criar outro arquivo e conservar o original.");
        }
        finally
        {
            System.IO.File.Delete(copyPath);
        }
        await OpenWorldAsync(path);
        Require(ReadDense(_world!).SequenceEqual(after) && !_history!.CanUndo && !_dirty,
            "Reabrir deve restaurar as edições e iniciar outro histórico.");

        WorldData modes = WorldGenerator.Create(manifest);
        int sample = 33 * modes.Width + 33;
        var single = new WorldRegion(33, 33, 1, 1);
        modes.Water[sample] = WaterType.Lake;
        modes.Elevation[sample] = short.MaxValue;
        modes.Rainfall[sample] = ushort.MaxValue;
        var singleBefore = ReadDense(modes);
        WorldGenerator.RegenerateRegion(modes, single, RegenerationMode.Everything, HexLocks.Rainfall);
        var singleAfter = ReadDense(modes);
        Require(singleAfter[sample].Rainfall != ushort.MaxValue &&
            singleAfter[sample] with { Rainfall = ushort.MaxValue } == singleBefore[sample] &&
            singleAfter.Where((cell, i) => i != sample && cell != singleBefore[i]).Count() == 0,
            "Regenerar campo deve alterar somente o campo escolhido.");
        WorldGenerator.RegenerateRegion(modes, single, RegenerationMode.KeepWater);
        Require(modes.Water[sample] == WaterType.Lake && modes.Elevation[sample] != short.MaxValue,
            "Manter terra/água deve preservar lagos e recalcular elevação.");
        modes.LockMask[sample] = (ushort)HexLocks.All;
        var fullBefore = ReadDense(modes);
        WorldGenerator.RegenerateRegion(modes, new WorldRegion(0, 0, modes.Width, modes.Height), RegenerationMode.Everything);
        var fullAfter = ReadDense(modes);
        Require(fullBefore[sample] == fullAfter[sample], "Regenerar mundo precisa preservar um hex travado.");
    }

    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            yield return child;
            foreach (Node descendant in Descendants(child))
                yield return descendant;
        }
    }

    private readonly record struct DenseCell(short Elevation, WaterType Water, ReliefType Relief, BiomeType Biome,
        short Temperature, ushort Rainfall, ushort Roads, ushort Rivers, byte Flags, ushort Locks);

    private static DenseCell[] ReadDense(WorldData world) =>
        Enumerable.Range(0, world.Count).Select(i => new DenseCell(world.Elevation[i], world.Water[i], world.Relief[i],
            world.Biomes[i], world.Temperature[i], world.Rainfall[i], world.RoadEdges[i], world.RiverEdges[i],
            world.Flags[i], world.LockMask[i])).ToArray();
}
