using System;
using Godot;
using Mapmaker.Core;

namespace Mapmaker.GodotApp;

internal sealed partial class MapEditorPanel : PanelContainer
{
    private static readonly Color Ink = new("#f0efdf");
    private static readonly Color Muted = new("#a9beb9");
    private static readonly Color Gold = new("#edc66c");
    private const float PanelWidth = 485;
    private const float RightMargin = 6;
    internal const float Top = 86;
    private const float Bottom = 68;
    private readonly Button[] _rail = new Button[4];
    private readonly Label[] _pageCaptions = new Label[4];
    private readonly Control[] _pageIcons = new Control[4];
    private Label[] _pageHeadings = Array.Empty<Label>();
    private readonly Button[] _brushTypes = new Button[4];
    private readonly Button[] _regenerationTargets = new Button[3];
    private readonly CheckBox[] _modes = new CheckBox[3];
    private readonly System.Collections.Generic.Dictionary<HexLocks, BaseButton> _locks = new();
    private Button _undo = null!;
    private Button _redo = null!;
    private readonly Button[] _fileActions = new Button[3];
    private VBoxContainer _saveProgress = null!;
    private Label _saveStatus = null!;
    private ProgressBar _saveBar = null!;
    private SpinBox _radius = null!;
    private SpinBox _meters = null!;
    private OptionButton _shape = null!;
    private HSlider _strength = null!;
    private readonly Button[] _elevationTools = new Button[5];
    private HBoxContainer _elevationValue = null!;
    private int _brushType;
    private readonly VBoxContainer[] _pages = new VBoxContainer[4];
    private VBoxContainer _selectionControls = null!;
    private Label _biomeName = null!;
    private Label _hexDetails = null!;
    private OptionButton _hexField = null!;
    private OptionButton _hexValue = null!;
    private SpinBox _hexNumber = null!;
    private Button _applyHex = null!;
    private WorldData? _inspectedWorld;
    private int _inspectedCell = -1;
    private bool _editingEnabled;
    internal event Action<HexLocks, int>? HexEditRequested;
    internal event Action? PageChanged;

    private Label _selectionStatus = null!;
    private Button _clear = null!;
    private OptionButton _brushValue = null!;
    private HBoxContainer _swatches = null!;
    private GridContainer _palette = null!;
    private OptionButton _field = null!;
    private Button _regenerate = null!;

    internal event Action<int>? FileActionRequested;
    internal event Action? ClearRequested;
    internal event Action<bool>? HistoryRequested;
    internal event Action<HexLocks, bool>? LockRequested;
    internal event Action? RegenerationRequested;
    internal int ActiveSection => Array.FindIndex(_rail, button => button.ButtonPressed);
    internal bool RegionTool => ActiveSection is 2 or 3;
    internal BrushSettings Brush => new((BrushKind)_brushType, _brushValue.GetSelectedId(), (int)_radius.Value,
        (int)_meters.Value, (BrushShape)_shape.GetSelectedId(), (float)_strength.Value / 100f);
    internal int RegenerationTarget => Array.FindIndex(_regenerationTargets, button => button.ButtonPressed);
    internal RegenerationMode Mode => (RegenerationMode)Array.FindIndex(_modes, button => button.ButtonPressed);
    internal HexLocks Field => (HexLocks)_field.GetSelectedId();

    public override void _Ready()
    {
        MouseForcePassScrollEvents = false;
        CustomMinimumSize = new Vector2(PanelWidth, 0);
        AddThemeStyleboxOverride("panel", Surface(new Color("#14292e"), new Color("#3b575b"), 8, 7));
        var layout = Row(this, 7);
        var stack = Stack(5);
        stack.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        layout.AddChild(stack);

        var header = Row(stack, 5);
        _undo = ActionButton(header, "↶  Desfazer", disabled: true);
        _redo = ActionButton(header, "↷  Refazer", disabled: true);
        _undo.Pressed += () => HistoryRequested?.Invoke(false);
        _redo.Pressed += () => HistoryRequested?.Invoke(true);

        _selectionControls = Stack(4);
        stack.AddChild(_selectionControls);
        BuildSelection(_selectionControls);
        for (int i = 0; i < _pages.Length; i++)
        {
            _pages[i] = Stack(10);
            stack.AddChild(_pages[i]);
        }
        var sections = new[] { BuildHexEditor(_pages[0]), BuildBrush(_pages[1]), BuildLocks(_pages[2]), BuildRegeneration(_pages[3]) };
        stack.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        _saveProgress = Stack(4);
        stack.AddChild(_saveProgress);
        _saveStatus = Label(_saveProgress, "Salvando…", 14, Gold);
        _saveBar = new ProgressBar { MinValue = 0, MaxValue = 100, ShowPercentage = true };
        _saveProgress.AddChild(_saveBar);
        _saveProgress.Hide();
        HBoxContainer fileActions = Row(stack, 5);
        string[] fileLabels = { "Salvar", "Salvar como…", "Cancelar" };
        for (int i = 0; i < fileLabels.Length; i++)
        {
            int action = i;
            Button button = _fileActions[i] = ActionButton(fileActions, fileLabels[i], accent: i == 0, stretch: true);
            button.Pressed += () => FileActionRequested?.Invoke(action);
        }
        BuildRail(layout, sections);
        ShowPage(0);
        UpdateSelection(0);
    }

    internal void FitIn(Vector2 viewport)
    {
        Vector2 natural = GetCombinedMinimumSize();
        float available = Mathf.Max(1, viewport.Y - Top - Bottom);
        float scale = Mathf.Clamp(Mathf.Min((viewport.X - RightMargin * 2) / natural.X, available / natural.Y), 0.01f, 1f);
        Size = new Vector2(natural.X, available / scale);
        Scale = new Vector2(scale, scale);
        Position = new Vector2(viewport.X - RightMargin - natural.X * scale, Top);
    }

    internal void UpdateSelection(int count)
    {
        _selectionStatus.Text = count switch
        {
            0 => "Nenhum hex selecionado",
            1 => "1 hex selecionado",
            _ => $"{count:N0} hexes selecionados"
        };
        _clear.Disabled = count == 0;
    }

    internal void UpdateActions(GenerationStage stage, bool enabled, bool selected,
        bool canUndo, bool canRedo, HexLocks allLocks, HexLocks anyLocks)
    {
        _editingEnabled = enabled;
        foreach (Button action in _fileActions)
            action.Disabled = !enabled;
        _undo.Disabled = !enabled || !canUndo;
        _redo.Disabled = !enabled || !canRedo;
        foreach (Button page in _rail)
            page.Disabled = !enabled;
        _applyHex.Disabled = !enabled || _inspectedCell < 0 ||
            !FieldAvailable(stage, (HexLocks)_hexField.GetSelectedId()) ||
            (_inspectedWorld is not null && _inspectedCell >= 0 &&
            ((HexLocks)_inspectedWorld.LockMask[_inspectedCell] & (HexLocks)_hexField.GetSelectedId()) != 0);
        _clear.Disabled = !enabled || !selected;
        _brushTypes[0].Disabled = !enabled || stage < GenerationStage.Terrain;
        _brushTypes[1].Disabled = _brushTypes[3].Disabled = !enabled || stage < GenerationStage.Relief;
        _brushTypes[2].Disabled = !enabled || stage < GenerationStage.Land;
        foreach (Button button in _elevationTools)
            button.Disabled = _brushTypes[3].Disabled;
        _swatches.Modulate = _palette.Modulate = _brushTypes[0].Disabled ? Muted : Colors.White;
        foreach (var (field, button) in _locks)
        {
            bool all = (allLocks & field) == field;
            bool any = (anyLocks & field) != 0;
            button.Disabled = !enabled || !selected;
            button.SetPressedNoSignal(all);
            button.TooltipText = any && !all ? "Parte da seleção está travada" : "";
        }
        _field.Disabled = !enabled || RegenerationTarget != 2;
        for (int i = 0; i < _field.ItemCount; i++)
        {
            var field = (HexLocks)_field.GetItemId(i);
            bool available = FieldAvailable(stage, field);
            _field.SetItemDisabled(i, !available);
        }
        if (!FieldAvailable(stage, Field))
            _field.Select(_field.GetItemIndex((int)HexLocks.Water));
        _regenerate.Disabled = !enabled || stage == GenerationStage.Blank ||
            (RegenerationTarget != 1 && !selected);
    }

    internal void ShowSaveProgress(bool visible, double percent = 0, float opacity = 1)
    {
        _saveProgress.Visible = visible;
        _saveBar.Value = percent;
        _saveStatus.Modulate = new Color(1, 1, 1, opacity);
    }

    internal static bool FieldAvailable(GenerationStage stage, HexLocks field) => field switch
    {
        HexLocks.Water => stage >= GenerationStage.Land,
        HexLocks.Relief or HexLocks.Elevation => stage >= GenerationStage.Relief,
        _ => stage >= GenerationStage.Terrain
    };

    private void BuildRail(HBoxContainer layout, Label[] headings)
    {
        _pageHeadings = headings;
        var rail = new PanelContainer { CustomMinimumSize = new Vector2(89, 0) };
        rail.AddThemeStyleboxOverride("panel", Surface(new Color("#0e2025"), new Color("#3b575b"), 6, 4));
        layout.AddChild(rail);
        var shortcuts = Stack(6);
        rail.AddChild(shortcuts);
        var group = new ButtonGroup { AllowUnpress = false };
        string[] labels = { "Editar", "Terraformar", "Locks", "Gerar" };
        for (int i = 0; i < labels.Length; i++)
        {
            int index = i;
            Button shortcut = _rail[i] = ActionButton(shortcuts, "", stretch: true);
            shortcut.CustomMinimumSize = new Vector2(55, 65);
            shortcut.TooltipText = headings[i].Text;
            shortcut.ToggleMode = true;
            shortcut.ButtonGroup = group;
            shortcut.SetPressedNoSignal(i == 0);
            shortcut.AddThemeStyleboxOverride("normal", Surface(new Color("#0e2025"), new Color("#0e2025"), 5, 0));
            VBoxContainer contents = Stack(0);
            contents.MouseFilter = MouseFilterEnum.Ignore;
            contents.Alignment = BoxContainer.AlignmentMode.Center;
            shortcut.AddChild(contents);
            contents.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            var symbol = new Control { CustomMinimumSize = new Vector2(26, 29), MouseFilter = MouseFilterEnum.Ignore };
            _pageIcons[i] = symbol;
            contents.AddChild(symbol);
            symbol.Draw += () => DrawShortcut(symbol, index, shortcut.ButtonPressed ? Gold : Ink);
            Label caption = Label(contents, labels[i], 12, i == 0 ? Gold : Ink);
            _pageCaptions[i] = caption;
            caption.HorizontalAlignment = HorizontalAlignment.Center;
            shortcut.Toggled += active =>
            {
                if (active)
                    ShowPage(index);
            };
        }
        headings[0].AddThemeColorOverride("font_color", Gold);
    }

    internal void ShowPage(int index)
    {
        for (int i = 0; i < _rail.Length; i++)
        {
            _rail[i].SetPressedNoSignal(i == index);
            Color color = i == index ? Gold : Ink;
            _pageCaptions[i].AddThemeColorOverride("font_color", color);
            _pageHeadings[i].AddThemeColorOverride("font_color", color);
            _pageIcons[i].QueueRedraw();
        }
        for (int i = 0; i < _pages.Length; i++)
            _pages[i].Visible = i == index;
        _selectionControls.Visible = index != 1;
        PageChanged?.Invoke();
    }

    private Label BuildHexEditor(VBoxContainer stack)
    {
        var (section, title) = Section(stack, "Editar hex");
        _hexDetails = Label(section, "Selecione um hex no mapa.", 14, Ink);
        _hexDetails.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _hexField = Dropdown(section);
        foreach (var (field, label) in EditableFields)
            _hexField.AddItem(label, (int)field);
        _hexValue = Dropdown(section);
        _hexNumber = new SpinBox { MinValue = short.MinValue, MaxValue = short.MaxValue, Step = 1 };
        section.AddChild(_hexNumber);
        _hexField.ItemSelected += _ => RefreshHexValue();
        _applyHex = ActionButton(section, "Aplicar ao hex", accent: true);
        _applyHex.Pressed += () => HexEditRequested?.Invoke((HexLocks)_hexField.GetSelectedId(),
            _hexValue.Visible ? _hexValue.GetSelectedId() : (int)_hexNumber.Value);
        RefreshHexValue();
        return title;
    }

    internal void InspectHex(WorldData world, int cell)
    {
        _inspectedWorld = world;
        _inspectedCell = cell;
        _hexField.Visible = _applyHex.Visible = cell >= 0;
        _hexDetails.Text = cell < 0 ? "Selecione um hex no mapa." :
            $"Hex {cell % world.Width}, {cell / world.Width}\n{MapView.BiomeLabel(world.Biomes[cell])}\n" +
            $"{MapView.ReliefLabel(world.Relief[cell])} · {world.Elevation[cell]} m\n" +
            $"{world.Temperature[cell] * 0.1:F1} °C · {world.Rainfall[cell]} mm/ano\n" +
            $"Locks · {LockLabel((HexLocks)world.LockMask[cell])}";
        RefreshHexValue();
    }

    private void RefreshHexValue()
    {
        var field = (HexLocks)_hexField.GetSelectedId();
        _applyHex.Disabled = !_editingEnabled || _inspectedCell < 0 || _inspectedWorld is null ||
            !FieldAvailable(_inspectedWorld.Manifest.Generation.Stage, field) ||
            ((HexLocks)_inspectedWorld.LockMask[_inspectedCell] & field) != 0;
        bool categorical = field is HexLocks.Biome or HexLocks.Relief or HexLocks.Water;
        _hexValue.Visible = categorical && _inspectedCell >= 0;
        _hexNumber.Visible = !categorical && _inspectedCell >= 0;
        _hexValue.Clear();
        if (field == HexLocks.Biome)
            foreach (BiomeType biome in Enum.GetValues<BiomeType>())
                _hexValue.AddItem(MapView.BiomeLabel(biome), (int)biome);
        if (field == HexLocks.Relief)
            foreach (ReliefType relief in Enum.GetValues<ReliefType>())
                _hexValue.AddItem(MapView.ReliefLabel(relief), (int)relief);
        if (field == HexLocks.Water)
            foreach (string water in new[] { "Terra", "Oceano", "Lago", "Pântano" })
                _hexValue.AddItem(water);
        _hexNumber.MinValue = field == HexLocks.Rainfall ? 0 : short.MinValue;
        _hexNumber.MaxValue = field == HexLocks.Rainfall ? ushort.MaxValue : short.MaxValue;
        _hexNumber.TooltipText = field == HexLocks.Temperature ? "Temperatura em décimos de °C" :
            field == HexLocks.Rainfall ? "Chuva em mm/ano" : "Elevação em metros";
        if (_inspectedWorld is not WorldData world || _inspectedCell < 0)
            return;
        int i = _inspectedCell;
        int value = field switch
        {
            HexLocks.Biome => (int)world.Biomes[i], HexLocks.Relief => (int)world.Relief[i],
            HexLocks.Water => (int)world.Water[i], HexLocks.Elevation => world.Elevation[i],
            HexLocks.Temperature => world.Temperature[i], _ => world.Rainfall[i]
        };
        if (categorical)
            _hexValue.Select(_hexValue.GetItemIndex(value));
        else
            _hexNumber.Value = value;
    }

    private static void DrawShortcut(Control icon, int index, Color color)
    {
        Vector2 mid = icon.Size * 0.5f;
        void Line(float x, float y, float toX, float toY, float width = 1.7f) =>
            icon.DrawLine(mid + new Vector2(x, y), mid + new Vector2(toX, toY), color, width, true);

        switch (index)
        {
            case 0:
                foreach (int x in new[] { -1, 1 })
                foreach (int y in new[] { -1, 1 })
                {
                    Line(x * 9, y * 9, x * 4, y * 9);
                    Line(x * 9, y * 9, x * 9, y * 4);
                }
                break;
            case 1:
                Line(-1, 1, 8, -8, 3.3f);
                icon.DrawCircle(mid + new Vector2(-4, 5), 3.3f, color, true, -1, true);
                break;
            case 2:
                icon.DrawRect(new Rect2(mid + new Vector2(-8, -1), new Vector2(16, 11)), color, false, 1.7f);
                icon.DrawArc(mid + new Vector2(0, -1), 5.3f, Mathf.Pi, Mathf.Tau, 16, color, 1.7f, true);
                break;
            case 3:
                icon.DrawArc(mid, 8.5f, -2.8f, -0.55f, 16, color, 1.7f, true);
                icon.DrawArc(mid, 8.5f, 0.35f, 2.6f, 16, color, 1.7f, true);
                Line(8, -9, 8, -4);
                Line(8, -4, 3, -4);
                Line(-8, 9, -8, 4);
                Line(-8, 4, -3, 4);
                break;
        }
    }

    private Label BuildSelection(VBoxContainer stack)
    {
        var (section, title) = Section(stack, "Seleção");
        var row = Row(section, 5);
        _selectionStatus = Label(row, "", 13, Muted);
        _selectionStatus.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _selectionStatus.HorizontalAlignment = HorizontalAlignment.Center;
        _clear = ActionButton(row, "Limpar");
        _clear.Pressed += () => ClearRequested?.Invoke();
        return title;
    }

    private Label BuildBrush(VBoxContainer stack)
    {
        var (section, title) = Section(stack, "Pincel");
        var tools = Row(section, 5);
        var types = new ButtonGroup { AllowUnpress = false };
        string[] names = { "Bioma", "Relevo", "Água", "Elevação" };
        for (int i = 0; i < names.Length; i++)
        {
            int type = i;
            Button choice = _brushTypes[i] = ActionButton(tools, names[i], stretch: true);
            choice.ToggleMode = true;
            choice.ButtonGroup = types;
            choice.SetPressedNoSignal(i == 0);
            choice.Pressed += () => ActivateBrush(type);
        }
        var settings = Row(section, 7);
        Label(settings, "Raio", 13, Muted);
        _radius = new SpinBox { Name = "BrushRadius", MinValue = 0, MaxValue = 20, Step = 1, Value = 2, CustomMinimumSize = new Vector2(60, 26) };
        _radius.TooltipText = "Raio em hexes. Com seleção ativa, a pintura fica dentro do contorno.";
        _radius.GetLineEdit().AddThemeFontSizeOverride("font_size", 13);
        settings.AddChild(_radius);
        var slider = new HSlider { SizeFlagsHorizontal = SizeFlags.ExpandFill, Scrollable = false };
        settings.AddChild(slider);
        _radius.Share(slider);
        _radius.ValueChanged += _ => _rail[1].ButtonPressed = true;

        var profile = Row(section, 6);
        _shape = Dropdown(profile);
        _shape.Name = "BrushShape";
        foreach (string shape in new[] { "Hexagonal", "Redondo", "Quadrado", "Losango" })
            _shape.AddItem(shape);
        _shape.TooltipText = "Formato do pincel";
        _shape.ItemSelected += _ => _rail[1].ButtonPressed = true;
        Label(profile, "Força na borda", 13, Muted);
        _strength = new HSlider
        {
            MinValue = 0, MaxValue = 100, Step = 1, Value = 30,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, Scrollable = false,
            TooltipText = "O centro sempre aplica 100%. A força diminui até este valor na borda."
        };
        profile.AddChild(_strength);
        Label strengthValue = Label(profile, "30%", 13, Ink);
        strengthValue.CustomMinimumSize = new Vector2(36, 0);
        _strength.ValueChanged += value =>
        {
            strengthValue.Text = $"{value:0}%";
            _rail[1].ButtonPressed = true;
        };

        _swatches = Row(section, 5);
        AddSwatch("Campo", "grasslands", BiomeType.Grassland);
        AddSwatch("Floresta", "temperate_forest", BiomeType.TemperateForest);
        AddSwatch("Praia", "beach", BiomeType.Beach);
        _palette = new GridContainer { Columns = 5 };
        _palette.AddThemeConstantOverride("h_separation", 5);
        _palette.AddThemeConstantOverride("v_separation", 3);
        section.AddChild(_palette);
        string[] tiles = { "beach", "grasslands", "temperate_forest", "rainforest", "savanna",
            "steppe", "desert", "taiga", "tundra", "alpine" };
        for (int i = 0; i < tiles.Length; i++)
        {
            var biome = (BiomeType)(i + 1);
            Button tile = ActionButton(_palette, "", stretch: true);
            tile.ToggleMode = true;
            var sample = new TextureRect
            {
                Texture = GD.Load<Texture2D>($"res://tiles/{tiles[i]}.png"),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                MouseFilter = MouseFilterEnum.Ignore
            };
            tile.AddChild(sample);
            sample.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            sample.OffsetLeft = 4;
            sample.OffsetRight = -4;
            sample.OffsetTop = 3;
            sample.OffsetBottom = -3;
            tile.TooltipText = MapView.BiomeLabel(biome);
            tile.Pressed += () => ActivateBiome(biome);
        }
        _biomeName = Label(section, "", 15, Gold);
        _brushValue = Dropdown(section);
        _brushValue.Name = "BrushValue";
        _brushValue.ItemSelected += _ => _rail[1].ButtonPressed = true;
        Label(section, "Elevação", 13, Ink);
        HBoxContainer elevation = Row(section, 4);
        string[] elevationTools = { "Subir", "Baixar", "Definir", "Suavizar", "Nivelar" };
        for (int i = 0; i < elevationTools.Length; i++)
        {
            int operation = i;
            Button button = _elevationTools[i] = ActionButton(elevation, elevationTools[i], stretch: true);
            if (operation == (int)ElevationTool.Smooth)
                button.TooltipText = "Usa a média da altitude do hex e dos vizinhos.";
            if (operation == (int)ElevationTool.Flatten)
                button.TooltipText = "Usa a altitude do hex onde a pincelada começa.";
            button.ToggleMode = true;
            button.Pressed += () =>
            {
                if (_brushType != 3)
                    SetBrushOptions(3);
                _brushTypes[3].ButtonPressed = true;
                _brushValue.Select(operation);
                UpdateElevationInput();
                _rail[1].ButtonPressed = true;
            };
        }
        _elevationValue = Row(section, 5);
        Label(_elevationValue, "Metros", 13, Muted);
        _meters = new SpinBox { Name = "BrushMeters", MinValue = short.MinValue, MaxValue = short.MaxValue, Step = 1, Value = 100 };
        _elevationValue.AddChild(_meters);
        SetBrushOptions(0);
        return title;
    }

    private void AddSwatch(string name, string tile, BiomeType biome)
    {
        Button swatch = ActionButton(_swatches, name, stretch: true);
        swatch.TooltipText = MapView.BiomeLabel(biome);
        swatch.Icon = GD.Load<Texture2D>($"res://tiles/{tile}.png");
        swatch.AddThemeConstantOverride("icon_max_width", 19);
        swatch.Pressed += () => ActivateBiome(biome);
    }

    private void ActivateBiome(BiomeType biome)
    {
        if (_brushTypes[0].Disabled)
            return;
        PickBiome(biome);
        _rail[1].ButtonPressed = true;
    }

    private void ActivateBrush(int type)
    {
        SetBrushOptions(type);
        _rail[1].ButtonPressed = true;
    }

    private void PickBiome(BiomeType biome)
    {
        _brushValue.Select(_brushValue.GetItemIndex((int)biome));
        _biomeName.Text = MapView.BiomeLabel(biome);
        foreach (Node child in _palette.GetChildren())
        {
            if (child is Button button)
            {
                bool selected = button.GetIndex() + 1 == (int)biome;
                button.SetPressedNoSignal(selected);
            }
        }
    }

    private void SetBrushOptions(int type)
    {
        _brushType = type;
        _brushValue.Clear();
        _swatches.Visible = type == 0;
        _palette.Visible = type == 0;
        _biomeName.Visible = type == 0;
        _brushValue.Visible = type is 1 or 2;
        switch (type)
        {
            case 0:
                foreach (BiomeType biome in Enum.GetValues<BiomeType>())
                    if (biome != BiomeType.None)
                        _brushValue.AddItem(MapView.BiomeLabel(biome), (int)biome);
                PickBiome(BiomeType.TemperateForest);
                break;
            case 1:
                foreach (ReliefType relief in Enum.GetValues<ReliefType>())
                    _brushValue.AddItem(MapView.ReliefLabel(relief), (int)relief);
                break;
            case 2:
                foreach (string water in new[] { "Terra", "Oceano", "Lago", "Pântano" })
                    _brushValue.AddItem(water);
                break;
            case 3:
                foreach (string elevation in new[] { "Subir", "Baixar", "Definir", "Suavizar", "Nivelar" })
                    _brushValue.AddItem(elevation);
                break;
        }
        UpdateElevationInput();
    }

    private void UpdateElevationInput()
    {
        for (int i = 0; i < _elevationTools.Length; i++)
            _elevationTools[i].SetPressedNoSignal(_brushType == 3 && _brushValue.Selected == i);
        _elevationValue.Visible = _brushType == 3 && _brushValue.Selected <= 2;
        _meters.MinValue = _brushValue.Selected == 2 ? short.MinValue : 1;
        _elevationValue.TooltipText = _brushValue.Selected == 2 ? "Altitude final do hex" : "Variação por hex em cada pincelada";
    }

    private Label BuildLocks(VBoxContainer stack)
    {
        var (section, title) = Section(stack, "Locks");
        var entireHex = new CheckButton { Text = "Hex inteiro", SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        section.AddChild(entireHex);
        AddLock(entireHex, HexLocks.All);
        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 2);
        section.AddChild(grid);
        foreach (var (field, label) in EditableFields)
        {
            var check = new CheckBox { Text = label };
            grid.AddChild(check);
            AddLock(check, field);
        }
        var riverLock = new CheckBox { Text = "Rios", TooltipText = "Protege todas as arestas fluviais do hex, inclusive o lado compartilhado com vizinhos." };
        grid.AddChild(riverLock);
        AddLock(riverLock, HexLocks.Rivers);
        return title;
    }

    internal static readonly (HexLocks Field, string Label)[] EditableFields =
    {
        (HexLocks.Biome, "Bioma"), (HexLocks.Relief, "Relevo"), (HexLocks.Water, "Água"),
        (HexLocks.Elevation, "Elevação"), (HexLocks.Temperature, "Temperatura"), (HexLocks.Rainfall, "Chuva")
    };

    internal static string LockLabel(HexLocks locks)
    {
        if ((locks & HexLocks.All) == HexLocks.All)
            return "Hex inteiro";
        var fields = new System.Collections.Generic.List<string>();
        foreach (var (field, label) in EditableFields)
            if ((locks & field) != 0)
                fields.Add(label);
        if ((locks & HexLocks.Roads) != 0) fields.Add("Estradas");
        if ((locks & HexLocks.Rivers) != 0) fields.Add("Rios");
        if ((locks & HexLocks.Flags) != 0) fields.Add("Marcadores");
        return fields.Count == 0 ? "—" : string.Join(", ", fields);
    }

    private void AddLock(BaseButton button, HexLocks field)
    {
        button.AddThemeFontSizeOverride("font_size", 13);
        button.AddThemeColorOverride("font_disabled_color", Muted);
        button.Toggled += locked => LockRequested?.Invoke(field, locked);
        _locks.Add(field, button);
    }

    internal event Action? RegenerationTargetChanged;

    internal void SelectRegenerationTarget(int target)
    {
        Button button = _regenerationTargets[target];
        button.ButtonPressed = true;
        _rail[3].ButtonPressed = true;
        _regenerate.Text = target switch
        {
            1 => "Regenerar mundo",
            2 => "Regenerar campo",
            _ => "Regenerar seleção"
        };
        foreach (CheckBox mode in _modes)
            mode.Disabled = target == 2;
        RegenerationTargetChanged?.Invoke();
    }

    private Label BuildRegeneration(VBoxContainer stack)
    {
        var (section, title) = Section(stack, "Regenerar");
        var target = Row(section, 5);
        Label(target, "Alvo", 13, Muted);
        var targets = new ButtonGroup { AllowUnpress = false };
        string[] names = { "Seleção", "Mundo", "Campo" };
        for (int i = 0; i < names.Length; i++)
        {
            int scope = i;
            Button button = _regenerationTargets[i] = ActionButton(target, names[i], stretch: true);
            button.ToggleMode = true;
            button.ButtonGroup = targets;
            button.SetPressedNoSignal(i == 0);
            button.Pressed += () => SelectRegenerationTarget(scope);
        }

        var options = Row(section, 8);
        var modes = Stack(2);
        modes.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        options.AddChild(modes);
        var group = new ButtonGroup { AllowUnpress = false };
        string[] modesText = { "Tudo", "Manter terra/água", "Manter terra/água + elevação" };
        for (int i = 0; i < modesText.Length; i++)
        {
            var radio = _modes[i] = new CheckBox { Text = modesText[i], ButtonGroup = group };
            radio.AddThemeFontSizeOverride("font_size", 13);
            radio.SetPressedNoSignal(i == 0);
            modes.AddChild(radio);
        }

        var fieldStack = Stack(2);
        options.AddChild(fieldStack);
        Label(fieldStack, "Campo específico", 12, Muted);
        _field = Dropdown(fieldStack);
        _field.CustomMinimumSize = new Vector2(128, 26);
        _field.Disabled = true;
        foreach (var (field, label) in EditableFields)
            _field.AddItem(label, (int)field);
        _field.AddItem("Rios", (int)HexLocks.Rivers);
        _regenerate = ActionButton(section, "Regenerar seleção", accent: true, disabled: true);
        _regenerate.Pressed += () => RegenerationRequested?.Invoke();
        _regenerate.CustomMinimumSize = new Vector2(0, 33);
        return title;
    }

    private static (VBoxContainer Content, Label Title) Section(VBoxContainer parent, string heading)
    {
        var divider = new HSeparator();
        var line = new StyleBoxLine { Color = new Color("#466064"), Thickness = 1 };
        divider.AddThemeStyleboxOverride("separator", line);
        parent.AddChild(divider);
        VBoxContainer content = Stack(4);
        parent.AddChild(content);
        return (content, Label(content, heading, 16, Ink));
    }

    private static VBoxContainer Stack(int separation)
    {
        var node = new VBoxContainer();
        node.AddThemeConstantOverride("separation", separation);
        return node;
    }

    private static HBoxContainer Row(Control parent, int separation)
    {
        var node = new HBoxContainer();
        node.AddThemeConstantOverride("separation", separation);
        parent.AddChild(node);
        return node;
    }

    private static Label Label(Control parent, string text, int size, Color color)
    {
        var node = new Label { Text = text, MouseFilter = MouseFilterEnum.Ignore };
        node.AddThemeFontSizeOverride("font_size", size);
        node.AddThemeColorOverride("font_color", color);
        parent.AddChild(node);
        return node;
    }

    private static OptionButton Dropdown(Control parent)
    {
        var node = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 26) };
        node.AddThemeFontSizeOverride("font_size", 13);
        parent.AddChild(node);
        return node;
    }

    private static Button ActionButton(Control parent, string text, bool accent = false, bool disabled = false, bool stretch = false)
    {
        var button = new Button { Text = text, Disabled = disabled, CustomMinimumSize = new Vector2(0, 27) };
        if (stretch)
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        button.AddThemeFontSizeOverride("font_size", 13);
        button.AddThemeColorOverride("font_color", accent ? Gold : Ink);
        button.AddThemeColorOverride("font_pressed_color", Gold);
        button.AddThemeColorOverride("font_disabled_color", accent ? new Color("#dbc38a") : new Color("#9aa9a7"));
        button.AddThemeStyleboxOverride("normal", Surface(new Color("#172e33"), new Color("#425e62"), 5, 4));
        button.AddThemeStyleboxOverride("hover", Surface(new Color("#264248"), new Color("#638185"), 5, 4));
        button.AddThemeStyleboxOverride("pressed", Surface(new Color("#3c3929"), Gold, 5, 4));
        button.AddThemeStyleboxOverride("disabled", Surface(accent ? new Color("#48402b") : new Color("#14272b"), new Color("#3b5153"), 5, 4));
        parent.AddChild(button);
        return button;
    }

    private static StyleBoxFlat Surface(Color background, Color stroke, int radius, int padding, bool shadow = false)
    {
        var style = new StyleBoxFlat { BgColor = background, BorderColor = stroke };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(radius);
        style.ContentMarginLeft = padding;
        style.ContentMarginRight = padding;
        style.ContentMarginTop = padding;
        style.ContentMarginBottom = padding;
        if (shadow)
        {
            style.ShadowColor = new Color(0, 0, 0, 0.55f);
            style.ShadowSize = 10;
            style.ShadowOffset = new Vector2(0, 4);
        }
        return style;
    }
}
