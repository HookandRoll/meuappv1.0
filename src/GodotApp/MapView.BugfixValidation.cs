using System;
using System.Linq;
using Godot;
using Mapmaker.Core;

namespace Mapmaker.GodotApp;

public partial class MapView
{
    private async void VerifyEditorBugfixes()
    {
        try
        {
            var source = new WorldData(TestManifest(32, 32, TerrainAlgorithm.ContinentalSculpt) with { GeneratorVersion = WorldGenerator.Version });
            Array.Fill(source.Water, WaterType.Ocean);
            Array.Fill(source.Elevation, (short)-1000);
            for (int row = 8; row < 24; row++)
            for (int col = 8; col < 24; col++)
            {
                int cell = row * source.Width + col;
                source.Water[cell] = WaterType.None;
                source.Elevation[cell] = 100;
            }
            for (int cell = 0; cell < source.Count; cell++) source.RefreshCoast(cell);
            InstallWorld(source); RefreshInterface(); SetLayer(7);
            SetEditorPanelOpen(true);
            var draft = _world!;
            var brush = new BrushSettings(BrushKind.Water, (int)WaterType.Ocean, 0, 0, BrushShape.Hexagon, 1);
            BuildBrushFootprint(brush); _stroke = brush;
            for (int row = 8; row < 24; row++)
            for (int col = 8; col < 24; col++) ApplyDab(draft, brush, HexGrid.FromOffset(col, row));
            FinishGesture();
            Require(GeographicRegions.Get(draft).Regions.All(r => r.Kind == RegionKind.Ocean), "Continente não foi apagado.");
            SetLayer(0); CancelEditorDraft();
            Require(_world == source && _regionLabels.World == source && _regionTexture is null, "Cancelar manteve dados visuais do rascunho.");
            SetLayer(7);
            Require(_regionTexture is not null && GeographicRegions.Get(source).At(16 * 32 + 16)?.Kind == RegionKind.Continent,
                "Regiões não foram restauradas após cancelar.");
            await NextDraw();

            SetLayer(0); SetEditorPanelOpen(true); draft = _world!;
            int center = 16 * 32 + 16;
            Hex hex = HexGrid.FromOffset(16, 16);
            int locked = HexGrid.NeighborIndex(hex, 0, 32, 32, false);
            draft.LockMask[locked] = (ushort)HexLocks.Flags;
            BuildBrushFootprint(brush); _stroke = brush;
            ApplyDab(draft, brush, hex); FinishGesture();
            for (int direction = 0; direction < 6; direction++)
            {
                int neighbor = HexGrid.NeighborIndex(hex, direction, 32, 32, false);
                Require(((draft.Flags[neighbor] & WorldData.CoastFlag) != 0) == (neighbor != locked), "Pincel não atualizou a margem adjacente ou ignorou o lock.");
            }
            EditHistory(false);
            Require(draft.Water[center] == WaterType.None && Enumerable.Range(0, 6).All(d =>
                (draft.Flags[HexGrid.NeighborIndex(hex, d, 32, 32, false)] & WorldData.CoastFlag) == 0), "Undo não restaurou a costa adjacente.");
            CancelEditorDraft();
            GD.Print("EDITOR BUGFIXES PASSED: continent erase, layer switching, draft cancel, region texture/labels, adjacent coasts, flag locks and undo.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
