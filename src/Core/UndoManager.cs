using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

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
    All = Biome | Relief | Water | Elevation | Temperature | Rainfall | Roads | Rivers | Flags
}

public enum RegenerationMode { Everything, KeepWater, KeepWaterAndElevation }

internal sealed class UndoManager
{
    private readonly record struct Cell(short Elevation, short Temperature, ushort Rainfall,
        ushort Roads, ushort Rivers, ushort Locks, WaterType Water, ReliefType Relief, BiomeType Biome, byte Flags)
    {
        internal static Cell Read(WorldData world, int i) => new(world.Elevation[i], world.Temperature[i],
            world.Rainfall[i], world.RoadEdges[i], world.RiverEdges[i], world.LockMask[i],
            world.Water[i], world.Relief[i], world.Biomes[i], world.Flags[i]);

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
        }
    }

    private sealed class ChunkSnapshot
    {
        private readonly WorldRegion _region;
        private readonly Cell[] _cells;

        internal ChunkSnapshot(WorldData world, WorldRegion region)
        {
            _region = region;
            _cells = new Cell[region.Width * region.Height];
            int j = 0;
            foreach (int i in region.Cells(world.Width))
                _cells[j++] = Cell.Read(world, i);
        }

        internal void Restore(WorldData world)
        {
            int j = 0;
            foreach (int i in _region.Cells(world.Width))
                _cells[j++].Restore(world, i);
        }

        internal bool Matches(ChunkSnapshot other) => _cells.AsSpan().SequenceEqual(other._cells);
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
        int size = WorldData.ChunkSize;
        int columns = _world.Chunks.Columns;
        for (int row = region.Row / size; row <= (region.Row + region.Height - 1) / size; row++)
        for (int column = region.Column / size; column <= (region.Column + region.Width - 1) / size; column++)
            CaptureChunk(row * columns + column);
    }

    private void CaptureChunk(int chunk)
    {
        _pendingSparse ??= _world.SparseEntities;
        if (!_pending.ContainsKey(chunk))
            _pending.Add(chunk, new ChunkSnapshot(_world, _world.Chunks.GetChunk(chunk)));
    }

    internal bool Commit()
    {
        var before = new List<ChunkSnapshot>();
        var after = new List<ChunkSnapshot>();
        foreach (var (chunk, snapshot) in _pending)
        {
            var current = new ChunkSnapshot(_world, _world.Chunks.GetChunk(chunk));
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
        int chunkPairs = _undo.Sum(command => command.Before.Length);
        while (_undo.Count > 1 && (_undo.Count > Limit ||
            chunkPairs * (2L * WorldData.ChunkSize * WorldData.ChunkSize * 16) > SnapshotBudget))
        {
            chunkPairs -= _undo.First!.Value.Before.Length;
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
