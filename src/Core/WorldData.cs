using System;
using System.Text.Json;

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
    public byte[] Flags { get; }
    public ushort[] LockMask { get; }
    public JsonElement SparseEntities { get; internal set; }
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

    public void RebuildCoast()
    {
        for (int row = 0; row < Height; row++)
        for (int column = 0; column < Width; column++)
        {
            int cell = row * Width + column;
            Flags[cell] &= unchecked((byte)~CoastFlag);
            if (Water[cell] == WaterType.Ocean)
                continue;
            Hex hex = HexGrid.FromOffset(column, row);
            for (int direction = 0; direction < 6; direction++)
            {
                int neighbor = HexGrid.NeighborIndex(hex, direction, Width, Height, Manifest.HorizontalWrap);
                if (neighbor < 0 || Water[neighbor] != WaterType.Ocean)
                    continue;
                Flags[cell] |= CoastFlag;
                break;
            }
        }
    }
}
