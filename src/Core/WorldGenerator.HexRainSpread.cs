namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    private static readonly Hex[] SecondRainRing =
    {
        new(2, 0), new(1, 1), new(0, 2), new(-1, 2), new(-2, 2), new(-2, 1),
        new(-2, 0), new(-1, -1), new(0, -2), new(1, -2), new(2, -2), new(2, -1)
    };

    internal static float SpreadHexRain(WorldData world, float[] source, int cell)
    {
        if (!world.Domain.Contains(cell)) return 0;
        bool twoRings = world.Manifest.GeneratorVersion >= BalancedRainVersion;
        float rain = source[cell] * (twoRings ? 0.50f : 0.75f);
        var hex = HexGrid.FromOffset(cell % world.Width, cell / world.Width);
        // Read only the immutable base field: newly received rain must not spread again in this pass.
        for (int direction = 0; direction < 6; direction++)
        {
            int neighbor = HexGrid.NeighborIndex(hex, direction, world.Width, world.Height, world.Manifest.HorizontalWrap);
            if (world.Domain.Contains(neighbor) && AllowsRainRise(world, neighbor, cell)) rain += source[neighbor] * (twoRings ? 0.15f : 0.25f);
        }
        if (twoRings)
            foreach (var offset in SecondRainRing)
            {
                var (col, row) = HexGrid.ToOffset(new Hex(hex.Q + offset.Q, hex.R + offset.R));
                if (world.Manifest.HorizontalWrap) col = (col % world.Width + world.Width) % world.Width;
                if (!world.Domain.Contains(col, row)) continue;
                int origin = row * world.Width + col;
                if (world.Manifest.GeneratorVersion >= TerrainRainVersion && !RainReachesSecondRing(world, origin, cell, hex)) continue;
                rain += source[origin] * 0.05f;
            }
        return rain;
    }
    private static bool AllowsRainRise(WorldData world, int origin, int destination) =>
        world.Manifest.GeneratorVersion < TerrainRainVersion || world.Elevation[destination] - world.Elevation[origin] <= 500;

    private static bool RainReachesSecondRing(WorldData world, int origin, int destination, Hex target)
    {
        if (!AllowsRainRise(world, origin, destination)) return false;
        var start = HexGrid.FromOffset(origin % world.Width, origin / world.Width);
        for (int d = 0; d < 6; d++)
        {
            int middle = HexGrid.NeighborIndex(target, d, world.Width, world.Height, world.Manifest.HorizontalWrap);
            if (!world.Domain.Contains(middle)) continue;
            var step = HexGrid.FromOffset(middle % world.Width, middle / world.Width);
            if (HexGrid.Distance(start, step, world.Width, world.Manifest.HorizontalWrap) == 1 &&
                AllowsRainRise(world, origin, middle) && AllowsRainRise(world, middle, destination)) return true;
        }
        return false;
    }
}
