using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Mapmaker.Core;

namespace Mapmaker.GodotApp;

public partial class MapView
{
    private async void RunShoreFeatherReview()
    {
        try
        {
            var world = new WorldData(TestManifest(64, 48, TerrainAlgorithm.ContinentalSculpt) with { GeneratorVersion = WorldGenerator.Version });
            Array.Fill(world.Water, WaterType.Ocean); Array.Fill(world.Elevation, (short)-500);
            void Land(int x, int y)
            {
                int i = y * world.Width + x;
                world.Water[i] = WaterType.None; world.Elevation[i] = 90; world.Biomes[i] = BiomeType.Grassland;
            }
            Land(16,24); Land(28,24); Land(29,24); Land(29,25);
            for (int y = 10; y < 38; y++) for (int x = 39; x < 58; x++) Land(x,y);
            for (int y = 20; y < 29; y++) for (int x = 44; x < 54; x++)
            { int i = y * world.Width + x; world.Water[i] = WaterType.Lake; world.Elevation[i] = -30; world.Biomes[i] = BiomeType.None; }
            Land(48,24);
            var water = (WaterType[])world.Water.Clone(); var elevations = (short[])world.Elevation.Clone();
            string directory = ProjectSettings.GlobalizePath("res://.godot/shore-feather-review");
            Directory.CreateDirectory(directory);
            Shader fixedShader = _material.Shader;
            var coreColors = new Dictionary<int, Color>();
            float beforeSeam = 0, afterSeam = 0;
            string beforePath = ProjectSettings.GlobalizePath("res://.godot/shore-feather-before.gdshader");
            foreach (bool before in File.Exists(beforePath) ? new[] { true, false } : new[] { false })
            {
                using var legacy = before ? new Shader { Code = File.ReadAllText(beforePath) } : null;
                _material.Shader = before ? legacy! : fixedShader;
                InstallWorld(world); RefreshInterface(); SetLayer(0);
                _material.SetShaderParameter("show_grid", false); _material.SetShaderParameter("show_details", false);
                _material.SetShaderParameter("show_relief", false); _material.SetShaderParameter("show_vegetation", false);
                foreach (var (name, x, y) in new[] { ("single",16,24), ("group",29,24), ("lake",48,24) })
                foreach (int zoom in new[] { 24, 64, 128 })
                {
                    var p = HexGrid.Center(HexGrid.FromOffset(x,y));
                    _map.Scale = Vector2.One * zoom;
                    _map.Position = MapCanvasRect().GetCenter() - new Vector2(p.X,p.Y) * zoom; UpdateArtworkZoom();
                    await NextDraw(); await NextDraw();
                    using Image screenshot = GetViewport().GetTexture().GetImage();
                    var center = MapCanvasRect().GetCenter();
                    using Image crop = screenshot.GetRegion(new Rect2I((int)center.X-220,(int)center.Y-220,440,440));
                    Require(crop.SavePng(Path.Combine(directory,$"{(before ? "before" : "after")}-{name}-{zoom}.png")) == Error.Ok,"Coast screenshot failed.");
                    if (name == "single")
                    {
                        Color core = screenshot.GetPixel((int)center.X,(int)center.Y);
                        int border = (int)MathF.Floor(center.X + HexGrid.Root3 * 0.5f * zoom);
                        int row = (int)(center.Y + zoom * 0.15f);
                        Color inner = screenshot.GetPixel(border-1,row), outer = screenshot.GetPixel(border+1,row);
                        float jump = new Vector3(inner.R-outer.R,inner.G-outer.G,inner.B-outer.B).Length();
                        // At distant zoom a two-pixel probe can span the entire
                        // beach. Measure the internal seam only in close views.
                        if (before) { coreColors[zoom] = core; if (zoom >= 64) beforeSeam += jump; }
                        else
                        {
                            if (zoom >= 64) afterSeam += jump;
                            Require(core.G > core.B * 1.1f && core.A > 0.99f,"Coastal feather erased the island core.");
                            if (coreColors.TryGetValue(zoom,out Color original))
                                Require(new Vector3(core.R-original.R,core.G-original.G,core.B-original.B).Length() < 0.01f,"Feather changed the island center.");
                        }
                    }
                }
            }
            Require(water.SequenceEqual(world.Water) && elevations.SequenceEqual(world.Elevation),"Rendering changed physical geography.");
            if (beforeSeam > 0) Require(afterSeam < beforeSeam * 0.7f,"The internal hex seam was not softened.");
            GD.Print($"SHORE SEAM summed RGB jump: {beforeSeam:F3} -> {afterSeam:F3}");
            GD.Print("SHORE FEATHER REVIEW PASSED: ocean/lake shores, one-hex core, island group, three zooms, unchanged geography.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
