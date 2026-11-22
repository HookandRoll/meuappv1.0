using System;

namespace Mapmaker.Core;

internal static class AnnualWind
{
    internal const float AlongLatitude = 0.72f;
    internal const float AcrossLatitude = 0.28f;
    internal const float OceanCondensationShare = 0.5f;

    private static int BaseLateralReach(int width, int height) =>
        Math.Clamp((Math.Max(width, height * 2) + 511) / 512, 1, 12);

    private static float SpreadScale(int baseReach, int version) =>
        version >= 62 && baseReach > 1 ? 1.30f : 1f;

    internal static int LateralReach(int width, int height, int version)
    {
        int baseReach = BaseLateralReach(width, height);
        return version >= 61 ? (int)MathF.Round(2 * baseReach * SpreadScale(baseReach, version)) : baseReach;
    }

    internal static float WindwardLocalShare(int width, int height, int version)
    {
        int baseReach = BaseLateralReach(width, height);
        float share = version >= 59 ? 0.20f / MathF.Sqrt(baseReach) : 0.20f;
        if (version < 61) return share;
        // Scale the expected travel distance along a continuous descent.
        float distanceScale = 2 * SpreadScale(baseReach, version);
        return share / (distanceScale - (distanceScale - 1) * share);
    }

    internal static void SpreadAcrossRow(float[] vapor, double[] sum,
        WaterType[] water, short[] elevation, int rowStart, int left, int right, int reach, bool wrap)
    {
        bool Low(int col) => elevation[rowStart + col] <= 1800 || water[rowStart + col] != WaterType.None;
        int length = right - left;
        int origin = left;
        if (wrap)
        {
            while (origin < right && Low(origin))
                origin++;
            if (origin == right)
            {
                Mix(left, right, true);
                return;
            }
        }

        int end = origin + length;
        for (int start = origin; start < end;)
        {
            int col = Column(start);
            if (!Low(col))
            {
                start++;
                continue;
            }
            int stop = start + 1;
            while (stop < end && Low(Column(stop)))
                stop++;
            Mix(start, stop, false);
            start = stop;
        }

        int Column(int index) => index < right ? index : index - length;

        void Mix(int start, int stop, bool circular)
        {
            int count = stop - start;
            int radius = circular ? Math.Min(reach, (count - 1) / 2) : reach;
            int size = radius * 2 + 1;
            sum[0] = 0;
            for (int i = 0; i < count; i++)
                sum[i + 1] = sum[i] + vapor[Column(start + i)];

            for (int i = 0; i < count; i++)
            {
                int first = i - radius, last = i + radius + 1;
                int low = Math.Max(0, first), high = Math.Min(count, last);
                int col = Column(start + i);
                double total = sum[high] - sum[low];
                // A blocked exchange stays at its source, so spreading preserves total vapor.
                if (!circular) total += (size - (high - low)) * vapor[col];
                else
                {
                    if (first < 0) total += sum[count] - sum[count + first];
                    if (last > count) total += sum[last - count];
                }
                vapor[col] = (float)(total / size);
            }
        }
    }

    internal static (float Eastward, float Southward) DirectionWeights(int row, int height)
    {
        float signedLatitude = (row + 0.5f) / height * 2 - 1;
        float latitude = Math.Abs(signedLatitude);
        static float Smooth01(float value)
        {
            float t = Math.Clamp(value, 0, 1);
            return t * t * (3 - 2 * t);
        }
        float east = Smooth01((latitude - 0.25f) / 0.12f) * (1 - Smooth01((latitude - 0.65f) / 0.14f));
        float south = 0.5f - Math.Clamp(signedLatitude * 12f, -1, 1) * (0.40f - 0.8f * east);
        return (east, south);
    }

    internal static bool WrapsAtlas(WorldData world) => world.Domain.IsAtlas && world.Manifest.GeneratorVersion >= 58;

    internal static int ProjectColumn(int col, int left, int length, int targetLeft, int targetLength)
    {
        // Use the same longitude when the curved atlas changes width on the next row.
        long offset = (2L * (col - left) + 1) * targetLength / (2L * length);
        return targetLeft + (int)offset;
    }
}
