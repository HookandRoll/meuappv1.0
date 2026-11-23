using System;
using System.Threading.Tasks;
using Godot;
using Mapmaker.Core;

namespace Mapmaker.GodotApp;

internal sealed class GlobalDataTextures : IDisposable
{
    private readonly Image _terrainImage;
    private readonly Image _climateImage;
    private readonly Image _detailImage;
    public Texture2DArray Attributes { get; }
    public Texture2DArray Features { get; }
    public bool HasFeatures { get; }
    public ImageTexture Terrain { get; }
    public ImageTexture Climate { get; }
    public ImageTexture Details { get; }

    internal readonly record struct Buffers(byte[] Terrain, byte[] Surface, byte[] Geography, byte[]? FeatureLow, byte[]? FeatureHigh, byte[] Climate, byte[] Details);

    public GlobalDataTextures(WorldData world, Buffers buffers)
    {
        _terrainImage = Image.CreateFromData(world.Width, world.Height, false, Image.Format.Rgba8, buffers.Terrain);
        _climateImage = Image.CreateFromData(world.Width, world.Height, false, Image.Format.Rgba8, buffers.Climate);
        _detailImage = Image.CreateFromData(world.Width, world.Height, false, Image.Format.Rgba8, buffers.Details);
        Terrain = ImageTexture.CreateFromImage(_terrainImage);
        Climate = ImageTexture.CreateFromImage(_climateImage);
        Details = ImageTexture.CreateFromImage(_detailImage);
        Attributes = CreateArray(world.Width, world.Height, buffers.Surface, buffers.Geography);
        HasFeatures = buffers.FeatureLow is not null;
        Features = HasFeatures ? CreateArray(world.Width, world.Height, buffers.FeatureLow!, buffers.FeatureHigh!)
            : CreateArray(1, 1, new byte[4], new byte[4]);
    }

    public static Buffers BuildBuffers(WorldData world)
    {
        byte[] terrain = new byte[world.Count * 4];
        byte[] surface = new byte[terrain.Length], geography = new byte[terrain.Length], climate = new byte[terrain.Length];
        bool hasFeatures = Array.Exists(world.Features, feature => feature != GeographicFeatures.None);
        byte[]? featureLow = hasFeatures ? new byte[terrain.Length] : null, featureHigh = hasFeatures ? new byte[terrain.Length] : null;
        Parallel.For(0, world.Height, row =>
        {
            Span<byte> unusedLow = stackalloc byte[4], unusedHigh = stackalloc byte[4];
            for (int cell = row * world.Width; cell < (row + 1) * world.Width; cell++)
            {
                int index = cell * 4;
                MapRenderFormat.WriteHex(world, cell, terrain.AsSpan(index, 4), surface.AsSpan(index, 4), geography.AsSpan(index, 4),
                    featureLow is null ? unusedLow : featureLow.AsSpan(index, 4), featureHigh is null ? unusedHigh : featureHigh.AsSpan(index, 4));
                Pack(climate, index, (ushort)(world.Temperature[cell] - short.MinValue));
                Pack(climate, index + 2, world.Rainfall[cell]);
            }
        });
        if (world.Manifest.Generation.Stage >= GenerationStage.Relief)
            MarkReliefSymbols(world, surface);
        if (world.Manifest.Generation.Stage == GenerationStage.Terrain)
            MarkVegetationSymbols(world, surface);
        MarkTightShorelines(world, geography);

        return new Buffers(terrain, surface, geography, featureLow, featureHigh, climate, OceanShelf.BuildDetails(world, MapDetailIcons.Build(world, surface)));
    }

    private static void MarkTightShorelines(WorldData world, byte[] terrain)
    {
        // Shore metadata has its own byte; no biome ID bits are overwritten.
        // Derive the visual shoreline from water so it crosses the edge of an edit without writing to adjacent hexes.
        Parallel.For(0, world.Height, row =>
        {
            for (int index = row * world.Width; index < (row + 1) * world.Width; index++)
            {
                var water = world.Water[index];
                if (water is not (WaterType.None or WaterType.Ocean)) continue;
                Hex hex = HexGrid.FromOffset(index % world.Width, row);
                int landNeighbors = 0;
                bool coast = false, adjacentLand = false;
                for (int direction = 0; direction < 6; direction++)
                {
                    int neighbor = HexGrid.NeighborIndex(hex, direction, world.Width, world.Height, world.Manifest.HorizontalWrap);
                    if (neighbor < 0 || world.Water[neighbor] == WaterType.None) landNeighbors++;
                    if (neighbor >= 0)
                    {
                        adjacentLand |= world.Water[neighbor] == WaterType.None;
                        coast |= world.Water[neighbor] == WaterType.Ocean;
                    }
                }
                if (water == WaterType.None && coast)
                    terrain[index * 4 + 2] |= (byte)ShoreCrowding(landNeighbors);
                else if (water == WaterType.Ocean && adjacentLand)
                    MarkOceanCrowding(world, terrain, index);
            }
        });
    }

    private static void MarkOceanCrowding(WorldData world, byte[] terrain, int index)
    {
        Hex hex = HexGrid.FromOffset(index % world.Width, index / world.Width);
        int oceanNeighbors = 0;
        for (int direction = 0; direction < 6; direction++)
        {
            int neighbor = HexGrid.NeighborIndex(hex, direction, world.Width, world.Height, world.Manifest.HorizontalWrap);
            if (neighbor < 0 || world.Water[neighbor] == WaterType.Ocean)
                oceanNeighbors++;
        }
        terrain[index * 4 + 2] |= (byte)ShoreCrowding(oceanNeighbors);
    }

    private static int ShoreCrowding(int neighbors) => neighbors <= 2 ? 3 : neighbors == 3 ? 1 : 0;

    public void UploadAll()
    {
        Terrain.Update(_terrainImage);
        Climate.Update(_climateImage);
        Details.Update(_detailImage);
    }

    public void Dispose()
    {
        Terrain.Dispose();
        Climate.Dispose();
        Details.Dispose();
        Attributes.Dispose();
        Features.Dispose();
        _terrainImage.Dispose();
        _climateImage.Dispose();
        _detailImage.Dispose();
    }

    private static Texture2DArray CreateArray(int width, int height, params byte[][] planes)
    {
        var images = new Godot.Collections.Array<Image>();
        var texture = new Texture2DArray();
        try
        {
            foreach (var plane in planes) images.Add(Image.CreateFromData(width, height, false, Image.Format.Rgba8, plane));
            if (texture.CreateFromImages(images) != Error.Ok) throw new InvalidOperationException("Falha ao criar atributos do mapa.");
            return texture;
        }
        catch { texture.Dispose(); throw; }
        finally { foreach (var image in images) image.Dispose(); }
    }

    private static void Pack(byte[] bytes, int index, ushort value)
    {
        bytes[index] = (byte)(value & 0xff);
        bytes[index + 1] = (byte)(value >> 8);
    }

    private static void MarkReliefSymbols(WorldData world, byte[] terrain)
    {
        Parallel.For(0, world.Height, row =>
        {
            for (int cell = row * world.Width; cell < (row + 1) * world.Width; cell++)
            {
                byte symbols = ReliefContours.Symbol(world, cell);
                var features = world.Features[cell];
                if (world.Water[cell] == WaterType.None && (features & GeographicFeatures.Volcano) != 0) symbols = MapRenderFormat.ReliefSymbol | MapRenderFormat.MassifSymbol | 1;
                else if ((features & GeographicFeatures.Canyon) != 0 && StableRank(cell) % 3 == 0) symbols = MapRenderFormat.ReliefSymbol | MapRenderFormat.MassifSymbol | 2;
                else if ((features & GeographicFeatures.BadlandRocks) != 0 && symbols == 0 && StableRank(cell) % 7 == 0) symbols = MapRenderFormat.ReliefSymbol | MapRenderFormat.MassifSymbol | 3;
                terrain[cell * 4 + 3] |= symbols;
                if ((symbols & MapRenderFormat.MassifSymbol) == 0) continue;
                ReliefType visual = ReliefContours.VisualRelief(world, cell);
                Pack(terrain, cell * 4 + 1, (ushort)visual);
                if (world.Elevation[cell] >= 3200 && (world.Temperature[cell] <= 0 || world.Biomes[cell] == BiomeType.Alpine))
                    terrain[cell * 4 + 3] |= MapRenderFormat.Snow;
            }
        });
    }

    private static void MarkVegetationSymbols(WorldData world, byte[] terrain)
    {
        int width = world.Width;
        int height = world.Height;
        float aspect = width / (float)height;
        bool wrap = world.Manifest.HorizontalWrap;
        int seed = world.Manifest.Seed;
        Parallel.For(0, height, row =>
        {
            float y = row * 0.8660254f / height;
            for (int col = 0; col < width; col++)
            {
                int cell = row * width + col;
                if (world.Relief[cell] > ReliefType.Hills)
                    continue;

                WaterType water = world.Water[cell];
                BiomeType biome = world.Biomes[cell];
                float habitat = (water, biome) switch
                {
                    (WaterType.None, BiomeType.TemperateForest) => 0.84f,
                    (WaterType.None, BiomeType.Rainforest) => 0.88f,
                    (WaterType.None, BiomeType.Taiga) => 0.82f,
                    (WaterType.None, BiomeType.Swamp) => 0.80f,
                    (WaterType.None, BiomeType.SeasonalForest) => 0.78f,
                    (WaterType.None, BiomeType.TemperateRainforest) => 0.91f,
                    (WaterType.None, BiomeType.Mangrove) => 0.78f,
                    (WaterType.None, BiomeType.Oasis) => 0.55f,
                    (WaterType.None, BiomeType.Shrubland) => 0.38f,
                    (WaterType.None, BiomeType.Bog) => 0.24f,
                    (WaterType.None, BiomeType.Savanna) => 0.30f,
                    (WaterType.None, BiomeType.Desert) => 0.14f,
                    (WaterType.Wetland, _) => 0.46f,
                    _ => 0f
                };
                if (habitat == 0f)
                    continue;

                float x = (col + (row & 1) * 0.5f) / height;
                float field = 0.53f * WorldGenerator.TerrainNoise(x, y, height / 29f, aspect, wrap, (uint)seed ^ 0x5297A4D1u)
                    + 0.31f * WorldGenerator.TerrainNoise(x, y, height / 11f, aspect, wrap, (uint)seed ^ 0x68E31DA4u)
                    + 0.16f * WorldGenerator.TerrainNoise(x, y, height / 4.3f, aspect, wrap, (uint)seed ^ 0x1B56C4E9u);
                float canopy = Math.Clamp((field - 0.18f) / 0.52f, 0f, 1f);
                canopy *= canopy * (3f - 2f * canopy);
                float depth = VegetationInterior(world, HexGrid.FromOffset(col, row), water, biome);
                float chance = habitat * (0.45f + 0.55f * canopy) * (0.58f + 0.42f * depth);
                uint rank = StableRank(cell ^ seed ^ 0x517CC1B7);
                if ((rank >> 8) * (1f / 16777216f) < chance)
                    terrain[cell * 4 + 3] |= 1 << 7;
            }
        });
    }

    private static float VegetationInterior(WorldData world, Hex hex, WaterType water, BiomeType biome)
    {
        int contribution = 0;
        for (int direction = 0; direction < 6; direction++)
        {
            int near = HexGrid.NeighborIndex(hex, direction, world.Width, world.Height, world.Manifest.HorizontalWrap);
            if (near < 0 || world.Water[near] != water || world.Biomes[near] != biome)
                continue;
            contribution += 2;
            int farther = HexGrid.NeighborIndex(hex, direction, world.Width, world.Height, world.Manifest.HorizontalWrap, steps: 2);
            if (farther >= 0 && world.Water[farther] == water && world.Biomes[farther] == biome)
                contribution++;
        }
        return contribution / 18f;
    }

    private static uint StableRank(int cell)
    {
        uint value = (uint)cell * 0x9E3779B9u;
        value = (value ^ (value >> 16)) * 0x7FEB352Du;
        return value ^ (value >> 15);
    }
}
