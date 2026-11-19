using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mapmaker.Core;

public static class WorldFile
{
    public const int CurrentVersion = 1;
    private const int SourceBytesPerHex = 14;
    private const int MaxManifestBytes = 32 * 1024;
    private const int MaxSparseBytes = 8 * 1024 * 1024;
    private static long SparseLimit(WorldManifest manifest) => Math.Max(MaxSparseBytes, (long)manifest.Width * manifest.Height * 64);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public static WorldManifest Save(string path, WorldData world, byte[]? preview)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(world.SparseEntities.GetRawText()) > SparseLimit(world.Manifest))
            throw new InvalidDataException("Entidades esparsas acima do limite do mundo.");
        path = Path.GetFullPath(path);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var manifest = world.Manifest with { ModifiedAt = DateTimeOffset.UtcNow };
        bool created = false;
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
                using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
                {
                    using (var entry = archive.CreateEntry("manifest.json").Open())
                        JsonSerializer.Serialize(entry, manifest, JsonOptions);
                    using (var entry = archive.CreateEntry("dense_data.bin", CompressionLevel.Fastest).Open())
                        WriteDense(entry, world);
                    using (var entry = archive.CreateEntry("sparse_entities.json").Open())
                        JsonSerializer.Serialize(entry, world.SparseEntities);
                    if (world.Tectonics is not null)
                    {
                        using var tectonics = archive.CreateEntry("tectonics.json").Open();
                        JsonSerializer.Serialize(tectonics, world.Tectonics, JsonOptions);
                    }
                    if (preview is { Length: > 0 })
                    {
                        using var entry = archive.CreateEntry("preview.png", CompressionLevel.NoCompression).Open();
                        entry.Write(preview);
                    }
                }
                output.Flush(flushToDisk: true);
            }
            if (File.Exists(path))
                File.Replace(temporary, path, null);
            else
                File.Move(temporary, path);
            return manifest;
        }
        finally
        {
            if (created)
                File.Delete(temporary);
        }
    }

    public static WorldData Load(string path)
    {
        using var input = File.OpenRead(path);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        using var metadata = FindEntry(archive, "manifest.json", MaxManifestBytes).Open();
        var manifest = JsonSerializer.Deserialize<WorldManifest>(metadata, JsonOptions)
            ?? throw new InvalidDataException("Manifesto vazio.");
        if (manifest.WorldSaveVersion != CurrentVersion)
            throw new InvalidDataException($"Versão .world não suportada: {manifest.WorldSaveVersion}.");
        WorldData.Validate(manifest);

        long expectedLength = (long)manifest.Width * manifest.Height * SourceBytesPerHex;
        var denseEntry = FindEntry(archive, "dense_data.bin", expectedLength);
        if (denseEntry.Length != expectedLength)
            throw new InvalidDataException("Tamanho incorreto dos dados de terreno.");

        using var sparseStream = FindEntry(archive, "sparse_entities.json", SparseLimit(manifest)).Open();
        using var sparse = JsonDocument.Parse(sparseStream);
        if (sparse.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Dados de entidades inválidos.");
        var world = new WorldData(manifest) { SparseEntities = sparse.RootElement.Clone() };
        using (var dense = denseEntry.Open())
        {
            ReadDense(dense, world);
            if (dense.ReadByte() != -1)
                throw new InvalidDataException("Dados extras no terreno.");
        }
        if (archive.GetEntry("tectonics.json") is not null)
        {
            using var tectonics = FindEntry(archive, "tectonics.json", MaxSparseBytes).Open();
            world.Tectonics = JsonSerializer.Deserialize<TectonicReference>(tectonics, JsonOptions);
            world.Tectonics?.Validate(world.Width, world.Height);
        }
        if (manifest.Generation.Stage != GenerationStage.Blank)
            world.RebuildCoast();
        WorldGenerator.RebuildRivers(world);
        return world;
    }

    private static ZipArchiveEntry FindEntry(ZipArchive archive, string name, long maxLength)
    {
        ZipArchiveEntry entry = archive.GetEntry(name)
            ?? throw new InvalidDataException($"Entrada ausente: {name}.");
        if (entry.Length > maxLength)
            throw new InvalidDataException($"Entrada acima do tamanho permitido: {name}.");
        return entry;
    }

    // Version 1 uses field-major arrays and little-endian 16-bit fields.
    private static void WriteDense(Stream stream, WorldData world)
    {
        EnsureLittleEndian();
        stream.Write(MemoryMarshal.AsBytes(world.Elevation.AsSpan()));
        stream.Write(MemoryMarshal.AsBytes(world.Water.AsSpan()));
        stream.Write(MemoryMarshal.AsBytes(world.Relief.AsSpan()));
        stream.Write(MemoryMarshal.AsBytes(world.Biomes.AsSpan()));
        stream.Write(MemoryMarshal.AsBytes(world.Temperature.AsSpan()));
        stream.Write(MemoryMarshal.AsBytes(world.Rainfall.AsSpan()));
        stream.Write(MemoryMarshal.AsBytes(world.RoadEdges.AsSpan()));
        Span<byte> flags = stackalloc byte[4096];
        for (int start = 0; start < world.Count; start += flags.Length)
        {
            int length = Math.Min(world.Count - start, flags.Length);
            for (int i = 0; i < length; i++)
                flags[i] = (byte)(world.Flags[start + i] & ~WorldData.CoastFlag);
            stream.Write(flags[..length]);
        }
        stream.Write(MemoryMarshal.AsBytes(world.LockMask.AsSpan()));
    }

    private static void ReadDense(Stream stream, WorldData world)
    {
        EnsureLittleEndian();
        stream.ReadExactly(MemoryMarshal.AsBytes(world.Elevation.AsSpan()));
        stream.ReadExactly(MemoryMarshal.AsBytes(world.Water.AsSpan()));
        stream.ReadExactly(MemoryMarshal.AsBytes(world.Relief.AsSpan()));
        stream.ReadExactly(MemoryMarshal.AsBytes(world.Biomes.AsSpan()));
        stream.ReadExactly(MemoryMarshal.AsBytes(world.Temperature.AsSpan()));
        stream.ReadExactly(MemoryMarshal.AsBytes(world.Rainfall.AsSpan()));
        stream.ReadExactly(MemoryMarshal.AsBytes(world.RoadEdges.AsSpan()));
        stream.ReadExactly(world.Flags.AsSpan());
        stream.ReadExactly(MemoryMarshal.AsBytes(world.LockMask.AsSpan()));
    }

    private static void EnsureLittleEndian()
    {
        if (!BitConverter.IsLittleEndian)
            throw new PlatformNotSupportedException("O formato .world requer uma plataforma little-endian nesta versão.");
    }
}
