using System;
using System.IO;
using System.Linq;
using Godot;
using Mapmaker.Core;

namespace Mapmaker.GodotApp;

public partial class MapView
{
    private async void RunTerrainClassificationReview()
    {
        try
        {
            var world = new WorldData(TestManifest(64, 64, TerrainAlgorithm.ContinentalSculpt) with { GeneratorVersion = WorldGenerator.Version });
            Array.Fill(world.Elevation, (short)100); Array.Fill(world.Biomes, BiomeType.Grassland);
            Array.Fill(world.Temperature, (short)150); Array.Fill(world.Rainfall, (ushort)1000);
            var plateau = new WorldRegion(27, 27, 10, 10);
            foreach (int cell in plateau.Cells(world.Width)) { world.Elevation[cell] = 1200; world.Relief[cell] = ReliefType.Hills; }
            var heights = (short[])world.Elevation.Clone();
            InstallWorld(world); RefreshInterface(); SetLayer(0);
            Show(); await NextDraw(); await NextDraw(); Screenshot("landforms-plateau-before.png");
            TerrainClassifier.ReclassifyRegion(world, new(0, 0, world.Width, world.Height));
            InstallWorld(world); Show();
            await NextDraw(); await NextDraw(); Screenshot("landforms-plateau-after.png");
            int center = 32 * 64 + 32, rim = 32 * 64 + 27, foot = rim - 1;
            Require(world.Landforms[center] == LandformType.Plateau && world.Relief[center] == ReliefType.Flat &&
                world.Landforms[rim] == LandformType.Escarpment && world.Relief[rim] == ReliefType.Mountains &&
                world.Landforms[foot] == LandformType.Foothills && heights.SequenceEqual(world.Elevation),
                "O planalto perdeu seu topo plano, sua borda ou sua altitude.");
            var buffers = GlobalDataTextures.BuildBuffers(world);
            foreach (int cell in new WorldRegion(30, 30, 4, 4).Cells(world.Width))
                Require((buffers.Surface[cell * 4 + 3] & MapRenderFormat.ReliefSymbol) == 0,
                    "O renderer colocou morros no topo plano do planalto.");
            GD.Print("LANDFORMS renderer: 100 hexes at 1200 m, flat interior, rim mountains, foothills and preserved altitude.");

            Require(plateau.Cells(world.Width).Any(cell => world.Landforms[cell] == LandformType.Plateau &&
                (buffers.Surface[cell * 4 + 3] & MapRenderFormat.MassifSymbol) != 0), "Sem ramificações para o lado alto da escarpa.");
            var highland = new WorldData(world.Manifest);
            Array.Fill(highland.Elevation, (short)100); Array.Fill(highland.Biomes, BiomeType.Grassland);
            foreach (int cell in new WorldRegion(20, 20, 24, 24).Cells(highland.Width))
            {
                highland.Elevation[cell] = 5000; highland.Biomes[cell] = BiomeType.Alpine; highland.Temperature[cell] = -180;
            }
            TerrainClassifier.ReclassifyRegion(highland, new(0, 0, 64, 64));
            var highBuffers = GlobalDataTextures.BuildBuffers(highland);
            int centerArt = new WorldRegion(25, 25, 14, 14).Cells(highland.Width).Count(cell =>
                (highBuffers.Surface[cell * 4 + 3] & MapRenderFormat.MassifSymbol) != 0);
            Require(centerArt is > 10 and < 98, "Interior alto sem montanhas ou coberto demais.");
            Require(highland.Relief[32 * 64 + 32] == ReliefType.Flat, "Arte de montanha alterou a classificação do planalto.");
            InstallWorld(highland); Show();
            _map.Scale = Vector2.One * 12;
            var highCenter = HexGrid.Center(HexGrid.FromOffset(32, 32));
            _map.Position = MapCanvasRect().GetCenter() - new Vector2(highCenter.X, highCenter.Y) * 12;
            UpdateArtworkZoom();
            await NextDraw(); await NextDraw(); Screenshot("relief-massif-highland.png");
            if (OS.GetCmdlineUserArgs().Contains("--compare-relief-spacing"))
            {
                var updatedShader = _material.Shader;
                string shaderBefore = ProjectSettings.GlobalizePath("res://.godot/relief-spacing-before.gdshader");
                using var beforeShader = new Shader { Code = File.ReadAllText(shaderBefore) };
                foreach (var (shader, name) in new[] { (beforeShader, "antes"), (updatedShader, "depois") })
                {
                    _material.Shader = shader;
                    _artwork!.Bind(_material);
                    InstallWorld(highland);
                    SetLayer(0);
                    _material.SetShaderParameter("show_grid", false);
                    _material.SetShaderParameter("show_vegetation", false);
                    _material.SetShaderParameter("show_details", false);
                    _map.Scale = Vector2.One * 12;
                    _map.Position = MapCanvasRect().GetCenter() - new Vector2(highCenter.X, highCenter.Y) * 12;
                    UpdateArtworkZoom();
                    await NextDraw(); await NextDraw();
                    Screenshot($"relevo-v56-{name}.png");
                }
            }
            GD.Print($"RELIEF MASSIFS PASSED: uphill spurs and {centerArt}/196 interior symbols, unchanged flat classification.");

            var source = new WorldData(world.Manifest);
            Array.Fill(source.Elevation, (short)100); Array.Fill(source.Biomes, BiomeType.Grassland);
            InstallWorld(source); RefreshInterface(); SetEditorPanelOpen(true);
            _editorPanel.ShowPage(1);
            var set = Descendants(_editorPanel).OfType<Button>().First(button => button.Text == "Definir");
            if (set.ToggleMode) set.ButtonPressed = true;
            set.EmitSignal(BaseButton.SignalName.Pressed);
            Descendants(_editorPanel).OfType<SpinBox>().First(box => box.Name == "BrushRadius").Value = 5;
            Descendants(_editorPanel).OfType<SpinBox>().First(box => box.Name == "BrushMeters").Value = 1200;
            Descendants(_editorPanel).OfType<HSlider>().Last().Value = 100;
            var draft = _world!;
            var before = ReadDense(draft);
            var point = HexGrid.Center(HexGrid.FromOffset(32, 32));
            StartMapEdit(_map.ToGlobal(new Vector2(point.X, point.Y))); FinishGesture();
            Require(draft.Elevation[center] == 1200 && draft.Relief[center] == ReliefType.Flat &&
                draft.Landforms[center] == LandformType.Plateau && _history!.Revision == 1,
                "O pincel não classificou o planalto em uma única operação.");
            var painted = ReadDense(draft);
            EditHistory(false); Require(ReadDense(draft).SequenceEqual(before), "Undo deixou classificação residual nos vizinhos.");
            EditHistory(true); Require(ReadDense(draft).SequenceEqual(painted), "Redo não restaurou relevo e forma geográfica.");
            _editorPanel.ShowPage(0);
            SelectHex(new Vector2I(32, 32));
            draft.LockMask[center] = (ushort)(HexLocks.Relief | HexLocks.Landform);
            EditSingleHex(HexLocks.Elevation, 2400);
            Require(draft.Elevation[center] == 2400 && draft.Relief[center] == ReliefType.Flat &&
                draft.Landforms[center] == LandformType.Plateau, "Editar altitude ignorou locks de classificação.");
            CancelEditorDraft();
            Require(source.Elevation[center] == 100 && source.Landforms[center] == LandformType.None,
                "Cancelar edição alterou o mapa original.");
            GD.Print("LANDFORMS EDITOR PASSED: elevation brush, affected neighbors, atomic undo/redo, independent locks and cancel.");
            GD.Print("TERRAIN CLASSIFICATION REVIEW PASSED");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }

        void Show()
        {
            _material.SetShaderParameter("show_grid", true);
            _material.SetShaderParameter("show_vegetation", false);
            _material.SetShaderParameter("show_details", false);
            var center = HexGrid.Center(HexGrid.FromOffset(32, 32));
            _map.Scale = Vector2.One * 22;
            _map.Position = MapCanvasRect().GetCenter() - new Vector2(center.X, center.Y) * 22;
            UpdateArtworkZoom();
        }
    }
}
