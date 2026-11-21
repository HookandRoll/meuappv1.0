using System;
using System.Threading.Tasks;
using Mapmaker.Core;

namespace Mapmaker.GodotApp;

internal static class OceanShelf
{
    // RGBA: icon ID low/high, display flags, distance to land.
    public static byte[] BuildDetails(WorldData world, uint[] icons)
    {
        var data = new byte[world.Count * 4];
        for (int i = 0; i < world.Count; i++)
        {
            data[i * 4] = (byte)icons[i];
            data[i * 4 + 1] = (byte)(icons[i] >> 8);
            data[i * 4 + 2] = (byte)(icons[i] >> 16);
        }

        int width = world.Width;
        int height = world.Height;
        bool wrap = world.Manifest.HorizontalWrap;
        int reach = 2 * Math.Clamp((int)(height * 0.047f), 14, 90);
        int limit = reach * reach;
        var horizontal = new int[world.Count];

        // Measure in half-column units. The stagger adds one unit to X, and a row
        // has length sqrt(3) in the same units. Each parity needs its own X samples.
        for (int parity = 0; parity < 2; parity++)
        {
            int targetParity = parity;
            Parallel.For(0, height, row =>
            {
                int start = row * width;
                int offset = (row & 1) - targetParity;
                int first = width;
                int last = -1;
                if (wrap)
                {
                    for (int col = 0; col < width; col++)
                    {
                        if (world.Water[start + col] != WaterType.None)
                            continue;
                        first = Math.Min(first, col);
                        last = col;
                    }
                }

                int left = last < 0 ? -width : last - width;
                for (int col = 0; col < width; col++)
                {
                    int distance = 2 * (col - left) - offset;
                    if (world.Water[start + col] == WaterType.None)
                    {
                        left = col;
                        distance = Math.Abs(offset);
                    }
                    horizontal[start + col] = Math.Min(limit, distance * distance);
                }
                int right = first == width ? 2 * width : first + width;
                for (int col = width - 1; col >= 0; col--)
                {
                    if (world.Water[start + col] == WaterType.None)
                        right = col;
                    int distance = 2 * (right - col) + offset;
                    horizontal[start + col] = Math.Min(horizontal[start + col], distance * distance);
                }
            });
            Parallel.For(0, width,
                () => (Sites: new int[height], Crossings: new float[height]),
                (column, _, scratch) =>
                {
                    WriteColumn(horizontal, data, width, height, column, targetParity, reach, scratch.Sites, scratch.Crossings);
                    return scratch;
                }, _ => { });
        }
        return data;
    }

    private static void WriteColumn(int[] horizontal, byte[] data, int width, int height,
        int col, int parity, int reach, int[] sites, float[] crossings)
    {
        // Lower envelope of the squared-distance parabola from each source row.
        int last = 0;
        crossings[0] = float.NegativeInfinity;
        sites[0] = 0;
        for (int row = 1; row < height; row++)
        {
            float crossing;
            while (true)
            {
                int site = sites[last];
                crossing = (horizontal[row * width + col] - horizontal[site * width + col]
                    + 3f * (row * row - site * site)) / (6f * (row - site));
                if (crossing > crossings[last])
                    break;
                last--;
            }
            sites[++last] = row;
            crossings[last] = crossing;
        }

        int best = 0;
        for (int row = parity; row < height; row += 2)
        {
            while (best < last && crossings[best + 1] <= row)
                best++;
            int site = sites[best];
            int dy = row - site;
            float distance = MathF.Sqrt(horizontal[site * width + col] + 3 * dy * dy);
            data[(row * width + col) * 4 + 3] = (byte)Math.Min(255f, MathF.Round(distance * 255f / reach));
        }
    }
}
