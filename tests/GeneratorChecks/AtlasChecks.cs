using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    internal static void CheckAtlasSpacing(bool review = false)
    {
        (int Continents, int FacingShore) Measure(WorldData world)
        {
            var labels = new int[world.Count];
            var sizes = new List<int> { 0 };
            var queue = new Queue<int>();
            int Neighbor(int i, int d, int steps = 1) => HexGrid.NeighborIndex(
                HexGrid.FromOffset(i % world.Width, i / world.Width), d, world.Width, world.Height,
                world.Manifest.HorizontalWrap, steps);
            for (int start = 0; start < world.Count; start++)
            {
                if (labels[start] != 0 || world.Water[start] != WaterType.None || !world.Domain.CountsTowardLandQuota(start)) continue;
                int id = sizes.Count, count = 0;
                labels[start] = id; queue.Enqueue(start);
                while (queue.TryDequeue(out int cell))
                {
                    count++;
                    for (int d = 0; d < 6; d++)
                    {
                        int n = Neighbor(cell, d);
                        if (!world.Domain.CountsTowardLandQuota(n) || labels[n] != 0 || world.Water[n] != WaterType.None) continue;
                        labels[n] = id; queue.Enqueue(n);
                    }
                }
                sizes.Add(count);
            }
            int threshold = Math.Max(32, sizes.Sum() / 20), facing = 0;
            for (int cell = 0; cell < labels.Length; cell++)
            {
                int id = labels[cell];
                if (sizes[id] < threshold) continue;
                bool close = false;
                for (int d = 0; d < 6 && !close; d++)
                for (int step = 1; step <= Math.Max(3, world.Height / 32); step++)
                {
                    int n = Neighbor(cell, d, step);
                    if (!world.Domain.Contains(n)) break;
                    int other = labels[n];
                    if (other != 0 && other != id && sizes[other] >= threshold) close = true;
                    if (world.Water[n] != WaterType.Ocean) break;
                }
                if (close) facing++;
            }
            return (sizes.Count(s => s >= threshold), facing);
        }

        int before = 0, after = 0;
        foreach (int seed in new[] { 1234, 1, -77, 2026 })
        foreach (bool wrap in new[] { false })
        {
            var manifest = CheckManifest(512, 256, wrap) with { Seed = seed };
            manifest = manifest with { Generation = manifest.Generation with
                { Extent = MapExtent.World, Stage = GenerationStage.Land, LandPercent = 35 } };
            var old = Create(manifest with { GeneratorVersion = 53 });
            var current = Create(manifest);
            var a = Measure(old); var b = Measure(current);
            before += a.FacingShore; after += b.FacingShore;
            if (old.Water.Count(w => w == WaterType.None) != current.Water.Count(w => w == WaterType.None))
                throw new Exception("Continental spacing changed the land quota.");
            Console.WriteLine($"ATLAS SPACING seed={seed} wrap={wrap}: major={a.Continents}->{b.Continents}, close shore={a.FacingShore}->{b.FacingShore}");
            if (review && !wrap)
            {
                Directory.CreateDirectory(".godot/atlas-spacing54");
                foreach (var sample in new[] { old, current })
                {
                    var g = GeographicRegions.Get(sample);
                    File.WriteAllText($".godot/atlas-spacing54/{seed}-256-v{sample.Manifest.GeneratorVersion}.json", JsonSerializer.Serialize(new
                    { sample.Width, sample.Height, Water = sample.Water.Select(w => (int)w).ToArray(), Regions = g.CellIds,
                      Kinds = g.Regions.ToDictionary(r => r.Id, r => (int)r.Kind) }));
                }
            }
        }
        Console.WriteLine($"ATLAS SPACING total close shore={before}->{after}");
        if (after >= before) throw new Exception("Continental spacing did not reduce narrow gaps between mainlands.");
    }

    internal static void CheckFullAtlas()
    {
        foreach (int seed in new[] { 1234, 1, -77 })
        foreach (int continents in new[] { 3, 12 })
        foreach (bool wrap in new[] { false })
        foreach (int landPercent in new[] { 10, 38, 65 })
        {
            var size = MapDomain.ExpandedSize(128, 64);
            var m = CheckManifest(size.Width, size.Height, wrap) with { Seed = seed };
            m = m with { Generation = m.Generation with { Extent = MapExtent.World,
                ReferenceWidth = 128, ReferenceHeight = 64, LandPercent = landPercent, Continents = continents } };
            var world = Create(m);
            var ice = PolarIceField.Enabled(m) ? new PolarIceField(m) : null;
            int target = world.Domain.PercentageAreaCount * landPercent / 100;
            var seen = new bool[world.Count];
            var queue = new System.Collections.Generic.Queue<int>();
            int major = 0;
            for (int start = 0; start < world.Count; start++)
            {
                if (seen[start] || world.Water[start] != WaterType.None || (ice?.IsPolar(start) ?? false)) continue;
                int count = 0; seen[start] = true; queue.Enqueue(start);
                while (queue.TryDequeue(out int i))
                {
                    count++; var hex = HexGrid.FromOffset(i % world.Width, i / world.Width);
                    for (int d = 0; d < 6; d++)
                    {
                        int n = HexGrid.NeighborIndex(hex, d, world.Width, world.Height, wrap);
                        if (n < 0 || seen[n] || world.Water[n] != WaterType.None || (ice?.IsPolar(n) ?? false)) continue;
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
            if (Enumerable.Range(0, world.Count).Count(i => world.Water[i] == WaterType.None && !(ice?.IsPolar(i) ?? false)) != target)
                throw new Exception("Nonpolar atlas land quota changed.");
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
            if (!original.SequenceEqual(repeated.Biomes))
            {
                int cell = Enumerable.Range(0, original.Length).First(i => original[i] != repeated.Biomes[i]);
                throw new Exception($"Regenerating the curved edge changed the atlas: seed={seed}, wrap={wrap}, land={landPercent}, continents={continents}, hex={cell % world.Width},{cell / world.Width}, {original[cell]} -> {repeated.Biomes[cell]}.");
            }
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
