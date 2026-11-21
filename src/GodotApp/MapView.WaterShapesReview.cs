using System;
using System.Linq;
using Godot;
using Mapmaker.Core;

namespace Mapmaker.GodotApp;

public partial class MapView
{
    private async void RunWaterShapesReview()
    {
        try
        {
            Vector2 focus = Vector2.Zero;
            foreach (int version in new[] { 41, 42 })
            {
                var manifest = TestManifest(512, 256, TerrainAlgorithm.ContinentalSculpt) with { GeneratorVersion = version };
                var world = await System.Threading.Tasks.Task.Run(() => WorldGenerator.Create(manifest));
                if (version == 41)
                {
                    // Largest closed basin, away from the map border.
                    var seen = new bool[world.Count]; int largest = 0;
                    for (int start = 0; start < world.Count; start++)
                    {
                        if (seen[start] || world.Water[start] != WaterType.Lake) continue;
                        var queue = new System.Collections.Generic.Queue<int>(); queue.Enqueue(start); seen[start] = true;
                        int count = 0; Vector2 sum = Vector2.Zero;
                        while (queue.TryDequeue(out int i))
                        {
                            count++; var hex = HexGrid.FromOffset(i % world.Width, i / world.Width);
                            var center = HexGrid.Center(hex); sum += new Vector2(center.X, center.Y);
                            for (int d = 0; d < 6; d++)
                            {
                                int n = HexGrid.NeighborIndex(hex, d, world.Width, world.Height, world.Manifest.HorizontalWrap);
                                if (n < 0 || seen[n] || world.Water[n] != WaterType.Lake) continue;
                                seen[n] = true; queue.Enqueue(n);
                            }
                        }
                        if (count > largest) { largest = count; focus = sum / count; }
                    }
                    Require(largest > 10, "Missing lake fixture.");
                }
                InstallWorld(world); RefreshInterface(); SetLayer(0);
                await NextDraw(); await NextDraw(); Screenshot($"water-shapes-v{version}-overview.png");
                _map.Scale = Vector2.One * 8;
                _map.Position = MapCanvasRect().GetCenter() - focus * 8;
                UpdateArtworkZoom();
                await NextDraw(); await NextDraw(); Screenshot($"water-shapes-v{version}-lake.png");
                GD.Print($"WATER v{version}: land={world.Water.Count(w => w == WaterType.None)}, lake={world.Water.Count(w => w == WaterType.Lake)}");
            }
            GD.Print("WATER SHAPES REVIEW PASSED"); GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
