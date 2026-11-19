using System;
using System.Text.Json;
using System.Collections.Generic;

namespace Mapmaker.Core;

public enum TerrainAlgorithm { Fractal, LandmassCenters, ContinentalSculpt }
public enum GenerationStage { Blank, Land, Relief, Terrain }
public enum WaterType : byte { None, Ocean, Lake, Wetland }
public enum ReliefType : byte { Flat, Rolling, Hills, Mountains, Peak }
public enum BiomeType : byte
{
    None, Beach, Grassland, TemperateForest, Rainforest, Savanna, Steppe, Desert, Taiga, Tundra, Alpine
}

public static class Biomes
{
    public static bool IsForest(BiomeType type) =>
        type is BiomeType.TemperateForest or BiomeType.Rainforest or BiomeType.Taiga;
}

public sealed record GenerationSettings(
    TerrainAlgorithm Algorithm, int Continents, int LandPercent, int CoastDetail, GenerationStage Stage);

public sealed record WorldManifest(
    int WorldSaveVersion, string WorldName, int Seed, int GeneratorVersion, int Width, int Height,
    bool HorizontalWrap, double HexDistance, string DistanceUnit, DateTimeOffset CreatedAt,
    DateTimeOffset ModifiedAt, GenerationSettings Generation);

public sealed class WorldData
{
    public const int ChunkSize = 32;
    public const byte CoastFlag = 4;
    public const int MaxWidth = 4096;
    public const int MaxHeight = 2048;

    public WorldManifest Manifest { get; internal set; }
    public int Width => Manifest.Width;
    public int Height => Manifest.Height;
    public int Count => Elevation.Length;
    public ChunkGrid Chunks => new(Width, Height);
    public short[] Elevation { get; }
    public WaterType[] Water { get; }
    public ReliefType[] Relief { get; }
    public BiomeType[] Biomes { get; }
    public short[] Temperature { get; }
    public ushort[] Rainfall { get; }
    public ushort[] RoadEdges { get; }
    public ushort[] RiverEdges { get; }
    internal Dictionary<int, List<int>> HexToRivers { get; } = new();
    internal Dictionary<int, RiverRecord> RiversById { get; } = new();
    internal (int HeadwaterJoins, int CurvedReaches) RiverDiagnostics { get; set; }
    public byte[] Flags { get; }
    public ushort[] LockMask { get; }
    public JsonElement SparseEntities { get; internal set; }
    public TectonicReference? Tectonics { get; internal set; }
    // The sea level is derived from the manifest and recalibrated on demand after opening a save.
    internal float GeneratedSeaLevel { get; set; } = float.NaN;

    public WorldData(WorldManifest manifest)
    {
        Validate(manifest);
        Manifest = manifest;
        int count = manifest.Width * manifest.Height;
        Elevation = new short[count];
        Water = new WaterType[count];
        Relief = new ReliefType[count];
        Biomes = new BiomeType[count];
        Temperature = new short[count];
        Rainfall = new ushort[count];
        RoadEdges = new ushort[count];
        RiverEdges = new ushort[count];
        Flags = new byte[count];
        LockMask = new ushort[count];
        SparseEntities = JsonSerializer.SerializeToElement(new
        {
            rivers = Array.Empty<object>(), pois = Array.Empty<object>(), notes = Array.Empty<object>(),
            party = (object?)null, overrides = Array.Empty<object>()
        });
    }

    public static void Validate(WorldManifest manifest)
    {
        if (manifest.Width is < 32 or > MaxWidth || manifest.Height is < 32 or > MaxHeight)
            throw new ArgumentException("Dimensões do mundo fora do intervalo suportado.");
        if (string.IsNullOrWhiteSpace(manifest.WorldName) || manifest.WorldName.Length > 120 ||
            !double.IsFinite(manifest.HexDistance) || manifest.HexDistance <= 0 || manifest.DistanceUnit != "km")
            throw new ArgumentException("Nome ou escala do mundo inválido.");
        if (manifest.Generation is not { Continents: >= 1 and <= 12, LandPercent: >= 10 and <= 65, CoastDetail: >= 0 and <= 100 } options
            || !Enum.IsDefined(options.Algorithm) || !Enum.IsDefined(options.Stage))
            throw new ArgumentException("Configuração de geração inválida.");
        if (options.Algorithm == TerrainAlgorithm.ContinentalSculpt && manifest.GeneratorVersion < 6)
            throw new ArgumentException("A escultura continental requer a versão 6 do gerador ou posterior.");
    }

    internal WorldData CreateEditCopy()
    {
        var copy = new WorldData(Manifest) { SparseEntities = SparseEntities, Tectonics = Tectonics, GeneratedSeaLevel = GeneratedSeaLevel };
        Elevation.CopyTo(copy.Elevation, 0);
        Water.CopyTo(copy.Water, 0);
        Relief.CopyTo(copy.Relief, 0);
        Biomes.CopyTo(copy.Biomes, 0);
        Temperature.CopyTo(copy.Temperature, 0);
        Rainfall.CopyTo(copy.Rainfall, 0);
        RoadEdges.CopyTo(copy.RoadEdges, 0);
        RiverEdges.CopyTo(copy.RiverEdges, 0);
        foreach (var (id, river) in RiversById) copy.RiversById[id] = river;
        foreach (var (cell, rivers) in HexToRivers) copy.HexToRivers[cell] = new List<int>(rivers);
        Flags.CopyTo(copy.Flags, 0);
        LockMask.CopyTo(copy.LockMask, 0);
        return copy;
    }

    public void RebuildCoast()
    {
        for (int cell = 0; cell < Count; cell++)
            RefreshCoast(cell);
    }

    internal void RefreshCoast(int cell) =>
        Flags[cell] = (byte)((Flags[cell] & ~CoastFlag) | (IsCoast(cell) ? CoastFlag : 0));

    internal bool IsCoast(int cell)
    {
        if (Water[cell] != WaterType.None)
            return false;
        Hex hex = HexGrid.FromOffset(cell % Width, cell / Width);
        for (int direction = 0; direction < 6; direction++)
        {
            int neighbor = HexGrid.NeighborIndex(hex, direction, Width, Height, Manifest.HorizontalWrap);
            if (neighbor >= 0 && Water[neighbor] == WaterType.Ocean)
                return true;
        }
        return false;
    }
}
