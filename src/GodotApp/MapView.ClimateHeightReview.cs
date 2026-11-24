using System;
using System.Linq;
using Godot;
using Mapmaker.Core;

namespace Mapmaker.GodotApp;

public partial class MapView
{
    private async void RunClimateHeightReview()
    {
        try
        {
            foreach (bool atlas in new[] { false, true })
            {
                var size = atlas ? MapDomain.ExpandedSize(384, 192) : (Width: 512, Height: 256, ActiveHexes: 512 * 256);
                WorldData? before = null;
                foreach (int version in new[] { 42, 43 })
                {
                    var manifest = TestManifest(size.Width, size.Height, TerrainAlgorithm.ContinentalSculpt) with { GeneratorVersion = version };
                    if (atlas) manifest = manifest with { Generation = manifest.Generation with {
                        Extent = MapExtent.World, ReferenceWidth = 384, ReferenceHeight = 192 } };
                    var world = await System.Threading.Tasks.Task.Run(() => WorldGenerator.Create(manifest));
                    if (version == 42) before = world;
                    else
                    {
                        Require(before!.Water.SequenceEqual(world.Water), "Clima ou altitude alteraram a cota de terra.");
                        Require(world.Elevation.Max() is >= 6000 and <= 8000, "Picos fora da faixa de altitude.");
                        Require(world.Biomes.Any(b => b == BiomeType.Desert), "Sem desertos no mundo de teste.");
                    }
                    GD.Print($"CLIMATE HEIGHT RENDER atlas={atlas} v{version}: max={world.Elevation.Max()} m, desert={world.Biomes.Count(b => b == BiomeType.Desert)}, T={world.Temperature.Min() / 10f:F1}..{world.Temperature.Max() / 10f:F1} C");
                    InstallWorld(world); RefreshInterface();
                    foreach (int layer in new[] { 0, 1, 2, 3 })
                    {
                        SetLayer(layer); _layerSelect.Select(LayerMenuIndex(layer));
                        await NextDraw(); await NextDraw();
                        Screenshot($"climate-height-{(atlas ? "world" : "region")}-v{version}-layer-{layer}.png");
                    }
                }
            }
            GD.Print("CLIMATE HEIGHT REVIEW PASSED"); GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
