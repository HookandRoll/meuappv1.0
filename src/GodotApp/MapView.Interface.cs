using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Mapmaker.Core;

namespace Mapmaker.GodotApp;

public partial class MapView
{
    private enum MapAction { New = 100, Open, Recent, Save, SaveAs, Generate }
    private const int ProgressValuesPerStep = 101;
    private static string ProjectName => ProjectSettings.GetSetting("application/config/name").AsString();

    private Control _emptyState = null!;
    private Control _mapControls = null!;
    private Control _footer = null!;
    private Label _emptyStatus = null!;
    private Label _title = null!;
    private Button _newButton = null!;
    private Button _openButton = null!;
    private MenuButton _mapMenu = null!;
    private Button _editorToggle = null!;
    private MapEditorPanel _editorPanel = null!;
    private CheckButton _gridToggle = null!;
    private WorldCreationDialog _creation = null!;
    private FileDialog _openDialog = null!;
    private FileDialog _saveDialog = null!;
    private ConfirmationDialog _discardDialog = null!;
    private AcceptDialog _message = null!;
    private Control _generationStatus = null!;
    private Label _generationStep = null!;
    private ProgressBar _generationBar = null!;
    private int _reportedProgress;
    private Action? _afterDiscard;
    private string? _currentPath;
    private string? _recentPath;
    private string _preferencesPath = "user://mapmaker.cfg";
    private bool _dirty;
    private bool _busy;
    private bool _saveAsNewFile;

    private void BuildInterface()
    {
        var overlay = new CanvasLayer();
        AddChild(overlay);
        var hud = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        overlay.AddChild(hud);
        hud.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        BuildMenus(AddBar(hud, Control.LayoutPreset.TopWide, 18, 74));
        BuildStartPanel(hud);

        HBoxContainer footer = AddBar(hud, Control.LayoutPreset.BottomWide, -65, -18);
        _footer = (Control)footer.GetParent();
        _inspection = AddLabel(footer, "", 14, Ink);
        _inspection.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _inspection.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _performance = AddLabel(footer, "", 14, Ink);

        HBoxContainer generation = AddBar(hud, Control.LayoutPreset.BottomWide, -65, -18);
        _generationStatus = (Control)generation.GetParent();
        _generationStep = AddLabel(generation, "", 14, Ink);
        _generationStep.CustomMinimumSize = new Vector2(176, 0);
        _generationBar = new ProgressBar { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        generation.AddChild(_generationBar);
        _generationStatus.Hide();
        BuildEditorPanel(hud);

        _creation = new WorldCreationDialog();
        _creation.CreateRequested = (manifest, fillingBlank) => _ = CreateWorldAsync(manifest, fillingBlank);
        AddChild(_creation);
        _openDialog = MakeFileDialog("Abrir mapa", FileDialog.FileModeEnum.OpenFile);
        _openDialog.FileSelected += path => _ = OpenWorldAsync(path);
        _saveDialog = MakeFileDialog("Salvar mapa como", FileDialog.FileModeEnum.SaveFile);
        _saveDialog.FileSelected += OnSaveFileSelected;
        _message = new AcceptDialog { DialogAutowrap = true };
        AddChild(_message);
        _discardDialog = new ConfirmationDialog { Title = "Alterações sem salvar", DialogText = "O mapa tem alterações sem salvar. Descartar as alterações e continuar?" };
        AddChild(_discardDialog);
        _discardDialog.GetOkButton().Text = "Descartar";
        _discardDialog.GetCancelButton().Text = "Voltar";
        _discardDialog.Confirmed += () =>
        {
            Action? next = _afterDiscard;
            _afterDiscard = null;
            next?.Invoke();
        };
        _discardDialog.Canceled += () => _afterDiscard = null;

        using var config = new ConfigFile();
        Error preferences = config.Load(_preferencesPath);
        if (preferences == Error.FileNotFound && Path.GetDirectoryName(OS.GetUserDataDir()) is string mapsDirectory)
            preferences = config.Load(Path.Combine(mapsDirectory, "World Simulation RPG", "mapmaker.cfg"));
        if (preferences == Error.Ok)
            _recentPath = config.GetValue("maps", "recent", "").AsString();
        RefreshInterface();
    }

    private void BuildMenus(HBoxContainer header)
    {
        _mapMenu = new MenuButton { Text = "Mapa" };
        PopupMenu maps = _mapMenu.GetPopup();
        AddMenuItem(maps, "Novo mapa…", (int)MapAction.New, Key.N);
        AddMenuItem(maps, "Abrir mapa…", (int)MapAction.Open, Key.O);
        maps.AddItem("Abrir último mapa", (int)MapAction.Recent);
        maps.AddSeparator();
        AddMenuItem(maps, "Salvar", (int)MapAction.Save, Key.S);
        AddMenuItem(maps, "Salvar como…", (int)MapAction.SaveAs, Key.S, shift: true);
        maps.AddSeparator();
        maps.AddItem("Gerar terreno…", (int)MapAction.Generate);
        maps.IdPressed += OnMapAction;
        header.AddChild(_mapMenu);

        var edit = new MenuButton { Text = "Editar" };
        PopupMenu menu = _editMenu = edit.GetPopup();
        AddMenuItem(menu, "Desfazer", 101, Key.Z, disabled: true);
        AddMenuItem(menu, "Refazer", 102, Key.Y, disabled: true);
        menu.AddSeparator();
        AddMenuItem(menu, "Recortar", 103, Key.X, disabled: true);
        AddMenuItem(menu, "Copiar", 104, Key.C, disabled: true);
        AddMenuItem(menu, "Colar", 105, Key.V, disabled: true);
        menu.AddSeparator();
        AddMenuItem(menu, "Regenerar área…", 106, Key.R, disabled: true);
        AddMenuItem(menu, "Regenerar mapa inteiro…", 107, Key.P, disabled: true);
        menu.IdPressed += id =>
        {
            switch ((EditAction)id)
            {
                case EditAction.Undo: EditHistory(false); break;
                case EditAction.Redo: EditHistory(true); break;
                case EditAction.RegenerateSelection:
                case EditAction.RegenerateWorld:
                    SetEditorPanelOpen(true);
                    if (_editorPanel.Visible)
                        _editorPanel.SelectRegenerationTarget(id == (int)EditAction.RegenerateWorld ? 1 : 0);
                    break;
            }
        };
        header.AddChild(edit);
        _editorToggle = new CheckButton { Text = "Editar mapa" };
        _editorToggle.AddThemeColorOverride("font_pressed_color", new Color("#edc66c"));
        _editorToggle.Toggled += SetEditorPanelOpen;
        header.AddChild(_editorToggle);

        _title = AddLabel(header, ProjectName, 18, Ink);
        _title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        var controls = new HBoxContainer();
        controls.AddThemeConstantOverride("separation", 14);
        _mapControls = controls;
        header.AddChild(controls);
        _layerSelect = new OptionButton { CustomMinimumSize = new Vector2(132, 36) };
        foreach (string layer in Layers)
            _layerSelect.AddItem(layer);
        _layerSelect.ItemSelected += index => SetLayer((int)index);
        controls.AddChild(_layerSelect);
        _gridToggle = new CheckButton { Text = "Grade" };
        _gridToggle.Toggled += enabled => _material.SetShaderParameter("show_grid", enabled);
        controls.AddChild(_gridToggle);
        var relief = new CheckButton { Text = "Relevo", ButtonPressed = true };
        relief.Toggled += enabled => _material.SetShaderParameter("show_relief", enabled);
        controls.AddChild(relief);
        var vegetation = new CheckButton { Text = "Vegetação", ButtonPressed = true };
        vegetation.Toggled += enabled => _material.SetShaderParameter("show_vegetation", enabled);
        controls.AddChild(vegetation);
        var details = new CheckButton { Text = "Ícones", ButtonPressed = true };
        details.Toggled += enabled => _material.SetShaderParameter("show_details", enabled);
        controls.AddChild(details);
        var fit = new Button { Text = "Centralizar" };
        fit.Pressed += FitMap;
        controls.AddChild(fit);
    }

    private void BuildEditorPanel(Control hud)
    {
        _editorPanel = new MapEditorPanel { Visible = false };
        _editorPanel.ClearRequested += () => SelectHex(new Vector2I(-1, -1));
        hud.AddChild(_editorPanel);
        BindEditor();
        hud.Resized += () => FitEditorLayout(hud.Size);
        _editorPanel.MinimumSizeChanged += () => FitEditorLayout(hud.Size);
        FitEditorLayout(hud.Size);
    }

    private void FitEditorLayout(Vector2 size)
    {
        bool compact = size.X < 1180;
        _mapControls.AddThemeConstantOverride("separation", compact ? 6 : 14);
        _title.AddThemeFontSizeOverride("font_size", compact ? 15 : 18);
        _editorPanel.FitIn(size);
        UpdateMapCanvas();
    }

    private void SetEditorPanelOpen(bool open)
    {
        if (_busy || _world is not WorldData world || _editorPanel.Visible == open)
        {
            _editorToggle.SetPressedNoSignal(_editorPanel.Visible);
            return;
        }
        if (open)
        {
            StartEditorDraft(world);
            SetEditorPanelVisible(true);
        }
        else
        {
            _editorToggle.SetPressedNoSignal(true);
            RequestEditorFileAction(2);
        }
    }

    private void SetEditorPanelVisible(bool open)
    {
        _editorToggle.SetPressedNoSignal(open);
        if (_editorPanel.Visible == open)
            return;
        Vector2 previousCenter = MapCanvasRect().GetCenter();
        if (open)
            _editorPanel.FitIn(GetViewportRect().Size);
        if (!open)
        {
            FinishGesture();
            _material.SetShaderParameter(BrushCenter, NoHex);
        }
        _editorPanel.Visible = open;
        UpdateMapCanvas();
        _map.Position += MapCanvasRect().GetCenter() - previousCenter;
    }

    private Rect2 MapCanvasRect()
    {
        Rect2 viewport = GetViewportRect();
        if (!_editorPanel.Visible)
            return viewport;
        return new Rect2(5, MapEditorPanel.Top, Mathf.Max(1, _editorPanel.Position.X - 14),
            Mathf.Max(1, viewport.Size.Y - MapEditorPanel.Top - 69));
    }

    private void UpdateMapCanvas()
    {
        bool editing = _editorPanel.Visible;
        Rect2 canvas = MapCanvasRect();
        _footer.OffsetRight = editing ? canvas.End.X - GetViewportRect().Size.X : -18;
        _mapClip.ClipChildren = editing ? CanvasItem.ClipChildrenMode.Only : CanvasItem.ClipChildrenMode.Disabled;
        _mapClip.Polygon = editing
            ? new[] { canvas.Position, new Vector2(canvas.End.X, canvas.Position.Y), canvas.End, new Vector2(canvas.Position.X, canvas.End.Y) }
            : Array.Empty<Vector2>();
    }

    private void BuildStartPanel(Control hud)
    {
        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        hud.AddChild(center);
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _emptyState = center;
        var card = new PanelContainer { CustomMinimumSize = new Vector2(370, 0) };
        card.AddThemeStyleboxOverride("panel", BarStyle(24, 20));
        center.AddChild(card);
        var stack = new VBoxContainer();
        stack.AddThemeConstantOverride("separation", 14);
        card.AddChild(stack);
        AddLabel(stack, "Nenhum mapa aberto", 22, Ink);
        _emptyStatus = AddLabel(stack, "", 14, Muted);
        _emptyStatus.Hide();
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 10);
        stack.AddChild(actions);
        _newButton = new Button { Text = "Criar novo mapa", CustomMinimumSize = new Vector2(0, 36), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _newButton.Pressed += () => OnMapAction((long)MapAction.New);
        actions.AddChild(_newButton);
        _openButton = new Button { Text = "Abrir mapa", CustomMinimumSize = new Vector2(0, 36), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _openButton.Pressed += () => OnMapAction((long)MapAction.Open);
        actions.AddChild(_openButton);
    }

    private FileDialog MakeFileDialog(string title, FileDialog.FileModeEnum mode)
    {
        var dialog = new FileDialog
        {
            Title = title,
            FileMode = mode,
            Access = FileDialog.AccessEnum.Filesystem,
            Filters = new[] { "*.world ; Mapas " + ProjectName }
        };
        AddChild(dialog);
        return dialog;
    }

    private static void AddMenuItem(PopupMenu menu, string text, int id, Key key, bool shift = false, bool disabled = false)
    {
        var shortcut = new Shortcut();
        shortcut.Events.Add(new InputEventKey { Keycode = key, CtrlPressed = true, ShiftPressed = shift });
        menu.AddShortcut(shortcut, id);
        int item = menu.GetItemIndex(id);
        menu.SetItemText(item, text);
        menu.SetItemDisabled(item, disabled);
    }

    private void OnMapAction(long id)
    {
        if (_busy)
            return;
        if (_editSource is not null && (MapAction)id is MapAction.Save or MapAction.SaveAs)
        {
            RequestEditorFileAction((MapAction)id == MapAction.Save ? 0 : 1);
            return;
        }
        switch ((MapAction)id)
        {
            case MapAction.New:
                ConfirmReplace(() => _creation.ShowForm());
                break;
            case MapAction.Open:
                ConfirmReplace(() => _openDialog.PopupCenteredRatio(0.65f));
                break;
            case MapAction.Recent when _recentPath is { Length: > 0 } recent:
                ConfirmReplace(() => _ = OpenWorldAsync(recent));
                break;
            case MapAction.Save when _currentPath is { Length: > 0 } path:
                _ = SaveWorldAsync(path);
                break;
            case MapAction.Save:
            case MapAction.SaveAs:
                ShowSaveAsDialog();
                break;
            case MapAction.Generate when _world?.Manifest.Generation.Stage == GenerationStage.Blank:
                _creation.ShowForm(_world.Manifest);
                break;
        }
    }

    private void ShowSaveAsDialog()
    {
        if (_world is null)
            return;
        _saveAsNewFile = _currentPath is not null;
        string name = string.Join("_", _world.Manifest.WorldName.Split(Path.GetInvalidFileNameChars()));
        if (_currentPath is not null)
            _saveDialog.CurrentDir = Path.GetDirectoryName(_currentPath)!;
        _saveDialog.CurrentFile = name + (_saveAsNewFile ? " — cópia" : "") + ".world";
        _saveDialog.PopupCenteredRatio(0.65f);
    }

    private void OnSaveFileSelected(string path)
    {
        _saveDialog.Hide();
        if (!path.EndsWith(".world", StringComparison.OrdinalIgnoreCase))
            path += ".world";
        if (_saveAsNewFile && _currentPath is string current &&
            string.Equals(Path.GetFullPath(path), Path.GetFullPath(current), StringComparison.OrdinalIgnoreCase))
        {
            _message.Title = "Salvar como";
            _message.DialogText = "Escolha um nome diferente para criar o novo arquivo.";
            _message.PopupCentered();
            return;
        }
        _saveAsNewFile = false;
        _ = SaveWorldAsync(path);
    }

    private void ConfirmReplace(Action next)
    {
        FinishGesture();
        if (!_dirty)
        {
            next();
            return;
        }
        _afterDiscard = next;
        _discardDialog.PopupCentered();
    }

    private async Task CreateWorldAsync(WorldManifest manifest, bool fillingBlank)
    {
        await RunOperation("Criando mapa…", async () =>
        {
            bool generated = manifest.Generation.Stage != GenerationStage.Blank;
            if (generated)
            {
                _reportedProgress = 0;
                UpdateGenerationStatus();
                _generationStatus.Show();
                RefreshInterface();
            }
            try
            {
                var (world, buffers) = await Task.Run(() =>
                {
                    WorldData result = WorldGenerator.Create(manifest, ReportGenerationProgress);
                    GlobalDataTextures.Buffers? data = generated ? GlobalDataTextures.BuildBuffers(result) : null;
                    return (result, data);
                });
                InstallWorld(world, buffers);
                if (!fillingBlank)
                    _currentPath = null;
                _dirty = true;
            }
            finally
            {
                _generationStatus.Hide();
            }
        });
    }

    private void ReportGenerationProgress(GenerationProgress progress) =>
        Volatile.Write(ref _reportedProgress, (int)progress.Step * ProgressValuesPerStep + progress.Percent);

    private void UpdateGenerationStatus()
    {
        int progress = Volatile.Read(ref _reportedProgress);
        _generationStep.Text = (GenerationStep)(progress / ProgressValuesPerStep) switch
        {
            GenerationStep.Land => "Terra e água", GenerationStep.Elevation => "Elevação e relevo",
            GenerationStep.Climate => "Temperatura e chuva", GenerationStep.Biomes => "Biomas", GenerationStep.Rivers => "Rios e lagos", _ => ""
        };
        _generationBar.Value = progress % ProgressValuesPerStep;
    }

    private async Task OpenWorldAsync(string path)
    {
        await RunOperation("Abrindo mapa…", async () =>
        {
            var (world, buffers) = await Task.Run(() =>
            {
                WorldData result = WorldFile.Load(path);
                GlobalDataTextures.Buffers? data = result.Manifest.Generation.Stage == GenerationStage.Blank
                    ? null : GlobalDataTextures.BuildBuffers(result);
                return (result, data);
            });
            InstallWorld(world, buffers);
            _currentPath = path;
            MarkEditorSaved();
            RememberPath(path);
            _inspection.Text = "Mapa aberto.";
        });
    }

    private async Task SaveWorldAsync(string path)
    {
        if (_world is not WorldData world)
            return;
        if (!path.EndsWith(".world", StringComparison.OrdinalIgnoreCase))
            path += ".world";
        await RunOperation("Salvando mapa…", async () =>
        {
            _saving = true;
            _saveFinished = false;
            _saveStarted = Time.GetTicksMsec();
            UpdateSaveProgress();
            try
            {
                GlobalDataTextures.Buffers? prepared = null;
                if (_editorPreview is not null && _draftTerrainChanged && _textures is not null)
                    prepared = await Task.Run(() => GlobalDataTextures.BuildBuffers(world));
                await SaveWorldFileAsync(path);
                while (Time.GetTicksMsec() - _saveStarted < 3000)
                    await Task.Delay(25);
                _saveFinished = true;
                UpdateSaveProgress();
                await Task.Delay(180);
                await ApplyEditorDraftAsync(prepared);
                _inspection.Text = $"Salvo em {Path.GetFileName(path)}";
            }
            finally
            {
                _saving = false;
                _editorPanel.ShowSaveProgress(false);
            }
        });
    }

    private async Task SaveWorldFileAsync(string path)
    {
        WorldData world = _world!;
        if (DisplayServer.GetName() != "headless")
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        byte[]? preview = CapturePreview();
        world.Manifest = await Task.Run(() => WorldFile.Save(path, world, preview));
        _currentPath = path;
        MarkEditorSaved();
        RememberPath(path);
    }

    private byte[]? CapturePreview()
    {
        if (DisplayServer.GetName() == "headless")
            return null;
        using Image image = GetViewport().GetTexture().GetImage();
        if (image.IsEmpty())
            return null;
        image.Resize(320, Math.Max(1, 320 * image.GetHeight() / image.GetWidth()), Image.Interpolation.Bilinear);
        return image.SavePngToBuffer();
    }

    private void RememberPath(string path)
    {
        _recentPath = path;
        using var config = new ConfigFile();
        config.SetValue("maps", "recent", path);
        Error saved = config.Save(_preferencesPath);
        if (saved != Error.Ok)
            GD.PushWarning($"Não foi possível guardar o mapa recente: {saved}");
    }

    private async Task RunOperation(string status, Func<Task> operation)
    {
        if (_busy)
            return;
        FinishGesture();
        _busy = true;
        _inspection.Text = status;
        _emptyStatus.Text = status;
        _emptyStatus.Show();
        RefreshInterface();
        try
        {
            await operation();
        }
        catch (Exception error)
        {
            _inspection.Text = "A operação não foi concluída.";
            _message.Title = "Erro no mapa";
            _message.DialogText = error.Message;
            _message.PopupCentered(new Vector2I(490, 160));
            GD.PushWarning(error.Message);
        }
        finally
        {
            _busy = false;
            _emptyStatus.Hide();
            RefreshInterface();
        }
    }

    private void RefreshInterface()
    {
        bool hasMap = _world is not null;
        _emptyState.Visible = !hasMap;
        _footer.Visible = hasMap && !_generationStatus.Visible;
        _mapControls.Visible = hasMap;
        _editorToggle.Visible = hasMap;
        _editorToggle.Disabled = _busy || !hasMap;
        if (!hasMap)
            SetEditorPanelOpen(false);
        RefreshEditor();
        _title.Text = hasMap ? _world!.Manifest.WorldName + (_dirty ? " •" : "") : ProjectName;
        _mapMenu.Disabled = _busy;
        _newButton.Disabled = _busy;
        _openButton.Disabled = _busy;
        PopupMenu menu = _mapMenu.GetPopup();
        menu.SetItemDisabled(menu.GetItemIndex((int)MapAction.Save), !hasMap);
        menu.SetItemDisabled(menu.GetItemIndex((int)MapAction.SaveAs), !hasMap);
        menu.SetItemDisabled(menu.GetItemIndex((int)MapAction.Recent), string.IsNullOrEmpty(_recentPath));
        menu.SetItemDisabled(menu.GetItemIndex((int)MapAction.Generate), _world?.Manifest.Generation.Stage != GenerationStage.Blank);
    }
}
