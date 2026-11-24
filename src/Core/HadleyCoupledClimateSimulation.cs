using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mapmaker.Core;

internal sealed record HadleyHydrologyBand(string Label, int Cells, double MeanVapor, double MeanCloud, double MeanAnnualRain);
internal sealed record HadleyCoupledSettings(bool EvolveTemperature = true, bool RecycleSoil = true,
    float CloudRainTime = 3, double InitialRelativeHumidity = 0.55, double InitialSoilWater = 12,
    double OceanHumidityTarget = 0.90, double ShallowOceanHumidityTarget = 0.95,
    double LakeHumidityTarget = 0.85, double OtherHumidityTarget = 0.80,
    double AquaticReplenishmentMultiplier = 2, bool OrographicThermodynamics = true,
    double DryLapseRate = 9.8, double ThermalRelaxationMultiplier = 1,
    double VaporCapacityMultiplier = 1, double OceanRainShare = 1, double MoistureDiffusivity = 0,
    double RainReturnThresholdMm = 500, double RainReturnShare = 0);
internal sealed record HadleyCoupledDiagnostics(double OceanEvaporation, double SoilEvaporation,
    double Runoff, double Rainfall, double AtmosphericWater, double SoilWater,
    double CloudWater, double TemperatureChangeRms, float[] Vapor, float[] Clouds, float[] Soil,
    float[] LowerTemperature, float[] UpperTemperature, HadleyHydrologyBand[] LatitudeBands,
    HadleyHydrologyBand[] CoastDistanceBands, HadleyCoupledSettings Settings, int TemperatureLimitApplications,
    double ReturnedRain, float[] RainReturnRate, float[] AquaticEvaporationRate, float[] SoilEvaporationRate, float[] RelativeHumidity);

// Normalized two-layer experiment, not a calibrated atmospheric model.
internal sealed partial class HadleyCoupledClimateSimulation : HadleyWindSimulation.ICoupledAtmosphere
{
    private readonly int _width, _height, _count;
    private readonly bool _wrap;
    private readonly float _ix, _iy;
    private readonly HadleyMoistureSettings _settings;
    private readonly HadleyCoupledSettings _coupling;
    private readonly Action<int>? _progress;
    private readonly CancellationToken _token;
    private readonly float[] _target, _wet, _land, _slopeX, _slopeY;
    private readonly float[] _deepOcean, _shallowOcean, _lake, _wetland;
    private readonly double[] _lowerHeightTemperatureOffset;
    private readonly double[][] _q, _next;
    private readonly double[] _soil, _rain, _oceanEvap, _soilEvap, _runoff, _totalRain;
    private readonly double[] _fx, _fy;
    private readonly double[] _vaporSum, _cloudSum;
    private readonly double[] _relativeHumiditySum;
    private readonly double[] _returnedRain, _sampleRainReturn, _sampleOceanEvap, _sampleSoilEvap;
    private readonly double _rainOffsetMultiplier;
    private readonly int[] _temperatureLimits;
    private float[]? _baseForcing;
    private double _time;
    private readonly double _initial;

    private HadleyCoupledClimateSimulation(WorldData world, HadleyWindSettings windSettings,
        HadleyMoistureSettings settings, HadleyCoupledSettings coupling, Action<int>? progress, CancellationToken token)
    {
        _width = Math.Min(world.Width, windSettings.MaxGridWidth);
        _height = Math.Min(world.Height, windSettings.MaxGridHeight); _count = _width * _height;
        _wrap = world.Manifest.HorizontalWrap || AnnualWind.WrapsAtlas(world);
        _ix = _width / 128f; _iy = _height / 64f;
        _settings = settings; _coupling = coupling; _progress = progress; _token = token;
        _rainOffsetMultiplier = 1 + world.Manifest.Generation.PrecipitationOffsetPercent / 100.0;
        _target = new float[_count]; _wet = new float[_count]; _land = new float[_count];
        _deepOcean = new float[_count]; _shallowOcean = new float[_count];
        _lake = new float[_count]; _wetland = new float[_count];
        _lowerHeightTemperatureOffset = new double[_count];
        _slopeX = new float[_count]; _slopeY = new float[_count];
        var elevation = new float[_count];
        for (int y = 0; y < _height; y++)
        for (int x = 0; x < _width; x++)
        {
            double temp = 0, wet = 0, land = 0, ground = 0;
            int n = 0, deep = 0, shallow = 0, lakes = 0, wetlands = 0;
            for (int row = y * world.Height / _height; row < (y + 1) * world.Height / _height; row++)
            {
                var (left, right) = world.Domain.IsAtlas ? world.Domain.RowBounds(row) : (0, world.Width);
                int start = left + x * (right - left) / _width;
                int end = Math.Min(right, Math.Max(start + 1, left + (x + 1) * (right - left) / _width));
                for (int col = start; col < end; col++)
                {
                    int i = row * world.Width + col;
                    temp += world.Temperature[i] * 0.1;
                    wet += world.Water[i] switch { WaterType.Ocean => 1, WaterType.Lake => 0.6, WaterType.Wetland => 0.4, _ => 0 };
                    switch (world.Water[i])
                    {
                        case WaterType.Ocean:
                            if (world.Elevation[i] >= -200) shallow++; else deep++;
                            break;
                        case WaterType.Lake: lakes++; break;
                        case WaterType.Wetland: wetlands++; break;
                    }
                    if (world.Water[i] == WaterType.None) { land++; ground += Math.Max(0, (int)world.Elevation[i]) / 1000.0; }
                    n++;
                }
            }
            int cell = y * _width + x; double divisor = Math.Max(1, n);
            _target[cell] = (float)(temp / divisor); _wet[cell] = (float)(wet / divisor);
            _land[cell] = (float)(land / divisor); elevation[cell] = (float)(ground / divisor);
            _deepOcean[cell] = (float)(deep / divisor); _shallowOcean[cell] = (float)(shallow / divisor);
            _lake[cell] = (float)(lakes / divisor); _wetland[cell] = (float)(wetlands / divisor);
        }
        _q = Enumerable.Range(0, 6).Select(_ => new double[_count]).ToArray();
        _next = Enumerable.Range(0, 6).Select(_ => new double[_count]).ToArray();
        _soil = new double[_count]; _rain = new double[_count];
        _oceanEvap = new double[_count]; _soilEvap = new double[_count];
        _runoff = new double[_count]; _totalRain = new double[_count];
        _fx = new double[_count]; _fy = new double[_count];
        _vaporSum = new double[_count]; _cloudSum = new double[_count]; _temperatureLimits = new int[_count];
        _returnedRain = new double[_count]; _sampleRainReturn = new double[_count];
        _relativeHumiditySum = new double[_count];
        _sampleOceanEvap = new double[_count]; _sampleSoilEvap = new double[_count];
        for (int y = 0; y < _height; y++)
        for (int x = 0; x < _width; x++)
        {
            int i = y * _width + x;
            _q[0][i] = Capacity(_target[i]) * _coupling.InitialRelativeHumidity;
            _q[1][i] = Capacity(_target[i] - 25) * _coupling.InitialRelativeHumidity;
            _lowerHeightTemperatureOffset[i] = coupling.OrographicThermodynamics ? coupling.DryLapseRate * elevation[i] : 0;
            // Lower heat carries dry static temperature (T + g*z/cp), preserving the parcel's thermal history.
            _q[4][i] = _target[i] + 273.15 + _lowerHeightTemperatureOffset[i];
            _q[5][i] = _target[i] - 25 + 273.15;
            _soil[i] = (float)(_coupling.InitialSoilWater * _land[i]);
            _initial += _q[0][i] + _q[1][i] + _soil[i];
            _slopeX[i] = (elevation[y * _width + X(x + 1)] - elevation[y * _width + X(x - 1)]) * 0.5f * _ix;
            _slopeY[i] = (elevation[Math.Min(_height - 1, y + 1) * _width + x] - elevation[Math.Max(0, y - 1) * _width + x]) * 0.5f * _iy;
        }
    }

    internal static (HadleyWindField Wind, HadleyMoistureResult Moisture) Simulate(WorldData world,
        HadleyWindSettings windSettings, HadleyMoistureSettings? settings = null,
        Action<int>? progress = null, CancellationToken cancellationToken = default, HadleyCoupledSettings? coupling = null)
    {
        var s = settings ?? new HadleyMoistureSettings();
        if (s.WarmupSteps < 0 || s.SampleSteps < 1 || s.WarmupSteps + s.SampleSteps > 2000
            || !float.IsFinite(s.TimeStep) || s.TimeStep is <= 0 or > 0.5f
            || !float.IsFinite(s.EvaporationRelaxation) || s.EvaporationRelaxation is < 0 or > 1
            || !float.IsFinite(s.CondensationRate) || s.CondensationRate is <= 0 or > 4)
            throw new ArgumentOutOfRangeException(nameof(settings));
        cancellationToken.ThrowIfCancellationRequested();
        var c = coupling ?? CouplingFor(world.Manifest);
        if (!float.IsFinite(c.CloudRainTime) || c.CloudRainTime is < 0.1f or > 30
            || !double.IsFinite(c.InitialRelativeHumidity) || c.InitialRelativeHumidity is < 0 or > 1
            || !double.IsFinite(c.InitialSoilWater) || c.InitialSoilWater is < 0 or > 80)
            throw new ArgumentOutOfRangeException(nameof(coupling));
        foreach (double target in new[] { c.OceanHumidityTarget, c.ShallowOceanHumidityTarget,
            c.LakeHumidityTarget, c.OtherHumidityTarget })
            if (!double.IsFinite(target) || target is < 0 or > 1)
                throw new ArgumentOutOfRangeException(nameof(coupling));
        if (!double.IsFinite(c.AquaticReplenishmentMultiplier) || c.AquaticReplenishmentMultiplier is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(coupling));
        if (!double.IsFinite(c.DryLapseRate) || c.DryLapseRate is < 0 or > 15
            || !double.IsFinite(c.ThermalRelaxationMultiplier) || c.ThermalRelaxationMultiplier is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(coupling));
        if (!double.IsFinite(c.MoistureDiffusivity) || c.MoistureDiffusivity is < 0 or > 0.2)
            throw new ArgumentOutOfRangeException(nameof(coupling));
        if (!double.IsFinite(c.VaporCapacityMultiplier) || c.VaporCapacityMultiplier is < 1 or > 3
            || !double.IsFinite(c.OceanRainShare) || c.OceanRainShare is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(coupling));
        if (!double.IsFinite(c.RainReturnThresholdMm) || c.RainReturnThresholdMm is < 0 or > 10000
            || !double.IsFinite(c.RainReturnShare) || c.RainReturnShare is < 0 or > 0.5)
            throw new ArgumentOutOfRangeException(nameof(coupling));
        var solver = new HadleyCoupledClimateSimulation(world, windSettings, s, c, progress, cancellationToken);
        var wind = HadleyWindSimulation.Run(world, windSettings with
        {
            Iterations = s.WarmupSteps + s.SampleSteps, TimeStep = s.TimeStep,
            UseZonalPressureBands = windSettings.Cells is not null
        }, cancellationToken, solver);
        return (wind, solver.Result(wind));
    }

    internal static HadleyCoupledSettings CouplingFor(WorldManifest manifest) =>
        manifest.GeneratorVersion >= WorldGenerator.MoistureTransportVersion
            ? new HadleyCoupledSettings(
                VaporCapacityMultiplier: manifest.GeneratorVersion >= WorldGenerator.BalancedRainVersion ? 40.0 / 30
                    : manifest.GeneratorVersion >= WorldGenerator.HumidityBoostVersion ? 1.95 : 1.5,
                OceanRainShare: 0.5,
                MoistureDiffusivity: manifest.GeneratorVersion >= WorldGenerator.CloudTransportVersion ? 0.08 : 0,
                RainReturnShare: manifest.GeneratorVersion >= WorldGenerator.RainRecyclingVersion ? 0.25 : 0)
            : new HadleyCoupledSettings();

    public void BeforeTransport(HadleyWindField field, float dt)
    {
        _token.ThrowIfCancellationRequested();
        for (int channel = 0; channel < 6; channel++)
        {
            bool upper = channel % 2 == 1;
            var vx = upper ? field.UpperVx : field.Vx; var vy = upper ? field.UpperVy : field.Vy;
            var q = _q[channel]; var result = _next[channel];
            if (channel < 4 && _coupling.MoistureDiffusivity > 0)
                MixMoisture(q, result, upper ? field.UpperMass : field.LowerMass, dt);
            Rows(y =>
            {
                for (int x = 0; x < _width; x++)
                {
                    int i = y * _width + x, r = y * _width + X(x + 1), d = Math.Min(_height - 1, y + 1) * _width + x;
                    double u = (vx[i] + vx[r]) * 0.5, v = (vy[i] + vy[d]) * 0.5;
                    _fx[i] = !_wrap && x == _width - 1 ? 0 : u * dt * _ix * q[u >= 0 ? i : r];
                    _fy[i] = y == _height - 1 ? 0 : v * dt * _iy * q[v >= 0 ? i : d];
                }
            });
            Rows(y =>
            {
                for (int x = 0; x < _width; x++)
                {
                    int i = y * _width + x;
                    result[i] = q[i] - _fx[i] + (!_wrap && x == 0 ? 0 : _fx[y * _width + X(x - 1)])
                        - _fy[i] + (y == 0 ? 0 : _fy[(y - 1) * _width + x]);
                }
            });
            (_q[channel], _next[channel]) = (_next[channel], _q[channel]);
        }
    }

    private void MixMoisture(double[] water, double[] scratch, float[] mass, float dt)
    {
        double coefficient = _coupling.MoistureDiffusivity * dt;
        Rows(y =>
        {
            for (int x = 0; x < _width; x++)
            {
                int i = y * _width + x, r = y * _width + X(x + 1), d = Math.Min(_height - 1, y + 1) * _width + x;
                // Mix concentrations, with equal and opposite fluxes across each face.
                _fx[i] = !_wrap && x == _width - 1 ? 0 : coefficient * _ix * _ix * Math.Min(mass[i], mass[r])
                    * (water[i] / mass[i] - water[r] / mass[r]);
                _fy[i] = y == _height - 1 ? 0 : coefficient * _iy * _iy * Math.Min(mass[i], mass[d])
                    * (water[i] / mass[i] - water[d] / mass[d]);
            }
        });
        Rows(y =>
        {
            for (int x = 0; x < _width; x++)
            {
                int i = y * _width + x;
                scratch[i] = water[i] - _fx[i] + (!_wrap && x == 0 ? 0 : _fx[y * _width + X(x - 1)])
                    - _fy[i] + (y == 0 ? 0 : _fy[(y - 1) * _width + x]);
            }
        });
        Array.Copy(scratch, water, _count);
    }

    public void Exchange(int i, float amount, float lowerMass, float upperMass)
    {
        int donor = amount >= 0 ? 0 : 1, receiver = 1 - donor;
        double fraction = Math.Abs(amount) / (double)(donor == 0 ? lowerMass : upperMass);
        for (int channel = 0; channel < 6; channel += 2)
        {
            double moved = _q[channel + donor][i] * fraction;
            _q[channel + donor][i] -= moved; _q[channel + receiver][i] += moved;
        }
        // Convert between lower dry static temperature and upper temperature; retain the existing inter-layer lapse.
        double shift = 6 + _lowerHeightTemperatureOffset[i];
        _q[4 + receiver][i] += Math.Abs(amount) * (donor == 0 ? -shift : shift);
    }

    public void CompleteStep(HadleyWindField field, float[] forcing, float dt)
    {
        _baseForcing ??= (float[])forcing.Clone();
        bool sampled = _time + dt * 0.5f >= _settings.WarmupSteps * _settings.TimeStep;
        Rows(y =>
        {
            double latitude = (y + 0.5) / _height * 2 - 1;
            double season = Math.Sin(_time * Math.PI * 2 / 60) * Math.Sin(latitude * Math.PI / 2);
            for (int i = y * _width; i < (y + 1) * _width; i++)
            {
                double ml = field.LowerMass[i], mu = field.UpperMass[i];
                double tl = _q[4][i] / ml - 273.15 - _lowerHeightTemperatureOffset[i];
                double tu = _q[5][i] / mu - 273.15;
                double target = _target[i] + season * (2 + 6 * _land[i]);
                tl += (target - tl) * (1 - Math.Exp(-dt * (0.06 + 0.08 * _land[i]) * _coupling.ThermalRelaxationMultiplier));
                tu += (_target[i] - 25 + season * 2 - tu) * (1 - Math.Exp(-dt * 0.035 * _coupling.ThermalRelaxationMultiplier));
                if (!_coupling.EvolveTemperature) { tl = _target[i]; tu = _target[i] - 25; }
                double cap = Capacity(tl) * ml;
                double ocean = AquaticEvaporation(cap, _q[0][i], _deepOcean[i], _shallowOcean[i],
                    _lake[i], _wetland[i], _settings.EvaporationRelaxation, dt, _coupling);
                double soilEvap = _coupling.RecycleSoil ? Math.Min(_soil[i], _land[i] * 0.035 * dt * Math.Max(0, cap * _coupling.OtherHumidityTarget - _q[0][i])
                    * Math.Min(1, _soil[i] / Math.Max(1, 25 * _land[i]))) : 0;
                _soil[i] -= soilEvap;
                _q[0][i] += ocean + soilEvap;
                _oceanEvap[i] += ocean; _soilEvap[i] += soilEvap;
                tl -= (ocean + soilEvap) * 0.025 / ml;
                double uphill = Math.Max(0, field.Vx[i] * _slopeX[i] + field.Vy[i] * _slopeY[i]);
                for (int layer = 0; layer < 2; layer++)
                {
                    double mass = layer == 0 ? ml : mu;
                    double temperature = layer == 0
                        ? tl - (_coupling.OrographicThermodynamics ? 0 : Math.Min(8, uphill * 6)) : tu;
                    double capacity = Capacity(temperature) * mass;
                    double condensed = Math.Max(0, _q[layer][i] - capacity) * (1 - Math.Exp(-_settings.CondensationRate * dt));
                    _q[layer][i] -= condensed; _q[2 + layer][i] += condensed;
                    double reevap = Math.Min(_q[2 + layer][i], Math.Max(0, capacity - _q[layer][i]) * (1 - Math.Exp(-0.3 * dt)));
                    _q[2 + layer][i] -= reevap; _q[layer][i] += reevap;
                    if (layer == 0) tl += (condensed - reevap) * 0.025 / mass;
                    else tu += (condensed - reevap) * 0.025 / mass;
                }
                double oceanFraction = _deepOcean[i] + _shallowOcean[i];
                // Reduced ocean rain stays in the cloud reservoir and continues to travel.
                double retained = CloudRetention(dt, _coupling.CloudRainTime, oceanFraction, _coupling.OceanRainShare);
                double rain = (_q[2][i] + _q[3][i]) * (1 - retained);
                _q[2][i] *= retained; _q[3][i] *= retained;
                _totalRain[i] += rain; if (sampled) _rain[i] += rain;
                double infiltration = rain * _land[i]; _soil[i] += infiltration;
                double returned = ReturnedRain(rain, _land[i], _totalRain[i], _time + dt,
                    _coupling.RainReturnThresholdMm, _coupling.RainReturnShare, _rainOffsetMultiplier);
                // Returned rainfall is withdrawn from newly infiltrated water, then carried by lower clouds.
                _soil[i] -= returned; _q[2][i] += returned; _returnedRain[i] += returned;
                double overflow = Math.Max(0, _soil[i] - 80 * _land[i]);
                _soil[i] -= overflow; _runoff[i] += rain - infiltration + overflow;
                if (tl is < -90 or > 65 || tu is < -100 or > 50) _temperatureLimits[i]++;
                tl = Math.Clamp(tl, -90, 65); tu = Math.Clamp(tu, -100, 50);
                if (!_coupling.EvolveTemperature) { tl = _target[i]; tu = _target[i] - 25; }
                _q[4][i] = (tl + 273.15 + _lowerHeightTemperatureOffset[i]) * ml;
                _q[5][i] = (tu + 273.15) * mu;
                forcing[i] = _baseForcing[i] - (float)(0.04 * (tl - _target[i]) + 0.02 * (tu - (_target[i] - 25)));
                if (sampled)
                {
                    _relativeHumiditySum[i] += Math.Clamp(_q[0][i] / Math.Max(1e-12, Capacity(tl) * ml), 0, 1) * dt;
                    _vaporSum[i] += (_q[0][i] + _q[1][i]) * dt;
                    _cloudSum[i] += (_q[2][i] + _q[3][i]) * dt;
                    _sampleOceanEvap[i] += ocean; _sampleSoilEvap[i] += soilEvap;
                    _sampleRainReturn[i] += returned;
                }
            }
        });
        _time += dt;
        int percent = (int)Math.Min(100, _time / ((_settings.WarmupSteps + _settings.SampleSteps) * _settings.TimeStep) * 100);
        if (percent != _lastProgress) { _progress?.Invoke(percent); _lastProgress = percent; }
    }

    private int _lastProgress = -1;
    private HadleyMoistureResult Result(HadleyWindField wind)
    {
        double ocean = _oceanEvap.Sum(), runoff = _runoff.Sum();
        double atmospheric = _q.Take(4).Sum(channel => channel.Sum()), soil = _soil.Sum();
        double final = atmospheric + soil;
        double error = Math.Abs(_initial + ocean - runoff - final) / Math.Max(1, _initial + ocean);
        if (error > 1e-5 || _q.SelectMany(channel => channel).Any(value => !double.IsFinite(value) || value < 0)
            || _soil.Any(value => !double.IsFinite(value) || value < 0))
            throw new InvalidOperationException("Falha no balanço de água ou estabilidade do clima acoplado.");
        var lowerTemp = new float[_count]; var upperTemp = new float[_count]; double tempChange = 0;
        for (int i = 0; i < _count; i++)
        {
            lowerTemp[i] = (float)(_q[4][i] / wind.LowerMass[i] - 273.15 - _lowerHeightTemperatureOffset[i]);
            upperTemp[i] = (float)(_q[5][i] / wind.UpperMass[i] - 273.15);
            tempChange += Math.Pow(lowerTemp[i] - _target[i], 2);
        }
        var vapor = _vaporSum.Select(value => (float)(value / (_settings.SampleSteps * _settings.TimeStep))).ToArray();
        var clouds = _cloudSum.Select(value => (float)(value / (_settings.SampleSteps * _settings.TimeStep))).ToArray();
        var annualRain = _rain.Select(value => (float)(value * 365 / (_settings.SampleSteps * _settings.TimeStep))).ToArray();
        var distance = Enumerable.Repeat(int.MaxValue, _count).ToArray(); var queue = new Queue<int>();
        for (int i = 0; i < _count; i++) if (_wet[i] > 0.5f) { distance[i] = 0; queue.Enqueue(i); }
        while (queue.TryDequeue(out int i))
        {
            int x = i % _width, y = i / _width;
            Visit(y * _width + X(x - 1)); Visit(y * _width + X(x + 1));
            if (y > 0) Visit(i - _width); if (y + 1 < _height) Visit(i + _width);
            void Visit(int next)
            {
                if (distance[next] != int.MaxValue) return;
                distance[next] = distance[i] + 1; queue.Enqueue(next);
            }
        }
        var latitudeBands = new[] { Band("0–15°", i => Latitude(i) < 15), Band("15–35°", i => Latitude(i) >= 15 && Latitude(i) < 35),
            Band("35–60°", i => Latitude(i) >= 35 && Latitude(i) < 60), Band("60–90°", i => Latitude(i) >= 60) };
        var coastBands = new[] { Band("0–2 coarse cells", i => distance[i] <= 2),
            Band("3–8 coarse cells", i => distance[i] is >= 3 and <= 8), Band(">8 coarse cells", i => distance[i] > 8) };
        HadleyHydrologyBand Band(string label, Func<int, bool> predicate)
        {
            int cells = 0; double v = 0, c = 0, r = 0;
            for (int i = 0; i < _count; i++) if (_land[i] > 0.5f && predicate(i))
            { cells++; v += vapor[i]; c += clouds[i]; r += annualRain[i]; }
            return new(label, cells, v / Math.Max(1, cells), c / Math.Max(1, cells), r / Math.Max(1, cells));
        }
        double Latitude(int i) => Math.Abs((i / _width + 0.5) / _height * 2 - 1) * 90;
        var diagnostics = new HadleyCoupledDiagnostics(ocean, _soilEvap.Sum(), runoff, _totalRain.Sum(),
            atmospheric, soil, _q[2].Sum() + _q[3].Sum(), Math.Sqrt(tempChange / _count),
            vapor, clouds, _soil.Select(value => (float)value).ToArray(), lowerTemp, upperTemp,
            latitudeBands, coastBands, _coupling, _temperatureLimits.Sum(),
            _returnedRain.Sum(),
            _sampleRainReturn.Select(value => (float)(value / (_settings.SampleSteps * _settings.TimeStep))).ToArray(),
            _sampleOceanEvap.Select(value => (float)(value / (_settings.SampleSteps * _settings.TimeStep))).ToArray(),
            _sampleSoilEvap.Select(value => (float)(value / (_settings.SampleSteps * _settings.TimeStep))).ToArray(),
            _relativeHumiditySum.Select(value => (float)(value / (_settings.SampleSteps * _settings.TimeStep))).ToArray());
        return new(annualRain,
            _initial, ocean, _totalRain.Sum(), final, error, _settings, diagnostics);
    }

    internal static double CloudRetention(double dt, double rainTime, double oceanFraction, double oceanRainShare)
        => 1 - (1 - Math.Exp(-dt / rainTime)) * (1 - oceanFraction * (1 - oceanRainShare));

    internal static double ReturnedRain(double rain, double landFraction, double cumulativeRain, double elapsedTime,
        double thresholdMm, double share, double rainOffsetMultiplier = 1)
    {
        if (rain <= 0 || landFraction <= 0 || share <= 0) return 0;
        // Use the received rainfall history, in the same annual units as the generated map, not a per-step threshold.
        double annualMm = Math.Min(5000, cumulativeRain * 365 * WorldGenerator.HadleyRainScale / elapsedTime)
            * rainOffsetMultiplier;
        return annualMm > thresholdMm ? rain * landFraction * share : 0;
    }

    private double Capacity(double temperature) => _coupling.VaporCapacityMultiplier
        * 30 * Math.Exp(0.06 * (Math.Clamp(temperature, -60, 45) - 25));

    internal static double AquaticEvaporation(double capacity, double vapor, float deepOcean, float shallowOcean,
        float lake, float wetland, float relaxation, float dt, HadleyCoupledSettings settings)
    {
        // Keep each source's cutoff when a coarse cell contains several surface types.
        double deficit = deepOcean * Math.Max(0, capacity * settings.OceanHumidityTarget - vapor)
            + shallowOcean * Math.Max(0, capacity * settings.ShallowOceanHumidityTarget - vapor)
            + lake * 0.6 * Math.Max(0, capacity * settings.LakeHumidityTarget - vapor)
            + wetland * 0.4 * Math.Max(0, capacity * settings.OtherHumidityTarget - vapor);
        return deficit * relaxation * settings.AquaticReplenishmentMultiplier * dt;
    }
    private int X(int x) => _wrap ? (x % _width + _width) % _width : Math.Clamp(x, 0, _width - 1);
    private void Rows(Action<int> action)
    {
        if (_count >= 32768) Parallel.For(0, _height, action);
        else for (int y = 0; y < _height; y++) action(y);
    }
}
