using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Mapmaker.Core;

namespace Mapmaker.GodotApp;

public partial class MapView
{
    private static readonly HexLocks[] V29Fields = { HexLocks.Water, HexLocks.Elevation, HexLocks.Relief,
        HexLocks.Temperature, HexLocks.Rainfall, HexLocks.Biome, HexLocks.Rivers };

    private async void RunV29RegionalValidation(int version = 29)
    {
        try
        {
            int checks = await Task.Run(() =>
            {
                int count = 0;
                var configs = new List<(int Width, int Height, int Seed, int Land, bool Wrap)>();
                foreach (int seed in new[] { 1234, 1, -77 })
                foreach (int land in new[] { 30, 50 })
                foreach (bool wrap in new[] { false, true }) configs.Add((128, 64, seed, land, wrap));
                foreach (bool wrap in new[] { false, true })
                {
                    configs.Add((193, 95, 1234, 38, wrap));
                    configs.Add((512, 256, 1234, 38, wrap));
                }
                foreach (var c in configs)
                {
                    var manifest = TestManifest(c.Width, c.Height, TerrainAlgorithm.ContinentalSculpt) with
                    { Seed = c.Seed, GeneratorVersion = version, HorizontalWrap = c.Wrap };
                    manifest = manifest with { Generation = manifest.Generation with { LandPercent = c.Land } };
                    WorldData reference = WorldGenerator.Create(manifest);
                    string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".world");
                    try
                    {
                        WorldFile.Save(path, reference, null);
                        // Cold load deliberately drops the sea-level calibration cache.
                        WorldData reopened = WorldFile.Load(path);
                        var regions = new[] { new WorldRegion(29, 29, 10, 10), new WorldRegion(0, 0, 35, 33),
                            new WorldRegion(c.Width - 9, c.Height - 7, 9, 7), new WorldRegion(c.Width / 2 - 9, c.Height / 2 - 7, 19, 15) };
                        foreach (WorldRegion region in regions)
                        foreach (HexLocks field in V29Fields.Where(f => f != HexLocks.Rivers))
                        {
                            var world = reopened.CreateEditCopy();
                            foreach (int cell in region.Cells(world.Width)) CorruptV29Field(world, cell, field);
                            var before = ReadDense(world);
                            WorldGenerator.RegenerateRegion(world, region, RegenerationMode.Everything, field);
                            var actual = ReadDense(world);
                            var expected = ReadDense(reference);
                            for (int i = 0; i < world.Count; i++)
                            {
                                bool inside = region.Contains(i % world.Width, i / world.Width);
                                if (!inside) Require(actual[i] == before[i], $"V29 exterior alterado: {c}, {region}, {field}, cell={i}");
                                else
                                {
                                    Require(V29FieldMatches(actual[i], expected[i], field),
                                        $"V29 costura: {c}, {region}, {field}, cell={i}, actual={actual[i]}, expected={expected[i]}");
                                    Require(V29WithoutField(actual[i], field) == V29WithoutField(before[i], field),
                                        $"V29 campo isolado alterou outro dado: {field}, cell={i}");
                                }
                            }
                            count++;
                        }
                    }
                    finally { File.Delete(path); }
                    GD.Print($"REGIONS v{version} passed {c}; total checks={count}");
                }
                return count;
            });
            Directory.CreateDirectory($"results/validation-v{version}");
            File.WriteAllText($"results/validation-v{version}/regions.json", JsonSerializer.Serialize(new { generatorVersion = version, checks, passed = true }));
            GD.Print($"REGIONS V{version} PASSED: {checks} checks");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private async void RunV29EditorValidation(int version = 29)
    {
        string path = Path.Combine(Path.GetTempPath(), "v29-editor-" + Guid.NewGuid() + ".world");
        _preferencesPath = Path.ChangeExtension(path, ".cfg");
        try
        {
            await VerifyEditing(path, version);
            GD.Print($"EDITOR v{version} integrated paint/locks/undo/redo/save/cancel/reopen passed");
            int checks = 0;
            foreach (bool wrap in new[] { false, true })
            foreach (HexLocks field in new[] { HexLocks.None }.Concat(V29Fields))
            foreach (RegenerationMode mode in field == HexLocks.None ? Enum.GetValues<RegenerationMode>() : new[] { RegenerationMode.Everything })
            {
                var manifest = TestManifest(128, 64, TerrainAlgorithm.ContinentalSculpt) with { GeneratorVersion = version, HorizontalWrap = wrap };
                InstallWorld(WorldGenerator.Create(manifest)); RefreshInterface(); SetEditorPanelOpen(true);
                var world = _world!;
                var region = new WorldRegion(29, 29, 12, 12);
                if (field == HexLocks.Rivers)
                {
                    var river = WorldGenerator.ReadRivers(world).First(r => r.Path.Length >= 6);
                    int anchor = river.Path[river.Path.Length / 2];
                    region = new WorldRegion(Math.Clamp(anchor % world.Width - 5, 0, world.Width - 12),
                        Math.Clamp(anchor / world.Width - 5, 0, world.Height - 12), 12, 12);
                    Require(region.Cells(world.Width).Any(i => world.RiverEdges[i] != 0), "O teste fluvial precisa conter um rio real.");
                }
                SetSelection(region);
                int locked = (region.Row + 3) * world.Width + region.Column + 3;
                world.LockMask[locked] = (ushort)HexLocks.All;
                foreach (int cell in region.Cells(world.Width))
                    if (cell != locked) CorruptV29Field(world, cell, field == HexLocks.None ? HexLocks.Rainfall : field);
                var before = ReadDense(world);
                string sparseBefore = JsonSerializer.Serialize(world.SparseEntities);
                int revision = _history!.Revision;
                await RegenerateAsync(region, mode, field);
                var after = ReadDense(world);
                string sparseAfter = JsonSerializer.Serialize(world.SparseEntities);
                foreach (var river in WorldGenerator.ReadRivers(world))
                {
                    Require(river.Path.Distinct().Count() == river.Path.Length, "V29 regeneração criou um ciclo fluvial.");
                    if (river.ParentId is int parent)
                        Require(world.RiversById[parent].Path[0] == river.Path[^1], "V29 regeneração separou uma confluência.");
                    for (int p = 1; p < river.Path.Length; p++)
                    {
                        Hex from = HexGrid.FromOffset(river.Path[p - 1] % world.Width, river.Path[p - 1] / world.Width);
                        Require(Enumerable.Range(0, 6).Any(d => HexGrid.NeighborIndex(from, d, world.Width, world.Height, wrap) == river.Path[p]),
                            "V29 regeneração criou um salto entre hexes do rio.");
                    }
                }
                Require(after[locked] == before[locked], "V29 editor perdeu lock integral.");
                for (int i = 0; i < world.Count; i++)
                {
                    if (!region.Contains(i % world.Width, i / world.Width))
                        Require(after[i] == before[i], "V29 editor alterou exterior da seleção.");
                    else if (field != HexLocks.None)
                        Require(V29WithoutField(after[i], field) == V29WithoutField(before[i], field), "V29 editor alterou campo não selecionado.");
                    else if (mode >= RegenerationMode.KeepWater)
                    {
                        Require(after[i].Water == before[i].Water, "V29 editor perdeu água preservada.");
                        if (mode == RegenerationMode.KeepWaterAndElevation)
                            Require(after[i].Elevation == before[i].Elevation && after[i].Relief == before[i].Relief,
                                "V29 editor perdeu elevação/relevo preservados.");
                    }
                }
                if (_history.Revision != revision)
                {
                    EditHistory(false);
                    Require(ReadDense(world).SequenceEqual(before) && JsonSerializer.Serialize(world.SparseEntities) == sparseBefore,
                        "V29 editor undo incompleto.");
                    EditHistory(true);
                    Require(ReadDense(world).SequenceEqual(after) && JsonSerializer.Serialize(world.SparseEntities) == sparseAfter,
                        "V29 editor redo incompleto.");
                }
                WorldFile.Save(path, world, null);
                var loaded = WorldFile.Load(path);
                Require(ReadDense(loaded).SequenceEqual(after) && JsonSerializer.Serialize(loaded.SparseEntities) == sparseAfter,
                    "V29 editor save/load incompleto.");
                CancelEditorDraft();
                GD.Print($"EDITOR v{version} wrap={wrap} mode={mode} field={field} passed");
                checks++;
            }
            Directory.CreateDirectory($"results/validation-v{version}");
            File.WriteAllText($"results/validation-v{version}/editor.json", JsonSerializer.Serialize(new { generatorVersion = version, checks, passed = true }));
            GD.Print($"EDITOR V{version} PASSED: integrated flow + {checks} regeneration operations");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
        finally { File.Delete(path); File.Delete(_preferencesPath); }
    }

    private static void CorruptV29Field(WorldData world, int cell, HexLocks field)
    {
        switch (field)
        {
            case HexLocks.Water: world.Water[cell] = world.Water[cell] == WaterType.None ? WaterType.Ocean : WaterType.None; break;
            case HexLocks.Elevation: world.Elevation[cell] = short.MaxValue; break;
            case HexLocks.Relief: world.Relief[cell] = (ReliefType)255; break;
            case HexLocks.Temperature: world.Temperature[cell] = short.MinValue; break;
            case HexLocks.Rainfall: world.Rainfall[cell] = ushort.MaxValue; break;
            case HexLocks.Biome: world.Biomes[cell] = (BiomeType)255; break;
        }
    }

    private static bool V29FieldMatches(DenseCell a, DenseCell b, HexLocks field) => field switch
    {
        HexLocks.Water => a.Water == b.Water, HexLocks.Elevation => a.Elevation == b.Elevation,
        HexLocks.Relief => a.Relief == b.Relief, HexLocks.Temperature => a.Temperature == b.Temperature,
        HexLocks.Rainfall => a.Rainfall == b.Rainfall, HexLocks.Biome => a.Biome == b.Biome, _ => false
    };

    private static DenseCell V29WithoutField(DenseCell cell, HexLocks field) => field switch
    {
        // Water may rebuild the derived coastal flag; source flags remain protected.
        HexLocks.Water => cell with { Water = 0, Flags = (byte)(cell.Flags & ~WorldData.CoastFlag) },
        HexLocks.Elevation => cell with { Elevation = 0 }, HexLocks.Relief => cell with { Relief = 0 },
        HexLocks.Temperature => cell with { Temperature = 0 }, HexLocks.Rainfall => cell with { Rainfall = 0 },
        HexLocks.Biome => cell with { Biome = 0 }, HexLocks.Rivers => cell with { Rivers = 0 }, _ => cell
    };
}
