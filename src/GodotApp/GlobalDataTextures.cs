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

    public GlobalDataTextures(DemoWorld world)
    {
        byte[] terrain = new byte[world.Width * world.Height * 4];
        byte[] climate = new byte[terrain.Length];
        for (int row = 0; row < world.Height; row++)
        for (int column = 0; column < world.Width; column++)
        {
            int index = (row * world.Width + column) * 4;
            DemoHex hex = world[column, row];
            Pack(terrain, index, hex.Elevation);
            terrain[index + 2] = (byte)hex.Biome;
            terrain[index + 3] = (byte)hex.Relief;
            Pack(climate, index, hex.Temperature);
            Pack(climate, index + 2, hex.Rainfall);
        }

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
}
