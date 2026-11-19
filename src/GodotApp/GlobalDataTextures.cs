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
    public ImageTexture Terrain { get; }
    public ImageTexture Climate { get; }
    public ImageTexture Details { get; }

    internal readonly record struct Buffers(byte[] Terrain, byte[] Climate, byte[] Details);

    public GlobalDataTextures(WorldData world, Buffers buffers)
    {
        _terrainImage = Image.CreateFromData(world.Width, world.Height, false, Image.Format.Rgba8, buffers.Terrain);
        _climateImage = Image.CreateFromData(world.Width, world.Height, false, Image.Format.Rgba8, buffers.Climate);
        _detailImage = Image.CreateFromData(world.Width, world.Height, false, Image.Format.Rg8, buffers.Details);
        Terrain = ImageTexture.CreateFromImage(_terrainImage);
        Climate = ImageTexture.CreateFromImage(_climateImage);
        Details = ImageTexture.CreateFromImage(_detailImage);
    }

    public static Buffers BuildBuffers(WorldData world)
    {
        byte[] terrain = new byte[world.Width * world.Height * 4];
        byte[] climate = new byte[terrain.Length];
        for (int cell = 0; cell < world.Count; cell++)
        {
            int index = cell * 4;
            Pack(terrain, index, (ushort)(world.Elevation[cell] - short.MinValue));
            terrain[index + 2] = (byte)world.Biomes[cell];
            // Two bits for water, three for relief, one for snow and one each for relief and vegetation symbols.
            int surface = (int)world.Water[cell] | ((int)world.Relief[cell] << 2);
            if (world.Manifest.Generation.Stage == GenerationStage.Terrain && world.Relief[cell] == ReliefType.Peak &&
                (world.Temperature[cell] <= 0 || world.Biomes[cell] == BiomeType.Alpine))
                surface |= 1 << 5;
            terrain[index + 3] = (byte)surface;
            Pack(climate, index, (ushort)(world.Temperature[cell] - short.MinValue));
            Pack(climate, index + 2, world.Rainfall[cell]);
        }
        if (world.Manifest.Generation.Stage >= GenerationStage.Relief)
            MarkReliefSymbols(world, terrain);
        if (world.Manifest.Generation.Stage == GenerationStage.Terrain)
            MarkVegetationSymbols(world, terrain);
        MarkTightShorelines(world, terrain);

        return new Buffers(terrain, climate, OceanShelf.BuildDetails(world, MapDetailIcons.Build(world, terrain)));
    }

    private static void MarkTightShorelines(WorldData world, byte[] terrain)
    {
        // The low four bits hold the biome ID. The next two hold local shore crowding.
        // Derive the visual shoreline from water so it crosses the edge of an edit without writing to adjacent hexes.
        for (int index = 0; index < world.Count; index++)
        {
            if (world.Water[index] != WaterType.None)
                continue;

            Hex hex = HexGrid.FromOffset(index % world.Width, index / world.Width);
            int landNeighbors = 0;
            bool coast = false;
            for (int direction = 0; direction < 6; direction++)
            {
                int neighbor = HexGrid.NeighborIndex(hex, direction, world.Width, world.Height, world.Manifest.HorizontalWrap);
                if (neighbor < 0 || world.Water[neighbor] == WaterType.None)
                    landNeighbors++;
                else if (world.Water[neighbor] == WaterType.Ocean)
                {
                    coast = true;
                    MarkOceanCrowding(world, terrain, neighbor);
                }
            }
            if (coast)
                terrain[index * 4 + 2] |= (byte)(ShoreCrowding(landNeighbors) << 4);
        }
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
        terrain[index * 4 + 2] |= (byte)(ShoreCrowding(oceanNeighbors) << 4);
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
        _terrainImage.Dispose();
        _climateImage.Dispose();
        _detailImage.Dispose();
    }

    private static void Pack(byte[] bytes, int index, ushort value)
    {
        bytes[index] = (byte)(value & 0xff);
        bytes[index + 1] = (byte)(value >> 8);
    }

    private static void MarkReliefSymbols(WorldData world, byte[] terrain)
    {
        const int spacing = 5;
        for (int rowStart = 0; rowStart < world.Height; rowStart += spacing)
        for (int columnStart = 0; columnStart < world.Width; columnStart += spacing)
        {
            bool drawRolling = (StableRank(rowStart * world.Width + columnStart) & 1u) == 0;
            int bestCell = -1;
            int bestScore = -1;
            for (int row = rowStart; row < Math.Min(rowStart + spacing, world.Height); row++)
            for (int column = columnStart; column < Math.Min(columnStart + spacing, world.Width); column++)
            {
                int cell = row * world.Width + column;
                ReliefType relief = world.Relief[cell];
                if (world.Water[cell] != WaterType.None || relief is <= ReliefType.Flat or > ReliefType.Peak ||
                    (relief == ReliefType.Rolling && !drawRolling))
                    continue;

                int score = (int)relief * 2048 + Math.Max(0, (int)world.Elevation[cell]) / 32
                    + (int)(StableRank(cell) & 511u);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestCell = cell;
                }
            }
            if (bestCell >= 0)
                terrain[bestCell * 4 + 3] |= 1 << 6;
        }
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
                    (WaterType.None, BiomeType.TemperateForest) => 0.53f,
                    (WaterType.None, BiomeType.Rainforest) => 0.62f,
                    (WaterType.None, BiomeType.Taiga) => 0.43f,
                    (WaterType.None, BiomeType.Savanna) => 0.20f,
                    (WaterType.None, BiomeType.Desert) => 0.09f,
                    (WaterType.Wetland, _) => 0.35f,
                    _ => 0f
                };
                if (habitat == 0f)
                    continue;

                float x = (col + (row & 1) * 0.5f) / height;
                float field = 0.53f * WorldGenerator.TerrainNoise(x, y, height / 29f, aspect, wrap, (uint)seed ^ 0x5297A4D1u)
                    + 0.31f * WorldGenerator.TerrainNoise(x, y, height / 11f, aspect, wrap, (uint)seed ^ 0x68E31DA4u)
                    + 0.16f * WorldGenerator.TerrainNoise(x, y, height / 4.3f, aspect, wrap, (uint)seed ^ 0x1B56C4E9u);
                float canopy = Math.Clamp((field - 0.24f) / 0.53f, 0f, 1f);
                canopy *= canopy * (3f - 2f * canopy);
                float depth = VegetationInterior(world, HexGrid.FromOffset(col, row), water, biome);
                float chance = habitat * (0.11f + 0.89f * canopy) * (0.44f + 0.56f * depth);
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
