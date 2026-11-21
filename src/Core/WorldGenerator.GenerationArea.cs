using System;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    // Regional generation stages work in a temporary window. Only the requested rectangle is copied back.
    private sealed class GenerationArea
    {
        private readonly WorldRegion _bounds;
        private readonly int _worldWidth;
        private readonly bool _wrap;
        private readonly bool _fullWidth;
        private readonly WorldRegion? _selection;
        private readonly HexLocks _fields;
        private readonly ushort[] _locks;
        private readonly MapDomain _domain;
        public int WorldHeight { get; }
        public int Count => _bounds.Width * _bounds.Height;
        public ChunkGrid Chunks => new(_bounds.Width, _bounds.Height);
        public short[] Elevation { get; }
        public WaterType[] Water { get; }
        public ReliefType[] Relief { get; }
        public BiomeType[] Biomes { get; }
        public short[] Temperature { get; }
        public ushort[] Rainfall { get; }
        public byte[] Flags { get; }

        public GenerationArea(WorldData world, WorldRegion bounds, bool useWorldArrays = false,
            WorldRegion? selection = null, HexLocks fields = HexLocks.None)
        {
            _bounds = bounds;
            _selection = selection;
            _fields = fields;
            _locks = world.LockMask;
            _domain = world.Domain;
            _worldWidth = world.Width;
            WorldHeight = world.Height;
            _wrap = world.Manifest.HorizontalWrap;
            _fullWidth = bounds.Width == world.Width;
            Elevation = useWorldArrays ? world.Elevation : new short[Count];
            Water = useWorldArrays ? world.Water : new WaterType[Count];
            Relief = useWorldArrays ? world.Relief : new ReliefType[Count];
            Biomes = useWorldArrays ? world.Biomes : new BiomeType[Count];
            Temperature = useWorldArrays ? world.Temperature : new short[Count];
            Rainfall = useWorldArrays ? world.Rainfall : new ushort[Count];
            Flags = useWorldArrays ? world.Flags : new byte[Count];
        }

        public int WorldColumn(int column)
        {
            column += _bounds.Column;
            return column < 0 ? column + _worldWidth : column >= _worldWidth ? column - _worldWidth : column;
        }

        public int WorldRow(int row) => row + _bounds.Row;

        public bool Writes(int column, int row, int cell, HexLocks field) =>
            _domain.Contains(column, row) && (_selection is not WorldRegion region ||
            (region.Contains(column, row) && (_fields & field) != 0 && ((HexLocks)_locks[cell] & field) == 0));

        public Hex WorldHex(int column, int row) => HexGrid.FromOffset(WorldColumn(column), WorldRow(row));

        public int Neighbor(Hex hex, int direction, int steps = 1)
        {
            int index = HexGrid.NeighborIndex(hex, direction, _worldWidth, WorldHeight, _wrap, steps);
            if (index < 0)
                return -1;
            int row = index / _worldWidth - _bounds.Row;
            if ((uint)row >= (uint)_bounds.Height)
                return -1;
            if (_fullWidth)
                return index - _bounds.Row * _worldWidth;

            int column = index % _worldWidth - _bounds.Column;
            if (_wrap)
                column = column < 0 ? column + _worldWidth : column >= _worldWidth ? column - _worldWidth : column;
            return (uint)column < (uint)_bounds.Width ? row * _bounds.Width + column : -1;
        }

        public void CopyRegionTo(WorldData world, WorldRegion region)
        {
            int column = region.Column - _bounds.Column;
            GenerationStage stage = world.Manifest.Generation.Stage;
            for (int row = region.Row; row < region.Row + region.Height; row++)
            {
                int source = (row - _bounds.Row) * _bounds.Width + column;
                int destination = row * world.Width + region.Column;
                Array.Copy(Water, source, world.Water, destination, region.Width);
                Array.Copy(Elevation, source, world.Elevation, destination, region.Width);
                if (stage >= GenerationStage.Relief)
                    Array.Copy(Relief, source, world.Relief, destination, region.Width);
                if (stage == GenerationStage.Terrain)
                {
                    Array.Copy(Temperature, source, world.Temperature, destination, region.Width);
                    Array.Copy(Rainfall, source, world.Rainfall, destination, region.Width);
                    for (int offset = 0; offset < region.Width; offset++)
                    {
                        int i = destination + offset;
                        if (Writes(region.Column + offset, row, i, HexLocks.Biome))
                            world.Biomes[i] = Biomes[source + offset];
                    }
                }
                if ((_fields & HexLocks.Water) != 0)
                    for (int offset = 0; offset < region.Width; offset++)
                    {
                        int i = destination + offset;
                        if (((HexLocks)_locks[i] & HexLocks.Flags) == 0)
                            world.Flags[i] = (byte)((world.Flags[i] & ~WorldData.CoastFlag) | Flags[source + offset]);
                    }
            }
        }
    }
}
