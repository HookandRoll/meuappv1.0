using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    internal static void CheckBugfixes()
    {
        var manifest = CheckManifest(128, 128, false);
        var world = new WorldData(manifest);
        var geography = GeographicRegions.Get(world);
        var region = geography.At(0)!;
        geography.Rename(region.Id, "Renomeada");
        Require(geography.At(0)?.Name == "Renomeada", "Cached rename");
        Require(geography.At(-1) is null && geography.At(world.Count) is null, "Invalid region cell");
        geography.Regions = geography.Regions.Select(r => r with { Name = "Substituída" }).ToArray();
        Require(geography.At(0)?.Name == "Substituída", "Replaced region catalogue");
        Array.Fill(world.Water, WaterType.Ocean);
        world.InvalidateGeography();
        Require(GeographicRegions.Get(world).Regions.All(r => r.Kind == RegionKind.Ocean), "Erased continent");
        string path = Path.Combine(Path.GetTempPath(), $"bugfix-{Guid.NewGuid():N}.world");
        try
        {
            var cached = world.Geography;
            WorldFile.Save(path, world, null);
            Require(ReferenceEquals(cached, world.Geography), "Unedited save reused geography");
            Require(WorldFile.Load(path).Geography!.CellIds.SequenceEqual(cached!.CellIds), "Region round trip");
        }
        finally { File.Delete(path); }

        var atlas = manifest with { HorizontalWrap = true, Generation = manifest.Generation with { Extent = MapExtent.World } };
        bool rejected = false;
        try { WorldData.Validate(atlas); } catch (ArgumentException) { rejected = true; }
        Require(rejected, "Atlas wrap rejected");
        WorldData.Validate(atlas with { GeneratorVersion = 36 });

        world = new WorldData(manifest);
        var riverPath = Enumerable.Range(10, 100).Select(x => 64 * world.Width + x).ToArray();
        var river = new RiverRecord(0, riverPath, Enumerable.Range(0, 99).Select(i => (byte)(1 + i % 5)).ToArray(),
            Enumerable.Range(0, 99).Select(i => i + 0.25f).ToArray(), "Ocean", null);
        world.Elevation[riverPath[^1]] = 30000;
        var rerouted = RerouteRiver(world, river, _ => true, new(0, 0, 4, 4));
        Require(rerouted.Path.SequenceEqual(river.Path) && rerouted.Tiers.SequenceEqual(river.Tiers) &&
            rerouted.AnnualFlow.SequenceEqual(river.AnnualFlow), "Bounded river search preserved original segment");

        var history = new UndoManager(world);
        history.CaptureFullWorld();
        world.Features[^1] = GeographicFeatures.Canyon;
        world.Landforms[^1] = LandformType.Valley;
        world.Rainfall[^1] = 4321;
        Require(history.Commit() && history.Undo(), "Compressed world undo");
        Require(world.Features[^1] == 0 && world.Landforms[^1] == 0 && world.Rainfall[^1] == 0, "All fields restored");
        Require(history.Redo() && world.Features[^1] == GeographicFeatures.Canyon && world.Rainfall[^1] == 4321, "Compressed redo");
        history.CaptureFullWorld();
        world.Features[^1] = GeographicFeatures.Volcano;
        history.Cancel();
        Require(world.Features[^1] == GeographicFeatures.Canyon && !history.Commit(), "Compressed cancel");

        CheckOversizedUndo();
        Console.WriteLine("PASS bugfixes: cached regions, erased continent, save cache, atlas validation, bounded river fallback, compressed undo/redo/cancel, oversized command eviction");
    }

    private static void CheckOversizedUndo()
    {
        var world = new WorldData(CheckManifest(2112, 1024, false));
        var history = new UndoManager(world);
        for (int row = 0; row < world.Height; row += WorldData.ChunkSize)
        for (int col = 0; col < world.Width; col += WorldData.ChunkSize)
        {
            int cell = row * world.Width + col;
            history.Capture(cell);
            world.Elevation[cell] = 123;
        }
        Require(history.Commit() && !history.CanUndo, "A single oversized command was evicted");
        history.Capture(0); world.Elevation[0] = 456;
        Require(history.Commit() && history.Undo() && world.Elevation[0] == 123, "History works after eviction");
    }

    internal static void CheckAtlasUndoMemory()
    {
        var world = new WorldData(CheckManifest(6144, 3072, false) with
        {
            Generation = new(TerrainAlgorithm.ContinentalSculpt, 3, 38, 50, GenerationStage.Blank, MapExtent.World)
        });
        var history = new UndoManager(world);
        var timer = Stopwatch.StartNew();
        for (int pass = 1; pass <= 2; pass++)
        {
            history.CaptureFullWorld();
            for (int i = 0; i < world.Count; i++) if (world.Domain.Contains(i)) world.Elevation[i] = (short)(i % 3000 + pass);
            Require(history.Commit() && history.Undo() && history.Redo(), "Maximum atlas undo/redo");
            Console.WriteLine($"PASS atlas snapshot pass={pass}: {timer.Elapsed.TotalSeconds:F2}s, managed={GC.GetTotalMemory(true) / 1048576.0:F2} MiB, peak working set={Process.GetCurrentProcess().PeakWorkingSet64 / 1048576.0:F2} MiB");
        }
    }

    internal static void CheckAtlasRegenerationMemory()
    {
        var size = MapDomain.ExpandedSize(4096, 2048);
        var manifest = CheckManifest(size.Width, size.Height, false);
        manifest = manifest with { Generation = manifest.Generation with
        {
            Extent = MapExtent.World, ReferenceWidth = 4096, ReferenceHeight = 2048,
            LandPercent = 25, CoastDetail = 80
        } };
        var timer = Stopwatch.StartNew();
        var world = Create(manifest);
        var history = new UndoManager(world);
        Console.WriteLine($"Generated atlas for regeneration stress: {world.Width}x{world.Height}, {timer.Elapsed.TotalSeconds:F2}s");
        for (int pass = 1; pass <= 2; pass++)
        {
            timer.Restart();
            history.CaptureFullWorld();
            RegenerateRegion(world, new(0, 0, world.Width, world.Height));
            bool changed = history.Commit();
            Console.WriteLine($"PASS full atlas regeneration pass={pass}: {timer.Elapsed.TotalSeconds:F2}s, changed={changed}, managed={GC.GetTotalMemory(true) / 1048576.0:F2} MiB, peak working set={Process.GetCurrentProcess().PeakWorkingSet64 / 1048576.0:F2} MiB");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
