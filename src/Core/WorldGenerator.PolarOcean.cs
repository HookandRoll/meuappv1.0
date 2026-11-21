using System;
using System.Collections.Immutable;
using System.Linq;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    public static int PolarRowsFor(int centralHeight) => Math.Max(2, ((centralHeight + 31) / 32) * 2);

    private static WorldManifest CentralManifest(WorldManifest manifest) => manifest with
    {
        Height = manifest.Height - 2 * manifest.Generation.PolarOceanRows,
        Generation = manifest.Generation with { PolarOceanRows = 0 }
    };

    private static WorldData CreatePolarWorld(WorldManifest manifest, Action<GenerationProgress>? progress)
    {
        var central = Create(CentralManifest(manifest), progress);
        return AddPolarRows(central, manifest);
    }

    private static WorldData AddPolarRows(WorldData central, WorldManifest manifest)
    {
        var world = new WorldData(manifest) { SparseEntities = central.SparseEntities };
        int padding = manifest.Generation.PolarOceanRows, shift = padding * world.Width;
        bool blank = manifest.Generation.Stage == GenerationStage.Blank;
        if (!blank)
        {
            Array.Fill(world.Water, WaterType.Ocean);
            Array.Fill(world.Elevation, (short)-6000);
            Array.Fill(world.Temperature, (short)-250);
        }
        CopyCentralFields(central, world, shift);
        if (central.Tectonics is { } tectonics)
        {
            float dy = padding * 1.5f;
            world.Tectonics = new(
                tectonics.Plates.Select(p => p with { Y = p.Y + dy }).ToImmutableArray(),
                tectonics.Contacts.Select(c => c with { Y = c.Y + dy, EndY = c.EndY + dy }).ToImmutableArray());
        }
        if (!blank)
        {
            GrowPolarCoasts(central, world, padding);
            var rivers = ReadRivers(central).Select(r =>
            {
                var path = r.Path.Select(i => i + shift).ToList();
                var tiers = r.Tiers.ToList();
                var flow = r.AnnualFlow.ToList();
                string endType = r.EndType;
                int lastRow = r.Path[^1] / central.Width;
                if (endType == "Boundary" && (lastRow == 0 || lastRow == central.Height - 1))
                {
                    int step = lastRow == 0 ? -world.Width : world.Width;
                    while (world.Water[path[^1]] == WaterType.None)
                    {
                        int next = path[^1] + step;
                        if ((uint)next >= world.Count) break;
                        path.Add(next); tiers.Add(tiers[^1]); flow.Add(flow[^1]);
                    }
                    if (world.Water[path[^1]] == WaterType.Ocean) endType = "Ocean";
                }
                return r with { Path = path.ToArray(), Tiers = tiers.ToArray(), AnnualFlow = flow.ToArray(), EndType = endType };
            }).ToArray();
            WriteRivers(world, rivers);
            world.RebuildCoast();
        }
        world.RiverDiagnostics = central.RiverDiagnostics;
        return world;
    }

    private static void CopyCentralFields(WorldData source, WorldData destination, int destinationOffset)
    {
        source.Water.CopyTo(destination.Water, destinationOffset);
        source.Elevation.CopyTo(destination.Elevation, destinationOffset);
        source.Relief.CopyTo(destination.Relief, destinationOffset);
        source.Biomes.CopyTo(destination.Biomes, destinationOffset);
        source.Temperature.CopyTo(destination.Temperature, destinationOffset);
        source.Rainfall.CopyTo(destination.Rainfall, destinationOffset);
        source.RoadEdges.CopyTo(destination.RoadEdges, destinationOffset);
        source.RiverEdges.CopyTo(destination.RiverEdges, destinationOffset);
        source.Flags.CopyTo(destination.Flags, destinationOffset);
        source.LockMask.CopyTo(destination.LockMask, destinationOffset);
    }

    private static void GrowPolarCoasts(WorldData central, WorldData world, int padding)
    {
        uint seed = unchecked((uint)central.Manifest.Seed);
        float aspect = central.Width / (float)central.Height;
        for (int side = 0; side < 2; side++)
        for (int col = 0; col < world.Width; col++)
        {
            int source = side == 0 ? col : (central.Height - 1) * world.Width + col;
            float x = col / (float)central.Height;
            float noise = TerrainNoise(x, side * 3.7f, 7f, aspect, central.Manifest.HorizontalWrap, seed ^ 0x81B4E397u);
            float detail = TerrainNoise(x, side * 7.1f, 23f, aspect, central.Manifest.HorizontalWrap, seed ^ 0xC36F179Bu);
            float broad = Smooth(Math.Clamp((noise - 0.2f) / 0.6f, 0, 1));
            float reach = Math.Max(1, padding - 2) * (0.15f + broad * 0.65f + detail * 0.10f);
            for (int depth = 1; depth <= padding; depth++)
            {
                int row = side == 0 ? padding - depth : padding + central.Height - 1 + depth;
                int i = row * world.Width + col;
                bool land = central.Water[source] == WaterType.None && depth < reach;
                float fade = Math.Clamp(1 - depth / Math.Max(1, reach), 0, 1);
                world.Water[i] = land ? WaterType.None : WaterType.Ocean;
                world.Elevation[i] = land ? (short)Math.Max(1, central.Elevation[source] * Smooth(fade))
                    : (short)-Math.Clamp(200 + (depth - reach) * 800, 1, 6000);
                world.Relief[i] = !land ? ReliefType.Flat : world.Elevation[i] switch
                { >= 3200 => ReliefType.Peak, >= 1850 => ReliefType.Mountains, >= 650 => ReliefType.Hills, >= 220 => ReliefType.Rolling, _ => ReliefType.Flat };
                world.Biomes[i] = land ? central.Biomes[source] : BiomeType.None;
                world.Temperature[i] = (short)Math.Min(central.Temperature[source], -160 - depth * 60 / padding);
                world.Rainfall[i] = land ? central.Rainfall[source] : (ushort)0;
            }
        }
    }

    private static void RegeneratePolarRegion(WorldData world, WorldRegion region, HexLocks fields, Action<GenerationProgress>? progress)
    {
        int padding = world.Manifest.Generation.PolarOceanRows;
        var central = new WorldData(CentralManifest(world.Manifest));
        int shift = padding * world.Width;
        for (int i = 0; i < central.Count; i++)
        {
            int j = i + shift;
            central.Water[i] = world.Water[j]; central.Elevation[i] = world.Elevation[j];
            central.Relief[i] = world.Relief[j]; central.Biomes[i] = world.Biomes[j];
            central.Temperature[i] = world.Temperature[j]; central.Rainfall[i] = world.Rainfall[j];
            central.Flags[i] = world.Flags[j]; central.LockMask[i] = world.LockMask[j];
        }
        central.Tectonics = TectonicReference.Create(central.Manifest);
        int top = Math.Max(region.Row, padding), bottom = Math.Min(region.Row + region.Height, padding + central.Height);
        if (bottom > top)
            RegenerateRegion(central, new(region.Column, top - padding, region.Width, bottom - top), fields, progress);
        var rebuilt = AddPolarRows(central, world.Manifest);
        foreach (int i in region.Cells(world.Width))
        {
            HexLocks allowed = fields & ~(HexLocks)world.LockMask[i];
            if ((allowed & HexLocks.Water) != 0) world.Water[i] = rebuilt.Water[i];
            if ((allowed & HexLocks.Elevation) != 0) world.Elevation[i] = rebuilt.Elevation[i];
            if ((allowed & HexLocks.Relief) != 0) world.Relief[i] = rebuilt.Relief[i];
            if ((allowed & HexLocks.Biome) != 0) world.Biomes[i] = rebuilt.Biomes[i];
            if ((allowed & HexLocks.Temperature) != 0) world.Temperature[i] = rebuilt.Temperature[i];
            if ((allowed & HexLocks.Rainfall) != 0) world.Rainfall[i] = rebuilt.Rainfall[i];
        }
        foreach (int i in region.Cells(world.Width))
            if (((HexLocks)world.LockMask[i] & HexLocks.Flags) == 0) world.RefreshCoast(i);
    }
}
