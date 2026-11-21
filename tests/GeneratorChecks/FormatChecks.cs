using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    internal static void CheckExtendedWorldFormat()
    {
        var world = Create(CheckManifest(32, 32, false));
        int cell = 100;
        world.Biomes[cell] = (BiomeType)65535;
        world.Relief[cell] = (ReliefType)32769;
        world.Landforms[cell] = (LandformType)60000;
        world.Features[cell] = GeographicFeatures.Volcano | GeographicFeatures.Canyon | (GeographicFeatures)(1UL << 63);
        world.LockMask[cell] = (ushort)(HexLocks.Landform | HexLocks.Features);
        Span<byte> data = stackalloc byte[20];
        MapRenderFormat.WriteHex(world, cell, data[..4], data.Slice(4, 4), data.Slice(8, 4), data.Slice(12, 4), data.Slice(16, 4));
        Require(BinaryPrimitives.ReadUInt16LittleEndian(data[2..]) == 65535 &&
            BinaryPrimitives.ReadUInt16LittleEndian(data[5..]) == 32769 &&
            BinaryPrimitives.ReadUInt16LittleEndian(data[8..]) == 60000 &&
            BinaryPrimitives.ReadUInt64LittleEndian(data[12..]) == (ulong)world.Features[cell], "GPU planes truncated an independent ID/feature.");
        Require(data[4] == (byte)world.Water[cell] && data[10] == 0 && data[11] == 1, "Metadata collided with an ID.");

        var copy = world.CreateEditCopy();
        Require(copy.Landforms.SequenceEqual(world.Landforms) && copy.Features.SequenceEqual(world.Features), "Edit copy lost geography.");
        var history = new UndoManager(world);
        history.Capture(cell);
        world.Landforms[cell] = LandformType.Plateau; world.Features[cell] = GeographicFeatures.Escarpment;
        history.Commit(); history.Undo();
        Require(world.Landforms[cell] == (LandformType)60000 && world.Features[cell] == copy.Features[cell], "Undo lost geography.");
        history.Redo();
        Require(world.Landforms[cell] == LandformType.Plateau && world.Features[cell] == GeographicFeatures.Escarpment, "Redo lost geography.");
        history.Undo();
        string path = Path.Combine(Path.GetTempPath(), $"format-v2-{Guid.NewGuid():N}.world");
        try
        {
            var saved = WorldFile.Save(path, world, null);
            var loaded = WorldFile.Load(path);
            Require(saved.WorldSaveVersion == 2 && loaded.Manifest.GeneratorVersion == world.Manifest.GeneratorVersion &&
                loaded.Biomes.SequenceEqual(world.Biomes) && loaded.Relief.SequenceEqual(world.Relief) &&
                loaded.Landforms.SequenceEqual(world.Landforms) && loaded.Features.SequenceEqual(world.Features) &&
                loaded.LockMask.SequenceEqual(world.LockMask), "Version 2 round-trip changed geography or generation version.");
            RegenerateRegion(loaded, new(0, 0, 32, 32), RegenerationMode.Everything);
            Require(loaded.Landforms[cell] == world.Landforms[cell] && loaded.Features[cell] == world.Features[cell], "Regeneration erased independent geography.");
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Update))
            {
                archive.GetEntry("layers/landforms.bin")!.Delete();
                archive.GetEntry("layers/features.bin")!.Delete();
            }
            loaded = WorldFile.Load(path);
            Require(loaded.Landforms.All(x => x == LandformType.None) && loaded.Features.All(x => x == GeographicFeatures.None), "Absent optional layers must default to None.");
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Update))
            {
                archive.GetEntry("layers/biomes.bin")!.Delete();
                using var truncated = archive.CreateEntry("layers/biomes.bin").Open(); truncated.WriteByte(1);
            }
            bool rejected = false;
            try { WorldFile.Load(path); } catch (InvalidDataException) { rejected = true; }
            Require(rejected, "Truncated layer accepted.");

            var legacy = Create(CheckManifest(32, 32, false));
            legacy.LockMask[cell] = 0x1ff;
            WriteLegacyFixture(path, legacy);
            loaded = WorldFile.Load(path);
            Require(loaded.Manifest.WorldSaveVersion == 1 && loaded.Elevation.SequenceEqual(legacy.Elevation) &&
                loaded.Biomes.SequenceEqual(legacy.Biomes) && loaded.Relief.SequenceEqual(legacy.Relief) &&
                loaded.Water.SequenceEqual(legacy.Water) && loaded.Temperature.SequenceEqual(legacy.Temperature) &&
                loaded.Rainfall.SequenceEqual(legacy.Rainfall) && loaded.Landforms.All(x => x == LandformType.None) &&
                loaded.Features.All(x => x == GeographicFeatures.None) && loaded.LockMask[cell] == (ushort)HexLocks.All,
                "Legacy load changed data or lost the whole-hex lock.");
            saved = WorldFile.Save(path, loaded, null);
            loaded = WorldFile.Load(path);
            Require(saved.WorldSaveVersion == 2 && loaded.Manifest.GeneratorVersion == legacy.Manifest.GeneratorVersion &&
                loaded.Biomes.SequenceEqual(legacy.Biomes), "Legacy upgrade changed the generator recipe.");
        }
        finally { File.Delete(path); }
        Console.WriteLine("PASS: independent ushort IDs, 64-bit features, GPU byte planes, edit copy/undo/redo, locks, v1 migration, v2 optional layers and corruption checks");

        static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    }

    private static void WriteLegacyFixture(string path, WorldData world)
    {
        using var file = new FileStream(path, FileMode.Create);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter(allowIntegerValues: false) } };
        using (var meta = archive.CreateEntry("manifest.json").Open())
            JsonSerializer.Serialize(meta, world.Manifest with { WorldSaveVersion = 1 }, options);
        using (var sparse = archive.CreateEntry("sparse_entities.json").Open()) JsonSerializer.Serialize(sparse, world.SparseEntities);
        using var dense = archive.CreateEntry("dense_data.bin").Open();
        dense.Write(MemoryMarshal.AsBytes(world.Elevation.AsSpan()));
        dense.Write(MemoryMarshal.AsBytes(world.Water.AsSpan()));
        dense.Write(world.Relief.Select(x => (byte)x).ToArray());
        dense.Write(world.Biomes.Select(x => (byte)x).ToArray());
        dense.Write(MemoryMarshal.AsBytes(world.Temperature.AsSpan()));
        dense.Write(MemoryMarshal.AsBytes(world.Rainfall.AsSpan()));
        dense.Write(MemoryMarshal.AsBytes(world.RoadEdges.AsSpan()));
        dense.Write(world.Flags);
        dense.Write(MemoryMarshal.AsBytes(world.LockMask.AsSpan()));
    }
}
