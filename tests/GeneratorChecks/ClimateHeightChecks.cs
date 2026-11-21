using System;
using System.Linq;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    internal static void CheckClimateAndHeights()
    {
        foreach (var (latitude, expected) in new[] { (0f, 265f), (0.25f, 221f), (0.5f, 111f), (0.75f, -56f), (1f, -280f) })
        {
            float actual = ModernSeaTemperature(latitude);
            if (Math.Abs(actual - expected) > 5) throw new Exception($"Latitude temperature mismatch: {latitude}/{actual}.");
            Console.WriteLine($"CLIMATE sea level latitude={latitude * 90:F1}°: {actual / 10:F1} C");
        }
        var low = Climate(0.4f, 0.5f, 2, 0, 0, 1234, 43);
        var high = Climate(0.4f, 0.5f, 2, 1000, 0, 1234, 43);
        if (low.Temperature - high.Temperature != 65) throw new Exception("Lapse rate is not 6.5 C/km.");
        if (ChooseBiome(100, 300, 100) != BiomeType.Desert || ChooseBiome(100, 300, 2000) != BiomeType.Rainforest)
            throw new Exception("Hot biomes ignore water availability.");
        double MeanRain(float latitude) => Enumerable.Range(0, 256).Average(x => (double)RegionalRainBaseline(x / 256f,
            0.5f + latitude / 2, 2, 0, 1234, true, 43));
        if (MeanRain(0.28f) >= MeanRain(0) * 0.4 || MeanRain(0.28f) >= MeanRain(0.54f) * 0.5 || MeanRain(1) >= MeanRain(0.54f) * 0.3)
            throw new Exception("Subtropical/polar dry belts did not form.");
        var heights = new short[512]; var water = new WaterType[512]; var old = new ushort[512]; var modern = new ushort[512];
        for (int x = 0; x < 512; x++)
        { water[x] = x < 80 ? WaterType.Ocean : WaterType.None; heights[x] = (short)(x is >= 200 and <= 230 ? 3500 : 100); }
        TransportMoistureRow(heights, water, old, 512, false, 1, 30);
        TransportMoistureRow(heights, water, modern, 512, false, 1, 30, true);
        if (modern[240] >= old[240] || modern[240] >= modern[190] || modern[450] >= old[450])
            throw new Exception("Rain shadow / continental drying failed.");
        var inactiveHeights = new short[] { -3000, 100, 400, 1200, 2400 };
        var inactiveCopy = (short[])inactiveHeights.Clone();
        var inactiveWorld = new WorldData(CheckManifest(32, 32, false));
        CalibrateTectonicSummits(inactiveHeights, new float[inactiveHeights.Length], inactiveWorld.Domain, 1234);
        if (!inactiveHeights.SequenceEqual(inactiveCopy)) throw new Exception("Summit calibration created relief without tectonic uplift.");

        foreach (int seed in new[] { 1234, 1, -77 })
        {
            var manifest = CheckManifest(512, 256, true) with { Seed = seed, GeneratorVersion = 43 };
            var before = Create(manifest with { GeneratorVersion = 42 });
            var world = Create(manifest);
            int land = world.Water.Count(v => v == WaterType.None);
            int deserts = world.Biomes.Count(v => v == BiomeType.Desert);
            int extreme = world.Elevation.Count(v => v >= 6000);
            Console.WriteLine($"CLIMATE HEIGHT seed={seed}: max {before.Elevation.Max()} -> {world.Elevation.Max()} m, >=6000={extreme}/{land}, desert={before.Biomes.Count(v => v == BiomeType.Desert)} -> {deserts}, T={world.Temperature.Min() / 10f:F1}..{world.Temperature.Max() / 10f:F1} C");
            if (!before.Water.SequenceEqual(world.Water)) throw new Exception("Climate/height update changed land or water quota.");
            for (int i = 0; i < world.Count; i++)
                if ((before.Elevation[i] <= 800 || world.Water[i] != WaterType.None) && world.Elevation[i] != before.Elevation[i])
                    throw new Exception("Summit calibration changed lowlands or water depths.");
            if (world.Elevation.Max() is < 6000 or > 8000 || extreme > land / 50)
                throw new Exception("Summit heights or rarity out of range.");
            if (deserts < land / 100) throw new Exception("No meaningful desert areas formed.");
            var second = Create(manifest);
            if (!world.Elevation.SequenceEqual(second.Elevation) || !world.Temperature.SequenceEqual(second.Temperature) ||
                !world.Rainfall.SequenceEqual(second.Rainfall) || !world.Biomes.SequenceEqual(second.Biomes))
                throw new Exception("Climate or summits are not deterministic.");
            RegenerateRegion(second, new(99, 65, 33, 40), RegenerationMode.Everything);
            if (!world.Elevation.SequenceEqual(second.Elevation) || !world.Temperature.SequenceEqual(second.Temperature) ||
                !world.Rainfall.SequenceEqual(second.Rainfall) || !world.Biomes.SequenceEqual(second.Biomes))
                throw new Exception("Regional climate/height regeneration changed its seam.");
        }
        Console.WriteLine("PASS: latitude curve, lapse rate, dry belts, rain shadow, humid tropics, rare 6000..8000 m summits, unchanged water, deterministic climate and regional regeneration");
    }
}
