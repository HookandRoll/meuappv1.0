using System;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Numerics;
using System.Collections.Immutable;
using Mapmaker.Core;

WorldGenerator.CheckBiomeConsolidation();
WorldGenerator.CheckWorldExtent();
WorldGenerator.CheckFullAtlas();
WorldGenerator.CheckMandatoryPolarPlates();
WorldGenerator.CheckPolarOcean();
WorldGenerator.CheckContinentSeparation();
if (args.Contains("--separation-large")) WorldGenerator.CheckLargeContinentSeparation();
if (args.Contains("--extent-review")) WorldGenerator.ReviewWorldExtent();
WorldGenerator.CheckMicroplateRelief();
WorldGenerator.CheckPlateTerrain();
WorldGenerator.CheckPlateOptions();
WorldGenerator.CheckCoastalTectonicRelief();
if (args.Contains("--plate-review")) WorldGenerator.ReviewPlateWorlds();
foreach (int seed in new[] { 1234, 1, -77 })
foreach (bool wrap in new[] { false, true })
{
    var m = WorldGenerator.CheckManifest(512, 256, wrap) with { Seed = seed };
    var reference = TectonicReference.Create(m);
    reference.Validate(m.Width, m.Height);
    Check(reference.Plates.Any(p => p.Size < 0.6f) && reference.Plates.Any(p => p.Size > 1f && p.Continental), "major continental plates and microplates");
    Check(reference.Plates.Select((p, i) => (p, i)).Where(x => x.p.Size < 0.6f)
        .All(x => reference.Contacts.Any(c => c.A == x.i || c.B == x.i)), "microplates have actual boundaries");
    int bends = reference.Contacts.GroupBy(c => (c.A < c.B ? c.A : c.B, c.A < c.B ? c.B : c.A))
        .Count(g => { var lines = g.Select(c => Vector2.Normalize(new Vector2(c.EndX - c.X, c.EndY - c.Y))).ToArray();
            return lines.Any(v => Math.Abs(Vector2.Dot(v, lines[0])) < 0.8f); });
    Check(bends >= 3, "jagged contacts change direction");
    var restored = JsonSerializer.Deserialize<TectonicReference>(JsonSerializer.Serialize(reference))!;
    Check(reference.Plates.SequenceEqual(restored.Plates) && reference.Contacts.SequenceEqual(restored.Contacts), "plate sizes survive serialization");
    Console.WriteLine($"PASS: seed={seed}, wrap={wrap}, major={reference.Plates.Count(p => p.Size > 1)}, micro={reference.Plates.Count(p => p.Size < 0.6f)}, bent boundaries={bends}, segments={reference.Contacts.Length}");
    if (seed == 1234 && !wrap)
    {
        Directory.CreateDirectory(".godot/tectonic-review33");
        File.WriteAllText(".godot/tectonic-review33/plates.json", JsonSerializer.Serialize(reference));
    }
}
var oldPlate = JsonSerializer.Deserialize<PlateReference>("{\"X\":1,\"Y\":2,\"Vx\":1,\"Vy\":0,\"Continental\":true}")!;
Check(oldPlate.Size == 1f, "old plate JSON defaults to original size");
foreach (int version in new[] { 30, 31 })
    Check(TectonicReference.Create(WorldGenerator.CheckManifest(256, 128, false) with { GeneratorVersion = version }).Plates.All(p => p.Size == 1f), "legacy plate sizes");
foreach (int size in new[] { 32, 4096 })
{
    var m = WorldGenerator.CheckManifest(size, size == 32 ? 32 : size / 2, true);
    TectonicReference.Create(m).Validate(m.Width, m.Height);
}
if (args.Contains("--tectonics-only")) return;
foreach (bool wrap in new[] { false, true })
{
    var manifest = WorldGenerator.CheckManifest(args.Contains("--large") ? 1024 : 256, args.Contains("--large") ? 512 : 128, wrap);
    var first = WorldGenerator.Create(manifest);
    if (!wrap)
    {
        File.WriteAllText(".godot/tectonic-review33/terrain.json", JsonSerializer.Serialize(new { first.Width, first.Height, first.Elevation, Water = first.Water.Select(w => (int)w).ToArray() }));
    }
    string savePath = Path.Combine(Path.GetTempPath(), $"tectonic32-{Guid.NewGuid():N}.world");
    try
    {
        WorldFile.Save(savePath, first, null);
        var loaded = WorldFile.Load(savePath);
        Check(first.Tectonics!.Plates.SequenceEqual(loaded.Tectonics!.Plates) &&
            first.Tectonics.Contacts.SequenceEqual(loaded.Tectonics.Contacts), "world save/load preserves tectonics");
    }
    finally { File.Delete(savePath); }
    var second = WorldGenerator.Create(manifest);
    Check(first.Biomes.SequenceEqual(second.Biomes) && first.Elevation.SequenceEqual(second.Elevation), "seed determinism");
    first.Tectonics!.Validate(first.Width, first.Height);
    Check(first.Tectonics.Contacts.SequenceEqual(second.Tectonics!.Contacts), "contact determinism");
    var legacy = WorldGenerator.Create(manifest with { GeneratorVersion = 30 });
    Check(!first.Tectonics.Contacts.SequenceEqual(legacy.Tectonics!.Contacts), "seeded plate warp changes contacts");
    foreach (var region in new[] { new WorldRegion(0, 40, 35, 35), new WorldRegion(110, 45, 35, 35), new WorldRegion(230, 90, 26, 30) })
    {
        WorldGenerator.RegenerateRegion(second, region, RegenerationMode.Everything, HexLocks.Biome);
        Check(first.Biomes.SequenceEqual(second.Biomes), "regional regeneration matches full generation");
    }
    int locked = Array.FindIndex(second.Water, w => w == WaterType.None);
    second.Biomes[locked] = BiomeType.Alpine;
    second.LockMask[locked] = (ushort)HexLocks.Biome;
    WorldGenerator.RegenerateRegion(second, new WorldRegion(0, 0, second.Width, second.Height), RegenerationMode.Everything, HexLocks.Biome);
    Check(second.Biomes[locked] == BiomeType.Alpine, "biome locks");
    Console.WriteLine($"PASS: wrap={wrap}, deterministic world, tectonics, regional regeneration and locks; contacts={first.Tectonics.Contacts.Length}");
}
Console.WriteLine("All generator checks passed.");
static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

namespace Mapmaker.Core
{
    public static partial class WorldGenerator
    {
        internal static WorldManifest CheckManifest(int width, int height, bool wrap) => new(
            WorldFile.CurrentVersion, "Generator checks", 1234, Version, width, height, wrap, 5, "km",
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            new(TerrainAlgorithm.ContinentalSculpt, 3, 38, 50, GenerationStage.Terrain));

        internal static void CheckMicroplateRelief()
        {
            var m = CheckManifest(128, 64, false);
            var reference = TectonicReference.Create(m);
            var actual = new WorldData(m) { Tectonics = reference };
            var uniform = new WorldData(m) { Tectonics = reference with
                { Plates = reference.Plates.Select(p => p with { Size = 1f }).ToImmutableArray() } };
            Generate(actual, new GenerationArea(actual, new(0, 0, m.Width, m.Height), useWorldArrays: true), null);
            Generate(uniform, new GenerationArea(uniform, new(0, 0, m.Width, m.Height), useWorldArrays: true), null);
            if (actual.Elevation.SequenceEqual(uniform.Elevation))
                throw new Exception("Microplate sizes did not affect terrain.");
            Console.WriteLine("PASS: plate sizes change actual terrain, beyond overlay geometry");
        }

        internal static void CheckBiomeConsolidation()
        {
            var world = new WorldData(CheckManifest(64, 32, true));
            Array.Fill(world.Biomes, BiomeType.Grassland);
            Array.Fill(world.Temperature, (short)180);
            Array.Fill(world.Rainfall, (ushort)900);
            int single = 10 * world.Width + 10;
            int pair = 10 * world.Width + 20;
            int beach = 20 * world.Width + 10;
            int alpine = beach + 5;
            int arid = beach + 10;
            world.Biomes[single] = BiomeType.TemperateForest;
            world.Biomes[pair] = world.Biomes[pair + 1] = BiomeType.TemperateForest;
            world.Biomes[beach] = BiomeType.Beach;
            world.Biomes[alpine] = BiomeType.Alpine;
            world.Biomes[arid] = BiomeType.Desert;
            world.Rainfall[arid] = 50;
            // A component crossing the wrap seam is one region, not two fragments.
            for (int row = 14; row <= 17; row++)
                world.Biomes[row * world.Width] = world.Biomes[row * world.Width + 63] = BiomeType.TemperateForest;
            var area = new GenerationArea(world, new(0, 0, 64, 32), useWorldArrays: true);
            ConsolidateSmallBiomes(area, new bool[world.Count], null);
            if (world.Biomes[single] != BiomeType.Grassland || world.Biomes[pair] != BiomeType.Grassland ||
                world.Biomes[pair + 1] != BiomeType.Grassland) throw new Exception("Small components were not merged.");
            if (world.Biomes[beach] != BiomeType.Beach || world.Biomes[alpine] != BiomeType.Alpine ||
                world.Biomes[arid] != BiomeType.Desert) throw new Exception("Protected habitats or climate changed.");
            if (world.Biomes[15 * world.Width] != BiomeType.TemperateForest) throw new Exception("Wrap split a large component.");
            // Water cannot receive a terrestrial biome during cleanup.
            world.Water[single] = WaterType.Lake;
            world.Biomes[single] = BiomeType.None;
            ConsolidateSmallBiomes(area, new bool[world.Count], null);
            if (world.Biomes[single] != BiomeType.None) throw new Exception("Lake changed.");
            Console.WriteLine("PASS: small components, climate limits, protected biomes, wrap and water");
        }
    }
}
