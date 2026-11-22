using System;
using System.Collections.Generic;
using System.Numerics;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    private sealed partial class ContinentalSculpture
    {
        // v33: a single geological surface owns land, sea, mountain belts and arcs.
        // No continent territories, per-continent quotas or random island budget
        // may replace that surface after the plate forces have been applied.
        private (float[] Scores, float[]? Ridges, float SeaLevel, WaterType[] Water)
            BuildPlateTerrain(Action<GenerationProgress>? progress)
        {
            TectonicReference tectonics = _tectonics!;
            float spacing = MathF.Sqrt(_width * HexGrid.Root3 * _height * 1.5f / _plateCount);
            var uplift = _ridges ?? new float[_scores.Length];
            var depression = new float[_scores.Length];
            var arcs = new float[_scores.Length];
            VisitChunks(_chunks, (i, col, row) =>
            {
                Vector2 point = TectonicReference.WarpPoint(_manifest, HexGrid.Center(HexAt(i)), _plateCount);
                float continental = float.PositiveInfinity, oceanic = float.PositiveInfinity;
                foreach (PlateReference plate in tectonics.Plates)
                {
                    if (plate.Kind != PlateKind.Normal) continue;
                    float distance = WrappedDelta(point, new(plate.X, plate.Y)).LengthSquared() / (plate.Size * plate.Size);
                    if (plate.Continental) continental = Math.Min(continental, distance);
                    else oceanic = Math.Min(oceanic, distance);
                }
                if (_polarBands is not null)
                {
                    float polarDistance = _polarBands.DistanceToOcean(HexGrid.Center(HexAt(i)));
                    oceanic = Math.Min(oceanic, polarDistance * polarDistance);
                }
                if (_oceanicMargins is not null)
                {
                    float distance = _oceanicMargins.DistanceToOcean(HexGrid.Center(HexAt(i)));
                    oceanic = Math.Min(oceanic, distance * distance);
                }
                float crust = float.IsPositiveInfinity(oceanic) ? 1f : float.IsPositiveInfinity(continental) ? -1f
                    : (oceanic - continental) / Math.Max(0.001f, oceanic + continental);
                // Irregular shelves and bays follow the crust field. This detail
                // cannot independently lift deep ocean into unrelated islands.
                float regional = (FieldNoise(col, row, 2.7f, _seed ^ 0xA753B19Du) - 0.5f) * 0.32f;
                float coast = (FieldNoise(col, row, 8.3f, _seed ^ 0x72C49AF1u) - 0.5f) * 0.50f
                    + (FieldNoise(col, row, 24f, _seed ^ 0xE1358C27u) - 0.5f) * 0.34f
                    + (FieldNoise(col, row, 63f, _seed ^ 0x4A9BD165u) - 0.5f) * 0.14f;
                _scores[i] = crust + regional + coast * (0.35f + _coastDetail * 0.9f);
            }, progress, GenerationStep.Land, 0, 40);

            int done = 0;
            foreach (PlateContact contact in tectonics.Contacts)
            {
                if (Math.Abs(contact.Compression) < 0.25f && contact.Shear < 0.8f) continue;
                var a = new Vector2(contact.X, contact.Y);
                var b = new Vector2(contact.EndX, contact.EndY);
                Vector2 segment = b - a;
                float lengthSquared = segment.LengthSquared();
                if (lengthSquared < 0.00001f) continue;
                Vector2 normal = tectonics.ContactNormal(_manifest, contact);
                var pa = tectonics.Plates[contact.A];
                var pb = tectonics.Plates[contact.B];
                bool continentalCollision = pa.Continental && pb.Continental;
                bool upperA = pa.Continental || (!pb.Continental && contact.A < contact.B);
                float upperSign = upperA ? -1 : 1;
                float strength = Math.Clamp(Math.Abs(contact.Compression) / 1.5f, 0, 1);
                float micro = Math.Min(pa.Size, pb.Size) < 0.6f ? 0.7f : 1f;
                float belt = Math.Max(HexGrid.Root3 * 1.7f, spacing * 0.055f * micro);
                float arcOffset = Math.Max(HexGrid.Root3 * 2.2f, spacing * 0.055f);
                float arcWidth = Math.Max(HexGrid.Root3 * 1.2f, spacing * 0.026f * micro);
                bool atlasArc = _manifest.GeneratorVersion >= 53 && _domain.IsAtlas && !continentalCollision;
                if (atlasArc) arcWidth = Math.Min(arcWidth, HexGrid.Root3 * 2.4f);
                float reach = Math.Max(belt * 2.8f, arcOffset + arcWidth * 2.8f);
                int top = Math.Max(0, (int)MathF.Floor((Math.Min(a.Y, b.Y) - reach) / 1.5f));
                int bottom = Math.Min(_height - 1, (int)MathF.Ceiling((Math.Max(a.Y, b.Y) + reach) / 1.5f));
                for (int row = top; row <= bottom; row++)
                {
                    float offset = (row & 1) * 0.5f;
                    int left = (int)MathF.Floor((Math.Min(a.X, b.X) - reach) / HexGrid.Root3 - offset);
                    int right = (int)MathF.Ceiling((Math.Max(a.X, b.X) + reach) / HexGrid.Root3 - offset);
                    if (!_wrap) { left = Math.Max(0, left); right = Math.Min(_width - 1, right); }
                    for (int x = left; x <= right; x++)
                    {
                        int col = _wrap ? (x % _width + _width) % _width : x;
                        int i = row * _width + col;
                        var point = new Vector2((x + offset) * HexGrid.Root3, row * 1.5f);
                        Vector2 relative = point - a;
                        float t = Math.Clamp(Vector2.Dot(relative, segment) / lengthSquared, 0, 1);
                        Vector2 delta = relative - segment * t;
                        float distance = delta.Length();
                        if (distance > reach) continue;
                        float Band(float d, float width) => MathF.Exp(-d * d / (width * width));
                        if (contact.Compression < -0.25f)
                        {
                            float riftDistance = distance, riftWidth = belt, riftStrength = strength;
                            if (_manifest.GeneratorVersion >= 44)
                            {
                                float signed = Vector2.Dot(delta, normal);
                                float riftOffset = WaterMarginField(col, row) * belt * 0.65f;
                                riftDistance = MathF.Sqrt(Math.Max(0, distance * distance - signed * signed)
                                    + (signed + riftOffset) * (signed + riftOffset));
                                riftWidth *= 0.65f + 0.40f * WaterWidthFactor(col, row);
                                riftStrength *= 0.70f + 0.30f * FieldNoise(col, row, 17, _seed ^ 0x72D19B43u);
                            }
                            depression[i] = Math.Max(depression[i], riftStrength * Band(riftDistance, riftWidth));
                        }
                        else if (contact.Compression > 0.25f)
                        {
                            if (continentalCollision)
                                uplift[i] = Math.Max(uplift[i], strength * Band(distance, belt));
                            else
                            {
                                float signed = Vector2.Dot(delta, normal) * upperSign;
                                // A finite arc strip on the overriding side, broken
                                // into islands by a seeded along-boundary signal.
                                float endFade = Band(MathF.Sqrt(Math.Max(0, delta.LengthSquared() - signed * signed)), arcWidth * 1.6f);
                                float pulse = Smooth(Math.Clamp((FieldNoise(col, row, 19f, _seed ^ 0xC7159A43u) - 0.40f) / 0.40f, 0, 1));
                                if (atlasArc) pulse = AtlasIslandConstruction(col, row, pulse);
                                float arc = Band(signed - arcOffset, arcWidth) * endFade;
                                // Volcanic construction can expose parts of an arc even
                                // at moderate convergence; pressure still controls relief.
                                arcs[i] = Math.Max(arcs[i], MathF.Sqrt(strength) * arc * pulse);
                                uplift[i] = Math.Max(uplift[i], strength * arc * (0.5f + 0.5f * pulse));
                                if (_manifest.GeneratorVersion >= 47)
                                    uplift[i] = Math.Max(uplift[i], strength * Band(distance, belt));
                                if (_manifest.GeneratorVersion < 47 && signed < 0)
                                    depression[i] = Math.Max(depression[i], strength * Band(signed + arcOffset * 0.45f, arcWidth * 0.75f) * endFade * 0.8f);
                            }
                        }
                        else
                        {
                            // Transform motion weakens a narrow fault zone; it
                            // does not create a mountain chain by itself.
                            if (_manifest.GeneratorVersion < 47)
                                depression[i] = Math.Max(depression[i], Math.Min(0.2f, contact.Shear * 0.06f) * Band(distance, belt * 0.5f));
                        }
                    }
                }
                if (++done % 64 == 0)
                    progress?.Invoke(new(GenerationStep.Land, 40 + done * 30 / Math.Max(1, tectonics.Contacts.Length)));
            }
            VisitChunks(_chunks, (i, _, _) =>
            {
                // Max envelopes, not sums: finer boundary tessellation cannot
                // inflate uplift. Active extension also suppresses nearby crests.
                uplift[i] *= 1 - Smooth(Math.Clamp(depression[i] / 0.65f, 0, 1));
                _scores[i] += uplift[i] * 0.65f + arcs[i] * 2.4f - depression[i] * 1.8f;
                if (_manifest.GeneratorVersion == 34 && _settings.Extent == MapExtent.World)
                    _scores[i] = OceanFrameScore(i, _scores[i]);
                if (_domain.IsAtlas && !AtlasLandEligible(i)) _scores[i] = float.NegativeInfinity;
                if ((_polarBands is not null && _polarBands.KindAt(HexGrid.Center(HexAt(i))) != PlateKind.Normal) ||
                    (_oceanicMargins is not null && _oceanicMargins.SideAt(HexGrid.Center(HexAt(i))) != 0))
                    uplift[i] = -Math.Max(0.7f, depression[i]);
                // v52 island placement reads net relief even in the Land stage,
                // where the optional output ridge array is not allocated.
                if (_ridges is not null || _manifest.GeneratorVersion >= 52) uplift[i] -= depression[i];
            }, progress, GenerationStep.Land, 70, 80);

            float sea = FindSculptedSeaLevel();
            WaterType[] water = SelectPlateSeaLevel(sea);
            if (_manifest.GeneratorVersion >= 35) SeparatePlateContinents(water, sea);
            if (_manifest.GeneratorVersion >= 42) SculptWaterMargins(water, sea);
            if (_manifest.GeneratorVersion == 51) ShapeSmallIslandsLegacy51(water, uplift);
            if (_manifest.GeneratorVersion >= 52) AddSmallIslands(water, uplift, sea);
            if (PolarIceField.Enabled(_manifest))
            {
                var ice = new PolarIceField(_manifest);
                for (int i = 0; i < water.Length; i++)
                    if (ice.IsLand(i)) { water[i] = WaterType.None; _scores[i] = sea + 0.1f; }
            }
            VisitChunks(_chunks, (i, _, _) =>
            {
                float altitude = _scores[i] - sea;
                _scores[i] = water[i] == WaterType.None ? Math.Max(0.0005f, altitude) : Math.Min(-0.0005f, altitude);
            }, progress, GenerationStep.Land, 80, 95);
            LabelOpenSea(water, new Queue<int>());
            progress?.Invoke(new(GenerationStep.Land, 100));
            return (_scores, _ridges, 0, water);
        }

        private bool AtlasLandEligible(int cell)
        {
            if (!_domain.IsAtlas) return true;
            if (!_domain.Contains(cell)) return false;
            if (_polarBands is not null && _polarBands.KindAt(HexGrid.Center(HexAt(cell))) != PlateKind.Normal) return false;
            if (_oceanicMargins is not null && _oceanicMargins.SideAt(HexGrid.Center(HexAt(cell))) != 0) return false;
            Vector2 p = _domain.Normalize(HexGrid.Center(HexAt(cell)));
            float radius = MathF.Sqrt(p.X * p.X + 0.64f * p.Y * p.Y);
            float rim = Math.Max(2, _height / 160f);
            return (1 - radius) * Math.Min(_width, _height) * 0.5f > rim &&
                (1 - Math.Abs(p.Y)) * _height * 0.5f > rim;
        }

        private float OceanFrameScore(int cell, float score)
        {
            int col = cell % _width, row = cell / _width;
            float x = (col + 0.5f) / _width * 2 - 1;
            float y = (row + 0.5f) / _height * 2 - 1;
            float radius = MathF.Sqrt(x * x + y * y);
            // A true ocean rim, even at high land percentages. The interior keeps
            // the plate field; only the outer transition lowers it into the sea.
            if (radius >= 0.97f) return -10f - radius;
            float bend = (FieldNoise(col, row, 5.3f, _seed ^ 0x8A73C591u) - 0.5f) * 0.16f
                + (FieldNoise(col, row, 17f, _seed ^ 0xE4C1962Bu) - 0.5f) * 0.10f
                + (FieldNoise(col, row, 47f, _seed ^ 0x3C91A7EDu) - 0.5f) * 0.04f;
            bend *= Smooth(Math.Clamp((0.97f - radius) / 0.10f, 0, 1));
            float transition = Smooth(Math.Clamp((radius + bend - 0.84f) / 0.13f, 0, 1));
            return score - transition * 3.5f;
        }

        private WaterType[] SelectPlateSeaLevel(float sea)
        {
            int target = PlateLandTarget();
            var water = new WaterType[_scores.Length];
            int land = 0;
            for (int i = 0; i < water.Length; i++)
            {
                water[i] = _scores[i] >= sea ? WaterType.None : WaterType.Lake;
                if (water[i] == WaterType.None) land++;
            }
            // Histogram rounding may miss a few cells. Resolve only the closest
            // cells globally, never impose equal territories or regrow smooth coasts.
            int difference = Math.Abs(land - target);
            if (difference == 0) return water;
            bool remove = land > target;
            var nearest = new PriorityQueue<int, (float Score, int Cell)>();
            for (int i = 0; i < water.Length; i++)
            {
                if ((water[i] == WaterType.None) != remove) continue;
                float priority = remove ? -_scores[i] : _scores[i];
                nearest.Enqueue(i, (priority, i));
                if (nearest.Count > difference) nearest.Dequeue();
            }
            while (nearest.TryDequeue(out int i, out _)) water[i] = remove ? WaterType.Lake : WaterType.None;
            return water;
        }

        private int PlateLandTarget()
        {
            int target = (int)((long)_domain.PercentageAreaCount * _settings.LandPercent / 100);
            if (_manifest.GeneratorVersion < 49) return target;
            int eligible = 0;
            foreach (float score in _scores) if (float.IsFinite(score)) eligible++;
            return Math.Min(target, eligible);
        }
    }

    private static (short Elevation, float Plain) PlateLandElevation(float x, float y, float aspect,
        float altitude, uint seed, float ridge, bool wrap)
    {
        float texture = TerrainNoise(x * aspect, y, 9f, aspect, wrap, seed ^ 0x714C9AB3u);
        float lowlands = 25 + 680 * (1 - MathF.Exp(-Math.Max(0, altitude) * 1.2f)) + texture * 100;
        float collision = Math.Max(0, ridge);
        float rift = Math.Clamp(-ridge, 0, 1);
        float elevation = lowlands + 4100 * MathF.Pow(collision, 1.25f);
        elevation *= 1 - 0.85f * rift;
        return ((short)Math.Clamp(elevation, 1, 6800), 0);
    }
}
