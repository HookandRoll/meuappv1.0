using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace Mapmaker.Core;

[Flags]
public enum HexLocks : ushort
{
    None = 0,
    Biome = 1 << 0,
    Relief = 1 << 1,
    Water = 1 << 2,
    Elevation = 1 << 3,
    Temperature = 1 << 4,
    Rainfall = 1 << 5,
    Roads = 1 << 6,
    Rivers = 1 << 7,
    Flags = 1 << 8,
    Landform = 1 << 9,
    Features = 1 << 10,
    All = Biome | Relief | Water | Elevation | Temperature | Rainfall | Roads | Rivers | Flags | Landform | Features
}

public enum RegenerationMode { Everything, KeepWater, KeepWaterAndElevation }

internal sealed class UndoManager
{
    private readonly record struct Cell(short Elevation, short Temperature, ushort Rainfall,
        ushort Roads, ushort Rivers, ushort Locks, WaterType Water, ReliefType Relief, BiomeType Biome, byte Flags,
        LandformType Landform, GeographicFeatures Features)
    {
        internal static Cell Read(WorldData world, int i) => new(world.Elevation[i], world.Temperature[i],
            world.Rainfall[i], world.RoadEdges[i], world.RiverEdges[i], world.LockMask[i],
            world.Water[i], world.Relief[i], world.Biomes[i], world.Flags[i], world.Landforms[i], world.Features[i]);

        internal void Restore(WorldData world, int i)
        {
            world.Elevation[i] = Elevation;
            world.Water[i] = Water;
            world.Relief[i] = Relief;
            world.Biomes[i] = Biome;
            world.Temperature[i] = Temperature;
            world.Rainfall[i] = Rainfall;
            world.RoadEdges[i] = Roads;
            world.RiverEdges[i] = Rivers;
            world.Flags[i] = Flags;
            world.LockMask[i] = Locks;
            world.Landforms[i] = Landform;
            world.Features[i] = Features;
        }
    }

    private sealed class ChunkSnapshot
    {
        private readonly WorldRegion _region;
        private readonly Cell[]? _cells;
        private readonly byte[]? _compressed;
        internal bool Compressed => _compressed is not null;
        internal long Bytes => _compressed?.LongLength ?? (long)_cells!.Length * System.Runtime.CompilerServices.Unsafe.SizeOf<Cell>();

        internal ChunkSnapshot(WorldData world, WorldRegion region, bool compress = false)
        {
            _region = region;
            var cells = new Cell[region.Width * region.Height];
            int j = 0;
            foreach (int i in region.Cells(world.Width))
                cells[j++] = Cell.Read(world, i);
            if (compress)
            {
                using var output = new MemoryStream();
                using (var stream = new DeflateStream(output, CompressionLevel.Fastest, leaveOpen: true))
                    stream.Write(MemoryMarshal.AsBytes(cells.AsSpan()));
                _compressed = output.ToArray();
            }
            else _cells = cells;
        }

        private Cell[] ReadCells()
        {
            if (_cells is not null) return _cells;
            var cells = new Cell[_region.Width * _region.Height];
            using var input = new MemoryStream(_compressed!);
            using var stream = new DeflateStream(input, CompressionMode.Decompress);
            stream.ReadExactly(MemoryMarshal.AsBytes(cells.AsSpan()));
            return cells;
        }

        internal void Restore(WorldData world)
        {
            var cells = ReadCells();
            int j = 0;
            foreach (int i in _region.Cells(world.Width))
                cells[j++].Restore(world, i);
        }

        internal bool Matches(ChunkSnapshot other) => ReadCells().AsSpan().SequenceEqual(other.ReadCells());
    }

    private sealed record Command(ChunkSnapshot[] Before, ChunkSnapshot[] After, int Revision, int PreviousRevision, JsonElement SparseBefore, JsonElement SparseAfter);

    private const int Limit = 32;
    private const long SnapshotBudget = 128L * 1024 * 1024;
    private WorldData _world;
    private readonly Dictionary<int, ChunkSnapshot> _pending = new();
    private readonly LinkedList<Command> _undo = new();
    private readonly Stack<Command> _redo = new();
    private int _nextRevision;
    private JsonElement? _pendingSparse;

    internal UndoManager(WorldData world) => _world = world;
    internal void UseWorld(WorldData world) => _world = world;
    internal UndoManager Fork(WorldData world)
    {
        var copy = new UndoManager(world) { Revision = Revision, _nextRevision = _nextRevision };
        foreach (Command command in _undo)
            copy._undo.AddLast(command);
        foreach (Command command in _redo.Reverse())
            copy._redo.Push(command);
        return copy;
    }

    internal int Revision { get; private set; }
    internal bool CanUndo => _undo.Count > 0;
    internal bool CanRedo => _redo.Count > 0;

    internal void Capture(int cell)
    {
        int size = WorldData.ChunkSize;
        int chunk = cell / _world.Width / size * _world.Chunks.Columns + cell % _world.Width / size;
        CaptureChunk(chunk);
    }

    internal void Capture(WorldRegion region)
    {
        bool compress = (long)region.Width * region.Height > _world.Count / 2;
        int size = WorldData.ChunkSize;
        int columns = _world.Chunks.Columns;
        for (int row = region.Row / size; row <= (region.Row + region.Height - 1) / size; row++)
        for (int column = region.Column / size; column <= (region.Column + region.Width - 1) / size; column++)
            CaptureChunk(row * columns + column, compress);
    }

    internal void CaptureFullWorld() => Capture(new WorldRegion(0, 0, _world.Width, _world.Height));

    private void CaptureChunk(int chunk, bool compress = false)
    {
        _pendingSparse ??= _world.SparseEntities;
        if (!_pending.ContainsKey(chunk))
            _pending.Add(chunk, new ChunkSnapshot(_world, _world.Chunks.GetChunk(chunk), compress));
    }

    internal bool Commit()
    {
        var before = new List<ChunkSnapshot>();
        var after = new List<ChunkSnapshot>();
        foreach (var (chunk, snapshot) in _pending)
        {
            var current = new ChunkSnapshot(_world, _world.Chunks.GetChunk(chunk), snapshot.Compressed);
            if (snapshot.Matches(current))
                continue;
            before.Add(snapshot);
            after.Add(current);
        }
        _pending.Clear();
        JsonElement sparseBefore = _pendingSparse ?? _world.SparseEntities;
        _pendingSparse = null;
        if (before.Count == 0 && sparseBefore.GetRawText() == _world.SparseEntities.GetRawText())
            return false;

        _redo.Clear();
        int previous = Revision;
        Revision = ++_nextRevision;
        _undo.AddLast(new Command(before.ToArray(), after.ToArray(), Revision, previous, sparseBefore, _world.SparseEntities));
        static long Bytes(Command command) => command.Before.Sum(snapshot => snapshot.Bytes) + command.After.Sum(snapshot => snapshot.Bytes);
        long snapshotBytes = _undo.Sum(Bytes);
        while (_undo.Count > 0 && (_undo.Count > Limit || snapshotBytes > SnapshotBudget))
        {
            snapshotBytes -= Bytes(_undo.First!.Value);
            _undo.RemoveFirst();
        }
        return true;
    }

    internal void Cancel()
    {
        foreach (ChunkSnapshot snapshot in _pending.Values)
            snapshot.Restore(_world);
        _pending.Clear();
        if (_pendingSparse is JsonElement sparse) { _world.SparseEntities = sparse; WorldGenerator.RebuildRivers(_world); }
        _pendingSparse = null;
    }

    internal bool Undo()
    {
        if (_undo.Last is not { } last)
            return false;
        Command command = last.Value;
        foreach (ChunkSnapshot snapshot in command.Before)
            snapshot.Restore(_world);
        _world.SparseEntities = command.SparseBefore;
        WorldGenerator.RebuildRivers(_world);
        _undo.RemoveLast();
        _redo.Push(command);
        Revision = command.PreviousRevision;
        return true;
    }

    internal bool Redo()
    {
        if (!_redo.TryPop(out Command? command))
            return false;
        foreach (ChunkSnapshot snapshot in command.After)
            snapshot.Restore(_world);
        _world.SparseEntities = command.SparseAfter;
        WorldGenerator.RebuildRivers(_world);
        _undo.AddLast(command);
        Revision = command.Revision;
        return true;
    }
}
