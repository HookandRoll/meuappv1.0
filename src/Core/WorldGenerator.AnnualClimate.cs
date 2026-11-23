using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    private static void ApplyAnnualClimate(WorldData world, GenerationArea area, uint seed, Action<GenerationProgress>? progress)
    {
        var water = world.Water;
        var elevation = world.Elevation;
        if (!area.IsInitialGeneration)
        {
            water = (WaterType[])water.Clone();
            elevation = (short[])elevation.Clone();
            VisitChunks(area.Chunks, (i, col, row) =>
            {
                int cell = area.WorldRow(row) * world.Width + area.WorldColumn(col);
                water[cell] = area.Water[i];
                elevation[cell] = area.Elevation[i];
            }, null, GenerationStep.Climate, 0, 0);
        }

        progress?.Invoke(new(GenerationStep.Climate, 0));
        var regions = GeographicRegions.ClassifyClimateSurface(world, water, elevation,
            cacheGeography: area.IsInitialGeneration && world.Manifest.Generation.Stage == GenerationStage.Terrain);
        BuildAnnualClimate(world, water, elevation, regions, seed, progress, (cell, temperature, rain) =>
        {
            int col = cell % world.Width, row = cell / world.Width;
            int local = area.Neighbor(HexGrid.FromOffset(col, row), 0, 0);
            if (local < 0) return;
            area.Temperature[local] = area.Writes(col, row, cell, HexLocks.Temperature) ? temperature : world.Temperature[cell];
            area.Rainfall[local] = area.Writes(col, row, cell, HexLocks.Rainfall) ? rain : world.Rainfall[cell];
        });
    }

    private static void BuildAnnualClimate(WorldData world, WaterType[] water, short[] elevation,
        RegionKind[] regions, uint seed, Action<GenerationProgress>? progress, Action<int, short, ushort> write)
    {
        int width = world.Width, height = world.Height;
        bool wrap = world.Manifest.HorizontalWrap;
        bool curvedWrap = AnnualWind.WrapsAtlas(world);
        bool windWrap = wrap || curvedWrap;
        bool elevationBarrier = world.Manifest.GeneratorVersion >= 56;
        bool scaleSpreading = world.Manifest.GeneratorVersion >= 59;
        bool retainLandPrecipitation = world.Manifest.GeneratorVersion >= 60;
        byte[]? stillWaterMoisture = world.Manifest.GeneratorVersion >= 63
            ? SpreadWaterMoisture(world, water, elevation) : null;
        float localRainPerLevel = MoisturePerLevel / (float)WaterMoistureScale(height);
        float aspect = width / (float)height;
        var settings = world.Manifest.Generation;
        var temperature = BuildAnnualTemperature(world, water, elevation, regions, seed);
        var rain = new float[world.Count];
        float[]? divertedVapor = world.Manifest.GeneratorVersion >= 57 ? new float[world.Count] : null;
        const float reflectedShare = 0.88f;
        float Latitude(int row) => (row + 0.5f) / height * 2 - 1;
        float SurfaceHeight(int cell) => water[cell] == WaterType.None ? Math.Max(0, (int)elevation[cell]) : 0;

        // Four directional sweeps mix zonal flow with cross-latitude flow.
        // Their weights vary by latitude so the prevailing wind turns gradually.
        float scale = Math.Max(1, height / 128f);
        int lateralReach = scaleSpreading ? AnnualWind.LateralReach(width, height, world.Manifest.GeneratorVersion) : 0;
        var passRain = new float[4][];
        var passReturns = new List<(int Cell, float Vapor)>?[4];
        Parallel.For(0, 4, pass =>
        {
            int dy = pass < 2 ? 1 : -1, dx = (pass & 1) == 0 ? 1 : -1;
            var previous = new float[width];
            var current = new float[width];
            double[]? rowSum = scaleSpreading ? new double[width + 1] : null;
            var rainfall = passRain[pass] = new float[world.Count];
            var returns = passReturns[pass] = divertedVapor is null ? null : new List<(int, float)>();
            for (int r = 0; r < height; r++)
            {
                int row = dy > 0 ? r : height - 1 - r;
                int upRow = row - dy;
                float signedLatitude = Latitude(row);
                float latitude = Math.Abs(signedLatitude);
                var (eastward, southward) = AnnualWind.DirectionWeights(row, height);
                float contribution = (dx > 0 ? eastward : 1 - eastward) * (dy > 0 ? southward : 1 - southward);
                float equatorialLift = 1 - Smooth(Math.Clamp(latitude / 0.29f, 0, 1));
                float stormLift = Math.Max(0, 1 - Math.Abs(latitude - 0.57f) / 0.23f);
                float background = (0.027f + 0.09f * equatorialLift + 0.067f * stormLift) / scale;
                float horizontalVapor = 0;
                float horizontalHeight = 0;
                var (left, right) = curvedWrap ? world.Domain.RowBounds(row) : (0, width);
                int length = right - left;
                var (upLeft, upRight) = curvedWrap && (uint)upRow < (uint)height ? world.Domain.RowBounds(upRow) : (0, width);
                int steps = windWrap ? length * 2 : length;
                for (int step = 0; step < steps; step++)
                {
                    int offset = step < length ? step : step - length;
                    int col = dx > 0 ? left + offset : right - 1 - offset;
                    bool record = !windWrap || step >= length;
                    if (!world.Domain.Contains(col, row))
                    {
                        current[col] = 0;
                        horizontalVapor = horizontalHeight = 0;
                        continue;
                    }
                    int cell = row * width + col;
                    // The adjacent row is offset by half a hex.
                    int upstream = curvedWrap
                        ? AnnualWind.ProjectColumn(col, left, length, upLeft, upRight - upLeft) : col;
                    int diagonal = upstream + ((row & 1) == 0 ? -1 : 1);
                    if (windWrap) diagonal = diagonal < upLeft ? upRight - 1 : diagonal >= upRight ? upLeft : diagonal;
                    bool northSouth = world.Domain.Contains(upstream, upRow);
                    bool diagonalValid = world.Domain.Contains(diagonal, upRow);
                    float verticalVapor = ((northSouth ? previous[upstream] : 0) + (diagonalValid ? previous[diagonal] : 0)) * 0.5f;
                    float localHeight = SurfaceHeight(cell);
                    float upstreamHeight = northSouth ? SurfaceHeight(upRow * width + upstream) : localHeight;
                    float diagonalHeight = diagonalValid ? SurfaceHeight(upRow * width + diagonal) : localHeight;
                    float vapor = AnnualWind.AlongLatitude * horizontalVapor + AnnualWind.AcrossLatitude * verticalVapor;
                    float warmth = Math.Clamp((temperature[cell] + 110f) / 390f, 0, 1);
                    float source = regions[cell] switch
                    {
                        RegionKind.Ocean => 1f,
                        RegionKind.Sea => 0.90f,
                        RegionKind.Lake when water[cell] == WaterType.Lake => elevationBarrier ? 0.60f : 0.30f,
                        _ => 0f
                    };
                    float recharge = elevationBarrier && water[cell] == WaterType.Lake ? 0.32f : 0.21f;
                    vapor += Math.Max(0, (0.12f + warmth * warmth * 0.94f) * source - vapor) * recharge / scale;
                    float condensation;
                    float reflected = 0;
                    if (elevationBarrier)
                    {
                        float through = LandVaporTransmission(localHeight);
                        float blockedHorizontal = AnnualWind.AlongLatitude * horizontalVapor * BarrierLoss(horizontalHeight, through);
                        float blockedVertical = northSouth ? (AnnualWind.AcrossLatitude * 0.5f) * previous[upstream] * BarrierLoss(upstreamHeight, through) : 0;
                        float blockedDiagonal = diagonalValid ? (AnnualWind.AcrossLatitude * 0.5f) * previous[diagonal] * BarrierLoss(diagonalHeight, through) : 0;
                        float blocked = blockedHorizontal + blockedVertical + blockedDiagonal;
                        if (divertedVapor is not null && blocked > 0 && contribution > 0 && record)
                        {
                            // Return vapor to the actual incoming cells so it stays
                            // on the windward side instead of jumping across the crest.
                            reflected = blocked * reflectedShare;
                            float returnWeight = contribution * scale * reflectedShare;
                            if (blockedHorizontal > 0)
                            {
                                int fromCol = col - dx;
                                if (windWrap) fromCol = fromCol < left ? right - 1 : fromCol >= right ? left : fromCol;
                                returns!.Add((row * width + fromCol, blockedHorizontal * returnWeight));
                            }
                            if (blockedVertical > 0) returns!.Add((upRow * width + upstream, blockedVertical * returnWeight));
                            if (blockedDiagonal > 0) returns!.Add((upRow * width + diagonal, blockedDiagonal * returnWeight));
                        }
                        // Ordinary rain still forms over lowlands. Use the sea-level
                        // equivalent air temperature so low ridges add no vapor loss.
                        float baseWarmth = Math.Clamp((temperature[cell] + localHeight * 0.065f + 110f) / 390f, 0, 1);
                        float capacity = 0.22f + baseWarmth * 0.95f;
                        float passing = Math.Max(0, vapor - blocked);
                        float rate = water[cell] == WaterType.None ? background * 0.55f : background;
                        condensation = through == 0 ? vapor :
                            blocked + passing * rate + Math.Max(0, passing - capacity) * 0.22f;
                    }
                    else
                    {
                        float verticalHeight = (upstreamHeight + diagonalHeight) * 0.5f;
                        float ascent = Math.Max(0, localHeight - (AnnualWind.AlongLatitude * horizontalHeight + AnnualWind.AcrossLatitude * verticalHeight));
                        float lift = Math.Min(0.55f, ascent / 1800);
                        float capacity = 0.22f + warmth * 0.95f;
                        condensation = vapor * (background + lift) + Math.Max(0, vapor - capacity) * 0.22f;
                    }
                    if (scaleSpreading && water[cell] == WaterType.Ocean)
                        condensation *= AnnualWind.OceanCondensationShare;
                    horizontalVapor = current[col] = Math.Max(0, vapor - condensation);
                    horizontalHeight = localHeight;
                    if (record) rainfall[cell] += contribution * (condensation - reflected) * scale;
                }
                if (rowSum is not null && r < height - 1)
                    AnnualWind.SpreadAcrossRow(current, rowSum, water, elevation, row * width,
                        left, right, lateralReach, windWrap);
                (previous, current) = (current, previous);
            }
        });
        // Replay individual returns in sweep order: summing per-pass totals changes float rounding.
        for (int pass = 0; pass < 4; pass++)
        {
            var rainfall = passRain[pass];
            Parallel.For(0, height, row =>
            {
                for (int cell = row * width; cell < (row + 1) * width; cell++) rain[cell] += rainfall[cell];
            });
            if (passReturns[pass] is { } returns)
                foreach (var returned in returns) divertedVapor![returned.Cell] += returned.Vapor;
            passRain[pass] = null!;
            passReturns[pass] = null;
            progress?.Invoke(new(GenerationStep.Climate, 20 + 18 * (pass + 1)));
        }

        if (divertedVapor is not null)
            SpreadWindwardVapor(world, water, elevation, divertedVapor, rain);

        Parallel.For(0, height, row =>
        {
            for (int col = 0; col < width; col++)
            {
                if (!world.Domain.Contains(col, row)) continue;
                int cell = row * width + col;
                float background = ModernRainBaseline((col + 0.5f) / width, (row + 0.5f) / height, aspect, 0, seed, wrap);
                float cold = Smooth(Math.Clamp((temperature[cell] + 180f) / 260f, 0.18f, 1));
                float backgroundShare = 0.20f;
                if (retainLandPrecipitation && water[cell] == WaterType.None)
                {
                    // Cold air still supplies less vapor, but condensation on land can fall as snow.
                    cold = Interpolate(0.75f, 1, cold);
                    backgroundShare = 0.50f;
                }
                float localRain = stillWaterMoisture is null ? 0 : stillWaterMoisture[cell] * localRainPerLevel;
                float precipitation = Math.Min(5000, (background * backgroundShare + 22000 * rain[cell]) * cold + localRain);
                precipitation *= 1 + settings.PrecipitationOffsetPercent / 100f;
                write(cell, temperature[cell], (ushort)MathF.Round(precipitation));
            }
        });
        progress?.Invoke(new(GenerationStep.Climate, 100));
    }

    private static short[] BuildAnnualTemperature(WorldData world, WaterType[] water, short[] elevation,
        RegionKind[] regions, uint seed)
    {
        int width = world.Width, height = world.Height;
        bool wrap = world.Manifest.HorizontalWrap;
        float aspect = width / (float)height;
        var settings = world.Manifest.Generation;
        var temperature = new short[world.Count];
        int maritimeReach = Math.Max(6, height / 8);
        ushort[] marineDistance = DistanceFromMarineWater(world, regions, maritimeReach);
        float Latitude(int row) => (row + 0.5f) / height * 2 - 1;
        float SurfaceHeight(int cell) => water[cell] == WaterType.None ? Math.Max(0, (int)elevation[cell]) : 0;
        Parallel.For(0, height, row =>
        {
            float latitude = Math.Abs(Latitude(row));
            float sea = ModernSeaTemperature(latitude);
            float marineTemperature = Math.Clamp(sea * 0.9f, -18f, 260f);
            for (int col = 0; col < width; col++)
            {
                if (!world.Domain.Contains(col, row)) continue;
                int cell = row * width + col;
                float maritime = 1 - Smooth(marineDistance[cell] / (float)maritimeReach);
                float local = (TerrainNoise((col + 0.5f) / height, (row + 0.5f) / height, 3.1f,
                    aspect, wrap, seed ^ 0x4CF5AD43u) - 0.5f) * 48;
                float air = Interpolate(sea + local, marineTemperature, maritime * 0.62f) - SurfaceHeight(cell) * 0.065f;
                if (regions[cell] is RegionKind.Ocean or RegionKind.Sea) air = marineTemperature;
                temperature[cell] = (short)(MathF.Round(air) + settings.TemperatureOffsetC * 10);
            }
        });

        return temperature;
    }

    private static void SpreadWindwardVapor(WorldData world, WaterType[] water, short[] elevation,
        float[] vapor, float[] rain)
    {
        // Strictly descending edges let each cell collect its incoming moisture
        // before it is processed, without repeated diffusion over the whole atlas.
        var pending = new PriorityQueue<int, (int Height, int Cell)>();
        int Height(int cell) => water[cell] == WaterType.None ? Math.Max(0, (int)elevation[cell]) : 0;
        for (int cell = 0; cell < vapor.Length; cell++)
            if (vapor[cell] > 0) pending.Enqueue(cell, (-Height(cell), cell));

        Span<int> destinations = stackalloc int[6];
        Span<float> weights = stackalloc float[6];
        float localShare = AnnualWind.WindwardLocalShare(world.Width, world.Height, world.Manifest.GeneratorVersion);
        while (pending.TryDequeue(out int cell, out _))
        {
            float moisture = vapor[cell];
            int height = Height(cell), count = 0;
            float totalWeight = 0;
            Hex hex = HexGrid.FromOffset(cell % world.Width, cell / world.Width);
            for (int direction = 0; direction < 6 && height > 0; direction++)
            {
                int neighbor = HexGrid.NeighborIndex(hex, direction, world.Width, world.Height, world.Manifest.HorizontalWrap);
                if (!world.Domain.Contains(neighbor) || water[neighbor] != WaterType.None) continue;
                int drop = height - Height(neighbor);
                if (drop <= 0) continue;
                float weight = (1 + Math.Min(drop, 450) / 450f) / (1 + 12 * rain[neighbor]);
                destinations[count] = neighbor;
                weights[count++] = weight;
                totalWeight += weight;
            }
            if (totalWeight == 0)
            {
                rain[cell] += moisture;
                continue;
            }
            rain[cell] += moisture * localShare;
            for (int i = 0; i < count; i++)
            {
                int neighbor = destinations[i];
                float transfer = moisture * (1 - localShare) * weights[i] / totalWeight;
                if (transfer <= 0) continue;
                if (vapor[neighbor] == 0) pending.Enqueue(neighbor, (-Height(neighbor), neighbor));
                vapor[neighbor] += transfer;
            }
        }
    }

    private static float LandVaporTransmission(float elevation) =>
        1 - Smooth(Math.Clamp((elevation - 1800) / 1000f, 0, 1));

    private static float BarrierLoss(float fromHeight, float transmission)
    {
        float upstream = LandVaporTransmission(fromHeight);
        return upstream > transmission ? 1 - transmission / upstream : 0;
    }

    private static ushort[] DistanceFromMarineWater(WorldData world, RegionKind[] surface, int reach)
    {
        int width = world.Width, height = world.Height;
        var distance = new ushort[world.Count];
        Array.Fill(distance, (ushort)reach);
        for (int i = 0; i < distance.Length; i++)
            if (surface[i] is RegionKind.Ocean or RegionKind.Sea) distance[i] = 0;

        bool wrap = world.Manifest.HorizontalWrap;
        Parallel.For(0, height, row =>
        {
            foreach (int direction in new[] { 1, -1 })
            {
                int horizontal = reach;
                for (int step = 0; step < (wrap ? 2 * width : width); step++)
                {
                    int offset = step < width ? step : step - width;
                    int col = direction > 0 ? offset : width - 1 - offset;
                    if (!world.Domain.Contains(col, row)) { horizontal = reach; continue; }
                    int cell = row * width + col;
                    horizontal = Math.Min(distance[cell], horizontal + 1);
                    distance[cell] = (ushort)horizontal;
                }
            }
        });
        Parallel.For(0, width, col =>
        {
            foreach (int direction in new[] { 1, -1 })
            {
                int vertical = reach;
                for (int i = 0; i < height; i++)
                {
                    int row = direction > 0 ? i : height - 1 - i;
                    if (!world.Domain.Contains(col, row)) { vertical = reach; continue; }
                    int cell = row * width + col;
                    vertical = Math.Min(distance[cell], vertical + 1);
                    distance[cell] = (ushort)vertical;
                }
            }
        });
        return distance;
    }
}
