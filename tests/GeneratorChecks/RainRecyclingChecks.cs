using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    internal static void CheckRainRecycling()
    {
        double raw500 = 500 / (365 * HadleyRainScale);
        foreach (double share in new[] { 0.25, 0.5 })
        {
            Require(HadleyCoupledClimateSimulation.ReturnedRain(1, 1, raw500 * 0.9, 1, 500, share) == 0
                && HadleyCoupledClimateSimulation.ReturnedRain(1, 1, raw500, 1, 500, share) == 0,
                "Rain return activated below or exactly at the threshold");
            foreach (double land in new[] { 0, 0.25, 1 })
            {
                double returned = HadleyCoupledClimateSimulation.ReturnedRain(2, land, raw500 * 2, 1, 500, share);
                Require(returned == 2 * land * share && returned <= 2 * land,
                    "Rain return exceeded infiltration or included aquatic rainfall");
            }
            double whole = HadleyCoupledClimateSimulation.ReturnedRain(2, 1, raw500 * 2, 1, 750, share);
            double quarter = HadleyCoupledClimateSimulation.ReturnedRain(0.5, 1, raw500 * 0.5, 0.25, 750, share);
            Require(whole == 4 * quarter, "Annual rainfall threshold depends on timestep units");
            Require(HadleyCoupledClimateSimulation.ReturnedRain(2, 1, raw500 * 2, 1, 750, share, 0.5) == 0,
                "Rain threshold ignored the map precipitation offset");
            Require(HadleyCoupledClimateSimulation.ReturnedRain(0, 1, raw500 * 2, 1, 500, share) == 0,
                "Rain history created water when no new rain arrived");
        }
        var packet = HadleyCoupledClimateSimulation.RunCloudPacket(3);
        var returnedPacket = HadleyCoupledClimateSimulation.RunCloudPacket(3, rainReturnShare: 0.5);
        double Reach(HadleyMoistureResult r) => Enumerable.Range(0, 128).Sum(x => Math.Max(0, x - 16) * (double)r.AnnualRain[x])
            / r.AnnualRain.Take(128).Sum(v => (double)v);
        Require(Reach(returnedPacket) > Reach(packet) && returnedPacket.RelativeBudgetError < 1e-5,
            "Returned rainfall failed to propagate downwind conservatively");
        Require(Math.Abs(returnedPacket.Coupled!.ReturnedRain - returnedPacket.PrecipitatedWater * 0.5) < 1e-8,
            "Full-land packet did not return exactly half its received precipitation");

        var world = new WorldData(CheckManifest(128, 64, true) with { GeneratorVersion = RainRecyclingVersion });
        Array.Fill(world.Temperature, (short)250);
        for (int y = 0; y < world.Height; y++) for (int x = 0; x < 32; x++)
            world.Water[y * world.Width + x] = WaterType.Ocean;
        var wind = HadleySettingsFor(world);
        var duration = new HadleyMoistureSettings(WarmupSteps: 120, SampleSteps: 120);
        var a = HadleyCoupledClimateSimulation.Simulate(world, wind, duration);
        var b = HadleyCoupledClimateSimulation.Simulate(world, wind, duration);
        Require(a.Moisture.Coupled!.Settings is { RainReturnThresholdMm: 500, RainReturnShare: 0.25 }
            && a.Moisture.RelativeBudgetError < 1e-5
            && a.Moisture.Coupled.ReturnedRain > 0 && a.Moisture.AnnualRain.SequenceEqual(b.Moisture.AnnualRain),
            "v69 failed recipe selection, rain return, water budget or determinism");
        var closed = HadleyCoupledClimateSimulation.Simulate(world, wind, duration with { EvaporationRelaxation = 0 },
            coupling: a.Moisture.Coupled.Settings with { InitialRelativeHumidity = 0, InitialSoilWater = 0 });
        Require(closed.Moisture.FinalWater == 0 && closed.Moisture.PrecipitatedWater == 0,
            "Empty v69 system created water");
        var legacy = new WorldData(world.Manifest with { GeneratorVersion = CloudTransportVersion });
        Array.Copy(world.Water, legacy.Water, world.Count); Array.Copy(world.Temperature, legacy.Temperature, world.Count);
        var v67 = HadleyCoupledClimateSimulation.Simulate(legacy, wind, duration);
        var explicitLegacy = HadleyCoupledClimateSimulation.Simulate(world, wind, duration,
            coupling: a.Moisture.Coupled.Settings with { RainReturnShare = 0 });
        Require(v67.Moisture.Coupled!.Settings.RainReturnShare == 0 && v67.Moisture.Coupled.ReturnedRain == 0
            && v67.Moisture.AnnualRain.SequenceEqual(explicitLegacy.Moisture.AnnualRain), "v67 changed retroactively");
        foreach (var invalid in new[] { a.Moisture.Coupled.Settings with { RainReturnShare = double.NaN },
            a.Moisture.Coupled.Settings with { RainReturnShare = 0.51 },
            a.Moisture.Coupled.Settings with { RainReturnThresholdMm = double.PositiveInfinity },
            a.Moisture.Coupled.Settings with { RainReturnThresholdMm = -1 } })
        {
            bool rejected = false;
            try { HadleyCoupledClimateSimulation.Simulate(world, wind, duration, coupling: invalid); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Require(rejected, "Invalid rain-return settings accepted");
        }
        bool obsoleteRejected = false;
        try { WorldData.Validate(world.Manifest with { GeneratorVersion = 68 }); }
        catch (ArgumentException) { obsoleteRejected = true; }
        Require(obsoleteRejected, "Removed v68 recipe was silently reinterpreted");
        var generated = Create(CheckManifest(96, 64, false) with { GeneratorVersion = RainRecyclingVersion });
        string path = Path.Combine(Path.GetTempPath(), $"rain-return-{Guid.NewGuid():N}.world");
        try
        {
            WorldFile.Save(path, generated, null); var loaded = WorldFile.Load(path);
            RegenerateRegion(loaded, new WorldRegion(0, 0, loaded.Width, loaded.Height));
            Require(loaded.Rainfall.SequenceEqual(generated.Rainfall) && loaded.Biomes.SequenceEqual(generated.Biomes),
                "v69 save/load or regeneration changed seeded climate/biomes");
        }
        finally { File.Delete(path); }
        Console.WriteLine($"PASS rain return: thresholds, shares, mixed coasts, offsets, timestep units, downstream packet reach {Reach(packet):F3}->{Reach(returnedPacket):F3}, closed water budget, determinism, v67 isolation, v69 save/load/regeneration; error {a.Moisture.RelativeBudgetError:E3}");
    }

    internal static void ReviewClimateMatrix(string[] args)
    {
        bool small = args.Contains("--small");
        bool humidityBoost = args.Contains("--humidity-boost-review");
        bool hexSpread = args.Contains("--hex-rain-spread-review");
        bool balancedRain = args.Contains("--balanced-rain-review");
        int seedIndex = Array.IndexOf(args, "--seed");
        int seedsIndex = Array.IndexOf(args, "--seeds");
        int[] seeds = seedsIndex >= 0 ? args[seedsIndex + 1].Split(',').Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray()
            : seedIndex >= 0 ? new[] { int.Parse(args[seedIndex + 1], CultureInfo.InvariantCulture) }
            : new[] { 1155827384, 1234, -77 };
        int versionsIndex = Array.IndexOf(args, "--versions");
        int[] versions = versionsIndex >= 0 ? args[versionsIndex + 1].Split(',').Select(int.Parse).ToArray()
            : balancedRain ? new[] { HexRainSpreadVersion, BalancedRainVersion }
                : hexSpread ? new[] { HumidityBoostVersion, HexRainSpreadVersion }
                : humidityBoost ? new[] { RainRecyclingVersion, HumidityBoostVersion }
                : new[] { CloudTransportVersion, RainRecyclingVersion };
        Require(versions.All(v => v is CloudTransportVersion or (>= RainRecyclingVersion and <= BalancedRainVersion)), "Unsupported comparison recipe");
        foreach (int seed in seeds)
        {
            var size = MapDomain.ExpandedSize(small ? 256 : 4096, small ? 128 : 2048);
            int outputIndex = Array.IndexOf(args, "--output-root");
            string outputRoot = outputIndex >= 0 ? args[outputIndex + 1]
                : balancedRain ? "results/balanced-rain" : hexSpread ? "results/hex-rain-spread" : humidityBoost ? "results/humidity-boost" : "results/rain-return";
            string output = $"{outputRoot}{(small ? "-small" : "")}/{seed}";
            Directory.CreateDirectory(output);
            var timer = Stopwatch.StartNew(); var rows = new List<JsonElement>();
            string? surfaceHash = null;
            if (versionsIndex >= 0 && File.Exists(Path.Combine(output, "summary.json")))
            {
                using var previous = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "summary.json")));
                surfaceHash = previous.RootElement.GetProperty("surfaceHash").GetString();
                rows.AddRange(previous.RootElement.GetProperty("atmosphere").EnumerateArray().Select(e => e.Clone()));
            }
            foreach (int version in versions)
            {
                var manifest = CheckManifest(size.Width, size.Height, false) with
                {
                    Seed = seed, GeneratorVersion = version,
                    Generation = new(TerrainAlgorithm.ContinentalSculpt, 3, 25, 80, GenerationStage.Terrain,
                        MapExtent.World, ReferenceWidth: small ? 256 : 4096, ReferenceHeight: small ? 128 : 2048)
                };
                var world = new WorldData(manifest);
                var area = new GenerationArea(world, new WorldRegion(0, 0, world.Width, world.Height), useWorldArrays: true);
                int last = -1; HadleyClimateExperimentResult? baseline = null;
                Generate(world, area, p =>
                {
                    if ((int)p.Step == last) return;
                    last = (int)p.Step; Console.WriteLine($"seed {seed}, v{version}, {p.Step}, {timer.Elapsed}");
                }, experimentalClimate: value =>
                {
                    baseline = value;
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    hash.AppendData(MemoryMarshal.AsBytes(world.Elevation.AsSpan()));
                    hash.AppendData(MemoryMarshal.AsBytes(world.Water.AsSpan()));
                    hash.AppendData(MemoryMarshal.AsBytes(world.Temperature.AsSpan()));
                    string currentHash = Convert.ToHexString(hash.GetHashAndReset());
                    Console.WriteLine($"Surface hashes v{version}: elevation={Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(world.Elevation.AsSpan())))}, water={Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(world.Water.AsSpan())))}, temperature={Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(world.Temperature.AsSpan())))}");
                    Require(surfaceHash is null || surfaceHash == currentHash, "Variants changed geography or reference temperature");
                    surfaceHash = currentHash;
                    Record($"v{version}", value.Wind, value.Moisture);
                    if (version != CloudTransportVersion || args.Contains("--skip-comparison")) return;
                    foreach (double threshold in new[] { 500.0, 750.0 })
                    foreach (double share in new[] { 0.25, 0.5 })
                    {
                        var candidate = HadleyCoupledClimateSimulation.Simulate(world, HadleySettingsFor(world),
                            coupling: value.Moisture.Coupled!.Settings with { RainReturnThresholdMm = threshold, RainReturnShare = share });
                        Record(FormattableString.Invariant($"return-{threshold}-{share * 100}"), candidate.Wind, candidate.Moisture);
                    }
                }, experimentalRainScale: HadleyRainScale);
                GenerateRivers(world); ApplyFluvialBiomes(world, new WorldRegion(0, 0, world.Width, world.Height));
                var counts = new Dictionary<string, long>(); long land = 0, landRain = 0, totalRain = 0;
                int dryLand = 0, capped = 0;
                for (int y = 0; y < world.Height; y++)
                {
                    var (left, right) = world.Domain.RowBounds(y);
                    for (int x = left; x < right; x++)
                    {
                        int i = y * world.Width + x;
                        totalRain += world.Rainfall[i];
                        if (world.Rainfall[i] >= 5000) capped++;
                        if (world.Water[i] != WaterType.None) continue;
                        land++; landRain += world.Rainfall[i]; if (world.Rainfall[i] < 260) dryLand++;
                        string key = world.Biomes[i].ToString(); counts[key] = counts.GetValueOrDefault(key) + 1;
                    }
                }
                var biomes = new { variant = $"v{version}", landHexes = land, counts,
                    forests = counts.Where(p => Enum.TryParse<BiomeType>(p.Key, out var b) && Biomes.IsForest(b)).Sum(p => p.Value),
                    finalRain = new { allHexes = world.ActiveCount, mean = (double)totalRain / world.ActiveCount,
                        landMean = (double)landRain / Math.Max(1, land), landBelow260Percent = 100.0 * dryLand / Math.Max(1, land),
                        at5000Percent = 100.0 * capped / world.ActiveCount } };
                File.WriteAllText(Path.Combine(output, $"v{version}-biomes.json"), JsonSerializer.Serialize(biomes, JsonOptions));
                Console.WriteLine(JsonSerializer.Serialize(biomes));
                if (hexSpread || balancedRain)
                {
                    var wind = baseline!.Wind;
                    using var mapped = new StreamWriter(Path.Combine(output, $"v{version}-hex-rain.csv"));
                    mapped.WriteLine("x,y,rain");
                    for (int y = 0; y < wind.GridHeight; y++) for (int x = 0; x < wind.GridWidth; x++)
                    {
                        double sum = 0; int samples = 0;
                        for (int row = y * world.Height / wind.GridHeight; row < (y + 1) * world.Height / wind.GridHeight; row++)
                        {
                            var (left, right) = world.Domain.RowBounds(row);
                            int first = left + x * (right - left) / wind.GridWidth;
                            int end = Math.Min(right, Math.Max(first + 1, left + (x + 1) * (right - left) / wind.GridWidth));
                            for (int col = first; col < end; col++) { sum += world.Rainfall[row * world.Width + col]; samples++; }
                        }
                        mapped.WriteLine(FormattableString.Invariant($"{x},{y},{sum / Math.Max(1, samples)}"));
                    }
                }
                File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new
                    { seed, manifest, surfaceHash, elapsedSeconds = timer.Elapsed.TotalSeconds, atmosphere = rows }, JsonOptions));

                void Record(string label, HadleyWindField wind, HadleyMoistureResult moisture)
                {
                    Require(moisture.RelativeBudgetError < 1e-5, $"{label} failed water budget");
                    var d = moisture.Coupled!;
                    Require(d.ReturnedRain <= d.Rainfall * d.Settings.RainReturnShare + 1e-7,
                        $"{label} returned more water than the configured rain fraction");
                    var ground = new double[moisture.AnnualRain.Length]; var landCells = new bool[ground.Length];
                    for (int y = 0; y < wind.GridHeight; y++) for (int x = 0; x < wind.GridWidth; x++)
                    {
                        int samples = 0, dry = 0; double height = 0;
                        for (int row = y * world.Height / wind.GridHeight; row < (y + 1) * world.Height / wind.GridHeight; row++)
                        {
                            var (left, right) = world.Domain.RowBounds(row);
                            for (int col = left + x * (right - left) / wind.GridWidth; col < left + (x + 1) * (right - left) / wind.GridWidth; col++)
                            {
                                int i = row * world.Width + col; samples++;
                                if (world.Water[i] == WaterType.None) { dry++; height += Math.Max(0, (int)world.Elevation[i]); }
                            }
                        }
                        int cell = y * wind.GridWidth + x; landCells[cell] = dry > samples / 2; ground[cell] = height / Math.Max(1, dry);
                    }
                    var distance = Enumerable.Repeat(int.MaxValue, ground.Length).ToArray(); var queue = new Queue<int>();
                    for (int i = 0; i < distance.Length; i++) if (!landCells[i]) { distance[i] = 0; queue.Enqueue(i); }
                    while (queue.TryDequeue(out int i))
                    {
                        int x = i % wind.GridWidth, y = i / wind.GridWidth;
                        Visit(y * wind.GridWidth + (x + wind.GridWidth - 1) % wind.GridWidth);
                        Visit(y * wind.GridWidth + (x + 1) % wind.GridWidth);
                        if (y > 0) Visit(i - wind.GridWidth); if (y + 1 < wind.GridHeight) Visit(i + wind.GridWidth);
                        void Visit(int next) { if (distance[next] != int.MaxValue) return; distance[next] = distance[i] + 1; queue.Enqueue(next); }
                    }
                    var lowlands = Enumerable.Range(0, ground.Length).Where(i => landCells[i] && ground[i] <= 300 && distance[i] >= 3).ToArray();
                    var interior = new { cells = lowlands.Length,
                        rain = lowlands.Length == 0 ? 0 : lowlands.Average(i => moisture.AnnualRain[i] * HadleyRainScale),
                        below260Percent = lowlands.Length == 0 ? 0 : 100.0 * lowlands.Count(i => moisture.AnnualRain[i] * HadleyRainScale < 260) / lowlands.Length };
                    var measurement = new { variant = label, moisture.Settings, coupling = d.Settings, windSettings = wind.Settings,
                        waterError = moisture.RelativeBudgetError, d.OceanEvaporation, d.SoilEvaporation, d.SoilWater,
                        d.ReturnedRain, d.TemperatureLimitApplications,
                        meanRain = moisture.AnnualRain.Average(v => v * HadleyRainScale),
                        above5000Percent = 100.0 * moisture.AnnualRain.Count(v => v * HadleyRainScale > 5000) / ground.Length,
                        interior, latitude = d.LatitudeBands, coast = d.CoastDistanceBands };
                    rows.RemoveAll(e => e.GetProperty("variant").GetString() == label);
                    rows.Add(JsonSerializer.SerializeToElement(measurement)); Console.WriteLine(JsonSerializer.Serialize(measurement));
                    File.WriteAllText(Path.Combine(output, "atmosphere.json"), JsonSerializer.Serialize(rows, JsonOptions));
                    using var csv = new StreamWriter(Path.Combine(output, $"{label}-cells.csv"));
                    csv.WriteLine("x,y,land,elevation,distance,rain,vapor,cloud,soil,rainReturn,aquaticEvaporation,soilEvaporation");
                    for (int i = 0; i < moisture.AnnualRain.Length; i++) csv.WriteLine(FormattableString.Invariant(
                        $"{i % wind.GridWidth},{i / wind.GridWidth},{(landCells[i] ? 1 : 0)},{ground[i]},{distance[i]},{moisture.AnnualRain[i] * HadleyRainScale},{d.Vapor[i]},{d.Clouds[i]},{d.Soil[i]},{d.RainReturnRate[i]},{d.AquaticEvaporationRate[i]},{d.SoilEvaporationRate[i]}"));
                }
            }
            Console.WriteLine($"PASS climate matrix seed {seed}: {timer.Elapsed}");
            GC.Collect();
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
}
