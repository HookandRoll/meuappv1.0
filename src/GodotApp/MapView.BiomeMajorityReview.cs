using System;
using System.Linq;
using Godot;
using Mapmaker.Core;

namespace Mapmaker.GodotApp;

public partial class MapView
{
    private async void RunBiomeMajorityReview()
    {
        try
        {
            WorldData? previous = null;
            foreach (int version in new[] { 44, 45 })
            {
                var world = await System.Threading.Tasks.Task.Run(() => WorldGenerator.Create(
                    TestManifest(512, 256, TerrainAlgorithm.ContinentalSculpt) with { GeneratorVersion = version }));
                if (previous is not null)
                {
                    Require(previous.Water.SequenceEqual(world.Water) && previous.Elevation.SequenceEqual(world.Elevation) &&
                        previous.Temperature.SequenceEqual(world.Temperature) && previous.Rainfall.SequenceEqual(world.Rainfall),
                        "Maioria de biomas alterou geografia ou clima.");
                    GD.Print($"BIOME MAJORITY: {world.Biomes.Where((b, i) => b != previous.Biomes[i]).Count()} changed cells; unchanged water/elevation/climate.");
                }
                previous = world;
                InstallWorld(world); RefreshInterface();
                foreach (int layer in new[] { 0, 4 })
                {
                    SetLayer(layer); _layerSelect.Select(layer);
                    await NextDraw(); await NextDraw(); Screenshot($"biome-majority-v{version}-layer-{layer}.png");
                }
            }
            GD.Print("BIOME MAJORITY REVIEW PASSED"); GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
