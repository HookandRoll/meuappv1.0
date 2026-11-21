using System;
using System.Linq;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    internal static void CheckSwampMargins()
    {
        foreach (bool wrap in new[] { false, true })
        {
            var world = new WorldData(CheckManifest(64, 64, wrap));
            Array.Fill(world.Biomes, BiomeType.Grassland); Array.Fill(world.Elevation, (short)100);
            Array.Fill(world.Temperature, (short)180); Array.Fill(world.Rainfall, (ushort)1000);
            for (int y = 5; y < 59; y++)
            for (int x = 29; x <= 34; x++)
            { int i = y * 64 + x; world.Water[i] = WaterType.Lake; world.Elevation[i] = -50; world.Biomes[i] = BiomeType.None; }
            int locked = 30 * 64 + 28; world.LockMask[locked] = (ushort)HexLocks.Biome;
            var water = (WaterType[])world.Water.Clone(); var heights = (short[])world.Elevation.Clone();
            ApplyFluvialBiomes(world, new(0, 0, 64, 64));
            int count = world.Biomes.Count(b => b == BiomeType.Swamp);
            if (count < 30 || world.Biomes[locked] != BiomeType.Grassland || !water.SequenceEqual(world.Water) || !heights.SequenceEqual(world.Elevation))
                throw new Exception("Swamp margins absent, ignored locks, or changed water quota/altitude.");
            foreach (int i in Enumerable.Range(0, world.Count).Where(i => world.Biomes[i] == BiomeType.Swamp))
            {
                world.Temperature[i] = 0; if (IsSwampMargin(world, i)) throw new Exception("Frozen margin became warm swamp.");
                world.Temperature[i] = 180; world.Rainfall[i] = 100;
                if (IsSwampMargin(world, i)) throw new Exception("Arid margin became swamp.");
                world.Rainfall[i] = 1000; world.Elevation[i] = 1500;
                if (IsSwampMargin(world, i)) throw new Exception("High bank became swamp.");
                world.Elevation[i] = 100;
            }
            Console.WriteLine($"PASS: {count} swamp bank cells, wrap={wrap}, shallow/wet/low/warm habitat, water quota, altitude and locks");
            var border = new WorldData(CheckManifest(128, 64, wrap));
            Array.Fill(border.Temperature, (short)150); Array.Fill(border.Rainfall, (ushort)1000);
            for (int i = 0; i < border.Count; i++) border.Biomes[i] = i / 128 < 32 ? BiomeType.TemperateForest : BiomeType.Grassland;
            var previous = (BiomeType[])border.Biomes.Clone();
            border.LockMask[31 * 128 + 32] = (ushort)HexLocks.Biome;
            RoughenBiomeBorders(new GenerationArea(border, new(0, 0, 128, 64), useWorldArrays: true), 1234);
            if (border.Biomes.Where((b, i) => b != previous[i]).Count() < 10 || border.Biomes[31 * 128 + 32] != previous[31 * 128 + 32])
                throw new Exception("Biome boundary noise absent or ignored a lock.");
            var generated = Create(CheckManifest(256, 128, wrap));
            var expected = (BiomeType[])generated.Biomes.Clone();
            foreach (var region in new[] { new WorldRegion(0, 40, 35, 35), new WorldRegion(110, 45, 35, 35), new WorldRegion(230, 90, 26, 30) })
            {
                RegenerateRegion(generated, region, RegenerationMode.Everything, HexLocks.Biome);
                if (!expected.SequenceEqual(generated.Biomes)) throw new Exception("Biome noise/swamp regeneration left a regional seam.");
            }
        }
    }
}
