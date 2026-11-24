using System;
using System.IO;
using System.Linq;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    internal static void CheckHadleyMain()
    {
        foreach (var (seed, wrap) in new[] { (1234, false), (-77, true) })
        {
            var legacyManifest = CheckManifest(wrap ? 192 : 96, wrap ? 96 : 64, wrap) with { Seed = seed };
            var manifest = legacyManifest with { GeneratorVersion = HadleyVersion };
            var world = Create(manifest);
            Require(UsesHadleyClimate(world.Manifest) && world.ClimateWind is not null
                && !world.ClimateWind.Settings.UseZonalPressureBands && world.ClimateWind.Settings.Iterations == 480,
                "Normal creation did not use the coupled atmosphere without pressure bands");
            var reference = CreateHadleyClimateExperiment(legacyManifest, fixedRainScale: HadleyRainScale);
            Require(world.Rainfall.SequenceEqual(reference.World.Rainfall) && world.Biomes.SequenceEqual(reference.World.Biomes)
                && world.Temperature.SequenceEqual(reference.World.Temperature) && world.Elevation.SequenceEqual(reference.World.Elevation),
                "Main Hadley recipe differs from the verified climate experiment");
            var legacy = Create(legacyManifest);
            Require(!world.Rainfall.SequenceEqual(legacy.Rainfall), "The main climate fell back to annual rainfall bands");
            if (!wrap)
            {
                var wholeRain = (ushort[])world.Rainfall.Clone();
                var wholeTemperature = (short[])world.Temperature.Clone();
                var wholeElevation = (short[])world.Elevation.Clone();
                RegenerateRegion(world, new WorldRegion(0, 0, world.Width, world.Height));
                Require(world.Rainfall.SequenceEqual(wholeRain) && world.Temperature.SequenceEqual(wholeTemperature)
                    && world.Elevation.SequenceEqual(wholeElevation), "Full Hadley regeneration changed the seeded recipe");
            }

            string path = Path.Combine(Path.GetTempPath(), $"hadley-main-{Guid.NewGuid():N}.world");
            try
            {
                WorldFile.Save(path, world, null);
                var loaded = WorldFile.Load(path);
                Require(UsesHadleyClimate(loaded.Manifest) && loaded.ClimateWind is null
                    && loaded.Rainfall.SequenceEqual(world.Rainfall) && loaded.Biomes.SequenceEqual(world.Biomes),
                    "Saving lost the Hadley recipe or changed the climate");
                var before = (ushort[])loaded.Rainfall.Clone();
                var originalTemperature = (short[])loaded.Temperature.Clone();
                var originalElevation = (short[])loaded.Elevation.Clone();
                var originalBiomes = (BiomeType[])loaded.Biomes.Clone();
                var region = new WorldRegion(0, 16, 8, 8);
                for (int y = region.Row; y < region.Row + region.Height; y++)
                for (int x = region.Column; x < region.Column + region.Width; x++)
                    loaded.Rainfall[y * loaded.Width + x] = 60000;
                int locked = region.Row * loaded.Width + region.Column;
                loaded.Rainfall[locked] = 7777; loaded.LockMask[locked] = (ushort)HexLocks.Rainfall;
                loaded.Rainfall[loaded.Count - 1] = 44444;
                RegenerateRegion(loaded, region, RegenerationMode.Everything, HexLocks.Rainfall);
                for (int i = 0; i < loaded.Count; i++)
                {
                    ushort expected = i == locked ? (ushort)7777 : i == loaded.Count - 1 ? (ushort)44444 : before[i];
                    Require(loaded.Rainfall[i] == expected, "Regional Hadley rain ignored its selection, locks or recipe");
                }
                Require(loaded.Elevation.SequenceEqual(originalElevation) && loaded.Temperature.SequenceEqual(originalTemperature)
                    && loaded.Biomes.SequenceEqual(originalBiomes) && loaded.ClimateWind is { Settings.UseZonalPressureBands: false },
                    "Rain-only Hadley regeneration changed other fields or enabled pressure bands");
            }
            finally { File.Delete(path); }
            Console.WriteLine($"PASS Hadley main: seed={seed}, wrap={wrap}, normal creation, exact diagnostic recipe, legacy isolation, save/load, regional rain regeneration, locks and cached coupled winds");
        }
        var blankManifest = CheckManifest(96, 64, false) with { GeneratorVersion = HadleyVersion };
        var blank = Create(blankManifest with { Generation = blankManifest.Generation with { Stage = GenerationStage.Blank } });
        Require(UsesHadleyClimate(blank.Manifest) && blank.ClimateWind is null, "Blank map lost its future climate recipe");
        bool rejected = false;
        try { Create(blankManifest with { Generation = blankManifest.Generation with { Algorithm = TerrainAlgorithm.Fractal } }); }
        catch (ArgumentException) { rejected = true; }
        Require(rejected, "An incompatible algorithm silently bypassed the Hadley climate");
    }
}
