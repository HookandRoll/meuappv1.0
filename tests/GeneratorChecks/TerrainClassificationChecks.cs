using System;
using System.IO;
using System.Linq;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    internal static void CheckTerrainClassification()
    {
        var world = Flat(64, 64, 100);
        foreach (int cell in new WorldRegion(22, 22, 20, 20).Cells(world.Width)) world.Elevation[cell] = 1200;
        Expect(world, 32, 32, ReliefType.Flat, LandformType.Plateau, "flat plateau interior");
        Expect(world, 22, 32, ReliefType.Mountains, LandformType.Escarpment, "plateau rim");
        Expect(world, 21, 32, ReliefType.Hills, LandformType.Foothills, "low foot must not become a mountain");
        Expect(world, 18, 32, ReliefType.Flat, LandformType.Plain, "low plain");
        world = Flat(64, 64, 4000);
        Expect(world, 32, 32, ReliefType.Flat, LandformType.Plateau, "high elevation alone is not a peak");
        world.Elevation[32 * 64 + 32] = 4500;
        Expect(world, 32, 32, ReliefType.Peak, LandformType.Peak, "actual summit");
        world = Flat(64, 64, 1000); world.Elevation[32 * 64 + 32] = 100;
        Expect(world, 32, 32, ReliefType.Rolling, LandformType.Valley, "enclosed depression");
        world = Flat(64, 64, 1200); world.Elevation[32 * 64 + 33] = 2000;
        Expect(world, 32, 32, ReliefType.Hills, LandformType.Foothills, "uphill face must not become a plateau");
        world = Flat(64, 64, 700); world.Elevation[32 * 64 + 31] = 400;
        var coarse = new WorldData(world.Manifest with { HexDistance = 50 });
        world.Elevation.CopyTo(coarse.Elevation, 0);
        Expect(coarse, 32, 32, ReliefType.Flat, LandformType.Plateau, "physical scale");
        var fine = new WorldData(world.Manifest with { HexDistance = 1 });
        world.Elevation.CopyTo(fine.Elevation, 0);
        Expect(fine, 32, 32, ReliefType.Mountains, LandformType.Escarpment, "fine physical scale");
        foreach (int height in new[] { 256, 512, 1024 })
        {
            world = Flat(32, height, 1000);
            for (int row = 0; row < height; row++)
            for (int col = 0; col < world.Width; col++)
                world.Elevation[row * world.Width + col] = (short)Math.Clamp(1000 + (row - height / 2) * 300 * 256 / height, 1, 3000);
            Expect(world, 16, height / 2, ReliefType.Hills, LandformType.Hills, "regional resolution invariance");
        }
        world = Flat(64, 64, 1200);
        foreach (int cell in new WorldRegion(63, 0, 1, 64).Cells(64)) world.Elevation[cell] = 100;
        Expect(world, 0, 32, ReliefType.Flat, LandformType.Plateau, "open edge");
        var wrapped = new WorldData(world.Manifest with { HorizontalWrap = true });
        world.Elevation.CopyTo(wrapped.Elevation, 0);
        Expect(wrapped, 0, 32, ReliefType.Mountains, LandformType.Escarpment, "wrap neighbor");

        var size = MapDomain.ExpandedSize(128, 64);
        var atlasManifest = CheckManifest(size.Width, size.Height, false);
        atlasManifest = atlasManifest with { Generation = atlasManifest.Generation with { Extent = MapExtent.World } };
        world = new WorldData(atlasManifest);
        Array.Fill(world.Elevation, short.MinValue);
        for (int i = 0; i < world.Count; i++) if (world.Domain.Contains(i)) world.Elevation[i] = 1200;
        for (int i = 0; i < world.Count; i++)
            if (world.Domain.Contains(i)) Check(TerrainClassifier.Classify(world, i) == new TerrainClassification(ReliefType.Flat, LandformType.Plateau), "unused corners formed false cliffs");
        world = Flat(32, 32, -6000); Array.Fill(world.Water, WaterType.Lake);
        int lone = 16 * 32 + 16; world.Water[lone] = WaterType.None; world.Elevation[lone] = 1200;
        Expect(world, 16, 16, ReliefType.Flat, LandformType.Plateau, "water depths are not land relief");
        Check(TerrainClassifier.Classify(world, 0) == new TerrainClassification(ReliefType.Flat, LandformType.None), "water classification");

        world = Flat(64, 64, 100);
        TerrainClassifier.ReclassifyRegion(world, new(0, 0, 64, 64));
        int edited = 31 * 64 + 31;
        var before = world.CreateEditCopy();
        var undo = new UndoManager(world); undo.Capture(edited); world.Elevation[edited] = 1200;
        TerrainClassifier.ReclassifyAround(world, new[] { edited }, cell => undo.Capture(cell)); undo.Commit();
        Check(world.Relief[edited] == ReliefType.Mountains && world.Relief[edited + 1] == ReliefType.Hills, "editor dependency neighbors");
        undo.Undo(); Check(Same(world, before), "undo across classification/chunk boundaries");
        undo.Redo(); Check(world.Relief[edited] == ReliefType.Mountains, "redo classification");
        world = before.CreateEditCopy();
        world.LockMask[edited] = (ushort)HexLocks.Landform;
        world.LockMask[edited + 1] = (ushort)HexLocks.Relief;
        world.Features[edited] = GeographicFeatures.Canyon | (GeographicFeatures)(1UL << 63);
        world.Elevation[edited] = 1200;
        TerrainClassifier.ReclassifyAround(world, new[] { edited });
        Check(world.Landforms[edited] == LandformType.Plain && world.Relief[edited] == ReliefType.Mountains &&
            world.Relief[edited + 1] == ReliefType.Flat && world.Landforms[edited + 1] == LandformType.Foothills &&
            world.Features[edited] == (GeographicFeatures.Canyon | (GeographicFeatures)(1UL << 63)), "independent locks/features");
        world = before.CreateEditCopy(); world.Elevation[edited] = 1200;
        TerrainClassifier.ReclassifyAround(world, new[] { edited }, selection: new(31, 31, 1, 1));
        Check(world.Relief[edited + 1] == before.Relief[edited + 1], "classification escaped the editor selection");

        foreach (bool wrap in new[] { false, true })
        {
            var manifest = CheckManifest(256, 128, wrap) with { GeneratorVersion = 41 };
            var legacy = Create(manifest with { GeneratorVersion = 40 });
            world = Create(manifest);
            Check(world.Elevation.SequenceEqual(legacy.Elevation) && world.Water.SequenceEqual(legacy.Water) &&
                world.Temperature.SequenceEqual(legacy.Temperature) && world.Rainfall.SequenceEqual(legacy.Rainfall) &&
                world.Biomes.SequenceEqual(legacy.Biomes), "classification changed physical generation or climate");
            Check(legacy.Landforms.All(x => x == LandformType.None) && world.Landforms.Any(x => x == LandformType.Plateau), "versioned classification");
            for (int i = 0; i < world.Count; i++) Check(new TerrainClassification(world.Relief[i], world.Landforms[i]) == TerrainClassifier.Classify(world, i), "generation used stale elevations");
            var expected = world.CreateEditCopy();
            RegenerateRegion(world, new(29, 29, 12, 12), RegenerationMode.Everything);
            Check(Same(world, expected), "regional classification seam or determinism");
            int land = Array.FindIndex(world.Water, w => w == WaterType.None);
            world.Landforms[land] = (LandformType)60000; world.LockMask[land] = (ushort)HexLocks.Landform;
            RegenerateRegion(world, new(0, 0, world.Width, world.Height), RegenerationMode.Everything);
            Check(world.Landforms[land] == (LandformType)60000, "regeneration ignored landform lock");
            legacy.Landforms[land] = LandformType.None;
            var oldRelief = legacy.Relief[land];
            RegenerateRegion(legacy, new(land % legacy.Width, land / legacy.Width, 1, 1), RegenerationMode.Everything, HexLocks.Landform);
            Check(legacy.Landforms[land] == TerrainClassifier.Classify(legacy, land).Landform && legacy.Relief[land] == oldRelief, "landform-only regeneration changed relief");
            string path = Path.Combine(Path.GetTempPath(), $"landforms-{Guid.NewGuid():N}.world");
            try { WorldFile.Save(path, world, null); Check(Same(world, WorldFile.Load(path)), "classification save/load"); }
            finally { File.Delete(path); }
        }
        Console.WriteLine("PASS: plateaus/rims/plain/slopes/valleys/peaks, physical scale, regional resolution, water/domain/wrap, independent locks, editor undo/redo, versioned generation, regeneration and save/load");

        static WorldData Flat(int width, int height, short elevation)
        {
            var result = new WorldData(CheckManifest(width, height, false));
            Array.Fill(result.Elevation, elevation); return result;
        }
        static void Expect(WorldData w, int x, int y, ReliefType relief, LandformType form, string message)
        {
            var value = TerrainClassifier.Classify(w, y * w.Width + x);
            Check(value == new TerrainClassification(relief, form), $"{message}: got {value}, expected {relief}/{form}");
        }
        static bool Same(WorldData a, WorldData b) => a.Elevation.SequenceEqual(b.Elevation) && a.Water.SequenceEqual(b.Water) &&
            a.Relief.SequenceEqual(b.Relief) && a.Landforms.SequenceEqual(b.Landforms) && a.Biomes.SequenceEqual(b.Biomes) && a.Features.SequenceEqual(b.Features);
        static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    }
}
