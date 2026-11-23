using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mapmaker.Core;

public static class WorldFile
{
    public const int CurrentVersion = 2;
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
        var manifest = world.Manifest with { WorldSaveVersion = CurrentVersion, ModifiedAt = DateTimeOffset.UtcNow };
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
                    WriteLayers(archive, world);
                    if (world.Manifest.Generation.Stage != GenerationStage.Blank)
                    {
                        var geography = GeographicRegions.Get(world);
                        using(var regions=archive.CreateEntry("geography.json").Open())
                            JsonSerializer.Serialize(regions,new { geography.Version, geography.Regions },JsonOptions);
                        using var cells=archive.CreateEntry("layers/geographic_regions.bin").Open();
                        cells.Write(MemoryMarshal.AsBytes(geography.CellIds.AsSpan()));
                    }
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
        if (manifest.WorldSaveVersion is < 1 or > CurrentVersion)
            throw new InvalidDataException($"Versão .world não suportada: {manifest.WorldSaveVersion}.");
        WorldData.Validate(manifest);
        ValidateLayerLengths(archive, manifest);

        using var sparseStream = FindEntry(archive, "sparse_entities.json", SparseLimit(manifest)).Open();
        using var sparse = JsonDocument.Parse(sparseStream);
        if (sparse.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Dados de entidades inválidos.");
        var world = new WorldData(manifest) { SparseEntities = sparse.RootElement.Clone() };
        if (manifest.WorldSaveVersion == 1)
        {
            long expectedLength = (long)world.Count * SourceBytesPerHex;
            var denseEntry = FindEntry(archive, "dense_data.bin", expectedLength);
            if (denseEntry.Length != expectedLength) throw new InvalidDataException("Tamanho incorreto dos dados de terreno.");
            using var dense = denseEntry.Open();
            ReadLegacyDense(dense, world);
        }
        else ReadLayers(archive, world);
        if (archive.GetEntry("tectonics.json") is not null)
        {
            using var tectonics = FindEntry(archive, "tectonics.json", MaxSparseBytes).Open();
            world.Tectonics = JsonSerializer.Deserialize<TectonicReference>(tectonics, JsonOptions);
            world.Tectonics?.Validate(world.Width, world.Height);
        }
        if (manifest.Generation.Stage != GenerationStage.Blank)
            world.RebuildCoast();
        WorldGenerator.RebuildRivers(world);
        var regionEntry=archive.GetEntry("geography.json");
        var cellEntry=archive.GetEntry("layers/geographic_regions.bin");
        if ((regionEntry is null)!=(cellEntry is null)) throw new InvalidDataException("Camada de regiões incompleta.");
        if(regionEntry is not null && cellEntry is not null)
        {
            if(regionEntry.Length>Math.Max(MaxSparseBytes,(long)world.Count*256) || cellEntry.Length!=(long)world.Count*4)
                throw new InvalidDataException("Tamanho inválido da camada de regiões.");
            using var regions=regionEntry.Open();
            var catalogue=JsonSerializer.Deserialize<GeographicRegions>(regions,JsonOptions) ?? throw new InvalidDataException("Regiões inválidas.");
            var ids=new int[world.Count]; using var cells=cellEntry.Open(); cells.ReadExactly(MemoryMarshal.AsBytes(ids.AsSpan()));
            if(catalogue.Version is < 1 || catalogue.Version>GeographicRegions.AlgorithmVersion || catalogue.Regions is null || catalogue.Regions.Length>world.Count)
                throw new InvalidDataException("Versão de regiões inválida.");
            var known=new System.Collections.Generic.HashSet<int>();
            foreach(var r in catalogue.Regions)
                if(r is null || r.Id<=0 || !known.Add(r.Id) || !Enum.IsDefined(r.Kind) || string.IsNullOrWhiteSpace(r.Name) || r.Name.Length>80 ||
                    r.Name.Any(char.IsControl) || !world.Domain.Contains(r.Anchor) || r.Cells<=0 || r.AreaKm2<=0 || !double.IsFinite(r.AreaKm2) || !double.IsFinite(r.MeanElevation) ||
                    r.Plate < -1 || (r.Plate>=0 && (world.Tectonics is null || r.Plate>=world.Tectonics.Plates.Length)))
                    throw new InvalidDataException("Registro de região inválido.");
            var counts=new System.Collections.Generic.Dictionary<int,int>();
            var categories=catalogue.Regions.ToDictionary(r=>r.Id,r=>r.Kind);
            for(int i=0;i<ids.Length;i++)
            {
                if(ids[i]<0 || (ids[i]!=0 && (!known.Contains(ids[i]) || !world.Domain.Contains(i))) || (ids[i]==0 && world.Domain.Contains(i)))
                    throw new InvalidDataException("ID de região inválido.");
                if(ids[i]==0)continue;
                counts[ids[i]]=counts.GetValueOrDefault(ids[i])+1;
                var kind=categories[ids[i]];
                bool land=kind is RegionKind.Continent or RegionKind.Island or RegionKind.Archipelago;
                if(land!=(world.Water[i]==WaterType.None) ||
                    ((kind is RegionKind.Ocean or RegionKind.Sea) && world.Water[i]!=WaterType.Ocean) ||
                    (kind==RegionKind.Lake && world.Water[i]==WaterType.Ocean))
                    throw new InvalidDataException("Região incompatível com a superfície.");
            }
            foreach(var r in catalogue.Regions)
                if(counts.GetValueOrDefault(r.Id)!=r.Cells || ids[r.Anchor]!=r.Id)throw new InvalidDataException("Área ou posição da região inválida.");
            world.Geography=new() { Version=catalogue.Version, CellIds=ids, Regions=catalogue.Regions, SourceHash=GeographicRegions.SurfaceHash(world) };
        }
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

    // Version 2 has independent typed layers. Adding an optional layer never
    // changes the offsets or meanings of an existing layer.
    private static void ValidateLayerLengths(ZipArchive archive, WorldManifest manifest)
    {
        long count = (long)manifest.Width * manifest.Height;
        if (manifest.WorldSaveVersion == 1)
        {
            long size = count * SourceBytesPerHex;
            if (FindEntry(archive, "dense_data.bin", size).Length != size)
                throw new InvalidDataException("Tamanho incorreto dos dados de terreno.");
            return;
        }
        foreach (var (name, width) in new[] { ("elevation", 2), ("water", 1), ("relief", 2), ("biomes", 2),
            ("temperature", 2), ("rainfall", 2), ("roads", 2), ("flags", 1), ("locks", 2), ("landforms", 2), ("features", 8) })
        {
            string path = $"layers/{name}.bin";
            if ((name is "landforms" or "features") && archive.GetEntry(path) is null) continue;
            long size = count * width;
            if (FindEntry(archive, path, size).Length != size)
                throw new InvalidDataException($"Tamanho incorreto da camada {name}.");
        }
    }

    private static void WriteLayers(ZipArchive archive, WorldData world)
    {
        EnsureLittleEndian();
        WriteLayer(archive, "elevation", world.Elevation);
        WriteLayer(archive, "water", world.Water);
        WriteLayer(archive, "relief", world.Relief);
        WriteLayer(archive, "biomes", world.Biomes);
        WriteLayer(archive, "landforms", world.Landforms);
        WriteLayer(archive, "features", world.Features);
        WriteLayer(archive, "temperature", world.Temperature);
        WriteLayer(archive, "rainfall", world.Rainfall);
        WriteLayer(archive, "roads", world.RoadEdges);
        WriteLayer(archive, "locks", world.LockMask);
        using var entry = archive.CreateEntry("layers/flags.bin", CompressionLevel.Fastest).Open();
        Span<byte> flags = stackalloc byte[4096];
        for (int start = 0; start < world.Count; start += flags.Length)
        {
            int length = Math.Min(world.Count - start, flags.Length);
            for (int i = 0; i < length; i++) flags[i] = (byte)(world.Flags[start + i] & ~WorldData.CoastFlag);
            entry.Write(flags[..length]);
        }
    }

    private static void WriteLayer<T>(ZipArchive archive, string name, T[] values) where T : unmanaged
    {
        using var stream = archive.CreateEntry($"layers/{name}.bin", CompressionLevel.Fastest).Open();
        stream.Write(MemoryMarshal.AsBytes(values.AsSpan()));
    }

    private static void ReadLayers(ZipArchive archive, WorldData world)
    {
        EnsureLittleEndian();
        ReadLayer(archive, "elevation", world.Elevation);
        ReadLayer(archive, "water", world.Water);
        ReadLayer(archive, "relief", world.Relief);
        ReadLayer(archive, "biomes", world.Biomes);
        ReadLayer(archive, "landforms", world.Landforms, optional: true);
        ReadLayer(archive, "features", world.Features, optional: true);
        ReadLayer(archive, "temperature", world.Temperature);
        ReadLayer(archive, "rainfall", world.Rainfall);
        ReadLayer(archive, "roads", world.RoadEdges);
        ReadLayer(archive, "flags", world.Flags);
        ReadLayer(archive, "locks", world.LockMask);
    }

    private static void ReadLayer<T>(ZipArchive archive, string name, T[] values, bool optional = false) where T : unmanaged
    {
        string path = $"layers/{name}.bin";
        if (optional && archive.GetEntry(path) is null) return;
        Span<byte> bytes = MemoryMarshal.AsBytes(values.AsSpan());
        var entry = FindEntry(archive, path, bytes.Length);
        if (entry.Length != bytes.Length) throw new InvalidDataException($"Tamanho incorreto da camada {name}.");
        using var stream = entry.Open();
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw new InvalidDataException($"Dados extras na camada {name}.");
    }

    // Version 1 stores biome/relief in bytes; expand without renumbering IDs.
    private static void ReadLegacyDense(Stream stream, WorldData world)
    {
        EnsureLittleEndian();
        stream.ReadExactly(MemoryMarshal.AsBytes(world.Elevation.AsSpan()));
        stream.ReadExactly(MemoryMarshal.AsBytes(world.Water.AsSpan()));
        var legacy = new byte[world.Count];
        stream.ReadExactly(legacy);
        for (int i = 0; i < world.Count; i++) world.Relief[i] = (ReliefType)legacy[i];
        stream.ReadExactly(legacy);
        for (int i = 0; i < world.Count; i++) world.Biomes[i] = (BiomeType)legacy[i];
        stream.ReadExactly(MemoryMarshal.AsBytes(world.Temperature.AsSpan()));
        stream.ReadExactly(MemoryMarshal.AsBytes(world.Rainfall.AsSpan()));
        stream.ReadExactly(MemoryMarshal.AsBytes(world.RoadEdges.AsSpan()));
        stream.ReadExactly(world.Flags.AsSpan());
        stream.ReadExactly(MemoryMarshal.AsBytes(world.LockMask.AsSpan()));
        // A v1 "whole hex" lock also protects the new independent layers.
        for (int i = 0; i < world.Count; i++)
            if (world.LockMask[i] == 0x1ff) world.LockMask[i] = (ushort)HexLocks.All;
        if (stream.ReadByte() != -1) throw new InvalidDataException("Dados extras no terreno legado.");
    }

    private static void EnsureLittleEndian()
    {
        if (!BitConverter.IsLittleEndian)
            throw new PlatformNotSupportedException("O formato .world requer uma plataforma little-endian nesta versão.");
    }
}
