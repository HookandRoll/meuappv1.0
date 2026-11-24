using System;
using System.IO;
using System.Linq;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    internal static void CheckAnnualClimate()
    {
        var fixture = CheckManifest(128, 96, false) with { GeneratorVersion = 55 };
        var flat = new WorldData(fixture);
        Array.Fill(flat.Elevation, (short)100);
        var ridge = new WorldData(fixture);
        Array.Fill(ridge.Elevation, (short)100);
        for (int y = 0; y < flat.Height; y++)
        for (int x = 0; x < flat.Width; x++)
        {
            int cell = y * flat.Width + x;
            if (x >= 75)
            {
                flat.Water[cell] = ridge.Water[cell] = WaterType.Ocean;
                flat.Elevation[cell] = ridge.Elevation[cell] = -1000;
            }
            else ridge.Elevation[cell] += (short)Math.Max(0, 2400 - Math.Abs(x - 62) * 600);
        }
        void ClimateFixture(WorldData world)
        {
            var regions = GeographicRegions.ClassifyClimateSurface(world, world.Water, world.Elevation);
            BuildAnnualClimate(world, world.Water, world.Elevation, regions, 1234, null, (cell, temp, rain) =>
            { world.Temperature[cell] = temp; world.Rainfall[cell] = rain; });
        }
        ClimateFixture(flat); ClimateFixture(ridge);
        int Sample(int x, int y) => y * flat.Width + x;
        double Mean(WorldData w, int x) => Enumerable.Range(40, 16).Average(y => w.Rainfall[Sample(x, y)]);
        if (flat.Temperature[Sample(62, 48)] - ridge.Temperature[Sample(62, 48)] != 156 ||
            Mean(ridge, 64) <= Mean(flat, 64) || Mean(ridge, 56) >= Mean(flat, 56))
            throw new Exception("Expected lapse rate, windward rain and a drier lee.");

        var wetSouth = new WorldData(fixture);
        var lakeSouth = new WorldData(fixture);
        var dry = new WorldData(fixture);
        for (int i = wetSouth.Width * 55; i < wetSouth.Count; i++)
        {
            wetSouth.Water[i] = WaterType.Ocean;
            lakeSouth.Water[i] = WaterType.Lake;
        }
        ClimateFixture(wetSouth); ClimateFixture(lakeSouth); ClimateFixture(dry);
        int northOfWater = Sample(64, 53);
        if (wetSouth.Rainfall[northOfWater] <= lakeSouth.Rainfall[northOfWater] ||
            lakeSouth.Rainfall[northOfWater] <= dry.Rainfall[northOfWater])
            throw new Exception("Marine water and lakes should both transport moisture north, with a weaker lake source.");

        var current = fixture with { GeneratorVersion = 59 };
        foreach (var (width, height, oldReach, expectedReach) in new[]
        {
            (128, 64, 2, 2), (256, 128, 2, 2), (512, 256, 2, 2), (1024, 512, 4, 5),
            (2048, 1024, 8, 10), (4096, 2048, 16, 21), (WorldData.MaxWidth, 2784, 24, 31)
        })
        {
            int reach = AnnualWind.LateralReach(width, height, Version);
            float oldShare = AnnualWind.WindwardLocalShare(width, height, 61);
            float newShare = AnnualWind.WindwardLocalShare(width, height, Version);
            float scale = oldReach == 2 ? 1f : 1.30f;
            if (AnnualWind.LateralReach(width, height, 61) != oldReach ||
                Math.Abs((1 - newShare) / newShare - scale * (1 - oldShare) / oldShare) > 0.0001f)
                throw new Exception("Lateral and downhill moisture must scale from the previous recipe.");
            var rowVapor = new float[width];
            var rowSum = new double[width + 1];
            var rowWater = new WaterType[width];
            var rowElevation = new short[width];
            int left = 3, right = width - 3;
            rowVapor[left] = 1;
            AnnualWind.SpreadAcrossRow(rowVapor, rowSum, rowWater, rowElevation, 0, left, right, reach, true);
            if (reach != expectedReach || rowVapor.Count(value => value > 0) != 1 + 2 * expectedReach ||
                rowVapor[right - reach] <= 0 || Math.Abs(rowVapor.Sum() - 1) > 0.00001f)
                throw new Exception("Lateral vapor must spread farther on larger maps, conserve moisture and cross the atlas seam.");

            Array.Clear(rowVapor);
            rowVapor[left] = 1;
            rowElevation[right - 1] = 2800;
            AnnualWind.SpreadAcrossRow(rowVapor, rowSum, rowWater, rowElevation, 0, left, right, reach, true);
            if (rowVapor[right - 2] != 0 || Math.Abs(rowVapor.Sum() - 1) > 0.00001f)
                throw new Exception("Lateral spreading must not jump across a ridge or discard vapor at its edge.");
            Console.WriteLine($"LATERAL VAPOR {width}x{height}: radius={oldReach}->{reach} hexes");
        }

        var atlas = current with { Generation = current.Generation with { Extent = MapExtent.World } };
        foreach (var (row, waterOnWest) in new[] { (58, true), (73, false) })
        {
            WorldData SeamFixture(int version, bool hasWater)
            {
                var world = new WorldData(atlas with { GeneratorVersion = version });
                Array.Fill(world.Elevation, (short)100);
                if (hasWater)
                {
                    for (int y = row - 3; y <= row + 3; y++)
                    {
                        var (left, right) = world.Domain.RowBounds(y);
                        int start = waterOnWest ? left : right - 8;
                        for (int col = start; col < start + 8; col++)
                        {
                            int cell = Sample(col, y);
                            world.Water[cell] = WaterType.Ocean;
                            world.Elevation[cell] = -1000;
                        }
                    }
                }
                ClimateFixture(world);
                return world;
            }

            var connected = SeamFixture(58, true);
            var disconnected = SeamFixture(57, true);
            var dryAtlas = SeamFixture(58, false);
            var (left, right) = connected.Domain.RowBounds(row);
            int edge = Sample(waterOnWest ? right - 1 : left, row);
            if (left == 0 || right == connected.Width || disconnected.Rainfall[edge] != dryAtlas.Rainfall[edge] ||
                connected.Rainfall[edge] <= dryAtlas.Rainfall[edge])
                throw new Exception("Vapor must cross the curved atlas seam in both directions.");
            Console.WriteLine($"ATLAS WIND SEAM {(waterOnWest ? "west edge to east edge" : "east edge to west edge")}: {disconnected.Rainfall[edge]}->{connected.Rainfall[edge]} mm");
        }

        WorldData BarrierFixture(int summit, WorldManifest manifest)
        {
            var world = new WorldData(manifest);
            for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
            {
                int cell = y * world.Width + x;
                world.Water[cell] = x >= 75 ? WaterType.Ocean : WaterType.None;
                world.Elevation[cell] = (short)(x >= 75 ? -1000 : Math.Max(100, summit - Math.Abs(x - 62) * 700));
            }
            ClimateFixture(world);
            return world;
        }
        var open = BarrierFixture(100, current);
        var oldOcean = BarrierFixture(100, current with { GeneratorVersion = 58 });
        if (Mean(open, 100) >= Mean(oldOcean, 100) || Mean(open, 65) <= Mean(oldOcean, 65))
            throw new Exception("Lower ocean condensation must leave more vapor for land downwind.");
        Console.WriteLine($"OCEAN CONDENSATION mm: over water={Mean(oldOcean, 100):F1}->{Mean(open, 100):F1}; downwind land={Mean(oldOcean, 65):F1}->{Mean(open, 65):F1}");
        var low = BarrierFixture(1800, current);
        var slope = BarrierFixture(2300, current);
        var wall = BarrierFixture(2800, current);
        var unspread = BarrierFixture(2800, current with { GeneratorVersion = 56 });
        var oldLow = BarrierFixture(1800, fixture);
        var noSource = new WorldData(current);
        ClimateFixture(noSource);
        double downstream = Mean(open, 55);
        double lowRain = Mean(low, 55), slopeRain = Mean(slope, 55), wallRain = Mean(wall, 55);
        if (Math.Abs(downstream - lowRain) > 2 || !(lowRain > slopeRain && slopeRain > wallRain) ||
            lowRain <= Mean(oldLow, 55) || Math.Abs(wallRain - Mean(noSource, 55)) > 1 ||
            LandVaporTransmission(1800) != 1 || LandVaporTransmission(2800) != 0)
            throw new Exception($"Unexpected barrier response: flat={downstream:F1}, 1800={lowRain:F1}, 2300={slopeRain:F1}, 2800={wallRain:F1}.");
        var strongerLake = new WorldData(current);
        for (int i = strongerLake.Width * 55; i < strongerLake.Count; i++) strongerLake.Water[i] = WaterType.Lake;
        ClimateFixture(strongerLake);
        if (strongerLake.Rainfall[northOfWater] <= lakeSouth.Rainfall[northOfWater])
            throw new Exception("The larger lake vapor supply did not increase downwind rain.");
        Console.WriteLine($"BARRIER downwind mm: flat={downstream:F1}, 1800={lowRain:F1}, 2300={slopeRain:F1}, 2800={wallRain:F1}; lake={lakeSouth.Rainfall[northOfWater]}->{strongerLake.Rainfall[northOfWater]}");
        if (Mean(wall, 62) >= Mean(unspread, 62) || Mean(wall, 66) <= Mean(unspread, 66) ||
            Math.Abs(wallRain - Mean(unspread, 55)) > 1)
            throw new Exception("Redirected vapor should weaken the crest rain and wet the lower windward slope without crossing the barrier.");
        Console.WriteLine($"WINDWARD mm: crest={Mean(unspread, 62):F1}->{Mean(wall, 62):F1}; lower slope={Mean(unspread, 66):F1}->{Mean(wall, 66):F1}");
        var wetOpen = BarrierFixture(100, current with { GeneratorVersion = LegacyAnnualVersion });
        var wetWall = BarrierFixture(2800, current with { GeneratorVersion = LegacyAnnualVersion });
        if (Mean(wetOpen, 55) - Mean(wetWall, 55) < downstream - wallRain - 2)
            throw new Exception("The wetter continental climate must retain the rain shadow.");

        var basin = new WorldData(current);
        Array.Fill(basin.Elevation, (short)1500);
        int source = Sample(64, 48), dryBank = source - 1, wetBank = source + 1;
        basin.Elevation[dryBank] = basin.Elevation[wetBank] = 1000;
        var vapor = new float[basin.Count];
        var rain = new float[basin.Count];
        vapor[source] = 1;
        rain[wetBank] = 0.5f;
        SpreadWindwardVapor(basin, basin.Water, basin.Elevation, vapor, rain);
        if (Math.Abs(rain.Sum() - 1.5f) > 0.0001f || rain[dryBank] <= rain[wetBank] - 0.5f ||
            rain[Sample(64, 47)] != 0)
            throw new Exception("Windward spreading must conserve diverted vapor, favor drier terrain and stay downhill.");

        foreach (bool wrap in new[] { false, true })
        {
            var m = CheckManifest(128, 64, wrap);
            m = m with { HorizontalWrap = false, Generation = m.Generation with { Extent = MapExtent.World } };
            var standard = Create(m);
            var previous = Create(m with { GeneratorVersion = 61 });
            var spread = Create(m with { GeneratorVersion = 62 });
            if (!spread.Rainfall.SequenceEqual(previous.Rainfall))
                throw new Exception("Spreading should stay unchanged on small atlases.");
            var legacy = Create(m with { GeneratorVersion = 54 });
            if (!standard.Water.SequenceEqual(legacy.Water) || !standard.Elevation.SequenceEqual(legacy.Elevation))
                throw new Exception("The new climate changed the physical surface.");
            var warmer = Create(m with { Generation = m.Generation with { TemperatureOffsetC = 5 } });
            var wetter = Create(m with { Generation = m.Generation with { PrecipitationOffsetPercent = 10 } });
            var cooler = Create(m with { Generation = m.Generation with { TemperatureOffsetC = -5 } });
            var drier = Create(m with { Generation = m.Generation with { PrecipitationOffsetPercent = -10 } });
            if (warmer.Rainfall.Sum(value => (long)value) <= cooler.Rainfall.Sum(value => (long)value))
                throw new Exception("Warmer water did not supply more annual moisture.");
            foreach (int cell in Enumerable.Range(0, standard.Count).Where(cell => standard.Domain.Contains(cell)))
            {
                if (warmer.Temperature[cell] - standard.Temperature[cell] != 50 ||
                    cooler.Temperature[cell] - standard.Temperature[cell] != -50 ||
                    wetter.Temperature[cell] != standard.Temperature[cell] ||
                    drier.Temperature[cell] != standard.Temperature[cell] ||
                    Math.Abs(wetter.Rainfall[cell] - standard.Rainfall[cell] * 1.10f) > 1.1f ||
                    Math.Abs(drier.Rainfall[cell] - standard.Rainfall[cell] * 0.90f) > 1.1f)
                    throw new Exception("Climate controls changed unrelated fields or used the wrong scaling.");
            }
            var region = new WorldRegion(40, 22, 30, 18);
            var originalRain = (ushort[])wetter.Rainfall.Clone();
            int locked = 23 * wetter.Width + 50;
            wetter.Rainfall[locked] = 123;
            wetter.LockMask[locked] = (ushort)HexLocks.Rainfall;
            RegenerateRegion(wetter, region, RegenerationMode.Everything);
            originalRain[locked] = 123;
            if (!originalRain.SequenceEqual(wetter.Rainfall)) throw new Exception("Climate regeneration changed its seam or ignored a lock.");

            string file = Path.Combine(Path.GetTempPath(), $"annual-climate-{Guid.NewGuid():N}.world");
            try
            {
                WorldFile.Save(file, wetter, null);
                if (WorldFile.Load(file).Manifest.Generation.PrecipitationOffsetPercent != 10)
                    throw new Exception("The save lost its climate setting.");
            }
            finally { File.Delete(file); }
        }
        Console.WriteLine("PASS annual climate: altitude, windward rain, rain shadow, cross-latitude moisture, frozen geography, controls, locks and saves");
        var size = MapDomain.ExpandedSize(512, 256);
        var larger = CheckManifest(size.Width, size.Height, false);
        larger = larger with { Generation = larger.Generation with
            { Extent = MapExtent.World, ReferenceWidth = 512, ReferenceHeight = 256 } };
        var oldAtlas = Create(larger with { GeneratorVersion = 61 });
        var newAtlas = Create(larger with { GeneratorVersion = 62 });
        if (!oldAtlas.Water.SequenceEqual(newAtlas.Water) || !oldAtlas.Elevation.SequenceEqual(newAtlas.Elevation) ||
            oldAtlas.Rainfall.SequenceEqual(newAtlas.Rainfall))
            throw new Exception("The larger spread must reach generated rainfall without changing the atlas surface.");
        Console.WriteLine($"PASS atlas rain spread {size.Width}x{size.Height}: changed rainfall, preserved geography");
        CheckWaterMoisture();
        CheckContinentalRainfall();
    }

    private static void CheckWaterMoisture()
    {
        var manifest = CheckManifest(96, 64, false);
        var world = new WorldData(manifest);
        Array.Fill(world.Elevation, (short)100);
        Array.Fill(world.Temperature, (short)160);
        Array.Fill(world.Rainfall, (ushort)240);
        Array.Fill(world.Biomes, BiomeType.Desert);
        int sea = 25 * world.Width + 18, lake = 25 * world.Width + 48, river = 25 * world.Width + 75;
        world.Water[sea] = WaterType.Ocean;
        world.Water[lake] = WaterType.Lake;
        int Neighbor(int cell, int direction, int steps = 1) => HexGrid.NeighborIndex(
            HexGrid.FromOffset(cell % world.Width, cell / world.Width), direction, world.Width, world.Height, false, steps);

        var moisture = SpreadWaterMoisture(world, world.Water, world.Elevation);
        foreach (var (source, reach) in new[] { (sea, 6), (lake, 4) })
        {
            for (int direction = 0; direction < 6; direction++)
            {
                if (moisture[Neighbor(source, direction)] != reach || moisture[Neighbor(source, direction, reach)] != 1 ||
                    moisture[Neighbor(source, direction, reach + 1)] != 0)
                    throw new Exception("Water moisture must decay equally in all six directions.");
            }
        }
        ushort[] CalculateRain(WorldData sample)
        {
            var regions = GeographicRegions.ClassifyClimateSurface(sample, world.Water, world.Elevation);
            var rain = new ushort[world.Count];
            BuildAnnualClimate(sample, world.Water, world.Elevation, regions, 1234, null,
                (cell, _, precipitation) => rain[cell] = precipitation);
            return rain;
        }
        var oldRain = CalculateRain(new WorldData(manifest with { GeneratorVersion = 62 }));
        var newRain = CalculateRain(world);
        if (newRain[Neighbor(sea, 0)] - oldRain[Neighbor(sea, 0)] != 180 ||
            newRain[Neighbor(lake, 3)] - oldRain[Neighbor(lake, 3)] != 120 ||
            newRain[Neighbor(lake, 3, 5)] != oldRain[Neighbor(lake, 3, 5)])
            throw new Exception("The local ocean and lake halos must add precipitation on both sides of the wind.");

        for (int d = 0; d < 6; d++) world.Elevation[Neighbor(lake, d)] = 2800;
        if (SpreadWaterMoisture(world, world.Water, world.Elevation)[Neighbor(lake, 0, 2)] != 0)
            throw new Exception("Local moisture crossed a high ridge.");

        WriteRivers(world, new[] { new RiverRecord(0, new[] { river, Neighbor(river, 0) },
            new byte[] { 1 }, new float[] { 1 }, "Custom", null) });
        var riverMoisture = SpreadWaterMoisture(world, world.Water, world.Elevation, fromRivers: true);
        int bank = Neighbor(river, 3), distant = Neighbor(river, 3, 3);
        if (riverMoisture[river] != 3 || riverMoisture[bank] != 2 || riverMoisture[distant] != 0)
            throw new Exception("River moisture must decay beyond the riverbank.");
        ApplyContextBiomes(world, new WorldRegion(0, 0, world.Width, world.Height), updateFeatures: true);
        if (world.Biomes[bank] != BiomeType.Steppe || world.Biomes[distant] != BiomeType.Desert || world.Rainfall[bank] != 240)
            throw new Exception("River moisture must affect nearby biomes without recycling river water into rainfall.");
        Console.WriteLine("PASS windless moisture: coast, lake and river, six directions, bounded reach, ridge and riparian biome");
    }

    private static void CheckContinentalRainfall()
    {
        if (LimitColdDesert(BiomeType.Desert, 50, Version) != BiomeType.Desert ||
            LimitColdDesert(BiomeType.Desert, 49, Version) != BiomeType.Steppe ||
            LimitColdDesert(BiomeType.Desert, -55, Version) != BiomeType.Tundra ||
            LimitColdDesert(BiomeType.Desert, 49, 60) != BiomeType.Desert)
            throw new Exception("The 5 °C desert threshold or legacy behavior changed.");

        foreach (int seed in new[] { 1234, 1, -77 })
        {
            var manifest = CheckManifest(384, 192, false) with { Seed = seed };
            manifest = manifest with { Generation = manifest.Generation with
                { Extent = MapExtent.World, LandPercent = 25, CoastDetail = 80 } };
            foreach (var (temperature, precipitation) in new[] { (0, 0), (-5, 10) })
            {
                var scenario = manifest with { Generation = manifest.Generation with
                    { TemperatureOffsetC = temperature, PrecipitationOffsetPercent = precipitation } };
                var previous = Create(scenario with { GeneratorVersion = 59 });
                var current = Create(scenario);
                var land = Enumerable.Range(0, current.Count).Where(i =>
                    current.Domain.CountsTowardLandQuota(i) && current.Water[i] == WaterType.None).ToArray();
                double previousRain = land.Average(i => previous.Rainfall[i]);
                double currentRain = land.Average(i => current.Rainfall[i]);
                double previousDry = land.Count(i => previous.Rainfall[i] < 480) / (double)land.Length;
                double currentDry = land.Count(i => current.Rainfall[i] < 480) / (double)land.Length;
                double previousForest = land.Count(i => Biomes.IsForest(previous.Biomes[i])) / (double)land.Length;
                double currentForest = land.Count(i => Biomes.IsForest(current.Biomes[i])) / (double)land.Length;
                if (land.Any(i => current.Biomes[i] == BiomeType.Desert && current.Temperature[i] < 50))
                    throw new Exception("A desert was generated below 5 °C.");
                Console.WriteLine($"LAND PRECIP seed={seed} T={temperature} P={precipitation}: {previousRain:F0}->{currentRain:F0} mm; dry={previousDry:P0}->{currentDry:P0}; forest={previousForest:P0}->{currentForest:P0}");
                if (currentRain <= previousRain || previousDry - currentDry < 0.10 || currentDry < 0.05 ||
                    currentForest <= previousForest ||
                    !previous.Water.SequenceEqual(current.Water) || !previous.Elevation.SequenceEqual(current.Elevation) ||
                    !previous.Temperature.SequenceEqual(current.Temperature))
                    throw new Exception("Land precipitation must improve while preserving dry zones, temperature and geography.");

                if (seed == 1234 && temperature == -5)
                {
                    int cell = land.First(i => current.Temperature[i] < 50 && current.Rainfall[i] < 260);
                    var region = new WorldRegion(cell % current.Width, cell / current.Width, 1, 1);
                    current.Biomes[cell] = BiomeType.Desert;
                    current.LockMask[cell] = (ushort)HexLocks.Biome;
                    RegenerateRegion(current, region, RegenerationMode.Everything, HexLocks.Biome);
                    if (current.Biomes[cell] != BiomeType.Desert)
                        throw new Exception("A manually locked biome was changed.");
                    current.LockMask[cell] = 0;
                    RegenerateRegion(current, region, RegenerationMode.Everything, HexLocks.Biome);
                    if (current.Biomes[cell] == BiomeType.Desert)
                        throw new Exception("Regional regeneration generated a cold desert.");
                }
            }
        }
    }
}
