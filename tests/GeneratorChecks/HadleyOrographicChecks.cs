using System;
using System.Linq;

namespace Mapmaker.Core;

// Prescribed divergence-free winds isolate parcel thermodynamics from changes in the circulation solver.
internal sealed partial class HadleyCoupledClimateSimulation
{
    internal static HadleyCoupledDiagnostics CheckOrographicLayerTransfer(WorldData world, int cell, float amount)
    {
        var settings = new HadleyWindSettings(MaxGridWidth: 128, MaxGridHeight: 32,
            Iterations: 1, PressureForce: 0, ConvectiveLift: 0);
        var wind = HadleyWindSimulation.Simulate(world, settings);
        var solver = new HadleyCoupledClimateSimulation(world, settings, new HadleyMoistureSettings(),
            new HadleyCoupledSettings(InitialRelativeHumidity: 0, InitialSoilWater: 0), null, default);
        solver.Exchange(cell, amount, 1, 1);
        wind.LowerMass[cell] -= amount; wind.UpperMass[cell] += amount;
        return solver.Result(wind).Coupled!;
    }

    internal static HadleyMoistureResult RunOrographicColumn(WorldData world, float speed,
        HadleyMoistureSettings settings, HadleyCoupledSettings coupling)
    {
        var windSettings = new HadleyWindSettings(MaxGridWidth: 128, MaxGridHeight: 32,
            Iterations: 1, PressureForce: 0, ConvectiveLift: 0, UseZonalPressureBands: false);
        var wind = HadleyWindSimulation.Simulate(world, windSettings);
        if (!wind.WrapHorizontal) throw new ArgumentException("The column test needs periodic boundaries.");
        Array.Fill(wind.Vx, speed); Array.Fill(wind.UpperVx, speed);
        var solver = new HadleyCoupledClimateSimulation(world, windSettings, settings, coupling, null, default);
        var forcing = new float[wind.Vx.Length];
        int subdivisions = Math.Max(1, (int)Math.Ceiling(settings.TimeStep * Math.Abs(speed) * wind.GridWidth / 128 / 0.8));
        float dt = settings.TimeStep / subdivisions;
        for (int step = 0; step < settings.WarmupSteps + settings.SampleSteps; step++)
        for (int substep = 0; substep < subdivisions; substep++)
        {
            solver.BeforeTransport(wind, dt);
            solver.CompleteStep(wind, forcing, dt);
        }
        return solver.Result(wind);
    }
}

public static partial class WorldGenerator
{
    internal static void CheckHadleyOrography()
    {
        var world = new WorldData(CheckManifest(128, 32, true));
        for (int y = 0; y < world.Height; y++)
        for (int x = 0; x < world.Width; x++)
        {
            int i = y * world.Width + x;
            int height = Math.Max(0, 2000 - Math.Abs(2 * x - (world.Width - 1)) * 50);
            world.Elevation[i] = (short)height;
            world.Temperature[i] = (short)(250 - height / 10);
        }
        var dry = new HadleyCoupledSettings(InitialRelativeHumidity: 0, InitialSoilWater: 0,
            RecycleSoil: false, DryLapseRate: 10, ThermalRelaxationMultiplier: 0);
        var shortRun = new HadleyMoistureSettings(WarmupSteps: 20, SampleSteps: 40, EvaporationRelaxation: 0);
        var rising = HadleyCoupledClimateSimulation.RunOrographicColumn(world, 1.2f, shortRun, dry);
        for (int i = 0; i < world.Count; i++)
            Require(Math.Abs(rising.Coupled!.LowerTemperature[i] - world.Temperature[i] * 0.1) < 0.001,
                "Dry parcel did not preserve T + lapse*z across the mountain");
        Require(rising.FinalWater == 0 && rising.PrecipitatedWater == 0, "Dry mountain column created water");
        var legacyDry = HadleyCoupledClimateSimulation.RunOrographicColumn(world, 1.2f, shortRun,
            dry with { OrographicThermodynamics = false });
        Require(Math.Abs(legacyDry.Coupled!.LowerTemperature[63] - rising.Coupled!.LowerTemperature[63]) > 1,
            "Mountain thermodynamics ablation had no effect");
        Array.Fill(world.Temperature, (short)250);
        foreach (int cell in new[] { 0, 63 })
        {
            var upward = HadleyCoupledClimateSimulation.CheckOrographicLayerTransfer(world, cell, 0.1f);
            var downward = HadleyCoupledClimateSimulation.CheckOrographicLayerTransfer(world, cell, -0.1f);
            Require(Math.Abs(upward.LowerTemperature[cell] - 25) < 0.001
                && Math.Abs(upward.UpperTemperature[cell] - 19d / 11) < 0.001
                && Math.Abs(downward.LowerTemperature[cell] - 25.6 / 1.1) < 0.001
                && Math.Abs(downward.UpperTemperature[cell]) < 0.001,
                "Layer transfer confused static temperature with actual temperature over the mountain");
        }
        var stationary = HadleyCoupledClimateSimulation.RunOrographicColumn(world, 0, shortRun, dry);
        Require(stationary.Coupled!.LowerTemperature.All(t => Math.Abs(t - 25) < 0.001),
            "Stationary air cooled merely because a mountain was present");

        for (int y = 0; y < world.Height; y++)
        for (int x = 0; x < world.Width; x++)
        {
            int i = y * world.Width + x;
            if (x < 24 || x >= 104) { world.Water[i] = WaterType.Ocean; world.Elevation[i] = -1000; }
            world.Temperature[i] = (short)Math.Round((25 - Math.Max(0, (int)world.Elevation[i]) * 0.0065) * 10);
        }
        var wetSettings = new HadleyMoistureSettings(WarmupSteps: 240, SampleSteps: 240);
        var wetCoupling = dry with { InitialRelativeHumidity = 0.55, DryLapseRate = 9.8 };
        var east = HadleyCoupledClimateSimulation.RunOrographicColumn(world, 1.2f, wetSettings, wetCoupling);
        var west = HadleyCoupledClimateSimulation.RunOrographicColumn(world, -1.2f, wetSettings, wetCoupling);
        var legacy = HadleyCoupledClimateSimulation.RunOrographicColumn(world, 1.2f, wetSettings,
            wetCoupling with { OrographicThermodynamics = false });
        double windward = Enumerable.Range(44, 20).Average(x => east.AnnualRain[x]);
        double leeward = Enumerable.Range(64, 20).Average(x => east.AnnualRain[x]);
        Require(windward > 0 && windward > leeward * 1.2, "Controlled mountain did not produce a rain shadow");
        for (int x = 0; x < world.Width; x++)
            Require(Math.Abs(east.AnnualRain[x] - west.AnnualRain[world.Width - 1 - x]) < 0.001 * Math.Max(1, east.AnnualRain[x]),
                "Rain shadow did not reverse with the prevailing wind");
        Require(east.RelativeBudgetError < 1e-5 && west.RelativeBudgetError < 1e-5
            && east.Coupled!.TemperatureLimitApplications == 0 && west.Coupled!.TemperatureLimitApplications == 0,
            "Mountain column violated conservation or thermal stability");
        Require(!east.AnnualRain.SequenceEqual(legacy.AnnualRain) && legacy.RelativeBudgetError < 1e-5,
            "Detailed mountain model did not differ from the instantaneous cooling baseline");
        double oldWindward = Enumerable.Range(44, 20).Average(x => legacy.AnnualRain[x]);
        double oldLeeward = Enumerable.Range(64, 20).Average(x => legacy.AnnualRain[x]);
        System.IO.Directory.CreateDirectory("results/hadley-orographic-column");
        System.IO.File.WriteAllText("results/hadley-orographic-column/summary.json", System.Text.Json.JsonSerializer.Serialize(new
        {
            world.Width, world.Height, settings = wetSettings, coupling = wetCoupling,
            speed = 1.2, WindwardColumns = new[] { 44, 63 }, LeewardColumns = new[] { 64, 83 },
            TerrainMetres = world.Elevation.Take(world.Width).ToArray(),
            EastRain = east.AnnualRain.Take(world.Width).ToArray(), WestRain = west.AnnualRain.Take(world.Width).ToArray(),
            LegacyRain = legacy.AnnualRain.Take(world.Width).ToArray(),
            EastTemperature = east.Coupled!.LowerTemperature.Take(world.Width).ToArray(),
            LegacyTemperature = legacy.Coupled!.LowerTemperature.Take(world.Width).ToArray(),
            WindwardMeanRain = windward, LeewardMeanRain = leeward,
            LegacyWindwardMeanRain = oldWindward, LegacyLeewardMeanRain = oldLeeward,
            east.RelativeBudgetError, ReverseBudgetError = west.RelativeBudgetError,
            LegacyBudgetError = legacy.RelativeBudgetError
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS Hadley orography: dry ascent/descent, thermal memory, stationary air, layer transfer, ablation, reversed winds, conserved water; windward/leeward={windward:F2}/{leeward:F2}, ratio={windward / leeward:F2}; legacy={oldWindward:F2}/{oldLeeward:F2}");
    }
}
