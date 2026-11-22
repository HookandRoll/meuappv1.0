using System;
using Godot;
using Mapmaker.Core;

namespace Mapmaker.GodotApp;

public partial class MapView : Node2D
{
    private const int WindLayer = 8;
    private static readonly string[] Layers =
        { "Terreno", "Elevação", "Temperatura", "Chuva", "Biomas", "Locks", "Tectônica", "Regiões", "Ventos" };
    private static readonly Color Ink = new("#ebe2cc");
    private static readonly Color Muted = new("#a89b84");
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
    private Vector2 _mapOrigin;
    private Vector2I _selected = new(-1, -1);
    private int _layer;
    private TectonicOverlay _tectonicOverlay = null!;
    private WindOverlay _windOverlay = null!;
    private RiverOverlay _riverOverlay = null!;
    private VolcanoOverlay _volcanoOverlay = null!;
    private bool _showVolcanoes = true;
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
        _volcanoOverlay = new VolcanoOverlay();
        _map.AddChild(_volcanoOverlay);
        _tectonicOverlay = new TectonicOverlay { Visible = false };
        _map.AddChild(_tectonicOverlay);
        _windOverlay = new WindOverlay { Visible = false };
        _map.AddChild(_windOverlay);
        _regionLabels=new RegionLabels { Visible=false };_map.AddChild(_regionLabels);
        BuildInterface();
        GetViewport().SizeChanged += FitMap;
        GetTree().AutoAcceptQuit = false;

        string[] args = OS.GetCmdlineUserArgs();
        string? benchmarkSize = Array.Find(args, arg => arg.StartsWith("--benchmark-size="));
        if (Array.IndexOf(args, "--wind-review") >= 0)
            Callable.From(RunWindReview).CallDeferred();
        else if (Array.IndexOf(args, "--annual-climate-review") >= 0)
            Callable.From(RunAnnualClimateReview).CallDeferred();
        else if (Array.IndexOf(args, "--shore-feather-review") >= 0)
            Callable.From(RunShoreFeatherReview).CallDeferred();
        else if (Array.IndexOf(args, "--regions-review") >= 0)
            Callable.From(RunRegionReview).CallDeferred();
        else if (Array.IndexOf(args, "--menu-review") >= 0)
            Callable.From(RunMenuReview).CallDeferred();
        else if (Array.IndexOf(args, "--ocean-frame-review") >= 0)
            Callable.From(RunOceanFrameReview).CallDeferred();
        else if (Array.IndexOf(args, "--river-clearance-review") >= 0)
            Callable.From(RunRiverClearanceReview).CallDeferred();
        else if (Array.IndexOf(args, "--terrain-classification-review") >= 0)
            Callable.From(RunTerrainClassificationReview).CallDeferred();
        else if (Array.IndexOf(args, "--water-shapes-review") >= 0)
            Callable.From(RunWaterShapesReview).CallDeferred();
        else if (Array.IndexOf(args, "--boundary-review") >= 0)
            Callable.From(RunBoundaryReview).CallDeferred();
        else if (Array.IndexOf(args, "--biome-majority-review") >= 0)
            Callable.From(RunBiomeMajorityReview).CallDeferred();
        else if (Array.IndexOf(args, "--expanded-biomes-review") >= 0)
            Callable.From(RunExpandedBiomeReview).CallDeferred();
        else if (Array.IndexOf(args, "--verify-map-format") >= 0)
            Callable.From(VerifyMapFormatOnGpu).CallDeferred();
        else if (benchmarkSize is not null)
            Callable.From(() => RunSizeBenchmark(int.Parse(benchmarkSize.Split('=')[1]))).CallDeferred();
        else if (Array.IndexOf(args, "--climate-relief-review") >= 0)
            Callable.From(RunClimateReliefReview).CallDeferred();
        else if (Array.IndexOf(args, "--climate-height-review") >= 0)
            Callable.From(RunClimateHeightReview).CallDeferred();
        else if (Array.IndexOf(args, "--validate-regions-current") >= 0)
            Callable.From(() => RunV29RegionalValidation(WorldGenerator.Version)).CallDeferred();
        else if (Array.IndexOf(args, "--validate-editor-current") >= 0)
            Callable.From(() => RunV29EditorValidation(WorldGenerator.Version)).CallDeferred();
        else if (Array.IndexOf(args, "--validate-regions-v29") >= 0)
            Callable.From(() => RunV29RegionalValidation()).CallDeferred();
        else if (Array.IndexOf(args, "--validate-editor-v29") >= 0)
            Callable.From(() => RunV29EditorValidation()).CallDeferred();
        else if (Array.IndexOf(args, "--gentle-ridge-review") >= 0)
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
        _regionTexture?.Dispose();
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
        _performance.Text = world.Domain.IsAtlas
            ? $"{world.Width} × {world.Height}  ·  {world.ActiveCount:N0} hexes  ·  {Engine.GetFramesPerSecond():F0} FPS"
            : $"{world.Width} × {world.Height}  ·  {Engine.GetFramesPerSecond():F0} FPS";
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
        _material.SetShaderParameter("natural_water_shapes", world.Manifest.GeneratorVersion >= 42);
        _material.SetShaderParameter("boundary_noise_offset", BoundaryNoiseOffset(world));
        _material.SetShaderParameter("elevation_color_max", world.Manifest.GeneratorVersion >= 43 ? 8000f : 4400f);
        _material.SetShaderParameter("horizontal_wrap", world.Manifest.HorizontalWrap);

        Vector2 origin = new(-HexGrid.Root3 * 0.5f, -1f);
        Vector2 limit = new(HexGrid.Root3 * world.Width, 1.5f * (world.Height - 1) + 1f);
        bool oceanFrame = !world.Domain.IsAtlas && world.Manifest.GeneratorVersion >= 35 && world.Manifest.Generation.Extent == MapExtent.World && stage != GenerationStage.Blank;
        _material.SetShaderParameter("world_ocean_frame", oceanFrame);
        _material.SetShaderParameter("full_atlas_domain", world.Domain.IsAtlas);
        _material.SetShaderParameter("atlas_flat_poles", oceanFrame);
        uint frameSeed = unchecked((uint)world.Manifest.Seed);
        _material.SetShaderParameter("frame_noise_offset", new Vector2(frameSeed & 65535u, frameSeed >> 16) * 0.013f);
        _mapSize = limit - origin;
        if (oceanFrame)
        {
            Vector2 padding = _mapSize * 0.25f;
            origin -= padding; limit += padding;
            _mapSize = limit - origin;
        }
        _mapOrigin = origin;
        _surface.Polygon = new[] { origin, new Vector2(limit.X, origin.Y), limit, new Vector2(origin.X, limit.Y) };

        SetEditorPanelVisible(false);
        ReleaseEditorPreview();
        _world = world;
        _windOverlay.Domain = world.Domain;
        _windOverlay.QueueRedraw();
        _layerSelect.SetItemDisabled(WindLayer, stage < GenerationStage.Terrain || world.Manifest.GeneratorVersion < 55);
        _layerSelect.SetItemTooltip(WindLayer, "Direção predominante do vento médio anual. Clique em um hex para ver os pesos.");
        _regionLabels.World=world;
        _layerSelect.SetItemDisabled(7,stage==GenerationStage.Blank);
        if(_layer==7)RefreshRegions();
        _tectonicOverlay.Reference = world.Tectonics;
        _tectonicOverlay.SymbolSize = Math.Max(2, world.Height / 80f);
        _tectonicOverlay.MapHeight = world.Height * 1.5f;
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
        _volcanoOverlay.SetWorld(world, _artwork);
        GlobalDataTextures? previous = _textures;
        _textures = buffers.HasValue ? new GlobalDataTextures(world, buffers.Value) : null;
        _material.SetShaderParameter("terrain_data", _textures is null ? default : Variant.From(_textures.Terrain));
        _material.SetShaderParameter("climate_data", _textures is null ? default : Variant.From(_textures.Climate));
        _material.SetShaderParameter("map_attributes", _textures is null ? default : Variant.From(_textures.Attributes));
        _material.SetShaderParameter("feature_data", _textures is null ? default : Variant.From(_textures.Features));
        _material.SetShaderParameter("has_feature_data", _textures?.HasFeatures ?? false);
        _material.SetShaderParameter("detail_data", _textures is null ? default : Variant.From(_textures.Details));
        if (_textures is not null) _riverOverlay.BindTerrain(_textures.Terrain, _textures.Attributes, world);
        _riverOverlay.BindClearance(_material);
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
        _map.Position = area.GetCenter() - _mapSize * zoom * 0.5f - _mapOrigin * zoom;
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

    private void UpdateArtworkZoom()
    {
        float detail = Mathf.SmoothStep(12f, 22f, _map.Scale.X);
        _material.SetShaderParameter("detail_zoom", detail);
        _volcanoOverlay.SetZoom(detail);
        _regionLabels.Zoom=_map.Scale.X;_regionLabels.QueueRedraw();
    }

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
        if (_layer == WindLayer)
        {
            string edges = AnnualWind.WrapsAtlas(world) ? "Leste e oeste conectados"
                : world.Manifest.HorizontalWrap
                    ? world.Domain.IsAtlas ? "Conexão lateral parcial" : "Leste e oeste conectados"
                    : "Bordas laterais abertas";
            if (offset.X < 0)
                _inspection.Text = $"Vento predominante  ·  ↗ NE    ↖ NO    ↘ SE    ↙ SO  ·  {edges}";
            else
            {
                var (eastward, southward) = AnnualWind.DirectionWeights(offset.Y, world.Height);
                _inspection.Text = $"Hex {offset.X}, {offset.Y}  ·  Leste {eastward:P0} / Oeste {1 - eastward:P0}  ·  Norte {1 - southward:P0} / Sul {southward:P0}  ·  {edges}";
                _inspection.TooltipText = "Pesos por eixo. O transporte combina 72% ao longo da latitude e 28% entre latitudes.";
            }
            return;
        }
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
        label += GeographyDescription(world, i);
        if(stage!=GenerationStage.Blank && (!world.GeographyDirty || _layer==7))
        {
            var named=GeographicRegions.Get(world).At(i);
            if(named is not null) label+=$"  ·  {named.Name} ({GeographicRegions.Category(named.Kind)}, {named.AreaKm2:N0} km²)";
        }
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
        var namedRegion=world.GeographyDirty ? null : world.Geography?.At(i);
        if(namedRegion is not null)
            _inspection.TooltipText+=$"\nRegião #{namedRegion.Id} · {namedRegion.Cells:N0} hexes · Elevação média {namedRegion.MeanElevation:F0} m"+
                (namedRegion.Plate>=0 ? $" · Placa-base {namedRegion.Plate+1}" : "");
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
        ReliefType.Mountains => "Montanhas", ReliefType.Peak => "Picos", ReliefType.Flat => "Plano", _ => $"Relevo #{(ushort)relief}"
    };

    internal static string BiomeLabel(BiomeType biome) => biome switch
    {
        BiomeType.Beach => "Praia", BiomeType.Grassland => "Campo",
        BiomeType.TemperateForest => "Floresta temperada", BiomeType.Rainforest => "Floresta tropical",
        BiomeType.Savanna => "Savana", BiomeType.Steppe => "Estepe", BiomeType.Desert => "Deserto",
        BiomeType.Taiga => "Taiga", BiomeType.Tundra => "Tundra", BiomeType.Alpine => "Alpino", BiomeType.Swamp => "Pântano arborizado",
        BiomeType.Glacier => "Geleira polar", BiomeType.SeasonalForest => "Floresta tropical sazonal",
        BiomeType.Shrubland => "Arbustal / chaparral", BiomeType.Badlands => "Badlands / deserto rochoso",
        BiomeType.TemperateRainforest => "Floresta temperada chuvosa", BiomeType.Mangrove => "Manguezal",
        BiomeType.Bog => "Turfeira fria", BiomeType.Oasis => "Oásis / mata ciliar", BiomeType.Volcanic => "Terras vulcânicas", BiomeType.SaltFlat => "Salar",
        BiomeType.None => "Sem bioma terrestre", _ => $"Bioma #{(ushort)biome}"
    };

    internal static string LandformLabel(LandformType landform) => landform switch
    {
        LandformType.None => "Sem classificação", LandformType.Plain => "Planície",
        LandformType.RollingPlain => "Planície ondulada", LandformType.Plateau => "Planalto",
        LandformType.Escarpment => "Escarpa / Serra", LandformType.Hills => "Morros",
        LandformType.Mountains => "Montanhas", LandformType.Peak => "Pico", LandformType.Valley => "Vale / Depressão",
        LandformType.Slope => "Encosta", LandformType.Foothills => "Sopé de serra",
        _ => $"Forma geográfica #{(ushort)landform}"
    };

    internal static string GeographyDescription(WorldData world, int cell)
    {
        string result = world.Landforms[cell] == LandformType.None ? "" : $"  ·  {LandformLabel(world.Landforms[cell])}";
        GeographicFeatures features = world.Features[cell];
        if (features == GeographicFeatures.None) return result;
        var names = new System.Collections.Generic.List<string>();
        foreach (var (feature, name) in new[] { (GeographicFeatures.Volcano, "Vulcão"), (GeographicFeatures.Escarpment, "Escarpa"),
            (GeographicFeatures.Canyon, "Cânion"), (GeographicFeatures.BadlandRocks, "Mesas rochosas"), (GeographicFeatures.SeaIce, "Gelo marinho") })
            if ((features & feature) != 0) { names.Add(name); features &= ~feature; }
        if (features != 0) names.Add($"Features 0x{(ulong)features:X}");
        return result + "  ·  " + string.Join(", ", names);
    }

    private void SetLayer(int index)
    {
        bool refreshWindInspection = _layer == WindLayer || index == WindLayer;
        _layer = index;
        _layerSelect.Select(index);
        _regionLabels.Visible=index==7;
        if(index==7)RefreshRegions();
        _material.SetShaderParameter("layer", index == 6 ? 0 : index);
        _tectonicOverlay.Visible = index == 6;
        _windOverlay.Visible = index == WindLayer;
        if (_windOverlay.Visible)
            _windOverlay.QueueRedraw();
        _riverOverlay.Visible = index == 0 || index == 6;
        _volcanoOverlay.Visible = _showVolcanoes && (index == 0 || index == 6);
        if (!_busy)
            RefreshEditorClimate();
        if (refreshWindInspection)
            RefreshInspection();
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

    private static StyleBoxTexture BarStyle(float horizontal, float vertical) => MenuTheme.Wood(horizontal, vertical);

    private static Label AddLabel(Control parent, string text, int size, Color color)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        parent.AddChild(label);
        return label;
    }
}
