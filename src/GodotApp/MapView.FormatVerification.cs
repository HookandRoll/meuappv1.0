using System;
using System.Buffers.Binary;
using Godot;
using Mapmaker.Core;

namespace Mapmaker.GodotApp;

public partial class MapView
{
    private async void VerifyMapFormatOnGpu()
    {
        try
        {
            var world = new WorldData(TestManifest(32, 32, TerrainAlgorithm.ContinentalSculpt));
            Array.Fill(world.Water, WaterType.Ocean);
            Array.Fill(world.Biomes, (BiomeType)60000);
            Array.Fill(world.Relief, (ReliefType)40001);
            Array.Fill(world.Landforms, (LandformType)50002);
            Array.Fill(world.Features, GeographicFeatures.Volcano | GeographicFeatures.Canyon | (GeographicFeatures)(1UL << 63));
            world.LockMask[0] = (ushort)HexLocks.Landform;
            var buffers = GlobalDataTextures.BuildBuffers(world);
            using (var textures = new GlobalDataTextures(world, buffers))
            {
                using var surface = textures.Attributes.GetLayerData(0);
                using var geography = textures.Attributes.GetLayerData(1);
                using var high = textures.Features.GetLayerData(1);
                Require(BinaryPrimitives.ReadUInt16LittleEndian(surface.GetData().AsSpan(1)) == 40001 &&
                    BinaryPrimitives.ReadUInt16LittleEndian(geography.GetData()) == 50002 && high.GetData()[3] == 128,
                    "GPU truncou os IDs ou a feature 63.");
            }
            using (var preview = new EditorPreview(world))
            {
                int stride = world.Width * WorldData.ChunkSize * 4;
                using var layer = preview.Texture.GetLayerData(0);
                byte[] bytes = layer.GetData();
                Require(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2)) == 60000 &&
                    BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(stride + 1)) == 40001 &&
                    BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(stride * 2)) == 50002 &&
                    bytes[stride * 4 + 3] == 128, "Prévia truncou os planos independentes.");
                world.Biomes[0] = (BiomeType)65535; world.Features[0] = GeographicFeatures.Canyon;
                preview.UpdateCell(world, 0); preview.Refresh(0, force: true);
                using var updated = preview.Texture.GetLayerData(0);
                bytes = updated.GetData();
                Require(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2)) == 65535 && bytes[stride * 4 + 3] == 0,
                    "Atualização da prévia preservou bytes antigos.");
                world.Biomes[0] = (BiomeType)60000;
                world.Features[0] = world.Features[1];
            }
            var icon = new uint[world.Count]; icon[0] = 60000 | MapDetailIcons.CloseZoomFlag;
            byte[] details = OceanShelf.BuildDetails(world, icon);
            Require(BinaryPrimitives.ReadUInt16LittleEndian(details) == 60000 && details[2] == 1, "Ícone invadiu os flags de zoom.");

            InstallWorld(world, GlobalDataTextures.BuildBuffers(world)); RefreshInterface();
            var expected = new[] { new Color(96 / 255f, 234 / 255f, 1 / 255f),
                new Color(65 / 255f, 156 / 255f, 82 / 255f), new Color(195 / 255f, 5 / 255f, 128 / 255f) };
            for (int mode = 1; mode <= expected.Length; mode++)
            {
                _material.SetShaderParameter("format_probe", mode);
                await NextDraw(); await NextDraw();
                using var capture = GetViewport().GetTexture().GetImage();
                Color color = capture.GetPixel(capture.GetWidth() / 2, capture.GetHeight() / 2);
                Color target = expected[mode - 1];
                Require(Math.Abs(color.R - target.R) < 2f / 255 && Math.Abs(color.G - target.G) < 2f / 255 && Math.Abs(color.B - target.B) < 2f / 255,
                    $"Shader perdeu dados no modo {mode}: {color}, esperado {target}.");
            }
            _material.SetShaderParameter("format_probe", 0);
            SetEditorPanelOpen(true);
            SelectHex(new Vector2I(16, 16));
            var draft = _world!;
            const int selected = 16 * 32 + 16;
            EditSingleHex(HexLocks.Landform, (int)LandformType.Plateau);
            Require(draft.Landforms[selected] == LandformType.Plateau && draft.Biomes[selected] == (BiomeType)60000 &&
                draft.Relief[selected] == (ReliefType)40001 && draft.Features[selected] == world.Features[selected],
                "Editar a forma geográfica alterou bioma, relevo ou características.");
            EditHistory(false);
            Require(draft.Landforms[selected] == (LandformType)50002, "Undo da forma geográfica falhou.");
            EditHistory(true);
            draft.LockMask[selected] = (ushort)HexLocks.Landform;
            EditSingleHex(HexLocks.Landform, (int)LandformType.Valley);
            Require(draft.Landforms[selected] == LandformType.Plateau, "Editor ignorou o lock da forma geográfica.");
            CancelEditorDraft();
            Require(world.Landforms[selected] == (LandformType)50002, "Cancelar edição alterou a forma geográfica original.");
            GD.Print("MAP FORMAT GPU PASSED: high IDs, simultaneous features including bit 63, icon IDs, editor updates and actual shader readback.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
