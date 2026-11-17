using System;
using Godot;
using Mapmaker.Core;

namespace Mapmaker.GodotApp;

internal sealed class GlobalDataTextures : IDisposable
{
    private readonly Image _terrainImage;
    private readonly Image _climateImage;
    public ImageTexture Terrain { get; }
    public ImageTexture Climate { get; }

    public GlobalDataTextures(WorldData world)
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
            MarkVegetationPatches(world, terrain);

        _terrainImage = Image.CreateFromData(world.Width, world.Height, false, Image.Format.Rgba8, terrain);
        _climateImage = Image.CreateFromData(world.Width, world.Height, false, Image.Format.Rgba8, climate);
        Terrain = ImageTexture.CreateFromImage(_terrainImage);
        Climate = ImageTexture.CreateFromImage(_climateImage);
    }

    public void UploadAll()
    {
        Terrain.Update(_terrainImage);
        Climate.Update(_climateImage);
    }

    public void Dispose()
    {
        Terrain.Dispose();
        Climate.Dispose();
        _terrainImage.Dispose();
        _climateImage.Dispose();
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

    private static void MarkVegetationPatches(WorldData world, byte[] terrain)
    {
        // Seed up to four patches from each logical chunk. A patch may cross a chunk boundary.
        const int patchSpacing = WorldData.ChunkSize / 2;
        for (int startRow = 0; startRow < world.Height; startRow += patchSpacing)
        for (int startColumn = 0; startColumn < world.Width; startColumn += patchSpacing)
        {
            uint patch = StableRank((startRow * world.Width + startColumn) ^ world.Manifest.Seed);
            if ((patch & 7u) == 0)
                continue;

            int centerX = Math.Min(world.Width - 1, startColumn + 3 + (int)((patch >> 3) % 10u));
            int centerY = Math.Min(world.Height - 1, startRow + 3 + (int)((patch >> 11) % 10u));
            int radiusX = 4 + (int)((patch >> 19) & 3u);
            int radiusY = 3 + (int)((patch >> 24) & 3u);
            MarkVegetationPatch(world, terrain, centerX, centerY, radiusX, radiusY);
        }
    }

    private static void MarkVegetationPatch(WorldData world, byte[] terrain, int centerX, int centerY, int radiusX, int radiusY)
    {
        for (int row = Math.Max(0, centerY - radiusY); row <= Math.Min(world.Height - 1, centerY + radiusY); row++)
        for (int x = centerX - radiusX; x <= centerX + radiusX; x++)
        {
            int column = world.Manifest.HorizontalWrap ? (x + world.Width) % world.Width : x;
            if ((uint)column >= (uint)world.Width)
                continue;

            int cell = row * world.Width + column;
            if (world.Relief[cell] > ReliefType.Hills)
                continue;
            uint rank = StableRank(cell ^ world.Manifest.Seed);
            float dx = (x - centerX + ((row & 1) - (centerY & 1)) * 0.5f) / radiusX;
            float dy = (row - centerY) / (float)radiusY;
            if (dx * dx + dy * dy > 0.73f + (rank & 15u) * 0.022f)
                continue;

            WaterType water = world.Water[cell];
            BiomeType biome = world.Biomes[cell];
            bool wooded = water == WaterType.None && Biomes.IsForest(biome);
            bool scattered = water == WaterType.Wetland ||
                (water == WaterType.None && (biome is BiomeType.Savanna or BiomeType.Desert));
            if ((wooded && rank % 6u != 0) || (scattered && rank % 4u == 0))
                terrain[cell * 4 + 3] |= 1 << 7;
        }
    }

    private static uint StableRank(int cell)
    {
        uint value = (uint)cell * 0x9E3779B9u;
        value = (value ^ (value >> 16)) * 0x7FEB352Du;
        return value ^ (value >> 15);
    }
}
