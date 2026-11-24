using System;
using System.Globalization;
using Godot;
using Mapmaker.Core;

namespace Mapmaker.GodotApp;

public partial class WorldCreationDialog : ConfirmationDialog
{
    internal static int ClimateRecipe => WorldGenerator.Version;
    internal const int DefaultLandPercent = 25;
    internal const int DefaultCoastDetail = 80;
    internal static readonly Vector2I[] Sizes =
    {
        new(128, 64), new(256, 128), new(512, 256), new(1024, 512), new(2048, 1024), new(4096, 2048)
    };

    private readonly LineEdit _name = new() { MaxLength = 120 };
    private readonly OptionButton _size = new() { Name = "MapSize" };
    private readonly SpinBox _distance = new() { MinValue = 0.1, MaxValue = 1000, Step = 0.1, Value = 5, Suffix = "km" };
    private readonly OptionButton _extent = new() { Name = "MapExtent" };
    private readonly OptionButton _stage = new();
    private readonly OptionButton _algorithm = new();
    private readonly LineEdit _seed = new() { MaxLength = 11 };
    private readonly SpinBox _continents = new() { MinValue = 1, MaxValue = 12, Value = 3 };
    private readonly HSlider _land = new() { Name = "LandPercent", MinValue = 0, MaxValue = 100, Step = 5, Value = DefaultLandPercent, TickCount = 21, TicksOnBorders = true };
    private readonly HSlider _coast = new() { Name = "CoastDetail", MinValue = 0, MaxValue = 100, Step = 10, Value = DefaultCoastDetail, TickCount = 11, TicksOnBorders = true };
    private readonly HSlider _temperature = new() { Name = "TemperatureOffset", MinValue = -20, MaxValue = 20, Step = 1, TickCount = 5, TicksOnBorders = true };
    private readonly HSlider _precipitation = new() { Name = "PrecipitationOffset", MinValue = -50, MaxValue = 50, Step = 1, TickCount = 5, TicksOnBorders = true };
    private readonly Label _hint = new();
    private readonly Label _capacityWarning = new() { Name = "WorldCapacityWarning" };
    private readonly Label _error = new();
    private readonly GridContainer _generationFields = new() { Columns = 2 };
    private WorldManifest? _existing;

    internal Action<WorldManifest, bool>? CreateRequested { get; set; }

    public override void _Ready()
    {
        DialogHideOnOk = false;
        GetCancelButton().Text = "Cancelar";
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        AddChild(scroll);
        var form = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        form.AddThemeConstantOverride("separation", 12);
        scroll.AddChild(form);
        var fields = new GridContainer { Columns = 2 };
        form.AddChild(fields);
        AddField(fields, "Nome", _name);
        foreach (Vector2I size in Sizes)
            _size.AddItem(DescribeSize(size));
        _size.AddItem("Dimensões do mapa aberto");
        _size.GetPopup().SetItemDisabled(Sizes.Length, true);
        AddField(fields, "Tamanho de referência", _size);
        _extent.AddItem("Região — recorte aberto");
        _extent.AddItem("Mundo — atlas completo");
        _extent.ItemSelected += _ => RefreshOptions();
        AddField(fields, "Tipo de mapa", _extent);
        AddField(fields, "Distância por hex", _distance);

        foreach (string stage in new[] { "Grade vazia", "Só terra e água", "Terra, água e relevo", "Terreno completo" })
            _stage.AddItem(stage);
        _stage.ItemSelected += _ => RefreshOptions();
        _size.ItemSelected += _ => RefreshOptions();
        AddField(fields, "Conteúdo", _stage);
        form.AddChild(new HSeparator());
        form.AddChild(_generationFields);

        _algorithm.AddItem("Ruído fractal (fBm)");
        _algorithm.AddItem("Centros de massas de terra");
        _algorithm.AddItem("Escultura continental (estilo Azgaar)");
        _algorithm.SetItemDisabled((int)TerrainAlgorithm.Fractal, true);
        _algorithm.SetItemDisabled((int)TerrainAlgorithm.LandmassCenters, true);
        _algorithm.SetItemTooltip((int)TerrainAlgorithm.Fractal, "Algoritmo legado. Disponível nos mundos salvos que já o utilizam.");
        _algorithm.SetItemTooltip((int)TerrainAlgorithm.LandmassCenters, "Algoritmo legado. Disponível nos mundos salvos que já o utilizam.");
        AddField(_generationFields, "Algoritmo", _algorithm);
        var seedRow = new HBoxContainer();
        _seed.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        seedRow.AddChild(_seed);
        var dice = new Button { Text = "Sortear" };
        dice.Pressed += RollSeed;
        seedRow.AddChild(dice);
        AddField(_generationFields, "Seed", seedRow);
        _continents.TooltipText = "Quantidade mínima de massas principais separadas por água. Ilhas menores podem surgir além desse número.";
        _land.TooltipText = "Alvo de terra. No Mundo, a faixa polar fica fora do cálculo e as margens oceânicas são preservadas, limitando os valores altos. Região usa todo o retângulo.";
        AddField(_generationFields, "Continentes principais", _continents);
        AddField(_generationFields, "Área de terra alvo", PercentageSlider(_land));
        AddField(_generationFields, "Irregularidade da costa", PercentageSlider(_coast));
        _temperature.TooltipText = "Ajuste em relação à temperatura padrão: de −20 a +20 °C, em passos de 1 °C.";
        _precipitation.TooltipText = "Ajuste em relação à precipitação padrão: de −50% a +50%, em passos de 1%.";
        AddField(_generationFields, "Temperatura média anual", OffsetSlider(_temperature, " °C"));
        AddField(_generationFields, "Precipitação anual", OffsetSlider(_precipitation, "%"));
        _land.ValueChanged += _ => RefreshOptions();
        form.AddChild(_hint);
        _capacityWarning.AddThemeColorOverride("font_color", new Color("#e7ba70"));
        form.AddChild(_capacityWarning);
        _error.AddThemeColorOverride("font_color", new Color("#f1a997"));
        form.AddChild(_error);
        Confirmed += Submit;
    }

    internal void ShowForm(WorldManifest? existing = null)
    {
        _existing = existing;
        Title = existing is null ? "Novo mapa" : "Gerar terreno";
        GetOkButton().Text = existing is null ? "Criar mapa" : "Gerar";
        _name.Text = existing?.WorldName ?? "Novo mundo";
        _name.Editable = existing is null;
        _size.Disabled = existing is not null;
        Vector2I dimensions = existing is null ? Sizes[1] : ReferenceSize();
        int index = Array.IndexOf(Sizes, dimensions);
        _size.SetItemText(Sizes.Length, DescribeSize(dimensions));
        _size.Select(index >= 0 ? index : Sizes.Length);
        _extent.Select((int)(existing?.Generation.Extent ?? MapExtent.World));
        _distance.Value = existing?.HexDistance ?? 5;
        _distance.Editable = existing is null;
        _stage.SetItemDisabled((int)GenerationStage.Blank, existing is not null);
        _stage.Select((int)GenerationStage.Terrain);
        _algorithm.Select((int)TerrainAlgorithm.ContinentalSculpt);
        _continents.Value = existing?.Generation.Continents ?? 3;
        _land.Value = existing?.Generation.LandPercent ?? DefaultLandPercent;
        _coast.Value = existing?.Generation.CoastDetail ?? DefaultCoastDetail;
        _temperature.Value = existing?.Generation.TemperatureOffsetC ?? 0;
        _precipitation.Value = existing?.Generation.PrecipitationOffsetPercent ?? 0;
        if (existing is null)
            RollSeed();
        else
            _seed.Text = existing.Seed.ToString(CultureInfo.InvariantCulture);
        _error.Hide();
        RefreshOptions();
        PopupCentered(FormSize());
    }

    private void RefreshOptions()
    {
        bool generate = _stage.Selected != (int)GenerationStage.Blank;
        _generationFields.Visible = generate;
        bool climate = _stage.Selected == (int)GenerationStage.Terrain;
        _temperature.Editable = _precipitation.Editable = climate;
        Vector2I reference = ReferenceSize();
        bool atlas = _extent.Selected == (int)MapExtent.World;
        var expanded = MapDomain.ExpandedSize(reference.X, reference.Y);
        _hint.Text = !generate
            ? "Cria uma grade sem geografia. Para preenchê-la,\nuse Mapa → Gerar terreno."
            : atlas
                ? $"Mundo inteiro: {expanded.Width} × {expanded.Height}, {expanded.ActiveHexes:N0} hexes úteis.\nPlacas, terreno, clima e rios são calculados em todo o atlas."
                : $"Região: retângulo de {reference.X} × {reference.Y}, {(long)reference.X * reference.Y:N0} hexes.\nO terreno pode continuar além das bordas do recorte.";
        _capacityWarning.Visible = atlas;
        _capacityWarning.Text = expanded.ActiveHexes > 8_000_000
            ? $"Atenção: {expanded.ActiveHexes:N0} hexes úteis, mais que 4000 × 2000.\nA grade de cálculo tem {(long)expanded.Width * expanded.Height:N0} posições.\nA geração usará mais memória e poderá demorar mais."
            : "Aviso: Mundo tem mais hexes que a Região equivalente\ne exige mais memória e tempo de geração.";
        if (generate && _size.Selected <= 1 && !(atlas && _land.Value > 65))
            _hint.Text += "\nMapas pequenos têm menos espaço para costas, placas e rios.\nPara mais detalhe natural, recomendamos 512 × 256 ou maior.";
        if (generate && atlas && _land.Value > 65)
            _hint.Text += "\nO alvo respeita as margens oceânicas obrigatórias;\na área de terra efetiva pode ficar abaixo do valor escolhido.";
        if (climate) _hint.Text += "\nCirculação Hadley, Ferrel e Polar — chuva simulada.";
        if (Visible)
            Size = FormSize();
    }

    private void Submit()
    {
        string name = _name.Text.Trim();
        if (name.Length == 0 || !int.TryParse(_seed.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seed))
        {
            _error.Text = name.Length == 0 ? "Digite um nome para o mapa." : "Use uma seed inteira entre −2147483648 e 2147483647.";
            _error.Show();
            return;
        }

        Vector2I reference = ReferenceSize();
        bool atlas = _extent.Selected == (int)MapExtent.World;
        var expanded = MapDomain.ExpandedSize(reference.X, reference.Y);
        var settings = new GenerationSettings(TerrainAlgorithm.ContinentalSculpt, (int)_continents.Value,
            (int)_land.Value, (int)_coast.Value, (GenerationStage)_stage.Selected, (MapExtent)_extent.Selected,
            PolarOceanRows: 0, ReferenceWidth: atlas ? reference.X : 0, ReferenceHeight: atlas ? reference.Y : 0,
            TemperatureOffsetC: (int)_temperature.Value, PrecipitationOffsetPercent: (int)_precipitation.Value);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var manifest = new WorldManifest(WorldFile.CurrentVersion, name, seed, ClimateRecipe,
            atlas ? expanded.Width : reference.X, atlas ? expanded.Height : reference.Y,
            _existing?.HorizontalWrap ?? false, _existing?.HexDistance ?? _distance.Value,
            "km", _existing?.CreatedAt ?? now, now, settings);
        Hide();
        CreateRequested?.Invoke(manifest, _existing is not null);
    }

    private Vector2I ReferenceSize()
    {
        if (_existing is null) return Sizes[Math.Clamp(_size.Selected, 0, Sizes.Length - 1)];
        if (_existing.Generation.ReferenceWidth > 0)
            return new(_existing.Generation.ReferenceWidth, _existing.Generation.ReferenceHeight);
        if (_existing.GeneratorVersion >= 37 && _existing.Generation.Extent == MapExtent.World)
        {
            foreach (Vector2I preset in Sizes)
            {
                var expanded = MapDomain.ExpandedSize(preset.X, preset.Y);
                if (expanded.Width == _existing.Width && expanded.Height == _existing.Height) return preset;
            }
            return new(Math.Clamp((int)Math.Round(_existing.Width / 1.5), 32, 4096),
                Math.Clamp((int)Math.Round(_existing.Height / 1.35), 32, 2048));
        }
        return new(_existing.Width, _existing.Height - 2 * _existing.Generation.PolarOceanRows);
    }

    private void RollSeed() => _seed.Text = Random.Shared.Next().ToString(CultureInfo.InvariantCulture);

    private static Control PercentageSlider(HSlider slider)
    {
        var row = new HBoxContainer();
        var value = new Label { Text = $"{slider.Value:0}%", CustomMinimumSize = new Vector2(52, 0), HorizontalAlignment = HorizontalAlignment.Right };
        slider.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        slider.CustomMinimumSize = new Vector2(220, 30);
        row.TooltipText = slider.TooltipText;
        row.AddChild(slider); row.AddChild(value);
        slider.ValueChanged += v => value.Text = $"{v:0}%";
        return row;
    }

    private static Control OffsetSlider(HSlider slider, string suffix)
    {
        var row = new HBoxContainer { TooltipText = slider.TooltipText };
        var value = new Label
        {
            Name = slider.Name + "Value",
            Text = slider.Value.ToString("+0;−0;0", CultureInfo.InvariantCulture) + suffix,
            CustomMinimumSize = new Vector2(64, 0),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        slider.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        slider.CustomMinimumSize = new Vector2(220, 30);
        row.AddChild(slider);
        row.AddChild(value);
        slider.ValueChanged += v => value.Text = v.ToString("+0;−0;0", CultureInfo.InvariantCulture) + suffix;
        return row;
    }

    private Vector2I FormSize() => new(Math.Min(660, GetTree().Root.Size.X),
        Math.Min(_generationFields.Visible ? 700 : 400, Math.Max(1, GetTree().Root.Size.Y - 48)));

    private static string DescribeSize(Vector2I size) => $"{size.X} × {size.Y}";

    private static void AddField(GridContainer grid, string name, Control input)
    {
        grid.AddChild(new Label { Text = name, CustomMinimumSize = new Vector2(176, 0) });
        input.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        grid.AddChild(input);
    }
}
