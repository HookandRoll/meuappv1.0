using System;
using System.Collections.Generic;

namespace Mapmaker.Core;

public readonly record struct WorldRegion(int Column, int Row, int Width, int Height)
{
    public bool Contains(int column, int row) =>
        column >= Column && column < Column + Width && row >= Row && row < Row + Height;

    public IEnumerable<int> Cells(int worldWidth)
    {
        for (int row = Row; row < Row + Height; row++)
        for (int column = Column; column < Column + Width; column++)
            yield return row * worldWidth + column;
    }
}

public readonly struct ChunkGrid
{
    private readonly int _width;
    private readonly int _height;
    internal int Width => _width;
    public int Columns => (_width + WorldData.ChunkSize - 1) / WorldData.ChunkSize;
    public int Rows => (_height + WorldData.ChunkSize - 1) / WorldData.ChunkSize;
    public int Count => Columns * Rows;

    internal ChunkGrid(int width, int height)
    {
        _width = width;
        _height = height;
    }

    public WorldRegion GetChunk(int index)
    {
        if ((uint)index >= (uint)Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        int column = index % Columns * WorldData.ChunkSize;
        int row = index / Columns * WorldData.ChunkSize;
        return new WorldRegion(column, row, Math.Min(WorldData.ChunkSize, _width - column),
            Math.Min(WorldData.ChunkSize, _height - row));
    }
}
