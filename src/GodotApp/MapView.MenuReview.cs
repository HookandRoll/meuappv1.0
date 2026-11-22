using System;
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
            var temperature = _creation.FindChild("TemperatureOffset", true, false) as OptionButton;
            var precipitation = _creation.FindChild("PrecipitationOffset", true, false) as OptionButton;
            Require(temperature is { ItemCount: 3, Selected: 0, Disabled: false } &&
                precipitation is { ItemCount: 3, Selected: 0, Disabled: false }, "Controles de clima ausentes ou sem padrão.");
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
            GD.Print("MENU REVIEW PASSED: sliders 0..100, steps 5/10, defaults 25/80, seed click, wood, fonts, compact layout.");
            GetTree().Quit();
        }
        catch(Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
