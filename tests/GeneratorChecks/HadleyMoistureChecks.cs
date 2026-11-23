using System;
using System.Linq;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    internal static void CheckHadleyMoisture()
    {
        CheckHadleyEvaporation();
        CheckHadleyOrography();
        var manifest = CheckManifest(96, 64, false);
        var ocean = new WorldData(manifest);
        Array.Fill(ocean.Water, WaterType.Ocean); Array.Fill(ocean.Temperature, (short)250);
        var wind = HadleyWindSimulation.Simulate(ocean, new HadleyWindSettings(PressureForce: 0,
            ConvectiveLift: 0, UseZonalPressureBands: false));
        Require(wind.Pressure[21 * wind.GridWidth] < wind.Pressure[10 * wind.GridWidth],
            "Experimental forcing retained a subtropical pressure maximum");
        var shortRun = new HadleyMoistureSettings(WarmupSteps: 40, SampleSteps: 40);
        var still = HadleyMoistureSimulation.Simulate(ocean, wind, shortRun);
        Require(still.AnnualRain.All(value => value == 0), "Still unsaturated air produced rain");
        Array.Fill(wind.VerticalFlow, 0.05f);
        var rising = HadleyMoistureSimulation.Simulate(ocean, wind, shortRun);
        Require(rising.AnnualRain[0] > 0 && rising.AnnualRain.All(value => value == rising.AnnualRain[0]),
            "Uniform temperature/water/ascent acquired a latitude rainfall mask");
        Require(rising.RelativeBudgetError < 1e-5, "Water budget failed");
        var repeat = HadleyMoistureSimulation.Simulate(ocean, wind, shortRun);
        Require(rising.AnnualRain.SequenceEqual(repeat.AnnualRain), "Moisture transport is nondeterministic");
        var noSource = HadleyMoistureSimulation.Simulate(ocean, wind, shortRun with { EvaporationRelaxation = 0 });
        Require(noSource.EvaporatedWater == 0 && noSource.PrecipitatedWater <= noSource.InitialWater * 1.00001
            && noSource.RelativeBudgetError < 1e-5, "Water appeared without evaporation");

        var original = Create(manifest);
        var coupledSettings = new HadleyWindSettings(MaxGridWidth: 64, MaxGridHeight: 32, UseZonalPressureBands: false);
        var coupled = HadleyCoupledClimateSimulation.Simulate(ocean, coupledSettings, shortRun);
        var coupledRepeat = HadleyCoupledClimateSimulation.Simulate(ocean, coupledSettings, shortRun);
        Require(coupled.Moisture.AnnualRain.SequenceEqual(coupledRepeat.Moisture.AnnualRain)
            && coupled.Wind.Vx.SequenceEqual(coupledRepeat.Wind.Vx), "Coupled climate is nondeterministic");
        var diagnostic = coupled.Moisture.Coupled!;
        Require(diagnostic.TemperatureChangeRms > 0 && diagnostic.CloudWater > 0
            && coupled.Moisture.RelativeBudgetError < 1e-5, "Coupled temperature/clouds did not evolve");
        Require(diagnostic.TemperatureLimitApplications == 0, "Coupled heat needed emergency clamping");
        var fixedHeat = HadleyCoupledClimateSimulation.Simulate(ocean, coupledSettings, shortRun,
            coupling: new HadleyCoupledSettings(EvolveTemperature: false));
        Require(fixedHeat.Moisture.Coupled!.TemperatureChangeRms < 1e-4
            && !fixedHeat.Wind.Vy.SequenceEqual(coupled.Wind.Vy), "Thermal feedback did not change circulation");
        var fastRain = HadleyCoupledClimateSimulation.Simulate(ocean, coupledSettings, shortRun,
            coupling: new HadleyCoupledSettings(CloudRainTime: 0.1f));
        Require(fastRain.Moisture.Coupled!.CloudWater < diagnostic.CloudWater,
            "Cloud residence time did not retain condensate");
        Require(Math.Abs(coupled.Moisture.InitialWater + diagnostic.OceanEvaporation - diagnostic.Runoff
            - coupled.Moisture.FinalWater) < 1e-5 * (coupled.Moisture.InitialWater + diagnostic.OceanEvaporation),
            "Coupled budget treated precipitation as an external loss");
        var dryWorld = new WorldData(manifest);
        Array.Fill(dryWorld.Temperature, (short)250);
        var recycled = HadleyCoupledClimateSimulation.Simulate(dryWorld, coupledSettings,
            shortRun with { EvaporationRelaxation = 0 });
        Require(recycled.Moisture.Coupled!.OceanEvaporation == 0 && recycled.Moisture.Coupled.SoilEvaporation > 0
            && recycled.Moisture.FinalWater <= recycled.Moisture.InitialWater * 1.00001,
            "Soil recycling created water or failed to evaporate stored water");
        var noRecycling = HadleyCoupledClimateSimulation.Simulate(dryWorld, coupledSettings, shortRun,
            coupling: new HadleyCoupledSettings(RecycleSoil: false));
        Require(noRecycling.Moisture.Coupled!.SoilEvaporation == 0
            && noRecycling.Moisture.Coupled.SoilWater > recycled.Moisture.Coupled.SoilWater,
            "Soil ablation did not disable recycling");
        var emptyWater = new HadleyCoupledSettings(InitialRelativeHumidity: 0, InitialSoilWater: 0);
        var empty = HadleyCoupledClimateSimulation.Simulate(dryWorld, coupledSettings, shortRun, coupling: emptyWater);
        Require(empty.Moisture.InitialWater == 0 && empty.Moisture.FinalWater == 0
            && empty.Moisture.PrecipitatedWater == 0, "Dry closed atmosphere created water");
        var coast = new WorldData(manifest);
        Array.Fill(coast.Temperature, (short)250);
        for (int row = 0; row < coast.Height; row++)
        for (int col = 0; col < coast.Width / 4; col++) coast.Water[row * coast.Width + col] = WaterType.Ocean;
        var marine = HadleyCoupledClimateSimulation.Simulate(coast, coupledSettings, shortRun, coupling: emptyWater);
        Require(marine.Moisture.InitialWater == 0 && marine.Moisture.EvaporatedWater > 0
            && marine.Moisture.Coupled!.Vapor.Where((_, i) => i % marine.Wind.GridWidth > marine.Wind.GridWidth / 4 + 1).Any(v => v > 0),
            "Ocean-origin moisture did not cross into initially dry land");
        Require(coupled.Wind.LowerMass.All(value => float.IsFinite(value) && value > 0)
            && Math.Abs(coupled.Wind.LowerMass.Sum(value => (double)value)
                + coupled.Wind.UpperMass.Sum(value => (double)value) - 2 * coupled.Wind.LowerMass.Length)
                / (2 * coupled.Wind.LowerMass.Length) < 1e-5, "Coupled air mass is not conserved");
        using (var cancelled = new System.Threading.CancellationTokenSource())
        {
            cancelled.Cancel(); bool observed = false;
            try { HadleyCoupledClimateSimulation.Simulate(ocean, coupledSettings, cancellationToken: cancelled.Token); }
            catch (OperationCanceledException) { observed = true; }
            Require(observed, "Coupled climate ignored cancellation");
        }
        float target = (float)Enumerable.Range(0, original.Count).Where(i => original.Domain.Contains(i)).Average(i => (double)original.Rainfall[i]);
        var experiment = CreateHadleyClimateExperiment(manifest, referenceGlobalMeanRain: target);
        var changed = experiment.World;
        Require(!experiment.Wind.Settings.UseZonalPressureBands && experiment.Moisture.RelativeBudgetError < 1e-5,
            "Experiment enabled pressure bands or broke the water budget");
        Require(original.Elevation.SequenceEqual(changed.Elevation) && original.Water.SequenceEqual(changed.Water)
            && original.Relief.SequenceEqual(changed.Relief) && original.Landforms.SequenceEqual(changed.Landforms)
            && original.Temperature.SequenceEqual(changed.Temperature), "Climate comparison changed terrain or temperature");
        Require(!original.Rainfall.SequenceEqual(changed.Rainfall), "Experimental rain never reached world generation");
        Require(experiment.GlobalRainScale > 0 && double.IsFinite(experiment.GlobalRainScale), "Invalid global intensity conversion");
        var fixedConversion = CreateHadleyClimateExperiment(manifest, fixedRainScale: experiment.GlobalRainScale);
        Require(fixedConversion.GlobalRainScale == experiment.GlobalRainScale
            && fixedConversion.ReferenceGlobalMeanRain is null
            && fixedConversion.World.Rainfall.SequenceEqual(experiment.World.Rainfall),
            "Fixed rain conversion was normalized again or changed the resulting rain");
        bool conflictingScaleRejected = false;
        try { CreateHadleyClimateExperiment(manifest, referenceGlobalMeanRain: target, fixedRainScale: 1); }
        catch (ArgumentOutOfRangeException) { conflictingScaleRejected = true; }
        Require(conflictingScaleRejected, "Conflicting rain conversion modes were accepted");
        var largeOcean = new WorldData(CheckManifest(512, 256, true));
        Array.Fill(largeOcean.Water, WaterType.Ocean); Array.Fill(largeOcean.Temperature, (short)250);
        var large = HadleyCoupledClimateSimulation.Simulate(largeOcean,
            new HadleyWindSettings(MaxGridWidth: 512, MaxGridHeight: 256),
            new HadleyMoistureSettings(WarmupSteps: 4, SampleSteps: 4));
        Require(large.Moisture.RelativeBudgetError < 1e-5 && large.Moisture.AnnualRain.All(float.IsFinite)
            && large.Moisture.Coupled!.TemperatureLimitApplications == 0, "512x256 coupled grid is unstable");
        var regularAgain = Create(manifest);
        Require(original.Rainfall.SequenceEqual(regularAgain.Rainfall) && original.Biomes.SequenceEqual(regularAgain.Biomes),
            "Experimental climate leaked into normal generation");
        Console.WriteLine("PASS Hadley climate: frozen-column baseline; coupled heat/cloud/soil ablations; dry-source and ocean-to-land transport; conservative water/air; determinism; cancellation; 512x256 stability; controlled terrain/annual temperature; normal generation isolated");
    }

    private static void CheckHadleyEvaporation()
    {
        var settings = new HadleyCoupledSettings();
        foreach (var source in new[] { (WaterType.Ocean, -1000, 0.90, 1d),
            (WaterType.Ocean, -200, 0.95, 1d), (WaterType.Lake, -20, 0.85, 0.6),
            (WaterType.Wetland, 0, 0.80, 0.4) })
        {
            float deep = source.Item1 == WaterType.Ocean && source.Item2 < -200 ? 1 : 0;
            float shallow = source.Item1 == WaterType.Ocean && source.Item2 >= -200 ? 1 : 0;
            float lake = source.Item1 == WaterType.Lake ? 1 : 0;
            float wetland = source.Item1 == WaterType.Wetland ? 1 : 0;
            double At(double vapor, HadleyCoupledSettings? config = null) => HadleyCoupledClimateSimulation.AquaticEvaporation(
                100, vapor, deep, shallow, lake, wetland, 0.18f, 0.25f, config ?? settings);
            double cutoff = source.Item3 * 100;
            Require(At(cutoff) == 0 && At(cutoff + 1) == 0 && At(cutoff - 1) > 0,
                "Aquatic humidity cutoff does not match the surface");
            Require(Math.Abs(At(50) - (cutoff - 50) * source.Item4 * 0.36 * 0.25) < 1e-6,
                "Aquatic replenishment did not double");
            Require(At(50) == 2 * At(50, settings with { AquaticReplenishmentMultiplier = 1 }),
                "Aquatic strength override is ineffective");

            var world = new WorldData(CheckManifest(32, 32, false));
            Array.Fill(world.Water, source.Item1); Array.Fill(world.Elevation, (short)source.Item2);
            Array.Fill(world.Temperature, (short)250);
            var result = HadleyCoupledClimateSimulation.Simulate(world,
                new HadleyWindSettings(MaxGridWidth: 32, MaxGridHeight: 16, PressureForce: 0, ConvectiveLift: 0),
                new HadleyMoistureSettings(WarmupSteps: 0, SampleSteps: 1),
                coupling: settings with { EvolveTemperature = false, InitialRelativeHumidity = 0.5, InitialSoilWater = 0 });
            double expected = (30 * source.Item3 - 15) * source.Item4 * 0.36 * 0.25 * 32 * 16;
            Require(Math.Abs(result.Moisture.EvaporatedWater - expected) < expected * 1e-6,
                "Terrain sampling selected the wrong evaporation source");
            Require(result.Moisture.RelativeBudgetError < 1e-5, "Aquatic replenishment broke conservation");
        }
        double mixed = HadleyCoupledClimateSimulation.AquaticEvaporation(100, 92, 0.5f, 0.5f,
            0, 0, 0.18f, 0.25f, settings);
        Require(Math.Abs(mixed - 0.5 * (95 - 92) * 0.36 * 0.25) < 1e-7,
            "Deep ocean incorrectly cancelled shallow coast evaporation in a mixed cell");
        Console.WriteLine("PASS Hadley evaporation: 90% ocean, 95% shallow coast (<=200m), 85% lakes, 80% other; 2x aquatic replenishment; mixed-cell cutoffs; sampled terrain; conserved water");
    }
}
