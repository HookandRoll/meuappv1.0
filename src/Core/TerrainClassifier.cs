using System;
using System.Collections.Generic;

namespace Mapmaker.Core;

public readonly record struct TerrainClassification(ReliefType Relief, LandformType Landform);

// Classifies shape without changing altitude, water, climate, biomes or features.
public static class TerrainClassifier
{
    public static int RegionalRadius(WorldManifest manifest) => Math.Max(1, manifest.Height / 256);

    internal readonly struct Samples
    {
        internal readonly WorldData World;
        internal readonly int Radius;
        internal readonly double Scale;
        private readonly WorldRegion _bounds;
        private readonly short[] _elevation;
        private readonly WaterType[] _water;
        private readonly bool _full;

        internal Samples(WorldData world, WorldRegion? bounds = null, short[]? elevation = null, WaterType[]? water = null)
        {
            World = world;
            Radius = RegionalRadius(world.Manifest);
            // Cartographic relief equivalent at the default 5 km/hex scale.
            Scale = Math.Clamp(5 / world.Manifest.HexDistance, 0.05, 5);
            _bounds = bounds ?? new WorldRegion(0, 0, world.Width, world.Height);
            _elevation = elevation ?? world.Elevation; _water = water ?? world.Water;
            _full = _bounds.Column == 0 && _bounds.Row == 0 && _bounds.Width == world.Width && _bounds.Height == world.Height;
        }

        private int Local(int cell)
        {
            if (_full) return cell;
            int row = cell / World.Width - _bounds.Row;
            int col = cell % World.Width - _bounds.Column;
            if (World.Manifest.HorizontalWrap) col = (col % World.Width + World.Width) % World.Width;
            return (uint)row < _bounds.Height && (uint)col < _bounds.Width ? row * _bounds.Width + col : -1;
        }
        internal int Height(int cell)
        {
            int local = Local(cell);
            return local < 0 ? World.Elevation[cell] : _elevation[local];
        }
        internal bool IsLand(int cell)
        {
            if (!World.Domain.Contains(cell)) return false;
            int local = Local(cell);
            return (local < 0 ? World.Water[cell] : _water[local]) == WaterType.None;
        }
    }

    public static TerrainClassification Classify(WorldData world, int cell) => Classify(new Samples(world), cell);

    internal static TerrainClassification Classify(in Samples samples, int cell)
    {
        if (!samples.IsLand(cell)) return new(ReliefType.Flat, LandformType.None);
        var world = samples.World;
        int h = samples.Height(cell), minimum = h, maximum = h;
        int valid = 0, higher = 0, lower = 0, level = 0;
        Hex hex = HexGrid.FromOffset(cell % world.Width, cell / world.Width);
        for (int direction = 0; direction < 6; direction++)
        {
            int selected = -1;
            int near = HexGrid.NeighborIndex(hex, direction, world.Width, world.Height, world.Manifest.HorizontalWrap);
            if (samples.IsLand(near))
            {
                int height = samples.Height(near);
                minimum = Math.Min(minimum, height); maximum = Math.Max(maximum, height);
                selected = near;
            }
            if (samples.Radius > 1)
            {
                int regional = HexGrid.NeighborIndex(hex, direction, world.Width, world.Height, world.Manifest.HorizontalWrap, samples.Radius);
                if (samples.IsLand(regional))
                {
                    int height = samples.Height(regional);
                    minimum = Math.Min(minimum, height); maximum = Math.Max(maximum, height);
                    selected = regional;
                }
            }
            if (selected < 0) continue;
            int difference = samples.Height(selected) - h;
            valid++;
            if (difference >= 250) higher++;
            if (difference <= -250) lower++;
            if (Math.Abs(difference) < 120) level++;
        }

        int drop = h - minimum, rise = maximum - h;
        double down = drop * samples.Scale, up = rise * samples.Scale;
        if (h >= 3200 && down >= 400 && drop >= 250 && lower >= 3 && rise <= Math.Max(80, drop / 4))
            return new(ReliefType.Peak, LandformType.Peak);
        if (down >= 600 && drop >= 250)
            return new(ReliefType.Mountains, h >= 700 && level >= 2 ? LandformType.Escarpment : LandformType.Mountains);
        if (valid >= 4 && higher >= 3 && drop < 120)
            return new(up >= 100 ? ReliefType.Rolling : ReliefType.Flat, LandformType.Valley);
        if (down >= 250 && drop >= 100)
            return new(ReliefType.Hills, LandformType.Hills);
        // An ascending face is not a flat plateau, nor a peak at the foot of a cliff.
        if (up >= 250 && rise >= 100)
            return new(ReliefType.Hills, up >= 600 ? LandformType.Foothills : LandformType.Slope);
        bool rolling = Math.Max(down, up) >= 100;
        if (h >= 700) return new(rolling ? ReliefType.Rolling : ReliefType.Flat, LandformType.Plateau);
        return new(rolling ? ReliefType.Rolling : ReliefType.Flat, rolling ? LandformType.RollingPlain : LandformType.Plain);
    }

    public static void ReclassifyAround(WorldData world, IEnumerable<int> edited,
        Action<int>? beforeChange = null, Action<int>? afterChange = null, WorldRegion? selection = null)
    {
        if (world.Manifest.Generation.Stage < GenerationStage.Relief) return;
        var samples = new Samples(world);
        var affected = new HashSet<int>();
        foreach (int cell in edited)
        {
            if (!world.Domain.Contains(cell)) continue;
            affected.Add(cell);
            var hex = HexGrid.FromOffset(cell % world.Width, cell / world.Width);
            for (int d = 0; d < 6; d++)
            {
                affected.Add(HexGrid.NeighborIndex(hex, d, world.Width, world.Height, world.Manifest.HorizontalWrap));
                if (samples.Radius > 1) affected.Add(HexGrid.NeighborIndex(hex, d, world.Width, world.Height, world.Manifest.HorizontalWrap, samples.Radius));
            }
        }
        foreach (int cell in affected)
            if (world.Domain.Contains(cell) && (selection is not WorldRegion bounds || bounds.Contains(cell % world.Width, cell / world.Width)))
                Apply(world, cell, Classify(samples, cell), true, true, beforeChange, afterChange);
    }

    public static void ReclassifyRegion(WorldData world, WorldRegion region, bool updateRelief = true, bool updateLandform = true)
    {
        if (region.Width <= 0 || region.Height <= 0 || region.Column < 0 || region.Row < 0 ||
            region.Column > world.Width - region.Width || region.Row > world.Height - region.Height)
            throw new ArgumentOutOfRangeException(nameof(region));
        if (world.Manifest.Generation.Stage < GenerationStage.Relief) return;
        var samples = new Samples(world);
        foreach (int cell in region.Cells(world.Width))
            if (world.Domain.Contains(cell)) Apply(world, cell, Classify(samples, cell), updateRelief, updateLandform, null, null);
    }

    private static void Apply(WorldData world, int cell, TerrainClassification result, bool updateRelief, bool updateLandform,
        Action<int>? beforeChange, Action<int>? afterChange)
    {
        var locks = (HexLocks)world.LockMask[cell];
        bool relief = updateRelief && (locks & HexLocks.Relief) == 0 && world.Relief[cell] != result.Relief;
        bool landform = updateLandform && (locks & HexLocks.Landform) == 0 && world.Landforms[cell] != result.Landform;
        if (!relief && !landform) return;
        beforeChange?.Invoke(cell);
        if (relief) world.Relief[cell] = result.Relief;
        if (landform) world.Landforms[cell] = result.Landform;
        afterChange?.Invoke(cell);
    }
}
