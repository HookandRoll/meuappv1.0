using System;
using System.Text.Json;
using System.Collections.Generic;

namespace Mapmaker.Core;

public enum TerrainAlgorithm { Fractal, LandmassCenters, ContinentalSculpt }
public enum GenerationStage { Blank, Land, Relief, Terrain }
public enum MapExtent { Region, World }
public enum WaterType : byte { None, Ocean, Lake, Wetland }
public enum ReliefType : ushort { Flat, Rolling, Hills, Mountains, Peak }
public enum BiomeType : ushort
{
    None, Beach, Grassland, TemperateForest, Rainforest, Savanna, Steppe, Desert, Taiga, Tundra, Alpine, Swamp,
    Glacier, SeasonalForest, Shrubland, Badlands, TemperateRainforest, Mangrove, Bog, Oasis, Volcanic, SaltFlat
}

// Independent classifications: a forest can occupy a plateau with a canyon.
public enum LandformType : ushort
{
    None, Plain, RollingPlain, Plateau, Escarpment, Hills, Mountains, Peak, Valley, Slope, Foothills
}

[Flags]
public enum GeographicFeatures : ulong
{
    None = 0,
    Volcano = 1UL << 0,
    Escarpment = 1UL << 1,
    Canyon = 1UL << 2,
    BadlandRocks = 1UL << 3,
    SeaIce = 1UL << 4
}

public static class Biomes
{
    public static bool IsForest(BiomeType type) =>
        type is BiomeType.TemperateForest or BiomeType.Rainforest or BiomeType.Taiga or BiomeType.Swamp or
            BiomeType.SeasonalForest or BiomeType.TemperateRainforest or BiomeType.Mangrove;
}

public sealed record GenerationSettings(
    TerrainAlgorithm Algorithm, int Continents, int LandPercent, int CoastDetail, GenerationStage Stage,
    MapExtent Extent = MapExtent.Region, int PolarOceanRows = 0, int ReferenceWidth = 0, int ReferenceHeight = 0,
    int TemperatureOffsetC = 0, int PrecipitationOffsetPercent = 0);

public sealed record WorldManifest(
    int WorldSaveVersion, string WorldName, int Seed, int GeneratorVersion, int Width, int Height,
    bool HorizontalWrap, double HexDistance, string DistanceUnit, DateTimeOffset CreatedAt,
    DateTimeOffset ModifiedAt, GenerationSettings Generation);

public sealed class WorldData
{
    public const int ChunkSize = 32;
    public const byte CoastFlag = 4;
    public const int MaxWidth = 6144;
    public const int MaxHeight = 3072;

    public WorldManifest Manifest { get; internal set; }
    public MapDomain Domain { get; }
    public int ActiveCount => Domain.ActiveCount;
    public int Width => Manifest.Width;
    public int Height => Manifest.Height;
    public int Count => Elevation.Length;
    public ChunkGrid Chunks => new(Width, Height);
    public short[] Elevation { get; }
    public WaterType[] Water { get; }
    public ReliefType[] Relief { get; }
    public BiomeType[] Biomes { get; }
    public LandformType[] Landforms { get; }
    public GeographicFeatures[] Features { get; }
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
    public GeographicRegions? Geography { get; internal set; }
    internal bool GeographyDirty { get; set; }
    public void InvalidateGeography() => GeographyDirty = true;
    // The sea level is derived from the manifest and recalibrated on demand after opening a save.
    internal float GeneratedSeaLevel { get; set; } = float.NaN;

    public WorldData(WorldManifest manifest)
    {
        Validate(manifest);
        Manifest = manifest;
        Domain = new MapDomain(manifest);
        int count = manifest.Width * manifest.Height;
        Elevation = new short[count];
        Water = new WaterType[count];
        Relief = new ReliefType[count];
        Biomes = new BiomeType[count];
        Landforms = new LandformType[count];
        Features = new GeographicFeatures[count];
        Temperature = new short[count];
        Rainfall = new ushort[count];
        RoadEdges = new ushort[count];
        RiverEdges = new ushort[count];
        Flags = new byte[count];
        LockMask = new ushort[count];
        if (Domain.IsAtlas)
            for (int i = 0; i < count; i++)
                if (!Domain.Contains(i)) { Water[i] = WaterType.Ocean; Elevation[i] = -6000; }
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
        if (manifest.Generation is not { Continents: >= 1 and <= 12, LandPercent: >= 0 and <= 100, CoastDetail: >= 0 and <= 100 } options
            || !Enum.IsDefined(options.Algorithm) || !Enum.IsDefined(options.Stage) || !Enum.IsDefined(options.Extent)
            || options.TemperatureOffsetC is not (-5 or 0 or 5)
            || options.PrecipitationOffsetPercent is not (-10 or 0 or 10))
            throw new ArgumentException("Configuração de geração inválida.");
        int polarRows = options.PolarOceanRows;
        int centralHeight = manifest.Height - 2 * polarRows;
        bool atlas = manifest.GeneratorVersion >= 37 && options.Extent == MapExtent.World;
        if ((!atlas && (manifest.Width > 4096 || centralHeight is < 32 or > 2048)) ||
            polarRows is < 0 or > 256 || (polarRows & 1) != 0 ||
            (polarRows > 0 && (manifest.GeneratorVersion < 36 || options.Extent != MapExtent.World || atlas)) ||
            options.ReferenceWidth < 0 || options.ReferenceWidth > 4096 || options.ReferenceHeight < 0 || options.ReferenceHeight > 2048 ||
            ((options.ReferenceWidth == 0) != (options.ReferenceHeight == 0)))
            throw new ArgumentException("Faixas polares ou altura central inválidas.");
        if (options.ReferenceWidth > 0)
        {
            if (!atlas || options.ReferenceWidth < 32 || options.ReferenceHeight < 32)
                throw new ArgumentException("Tamanho de referência inválido para o atlas.");
            var expected = MapDomain.ExpandedSize(options.ReferenceWidth, options.ReferenceHeight);
            if (manifest.Width != expected.Width || manifest.Height != expected.Height)
                throw new ArgumentException("As dimensões não correspondem ao atlas ampliado.");
        }
        if (options.Extent == MapExtent.World && (manifest.GeneratorVersion < 34 || options.Algorithm != TerrainAlgorithm.ContinentalSculpt))
            throw new ArgumentException("O modo Mundo requer o gerador tectônico v34 ou posterior.");
        if (options.Algorithm == TerrainAlgorithm.ContinentalSculpt && manifest.GeneratorVersion < 6)
            throw new ArgumentException("A escultura continental requer a versão 6 do gerador ou posterior.");
    }

    internal WorldData CreateEditCopy()
    {
        var copy = new WorldData(Manifest) { SparseEntities = SparseEntities, Tectonics = Tectonics, GeneratedSeaLevel = GeneratedSeaLevel };
        copy.Geography = Geography?.Copy(); copy.GeographyDirty = GeographyDirty;
        Elevation.CopyTo(copy.Elevation, 0);
        Water.CopyTo(copy.Water, 0);
        Relief.CopyTo(copy.Relief, 0);
        Biomes.CopyTo(copy.Biomes, 0);
        Landforms.CopyTo(copy.Landforms, 0);
        Features.CopyTo(copy.Features, 0);
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
