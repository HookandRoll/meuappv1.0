using System;
using System.IO;
using System.Linq;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    internal static void CheckTerrainRain()
    {
        foreach (bool wrap in new[] { false, true })
        {
            var world = new WorldData(CheckManifest(64, 32, wrap) with { GeneratorVersion = TerrainRainVersion });
            var source = new float[world.Count];
            int origin = 16 * world.Width + (wrap ? 0 : 32);
            var hex = HexGrid.FromOffset(origin % world.Width, origin / world.Width);
            source[origin] = 1000;
            int first = HexGrid.NeighborIndex(hex, 3, world.Width, world.Height, wrap);
            int second = HexGrid.NeighborIndex(hex, 3, world.Width, world.Height, wrap, 2);
            Require(Enumerable.Range(0, world.Count).Count(i => SpreadHexRain(world, source, i) > 0) == 19,
                "Flat terrain changed the nineteen-cell footprint");
            world.Elevation[first] = 500;
            Require(SpreadHexRain(world, source, first) == 150, "Exactly 500m must remain reachable");
            world.Elevation[first] = 501;
            Require(SpreadHexRain(world, source, first) == 0 && SpreadHexRain(world, source, second) == 0,
                "Rain climbed 501m or jumped a ridge to reach the second ring");
            world.Elevation[first] = 400; world.Elevation[second] = 800;
            Require(SpreadHexRain(world, source, second) == 0, "Two small rises exceeded 500m from the origin");
            world.Elevation[first] = -600; world.Elevation[second] = 0;
            Require(SpreadHexRain(world, source, second) == 0, "Rain crossed a 600m rise after descending");
            world.Elevation[first] = -1000; world.Elevation[second] = -1500;
            Require(SpreadHexRain(world, source, first) == 150 && SpreadHexRain(world, source, second) == 50,
                "The elevation gate blocked downhill spreading");
            Array.Fill(world.Elevation, (short)501); world.Elevation[origin] = 0;
            Require(Enumerable.Range(0, world.Count).Sum(i => SpreadHexRain(world, source, i)) == 500,
                "Blocked weights were redistributed or central rain was changed");
            var previous = new WorldData(world.Manifest with { GeneratorVersion = BalancedRainVersion });
            world.Elevation.CopyTo(previous.Elevation, 0);
            Require(SpreadHexRain(previous, source, second) == 50, "v72 no longer preserves its old spread");

            Array.Clear(world.Elevation);
            var between = SecondRainRing[1];
            var (cx, cy) = HexGrid.ToOffset(new Hex(hex.Q + between.Q, hex.R + between.R));
            cx = (cx % world.Width + world.Width) % world.Width;
            int sharedTarget = cy * world.Width + cx;
            int blocked = HexGrid.NeighborIndex(hex, 0, world.Width, world.Height, wrap);
            int alternative = HexGrid.NeighborIndex(hex, 1, world.Width, world.Height, wrap);
            world.Elevation[blocked] = 501;
            Require(SpreadHexRain(world, source, sharedTarget) == 50, "One blocked path hid an open alternative");
            world.Elevation[alternative] = 501;
            Require(SpreadHexRain(world, source, sharedTarget) == 0, "Second-ring rain crossed two blocked paths");
            Require(source[origin] == 1000 && source.Sum() == 1000, "Spread mutated the input field");
        }

        bool foundShrub = false;
        for (int x = 0; x < 64; x++)
        {
            BiomeType Pick(short t, ushort p, int version = TerrainRainVersion) =>
                ChooseExpandedBiome(100, t, p, x, 16, 64, 32, false, 1234, version: version);
            Require(Pick(180, 149) == BiomeType.Desert, "Desert lost its <150mm threshold");
            Require(Pick(180, 150) != BiomeType.Desert, "Desert includes exactly 150mm");
            foundShrub |= Pick(180, 150) == BiomeType.Shrubland;
            Require(Pick(180, 200, BalancedRainVersion) == BiomeType.Desert, "v72 biome recipe changed");
            Require(Pick(199, 800) == BiomeType.Grassland && Pick(200, 800) == BiomeType.Savanna,
                "Plains/savanna boundary does not split at 20C");
            Require(Pick(260, 1200) == BiomeType.SeasonalForest && Pick(260, 1600) == BiomeType.Rainforest,
                "Hot open-land correction displaced forests");
        }
        Require(foundShrub, "Shrubland did not expand into the new 150mm range");
        foreach (var biome in new[] { BiomeType.Desert, BiomeType.Badlands })
        {
            Require(!FitsBiomeClimate(biome, 180, 150, TerrainRainVersion), "Biome smoothing can expand dry biomes beyond 150mm");
            Require(LimitDryBiomeClimate(biome, 180, 150, TerrainRainVersion) == BiomeType.Shrubland,
                "Final biome pass kept an invalid dry biome");
        }
        Require(!FitsBiomeClimate(BiomeType.Grassland, 200, 800, TerrainRainVersion)
            && !FitsBiomeClimate(BiomeType.Savanna, 199, 800, TerrainRainVersion), "Smoothing crossed the temperature boundary");

        foreach (var (seed, wrap) in new[] { (1155827384, false), (-77, true) })
        {
            var world = Create(CheckManifest(96, 64, wrap) with { Seed = seed, GeneratorVersion = TerrainRainVersion });
            for (int i = 0; i < world.Count; i++)
            {
                if (!world.Domain.Contains(i) || world.Water[i] != WaterType.None) continue;
                Require(world.Biomes[i] is not (BiomeType.Desert or BiomeType.Badlands) || world.Rainfall[i] < 150,
                    "Generated dry biome exceeded its rainfall threshold");
                Require(world.Biomes[i] != BiomeType.Grassland || world.Temperature[i] < 200, "Generated hot plains");
                Require(world.Biomes[i] != BiomeType.Savanna || world.Temperature[i] >= 200, "Generated cold savanna");
            }
            string path = Path.Combine(Path.GetTempPath(), $"terrain-rain-{Guid.NewGuid():N}.world");
            try
            {
                WorldFile.Save(path, world, null);
                var loaded = WorldFile.Load(path);
                RegenerateRegion(loaded, new WorldRegion(0, 0, loaded.Width, loaded.Height));
                Require(loaded.Rainfall.SequenceEqual(world.Rainfall) && loaded.Biomes.SequenceEqual(world.Biomes),
                    "v73 save/load/regeneration changed the recipe");
            }
            finally { File.Delete(path); }
        }
        Console.WriteLine("PASS terrain rain: 500m boundary, ridge paths, origin ceiling, downhill, wrap, discarded weights, immutable source, v72 compatibility, dry biomes <150mm, shrubland expansion, 20C plains/savanna, forests, seeded generation and save/load/regeneration");
    }
}
