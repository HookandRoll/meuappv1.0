using System;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    private static bool IsSwampMargin(WorldData world, int cell)
    {
        if (!world.Domain.Contains(cell) || world.Water[cell] != WaterType.None ||
            world.Elevation[cell] is < 0 or > 260 || world.Temperature[cell] <= 55 ||
            world.Relief[cell] > ReliefType.Rolling) return false;
        bool river = world.RiverEdges[cell] != 0;
        if (world.Rainfall[cell] < (river ? 300 : 650)) return false;
        var hex = HexGrid.FromOffset(cell % world.Width, cell / world.Width);
        bool margin = river;
        for (int d = 0; d < 6; d++)
        {
            int n = HexGrid.NeighborIndex(hex, d, world.Width, world.Height, world.Manifest.HorizontalWrap);
            if (!world.Domain.Contains(n)) continue;
            if (world.Water[n] == WaterType.None)
            {
                if (Math.Abs(world.Elevation[n] - world.Elevation[cell]) > 120) return false;
            }
            else if (world.Water[n] is WaterType.Lake or WaterType.Wetland && world.Elevation[n] >= -180) margin = true;
            else if (world.Water[n] == WaterType.Ocean && world.Elevation[n] >= -100)
            {
                // Confined shallow coves, not every exposed ocean coast.
                int dry = 0; var waterHex = HexGrid.FromOffset(n % world.Width, n / world.Width);
                for (int side = 0; side < 6; side++)
                {
                    int bank = HexGrid.NeighborIndex(waterHex, side, world.Width, world.Height, world.Manifest.HorizontalWrap);
                    if (world.Domain.Contains(bank) && world.Water[bank] == WaterType.None) dry++;
                }
                if (dry >= 3) margin = true;
            }
        }
        if (!margin) return false;
        float patch = TerrainNoise((cell % world.Width) / (float)world.Height, cell / world.Width / (float)world.Height,
            37, world.Width / (float)world.Height, world.Manifest.HorizontalWrap, unchecked((uint)world.Manifest.Seed) ^ 0xC49A371Du);
        return patch > 0.27f;
    }
}
