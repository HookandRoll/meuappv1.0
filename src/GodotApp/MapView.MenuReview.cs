using System;
using System.IO;
using Godot;
using Mapmaker.Core;

namespace Mapmaker.GodotApp;

public partial class MapView
{
    private async void RunMenuReview()
    {
        try
        {
            await NextDraw(); await NextDraw(); Screenshot("menus-v49-start.png");
            _creation.ShowForm();
            await NextDraw(); await NextDraw();
            var land = _creation.FindChild("LandPercent", true, false) as HSlider;
            var coast = _creation.FindChild("CoastDetail", true, false) as HSlider;
            Require(land is not null && land.MinValue==0 && land.MaxValue==100 && land.Step==5 && land.Value==25, "Slider de terra sem intervalo/passo/default pedidos.");
            Require(coast is not null && coast.Step==10 && coast.Value==80, "Slider da costa invalido.");
            var temperature = _creation.FindChild("TemperatureOffset", true, false) as HSlider;
            var precipitation = _creation.FindChild("PrecipitationOffset", true, false) as HSlider;
            Require(temperature is { MinValue: -20, MaxValue: 20, Step: 1, Value: 0, Editable: true } &&
                precipitation is { MinValue: -50, MaxValue: 50, Step: 1, Value: 0, Editable: true }, "Sliders de clima sem intervalo, passo ou padrão pedidos.");
            temperature!.Value = -20; precipitation!.Value = 50;
            Require((_creation.FindChild("TemperatureOffsetValue", true, false) as Label)?.Text == "−20 °C" &&
                (_creation.FindChild("PrecipitationOffsetValue", true, false) as Label)?.Text == "+50%", "Valores de clima não acompanharam os sliders.");
            temperature.Value = 7; precipitation.Value = -23;
            temperature.GrabFocus();
            temperature.GetViewport().PushInput(new InputEventKey { Keycode = Key.Right, Pressed = true });
            temperature.GetViewport().PushInput(new InputEventKey { Keycode = Key.Right, Pressed = false });
            await NextDraw();
            Require(temperature.Value == 8, "Teclado não ajustou a temperatura em um grau.");
            var sliderRect = temperature.GetGlobalRect();
            var pointer = sliderRect.Position + new Vector2(sliderRect.Size.X * 0.8f, sliderRect.Size.Y * 0.5f);
            temperature.GetViewport().PushInput(new InputEventMouseButton { Position = pointer, ButtonIndex = MouseButton.Left, Pressed = true });
            temperature.GetViewport().PushInput(new InputEventMouseButton { Position = pointer, ButtonIndex = MouseButton.Left, Pressed = false });
            Require(temperature.Value > 8 && temperature.Value == Math.Round(temperature.Value), "Clique não ajustou o slider em passos inteiros.");
            temperature.Value = 7;
            WorldManifest? submitted = null;
            var create = _creation.CreateRequested;
            _creation.CreateRequested = (manifest, _) => submitted = manifest;
            _creation.EmitSignal(ConfirmationDialog.SignalName.Confirmed);
            _creation.CreateRequested = create;
            Require(submitted?.Generation.TemperatureOffsetC == 7 && submitted.Generation.PrecipitationOffsetPercent == -23,
                "Sliders não enviaram valores intermediários à geração.");
            WorldData.Validate(submitted!);
            foreach (var (heat, rain) in new[] { (-20, -50), (20, 50), (-1, 1), (7, -23) })
                WorldData.Validate(submitted! with { Generation = submitted!.Generation with { TemperatureOffsetC = heat, PrecipitationOffsetPercent = rain } });
            foreach (var (heat, rain) in new[] { (-21, 0), (21, 0), (0, -51), (0, 51) })
            {
                bool rejected = false;
                try { WorldData.Validate(submitted! with { Generation = submitted!.Generation with { TemperatureOffsetC = heat, PrecipitationOffsetPercent = rain } }); }
                catch (ArgumentException) { rejected = true; }
                Require(rejected, "Configuração de clima fora dos limites foi aceita.");
            }
            string climatePath = Path.Combine(Path.GetTempPath(), $"climate-sliders-{Guid.NewGuid():N}.world");
            try
            {
                WorldFile.Save(climatePath, new WorldData(submitted!), null);
                var saved = WorldFile.Load(climatePath).Manifest;
                Require(saved.Generation.TemperatureOffsetC == 7 && saved.Generation.PrecipitationOffsetPercent == -23,
                    "Salvar/reabrir perdeu os valores intermediários do clima.");
            }
            finally { File.Delete(climatePath); }
            _creation.ShowForm(submitted);
            Require(temperature.Value == 7 && precipitation.Value == -23, "Reabrir formulário perdeu os valores do clima.");
            _creation.ShowForm();
            Require(temperature.Value == 0 && precipitation.Value == 0, "Novo mapa não restaurou os padrões do clima.");
            Screenshot("menus-v55-create.png");
            land!.Value=0; Require(land.Value==0,"Slider sem zero.");
            land.Value=100; Require(land.Value==100,"Slider sem cem.");
            land.Value=36; Require(land.Value==35,"Passo de terra nao respeitado.");
            coast!.Value=56; Require(coast.Value==60,"Passo de costa nao respeitado.");
            _creation.Hide();
            var m=TestManifest(256,128,TerrainAlgorithm.ContinentalSculpt) with { GeneratorVersion=WorldGenerator.Version, Seed=-123456789 };
            m=m with { Generation=m.Generation with { LandPercent=35 } };
            InstallWorld(await System.Threading.Tasks.Task.Run(()=>WorldGenerator.Create(m))); RefreshInterface(); SetLayer(0);
            Require(_seedCopy.Visible && _seedCopy.Text.Contains("-123456789"),"Seed ausente do menu.");
            string? copied=null; var writer=_copySeedAction;
            _copySeedAction=text=>copied=text;
            _seedCopy.EmitSignal(BaseButton.SignalName.Pressed);
            _copySeedAction=writer;
            Require(copied=="-123456789","Clique nao copiou a seed inteira.");
            await NextDraw(); await NextDraw(); Screenshot("menus-v49-map.png");
            GetWindow().Size=new Vector2I(1100,700);
            await NextDraw(); await NextDraw();
            Require(_seedCopy.GetGlobalRect().End.X<1100,"Seed ultrapassou a janela compacta.");
            Require(_seedCopy.Size.X >= _seedCopy.GetThemeFont("font").GetStringSize(_seedCopy.Text, HorizontalAlignment.Left, -1, 13).X,"Seed cortada na janela compacta.");
            Screenshot("menus-v49-compact.png");
            _creation.ShowForm(); land.Value=100;
            await NextDraw(); await NextDraw();
            Require(_creation.Position.Y>=0 && _creation.Position.Y+_creation.Size.Y<=700,"Formulario extremo ultrapassou a janela compacta.");
            Screenshot("menus-v49-extreme.png");
            _creation.Hide();
            GD.Print("MENU REVIEW PASSED: climate sliders -20..20 C / -50..50%, step 1, keyboard/mouse, defaults, labels, limits, intermediate save/load; land/coast sliders, seed click and compact layout.");
            GetTree().Quit();
        }
        catch(Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
