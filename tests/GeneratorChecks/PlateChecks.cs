using System;
using System.IO;
using System.Linq;
using System.Collections.Immutable;
using System.Text.Json;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    internal static void CheckPlateTerrain()
    {
        var m = CheckManifest(256, 128, false);
        float width = m.Width * HexGrid.Root3, height = m.Height * 1.5f;
        var oceanPlates = ImmutableArray.Create(
            new PlateReference(width * 0.08f, height * 0.5f, 0, 0, true, 1.3f),
            new PlateReference(width * 0.47f, height * 0.5f, 1, 0, false),
            new PlateReference(width * 0.85f, height * 0.5f, -1, 0, false));
        PlateContact Contact(float compression) => new(width * 0.66f, height * 0.1f, width * 0.66f, height * 0.9f,
            compression, 0, 1, 2);
        (WorldData World, float[] Ridges) Build(ImmutableArray<PlateReference> plates, float? compression)
        {
            var reference = new TectonicReference(plates, compression is float c ? ImmutableArray.Create(Contact(c)) : ImmutableArray<PlateContact>.Empty);
            var world = new WorldData(m) { Tectonics = reference };
            Generate(world, new GenerationArea(world, new(0, 0, m.Width, m.Height), useWorldArrays: true), null);
            var surface = new ContinentalSculpture(m, reference).Build(null);
            int land = world.Water.Count(w => w == WaterType.None);
            if (land != world.Count * m.Generation.LandPercent / 100) throw new Exception("Plate generation lost exact land quota.");
            return (world, surface.Ridges!);
        }
        var oceanCollision = Build(oceanPlates, 1.5f);
        var oceanPassive = Build(oceanPlates, null);
        int ArcLand(WorldData world) => Enumerable.Range(0, world.Count).Count(i =>
            i % world.Width > world.Width * 0.54f && i % world.Width < world.Width * 0.71f &&
            i / world.Width > world.Height * 0.1f && i / world.Width < world.Height * 0.9f && world.Water[i] == WaterType.None);
        int emerged = ArcLand(oceanCollision.World) - ArcLand(oceanPassive.World);
        if (emerged < 30) throw new Exception($"Ocean collision did not expose an arc: {emerged} additional land cells.");
        var labels = new int[oceanCollision.World.Count];
        var sizes = new System.Collections.Generic.List<int> { 0 };
        var queue = new System.Collections.Generic.Queue<int>();
        for (int start = 0; start < labels.Length; start++)
        {
            if (labels[start] != 0 || oceanCollision.World.Water[start] != WaterType.None) continue;
            int id = sizes.Count;
            int size = 0;
            queue.Enqueue(start); labels[start] = id;
            while (queue.TryDequeue(out int i))
            {
                size++;
                var hex = HexGrid.FromOffset(i % m.Width, i / m.Width);
                for (int d = 0; d < 6; d++)
                {
                    int n = HexGrid.NeighborIndex(hex, d, m.Width, m.Height, false);
                    if (n < 0 || labels[n] != 0 || oceanCollision.World.Water[n] != WaterType.None) continue;
                    labels[n] = id; queue.Enqueue(n);
                }
            }
            sizes.Add(size);
        }
        int mainland = sizes.IndexOf(sizes.Max());
        int detached = Enumerable.Range(0, labels.Length).Count(i => labels[i] != 0 && labels[i] != mainland &&
            i % m.Width > m.Width * 0.54f && i % m.Width < m.Width * 0.71f);
        int arcIslands = Enumerable.Range(0, labels.Length).Where(i => labels[i] != 0 && labels[i] != mainland &&
            i % m.Width > m.Width * 0.54f && i % m.Width < m.Width * 0.71f).Select(i => labels[i]).Distinct().Count();
        if (arcIslands < 2) throw new Exception("Oceanic arc became a continuous land ribbon instead of an island chain.");
        if (detached < 30) throw new Exception("Oceanic arc did not form islands separated from the mainland.");
        var continentalPlates = oceanPlates.Select((p, i) => p with { Continental = i != 0 }).ToImmutableArray();
        var collision = Build(continentalPlates, 1.5f);
        var extension = Build(continentalPlates, -1.5f);
        int[] core = Enumerable.Range(0, m.Width * m.Height).Where(i => Math.Abs(i % m.Width - m.Width * 0.66f) < 2 &&
            i / m.Width > m.Height * 0.2f && i / m.Width < m.Height * 0.8f).ToArray();
        double collisionHeight = core.Average(i => (double)collision.World.Elevation[i]);
        double riftHeight = core.Average(i => (double)extension.World.Elevation[i]);
        if (collisionHeight < riftHeight + 1000) throw new Exception($"Collision/rift have similar heights: {collisionHeight}/{riftHeight}.");
        if (core.Any(i => extension.World.Relief[i] >= ReliefType.Mountains && extension.World.Water[i] == WaterType.None))
            throw new Exception("Divergent core acquired mountains.");
        // v41 can classify the broad summit as a plateau. The collision must
        // still produce mountain flanks, rather than forcing icons by altitude.
        int[] belt = Enumerable.Range(0, m.Width * m.Height).Where(i => Math.Abs(i % m.Width - m.Width * 0.66f) < m.Width * 0.1f &&
            i / m.Width > m.Height * 0.2f && i / m.Width < m.Height * 0.8f).ToArray();
        int collisionMountains = belt.Count(i => collision.World.Water[i] == WaterType.None && collision.World.Relief[i] >= ReliefType.Mountains);
        int riftMountains = belt.Count(i => extension.World.Water[i] == WaterType.None && extension.World.Relief[i] >= ReliefType.Mountains);
        if (m.GeneratorVersion >= 41 ? collisionMountains < riftMountains + 30 : !core.Any(i => collision.World.Relief[i] >= ReliefType.Mountains))
            throw new Exception($"Continental collision lacks mountain flanks: collision={collisionMountains}, rift={riftMountains}.");
        Directory.CreateDirectory(".godot/tectonic-review33");
        foreach (var (name, sample) in new[] { ("ocean-collision", oceanCollision), ("ocean-passive", oceanPassive), ("continental-collision", collision), ("continental-rift", extension) })
            WritePlateReview(name, sample.World, sample.Ridges);
        Console.WriteLine($"PASS: ocean collision exposed {emerged} additional arc cells ({detached} cells across {arcIslands} detached islands); continental collision mean={collisionHeight:F0}m, rift mean={riftHeight:F0}m; mountain flanks={collisionMountains}/{riftMountains}; divergent core has no mountains");
    }

    internal static void CheckContinentSeparation()
    {
        foreach (int seed in new[] { 1234, 1, -77 })
        foreach (bool wrap in new[] { false, true })
        foreach (int continents in new[] { 2, 3, 6, 12 })
        foreach (int land in new[] { 10, 38, 65 })
        {
            var m = CheckManifest(193, 95, wrap) with { Seed = seed };
            m = m with { Generation = m.Generation with { Continents = continents, LandPercent = land, Stage = GenerationStage.Land } };
            var world = Create(m);
            var seen = new bool[world.Count];
            var queue = new System.Collections.Generic.Queue<int>();
            var sizes = new System.Collections.Generic.List<int>();
            for (int start = 0; start < world.Count; start++)
            {
                if (seen[start] || world.Water[start] != WaterType.None) continue;
                int size = 0; queue.Enqueue(start); seen[start] = true;
                while (queue.TryDequeue(out int i))
                {
                    size++; var hex = HexGrid.FromOffset(i % world.Width, i / world.Width);
                    for (int d = 0; d < 6; d++)
                    {
                        int n = HexGrid.NeighborIndex(hex, d, world.Width, world.Height, wrap);
                        if (n < 0 || seen[n] || world.Water[n] != WaterType.None) continue;
                        seen[n] = true; queue.Enqueue(n);
                    }
                }
                sizes.Add(size);
            }
            int target = world.Count * land / 100;
            int major = sizes.Count(n => n >= Math.Max(3, target / (continents * 12)));
            if (major < continents) throw new Exception($"Continents ignored: seed={seed}, continents={continents}, land={land}, sizes={string.Join(',', sizes.OrderByDescending(x => x).Take(15))}");
            if (world.Water.Count(w => w == WaterType.None) != target) throw new Exception("Separation changed target land area.");
        }
        Console.WriteLine("PASS: 72 seed/wrap/count/land cases, separated mainlands and exact rectangular land percentage");
    }

    internal static void CheckLargeContinentSeparation()
    {
        var m = CheckManifest(2048, 1024, true);
        m = m with { Generation = m.Generation with { Continents = 12, LandPercent = 65, Stage = GenerationStage.Land } };
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var world = Create(m);
        var seen = new bool[world.Count];
        var queue = new System.Collections.Generic.Queue<int>();
        int major = 0, land = 0;
        int target = world.Count * 65 / 100;
        for (int start = 0; start < world.Count; start++)
        {
            if (seen[start] || world.Water[start] != WaterType.None) continue;
            int size = 0; seen[start] = true; queue.Enqueue(start);
            while (queue.TryDequeue(out int i))
            {
                size++; var hex = HexGrid.FromOffset(i % world.Width, i / world.Width);
                for (int d = 0; d < 6; d++)
                {
                    int n = HexGrid.NeighborIndex(hex, d, world.Width, world.Height, true);
                    if (n < 0 || seen[n] || world.Water[n] != WaterType.None) continue;
                    seen[n] = true; queue.Enqueue(n);
                }
            }
            land += size;
            if (size >= target / (12 * 12)) major++;
        }
        if (land != target || major < 12) throw new Exception($"Large continent separation failed: {land}/{target} land, {major} mainlands.");
        Console.WriteLine($"PASS: 2048x1024, 12 mainlands requested, {major} separated mainlands, exactly {land} land hexes (65%), {watch.Elapsed.TotalSeconds:F2}s");
    }

    internal static void CheckCoastalTectonicRelief()
    {
        const int width = 64, height = 512;
        bool Land(int i) => i % width is >= 25 and <= 39;
        var ridge = Enumerable.Range(0, width * height).Select(i => Land(i) ? 0.8f : 0f).ToArray();
        short Raw(int i) => Land(i) ? (short)3200 : (short)-1000;
        WaterType Water(int i) => Land(i) ? WaterType.None : WaterType.Ocean;
        var old = BuildGentleElevation(width, height, false, Water, Raw);
        var active = BuildGentleElevation(width, height, false, Water, Raw, ridge);
        int sample = height / 2 * width + 28;
        if (active[sample] < 1850 || active[sample] < old[sample] + 1000)
            throw new Exception("Coastal feather erased the active tectonic range.");
        Console.WriteLine($"PASS: coastal tectonic range survives smoothing ({old[sample]}m -> {active[sample]}m)");
    }

    internal static void CheckPlateOptions()
    {
        foreach (int continents in new[] { 1, 12 })
        foreach (int land in new[] { 10, 65 })
        {
            var m = CheckManifest(193, 95, true);
            m = m with { Generation = m.Generation with { Continents = continents, LandPercent = land } };
            var terrain = Create(m);
            var surface = Create(m with { Generation = m.Generation with { Stage = GenerationStage.Land } });
            if (!terrain.Water.SequenceEqual(surface.Water)) throw new Exception("Terrain stage changed the plate water mask.");
            if (terrain.Water.Count(w => w == WaterType.None) != terrain.Count * land / 100)
                throw new Exception("Extreme plate settings lost land area.");
            if (terrain.Tectonics!.Plates.Count(p => p.Size > 0.6f && p.Continental) != continents)
                throw new Exception("Continental plate setting was ignored.");
        }
        Console.WriteLine("PASS: 1/12 continental plates, 10/65% land, odd dimensions, wrap and stage-independent water masks");
    }

    internal static void ReviewPlateWorlds()
    {
        WorldData? previous = null;
        foreach (int seed in new[] { 1234, 1, -77 })
        {
            var m = CheckManifest(512, 256, true) with { Seed = seed };
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var world = Create(m);
            var surface = new ContinentalSculpture(m, world.Tectonics).Build(null);
            int[] rifts = Enumerable.Range(0, world.Count).Where(i => surface.Ridges![i] < -0.3f && world.Water[i] == WaterType.None).ToArray();
            if (rifts.Any(i => world.Relief[i] >= ReliefType.Mountains)) throw new Exception("Generated rift core has mountains.");
            int distantMountains = Enumerable.Range(0, world.Count).Count(i => world.Water[i] == WaterType.None &&
                surface.Ridges![i] <= 0.02f && world.Elevation[i] >= 1850);
            if (distantMountains != 0) throw new Exception($"Unrelated noise created {distantMountains} distant mountain cells.");
            WritePlateReview($"seed-{seed}", world, surface.Ridges!);
            if (previous is not null)
            {
                double changed = Enumerable.Range(0, world.Count).Count(i => (world.Water[i] == WaterType.None) != (previous.Water[i] == WaterType.None)) / (double)world.Count;
                if (changed < 0.18) throw new Exception($"Seeds have too similar land masks: {changed:P1} changed.");
                Console.WriteLine($"Seed land-mask difference: {changed:P1}");
            }
            previous = world;
            Console.WriteLine($"PASS: v33 seed={seed}, land={world.Water.Count(w => w == WaterType.None)}, mountains={world.Elevation.Count(e => e >= 1850)}, land rift core={rifts.Length}, generation+review={watch.Elapsed.TotalSeconds:F2}s");
        }
    }

    private static void WritePlateReview(string name, WorldData world, float[] ridges)
    {
        Directory.CreateDirectory(".godot/tectonic-review33");
        File.WriteAllText($".godot/tectonic-review33/{name}.json", JsonSerializer.Serialize(new
        { world.Width, world.Height, world.Manifest.Seed, world.Elevation, Water = world.Water.Select(w => (int)w).ToArray(), Ridges = ridges, world.Tectonics }));
    }
}
