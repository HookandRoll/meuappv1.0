using System;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    private sealed partial class ContinentalSculpture
    {
        private float AtlasIslandConstruction(int col, int row, float broadPulse)
        {
            // Most oceanic arc construction is resolved in hexes, rather than
            // stretching every volcanic island with the atlas dimensions.
            int columns = Math.Max(1, _width / 10);
            float stepX = _width * HexGrid.Root3 / columns, stepY = 15f;
            var point = HexGrid.Center(HexAt(row * _width + col));
            int tileX = (int)MathF.Floor(point.X / stepX), tileY = (int)MathF.Floor(point.Y / stepY);
            float cones = 0;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int x = tileX + dx, y = tileY + dy;
                int keyX = _wrap ? (x % columns + columns) % columns : x;
                uint hash = unchecked((uint)keyX * 0x9E3779B9u ^ (uint)y * 0x85EBCA6Bu ^ _seed ^ 0x5A71C39Du);
                hash = unchecked((hash ^ (hash >> 16)) * 0x7FEB352Du); hash ^= hash >> 15;
                float cx = (x + 0.2f + (hash & 255) / 425f) * stepX;
                float cy = (y + 0.2f + ((hash >> 8) & 255) / 425f) * stepY;
                float radius = 3.5f + ((hash >> 16) & 255) / 85f;
                float ex = (point.X - cx) / radius, ey = (point.Y - cy) / (radius * 0.8f);
                cones = Math.Max(cones, Math.Max(0, 1 - ex * ex - ey * ey));
            }
            // Some sectors still grow large primary islands. Small cones are
            // positive volcanic additions; no existing island is hollowed out.
            float primary = Smooth(Math.Clamp((FieldNoise(col, row, 7, _seed ^ 0xA731D59Fu) - 0.48f) / 0.22f, 0, 1));
            return Math.Max(cones, broadPulse * primary);
        }
    }
}
