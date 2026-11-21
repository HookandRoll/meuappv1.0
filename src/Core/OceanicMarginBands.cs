using System;
using System.Numerics;

namespace Mapmaker.Core;

// Noisy ocean belts follow the curved sides, independently of the polar quota.
public sealed class OceanicMarginBands
{
    private readonly WorldManifest _manifest;
    private readonly float[] _left, _right;
    public static bool Enabled(WorldManifest m) => m.GeneratorVersion >= 40 && m.Generation.Extent == MapExtent.World;

    public OceanicMarginBands(WorldManifest m)
    {
        _manifest = m;
        _left = new float[m.Height + 1]; _right = new float[m.Height + 1];
        for (int row = 0; row <= m.Height; row++)
            (_left[row], _right[row]) = Limits(m, row * 1.5f);
    }

    private static (float Left, float Right) Limits(WorldManifest m, float worldY)
    {
        float y = (worldY / 1.5f + 0.5f) / m.Height * 2 - 1;
        float span = MathF.Sqrt(Math.Max(0, 1 - 0.64f * y * y));
        float width = m.Width * HexGrid.Root3;
        float Depth(int side)
        {
            uint seed = unchecked((uint)m.Seed) ^ (side < 0 ? 0x45F1A873u : 0x98CD72B1u);
            float broad = WorldGenerator.TerrainNoise(worldY / (m.Height * 1.5f), side * 2.7f,
                5.7f, 1, false, seed) - 0.5f;
            float fine = WorldGenerator.TerrainNoise(worldY / (m.Height * 1.5f), side * 4.1f,
                23f, 1, false, seed ^ 0x3CA718B5u, angular: true) - 0.5f;
            return Math.Max(HexGrid.Root3 * 3, width * (0.055f + broad * 0.025f + fine * 0.018f));
        }
        return (((1 - span) * m.Width * 0.5f - 0.5f) * HexGrid.Root3 + Depth(-1),
            ((1 + span) * m.Width * 0.5f - 0.5f) * HexGrid.Root3 - Depth(1));
    }

    public (float Left, float Right) Bounds(float worldY)
    {
        float row = Math.Clamp(worldY / 1.5f, 0, _manifest.Height);
        int i = Math.Min((int)row, _manifest.Height - 1);
        return (float.Lerp(_left[i], _left[i + 1], row - i), float.Lerp(_right[i], _right[i + 1], row - i));
    }

    public int SideAt(Vector2 point)
    {
        var bounds = Bounds(point.Y);
        return point.X <= bounds.Left ? -1 : point.X >= bounds.Right ? 1 : 0;
    }
    public float DistanceToOcean(Vector2 point)
    {
        var bounds = Bounds(point.Y);
        return Math.Max(0, Math.Min(point.X - bounds.Left, bounds.Right - point.X));
    }
    internal static float SignedDistance(WorldManifest m, Vector2 point, int side)
    {
        var bounds = Limits(m, point.Y);
        return side < 0 ? point.X - bounds.Left : bounds.Right - point.X;
    }
}
