using System;
using System.Threading.Tasks;
using Godot;
using Mapmaker.Core;

namespace Mapmaker.GodotApp;

public partial class MapView
{
    private async void RunWindReview()
    {
        try
        {
            var manifest = TestManifest(512, 256, TerrainAlgorithm.ContinentalSculpt) with
            {
                GeneratorVersion = WorldGenerator.Version,
                WorldName = "Ventos · Seed 1234"
            };
            manifest = manifest with
            {
                Generation = manifest.Generation with { Extent = MapExtent.World, LandPercent = 25, CoastDetail = 80 }
            };
            InstallWorld(await Task.Run(() => WorldGenerator.Create(manifest)));
            RefreshInterface();
            Require(!_layerSelect.IsItemDisabled(WindLayer), "Camada Ventos indisponível.");
            SetLayer(WindLayer);
            FitMap();
            Require(_windOverlay.Visible && _inspection.Text.Contains("Leste e oeste conectados"),
                "A camada Ventos não mostrou a conexão lateral.");
            await NextDraw(); await NextDraw();
            Screenshot("ventos-v58.png");
            SelectHex(new Vector2I(256, 180));
            Require(_inspection.Text.Contains("Leste 100") && _inspection.Text.Contains("Sul 90"),
                "A inspeção do hex não exibiu os pesos de latitude.");
            var center = HexGrid.Center(HexGrid.FromOffset(_selected.X, _selected.Y));
            ZoomAt(MapCanvasRect().GetCenter(), 8);
            _map.Position = MapCanvasRect().GetCenter() - new Vector2(center.X, center.Y) * _map.Scale.X;
            await NextDraw(); await NextDraw();
            Screenshot("ventos-v58-zoom.png");
            SetLayer(0);
            Require(!_windOverlay.Visible, "As setas permaneceram visíveis no terreno.");
            GD.Print("PASS wind layer: atlas, inspection, layer switch");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PushError(ex.ToString());
            GetTree().Quit(1);
        }
    }
}
