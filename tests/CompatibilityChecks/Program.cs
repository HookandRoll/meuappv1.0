using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Mapmaker.Core;
using Mapmaker.GodotApp;

var hashes = new SortedDictionary<string, string>();
foreach (int version in new[] { 34, 53, 56, 57, 59, 60, 63 })
foreach (int width in version == 63 ? new[] { 128, 512 } : new[] { 128 })
foreach (int seed in new[] { 1234, -77 })
foreach (int mode in new[] { 0, 1, 2 })
{
    bool atlas = mode == 2;
    var size = atlas && width > 128 ? MapDomain.ExpandedSize(width, width / 2) : default;
    var manifest = new WorldManifest(WorldFile.CurrentVersion, "Compatibility", seed, version,
        size.Width > 0 ? size.Width : width, size.Height > 0 ? size.Height : width / 2, mode == 1, 5, "km", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
        new(TerrainAlgorithm.ContinentalSculpt, 3, 38, 50, GenerationStage.Terrain,
            atlas ? MapExtent.World : MapExtent.Region));
    var world = WorldGenerator.Create(manifest);
    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    void Add<T>(T[] values) where T : unmanaged => hash.AppendData(MemoryMarshal.AsBytes(values.AsSpan()));
    Add(world.Elevation); Add(world.Water); Add(world.Relief); Add(world.Biomes);
    Add(world.Landforms); Add(world.Features); Add(world.Temperature); Add(world.Rainfall);
    Add(world.RoadEdges); Add(world.RiverEdges); Add(world.Flags); Add(world.LockMask);
    hash.AppendData(System.Text.Encoding.UTF8.GetBytes(world.SparseEntities.GetRawText()));
    var regions = GeographicRegions.Get(world);
    Add(regions.CellIds);
    hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(regions.Regions));
    // The detail picker reads symbol bits from the texture; exercise all four combinations.
    var symbols = new byte[world.Count * 4];
    for (int i = 0; i < world.Count; i++) symbols[i * 4 + 3] = (byte)((i % 4) * 64);
    Add(MapDetailIcons.Build(world, symbols));
    var buffers = GlobalDataTextures.BuildBuffers(world);
    Add(buffers.Terrain); Add(buffers.Surface); Add(buffers.Geography); Add(buffers.Climate); Add(buffers.Details);
    if (buffers.FeatureLow is not null) Add(buffers.FeatureLow);
    if (buffers.FeatureHigh is not null) Add(buffers.FeatureHigh);
    string key = $"v{version}/size{width}/seed{seed}/mode{mode}";
    hashes[key] = Convert.ToHexString(hash.GetHashAndReset());
    Console.WriteLine(key + " " + hashes[key]);
}
string path = "tests/CompatibilityChecks/reference.json";
if (args.Contains("--write")) File.WriteAllText(path, JsonSerializer.Serialize(hashes, new JsonSerializerOptions { WriteIndented = true }));
else
{
    var expected = JsonSerializer.Deserialize<SortedDictionary<string, string>>(File.ReadAllText(path))!;
    foreach (var pair in hashes)
        if (!expected.TryGetValue(pair.Key, out string? value) || value != pair.Value)
            throw new Exception("Bitwise compatibility changed: " + pair.Key);
    Console.WriteLine($"PASS: {hashes.Count} reference worlds match bit for bit, including all layers, rivers, regions and icons.");
}
