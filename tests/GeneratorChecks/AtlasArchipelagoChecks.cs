using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    internal static void CheckAtlasArchipelagos(bool review = false)
    {
        foreach (bool wrap in new[] { false })
        {
            var fixture = CheckManifest(128, 64, wrap);
            fixture = fixture with { Generation = fixture.Generation with { Extent = MapExtent.World, Stage = GenerationStage.Land } };
            new ContinentalSculpture(fixture).CheckAtlasAdditionPreservesIslands();
        }
        (int Small, int Medium, int Large, int Singles, int IslandArea, int LargeArea) Stats(WorldData w)
        {
            var seen = new bool[w.Count]; var sizes = new List<int>(); var queue = new Queue<int>();
            var polar = new PolarIceField(w.Manifest);
            for (int start = 0; start < w.Count; start++)
            {
                if (seen[start] || !w.Domain.Contains(start) || w.Water[start] != WaterType.None || polar.IsPolar(start)) continue;
                int size = 0; seen[start] = true; queue.Enqueue(start);
                while (queue.TryDequeue(out int cell))
                {
                    size++;
                    for (int d = 0; d < 6; d++)
                    {
                        int n = HexGrid.NeighborIndex(HexGrid.FromOffset(cell % w.Width, cell / w.Width), d, w.Width, w.Height, w.Manifest.HorizontalWrap);
                        if (!w.Domain.Contains(n) || seen[n] || w.Water[n] != WaterType.None || polar.IsPolar(n)) continue;
                        seen[n] = true; queue.Enqueue(n);
                    }
                }
                sizes.Add(size);
            }
            int continentMin = Math.Max(32, (int)Math.Ceiling(sizes.Sum() * 0.05));
            var islands = sizes.Where(n => n < continentMin).ToArray();
            return (islands.Count(n => n is >= 3 and <= 9), islands.Count(n => n is >= 10 and < 64),
                islands.Count(n => n >= 64), islands.Count(n => n == 1), islands.Sum(), islands.Where(n => n >= 64).Sum());
        }
        int oldSmall = 0, newSmall = 0, oldLargeArea = 0, newLargeArea = 0, newMedium = 0;
        foreach (int height in new[] { 64, 256, 512 })
        foreach (int seed in new[] { 1234, 1, -77 })
        foreach (bool wrap in new[] { false })
        {
            if (height == 512 && (wrap || seed == 1)) continue;
            var m = CheckManifest(height * 2, height, wrap) with { Seed = seed };
            m = m with { Generation = m.Generation with { Extent = MapExtent.World, Stage = GenerationStage.Land } };
            var old = Create(m with { GeneratorVersion = 52 }); var current = Create(m with { GeneratorVersion = 53 });
            var a = Stats(old); var b = Stats(current);
            if (old.Water.Count(v => v == WaterType.None) != current.Water.Count(v => v == WaterType.None))
                throw new Exception("Atlas archipelagos changed the land quota.");
            var repeat = Create(current.Manifest);
            if (!current.Water.SequenceEqual(repeat.Water)) throw new Exception("Atlas island construction is not deterministic.");
            var regions = GeographicRegions.Get(current);
            if (regions.Regions.Sum(r => r.Cells) != current.ActiveCount) throw new Exception("Island regions lost atlas cells.");
            Console.WriteLine($"ATLAS {m.Width}x{height} seed={seed} wrap={wrap}: 3..9 {a.Small}->{b.Small}; 10..63 {a.Medium}->{b.Medium}; >=64 {a.Large}->{b.Large}; single {a.Singles}->{b.Singles}; large island area {a.LargeArea}->{b.LargeArea}");
            oldSmall += a.Small; newSmall += b.Small; oldLargeArea += a.LargeArea; newLargeArea += b.LargeArea; newMedium += b.Medium;
            if (review && !wrap && seed != 1)
            {
                Directory.CreateDirectory(".godot/island-review53");
                foreach (var sample in new[] { old, current })
                {
                    var g = GeographicRegions.Get(sample);
                    File.WriteAllText($".godot/island-review53/{seed}-{height}-v{sample.Manifest.GeneratorVersion}.json", JsonSerializer.Serialize(new
                    { sample.Width, sample.Height, Water = sample.Water.Select(w => (int)w).ToArray(), Regions = g.CellIds,
                      Kinds = g.Regions.ToDictionary(r => r.Id, r => (int)r.Kind) }));
                }
            }
        }
        Console.WriteLine($"ATLAS TOTAL small={oldSmall}->{newSmall}; large island area={oldLargeArea}->{newLargeArea}");
        if (newSmall <= oldSmall || newLargeArea >= oldLargeArea || newMedium == 0)
            throw new Exception("Atlas archipelagos did not improve the size distribution while retaining medium islands.");
        foreach (bool wrap in new[] { false })
        {
            var m = CheckManifest(128, 64, wrap) with { Generation = CheckManifest(128, 64, wrap).Generation with { Stage = GenerationStage.Land } };
            if (!Create(m).Water.SequenceEqual(Create(m with { GeneratorVersion = 52 }).Water))
                throw new Exception("Atlas-specific island changes affected Region maps.");
        }
        Console.WriteLine("PASS atlas island size distribution, primary/medium islands, exact quota, determinism, region compatibility");
    }

    private sealed partial class ContinentalSculpture
    {
        public void CheckAtlasAdditionPreservesIslands()
        {
            var water = new WaterType[_width * _height]; Array.Fill(water, WaterType.Ocean);
            void Land(int x, int y, int width, int height)
            {
                for (int row = y; row < y + height; row++)
                for (int col = x; col < x + width; col++) water[row * _width + col] = WaterType.None;
            }
            Land(18,17,70,30); Land(100,24,8,8); Land(95,42,3,2);
            for (int i = 0; i < water.Length; i++) _scores[i] = water[i] == WaterType.None ? 0.05f : -0.05f;
            var original = (WaterType[])water.Clone();
            int land = water.Count(w => w == WaterType.None);
            AddSmallIslands(water, new float[water.Length], 0);
            int added = 0, removed = 0;
            for (int i = 0; i < water.Length; i++)
            {
                bool before = original[i] == WaterType.None, after = water[i] == WaterType.None;
                if (before && !after)
                {
                    removed++;
                    if (i % _width >= 95) throw new Exception("Atlas addition eroded an original island.");
                }
                if (!before && after) added++;
            }
            if (added == 0 || added != removed || water.Count(w => w == WaterType.None) != land || added > (int)(land * 0.006))
                throw new Exception("Atlas addition failed its area/budget contract.");
            var seen = new bool[water.Length]; var queue = new Queue<int>();
            for (int start = 0; start < water.Length; start++)
            {
                if (seen[start] || water[start] != WaterType.None) continue;
                int size = 0, inherited = 0; seen[start] = true; queue.Enqueue(start);
                while (queue.TryDequeue(out int cell))
                {
                    size++; if (original[cell] == WaterType.None) inherited++;
                    for (int d = 0; d < 6; d++)
                    {
                        int n = Neighbor(HexAt(cell), d);
                        if (n < 0 || seen[n] || water[n] != WaterType.None) continue;
                        seen[n] = true; queue.Enqueue(n);
                    }
                }
                if (inherited > 0 && size != 6 && size != 64 && size != 2100 - removed)
                    throw new Exception("An atlas addition fragmented, merged or resized original islands.");
                if (inherited == 0 && size != 1 && size is not (>= 3 and <= 9))
                    throw new Exception("A new island patch has an invalid size.");
            }
            Console.WriteLine($"PASS atlas additive fixture wrap={_wrap}: 6/64-hex islands intact, mainland connected, {added} locally funded hexes");
        }
    }
}
