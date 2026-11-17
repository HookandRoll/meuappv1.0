using System;

namespace Mapmaker.Core;

public enum Biome : byte { Water, Grassland, Forest, Highlands }
public enum Relief : byte { None, Trees, Mountain }

public readonly record struct DemoHex(
    ushort Elevation, ushort Temperature, ushort Rainfall, Biome Biome, Relief Relief);

public sealed class DemoWorld
{
    public const int ChunkSize = 32;
    private readonly DemoHex[] _hexes;

    public int Width { get; }
    public int Height { get; }
    public DemoHex this[int column, int row] => _hexes[row * Width + column];

    public DemoWorld(int width, int height)
    {
        Width = width;
        Height = height;
        _hexes = new DemoHex[width * height];

        for (int row = 0; row < height; row++)
        for (int column = 0; column < width; column++)
        {
            float x = (column + (row & 1) * 0.5f) / width;
            float y = (float)row / height;
            float land = MathF.Max(
                Island(x, y, 0.33f, 0.49f, 0.29f, 0.38f),
                Island(x, y, 0.73f, 0.51f, 0.19f, 0.34f));
            land = MathF.Max(land, Island(x, y, 0.56f, 0.76f, 0.11f, 0.16f));
            land += 0.14f * MathF.Sin(x * 51f + MathF.Sin(y * 21f) * 2f);
            land += 0.09f * MathF.Sin(y * 73f + x * 31f);
            land += 0.035f * MathF.Sin(x * 192f - y * 158f);
            float elevation = 0.42f + land * 0.33f;
            float temperature = 0.98f - MathF.Abs(y - 0.51f) * 1.45f - MathF.Max(land, 0) * 0.19f;
            float rainfall = 0.18f + 0.55f * Noise(x * 16f + 37f, y * 8f + 13f)
                + 0.21f * Noise(x * 43f + 53f, y * 21f + 19f);

            Biome biome = land < 0 ? Biome.Water
                : elevation > 0.65f ? Biome.Highlands
                : rainfall > 0.51f ? Biome.Forest : Biome.Grassland;
            Relief relief = (Hash(column, row) % 43u == 0) ? biome switch
            {
                Biome.Highlands => Relief.Mountain,
                Biome.Forest => Relief.Trees,
                _ => Relief.None
            } : Relief.None;

            _hexes[row * width + column] = new DemoHex(
                Quantize(elevation), Quantize(temperature), Quantize(rainfall), biome, relief);
        }
    }

    private static float Island(float x, float y, float cx, float cy, float rx, float ry)
    {
        float dx = (x - cx) / rx;
        float dy = (y - cy) / ry;
        return 1f - dx * dx - dy * dy;
    }

    private static ushort Quantize(float value) =>
        (ushort)MathF.Round(Math.Clamp(value, 0f, 1f) * ushort.MaxValue);

    private static float Noise(float x, float y)
    {
        int left = (int)MathF.Floor(x);
        int top = (int)MathF.Floor(y);
        float across = SmoothStep(x - left);
        float down = SmoothStep(y - top);
        float a = Lerp(Hash(left, top), Hash(left + 1, top), across);
        float b = Lerp(Hash(left, top + 1), Hash(left + 1, top + 1), across);
        return Lerp(a, b, down) / uint.MaxValue;
    }

    private static float SmoothStep(float t) => t * t * (3f - 2f * t);
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static uint Hash(int column, int row)
    {
        uint value = (uint)column * 0x9E3779B9u ^ (uint)row * 0x85EBCA6Bu;
        value = (value ^ (value >> 16)) * 0x7FEB352Du;
        return value ^ (value >> 15);
    }
}
