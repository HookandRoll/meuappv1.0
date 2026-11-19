using System;
using System.Globalization;
using Godot;
using Mapmaker.Core;

namespace Mapmaker.GodotApp;

public partial class WorldCreationDialog : ConfirmationDialog
{
    internal static readonly Vector2I[] Sizes =
    {
        new(128, 64), new(256, 128), new(512, 256), new(1024, 512), new(2048, 1024), new(4096, 2048)
    };

    private readonly LineEdit _name = new() { MaxLength = 120 };
    private readonly OptionButton _size = new();
    private readonly SpinBox _distance = new() { MinValue = 0.1, MaxValue = 1000, Step = 0.1, Value = 5, Suffix = "km" };
    private readonly OptionButton _stage = new();
    private readonly OptionButton _algorithm = new();
    private readonly LineEdit _seed = new() { MaxLength = 11 };
    private readonly SpinBox _continents = new() { MinValue = 1, MaxValue = 12, Value = 3 };
    private readonly SpinBox _land = new() { MinValue = 10, MaxValue = 65, Value = 38, Suffix = "%" };
    private readonly SpinBox _coast = new() { MinValue = 0, MaxValue = 100, Value = 50, Suffix = "%" };
    private readonly Label _hint = new();
    private readonly Label _error = new();
    private readonly GridContainer _generationFields = new() { Columns = 2 };
    private WorldManifest? _existing;

    internal Action<WorldManifest, bool>? CreateRequested { get; set; }

    public override void _Ready()
    {
        DialogHideOnOk = false;
        GetCancelButton().Text = "Cancelar";
        var form = new VBoxContainer { CustomMinimumSize = new Vector2(450, 0) };
        form.AddThemeConstantOverride("separation", 12);
        AddChild(form);
        var fields = new GridContainer { Columns = 2 };
        form.AddChild(fields);
        AddField(fields, "Nome", _name);
        foreach (Vector2I size in Sizes)
            _size.AddItem(DescribeSize(size));
        _size.AddItem("Dimensões do mapa aberto");
        _size.GetPopup().SetItemDisabled(Sizes.Length, true);
        AddField(fields, "Dimensões", _size);
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
        AddField(_generationFields, "Continentes principais", _continents);
        AddField(_generationFields, "Área de terra alvo", _land);
        AddField(_generationFields, "Irregularidade da costa", _coast);
        form.AddChild(_hint);
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
        Vector2I dimensions = existing is null ? Sizes[1] : new Vector2I(existing.Width, existing.Height);
        int index = Array.IndexOf(Sizes, dimensions);
        _size.SetItemText(Sizes.Length, DescribeSize(dimensions));
        _size.Select(index >= 0 ? index : Sizes.Length);
        _distance.Value = existing?.HexDistance ?? 5;
        _distance.Editable = existing is null;
        _stage.SetItemDisabled((int)GenerationStage.Blank, existing is not null);
        _stage.Select((int)GenerationStage.Terrain);
        _algorithm.Select((int)TerrainAlgorithm.ContinentalSculpt);
        _continents.Value = existing?.Generation.Continents ?? 3;
        _land.Value = existing?.Generation.LandPercent ?? 38;
        _coast.Value = existing?.Generation.CoastDetail ?? 50;
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
        _hint.Text = generate
            ? "A seed reproduz o terreno. Continentes podem se unir\nou formar ilhas menores."
            : "Cria uma grade sem geografia. Para preenchê-la,\nuse Mapa → Gerar terreno.";
        if (generate && _size.Selected <= 1)
            _hint.Text += "\nMapas pequenos têm menos espaço para costas, placas e rios.\nPara mais detalhe natural, recomendamos 512 × 256 ou maior.";
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

        var settings = new GenerationSettings(TerrainAlgorithm.ContinentalSculpt, (int)_continents.Value,
            (int)_land.Value, (int)_coast.Value, (GenerationStage)_stage.Selected);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Vector2I size = _existing is null ? Sizes[_size.Selected] : new Vector2I(_existing.Width, _existing.Height);
        var manifest = new WorldManifest(WorldFile.CurrentVersion, name, seed, WorldGenerator.Version,
            size.X, size.Y, _existing?.HorizontalWrap ?? false, _existing?.HexDistance ?? _distance.Value,
            "km", _existing?.CreatedAt ?? now, now, settings);
        Hide();
        CreateRequested?.Invoke(manifest, _existing is not null);
    }

    private void RollSeed() => _seed.Text = Random.Shared.Next().ToString(CultureInfo.InvariantCulture);

    private Vector2I FormSize() => new(500, _generationFields.Visible ? 448 : 284);

    private static string DescribeSize(Vector2I size) => $"{size.X} × {size.Y}";

    private static void AddField(GridContainer grid, string name, Control input)
    {
        grid.AddChild(new Label { Text = name, CustomMinimumSize = new Vector2(176, 0) });
        input.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        grid.AddChild(input);
    }
}
