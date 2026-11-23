using System;
using System.Threading.Tasks;
using Godot;
using Mapmaker.Core;

namespace Mapmaker.GodotApp;

public partial class MapView
{
    private async void RunHadleyReview()
    {
        try
        {
            var size = MapDomain.ExpandedSize(256, 128);
            var manifest = TestManifest(size.Width, size.Height, TerrainAlgorithm.ContinentalSculpt) with { GeneratorVersion = WorldGenerator.Version };
            manifest = manifest with { Generation = manifest.Generation with { Extent = MapExtent.World, ReferenceWidth = 256, ReferenceHeight = 128 } };
            var world = await Task.Run(() => WorldGenerator.Create(manifest));
            InstallWorld(world); RefreshInterface(); SetLayer(HadleyLayer); FitMap();
            await WaitForField();
            var cached = _hadleyWind;
            var anchor = new Vector2I(world.Width / 2, world.Height * 43 / 100);
            SelectHex(anchor);
            Require(_inspection.Text.Contains("Solo") && _hadleyOverlay.Layer == HadleyAtmosphereLayer.Surface, "Inspeção de superfície incorreta.");
            await NextDraw(); Screenshot("hadley-two-layers-surface.png");
            _layerSelect.GetPopup().EmitSignal(PopupMenu.SignalName.IndexPressed, HadleyUpperLayer);
            await NextDraw();
            Require(ReferenceEquals(cached, _hadleyWind) && _inspection.Text.Contains("Altitude")
                && _layerSelect.Selected == HadleyUpperLayer && _hadleyOverlay.Layer == HadleyAtmosphereLayer.Upper,
                "Selecionar altitude não reutilizou o mesmo campo atmosférico.");
            await NextDraw(); Screenshot("hadley-two-layers-upper.png");

            SetEditorPanelOpen(true);
            EditSingleHex(HexLocks.Elevation, _world!.Elevation[anchor.Y * world.Width + anchor.X] + 1000);
            Require(_hadleyWind is null, "Editar relevo manteve o campo de vento antigo.");
            RefreshInspection(); await WaitForField();
            Require(!ReferenceEquals(cached, _hadleyWind), "Relevo não recalculou o vento.");
            CancelEditorDraft(); RefreshInspection(); await WaitForField();
            Require(ReferenceEquals(_hadleyWorld, world), "Cancelar não restaurou a origem do campo atmosférico.");

            // Replacing a map while work is pending must not install the obsolete result.
            InvalidateHadleyWind(); RefreshHadleyWind();
            var replacement = new WorldData(manifest with { Seed = 99 });
            InstallWorld(replacement); RefreshInterface(); SetLayer(HadleyUpperLayer);
            await WaitForField();
            Require(ReferenceEquals(_hadleyWorld, replacement) && _hadleyWind!.WorldWidth == replacement.Width,
                "Um resultado assíncrono antigo substituiu o mapa atual.");
            SetLayer(WindLayer);
            Require(_windOverlay.Visible && !_hadleyOverlay.Visible, "Camada de ventos original afetada.");
            GD.Print("HADLEY REVIEW PASSED: surface/upper display, inspection, shared field, background solve, terrain invalidation, cancel and map replacement.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }

        async Task WaitForField()
        {
            ulong deadline = Time.GetTicksMsec() + 60_000;
            while (_hadleyWind is null && Time.GetTicksMsec() < deadline && _hadleyError is null) await NextDraw();
            Require(_hadleyWind is not null, "Campo atmosférico não terminou: " + _hadleyError);
            await NextDraw();
        }
    }
}
