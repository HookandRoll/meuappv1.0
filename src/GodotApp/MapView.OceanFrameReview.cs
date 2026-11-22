using System;
using System.Linq;
using Godot;
using Mapmaker.Core;

namespace Mapmaker.GodotApp;

public partial class MapView
{
    private async void RunOceanFrameReview()
    {
        try
        {
            var m = TestManifest(512, 256, TerrainAlgorithm.ContinentalSculpt) with { GeneratorVersion = WorldGenerator.Version };
            foreach (MapExtent extent in new[] { MapExtent.Region, MapExtent.World })
            {
                bool atlas = extent == MapExtent.World;
                var size = MapDomain.ExpandedSize(m.Width, m.Height);
                var manifest = m with { Width = atlas ? size.Width : m.Width, Height = atlas ? size.Height : m.Height,
                    Generation = m.Generation with { Extent = extent, ReferenceWidth = atlas ? m.Width : 0, ReferenceHeight = atlas ? m.Height : 0 } };
                var world = await System.Threading.Tasks.Task.Run(() => WorldGenerator.Create(manifest));
                if (atlas)
                {
                    int land = Enumerable.Range(0, world.Count).Count(i => world.Water[i] == WaterType.None && world.Domain.CountsTowardLandQuota(i));
                    Require(land == (long)world.Domain.PercentageAreaCount * manifest.Generation.LandPercent / 100,
                        "O terreno deve respeitar o percentual fora dos polos.");
                    GD.Print($"ATLAS QUOTA: target={manifest.Generation.LandPercent}%, land={land}, nonpolar={world.Domain.PercentageAreaCount}, actual={100.0 * land / world.Domain.PercentageAreaCount:F4}%.");
                }
                InstallWorld(world); RefreshInterface();
                SetLayer(6); _layerSelect.Select(6);
                await NextDraw(); await NextDraw();
                if (extent == MapExtent.World)
                    for (int frame = 0; frame < 45; frame++) await NextDraw();
                Screenshot($"ocean-frame-v{WorldGenerator.Version}-{extent}.png");
                if (extent == MapExtent.World)
                {
                    Require(world.Domain.IsAtlas && !_material.GetShaderParameter("world_ocean_frame").AsBool(), "Atlas inteiro precisa usar apenas terreno real.");
                    SelectHex(new Vector2I(world.Width / 2, world.Height / 2));
                    Require(_selected == new Vector2I(world.Width / 2, world.Height / 2), "O centro do atlas deve ser selecionável.");
                    SelectHex(new Vector2I(0, 0));
                    Require(_selected == NoHex, "O canto fora do atlas não pode ser selecionado.");
                    SelectHex(new Vector2I(-3, world.Height / 2));
                    Require(_selected == NoHex, "O oceano decorativo não pode ser selecionado.");
                }
            }
            _creation.ShowForm();
            var sizeChoice = _creation.FindChild("MapSize", recursive: true, owned: false) as OptionButton;
            sizeChoice!.Select(5);
            sizeChoice.EmitSignal(OptionButton.SignalName.ItemSelected, 5L);
            await NextDraw(); await NextDraw();
            Screenshot($"atlas-capacity-warning-v{WorldGenerator.Version}.png");
            _creation.Hide();
            GD.Print("OCEAN FRAME REVIEW PASSED");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
