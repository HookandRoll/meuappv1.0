using System;
using System.Numerics;

namespace Mapmaker.Core;

public readonly record struct Hex(int Q, int R);

public static class HexGrid
{
    public const float Root3 = 1.73205080757f;
    private static readonly Hex[] Directions = { new(1, 0), new(0, 1), new(-1, 1), new(-1, 0), new(0, -1), new(1, -1) };

    public static int NeighborIndex(Hex hex, int direction, int width, int height, bool wrapHorizontal, int steps = 1)
    {
        Hex delta = Directions[direction];
        var (column, row) = ToOffset(new Hex(hex.Q + delta.Q * steps, hex.R + delta.R * steps));
        if ((uint)row >= (uint)height)
            return -1;
        if (wrapHorizontal)
            column = (column % width + width) % width;
        return (uint)column < (uint)width ? row * width + column : -1;
    }

    public static int Distance(Hex a, Hex b, int width, bool wrapHorizontal)
    {
        int direct = CubeDistance(a, b);
        return wrapHorizontal
            ? Math.Min(direct, Math.Min(CubeDistance(a, b with { Q = b.Q - width }), CubeDistance(a, b with { Q = b.Q + width })))
            : direct;
    }

    private static int CubeDistance(Hex a, Hex b)
    {
        int q = a.Q - b.Q;
        int r = a.R - b.R;
        return Math.Max(Math.Max(Math.Abs(q), Math.Abs(r)), Math.Abs(q + r));
    }

    public static int EdgeValue(ushort packed, int direction) => (packed >> (direction * 2)) & 3;

    public static void SetSharedEdge(ushort[] edges, int index, int direction, int value,
        int width, int height, bool wrapHorizontal)
    {
        if ((uint)direction >= 6 || (uint)value >= 4)
            throw new ArgumentOutOfRangeException((uint)direction >= 6 ? nameof(direction) : nameof(value));
        int neighbor = NeighborIndex(FromOffset(index % width, index / width), direction, width, height, wrapHorizontal);
        edges[index] = WriteEdge(edges[index], direction, value);
        if (neighbor >= 0)
            edges[neighbor] = WriteEdge(edges[neighbor], (direction + 3) % 6, value);
    }

    private static ushort WriteEdge(ushort packed, int direction, int value)
    {
        int shift = direction * 2;
        return (ushort)((packed & ~(3 << shift)) | (value << shift));
    }

    public static Hex FromOffset(int column, int row) =>
        new(column - (row - (row & 1)) / 2, row);

    public static (int Column, int Row) ToOffset(Hex hex) =>
        (hex.Q + (hex.R - (hex.R & 1)) / 2, hex.R);

    public static Vector2 Center(Hex hex) =>
        new(Root3 * (hex.Q + hex.R * 0.5f), 1.5f * hex.R);

    public static Hex Pick(Vector2 position)
    {
        float r = position.Y * (2f / 3f);
        float q = position.X / Root3 - r * 0.5f;
        float s = -q - r;
        int roundedQ = (int)MathF.Floor(q + 0.5f);
        int roundedR = (int)MathF.Floor(r + 0.5f);
        int roundedS = (int)MathF.Floor(s + 0.5f);
        float errorQ = MathF.Abs(roundedQ - q);
        float errorR = MathF.Abs(roundedR - r);
        float errorS = MathF.Abs(roundedS - s);

        if (errorQ > errorR && errorQ > errorS)
            roundedQ = -roundedR - roundedS;
        else if (errorR > errorS)
            roundedR = -roundedQ - roundedS;

        return new Hex(roundedQ, roundedR);
    }
}
