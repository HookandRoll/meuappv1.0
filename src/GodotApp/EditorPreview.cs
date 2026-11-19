using System;
using System.Collections.Generic;
using Godot;
using Mapmaker.Core;

namespace Mapmaker.GodotApp;

internal sealed class EditorPreview : IDisposable
{
    private readonly Image _scratch;
    private readonly int _width;
    private readonly int _cellsPerBand;
    private readonly byte[][] _bands;
    private readonly HashSet<int> _dirtyBands = new();
    private double _elapsed;
    private ImageTexture? _climate;

    internal Texture2DArray Texture { get; }

    internal EditorPreview(WorldData world)
    {
        _width = world.Width;
        _cellsPerBand = _width * WorldData.ChunkSize;
        _bands = new byte[world.Chunks.Rows][];
        for (int band = 0; band < _bands.Length; band++)
            _bands[band] = new byte[_cellsPerBand * 4];
        Rebuild(world);

        var images = new Godot.Collections.Array<Image>();
        foreach (byte[] pixels in _bands)
            images.Add(Image.CreateFromData(_width, WorldData.ChunkSize, false, Image.Format.Rgba8, pixels));
        Texture = new Texture2DArray();
        Error error = Texture.CreateFromImages(images);
        foreach (Image image in images)
            image.Dispose();
        if (error != Error.Ok)
        {
            Texture.Dispose();
            throw new InvalidOperationException($"Não foi possível criar a prévia da edição: {error}.");
        }
        _scratch = Image.CreateEmpty(_width, WorldData.ChunkSize, false, Image.Format.Rgba8);
        _dirtyBands.Clear();
    }

    internal void Rebuild(WorldData world)
    {
        for (int i = 0; i < world.Count; i++)
            UpdateCell(world, i);
    }

    internal void UpdateCell(WorldData world, int cell)
    {
        int band = cell / _cellsPerBand;
        int i = cell % _cellsPerBand * 4;
        byte[] pixels = _bands[band];
        ushort elevation = (ushort)(world.Elevation[cell] - short.MinValue);
        byte low = (byte)elevation;
        byte high = (byte)(elevation >> 8);
        byte biome = (byte)world.Biomes[cell];
        var locks = (HexLocks)world.LockMask[cell];
        int lockLevel = (locks & HexLocks.All) == HexLocks.All ? 2 : locks == 0 ? 0 : 1;
        byte surface = (byte)((int)world.Water[cell] | (int)world.Relief[cell] << 2 | lockLevel << 6);
        if (pixels[i] == low && pixels[i + 1] == high && pixels[i + 2] == biome && pixels[i + 3] == surface)
            return;

        pixels[i] = low;
        pixels[i + 1] = high;
        pixels[i + 2] = biome;
        pixels[i + 3] = surface;
        _dirtyBands.Add(band);
    }

    internal void Refresh(double delta, bool force = false)
    {
        _elapsed += delta;
        if (_dirtyBands.Count == 0 || (!force && _elapsed < 1.0 / 30))
            return;

        foreach (int band in _dirtyBands)
        {
            _scratch.SetData(_width, WorldData.ChunkSize, false, Image.Format.Rgba8, _bands[band]);
            Texture.UpdateLayer(_scratch, band);
        }
        _dirtyBands.Clear();
        _elapsed = 0;
    }

    internal ImageTexture RefreshClimate(WorldData world)
    {
        var pixels = new byte[world.Count * 4];
        for (int cell = 0; cell < world.Count; cell++)
        {
            int i = cell * 4;
            ushort temperature = (ushort)(world.Temperature[cell] - short.MinValue);
            ushort rainfall = world.Rainfall[cell];
            pixels[i] = (byte)temperature;
            pixels[i + 1] = (byte)(temperature >> 8);
            pixels[i + 2] = (byte)rainfall;
            pixels[i + 3] = (byte)(rainfall >> 8);
        }
        using Image image = Image.CreateFromData(world.Width, world.Height, false, Image.Format.Rgba8, pixels);
        if (_climate is null)
            _climate = ImageTexture.CreateFromImage(image);
        else
            _climate.Update(image);
        return _climate;
    }

    public void Dispose()
    {
        Texture.Dispose();
        _climate?.Dispose();
        _scratch.Dispose();
    }
}
