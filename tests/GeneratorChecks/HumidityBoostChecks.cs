using System;
using System.IO;
using System.Linq;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    internal static void CheckHumidityBoost()
    {
        foreach (var (width, height) in new[] { (128, 64), (256, 128), (512, 256), (1024, 512), (2048, 1024), (4096, 2048) })
        foreach (bool atlas in new[] { false, true })
        {
            var expanded = MapDomain.ExpandedSize(width, height);
            var manifest = CheckManifest(atlas ? expanded.Width : width, atlas ? expanded.Height : height, false) with
            {
                GeneratorVersion = HumidityBoostVersion,
                Generation = new(TerrainAlgorithm.ContinentalSculpt, 3, 25, 80, GenerationStage.Terrain,
                    atlas ? MapExtent.World : MapExtent.Region, ReferenceWidth: atlas ? width : 0, ReferenceHeight: atlas ? height : 0)
            };
            WorldData.Validate(manifest);
            var revised = HadleyCoupledClimateSimulation.CouplingFor(manifest);
            var previous = HadleyCoupledClimateSimulation.CouplingFor(manifest with { GeneratorVersion = RainRecyclingVersion });
            Require(revised.VaporCapacityMultiplier == 1.95
                && revised == previous with { VaporCapacityMultiplier = 1.95 },
                "Humidity boost depends on map size or changed unrelated climate settings");
            foreach (var surface in new[] { (1f, 0f, 0f, 0f), (0f, 1f, 0f, 0f), (0f, 0f, 1f, 0f), (0f, 0f, 0f, 1f) })
            {
                double oldCapacity = 45, newCapacity = 58.5;
                double before = HadleyCoupledClimateSimulation.AquaticEvaporation(oldCapacity, oldCapacity * 0.55,
                    surface.Item1, surface.Item2, surface.Item3, surface.Item4, 0.18f, 0.25f, previous);
                double after = HadleyCoupledClimateSimulation.AquaticEvaporation(newCapacity, newCapacity * 0.55,
                    surface.Item1, surface.Item2, surface.Item3, surface.Item4, 0.18f, 0.25f, revised);
                Require(Math.Abs(after / before - 1.3) < 1e-12, "Aquatic vapor supply did not increase by 30% at equal relative humidity");
            }
        }
        var manifestSmall = CheckManifest(128, 64, true) with { GeneratorVersion = HumidityBoostVersion };
        var wind = new HadleyWindSettings(Cells: new AtmosphericCellSettings());
        var duration = new HadleyMoistureSettings(WarmupSteps: 0, SampleSteps: 1);
        foreach (short temperature in new short[] { -300, 0, 250, 450 })
        {
            var ocean = new WorldData(manifestSmall);
            Array.Fill(ocean.Water, WaterType.Ocean); Array.Fill(ocean.Temperature, temperature);
            var boosted = HadleyCoupledClimateSimulation.Simulate(ocean, wind, duration);
            var control = HadleyCoupledClimateSimulation.Simulate(ocean, wind, duration,
                coupling: HadleyCoupledClimateSimulation.CouplingFor(manifestSmall with { GeneratorVersion = RainRecyclingVersion }));
            Require(Math.Abs(boosted.Moisture.InitialWater / control.Moisture.InitialWater - 1.3) < 1e-10,
                "Vapor storage did not rise by 30% in both layers at every temperature");
        }
        var coast = new WorldData(manifestSmall);
        Array.Fill(coast.Temperature, (short)250);
        for (int y = 0; y < coast.Height; y++) for (int x = 0; x < coast.Width / 4; x++) coast.Water[y * coast.Width + x] = WaterType.Ocean;
        duration = new(WarmupSteps: 120, SampleSteps: 120);
        var a = HadleyCoupledClimateSimulation.Simulate(coast, wind, duration);
        var b = HadleyCoupledClimateSimulation.Simulate(coast, wind, duration);
        Require(a.Moisture.RelativeBudgetError < 1e-5 && a.Moisture.AnnualRain.SequenceEqual(b.Moisture.AnnualRain)
            && a.Moisture.Coupled!.Settings.RainReturnShare == 0.25, "v70 violated conservation, determinism or rain return");
        var empty = HadleyCoupledClimateSimulation.Simulate(coast, wind, duration with { EvaporationRelaxation = 0 },
            coupling: a.Moisture.Coupled!.Settings with { InitialRelativeHumidity = 0, InitialSoilWater = 0 });
        Require(empty.Moisture.InitialWater == 0 && empty.Moisture.FinalWater == 0 && empty.Moisture.PrecipitatedWater == 0,
            "Boosted climate created water without a source");
        var legacyWorld = new WorldData(manifestSmall with { GeneratorVersion = RainRecyclingVersion });
        Array.Copy(coast.Water, legacyWorld.Water, coast.Count); Array.Copy(coast.Temperature, legacyWorld.Temperature, coast.Count);
        var legacy = HadleyCoupledClimateSimulation.Simulate(legacyWorld, wind, duration);
        var explicitLegacy = HadleyCoupledClimateSimulation.Simulate(coast, wind, duration,
            coupling: a.Moisture.Coupled.Settings with { VaporCapacityMultiplier = 1.5 });
        Require(legacy.Moisture.AnnualRain.SequenceEqual(explicitLegacy.Moisture.AnnualRain), "v69 changed retroactively");
        var generated = Create(CheckManifest(96, 64, false) with { GeneratorVersion = HumidityBoostVersion });
        string path = Path.Combine(Path.GetTempPath(), $"humidity-boost-{Guid.NewGuid():N}.world");
        try
        {
            WorldFile.Save(path, generated, null); var loaded = WorldFile.Load(path);
            RegenerateRegion(loaded, new WorldRegion(0, 0, loaded.Width, loaded.Height));
            Require(loaded.Manifest.GeneratorVersion == HumidityBoostVersion && loaded.Rainfall.SequenceEqual(generated.Rainfall)
                && loaded.Biomes.SequenceEqual(generated.Biomes), "v70 save/load or regeneration changed the seeded climate");
        }
        finally { File.Delete(path); }
        Console.WriteLine($"PASS humidity boost: all 6 sizes, World/Region, vapor storage and supply +30%, both layers, 4 temperatures, no double rate boost, water budget, empty system, determinism, v69 isolation, v70 creation/save/load/regeneration; error {a.Moisture.RelativeBudgetError:E3}");
    }
}
