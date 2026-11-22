using System;
using System.Numerics;

namespace Mapmaker.Core;

// Rectangular storage, but only the atlas footprint is generated/editable in v37 Worlds.
public sealed class MapDomain
{
    public bool IsAtlas { get; }
    public int Width { get; }
    public int Height { get; }
    public int ActiveCount { get; }
    // The polar band (including v46 ice-covered land) is outside the land/sea quota.
    public int PercentageAreaCount { get; }
    private readonly int[]? _left;
    private readonly int[]? _right;
    private readonly PolarPlateBands? _quotaBands;

    public MapDomain(WorldManifest m)
    {
        Width = m.Width; Height = m.Height;
        IsAtlas = m.GeneratorVersion >= 37 && m.Generation.Extent == MapExtent.World;
        if (!IsAtlas) { PercentageAreaCount = ActiveCount = Width * Height; return; }
        _left = new int[Height]; _right = new int[Height];
        int count = 0;
        for (int row = 0; row < Height; row++)
        {
            double y = (row + 0.5) / Height * 2 - 1;
            double span = Math.Sqrt(Math.Max(0, 1 - 0.64 * y * y));
            double stagger = 0.5 + (row & 1) * 0.5;
            _left[row] = Math.Max(0, (int)Math.Ceiling((1 - span) * Width * 0.5 - stagger));
            _right[row] = Math.Min(Width, (int)Math.Floor((1 + span) * Width * 0.5 - stagger) + 1);
            count += _right[row] - _left[row];
        }
        ActiveCount = count;
        if (m.GeneratorVersion >= 39)
        {
            var bands = _quotaBands = new PolarPlateBands(m);
            for (int row = 0; row < Height; row++)
            for (int col = _left[row]; col < _right[row]; col++)
                if (bands.KindAt(HexGrid.Center(HexGrid.FromOffset(col, row))) != PlateKind.Normal) count--;
        }
        PercentageAreaCount = count;
    }

    public bool Contains(int col, int row) => (uint)row < Height && (uint)col < Width &&
        (!IsAtlas || (col >= _left![row] && col < _right![row]));
    public bool Contains(int cell) => (uint)cell < Width * Height && Contains(cell % Width, cell / Width);
    public bool CountsTowardLandQuota(int cell) => Contains(cell) && (_quotaBands is null ||
        _quotaBands.KindAt(HexGrid.Center(HexGrid.FromOffset(cell % Width, cell / Width))) == PlateKind.Normal);
    public Vector2 Normalize(Vector2 p) => new((p.X / HexGrid.Root3 + 0.5f) / Width * 2 - 1,
        (p.Y / 1.5f + 0.5f) / Height * 2 - 1);
    public bool ContainsPoint(Vector2 p)
    {
        if (!IsAtlas) return true;
        Vector2 n = Normalize(p);
        return Math.Abs(n.Y) <= 1 && n.X * n.X + 0.64f * n.Y * n.Y <= 1.000001f;
    }
    public Vector2 ClampPoint(Vector2 p)
    {
        if (!IsAtlas) return p;
        float y = Math.Clamp(p.Y, 0, (Height - 1) * 1.5f);
        float ny = (y / 1.5f + 0.5f) / Height * 2 - 1;
        float span = MathF.Sqrt(Math.Max(0, 1 - 0.64f * ny * ny)) * 0.999f;
        float left = ((1 - span) * Width * 0.5f - 0.5f) * HexGrid.Root3;
        float right = ((1 + span) * Width * 0.5f - 0.5f) * HexGrid.Root3;
        return new(Math.Clamp(p.X, Math.Max(0, left), Math.Min(Width * HexGrid.Root3, right)), y);
    }
    public bool Clip(ref Vector2 start, ref Vector2 end)
    {
        if (!IsAtlas) return true;
        Vector2 a = Normalize(start), d = Normalize(end) - a;
        float qa = d.X * d.X + 0.64f * d.Y * d.Y;
        if (qa < 1e-14f) return ContainsPoint(start);
        float qb = a.X * d.X + 0.64f * a.Y * d.Y;
        float qc = a.X * a.X + 0.64f * a.Y * a.Y - 1;
        float disc = qb * qb - qa * qc;
        if (disc < 0) return false;
        float low = Math.Max(0, (-qb - MathF.Sqrt(disc)) / qa);
        float high = Math.Min(1, (-qb + MathF.Sqrt(disc)) / qa);
        if (Math.Abs(d.Y) < 1e-9f) { if (Math.Abs(a.Y) > 1) return false; }
        else
        {
            float p = (-1 - a.Y) / d.Y, q = (1 - a.Y) / d.Y;
            low = Math.Max(low, Math.Min(p, q)); high = Math.Min(high, Math.Max(p, q));
        }
        if (low > high) return false;
        Vector2 original = start, segment = end - start;
        start = original + segment * low; end = original + segment * high;
        return true;
    }

    public static (int Width, int Height, int ActiveHexes) ExpandedSize(int referenceWidth, int referenceHeight)
    {
        int width = ((referenceWidth * 3 / 2 + 31) / 32) * 32;
        int height = (((int)Math.Ceiling(referenceHeight * 1.35) + 31) / 32) * 32;
        var settings = new GenerationSettings(TerrainAlgorithm.ContinentalSculpt, 3, 38, 50, GenerationStage.Terrain, MapExtent.World);
        var m = new WorldManifest(1, "Atlas", 0, 37, width, height, false, 5, "km", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, settings);
        return (width, height, new MapDomain(m).ActiveCount);
    }
}
