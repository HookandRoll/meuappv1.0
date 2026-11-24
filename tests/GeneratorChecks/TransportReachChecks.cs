using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.Text.Json;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    internal static void CheckCloudTransport()
    {
        HadleyCoupledClimateSimulation.CheckConservativeMixing();
        var shortLife = HadleyCoupledClimateSimulation.RunCloudPacket(3);
        var longLife = HadleyCoupledClimateSimulation.RunCloudPacket(6);
        double Reach(HadleyMoistureResult result) => Enumerable.Range(0, 128)
            .Sum(x => Math.Max(0, x - 16) * (double)result.AnnualRain[x]) / result.AnnualRain.Take(128).Sum(x => (double)x);
        Console.WriteLine($"Packet reach: {Reach(shortLife):F6}->{Reach(longLife):F6}; budgets {shortLife.RelativeBudgetError:E3}/{longLife.RelativeBudgetError:E3}");
        Require(Reach(longLife) > Reach(shortLife) + 0.1 && shortLife.RelativeBudgetError < 1e-5
            && longLife.RelativeBudgetError < 1e-5, "Cloud lifetime failed to extend rainfall transport conservatively");
        var world = new WorldData(CheckManifest(128, 64, true) with { GeneratorVersion = CloudTransportVersion });
        Array.Fill(world.Water, WaterType.Ocean); Array.Fill(world.Temperature, (short)250);
        var settings = new HadleyWindSettings(Cells: new AtmosphericCellSettings());
        var duration = new HadleyMoistureSettings(WarmupSteps: 20, SampleSteps: 20);
        var main = HadleyCoupledClimateSimulation.Simulate(world, settings, duration);
        var explicitSettings = HadleyCoupledClimateSimulation.Simulate(world, settings, duration,
            coupling: new HadleyCoupledSettings(MoistureDiffusivity: 0.08, VaporCapacityMultiplier: 1.5, OceanRainShare: 0.5));
        Require(main.Moisture.Coupled!.Settings.CloudRainTime == 3 && main.Moisture.Coupled.Settings.MoistureDiffusivity == 0.08
            && main.Moisture.AnnualRain.SequenceEqual(explicitSettings.Moisture.AnnualRain),
            "v67 does not use conservative moisture mixing");
        Require(main.Moisture.RelativeBudgetError < 1e-5, "Mixing violated the coupled water budget");
        Console.WriteLine($"PASS cloud transport: prescribed flat wind rain travel {Reach(shortLife):F3}->{Reach(longLife):F3} coarse cells, conservation, exact v67 settings");
    }

    internal static void ReviewTransportReach(bool mixing = false)
    {
        var size = MapDomain.ExpandedSize(4096, 2048);
        var manifest = CheckManifest(size.Width, size.Height, false) with
        {
            Seed = 1155827384, GeneratorVersion = MoistureTransportVersion,
            Generation = new(TerrainAlgorithm.ContinentalSculpt, 3, 25, 80, GenerationStage.Terrain,
                MapExtent.World, ReferenceWidth: 4096, ReferenceHeight: 2048)
        };
        string output = mixing ? "results/transport-1155827384-mixing" : "results/transport-1155827384"; Directory.CreateDirectory(output);
        var timer = Stopwatch.StartNew();
        var world = new WorldData(manifest);
        var area = new GenerationArea(world, new WorldRegion(0, 0, world.Width, world.Height), useWorldArrays: true);
        HadleyClimateExperimentResult? baseline = null;
        int last = -1;
        Generate(world, area, p => { if ((int)p.Step != last) { last = (int)p.Step; Console.WriteLine($"Generate {p.Step}: {timer.Elapsed}"); } },
            experimentalClimate: value => baseline = value, experimentalRainScale: HadleyRainScale);
        Require(baseline is not null, "Missing baseline climate");
        Console.WriteLine($"Baseline complete {world.Width}x{world.Height}, {world.ActiveCount} active hexes: {timer.Elapsed}");
        var revised = HadleyCoupledClimateSimulation.Simulate(world, HadleySettingsFor(world),
            coupling: mixing ? baseline!.Moisture.Coupled!.Settings with { MoistureDiffusivity = 0.08 }
                : baseline!.Moisture.Coupled!.Settings with { CloudRainTime = 6 });
        Require(revised.Moisture.RelativeBudgetError < 1e-5, "Cloud lifetime change broke water conservation");
        var wind = baseline.Wind;
        int n = wind.GridWidth * wind.GridHeight;
        var land = new bool[n]; var elevation = new double[n];
        for (int y = 0; y < wind.GridHeight; y++) for (int x = 0; x < wind.GridWidth; x++)
        {
            int count = 0, dry = 0; double height = 0;
            for (int row = y * world.Height / wind.GridHeight; row < (y + 1) * world.Height / wind.GridHeight; row++)
            {
                var (left, right) = world.Domain.RowBounds(row);
                for (int col = left + x * (right - left) / wind.GridWidth; col < left + (x + 1) * (right - left) / wind.GridWidth; col++)
                { int i = row * world.Width + col; count++; if (world.Water[i] == WaterType.None) { dry++; height += Math.Max(0, (int)world.Elevation[i]); } }
            }
            int cell = y * wind.GridWidth + x; land[cell] = dry > count / 2; elevation[cell] = height / Math.Max(1, dry);
        }
        var distance = Enumerable.Repeat(int.MaxValue, n).ToArray(); var queue = new Queue<int>();
        for (int i = 0; i < n; i++) if (!land[i]) { distance[i] = 0; queue.Enqueue(i); }
        while (queue.TryDequeue(out int i))
        {
            int x = i % wind.GridWidth, y = i / wind.GridWidth;
            Visit(y * wind.GridWidth + (x + wind.GridWidth - 1) % wind.GridWidth);
            Visit(y * wind.GridWidth + (x + 1) % wind.GridWidth);
            if (y > 0) Visit(i - wind.GridWidth); if (y + 1 < wind.GridHeight) Visit(i + wind.GridWidth);
            void Visit(int next) { if (distance[next] != int.MaxValue) return; distance[next] = distance[i] + 1; queue.Enqueue(next); }
        }
        var rows = new List<object>();
        foreach (var (label, a, b) in new[] { ("baseline-3", baseline.Moisture, baseline.Wind), (mixing ? "mixing-0.08" : "cloud-6", revised.Moisture, revised.Wind) })
        {
            foreach (string band in new[] { "all-land", "coast-1-2", "inland-3-8", "inland-9+", "lowland-inland-3+" })
            {
                var cells = Enumerable.Range(0, n).Where(i => land[i] && (band switch
                { "coast-1-2" => distance[i] <= 2, "inland-3-8" => distance[i] is >= 3 and <= 8,
                  "inland-9+" => distance[i] > 8, "lowland-inland-3+" => distance[i] >= 3 && elevation[i] <= 300, _ => true })).ToArray();
                if (cells.Length == 0) continue;
                var row = new { variant = label, band, cells = cells.Length,
                    rain = cells.Average(i => a.AnnualRain[i] * HadleyRainScale),
                    vapor = cells.Average(i => (double)a.Coupled!.Vapor[i]),
                    clouds = cells.Average(i => (double)a.Coupled!.Clouds[i]),
                    below260Percent = 100.0 * cells.Count(i => a.AnnualRain[i] * HadleyRainScale < 260) / cells.Length,
                    surfaceSpeed = cells.Average(i => Math.Sqrt(b.Vx[i] * b.Vx[i] + b.Vy[i] * b.Vy[i])) };
                rows.Add(row); Console.WriteLine(JsonSerializer.Serialize(row));
            }
        }
        File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new
        { seed = manifest.Seed, manifest, grid = new[] { wind.GridWidth, wind.GridHeight },
          elapsedSeconds = timer.Elapsed.TotalSeconds, baselineBudget = baseline.Moisture.RelativeBudgetError,
          revisedBudget = revised.Moisture.RelativeBudgetError, bands = rows }, new JsonSerializerOptions { WriteIndented = true }));
        using var csv = new StreamWriter(Path.Combine(output, "cells.csv"));
        csv.WriteLine(mixing ? "x,y,land,elevation,distance,rain3,rainMixing08,vapor3,vaporMixing08,vx,vy,vertical"
            : "x,y,land,elevation,distance,rain3,rain6,vapor3,vapor6,vx,vy,vertical");
        for (int i = 0; i < n; i++) csv.WriteLine(FormattableString.Invariant($"{i % wind.GridWidth},{i / wind.GridWidth},{(land[i] ? 1 : 0)},{elevation[i]},{distance[i]},{baseline.Moisture.AnnualRain[i] * HadleyRainScale},{revised.Moisture.AnnualRain[i] * HadleyRainScale},{baseline.Moisture.Coupled!.Vapor[i]},{revised.Moisture.Coupled!.Vapor[i]},{wind.Vx[i]},{wind.Vy[i]},{wind.VerticalFlow[i]}"));
        Console.WriteLine($"PASS transport review: {timer.Elapsed}");
    }
}

internal sealed partial class HadleyCoupledClimateSimulation
{
    internal static void CheckConservativeMixing()
    {
        foreach (bool wrap in new[] { false, true })
        {
            var world = new WorldData(WorldGenerator.CheckManifest(128, 32, wrap));
            var solver = new HadleyCoupledClimateSimulation(world, new HadleyWindSettings(MaxGridHeight: 32),
                new HadleyMoistureSettings(), new HadleyCoupledSettings(MoistureDiffusivity: 0.08), null, default);
            var mass = Enumerable.Range(0, world.Count).Select(i => 0.5f + i % 13 * 0.1f).ToArray();
            var water = mass.Select(m => (double)m * 7).ToArray(); var scratch = new double[water.Length];
            solver.MixMoisture(water, scratch, mass, 0.125f);
            if (water.Where((q, i) => Math.Abs(q / mass[i] - 7) > 1e-12).Any())
                throw new Exception("Moisture mixing moved water at uniform mixing ratio");
            Array.Clear(water); water[16 * 128] = 10;
            solver.MixMoisture(water, scratch, mass, 0.125f);
            if (Math.Abs(water.Sum() - 10) > 1e-12 || water.Any(q => q < 0)
                || water[16 * 128 + 1] <= 0 || (water[16 * 128 + 127] > 0) != wrap)
                throw new Exception("Moisture mixing violated conservation, positivity or horizontal boundaries");
        }
    }

    internal static HadleyMoistureResult RunCloudPacket(float lifetime, double rainReturnShare = 0)
    {
        var world = new WorldData(WorldGenerator.CheckManifest(128, 32, true));
        Array.Fill(world.Temperature, (short)250);
        var settings = new HadleyWindSettings(MaxGridHeight: 32, Iterations: 1,
            PressureForce: 0, ConvectiveLift: 0, UseZonalPressureBands: false);
        var wind = HadleyWindSimulation.Simulate(world, settings);
        Array.Fill(wind.Vx, 1); Array.Fill(wind.UpperVx, 1);
        var moisture = new HadleyMoistureSettings(WarmupSteps: 0, SampleSteps: 160, EvaporationRelaxation: 0);
        var solver = new HadleyCoupledClimateSimulation(world, settings, moisture,
            new HadleyCoupledSettings(EvolveTemperature: false, RecycleSoil: false, CloudRainTime: lifetime,
                InitialRelativeHumidity: 1, InitialSoilWater: 0, RainReturnThresholdMm: 0, RainReturnShare: rainReturnShare), null, default);
        for (int y = 0; y < 32; y++) { int i = y * 128 + 16; solver._q[0][i] -= 10; solver._q[2][i] += 10; }
        var forcing = new float[128 * 32];
        for (int step = 0; step < 160; step++) { solver.BeforeTransport(wind, 0.25f); solver.CompleteStep(wind, forcing, 0.25f); }
        return solver.Result(wind);
    }
}
