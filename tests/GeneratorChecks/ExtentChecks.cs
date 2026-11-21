using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    internal static void CheckWorldExtent()
    {
        var legacySettings = JsonSerializer.Deserialize<GenerationSettings>("{\"Algorithm\":2,\"Continents\":3,\"LandPercent\":38,\"CoastDetail\":50,\"Stage\":3}")!;
        if (legacySettings.Extent != MapExtent.Region) throw new Exception("Old saves must remain open regions.");
        var m = CheckManifest(128, 64, false);
        var previous = Create(m with { GeneratorVersion = 33 });
        var region = Create(m with { GeneratorVersion = 34 });
        if (!region.Water.SequenceEqual(previous.Water) || !region.Elevation.SequenceEqual(previous.Elevation) ||
            !region.Biomes.SequenceEqual(previous.Biomes) || !region.Rainfall.SequenceEqual(previous.Rainfall))
            throw new Exception("Region mode changed the v33 natural terrain.");
        foreach (var (width, height, land, wrap) in new[] { (64, 32, 65, false), (193, 95, 10, false), (193, 95, 65, true), (64, 96, 65, false) })
        {
            var manifest = CheckManifest(width, height, wrap) with { GeneratorVersion = 34 };
            manifest = manifest with { Generation = manifest.Generation with { Extent = MapExtent.World, LandPercent = land } };
            var world = Create(manifest);
            AssertOceanRim(world);
            if (ReadRivers(world).Any(r => r.EndType == "Boundary"))
                throw new Exception("A world river terminated on the rectangular map boundary.");
            if (world.Width != width || world.Height != height) throw new Exception("World mode changed selected dimensions.");
            if (world.Water.Count(w => w == WaterType.None) != world.Count * land / 100)
                throw new Exception("World mode lost the selected land area.");
            var landOnly = Create(manifest with { Generation = manifest.Generation with { Stage = GenerationStage.Land } });
            if (!world.Water.SequenceEqual(landOnly.Water)) throw new Exception("World mode depends on generation stage.");
            var regenerated = Create(manifest);
            RegenerateRegion(regenerated, new(0, 0, Math.Min(35, width), Math.Min(30, height)), RegenerationMode.Everything, HexLocks.Biome);
            if (!world.Biomes.SequenceEqual(regenerated.Biomes)) throw new Exception("Regional regeneration lost the ocean frame.");
            string path = Path.Combine(Path.GetTempPath(), $"extent-{Guid.NewGuid():N}.world");
            try
            {
                WorldFile.Save(path, world, null);
                var loaded = WorldFile.Load(path);
                if (loaded.Manifest.Generation.Extent != MapExtent.World || !loaded.Water.SequenceEqual(world.Water))
                    throw new Exception("Save/load lost map extent or ocean rim.");
            }
            finally { File.Delete(path); }
        }
        var open = Create(m with { GeneratorVersion = 35 });
        var framed = Create((m with { GeneratorVersion = 35 }) with { Generation = m.Generation with { Extent = MapExtent.World } });
        if (!open.Water.SequenceEqual(framed.Water) || !open.Elevation.SequenceEqual(framed.Elevation) ||
            !open.Biomes.SequenceEqual(framed.Biomes) || !open.RiverEdges.SequenceEqual(framed.RiverEdges))
            throw new Exception("Visual ocean frame changed the generated data.");
        Console.WriteLine("PASS: v35 World and Region have identical generated data; frame has zero hexes");
        Console.WriteLine("PASS: v33 region preserved exactly; world ocean rim, dimensions, exact land area, stages, regional regeneration and save/load (including 65% land, portrait and wrap)");
    }

    internal static void ReviewWorldExtent()
    {
        Directory.CreateDirectory(".godot/world-extent-review34");
        foreach (MapExtent extent in Enum.GetValues<MapExtent>())
        {
            var m = CheckManifest(512, 256, false);
            m = m with { Generation = m.Generation with { Extent = extent } };
            var world = Create(m);
            
            File.WriteAllText($".godot/world-extent-review34/{extent}.json", JsonSerializer.Serialize(new
            { world.Width, world.Height, Extent = extent.ToString(), world.Elevation, Water = world.Water.Select(w => (int)w).ToArray(), world.Tectonics }));
        }
        Console.WriteLine("PASS: same-seed region/world review generated at 512x256");
    }

    private static void AssertOceanRim(WorldData world)
    {
        for (int row = 0; row < world.Height; row++)
        for (int col = 0; col < world.Width; col++)
        {
            float x = (col + 0.5f) / world.Width * 2 - 1, y = (row + 0.5f) / world.Height * 2 - 1;
            if (x * x + y * y < 0.97f * 0.97f) continue;
            int i = row * world.Width + col;
            if (world.Water[i] != WaterType.Ocean || world.Elevation[i] >= 0 || world.Biomes[i] != BiomeType.None)
                throw new Exception($"Ocean rim received land, a lake or a terrestrial biome at {col},{row}.");
        }
    }
}
