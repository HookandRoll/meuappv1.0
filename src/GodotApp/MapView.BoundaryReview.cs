using System;
using System.Linq;
using Godot;
using Mapmaker.Core;

namespace Mapmaker.GodotApp;

public partial class MapView
{
    internal static Vector2 BoundaryNoiseOffset(WorldData world)
    {
        uint value = unchecked((uint)world.Manifest.Seed) * 0x9E3779B9u;
        value = (value ^ (value >> 16)) * 0x7FEB352Du;
        return new Vector2((value & 65535) / 64f, (value >> 16) / 64f);
    }

    private async void RunBoundaryReview()
    {
        try
        {
            var world = new WorldData(TestManifest(64, 64, TerrainAlgorithm.ContinentalSculpt) with { GeneratorVersion = 44 });
            Array.Fill(world.Elevation, (short)100); Array.Fill(world.Temperature, (short)180);
            Array.Fill(world.Rainfall, (ushort)1100); Array.Fill(world.Biomes, BiomeType.Grassland);
            for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                int i = y * 64 + x;
                world.Biomes[i] = y < 30 ? BiomeType.TemperateForest : BiomeType.Savanna;
                if (x >= 29 && x <= 34 && y > 8 && y < 56)
                { world.Water[i] = WaterType.Lake; world.Elevation[i] = -50; world.Biomes[i] = BiomeType.None; }
                else if ((x == 28 || x == 35) && y > 8 && y < 56) world.Biomes[i] = BiomeType.Swamp;
            }
            var heights = (short[])world.Elevation.Clone(); var water = (WaterType[])world.Water.Clone();
            InstallWorld(world); RefreshInterface(); SetLayer(0);
            _map.Scale = Vector2.One * 16;
            var center = HexGrid.Center(HexGrid.FromOffset(32, 31));
            _map.Position = MapCanvasRect().GetCenter() - new Vector2(center.X, center.Y) * 16;
            UpdateArtworkZoom(); await NextDraw(); await NextDraw(); Screenshot("boundaries-swamp-v44.png");
            var buffers = GlobalDataTextures.BuildBuffers(world);
            Require(Enumerable.Range(0, world.Count).Any(i => world.Biomes[i] == BiomeType.Swamp &&
                (buffers.Surface[i * 4 + 3] & MapRenderFormat.VegetationSymbol) != 0), "Pântano não recebeu vegetação.");
            Require(heights.SequenceEqual(world.Elevation) && water.SequenceEqual(world.Water), "O renderer mudou a água ou altitude.");
            var generated = await System.Threading.Tasks.Task.Run(() => WorldGenerator.Create(
                TestManifest(512, 256, TerrainAlgorithm.ContinentalSculpt) with { GeneratorVersion = 44 }));
            InstallWorld(generated); RefreshInterface(); SetLayer(0);
            await NextDraw(); await NextDraw(); Screenshot("boundaries-generated-v44.png");
            GD.Print($"BOUNDARY GENERATED v44: swamp={generated.Biomes.Count(b => b == BiomeType.Swamp)}, land={generated.Water.Count(w => w == WaterType.None)}, max={generated.Elevation.Max()} m.");
            GD.Print("BOUNDARY SWAMP REVIEW PASSED: coast/biome noise, swamp ground/trees, unchanged water/elevation in renderer."); GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
