using System;
using Godot;
using Mapmaker.Core;

namespace Mapmaker.GodotApp;

public partial class MapView : Node2D
{
    private static readonly string[] Layers =
        { "Terreno", "Elevação", "Temperatura", "Chuva", "Biomas", "Locks", "Tectônica" };
    private static readonly Color Ink = new("#f0efdf");
    private static readonly Color Muted = new("#a9beb9");
    private Node2D _map = null!;
    private Polygon2D _mapClip = null!;
    private Polygon2D _surface = null!;
    private MapArtwork? _artwork;
    private ShaderMaterial _material = null!;
    private Label _inspection = null!;
    private Label _performance = null!;
    private OptionButton _layerSelect = null!;
    private WorldData? _world;
    private GlobalDataTextures? _textures;
    private Vector2 _mapSize;
    private Vector2I _selected = new(-1, -1);
    private int _layer;
    private TectonicOverlay _tectonicOverlay = null!;
    private RiverOverlay _riverOverlay = null!;
    private double _statusTime;

    public override void _Ready()
    {
        _mapClip = new Polygon2D();
        AddChild(_mapClip);
        _map = new Node2D();
        _mapClip.AddChild(_map);
        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://src/GodotApp/Terrain.gdshader") };
        _surface = new Polygon2D { Material = _material };
        _map.AddChild(_surface);
        _riverOverlay = new RiverOverlay();
        _map.AddChild(_riverOverlay);
        _tectonicOverlay = new TectonicOverlay { Visible = false };
        _map.AddChild(_tectonicOverlay);
        BuildInterface();
        GetViewport().SizeChanged += FitMap;
        GetTree().AutoAcceptQuit = false;

        string[] args = OS.GetCmdlineUserArgs();
        if (Array.IndexOf(args, "--gentle-ridge-review") >= 0)
            Callable.From(RunGentleRidgeReview).CallDeferred();
        else if (Array.IndexOf(args, "--restored-ridge-review") >= 0)
            Callable.From(RunRestoredRidgeReview).CallDeferred();
        else if (Array.IndexOf(args, "--feather-review") >= 0)
            Callable.From(RunFeatherReview).CallDeferred();
        else if (Array.IndexOf(args, "--organic-ridge-review") >= 0)
            Callable.From(RunOrganicRidgeReview).CallDeferred();
        else if (Array.IndexOf(args, "--ridge-review") >= 0)
            Callable.From(RunRidgeReview).CallDeferred();
        else if (Array.IndexOf(args, "--basin-review") >= 0)
            Callable.From(RunBasinReview).CallDeferred();
        else if (Array.IndexOf(args, "--river-visual-check") >= 0)
            Callable.From(RunRiverVisualCheck).CallDeferred();
        else if (Array.IndexOf(args, "--maximum-generation") >= 0)
            Callable.From(RunMaximumGeneration).CallDeferred();
        else if (Array.IndexOf(args, "--natural-preview") >= 0)
            Callable.From(RunNaturalPreview).CallDeferred();
        else if (Array.IndexOf(args, "--compare-generators") >= 0)
            Callable.From(RunGeneratorComparison).CallDeferred();
        else if (Array.IndexOf(args, "--smoke-test") >= 0)
            Callable.From(RunSmokeTest).CallDeferred();
        else if (Array.IndexOf(args, "--benchmark") >= 0 || Array.IndexOf(args, "--benchmark-large") >= 0)
        {
            bool large = Array.IndexOf(args, "--benchmark-large") >= 0;
            bool sculpt = Array.IndexOf(args, "--continental-sculpt") >= 0;
            Callable.From(() => RunBenchmark(large, sculpt)).CallDeferred();
        }
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= FitMap;
        _editorPreview?.Dispose();
        _lockTexture?.Dispose();
        _textures?.Dispose();
        _artwork?.Dispose();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMWindowFocusOut && !_busy)
            FinishGesture();
        if (what == NotificationWMCloseRequest && !_busy)
            ConfirmReplace(() => GetTree().Quit());
    }

    public override void _Process(double delta)
    {
        _editorPreview?.Refresh(delta);
        UpdateSaveProgress();
        if (_generationStatus.Visible)
            UpdateGenerationStatus();
        if (_world is not WorldData world)
            return;
        _statusTime += delta;
        if (_statusTime < 0.3)
            return;

        _statusTime = 0;
        _performance.Text = $"{world.Width} × {world.Height}  ·  {Engine.GetFramesPerSecond():F0} FPS";
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (_world is null || _busy)
            return;
        switch (input)
        {
            case InputEventKey key when key.Pressed && key.Keycode == Key.Escape && _editorPanel.Visible:
                SetEditorPanelOpen(false);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseMotion motion when
                (motion.ButtonMask & (MouseButtonMask.Right | MouseButtonMask.Middle)) != 0 &&
                MapCanvasRect().HasPoint(motion.Position):
                _map.Position += motion.Relative;
                break;
            case InputEventMouseButton button when button.Pressed && MapCanvasRect().HasPoint(button.Position):
                switch (button.ButtonIndex)
                {
                    case MouseButton.Left:
                        StartMapEdit(button.Position);
                        break;
                    case MouseButton.WheelUp:
                        ZoomAt(button.Position, 1.22f);
                        break;
                    case MouseButton.WheelDown:
                        ZoomAt(button.Position, 1f / 1.22f);
                        break;
                }
                break;
        }
    }

    private void InstallWorld(WorldData world, GlobalDataTextures.Buffers? buffers = null)
    {
        GenerationStage stage = world.Manifest.Generation.Stage;
        if (stage != GenerationStage.Blank && _artwork is null)
        {
            _artwork = new MapArtwork();
            _artwork.Bind(_material);
        }

        ReplaceTerrainTextures(world, stage == GenerationStage.Blank ? null : buffers ?? GlobalDataTextures.BuildBuffers(world));
        _material.SetShaderParameter("map_size", new Vector2I(world.Width, world.Height));
        _material.SetShaderParameter("stage", (int)stage);
        _material.SetShaderParameter("horizontal_wrap", world.Manifest.HorizontalWrap);

        Vector2 origin = new(-HexGrid.Root3 * 0.5f, -1f);
        Vector2 limit = new(HexGrid.Root3 * world.Width, 1.5f * (world.Height - 1) + 1f);
        _surface.Polygon = new[] { origin, new Vector2(limit.X, origin.Y), limit, new Vector2(origin.X, limit.Y) };
        _mapSize = limit - origin;

        SetEditorPanelVisible(false);
        ReleaseEditorPreview();
        _world = world;
        _tectonicOverlay.Reference = world.Tectonics;
        _tectonicOverlay.SymbolSize = Math.Max(2, world.Height / 80f);
        _tectonicOverlay.QueueRedraw();
        _layerSelect.SetItemDisabled(6, world.Tectonics is null);
        _layerSelect.SetItemTooltip(6, "Referência original: flechas = movimento; círculos = afastamento; X = colisão. Não muda ao editar o terreno.");
        _layerSelect.SetItemDisabled(1, stage < GenerationStage.Relief);
        for (int i = 2; i <= 4; i++)
            _layerSelect.SetItemDisabled(i, stage < GenerationStage.Terrain);
        if (_layerSelect.IsItemDisabled(_layer))
        {
            _layerSelect.Select(0);
            SetLayer(0);
        }
        _performance.Text = $"{world.Width} × {world.Height}";
        ResetEditor(world);
        FitMap();
    }

    private void ReplaceTerrainTextures(WorldData world, GlobalDataTextures.Buffers? buffers)
    {
        _riverOverlay.Invalidate();
        _riverOverlay.World = world;
        _riverOverlay.QueueRedraw();
        GlobalDataTextures? previous = _textures;
        _textures = buffers.HasValue ? new GlobalDataTextures(world, buffers.Value) : null;
        _material.SetShaderParameter("terrain_data", _textures is null ? default : Variant.From(_textures.Terrain));
        _material.SetShaderParameter("climate_data", _textures is null ? default : Variant.From(_textures.Climate));
        _material.SetShaderParameter("detail_data", _textures is null ? default : Variant.From(_textures.Details));
        if (_textures is not null) _riverOverlay.BindTerrain(_textures.Terrain, world);
        previous?.Dispose();
    }

    private void FitMap()
    {
        if (_world is null)
            return;
        Rect2 area = MapCanvasRect();
        float verticalMargin = _editorPanel.Visible ? 16f : 168f;
        float zoom = Mathf.Max(0.015f, Mathf.Min((area.Size.X - 80f) / _mapSize.X, (area.Size.Y - verticalMargin) / _mapSize.Y));
        _map.Scale = Vector2.One * zoom;
        Vector2 origin = new(-HexGrid.Root3 * 0.5f, -1f);
        _map.Position = area.GetCenter() - _mapSize * zoom * 0.5f - origin * zoom;
        UpdateArtworkZoom();
    }

    private void ZoomAt(Vector2 cursor, float factor)
    {
        float previousZoom = _map.Scale.X;
        float zoom = Mathf.Clamp(previousZoom * factor, 0.015f, 44f);
        _map.Position = cursor - (cursor - _map.Position) * (zoom / previousZoom);
        _map.Scale = Vector2.One * zoom;
        UpdateArtworkZoom();
    }

    private void UpdateArtworkZoom() =>
        _material.SetShaderParameter("detail_zoom", Mathf.SmoothStep(12f, 22f, _map.Scale.X));

    private void RefreshInspection()
    {
        if (_world is not WorldData world)
            return;
        _editorPanel.InspectHex(world, _selected == NoHex ? -1 : _selected.Y * world.Width + _selected.X);
        _inspection.TooltipText = "";
        if (_selection is WorldRegion region && _selected == NoHex)
        {
            _inspection.Text = $"Região de {region.Width} × {region.Height}  ·  Coluna {region.Column}, linha {region.Row}";
            return;
        }
        Vector2I offset = _selected;
        GenerationStage stage = world.Manifest.Generation.Stage;
        if (offset.X < 0)
        {
            _inspection.Text = stage == GenerationStage.Blank
                ? "Grade vazia  ·  Use Mapa → Gerar terreno."
                : "Clique para inspecionar  ·  Arraste com botão direito  ·  Roda para zoom";
            return;
        }

        Hex axial = HexGrid.FromOffset(offset.X, offset.Y);
        string label = $"Hex {offset.X}, {offset.Y}  ·  Axial {axial.Q}, {axial.R}";
        int i = offset.Y * world.Width + offset.X;
        if (stage >= GenerationStage.Land)
            label += world.Water[i] switch
            {
                WaterType.Ocean => IsCoastalOcean(world, axial) ? "  ·  Oceano costeiro" : "  ·  Oceano",
                WaterType.Lake => "  ·  Lago",
                WaterType.Wetland => "  ·  Pântano", _ => "  ·  Terra"
            };
        if (stage >= GenerationStage.Relief)
            label += $"  ·  {ReliefLabel(world.Relief[i])}  ·  {world.Elevation[i]} m";
        if (stage >= GenerationStage.Terrain)
            label += $"  ·  {BiomeLabel(world.Biomes[i])}  ·  {world.Temperature[i] * 0.1:F1} °C  ·  {world.Rainfall[i]} mm/ano";
        label += $"  ·  Locks: {MapEditorPanel.LockLabel((HexLocks)world.LockMask[i])}";
        if (world.RiverEdges[i] != 0)
        {
            int riverTier = WorldGenerator.RiverLevelAt(world, i);
            label += riverTier switch { 1 => "  ·  Nascente", 2 => "  ·  Riacho", 3 => "  ·  Rio pequeno", 4 => "  ·  Rio", _ => "  ·  Grande rio" };
        }
        _inspection.Text = label;
        _inspection.TooltipText = label;
    }

    private static bool IsCoastalOcean(WorldData world, Hex hex)
    {
        for (int direction = 0; direction < 6; direction++)
        {
            int neighbor = HexGrid.NeighborIndex(hex, direction, world.Width, world.Height, world.Manifest.HorizontalWrap);
            if (neighbor >= 0 && world.Water[neighbor] == WaterType.None)
                return true;
        }
        return false;
    }

    internal static string ReliefLabel(ReliefType relief) => relief switch
    {
        ReliefType.Rolling => "Ondulado", ReliefType.Hills => "Morros",
        ReliefType.Mountains => "Montanhas", ReliefType.Peak => "Picos", _ => "Plano"
    };

    internal static string BiomeLabel(BiomeType biome) => biome switch
    {
        BiomeType.Beach => "Praia", BiomeType.Grassland => "Campo",
        BiomeType.TemperateForest => "Floresta temperada", BiomeType.Rainforest => "Floresta tropical",
        BiomeType.Savanna => "Savana", BiomeType.Steppe => "Estepe", BiomeType.Desert => "Deserto",
        BiomeType.Taiga => "Taiga", BiomeType.Tundra => "Tundra", BiomeType.Alpine => "Alpino", _ => "Sem bioma terrestre"
    };

    private void SetLayer(int index)
    {
        _layer = index;
        _material.SetShaderParameter("layer", index == 6 ? 0 : index);
        _tectonicOverlay.Visible = index == 6;
        _riverOverlay.Visible = index == 0 || index == 6;
        if (!_busy)
            RefreshEditorClimate();
    }

    private static HBoxContainer AddBar(Control parent, Control.LayoutPreset preset, int top, int bottom)
    {
        var panel = new PanelContainer();
        parent.AddChild(panel);
        panel.SetAnchorsAndOffsetsPreset(preset);
        panel.OffsetLeft = 18;
        panel.OffsetRight = -18;
        panel.OffsetTop = top;
        panel.OffsetBottom = bottom;
        panel.AddThemeStyleboxOverride("panel", BarStyle(16, 8));

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);
        panel.AddChild(row);
        return row;
    }

    private static StyleBoxFlat BarStyle(float horizontal, float vertical) => new()
    {
        BgColor = new Color("#14252b"),
        BorderColor = new Color("#344e52"),
        BorderWidthTop = 1, BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
        CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10,
        CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10,
        ContentMarginLeft = horizontal, ContentMarginRight = horizontal,
        ContentMarginTop = vertical, ContentMarginBottom = vertical
    };

    private static Label AddLabel(Control parent, string text, int size, Color color)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        parent.AddChild(label);
        return label;
    }
}
