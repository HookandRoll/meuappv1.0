using System;
using Godot;
using Mapmaker.Core;

namespace Mapmaker.GodotApp;

public partial class MapView : Node2D
{
    private static readonly string[] Layers =
        { "Terreno", "Elevação", "Temperatura", "Chuva", "Biomas", "Locks" };
    private static readonly Color Ink = new("#f0efdf");
    private static readonly Color Muted = new("#a9beb9");
    private Node2D _map = null!;
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
    private double _statusTime;

    public override void _Ready()
    {
        _map = new Node2D();
        AddChild(_map);
        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://src/GodotApp/Terrain.gdshader") };
        _surface = new Polygon2D { Material = _material };
        _map.AddChild(_surface);
        BuildInterface();
        GetViewport().SizeChanged += FitMap;
        GetTree().AutoAcceptQuit = false;

        string[] args = OS.GetCmdlineUserArgs();
        if (Array.IndexOf(args, "--smoke-test") >= 0)
            Callable.From(RunSmokeTest).CallDeferred();
        else if (Array.IndexOf(args, "--benchmark") >= 0 || Array.IndexOf(args, "--benchmark-large") >= 0)
        {
            bool large = Array.IndexOf(args, "--benchmark-large") >= 0;
            Callable.From(() => RunBenchmark(large)).CallDeferred();
        }
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= FitMap;
        _textures?.Dispose();
        _artwork?.Dispose();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest && !_busy)
            ConfirmReplace(() => GetTree().Quit());
    }

    public override void _Process(double delta)
    {
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
            case InputEventMouseMotion motion when
                (motion.ButtonMask & (MouseButtonMask.Right | MouseButtonMask.Middle)) != 0:
                _map.Position += motion.Relative;
                break;
            case InputEventMouseButton button when button.Pressed:
                switch (button.ButtonIndex)
                {
                    case MouseButton.Left:
                        Vector2 local = _map.ToLocal(button.Position);
                        var (column, row) = HexGrid.ToOffset(HexGrid.Pick(new System.Numerics.Vector2(local.X, local.Y)));
                        SelectHex(new Vector2I(column, row));
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
        GlobalDataTextures? nextTextures = stage == GenerationStage.Blank ? null
            : new GlobalDataTextures(world, buffers ?? GlobalDataTextures.BuildBuffers(world));
        if (stage != GenerationStage.Blank && _artwork is null)
        {
            _artwork = new MapArtwork();
            _artwork.Bind(_material);
        }

        GlobalDataTextures? previousTextures = _textures;
        _textures = nextTextures;
        _material.SetShaderParameter("terrain_data", nextTextures is null ? default : Variant.From(nextTextures.Terrain));
        _material.SetShaderParameter("climate_data", nextTextures is null ? default : Variant.From(nextTextures.Climate));
        _material.SetShaderParameter("map_size", new Vector2I(world.Width, world.Height));
        _material.SetShaderParameter("stage", (int)stage);
        _material.SetShaderParameter("horizontal_wrap", world.Manifest.HorizontalWrap);
        _material.SetShaderParameter("classified_beaches", stage == GenerationStage.Terrain && world.Manifest.GeneratorVersion >= 3);
        previousTextures?.Dispose();

        Vector2 origin = new(-HexGrid.Root3 * 0.5f, -1f);
        Vector2 limit = new(HexGrid.Root3 * world.Width, 1.5f * (world.Height - 1) + 1f);
        _surface.Polygon = new[] { origin, new Vector2(limit.X, origin.Y), limit, new Vector2(origin.X, limit.Y) };
        _mapSize = limit - origin;

        _world = world;
        _layerSelect.SetItemDisabled(1, stage < GenerationStage.Relief);
        for (int i = 2; i <= 4; i++)
            _layerSelect.SetItemDisabled(i, stage < GenerationStage.Terrain);
        if (_layerSelect.IsItemDisabled(_layer))
        {
            _layerSelect.Select(0);
            SetLayer(0);
        }
        _performance.Text = $"{world.Width} × {world.Height}";
        SelectHex(new Vector2I(-1, -1));
        FitMap();
    }

    private void FitMap()
    {
        if (_world is null)
            return;
        Vector2 area = GetViewportRect().Size;
        float zoom = Mathf.Max(0.015f, Mathf.Min((area.X - 80f) / _mapSize.X, (area.Y - 168f) / _mapSize.Y));
        _map.Scale = Vector2.One * zoom;
        Vector2 origin = new(-HexGrid.Root3 * 0.5f, -1f);
        _map.Position = (area - _mapSize * zoom) * 0.5f - origin * zoom;
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

    private void SelectHex(Vector2I offset)
    {
        if (_world is not WorldData world)
            return;
        if (offset.X < 0 || offset.X >= world.Width || offset.Y < 0 || offset.Y >= world.Height)
            offset = new(-1, -1);
        _selected = offset;
        _material.SetShaderParameter("selected", offset);
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
        _inspection.Text = label;
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

    private static string ReliefLabel(ReliefType relief) => relief switch
    {
        ReliefType.Rolling => "Ondulado", ReliefType.Hills => "Morros",
        ReliefType.Mountains => "Montanhas", ReliefType.Peak => "Picos", _ => "Plano"
    };

    private static string BiomeLabel(BiomeType biome) => biome switch
    {
        BiomeType.Beach => "Praia", BiomeType.Grassland => "Campo",
        BiomeType.TemperateForest => "Floresta temperada", BiomeType.Rainforest => "Floresta tropical",
        BiomeType.Savanna => "Savana", BiomeType.Steppe => "Estepe", BiomeType.Desert => "Deserto",
        BiomeType.Taiga => "Taiga", BiomeType.Tundra => "Tundra", BiomeType.Alpine => "Alpino", _ => "Sem bioma terrestre"
    };

    private void SetLayer(int index)
    {
        _layer = index;
        _material.SetShaderParameter("layer", index);
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
