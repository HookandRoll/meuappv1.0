using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using Mapmaker.Core;

namespace Mapmaker.GodotApp;

public partial class MapView
{
    private async void RunNetworkReview()
    {
        string output = ProjectSettings.GlobalizePath("res://results/m5-networks");
        string file = Path.Combine(Path.GetTempPath(), $"m5-ui-{Guid.NewGuid():N}.world");
        Directory.CreateDirectory(output);
        try
        {
            var world = new WorldData(TestManifest(128, 64, TerrainAlgorithm.ContinentalSculpt) with { GeneratorVersion = WorldGenerator.Version });
            Array.Fill(world.Biomes, BiomeType.Grassland);
            Array.Fill(world.Elevation, (short)100);
            InstallWorld(world); RefreshInterface(); SetEditorPanelOpen(true);
            _editorPanel.ShowPage(4);
            var kind = Descendants(_editorPanel).OfType<OptionButton>().Single(c => c.Name == "NetworkKind");
            var tier = Descendants(_editorPanel).OfType<OptionButton>().Single(c => c.Name == "NetworkTier");
            var erase = Descendants(_editorPanel).OfType<CheckButton>().Single(c => c.Name == "NetworkErase");
            void ChooseKind(int value) { kind.Select(value); kind.EmitSignal(OptionButton.SignalName.ItemSelected, value); }
            Vector2 At(int x, int y)
            {
                var center = HexGrid.Center(HexGrid.FromOffset(x, y));
                return _map.ToGlobal(new Vector2(center.X, center.Y));
            }
            void Focus()
            {
                var center = HexGrid.Center(HexGrid.FromOffset(33, 33));
                _map.Scale = Vector2.One * 24f;
                _map.Position = MapCanvasRect().GetCenter() - new Vector2(center.X, center.Y) * 24f;
                UpdateArtworkZoom();
            }
            async System.Threading.Tasks.Task Drag(int x1, int y1, int x2, int y2)
            {
                StartMapEdit(At(x1, y1)); ContinueGesture(At(x2, y2));
                Require(_networkDrawing && _networkPath.Count > 1, "Prévia de rede ausente.");
                await NextDraw(); FinishGesture(); await NextDraw();
            }
            Focus(); await NextDraw(); await NextDraw();
            for (int level = 1; level <= 3; level++)
            {
                tier.Select(tier.GetItemIndex(level));
                await Drag(28, 28 + level * 2, 38, 28 + level * 2);
                Require(HexGrid.EdgeValue(_world!.RoadEdges[(28 + level * 2) * world.Width + 31], 0) == level, "Tier de estrada não aplicado.");
            }
            ChooseKind(1); tier.Select(tier.GetItemIndex(5));
            await Drag(31, 29, 31, 38);
            Require(WorldGenerator.ReadRivers(_world!).Length == 1 && WorldGenerator.ReadRivers(_world!).Single().Tiers.All(t => t == 5), "Rio manual ausente.");
            string riverBefore = _world!.SparseEntities.GetRawText();
            EditHistory(false);
            Require(WorldGenerator.ReadRivers(_world!).Length == 0, "Undo fluvial pela interface.");
            EditHistory(true);
            Require(_world!.SparseEntities.GetRawText() == riverBefore, "Redo fluvial pela interface.");
            int[] manualPath = WorldGenerator.ReadRivers(_world).Single().Path;
            erase.ButtonPressed = true;
            int cutA = manualPath[3], cutB = manualPath[4];
            await Drag(cutA % world.Width, cutA / world.Width, cutB % world.Width, cutB / world.Width);
            Require(WorldGenerator.ReadRivers(_world).Length == 2, "Apagar trecho fluvial pela interface.");
            EditHistory(false);
            Require(_world!.SparseEntities.GetRawText() == riverBefore, "Undo do corte fluvial pela interface.");
            erase.ButtonPressed = false;
            _gridToggle.ButtonPressed = true;
            await NextDraw(); await NextDraw();
            using (var screenshot = GetViewport().GetTexture().GetImage())
                Require(screenshot.SavePng(Path.Combine(output, "networks-grid.png")) == Error.Ok, "Captura redes com grade.");
            _gridToggle.ButtonPressed = false;
            await NextDraw(); await NextDraw();
            using (var screenshot = GetViewport().GetTexture().GetImage())
                Require(screenshot.SavePng(Path.Combine(output, "networks-natural.png")) == Error.Ok, "Captura redes natural.");

            ChooseKind(0); erase.ButtonPressed = true;
            await Drag(31, 32, 33, 32);
            Require(HexGrid.EdgeValue(_world!.RoadEdges[32 * world.Width + 31], 0) == 0 &&
                HexGrid.EdgeValue(_world.RoadEdges[32 * world.Width + 30], 0) == 2, "Apagar estrada afetou exterior.");
            EditHistory(false);
            var expectedRoads = _world!.RoadEdges.ToArray();
            string expectedRivers = _world.SparseEntities.GetRawText();
            await SaveWorldAsync(file);
            Require(File.Exists(file), "Salvar redes pelo editor.");
            var loaded = WorldFile.Load(file);
            Require(expectedRoads.SequenceEqual(loaded.RoadEdges) && expectedRivers == loaded.SparseEntities.GetRawText(), "Save/load de redes pela interface.");
            SetEditorPanelOpen(true); _editorPanel.ShowPage(4); ChooseKind(0); erase.ButtonPressed = true; Focus();
            await Drag(31, 32, 33, 32);
            CancelEditorDraft();
            Require(expectedRoads.SequenceEqual(_world!.RoadEdges), "Cancelar redes perdeu origem.");
            File.WriteAllText(Path.Combine(output, "verification.json"), JsonSerializer.Serialize(new
            {
                sharedRoadTiers = new[] { 1, 2, 3 }, riverLevels = 5, drawInterpolated = true,
                editorUndoRedo = true, erasePreservedExterior = true, roundTrip = true, cancelDraft = true
            }, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print("M5 NETWORK UI PASSED: road tiers, interpolated river, preview, undo/redo, erase, save/load, cancel draft, grid/natural rendering.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
        finally { File.Delete(file); }
    }
}
