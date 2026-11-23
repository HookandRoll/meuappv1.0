using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mapmaker.Core;

public readonly record struct HadleyWindSettings(
    int MaxGridWidth = 128, int MaxGridHeight = 64, int Iterations = 80, float TimeStep = 0.25f,
    float PressureForce = 0.62f, float CoriolisStrength = 0.95f, float ThermalContrast = 0.22f,
    float OrographicDeflection = 1.35f, float OrographicPressure = 0.35f,
    float VorticityConfinement = 0.16f, float PressureCoupling = 0.28f,
    float OceanDrag = 0.045f, float LandDrag = 0.085f, float MountainDrag = 0.22f, float Diffusion = 0.04f,
    float VerticalExchange = 0.8f, float ConvectiveLift = 0.12f, float UpperDrag = 0.02f, float LayerMixing = 0.04f,
    bool UseZonalPressureBands = true)
{
    // The implicit parameterless struct constructor would zero all coefficients.
    public HadleyWindSettings() : this(MaxGridWidth: 128) { }
}

public enum HadleyAtmosphereLayer { Surface, Upper }

public readonly record struct HadleyWindSample(
    float Vx, float Vy, float Speed, float Pressure, float VerticalFlow, float Vorticity);

public sealed class HadleyWindField
{
    public int GridWidth { get; }
    public int GridHeight { get; }
    public int WorldWidth { get; }
    public int WorldHeight { get; }
    public bool WrapHorizontal { get; }
    public HadleyWindSettings Settings { get; }
    public float[] Vx { get; }
    public float[] Vy { get; }
    public float[] UpperVx { get; }
    public float[] UpperVy { get; }
    public float[] Pressure { get; }
    public float[] UpperPressure { get; }
    public float[] LowerMass { get; }
    public float[] UpperMass { get; }
    // Positive means mass moves from the surface layer to the upper layer.
    public float[] VerticalFlow { get; }
    public float[] Vorticity { get; }
    public float[] UpperVorticity { get; }
    private readonly MapDomain _domain;

    internal HadleyWindField(WorldData world, int width, int height, bool wrap, HadleyWindSettings settings,
        float[] vx, float[] vy, float[] upperVx, float[] upperVy, float[] pressure, float[] upperPressure,
        float[] lowerMass, float[] upperMass, float[] verticalFlow, float[] vorticity, float[] upperVorticity)
    {
        GridWidth = width; GridHeight = height; WorldWidth = world.Width; WorldHeight = world.Height;
        _domain = world.Domain; WrapHorizontal = wrap; Settings = settings;
        Vx = vx; Vy = vy; UpperVx = upperVx; UpperVy = upperVy;
        Pressure = pressure; UpperPressure = upperPressure; LowerMass = lowerMass; UpperMass = upperMass;
        VerticalFlow = verticalFlow; Vorticity = vorticity; UpperVorticity = upperVorticity;
    }

    public HadleyWindSample Sample(int col, int row, HadleyAtmosphereLayer layer = HadleyAtmosphereLayer.Surface)
    {
        if (!_domain.Contains(col, row)) return default;
        var (left, right) = _domain.IsAtlas ? _domain.RowBounds(row) : (0, WorldWidth);
        float x = (col - left + 0.5f) / (right - left) * GridWidth - 0.5f;
        float y = (row + 0.5f) / WorldHeight * GridHeight - 0.5f;
        bool upper = layer == HadleyAtmosphereLayer.Upper;
        float Read(float[] values) => HadleyWindSimulation.SampleGrid(values, GridWidth, GridHeight, WrapHorizontal, x, y);
        float vx = Read(upper ? UpperVx : Vx), vy = Read(upper ? UpperVy : Vy);
        return new(vx, vy, MathF.Sqrt(vx * vx + vy * vy), Read(upper ? UpperPressure : Pressure),
            Read(VerticalFlow), Read(upper ? UpperVorticity : Vorticity));
    }
}

public static class HadleyWindSimulation
{
    internal interface ICoupledAtmosphere
    {
        void BeforeTransport(HadleyWindField field, float dt);
        void Exchange(int cell, float amount, float lowerMass, float upperMass);
        void CompleteStep(HadleyWindField field, float[] forcing, float dt);
    }
    private const float MaxSpeed = 2.6f;
    private const float MinimumMass = 0.05f;

    private sealed class Layer
    {
        internal float[] X, Y, Mass, NextX, NextY, NextMass;
        internal readonly float[] Pressure, Curl, FluxX, FluxY;
        internal Layer(int count)
        {
            X = new float[count]; Y = new float[count]; Mass = new float[count]; Array.Fill(Mass, 1);
            NextX = new float[count]; NextY = new float[count]; NextMass = new float[count];
            Pressure = new float[count]; Curl = new float[count]; FluxX = new float[count]; FluxY = new float[count];
        }
    }

    public static HadleyWindField Simulate(WorldData world, HadleyWindSettings? settingsOverride = null,
        CancellationToken cancellationToken = default)
        => Run(world, settingsOverride, cancellationToken, null);

    internal static HadleyWindField Run(WorldData world, HadleyWindSettings? settingsOverride,
        CancellationToken cancellationToken, ICoupledAtmosphere? atmosphere)
    {
        var s = settingsOverride ?? (world.Width >= 2048
            ? new HadleyWindSettings(MaxGridWidth: 512, MaxGridHeight: 256)
            : world.Width >= 512 ? new HadleyWindSettings(MaxGridWidth: 256, MaxGridHeight: 128) : new HadleyWindSettings());
        Validate(s);
        cancellationToken.ThrowIfCancellationRequested();
        int width = Math.Min(world.Width, s.MaxGridWidth), height = Math.Min(world.Height, s.MaxGridHeight);
        int count = width * height;
        bool wrap = world.Manifest.HorizontalWrap || AnnualWind.WrapsAtlas(world);
        var terrain = new float[count]; var land = new float[count]; var forcing = new float[count];
        var slopeX = new float[count]; var slopeY = new float[count];
        SampleTerrain(world, width, height, terrain, land, forcing, s.ThermalContrast, s.UseZonalPressureBands, cancellationToken);
        float ix = width / 128f, iy = height / 64f;
        Gradient(terrain, slopeX, slopeY, width, height, wrap, ix, iy);
        var lower = new Layer(count); var upper = new Layer(count);
        var vertical = new float[count];
        // Preserve simulated duration across grids while respecting the finite-volume CFL bound.
        int substeps = Math.Max(1, (int)MathF.Ceiling(s.TimeStep * MaxSpeed * (ix + iy) / 0.8f));
        float dt = s.TimeStep / substeps;
        for (int iteration = 0; iteration < s.Iterations; iteration++)
        for (int step = 0; step < substeps; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UpdatePressure(lower, upper, forcing, s.PressureCoupling);
            Accelerate(lower, false); Accelerate(upper, true);
            atmosphere?.BeforeTransport(Field(), dt);
            Transport(lower, width, height, wrap, ix, iy, dt, s.Diffusion);
            Transport(upper, width, height, wrap, ix, iy, dt, s.Diffusion);
            for (int i = 0; i < count; i++)
            {
                float rate = s.VerticalExchange * (lower.Mass[i] - upper.Mass[i]) - s.ConvectiveLift * forcing[i];
                float exchange = Math.Clamp(rate * dt, -Math.Max(0, upper.Mass[i] - MinimumMass) * 0.25f,
                    Math.Max(0, lower.Mass[i] - MinimumMass) * 0.25f);
                atmosphere?.Exchange(i, exchange, lower.Mass[i], upper.Mass[i]);
                Exchange(lower, upper, i, exchange);
                vertical[i] = exchange / dt;
                float mixing = s.LayerMixing * dt;
                float mx = (lower.X[i] - upper.X[i]) * Math.Min(lower.Mass[i], upper.Mass[i]) * mixing;
                float my = (lower.Y[i] - upper.Y[i]) * Math.Min(lower.Mass[i], upper.Mass[i]) * mixing;
                lower.X[i] -= mx / lower.Mass[i]; upper.X[i] += mx / upper.Mass[i];
                lower.Y[i] -= my / lower.Mass[i]; upper.Y[i] += my / upper.Mass[i];
            }
            atmosphere?.CompleteStep(Field(), forcing, dt);
        }
        UpdatePressure(lower, upper, forcing, s.PressureCoupling);
        Curl(lower, width, height, wrap, ix, iy); Curl(upper, width, height, wrap, ix, iy);
        return Field();

        HadleyWindField Field() => new(world, width, height, wrap, s, lower.X, lower.Y, upper.X, upper.Y,
            lower.Pressure, upper.Pressure, lower.Mass, upper.Mass, vertical, lower.Curl, upper.Curl);

        void Accelerate(Layer layer, bool aloft)
        {
            Curl(layer, width, height, wrap, ix, iy);
            Rows(width, height, y =>
            {
                float latitude = (y + 0.5f) / height * 2 - 1;
                float angle = -s.CoriolisStrength * MathF.Sin(latitude * MathF.PI * 0.5f) * dt;
                float c = MathF.Cos(angle), sn = MathF.Sin(angle);
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x, left = y * width + WrapX(x - 1, width, wrap), right = y * width + WrapX(x + 1, width, wrap);
                    int up = Math.Max(0, y - 1) * width + x, down = Math.Min(height - 1, y + 1) * width + x;
                    float u = layer.X[i] - (layer.Pressure[right] - layer.Pressure[left]) * 0.5f * ix * s.PressureForce * dt;
                    float v = layer.Y[i] - (layer.Pressure[down] - layer.Pressure[up]) * 0.5f * iy * s.PressureForce * dt;
                    float rx = u * c - v * sn, ry = u * sn + v * c;
                    if (!aloft)
                    {
                        float gx = slopeX[i], gy = slopeY[i], norm = gx * gx + gy * gy;
                        float uphill = rx * gx + ry * gy;
                        if (uphill > 0 && norm > 1e-8f)
                        {
                            float block = 1 - MathF.Exp(-s.OrographicDeflection * terrain[i] * dt);
                            rx -= gx * uphill / norm * block; ry -= gy * uphill / norm * block;
                            rx -= gx * s.OrographicPressure * terrain[i] * dt;
                            ry -= gy * s.OrographicPressure * terrain[i] * dt;
                        }
                    }
                    float drag = aloft ? s.UpperDrag : s.OceanDrag + s.LandDrag * land[i] + s.MountainDrag * terrain[i] * terrain[i];
                    float keep = MathF.Exp(-drag * dt);
                    rx *= keep; ry *= keep;
                    float nx = (Math.Abs(layer.Curl[right]) - Math.Abs(layer.Curl[left])) * 0.5f * ix;
                    float ny = (Math.Abs(layer.Curl[down]) - Math.Abs(layer.Curl[up])) * 0.5f * iy;
                    float length = MathF.Sqrt(nx * nx + ny * ny) + 1e-5f;
                    rx += ny / length * layer.Curl[i] * s.VorticityConfinement * dt;
                    ry -= nx / length * layer.Curl[i] * s.VorticityConfinement * dt;
                    Limit(ref rx, ref ry);
                    layer.NextX[i] = rx; layer.NextY[i] = ry;
                }
            });
            (layer.X, layer.NextX) = (layer.NextX, layer.X); (layer.Y, layer.NextY) = (layer.NextY, layer.Y);
        }
    }

    private static void UpdatePressure(Layer lower, Layer upper, float[] forcing, float coupling)
    {
        for (int i = 0; i < forcing.Length; i++)
        {
            lower.Pressure[i] = forcing[i] + coupling * (lower.Mass[i] - 1);
            upper.Pressure[i] = -forcing[i] + coupling * (upper.Mass[i] - 1);
        }
    }

    private static void Exchange(Layer lower, Layer upper, int i, float amount)
    {
        Layer donor = amount >= 0 ? lower : upper, receiver = amount >= 0 ? upper : lower;
        float mass = Math.Abs(amount), total = receiver.Mass[i] + mass;
        receiver.X[i] = (receiver.X[i] * receiver.Mass[i] + donor.X[i] * mass) / total;
        receiver.Y[i] = (receiver.Y[i] * receiver.Mass[i] + donor.Y[i] * mass) / total;
        donor.Mass[i] -= mass; receiver.Mass[i] = total;
    }

    private static void Transport(Layer layer, int width, int height, bool wrap, float ix, float iy, float dt, float diffusion)
    {
        Rows(width, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x, right = y * width + WrapX(x + 1, width, wrap), down = Math.Min(height - 1, y + 1) * width + x;
                float u = (layer.X[i] + layer.X[right]) * 0.5f, v = (layer.Y[i] + layer.Y[down]) * 0.5f;
                layer.FluxX[i] = !wrap && x == width - 1 ? 0 : u * dt * ix * layer.Mass[u >= 0 ? i : right];
                layer.FluxY[i] = y == height - 1 ? 0 : v * dt * iy * layer.Mass[v >= 0 ? i : down];
            }
        });
        Rows(width, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x, left = y * width + WrapX(x - 1, width, wrap), right = y * width + WrapX(x + 1, width, wrap);
                int up = Math.Max(0, y - 1) * width + x, down = Math.Min(height - 1, y + 1) * width + x;
                float fx = layer.FluxX[i], fy = layer.FluxY[i], fl = !wrap && x == 0 ? 0 : layer.FluxX[left], fu = y == 0 ? 0 : layer.FluxY[up];
                float mass = layer.Mass[i] - fx + fl - fy + fu;
                int a = fx >= 0 ? i : right, b = fl >= 0 ? left : i, c = fy >= 0 ? i : down, d = fu >= 0 ? up : i;
                float u = (layer.Mass[i] * layer.X[i] - fx * layer.X[a] + fl * layer.X[b] - fy * layer.X[c] + fu * layer.X[d]) / mass;
                float v = (layer.Mass[i] * layer.Y[i] - fx * layer.Y[a] + fl * layer.Y[b] - fy * layer.Y[c] + fu * layer.Y[d]) / mass;
                u += diffusion * dt * ((layer.X[left] - 2 * layer.X[i] + layer.X[right]) * ix * ix + (layer.X[up] - 2 * layer.X[i] + layer.X[down]) * iy * iy);
                v += diffusion * dt * ((layer.Y[left] - 2 * layer.Y[i] + layer.Y[right]) * ix * ix + (layer.Y[up] - 2 * layer.Y[i] + layer.Y[down]) * iy * iy);
                Limit(ref u, ref v);
                layer.NextMass[i] = mass; layer.NextX[i] = u; layer.NextY[i] = v;
            }
        });
        (layer.X, layer.NextX) = (layer.NextX, layer.X); (layer.Y, layer.NextY) = (layer.NextY, layer.Y);
        (layer.Mass, layer.NextMass) = (layer.NextMass, layer.Mass);
    }

    private static void Limit(ref float x, ref float y)
    {
        float speed = MathF.Sqrt(x * x + y * y);
        if (speed > MaxSpeed) { x *= MaxSpeed / speed; y *= MaxSpeed / speed; }
    }

    private static void SampleTerrain(WorldData world, int width, int height, float[] terrain, float[] land,
        float[] forcing, float thermalContrast, bool zonalBands, CancellationToken token)
    {
        for (int y = 0; y < height; y++)
        {
            token.ThrowIfCancellationRequested();
            float latitude = Math.Abs((y + 0.5f) / height * 2 - 1);
            for (int x = 0; x < width; x++)
            {
                float sum = 0, peak = 0, ground = 0, temperature = 0; int samples = 0;
                for (int row = y * world.Height / height; row < (y + 1) * world.Height / height; row++)
                {
                    var (left, right) = world.Domain.IsAtlas ? world.Domain.RowBounds(row) : (0, world.Width);
                    int start = left + x * (right - left) / width;
                    int end = Math.Min(right, Math.Max(start + 1, left + (x + 1) * (right - left) / width));
                    for (int col = start; col < end; col++)
                    {
                        int cell = row * world.Width + col;
                        bool dry = world.Water[cell] == WaterType.None;
                        float h = dry ? Math.Clamp(world.Elevation[cell] / 3200f, 0, 1.5f) : 0;
                        sum += h; peak = Math.Max(peak, h); ground += dry ? 1 : 0;
                        temperature += world.Temperature[cell] * 0.1f; samples++;
                    }
                }
                int i = y * width + x;
                float n = Math.Max(1, samples);
                terrain[i] = sum / n * 0.65f + peak * 0.35f; land[i] = ground / n;
                float anomaly = world.Manifest.Generation.Stage == GenerationStage.Terrain && samples > 0
                    ? Math.Clamp((temperature / n - (26 - 48 * latitude * latitude)) / 25, -1, 1) * land[i] : 0;
                // The experimental alternative has no imposed subtropical high or subpolar low.
                forcing[i] = -MathF.Cos((zonalBands ? 3 : 1) * MathF.PI * latitude) - anomaly * thermalContrast;
            }
        }
    }

    private static void Gradient(float[] values, float[] gx, float[] gy, int width, int height, bool wrap, float ix, float iy)
    {
        Rows(width, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                gx[i] = (values[y * width + WrapX(x + 1, width, wrap)] - values[y * width + WrapX(x - 1, width, wrap)]) * 0.5f * ix;
                gy[i] = (values[Math.Min(height - 1, y + 1) * width + x] - values[Math.Max(0, y - 1) * width + x]) * 0.5f * iy;
            }
        });
    }

    private static void Curl(Layer layer, int width, int height, bool wrap, float ix, float iy)
    {
        Rows(width, height, y =>
        {
            for (int x = 0; x < width; x++)
                layer.Curl[y * width + x] = (layer.Y[y * width + WrapX(x + 1, width, wrap)] - layer.Y[y * width + WrapX(x - 1, width, wrap)]) * 0.5f * ix
                    - (layer.X[Math.Min(height - 1, y + 1) * width + x] - layer.X[Math.Max(0, y - 1) * width + x]) * 0.5f * iy;
        });
    }

    private static void Rows(int width, int height, Action<int> visit)
    {
        if (width * height >= 32768) Parallel.For(0, height, visit);
        else for (int row = 0; row < height; row++) visit(row);
    }

    private static void Validate(HadleyWindSettings s)
    {
        if (s.MaxGridWidth is < 32 or > 512 || s.MaxGridHeight is < 16 or > 256 || s.Iterations is < 1 or > 2000
            || !float.IsFinite(s.TimeStep) || s.TimeStep is <= 0 or > 0.5f)
            throw new ArgumentOutOfRangeException(nameof(s), "Grade, iterações ou passo de tempo inválidos.");
        foreach (float value in new[] { s.PressureForce, s.CoriolisStrength, s.ThermalContrast, s.OrographicDeflection,
            s.OrographicPressure, s.VorticityConfinement, s.PressureCoupling, s.OceanDrag, s.LandDrag, s.MountainDrag,
            s.Diffusion, s.VerticalExchange, s.ConvectiveLift, s.UpperDrag, s.LayerMixing })
            if (!float.IsFinite(value) || value is < 0 or > 4)
                throw new ArgumentOutOfRangeException(nameof(s), "Coeficiente atmosférico inválido.");
        if (s.Diffusion > 0.1f) throw new ArgumentOutOfRangeException(nameof(s), "Difusão acima do limite estável.");
    }

    internal static float SampleGrid(float[] values, int width, int height, bool wrap, float x, float y)
    {
        y = Math.Clamp(y, 0, height - 1);
        x = wrap ? (x % width + width) % width : Math.Clamp(x, 0, width - 1);
        int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
        int x1 = wrap ? (x0 + 1) % width : Math.Min(width - 1, x0 + 1), y1 = Math.Min(height - 1, y0 + 1);
        float tx = x - x0, ty = y - y0;
        float a = values[y0 * width + x0] * (1 - tx) + values[y0 * width + x1] * tx;
        float b = values[y1 * width + x0] * (1 - tx) + values[y1 * width + x1] * tx;
        return a * (1 - ty) + b * ty;
    }

    private static int WrapX(int x, int width, bool wrap) => wrap ? (x % width + width) % width : Math.Clamp(x, 0, width - 1);
}
