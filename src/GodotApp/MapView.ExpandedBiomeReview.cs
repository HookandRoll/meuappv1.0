using System;
using System.Linq;
using Godot;
using Mapmaker.Core;

namespace Mapmaker.GodotApp;

public partial class MapView
{
    private async void RunExpandedBiomeReview()
    {
        try
        {
            VerifyArtworkResources();
            var gallery = new WorldData(TestManifest(112, 64, TerrainAlgorithm.ContinentalSculpt) with { GeneratorVersion = WorldGenerator.Version });
            Array.Fill(gallery.Elevation, (short)100); Array.Fill(gallery.Temperature, (short)180); Array.Fill(gallery.Rainfall, (ushort)1100);
            for (int i = 0; i < gallery.Count; i++)
            {
                int tile = Math.Min(20, i % 112 / 16 + (i / 112 / 21) * 7);
                gallery.Biomes[i] = (BiomeType)(tile + 1);
                if (gallery.Biomes[i] == BiomeType.Alpine) { gallery.Elevation[i] = 4500; gallery.Temperature[i] = -100; }
                if (gallery.Biomes[i] == BiomeType.Glacier) { gallery.Elevation[i] = 500; gallery.Temperature[i] = -240; }
            }
            int volcano = 48 * 112 + 88;
            gallery.Biomes[volcano] = BiomeType.Volcanic; gallery.Elevation[volcano] = 2800;
            gallery.Relief[volcano] = ReliefType.Peak; gallery.Features[volcano] = GeographicFeatures.Volcano;
            gallery.Features[50 * 112 + 8] = GeographicFeatures.Canyon;
            for (int x = 2; x < 15; x += 3) gallery.Features[55 * 112 + x] = GeographicFeatures.BadlandRocks;
            InstallWorld(gallery); RefreshInterface(); SetLayer(0);
            await NextDraw(); await NextDraw(); Screenshot("expanded-biomes-v48-gallery.png");
            var labels = new System.Collections.Generic.List<Label>();
            for (int tile = 0; tile < 21; tile++)
            {
                var p = HexGrid.Center(HexGrid.FromOffset((tile % 7) * 16 + 1, (tile / 7) * 21 + 1));
                var label = new Label { Text = BiomeLabel((BiomeType)(tile + 1)), Position = _map.ToGlobal(new Vector2(p.X, p.Y)), MouseFilter = Control.MouseFilterEnum.Ignore };
                label.AddThemeFontSizeOverride("font_size", 10); label.AddThemeColorOverride("font_color", Colors.White);
                label.AddThemeColorOverride("font_shadow_color", Colors.Black); label.AddThemeConstantOverride("shadow_offset_x", 1); label.AddThemeConstantOverride("shadow_offset_y", 1);
                AddChild(label); labels.Add(label);
            }
            await NextDraw(); Screenshot("expanded-biomes-v48-gallery.png");
            foreach (var label in labels) label.QueueFree();
            var size = MapDomain.ExpandedSize(768, 384);
            foreach (int seed in new[] { 1234, 1, -77 })
            {
                var manifest = TestManifest(size.Width, size.Height, TerrainAlgorithm.ContinentalSculpt) with { GeneratorVersion = WorldGenerator.Version, Seed = seed };
                manifest = manifest with { Generation = manifest.Generation with { Extent = MapExtent.World, ReferenceWidth = 768, ReferenceHeight = 384 } };
                var world = await System.Threading.Tasks.Task.Run(() => WorldGenerator.Create(manifest));
                InstallWorld(world); RefreshInterface(); SetLayer(0);
                await NextDraw(); await NextDraw(); Screenshot($"expanded-biomes-v48-world-{seed}.png");
                int centers = world.Features.Count(f => (f & GeographicFeatures.Volcano) != 0);
                GD.Print($"EXPANDED WORLD seed={seed}: glacier={world.Biomes.Count(b=>b==BiomeType.Glacier)}, volcanoes={centers}, volcanic={world.Biomes.Count(b=>b==BiomeType.Volcanic)}, biomeKinds={world.Biomes.Distinct().Count()}");
                Require(world.Biomes.Contains(BiomeType.Glacier), "Sem geleiras polares.");
                Require(_volcanoOverlay.CenterCount == centers, "Vulcoes ausentes do overlay.");
                Require(_volcanoOverlay.GetIndex() > _surface.GetIndex(), "Vulcao abaixo das montanhas.");
                _volcanoOverlay.SetZoom(0);
                Require(Mathf.IsEqualApprox(_volcanoOverlay.SymbolSize.X, HexGrid.Root3 * 6), "Vulcao distante sem seis hexes.");
                _volcanoOverlay.SetZoom(1);
                Require(Mathf.IsEqualApprox(_volcanoOverlay.SymbolSize.X, HexGrid.Root3 * 1.35f), "Vulcao proximo nao voltou ao tamanho normal.");
                UpdateArtworkZoom();
                if (centers > 0)
                {
                    int center = Array.FindIndex(world.Features, f => (f & GeographicFeatures.Volcano) != 0);
                    var p = HexGrid.Center(HexGrid.FromOffset(center % world.Width, center / world.Width));
                    _map.Scale = Vector2.One * 12; _map.Position = MapCanvasRect().GetCenter() - new Vector2(p.X, p.Y) * 12; UpdateArtworkZoom();
                    await NextDraw(); await NextDraw(); Screenshot($"expanded-biomes-v48-volcano-{seed}.png");
                    _map.Scale = Vector2.One * 24; _map.Position = MapCanvasRect().GetCenter() - new Vector2(p.X, p.Y) * 24; UpdateArtworkZoom();
                    await NextDraw(); await NextDraw(); Screenshot($"expanded-biomes-v48-volcano-close-{seed}.png");
                }
            }
            GD.Print("EXPANDED BIOMES REVIEW PASSED"); GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
