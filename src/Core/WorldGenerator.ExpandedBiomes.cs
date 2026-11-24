using System;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    private static BiomeType LimitColdDesert(BiomeType biome, short temperature, int version) =>
        version >= 61 && biome == BiomeType.Desert && temperature < 50
            ? (temperature <= -55 ? BiomeType.Tundra : BiomeType.Steppe)
            : biome;

    private static BiomeType LimitDryBiomeClimate(BiomeType biome, short temperature, ushort rain, int version)
    {
        biome = LimitColdDesert(biome, temperature, version);
        if (version < TerrainRainVersion) return biome;
        if ((biome is BiomeType.Desert or BiomeType.Badlands) && rain >= 150)
            biome = temperature is >= 120 and < 225 && rain < 680 ? BiomeType.Shrubland
                : rain < 480 ? BiomeType.Steppe : temperature >= 200 ? BiomeType.Savanna : BiomeType.Grassland;
        if (biome == BiomeType.Grassland && temperature >= 200) return BiomeType.Savanna;
        if (biome == BiomeType.Savanna && temperature < 200) return BiomeType.Grassland;
        return biome;
    }

    private static bool ProtectedHabitat(BiomeType biome) => biome is BiomeType.None or BiomeType.Beach or BiomeType.Alpine or
        BiomeType.Glacier or BiomeType.Mangrove or BiomeType.Swamp or BiomeType.Bog or BiomeType.Oasis or BiomeType.Volcanic or BiomeType.SaltFlat;
    private static BiomeType ChooseExpandedBiome(short h, short t, ushort p, int x, int y, int width, int height, bool wrap, uint seed, bool forests = true, int version = BalancedRainVersion)
    {
        float region = TerrainNoise(x / (float)height, y / (float)height, 9, width / (float)height, wrap, seed ^ 0x719A35CDu);
        int alpine = 3800 + (int)(200 * region);
        if (h >= alpine && t <= 35) return BiomeType.Alpine;
        if (t <= -55) return BiomeType.Tundra;
        if (t <= 45 && p >= 400 && forests) return BiomeType.Taiga;
        if (p < (version >= TerrainRainVersion ? 150 : 260)) return BiomeType.Desert;
        if (t is >= 120 and < 225 && p >= (version >= TerrainRainVersion ? 150 : 360) && p < 680 && region > 0.46f) return BiomeType.Shrubland;
        if (p < 480) return BiomeType.Steppe;
        if (t >= 225 && p >= 1550 && forests) return BiomeType.Rainforest;
        if (t is > 45 and < 225 && p >= 1850 && forests) return BiomeType.TemperateRainforest;
        if (t >= 200 && p >= 1050 && forests) return BiomeType.SeasonalForest;
        if (t >= 200 && p < 1050) return BiomeType.Savanna;
        if (t > 45 && p >= 1000 && forests) return BiomeType.TemperateForest;
        return version >= TerrainRainVersion && t >= 200 ? BiomeType.Savanna : BiomeType.Grassland;
    }

    private static void ApplyContextBiomes(WorldData world, WorldRegion region, bool updateFeatures, bool updateBiomes = true,
        Func<int, BiomeType>? baseBiome = null)
    {
        var ice = PolarIceField.Enabled(world.Manifest) ? new PolarIceField(world.Manifest) : null;
        uint seed = unchecked((uint)world.Manifest.Seed);
        byte[]? riverMoisture = world.Manifest.GeneratorVersion >= 63 && world.HexToRivers.Count > 0
            ? SpreadWaterMoisture(world, world.Water, world.Elevation, fromRivers: true) : null;
        int moistureScale = WaterMoistureScale(world.Height);
        // Evaluate the halo against one immutable source before writing. Regional
        // regeneration and traversal order therefore produce the same vote.
        (BiomeType Biome, GeographicFeatures Features) Evaluate(int i)
        {
            if (!world.Domain.Contains(i) || world.Water[i] != WaterType.None) return (world.Biomes[i], world.Features[i]);
            int h = world.Elevation[i], t = world.Temperature[i], p = world.Rainfall[i];
            var biome = baseBiome?.Invoke(i) ?? world.Biomes[i];
            int moisture = riverMoisture is null ? 0 : riverMoisture[i] * MoisturePerLevel / moistureScale;
            var features = world.Features[i] & ~(GeographicFeatures.Canyon | GeographicFeatures.BadlandRocks);
            if (world.Manifest.GeneratorVersion >= 47 && biome == BiomeType.Badlands)
                biome = ChooseExpandedBiome((short)h, (short)t, (ushort)p, i % world.Width, i / world.Width,
                    world.Width, world.Height, world.Manifest.HorizontalWrap, seed, version: world.Manifest.GeneratorVersion);
            if (ice?.IsPolar(i) ?? false) biome = BiomeType.Glacier;
            else if (biome != BiomeType.Volcanic)
            {
                var hex = HexGrid.FromOffset(i % world.Width, i / world.Width);
                bool fresh = world.RiverEdges[i] != 0, sea = false, sheltered = false, flat = world.Relief[i] <= ReliefType.Rolling;
                int lower = 0, higher = 0; bool enclosed = true;
                for (int d = 0; d < 6; d++)
                {
                    int n = HexGrid.NeighborIndex(hex, d, world.Width, world.Height, world.Manifest.HorizontalWrap);
                    if (!world.Domain.Contains(n)) { enclosed = false; continue; }
                    sea |= world.Water[n] == WaterType.Ocean;
                    if (world.Water[n] == WaterType.Ocean)
                    {
                        int banks = 0; var coastHex = HexGrid.FromOffset(n % world.Width, n / world.Width);
                        for (int side = 0; side < 6; side++)
                        {
                            int bank = HexGrid.NeighborIndex(coastHex, side, world.Width, world.Height, world.Manifest.HorizontalWrap);
                            if (world.Domain.Contains(bank) && world.Water[bank] == WaterType.None) banks++;
                        }
                        sheltered |= banks >= 3;
                    }
                    fresh |= world.Water[n] is WaterType.Lake or WaterType.Wetland;
                    if (world.Water[n] == WaterType.None)
                    { higher += world.Elevation[n] > h + 120 ? 1 : 0; lower += world.Elevation[n] < h - 60 ? 1 : 0; }
                    else enclosed = false;
                }
                float stagger = world.Manifest.GeneratorVersion >= 47 ? (i / world.Width & 1) * 0.5f : 0;
                float field = TerrainNoise((i % world.Width + stagger) / world.Height, i / world.Width / (float)world.Height,
                    23, world.Width / (float)world.Height, world.Manifest.HorizontalWrap, seed ^ 0x915AC37Du);
                bool eroded = lower >= 2;
                if (world.Manifest.GeneratorVersion >= 47)
                {
                    // Two-row sampling removes odd/even-row threshold aliasing.
                    // Badlands describe a region of eroded relief, not each
                    // individual cell that has two lower immediate neighbors.
                    int low = h, high = h;
                    int reach = Math.Max(2, world.Height / 192);
                    for (int d = 0; d < 6; d++)
                    {
                        int n = HexGrid.NeighborIndex(hex, d, world.Width, world.Height, world.Manifest.HorizontalWrap, reach);
                        if (!world.Domain.Contains(n) || world.Water[n] != WaterType.None) continue;
                        low = Math.Min(low, world.Elevation[n]); high = Math.Max(high, world.Elevation[n]);
                    }
                    eroded = high - low >= 120;
                }
                if (h <= 30 && t >= 195 && sea && (sheltered || world.RiverEdges[i] != 0) && flat && (p >= 950 || world.RiverEdges[i] != 0) && field > 0.28f) biome = BiomeType.Mangrove;
                else if ((biome is BiomeType.Desert or BiomeType.Badlands) && fresh && t > 45 && field > 0.3f) biome = BiomeType.Oasis;
                else if (h <= 260 && t > 55 && IsSwampMargin(world, i)) biome = BiomeType.Swamp;
                else if (h <= 260 && t is > -55 and <= 55 && fresh && flat && p + moisture >= 850 && field > 0.3f) biome = BiomeType.Bog;
                else if (flat && enclosed && !fresh && p < 280 && t > 45 && higher == 6 && field > 0.6f) biome = BiomeType.SaltFlat;
                else if (p < (world.Manifest.GeneratorVersion >= TerrainRainVersion ? 150 : 350) && eroded && t > -55 && field > 0.48f) biome = BiomeType.Badlands;
                else if (moisture > 0 && (biome is BiomeType.Desert or BiomeType.Steppe or BiomeType.Shrubland or BiomeType.Grassland or BiomeType.Savanna))
                {
                    var dry = ChooseExpandedBiome((short)h, (short)t, (ushort)p, i % world.Width, i / world.Width,
                        world.Width, world.Height, world.Manifest.HorizontalWrap, seed, version: world.Manifest.GeneratorVersion);
                    var wet = ChooseExpandedBiome((short)h, (short)t, (ushort)Math.Min(ushort.MaxValue, p + moisture),
                        i % world.Width, i / world.Width, world.Width, world.Height, world.Manifest.HorizontalWrap, seed, version: world.Manifest.GeneratorVersion);
                    if (dry != wet) biome = wet;
                }
                if (world.RiverEdges[i] != 0 && h >= 600 && p < 1100 && higher >= 2) features |= GeographicFeatures.Canyon;
                if (biome == BiomeType.Badlands) features |= GeographicFeatures.BadlandRocks;
            }
            return (biome, features);
        }
        if (world.Manifest.GeneratorVersion < 47)
        {
            foreach (int i in region.Cells(world.Width))
            {
                var value = Evaluate(i); var locks = (HexLocks)world.LockMask[i];
                if (updateBiomes && (locks & HexLocks.Biome) == 0) world.Biomes[i] = value.Biome;
                if (updateFeatures && (locks & HexLocks.Features) == 0) world.Features[i] = value.Features;
            }
            return;
        }
        var candidates = new BiomeType[world.Count];
        int radius = Math.Clamp(world.Height / 192, 2, 12);
        int top = Math.Max(0, region.Row - radius * 2), bottom = Math.Min(world.Height, region.Row + region.Height + radius * 2);
        int left = Math.Max(0, region.Column - radius * 2), right = Math.Min(world.Width, region.Column + region.Width + radius * 2);
        if (world.Manifest.HorizontalWrap && (left == 0 || right == world.Width)) { left = 0; right = world.Width; }
        for (int y = top; y < bottom; y++) for (int x = left; x < right; x++)
        {
            int i = y * world.Width + x;
            var value = Evaluate(i); candidates[i] = value.Biome;
        }
        foreach (int i in region.Cells(world.Width))
        {
            if (!world.Domain.Contains(i) || world.Water[i] != WaterType.None) continue;
            var locks = (HexLocks)world.LockMask[i]; var biome = candidates[i];
            // Contextual badlands used to bypass the existing majority pass.
            // Thin erosion stripes revert to their surrounding climate habitat;
            // broad badlands remain. Never grow a dry habitat into wet banks.
            if (biome == BiomeType.Badlands && (locks & HexLocks.Biome) == 0)
            {
                var hex = HexGrid.FromOffset(i % world.Width, i / world.Width);
                int own = 0, regional = 0;
                for (int d = 0; d < 6; d++)
                {
                    int n = HexGrid.NeighborIndex(hex, d, world.Width, world.Height, world.Manifest.HorizontalWrap);
                    if (world.Domain.Contains(n) && candidates[n] == biome) own++;
                    int far = HexGrid.NeighborIndex(hex, d, world.Width, world.Height, world.Manifest.HorizontalWrap, radius);
                    if (world.Domain.Contains(far) && candidates[far] == biome) regional++;
                }
                if (own < 3 || regional < 2)
                    biome = world.Biomes[i] == BiomeType.Badlands
                        ? ChooseExpandedBiome(world.Elevation[i], world.Temperature[i], world.Rainfall[i], i % world.Width, i / world.Width,
                            world.Width, world.Height, world.Manifest.HorizontalWrap, seed, version: world.Manifest.GeneratorVersion)
                        : world.Biomes[i];
            }
            var feature = updateFeatures ? Evaluate(i).Features : GeographicFeatures.None;
            biome = LimitDryBiomeClimate(biome, world.Temperature[i], world.Rainfall[i], world.Manifest.GeneratorVersion);
            if (updateBiomes && (locks & HexLocks.Biome) == 0) world.Biomes[i] = biome;
            if (updateFeatures && (locks & HexLocks.Features) == 0)
            {
                var visible = updateBiomes && (locks & HexLocks.Biome) == 0 ? biome : world.Biomes[i];
                world.Features[i] = (feature & ~GeographicFeatures.BadlandRocks) |
                    (visible == BiomeType.Badlands ? GeographicFeatures.BadlandRocks : GeographicFeatures.None);
            }
        }
    }
}
