using System;
using System.Linq;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    internal static void CheckBiomeMajority()
    {
        foreach (bool wrap in new[] { false, true })
        {
            var world = new WorldData(CheckManifest(128, 64, wrap));
            Array.Fill(world.Biomes, BiomeType.Savanna); Array.Fill(world.Temperature, (short)225);
            Array.Fill(world.Rainfall, (ushort)450);
            for (int y = 29; y <= 33; y++)
            for (int x = 0; x < world.Width; x++) world.Biomes[y * world.Width + x] = BiomeType.Steppe;
            int locked = 31 * 128 + 64;
            world.LockMask[locked] = (ushort)HexLocks.Biome;
            var original = (BiomeType[])world.Biomes.Clone();
            var water = (WaterType[])world.Water.Clone();
            PropagateBiomeMajority(new GenerationArea(world, new(0, 0, 128, 64), useWorldArrays: true), new bool[world.Count]);
            int removed = world.Biomes.Where((b, i) => b != original[i]).Count();
            if (removed < 450 || world.Biomes[locked] != BiomeType.Steppe || !water.SequenceEqual(world.Water))
                throw new Exception($"Regional majority did not absorb the long stripe, or ignored locks/water: {removed}; rows={string.Join(',', Enumerable.Range(29, 5).Select(y => Enumerable.Range(y * 128, 128).Count(i => world.Biomes[i] == BiomeType.Steppe)))}.");

            // A dry strip belongs to a different climate even when its
            // surrounding biome wins every vote.
            original.CopyTo(world.Biomes, 0); Array.Fill(world.Rainfall, (ushort)200);
            PropagateBiomeMajority(new GenerationArea(world, new(0, 0, 128, 64), useWorldArrays: true), new bool[world.Count]);
            if (!original.SequenceEqual(world.Biomes)) throw new Exception("Majority crossed an arid climate boundary.");
            foreach (var type in new[] { BiomeType.Beach, BiomeType.Alpine, BiomeType.Swamp })
            {
                Array.Fill(world.Rainfall, (ushort)450);
                for (int x = 0; x < 128; x++) world.Biomes[31 * 128 + x] = type;
                var before = (BiomeType[])world.Biomes.Clone();
                PropagateBiomeMajority(new GenerationArea(world, new(0, 0, 128, 64), useWorldArrays: true), new bool[world.Count]);
                for (int x = 0; x < 128; x++) if (world.Biomes[31 * 128 + x] != type)
                    throw new Exception("Majority erased a protected habitat.");
            }
            Console.WriteLine($"PASS: biome majority wrap={wrap}, absorbed {removed}/640 stripe cells; locks, water, arid climate and protected habitats preserved");
        }
    }
}
