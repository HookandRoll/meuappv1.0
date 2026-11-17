using System;
using System.Numerics;

namespace Mapmaker.Core;

public readonly record struct Hex(int Q, int R);

public static class HexGrid
{
    public const float Root3 = 1.73205080757f;

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
