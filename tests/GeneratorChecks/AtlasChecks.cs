using System;
using System.IO;
using System.Linq;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    internal static void CheckFullAtlas()
    {
        foreach (int seed in new[] { 1234, 1, -77 })
        foreach (int continents in new[] { 3, 12 })
        foreach (bool wrap in new[] { false, true })
        foreach (int landPercent in new[] { 10, 38, 65 })
        {
            var size = MapDomain.ExpandedSize(128, 64);
            var m = CheckManifest(size.Width, size.Height, wrap) with { Seed = seed };
            m = m with { Generation = m.Generation with { Extent = MapExtent.World,
                ReferenceWidth = 128, ReferenceHeight = 64, LandPercent = landPercent, Continents = continents } };
            var world = Create(m);
            int target = world.Domain.PercentageAreaCount * landPercent / 100;
            var seen = new bool[world.Count];
            var queue = new System.Collections.Generic.Queue<int>();
            int major = 0;
            for (int start = 0; start < world.Count; start++)
            {
                if (seen[start] || world.Water[start] != WaterType.None) continue;
                int count = 0; seen[start] = true; queue.Enqueue(start);
                while (queue.TryDequeue(out int i))
                {
                    count++; var hex = HexGrid.FromOffset(i % world.Width, i / world.Width);
                    for (int d = 0; d < 6; d++)
                    {
                        int n = HexGrid.NeighborIndex(hex, d, world.Width, world.Height, wrap);
                        if (n < 0 || seen[n] || world.Water[n] != WaterType.None) continue;
                        seen[n] = true; queue.Enqueue(n);
                    }
                }
                if (count >= Math.Max(3, target / (continents * 12))) major++;
            }
            if (major < continents) throw new Exception($"Curved atlas lacks continents: seed={seed}, land={landPercent}, {major}/{continents}.");
            if (!Enumerable.Range(0, world.Count).Any(i => world.Domain.Contains(i) &&
                (i / world.Width < 16 || i / world.Width >= world.Height - 16 || i % world.Width < 32 || i % world.Width >= world.Width - 32) && world.Rainfall[i] > 0))
                throw new Exception("The enlarged area did not receive real climate calculations.");
            if (world.ActiveCount <= 128 * 64 || world.ActiveCount >= world.Count) throw new Exception("Atlas was not enlarged and curved.");
            if (world.Water.Count(w => w == WaterType.None) != target) throw new Exception($"Atlas land area differs: {world.Water.Count(w => w == WaterType.None)}/{target}");
            for (int i = 0; i < world.Count; i++)
                if (!world.Domain.Contains(i) && (world.Water[i] != WaterType.Ocean || world.Biomes[i] != BiomeType.None))
                    throw new Exception("Unused corner participated in land/biome generation.");
            if (!world.Tectonics!.Plates.All(p => world.Domain.ContainsPoint(new(p.X, p.Y))) ||
                !world.Tectonics.Contacts.All(c => world.Domain.ContainsPoint(new(c.X, c.Y)) && world.Domain.ContainsPoint(new(c.EndX, c.EndY))))
                throw new Exception("Tectonics extends outside the real atlas domain.");
            foreach (var river in ReadRivers(world))
                if (!river.Path.All(i => world.Domain.Contains(i))) throw new Exception("River ended outside the atlas footprint.");
            var repeated = Create(m);
            if (!world.Elevation.SequenceEqual(repeated.Elevation) || !world.Biomes.SequenceEqual(repeated.Biomes))
                throw new Exception("Full atlas is not deterministic.");
            var original = (BiomeType[])repeated.Biomes.Clone();
            RegenerateRegion(repeated, new(0, 0, 40, 30), RegenerationMode.Everything, HexLocks.Biome);
            if (!original.SequenceEqual(repeated.Biomes)) throw new Exception("Regenerating the curved edge changed the atlas.");
            var region = Create(m with { Generation = m.Generation with { Extent = MapExtent.Region, ReferenceWidth = 0, ReferenceHeight = 0 } });
            if (region.ActiveCount != region.Count) throw new Exception("Region stopped being rectangular.");
            if (world.Water.SequenceEqual(region.Water)) throw new Exception("Atlas is only a visual clip of a regional map.");
            string path = Path.Combine(Path.GetTempPath(), $"atlas-{Guid.NewGuid():N}.world");
            try
            {
                WorldFile.Save(path, world, null);
                var loaded = WorldFile.Load(path);
                if (loaded.ActiveCount != world.ActiveCount || !loaded.Water.SequenceEqual(world.Water)) throw new Exception("Atlas domain lost on save/load.");
            }
            finally { File.Delete(path); }
        }
        var max = MapDomain.ExpandedSize(4096, 2048);
        var manifest = CheckManifest(max.Width, max.Height, false);
        manifest = manifest with { Generation = manifest.Generation with { Extent = MapExtent.World, ReferenceWidth = 4096, ReferenceHeight = 2048 } };
        WorldData.Validate(manifest);
        if (max.ActiveHexes <= 8_000_000) throw new Exception("Largest atlas should trigger the 4000x2000 warning.");
        Console.WriteLine($"PASS: full atlas geometry, quotas, tectonics, rivers, determinism, curved regeneration, save/load; maximum {max.Width}x{max.Height}, {max.ActiveHexes:N0} real hexes");
    }
}
