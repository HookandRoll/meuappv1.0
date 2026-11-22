using System;
using System.Collections.Generic;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    private const int MoisturePerLevel = 30;

    private static int WaterMoistureScale(int height) => Math.Clamp((height + 255) / 256, 1, 4);

    private static byte[] SpreadWaterMoisture(WorldData world, WaterType[] water, short[] elevation, bool fromRivers = false)
    {
        int width = world.Width, height = world.Height;
        int scale = WaterMoistureScale(height);
        var moisture = new byte[world.Count];
        var pending = new List<int>[6 * scale + 1];
        bool wrap = world.Manifest.HorizontalWrap;

        void Supply(int cell, int level)
        {
            if (level <= moisture[cell]) return;
            moisture[cell] = (byte)level;
            (pending[level] ??= new List<int>()).Add(cell);
        }

        bool CanCarry(int cell) => water[cell] == WaterType.None && elevation[cell] < 2800;

        if (fromRivers)
        {
            foreach (int cell in world.HexToRivers.Keys)
                if (world.Domain.Contains(cell) && CanCarry(cell))
                    Supply(cell, 3 * scale);
        }
        else
        {
            for (int row = 0; row < height; row++)
            {
                var (left, right) = world.Domain.RowBounds(row);
                for (int col = left; col < right; col++)
                {
                    int cell = row * width + col;
                    if (!CanCarry(cell)) continue;
                    Hex hex = HexGrid.FromOffset(col, row);
                    int source = 0;
                    for (int direction = 0; direction < 6; direction++)
                    {
                        int neighbor = HexGrid.NeighborIndex(hex, direction, width, height, wrap);
                        if (!world.Domain.Contains(neighbor)) continue;
                        if (water[neighbor] == WaterType.Ocean) { source = 6; break; }
                        if (water[neighbor] == WaterType.Lake) source = 4;
                    }
                    if (source > 0) Supply(cell, source * scale);
                }
            }
        }

        // Process the strongest sources first so overlapping halos use the strongest contribution.
        for (int level = pending.Length - 1; level > 1; level--)
        {
            if (pending[level] is not List<int> cells) continue;
            foreach (int cell in cells)
            {
                if (moisture[cell] != level) continue;
                Hex hex = HexGrid.FromOffset(cell % width, cell / width);
                for (int direction = 0; direction < 6; direction++)
                {
                    int neighbor = HexGrid.NeighborIndex(hex, direction, width, height, wrap);
                    if (world.Domain.Contains(neighbor) && CanCarry(neighbor))
                        Supply(neighbor, level - 1);
                }
            }
        }
        return moisture;
    }
}
