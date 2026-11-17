using System;
using System.Diagnostics;
using Godot;
using Mapmaker.Core;

namespace Mapmaker.GodotApp;

public partial class MapView : Node2D
{
    private static readonly string[] Layers =
        { "Terreno", "Elevação", "Temperatura", "Chuva", "Biomas", "Locks" };
    private static readonly Vector2I[] Presets = { new(256, 128), new(1024, 512) };
    private static readonly Color Ink = new("#f0efdf");
    private static readonly Color Muted = new("#a9beb9");
    private Node2D _map = null!;
    private Polygon2D _surface = null!;
    private Node2D _decoration = null!;
    private ShaderMaterial _material = null!;
    private Label _inspection = null!;
    private Label _performance = null!;
    private OptionButton _layerSelect = null!;
    private DemoWorld _world = null!;
    private GlobalDataTextures? _textures;
    private Vector2 _mapSize;
    private Vector2I _selected = new(-1, -1);
    private int _layer;
    private bool _showDecorations = true;
    private double _statusTime;

    public override void _Ready()
    {
        _map = new Node2D();
        AddChild(_map);
        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://src/GodotApp/Terrain.gdshader") };
        _surface = new Polygon2D { Material = _material };
        _map.AddChild(_surface);
        BuildInterface();
        LoadWorld(Presets[1]);
        GetViewport().SizeChanged += FitMap;

        string[] args = OS.GetCmdlineUserArgs();
        if (Array.IndexOf(args, "--smoke-test") >= 0)
            RunSmokeTest();
        if (Array.IndexOf(args, "--benchmark") >= 0)
            CallDeferred(MethodName.RunBenchmark);
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= FitMap;
        _textures?.Dispose();
    }

    public override void _Process(double delta)
    {
        _statusTime += delta;
        if (_statusTime < 0.3)
            return;

        _statusTime = 0;
        _performance.Text = $"{_world.Width} × {_world.Height}  ·  {Engine.GetFramesPerSecond():F0} FPS";
    }

    public override void _UnhandledInput(InputEvent input)
    {
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

    private void LoadWorld(Vector2I size)
    {
        _world = new DemoWorld(size.X, size.Y);
        GlobalDataTextures? previousTextures = _textures;
        _textures = new GlobalDataTextures(_world);
        _material.SetShaderParameter("terrain_data", _textures.Terrain);
        _material.SetShaderParameter("climate_data", _textures.Climate);
        _material.SetShaderParameter("map_size", size);
        previousTextures?.Dispose();

        Vector2 origin = new(-HexGrid.Root3 * 0.5f, -1f);
        Vector2 limit = new(HexGrid.Root3 * size.X, 1.5f * (size.Y - 1) + 1f);
        _surface.Polygon = new[] { origin, new Vector2(limit.X, origin.Y), limit, new Vector2(origin.X, limit.Y) };
        _mapSize = limit - origin;

        if (IsInstanceValid(_decoration))
        {
            _map.RemoveChild(_decoration);
            _decoration.QueueFree();
        }
        _decoration = DecorationBatches.Create(_world);
        _map.AddChild(_decoration);
        _performance.Text = $"{size.X} × {size.Y}";
        SelectHex(new Vector2I(-1, -1));
        FitMap();
    }

    private void FitMap()
    {
        if (_mapSize.X <= 0)
            return;
        Vector2 area = GetViewportRect().Size;
        float zoom = Mathf.Max(0.05f, Mathf.Min((area.X - 80f) / _mapSize.X, (area.Y - 168f) / _mapSize.Y));
        _map.Scale = Vector2.One * zoom;
        Vector2 origin = new(-HexGrid.Root3 * 0.5f, -1f);
        _map.Position = (area - _mapSize * zoom) * 0.5f - origin * zoom;
        UpdateDecorations();
    }

    private void ZoomAt(Vector2 cursor, float factor)
    {
        float previousZoom = _map.Scale.X;
        float zoom = Mathf.Clamp(previousZoom * factor, 0.08f, 44f);
        _map.Position = cursor - (cursor - _map.Position) * (zoom / previousZoom);
        _map.Scale = Vector2.One * zoom;
        UpdateDecorations();
    }

    private void UpdateDecorations() =>
        _decoration.Visible = _showDecorations && _layer == 0 && _map.Scale.X >= 3f;

    private void SelectHex(Vector2I offset)
    {
        if (offset.X < 0 || offset.X >= _world.Width || offset.Y < 0 || offset.Y >= _world.Height)
            offset = new(-1, -1);
        _selected = offset;
        _material.SetShaderParameter("selected", offset);
        if (offset.X < 0)
        {
            _inspection.Text = "Clique em um hex para inspecionar.";
            return;
        }

        DemoHex hex = _world[offset.X, offset.Y];
        string biome = hex.Biome switch
        {
            Biome.Water => "Água",
            Biome.Forest => "Floresta",
            Biome.Highlands => "Terras altas",
            _ => "Campo"
        };
        _inspection.Text = $"Hex {offset.X}, {offset.Y}  ·  {biome}  ·  Elevação {hex.Elevation}  ·  Temp. {hex.Temperature}  ·  Chuva {hex.Rainfall}";
    }

    private void SetLayer(int index)
    {
        _layer = index;
        _material.SetShaderParameter("layer", index);
        UpdateDecorations();
    }

    private void BuildInterface()
    {
        var overlay = new CanvasLayer();
        AddChild(overlay);
        var hud = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        overlay.AddChild(hud);
        hud.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        HBoxContainer header = AddBar(hud, Control.LayoutPreset.TopWide, 18, 74);
        Label title = AddLabel(header, "World Simulation RPG", 20, Ink);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        _layerSelect = new OptionButton { CustomMinimumSize = new Vector2(132, 36) };
        foreach (string layer in Layers)
            _layerSelect.AddItem(layer);
        _layerSelect.ItemSelected += index => SetLayer((int)index);
        header.AddChild(_layerSelect);

        var sizePicker = new OptionButton { TooltipText = "Dimensões do mundo", CustomMinimumSize = new Vector2(110, 36) };
        foreach (Vector2I preset in Presets)
            sizePicker.AddItem($"{preset.X} × {preset.Y}");
        sizePicker.Select(1);
        sizePicker.ItemSelected += index => LoadWorld(Presets[(int)index]);
        header.AddChild(sizePicker);

        var grid = new CheckButton { Text = "Grade" };
        grid.Toggled += enabled => _material.SetShaderParameter("show_grid", enabled);
        header.AddChild(grid);
        var relief = new CheckButton { Text = "Relevo", ButtonPressed = true, TooltipText = "Relevo e vegetação aparecem ao aproximar" };
        relief.Toggled += enabled => { _showDecorations = enabled; UpdateDecorations(); };
        header.AddChild(relief);
        var fit = new Button { Text = "Centralizar" };
        fit.Pressed += FitMap;
        header.AddChild(fit);

        HBoxContainer footer = AddBar(hud, Control.LayoutPreset.BottomWide, -65, -18);
        _inspection = AddLabel(footer, "", 14, Ink);
        _inspection.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _inspection.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        AddLabel(footer, "Arraste com botão direito  ·  Role para zoom", 13, Muted);
        _performance = AddLabel(footer, "", 14, Ink);
    }

    private static HBoxContainer AddBar(Control parent, Control.LayoutPreset preset, int top, int bottom)
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color("#14252b"),
            BorderColor = new Color("#344e52"),
            BorderWidthTop = 1, BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
            CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10,
            CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10,
            ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 8, ContentMarginBottom = 8
        };
        var panel = new PanelContainer();
        parent.AddChild(panel);
        panel.SetAnchorsAndOffsetsPreset(preset);
        panel.OffsetLeft = 18;
        panel.OffsetRight = -18;
        panel.OffsetTop = top;
        panel.OffsetBottom = bottom;
        panel.AddThemeStyleboxOverride("panel", style);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);
        panel.AddChild(row);
        return row;
    }

    private static Label AddLabel(Control parent, string text, int size, Color color)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        parent.AddChild(label);
        return label;
    }

    private void RunSmokeTest()
    {
        for (int row = 0; row < _world.Height; row++)
        for (int column = 0; column < _world.Width; column++)
        {
            Hex hex = HexGrid.FromOffset(column, row);
            if (HexGrid.ToOffset(HexGrid.Pick(HexGrid.Center(hex))) == (column, row))
                continue;
            GD.PushError($"Round-trip failed at {column}, {row}.");
            GetTree().Quit(1);
            return;
        }
        SelectHex(new Vector2I(513, 257));
        SetLayer(3);
        if (_selected != new Vector2I(513, 257) || !_inspection.Text.Contains("513, 257") || _layer != 3)
        {
            GetTree().Quit(1);
            return;
        }
        GD.Print($"M0 smoke test passed ({_world.Width * _world.Height:N0} hex centers).");
        GetTree().Quit();
    }

    private async void RunBenchmark()
    {
        try
        {
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            for (int i = 0; i < 30; i++)
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

            using (Image screenshot = GetViewport().GetTexture().GetImage())
            {
                Error saved = screenshot.SavePng("res://.godot/benchmark-m0.png");
                if (saved != Error.Ok)
                    throw new InvalidOperationException($"Screenshot failed: {saved}");
            }

            const int frameCount = 240;
            Vector2 viewportCenter = GetViewportRect().Size * 0.5f;
            Stopwatch timer = Stopwatch.StartNew();
            for (int i = 0; i < frameCount; i++)
            {
                float zoom = 3.5f + Mathf.Sin(i * 0.04f) * 0.45f;
                var center = new Vector2(_mapSize.X * 0.71f + Mathf.Sin(i * 0.025f) * 58f, _mapSize.Y * 0.51f);
                _map.Scale = Vector2.One * zoom;
                _map.Position = viewportCenter - center * zoom;
                UpdateDecorations();
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            }
            double fps = frameCount / timer.Elapsed.TotalSeconds;
            double slowestLayer = 0;
            for (int i = 1; i <= Layers.Length; i++)
            {
                timer.Restart();
                SetLayer(i % Layers.Length);
                _layerSelect.Select(i % Layers.Length);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                slowestLayer = Math.Max(slowestLayer, timer.Elapsed.TotalMilliseconds);
            }
            timer.Restart();
            _textures!.UploadAll();
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            double upload = timer.Elapsed.TotalMilliseconds;

            GD.Print($"M0 {_world.Width}x{_world.Height}: {fps:F1} FPS moving, slowest layer {slowestLayer:F2} ms, full 4 MiB upload {upload:F2} ms (through frame submission).");
            GetTree().Quit(fps >= 30 && slowestLayer < 100 ? 0 : 2);
        }
        catch (Exception exception)
        {
            GD.PushError(exception.ToString());
            GetTree().Quit(1);
        }
    }
}
