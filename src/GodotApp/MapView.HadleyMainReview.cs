using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Mapmaker.Core;

namespace Mapmaker.GodotApp;

public partial class MapView
{
    private async void RunHadleyMainReview()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hadley-main-ui-{Guid.NewGuid():N}.world");
        _preferencesPath = Path.ChangeExtension(path, ".cfg");
        try
        {
            WorldManifest? submitted = null;
            var handler = _creation.CreateRequested;
            _creation.CreateRequested = (manifest, _) => submitted = manifest;
            _creation.ShowForm();
            var size = (OptionButton)_creation.FindChild("MapSize", true, false);
            size.Select(0); size.EmitSignal(OptionButton.SignalName.ItemSelected, 0L);
            _creation.FindChildren("*", "LineEdit", true, false).OfType<LineEdit>()
                .Single(field => field.MaxLength == 11).Text = "1234";
            await NextDraw(); await NextDraw(); Screenshot("hadley-main-create.png");
            _creation.EmitSignal(ConfirmationDialog.SignalName.Confirmed);
            _creation.CreateRequested = handler;
            Require(submitted is not null && submitted.GeneratorVersion == WorldCreationDialog.ClimateRecipe && WorldGenerator.UsesHadleyClimate(submitted)
                && submitted.Generation.Stage == GenerationStage.Terrain, "Novo mapa não selecionou o Hadley principal.");
            await CreateWorldAsync(submitted!, fillingBlank: false);
            var world = _world ?? throw new InvalidOperationException("Mapa Hadley não foi criado.");
            var cached = world.ClimateWind;
            bool threeCells = WorldGenerator.UsesThreeCellCirculation(world.Manifest);
            Require(cached is not null && cached.Settings.UseZonalPressureBands == threeCells
                && (cached.Settings.Cells is not null) == threeCells
                && cached.Settings.Iterations == 480, "A criação não usou o perfil acoplado esperado.");
            Require(_layerSelect.ItemCount == 10 && Layers.Select((name, i) =>
                _layerSelect.GetItemText(i) == name && _layerSelect.GetItemId(i) == LayerIds[i]).All(v => v),
                "Seletores não correspondem à lista e à ordem pedidas.");
            Require(world.Manifest.GeneratorVersion == WorldGenerator.Version && world.ClimateHumidity is not null
                && world.ClimateHumidity.All(v => float.IsFinite(v) && v >= 0 && v <= 1), "Umidade relativa ausente ou inválida.");
            var humidityHash = Hash(world.ClimateHumidity!);
            var rainHash = Hash(world.Rainfall); var windHashes = HashWind(cached!);
            SetLayer(HadleyLayer);
            SelectHex(new Vector2I(world.Width / 2, world.Height / 2));
            Require(_hadleyOverlay.Visible && !_windOverlay.Visible && ReferenceEquals(_hadleyWind, cached)
                && _inspection.Text.Contains("Solo") && _layerSelect.GetItemText(LayerMenuIndex(HadleyLayer)) == "Ventos Baixos",
                "Ventos ainda mostra os pesos e setas do clima antigo.");
            foreach (var (layer, name) in new[] { (0, "terrain"), (3, "rain"), (4, "biomes"),
                (HadleyLayer, "surface"), (HadleyUpperLayer, "upper") })
            {
                SetLayer(layer); await NextDraw(); await NextDraw(); Screenshot($"hadley-main-{name}.png");
            }
            for (int menu = 0; menu < Layers.Length; menu++)
            {
                _layerSelect.EmitSignal(OptionButton.SignalName.ItemSelected, (long)menu);
                Require(_layer == LayerIds[menu] && _layerSelect.Selected == menu, "Seletor ativou uma camada incorreta.");
                await NextDraw();
            }
            SetLayer(HumidityLayer); SelectHex(new Vector2I(world.Width / 2, world.Height / 2));
            Require(_inspection.Text.Contains("Umidade relativa") && _inspection.Text.Contains("%"), "Inspeção não identifica a umidade.");
            await NextDraw(); await NextDraw(); Screenshot("alpha21-humidity.png");
            _layerSelect.ShowPopup(); await NextDraw(); await NextDraw(); Screenshot("alpha21-layers.png");
            _layerSelect.GetPopup().Hide();
            await SaveWorldAsync(path);
            await OpenWorldAsync(path);
            Require(_world is not null && WorldGenerator.UsesHadleyClimate(_world.Manifest)
                && Hash(_world.Rainfall) == rainHash, "Salvar e abrir perdeu o modo ou a chuva Hadley.");
            var timer = Stopwatch.StartNew();
            while (_hadleyWind is null && _hadleyError is null && timer.Elapsed.TotalSeconds < 60)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(_hadleyWind is not null && _hadleyWind.Settings.UseZonalPressureBands == threeCells
                && (_hadleyWind.Settings.Cells is not null) == threeCells
                && windHashes.SequenceEqual(HashWind(_hadleyWind))
                && _world!.ClimateHumidity is not null && humidityHash == Hash(_world.ClimateHumidity),
                "O recálculo do save não reproduziu os ventos acoplados sem bandas.");
            SetLayer(HadleyLayer);
            Require(_hadleyOverlay.Visible && !_windOverlay.Visible, "Reabrir restaurou as setas zonais antigas.");
            GD.Print($"HADLEY MAIN REVIEW PASSED: UI creation, generator={_world!.Manifest.GeneratorVersion}, threeCells={threeCells}, coupled rain/biomes, primary wind renderer, cached atmosphere, ten selectors, RH layer, exact save/load and reconstructed wind/humidity hashes.");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString()); GetTree().Quit(1);
        }
        finally { File.Delete(path); File.Delete(_preferencesPath); }
    }
}
