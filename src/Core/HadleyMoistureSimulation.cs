using System;
using System.Linq;
using System.Threading.Tasks;

namespace Mapmaker.Core;

internal sealed record HadleyMoistureSettings(int WarmupSteps = 240, int SampleSteps = 240,
    float TimeStep = 0.25f, float EvaporationRelaxation = 0.18f, float CondensationRate = 1.5f);

internal sealed record HadleyMoistureResult(float[] AnnualRain, double InitialWater, double EvaporatedWater,
    double PrecipitatedWater, double FinalWater, double RelativeBudgetError, HadleyMoistureSettings Settings,
    HadleyCoupledDiagnostics? Coupled = null)
{
    internal double WaterOutput => Coupled?.Runoff ?? PrecipitatedWater;
}

// Experimental water transport on a fixed, two-layer wind snapshot. No latitude rainfall masks.
internal static class HadleyMoistureSimulation
{
    internal static HadleyMoistureResult Simulate(WorldData world, HadleyWindField wind,
        HadleyMoistureSettings? settings = null, Action<int>? progress = null)
    {
        var s = settings ?? new HadleyMoistureSettings();
        if (s.WarmupSteps < 0 || s.SampleSteps < 1 || s.WarmupSteps + s.SampleSteps > 4000
            || !float.IsFinite(s.TimeStep) || s.TimeStep is <= 0 or > 0.5f
            || !float.IsFinite(s.EvaporationRelaxation) || s.EvaporationRelaxation is < 0 or > 1
            || !float.IsFinite(s.CondensationRate) || s.CondensationRate is <= 0 or > 4)
            throw new ArgumentOutOfRangeException(nameof(settings));
        int width = wind.GridWidth, height = wind.GridHeight, count = width * height;
        float ix = width / 128f, iy = height / 64f;
        var capLower = new float[count]; var capUpper = new float[count];
        var water = new float[count]; var elevation = new float[count];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            double temp = 0, wet = 0, ground = 0; int samples = 0;
            for (int row = y * world.Height / height; row < (y + 1) * world.Height / height; row++)
            {
                var (left, right) = world.Domain.IsAtlas ? world.Domain.RowBounds(row) : (0, world.Width);
                int start = left + x * (right - left) / width;
                int end = Math.Min(right, Math.Max(start + 1, left + (x + 1) * (right - left) / width));
                for (int col = start; col < end; col++)
                {
                    int cell = row * world.Width + col;
                    temp += world.Temperature[cell] * 0.1;
                    wet += world.Water[cell] switch { WaterType.Ocean => 1, WaterType.Lake => 0.6, WaterType.Wetland => 0.4, _ => 0 };
                    ground += world.Water[cell] == WaterType.None ? Math.Max(0, (int)world.Elevation[cell]) / 1000.0 : 0;
                    samples++;
                }
            }
            int i = y * width + x;
            double divisor = Math.Max(1, samples);
            float temperature = (float)(temp / divisor);
            capLower[i] = Capacity(temperature) * wind.LowerMass[i];
            capUpper[i] = Capacity(temperature - 35) * wind.UpperMass[i];
            water[i] = (float)(wet / divisor); elevation[i] = (float)(ground / divisor);
        }
        var lower = new float[count]; var upper = new float[count];
        var nextLower = new float[count]; var nextUpper = new float[count];
        var fluxX = new float[count]; var fluxY = new float[count];
        var evaporation = new float[count]; var condensation = new float[count]; var measuredRain = new float[count];
        var lift = new float[count];
        double initial = 0; float maximumX = 0, maximumY = 0;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int i = y * width + x;
            lower[i] = capLower[i] * 0.55f; upper[i] = capUpper[i] * 0.55f;
            initial += lower[i] + (double)upper[i];
            maximumX = Math.Max(maximumX, Math.Max(Math.Abs(wind.Vx[i]), Math.Abs(wind.UpperVx[i])));
            maximumY = Math.Max(maximumY, Math.Max(Math.Abs(wind.Vy[i]), Math.Abs(wind.UpperVy[i])));
            float gx = (elevation[y * width + X(x + 1)] - elevation[y * width + X(x - 1)]) * 0.5f * ix;
            float gy = (elevation[Math.Min(height - 1, y + 1) * width + x] - elevation[Math.Max(0, y - 1) * width + x]) * 0.5f * iy;
            lift[i] = Math.Max(0, wind.Vx[i] * gx + wind.Vy[i] * gy) * 0.03f;
        }
        int substeps = Math.Max(1, (int)MathF.Ceiling(s.TimeStep * (maximumX * ix + maximumY * iy) / 0.8f));
        float dt = s.TimeStep / substeps;
        for (int step = 0; step < s.WarmupSteps + s.SampleSteps; step++)
        {
            for (int substep = 0; substep < substeps; substep++)
            {
                Advect(lower, nextLower, wind.Vx, wind.Vy);
                Advect(upper, nextUpper, wind.UpperVx, wind.UpperVy);
                (lower, nextLower) = (nextLower, lower); (upper, nextUpper) = (nextUpper, upper);
                Rows(y =>
                {
                    for (int i = y * width; i < (y + 1) * width; i++)
                    {
                        float evaporated = water[i] * s.EvaporationRelaxation * dt * Math.Max(0, capLower[i] * 0.8f - lower[i]);
                        lower[i] += evaporated; evaporation[i] += evaporated;
                        float w = wind.VerticalFlow[i];
                        if (w >= 0)
                        {
                            float moved = lower[i] * Math.Clamp(w / wind.LowerMass[i] * dt, 0, 0.25f);
                            lower[i] -= moved; upper[i] += moved;
                        }
                        else
                        {
                            float moved = upper[i] * Math.Clamp(-w / wind.UpperMass[i] * dt, 0, 0.25f);
                            upper[i] -= moved; lower[i] += moved;
                        }
                        float orographic = lower[i] * Math.Clamp(lift[i] * dt, 0, 0.25f);
                        lower[i] -= orographic; upper[i] += orographic;
                        float rate = Math.Min(1, s.CondensationRate * dt);
                        float rainLower = Math.Max(0, lower[i] - capLower[i]) * rate;
                        float rainUpper = Math.Max(0, upper[i] - capUpper[i]) * rate;
                        lower[i] -= rainLower; upper[i] -= rainUpper;
                        float rain = rainLower + rainUpper;
                        condensation[i] += rain;
                        if (step >= s.WarmupSteps) measuredRain[i] += rain;
                    }
                });
            }
            if (step % 20 == 0) progress?.Invoke(step * 100 / (s.WarmupSteps + s.SampleSteps));
        }
        double evaporatedTotal = evaporation.Sum(value => (double)value);
        double condensedTotal = condensation.Sum(value => (double)value);
        double final = lower.Sum(value => (double)value) + upper.Sum(value => (double)value);
        double budgetError = Math.Abs(initial + evaporatedTotal - condensedTotal - final) / Math.Max(1, initial + evaporatedTotal);
        if (budgetError > 1e-5 || lower.Any(value => !float.IsFinite(value) || value < 0)
            || upper.Any(value => !float.IsFinite(value) || value < 0))
            throw new InvalidOperationException("Falha no balanço de água do experimento Hadley.");
        float annualScale = 365f / (s.SampleSteps * s.TimeStep);
        for (int i = 0; i < count; i++) measuredRain[i] *= annualScale;
        progress?.Invoke(100);
        return new(measuredRain, initial, evaporatedTotal, condensedTotal, final, budgetError, s);

        int X(int x) => wind.WrapHorizontal ? (x % width + width) % width : Math.Clamp(x, 0, width - 1);
        void Rows(Action<int> action)
        {
            if (count >= 32768) Parallel.For(0, height, action);
            else for (int row = 0; row < height; row++) action(row);
        }
        void Advect(float[] q, float[] result, float[] vx, float[] vy)
        {
            Rows(y =>
            {
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x, right = y * width + X(x + 1), down = Math.Min(height - 1, y + 1) * width + x;
                    float u = (vx[i] + vx[right]) * 0.5f, v = (vy[i] + vy[down]) * 0.5f;
                    fluxX[i] = !wind.WrapHorizontal && x == width - 1 ? 0 : u * dt * ix * q[u >= 0 ? i : right];
                    fluxY[i] = y == height - 1 ? 0 : v * dt * iy * q[v >= 0 ? i : down];
                }
            });
            Rows(y =>
            {
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    float left = !wind.WrapHorizontal && x == 0 ? 0 : fluxX[y * width + X(x - 1)];
                    float up = y == 0 ? 0 : fluxY[(y - 1) * width + x];
                    result[i] = q[i] - fluxX[i] + left - fluxY[i] + up;
                }
            });
        }
    }

    private static float Capacity(float temperature) => 30 * MathF.Exp(0.06f * (Math.Clamp(temperature, -60, 45) - 25));
}
