using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Mapmaker.Core;

namespace Mapmaker.GodotApp;

public partial class MapView
{
    private async void RunRiverClearanceReview()
    {
        try
        {
            var world = new WorldData(TestManifest(64, 64, TerrainAlgorithm.ContinentalSculpt) with { GeneratorVersion = WorldGenerator.Version });
            Array.Fill(world.Biomes, BiomeType.TemperateForest);
            Array.Fill(world.Elevation, (short)100);
            Array.Fill(world.Temperature, (short)150);
            var trunk = new List<int>();
            int current = 5 * world.Width + 25;
            for (int step = 0; step < 43; step++)
            {
                trunk.Add(current);
                current = HexGrid.NeighborIndex(HexGrid.FromOffset(current % world.Width, current / world.Width),
                    step / 8 % 2 == 0 ? 1 : 2, world.Width, world.Height, false);
            }
            var branch = new List<int> { trunk[20] };
            for (int step = 0; step < 12; step++) branch.Add(branch[^1] - 1);
            branch.Reverse();
            var tiers = Enumerable.Range(0, trunk.Count - 1).Select(i => (byte)Math.Min(5, 1 + i / 8)).ToArray();
            var rivers = new[] { new RiverRecord(1, trunk.ToArray(), tiers, Enumerable.Repeat(20_000_000_000f, tiers.Length).ToArray(), "Wetland", null),
                new RiverRecord(2, branch.ToArray(), Enumerable.Repeat((byte)2, branch.Count - 1).ToArray(),
                    Enumerable.Repeat(1_000_000_000f, branch.Count - 1).ToArray(), "Tributary", 1) };
            WorldGenerator.WriteRivers(world, rivers);
            string original = world.SparseEntities.GetRawText();
            var edges = (ushort[])world.RiverEdges.Clone();
            var buffers = GlobalDataTextures.BuildBuffers(world);
            // Dense overlays make both partial-sprite cuts reproducible.
            for (int i = 0; i < world.Count; i++)
            {
                buffers.Surface[i * 4 + 3] |= MapRenderFormat.VegetationSymbol;
                buffers.Details[i * 4] = 8; buffers.Details[i * 4 + 1] = 0; buffers.Details[i * 4 + 2] = 0;
            }
            InstallWorld(world, buffers); RefreshInterface(); SetLayer(0);
            _material.SetShaderParameter("show_relief", false);
            var focus = HexGrid.Center(HexGrid.FromOffset(trunk[20] % world.Width, trunk[20] / world.Width));
            _map.Scale = Vector2.One * 22;
            _map.Position = MapCanvasRect().GetCenter() - new Vector2(focus.X, focus.Y) * 22;
            UpdateArtworkZoom();
            _material.SetShaderParameter("show_vegetation", false); _material.SetShaderParameter("show_details", false);
            await Frames();
            using Image ground = GetViewport().GetTexture().GetImage();
            foreach (bool vegetation in new[] { true, false })
            {
                string name = vegetation ? "vegetation" : "icons";
                _material.SetShaderParameter("show_vegetation", vegetation); _material.SetShaderParameter("show_details", !vegetation);
                _riverOverlay.ClearanceEnabled = false;
                await Frames();
                using Image before = GetViewport().GetTexture().GetImage();
                Screenshot($"river-clearance-{name}-before.png");
                _riverOverlay.ClearanceEnabled = true;
                await Frames();
                using Image after = GetViewport().GetTexture().GetImage();
                using Image mask = _riverOverlay.ClearanceTexture!.GetImage();
                Screenshot($"river-clearance-{name}-after.png");
                int cleared = 0, outsideChanges = 0;
                double beforeError = 0, afterError = 0;
                Rect2 bounds = MapCanvasRect().Grow(-8);
                for (int y = (int)bounds.Position.Y; y < bounds.End.Y; y++)
                for (int x = (int)bounds.Position.X; x < bounds.End.X; x++)
                {
                    float alpha = mask.GetPixel(x, y).A;
                    Color b = before.GetPixel(x, y), a = after.GetPixel(x, y), g = ground.GetPixel(x, y);
                    float oldError = Difference(b, g), newError = Difference(a, g);
                    if (alpha > 0.995f && oldError > 0.05f)
                    { cleared++; beforeError += oldError; afterError += newError; }
                    if (alpha < 0.00001f && Difference(b, a) > 0.025f) outsideChanges++;
                }
                GD.Print($"RIVER CLEARANCE {name}: cut pixels={cleared}, old error={beforeError:F2}, new error={afterError:F2}, outside changes={outsideChanges}");
                Require(cleared > 30 && afterError < beforeError * 0.15, $"O corredor não recortou {name} até o solo.");
                Require(outsideChanges < 12, $"O recorte de {name} alterou pixels fora da margem.");
            }
            // All camera changes must keep the mask on the exact same ribbons.
            CheckAlignment();
            _map.Position += new Vector2(47, 31); await Frames(); CheckAlignment();
            ZoomAt(MapCanvasRect().GetCenter(), 1.22f); await Frames(); CheckAlignment();
            GetWindow().Size = new Vector2I(1440, 900); await Frames(); CheckAlignment();
            Require(original == world.SparseEntities.GetRawText() && edges.SequenceEqual(world.RiverEdges), "O renderer alterou a hidrologia.");
            WorldGenerator.WriteRivers(world, Array.Empty<RiverRecord>());
            InstallWorld(world); await Frames();
            Require(!_material.GetShaderParameter("has_river_clearance").AsBool(), "Mapa sem rios manteve um recorte antigo.");
            GD.Print("RIVER CLEARANCE PASSED: vegetation/icons cut to ground, unchanged outside banks, width/curves/junctions, pan/zoom/resize, no-river reset and unchanged hydrology.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }

        async Task Frames() { for (int i = 0; i < 5; i++) await NextDraw(); }
        static float Difference(Color a, Color b) => Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));

        void CheckAlignment()
        {
            using Image mask = _riverOverlay.ClearanceTexture!.GetImage();
            int checkedPoints = 0;
            Rect2 bounds = MapCanvasRect().Grow(-5);
            foreach (var node in _riverOverlay.GetChildren().OfType<MeshInstance2D>())
            {
                var arrays = node.Mesh.SurfaceGetArrays(0);
                Vector3[] vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                for (int i = 4; i + 1 < vertices.Length; i += 6)
                {
                    Vector3 center = (vertices[i] + vertices[i + 1]) * 0.5f;
                    Vector2 screen = _riverOverlay.GetGlobalTransformWithCanvas() * new Vector2(center.X, center.Y);
                    if (!bounds.HasPoint(screen)) continue;
                    Require(mask.GetPixel((int)screen.X, (int)screen.Y).A > 0.4f, "Máscara desalinhada após mudar a câmera.");
                    checkedPoints++;
                }
            }
            Require(checkedPoints > 8, "Não há amostras suficientes de curva visível.");
            GD.Print($"RIVER CLEARANCE alignment: {checkedPoints} curve samples; viewport={mask.GetWidth()}x{mask.GetHeight()}");
        }
    }
}
