using System;
using System.Collections.Generic;
using System.Numerics;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    private sealed partial class ContinentalSculpture
    {
        private readonly int _width;
        private readonly int _height;
        private readonly bool _wrap;
        private readonly bool _useContinuousField;
        private readonly TectonicReference? _tectonics;
        private readonly int _plateCount;
        private readonly WorldManifest _manifest;
        private readonly MapDomain _domain;
        private readonly PolarPlateBands? _polarBands;
        private readonly OceanicMarginBands? _oceanicMargins;
        private readonly bool _strongTectonics;
        private readonly bool _microplateTerrain;
        private readonly bool _compactCoasts;
        private readonly bool _narrowRidges;
        private readonly bool _balancedRidges;
        private readonly bool _organicRidges;
        private readonly bool _featheredRidges;
        private readonly bool _gentleRidges;
        private readonly bool _refineShore;
        private readonly bool _preserveShoreDetail;
        private readonly bool _finishShoreline;
        private readonly bool _removeIslandRibbons;
        private readonly bool _roundShallowInlets;
        private readonly bool _refineCoastClusters;
        private readonly bool _separateContinents;
        private readonly bool _islandCoasts;
        private readonly bool _spacedIslands;
        private readonly GenerationSettings _settings;
        private readonly float _scale;
        private readonly float _coastDetail;
        private readonly uint _seed;
        private readonly Random _random;
        private readonly ChunkGrid _chunks;
        private readonly float[] _scores;
        private readonly float[]? _ridges;

        public ContinentalSculpture(WorldManifest manifest, TectonicReference? tectonics = null)
        {
            _tectonics = tectonics;
            _plateCount = tectonics?.GenerationPlateCount(manifest) ?? 0;
            _manifest = manifest;
            _domain = new MapDomain(manifest);
            _polarBands = PolarPlateBands.Enabled(manifest) ? new PolarPlateBands(manifest) : null;
            _oceanicMargins = OceanicMarginBands.Enabled(manifest) ? new OceanicMarginBands(manifest) : null;
            _strongTectonics = manifest.GeneratorVersion >= 19;
            _microplateTerrain = manifest.GeneratorVersion >= 32;
            _compactCoasts = manifest.GeneratorVersion >= 20;
            _narrowRidges = manifest.GeneratorVersion is >= 24 and < 26;
            _balancedRidges = manifest.GeneratorVersion == 25;
            _organicRidges = manifest.GeneratorVersion >= 26;
            // v28 restores the continuous v26 ranges. Retain the attenuated
            // v27 recipe only for worlds explicitly saved with that version.
            _featheredRidges = manifest.GeneratorVersion == 27;
            _gentleRidges = manifest.GeneratorVersion >= 29;
            _width = manifest.Width;
            _height = manifest.Height;
            _wrap = manifest.HorizontalWrap;
            _useContinuousField = manifest.GeneratorVersion is 7 or >= 13;
            _refineShore = manifest.GeneratorVersion >= 8 && !_useContinuousField;
            _preserveShoreDetail = manifest.GeneratorVersion >= 9;
            _finishShoreline = manifest.GeneratorVersion >= 10;
            _removeIslandRibbons = manifest.GeneratorVersion >= 11;
            _roundShallowInlets = manifest.GeneratorVersion >= 12 && !_useContinuousField;
            _refineCoastClusters = manifest.GeneratorVersion >= 13 && !_useContinuousField;
            _islandCoasts = manifest.GeneratorVersion >= 16;
            _spacedIslands = manifest.GeneratorVersion >= 17;
            _separateContinents = manifest.GeneratorVersion >= 14;
            _settings = manifest.Generation;
            _scale = MathF.Min(_height, MathF.Sqrt((float)_width * _height / _settings.Continents));
            _coastDetail = _settings.CoastDetail * 0.01f;
            _seed = unchecked((uint)manifest.Seed);
            _random = new Random(unchecked((int)(_seed ^ 0x4F1BBCDCu)));
            _chunks = new ChunkGrid(_width, _height);
            _scores = new float[_width * _height];
            _ridges = _settings.Stage >= GenerationStage.Relief ? new float[_scores.Length] : null;
        }

        public (float[] Scores, float[]? Ridges, float SeaLevel, WaterType[]? Water) Build(Action<GenerationProgress>? progress)
        {
            if (_manifest.GeneratorVersion >= 33 && _tectonics is not null)
                return BuildPlateTerrain(progress);
            int[] centers = PlaceCenters();
            Sculpt(centers, progress);
            ApplyTectonics();
            if (_featheredRidges) FeatherTectonicRidges();
            SmoothTerrain(progress);
            VisitChunks(_chunks, (i, col, row) =>
            {
                if (!_useContinuousField)
                {
                    float legacyDetail = 0.64f * FieldNoise(col, row, 5.2f, _seed ^ 0xEA89C12Bu)
                        + 0.26f * FieldNoise(col, row, 15.7f, _seed ^ 0x67E3314Du)
                        + 0.10f * FieldNoise(col, row, 43f, _seed ^ 0x9E3779B9u);
                    float legacyPolarDistance = Math.Min(row, _height - 1 - row) / (float)_height;
                    float legacyPolar = Smooth(Math.Clamp(legacyPolarDistance / 0.09f, 0f, 1f));
                    _scores[i] = Interpolate(-1.25f, _scores[i] + (legacyDetail - 0.5f) * (0.23f + 0.68f * _coastDetail), legacyPolar);
                    return;
                }

                float detail = SculptedCoastDetail(col, row);
                float polarWave = (FieldNoise(col, row, 3.2f, _seed ^ 0x3C6EF372u) - 0.5f) * 0.08f
                    + (detail - 0.5f) * 0.05f;
                float polarDistance = Math.Min(row, _height - 1 - row) / (float)_height + polarWave;
                float polar = Smooth(Math.Clamp(polarDistance / 0.13f, 0f, 1f));
                float edgeFade = Smooth(Math.Clamp(Math.Min(row, _height - 1 - row) / (_height * 0.025f), 0f, 1f));
                float amplitude = (0.72f + 1.45f * _coastDetail) * edgeFade;
                _scores[i] = Interpolate(-1.25f, _scores[i], polar) + (detail - 0.5f) * amplitude;
            }, progress, GenerationStep.Land, 70, 80);

            if (_tectonics is not null) SculptCoastalEvents();
            if (_separateContinents)
                return BuildSeparatedContinents(centers, progress);

            float seaLevel = FindSculptedSeaLevel();
            if (_refineShore)
            {
                RefineShoreScores(seaLevel, progress);
                seaLevel = FindSculptedSeaLevel();
            }
            if (CarveStraits(centers, seaLevel))
                seaLevel = FindSculptedSeaLevel();
            if (_finishShoreline)
                FinishShoreline(_scores, _width, _height, _wrap, seaLevel, _removeIslandRibbons, progress);
            if (_roundShallowInlets)
                RoundShallowInlets(_scores, _width, _height, _wrap, seaLevel);
            if (_refineCoastClusters)
                RefineCoastClusters(_scores, _width, _height, _wrap, seaLevel);
            progress?.Invoke(new(GenerationStep.Land, 85));
            return (_scores, _ridges, seaLevel, null);
        }

        private int[] PlaceCenters()
        {
            int count = _settings.Continents;
            int columns = Math.Clamp((int)MathF.Round(MathF.Sqrt(count * (_width / (float)_height) * 0.85f)), 1, count);
            int rows = (count + columns - 1) / columns;
            var centers = new int[count];
            for (int i = 0; i < count; i++)
            {
                int row = i / columns;
                int rowCount = Math.Min(columns, count - row * columns);
                int x = (int)((i % columns + 0.30f + _random.NextSingle() * 0.40f) * _width / rowCount);
                int y = (int)((row + 0.30f + _random.NextSingle() * 0.40f) * _height / rows);
                if (_tectonics is not null)
                {
                    float best = float.NegativeInfinity;
                    for (int attempt = 0; attempt < (_strongTectonics ? 32 : 12); attempt++)
                    {
                        int candidateX = (int)((i % columns + 0.22f + _random.NextSingle() * 0.56f) * _width / rowCount);
                        int candidateY = (int)((row + 0.22f + _random.NextSingle() * 0.56f) * _height / rows);
                        var point = HexGrid.Center(HexGrid.FromOffset(candidateX, candidateY));
                        float crust = 0;
                        float crustRadius = _strongTectonics ? MathF.Sqrt((float)_width * _height / _plateCount) * HexGrid.Root3 : _scale;
                        foreach (var plate in _tectonics.Plates)
                        {
                            float distance = WrappedDelta(point, new Vector2(plate.X, plate.Y)).LengthSquared();
                            crust += (plate.Continental ? 1f : (_strongTectonics ? -0.85f : -0.35f)) * MathF.Exp(-distance / (crustRadius * crustRadius * plate.Size * plate.Size * 0.7f));
                        }
                        if (crust > best) { best = crust; x = candidateX; y = candidateY; }
                    }
                }
                centers[i] = y * _width + x;
            }
            return centers;
        }

        private void Sculpt(int[] centers, Action<GenerationProgress>? progress)
        {
            Propagation? propagation = _useContinuousField ? null : new Propagation(this);
            ContinuousField? field = _useContinuousField ? new ContinuousField(this) : null;
            void ApplyBlob(int seed, float peak, float radius)
            {
                if (field is null)
                    propagation!.Apply(new[] { seed }, peak, radius);
                else
                    field.Apply(new[] { HexGrid.Center(HexAt(seed)) }, peak, radius, crest: false);
            }

            void ApplyLine(int start, int end, int? bend, float curvature, float peak, float radius, bool crest)
            {
                if (field is not null)
                {
                    var curve = new List<Vector2>(CurvePoints(start, bend ?? end, curvature));
                    if (bend is int middle)
                        curve.AddRange(CurvePoints(middle, end, curvature));
                    field.Apply(curve, peak, radius, crest);
                    return;
                }
                IEnumerable<int> path = TraceCurved(start, bend ?? end, curvature);
                if (bend is int legacyMiddle)
                {
                    var spine = new List<int>(path);
                    spine.AddRange(TraceCurved(legacyMiddle, end, curvature));
                    path = spine;
                }
                propagation!.Apply(path, peak, radius, crest);
            }

            Array.Fill(_scores, -0.85f);
            int done = 0;
            foreach (int center in centers)
            {
                ApplyBlob(center, 2.5f + _random.NextSingle() * 0.5f, _scale * 0.61f);
                for (int i = 0; i < 3; i++)
                {
                    int seed = PickNear(center, 0.19f, 0.40f);
                    ApplyBlob(seed, 1.05f + _random.NextSingle() * 0.55f, _scale * 0.34f);
                }
                int capes = 4 + (int)(_coastDetail * 4f);
                for (int i = 0; i < capes; i++)
                {
                    int seed = PickNear(center, 0.33f, 0.61f);
                    ApplyBlob(seed, 0.45f + _random.NextSingle() * 0.55f, _scale * 0.20f);
                }
                progress?.Invoke(new(GenerationStep.Land, ++done * 36 / centers.Length));
            }

            done = 0;
            foreach (int center in centers)
            {
                int gulf = PickNear(center, 0.26f, 0.48f);
                int basin = PickNear(center, 0.08f, 0.30f);
                ApplyBlob(gulf, -1.50f - _random.NextSingle() * 0.45f, _scale * 0.29f);
                ApplyBlob(basin, -1.65f - _random.NextSingle() * 0.60f, _scale * 0.20f);

                float bearing = _random.NextSingle() * MathF.Tau;
                int start = PointFrom(center, bearing, _scale * 0.40f);
                int finish = PointFrom(center, bearing + MathF.PI, _scale * 0.43f);
                int bend = PickNear(center, 0.06f, 0.15f);
                ApplyLine(start, finish, bend, 0.24f, _tectonics is null ? 0.67f : 0.15f, _scale * 0.052f, crest: _tectonics is null);

                int riftStart = PickNear(center, 0.15f, 0.32f);
                int riftEnd = PickNear(center, 0.31f, 0.54f);
                ApplyLine(riftStart, riftEnd, null, 0.18f, -0.60f, _scale * 0.072f, crest: false);
                progress?.Invoke(new(GenerationStep.Land, 36 + ++done * 22 / centers.Length));
            }
        }

        private void SculptCoastalEvents()
        {
            float sea = FindSeaLevel(_scores, _settings.LandPercent);
            var random = new Random(unchecked((int)(_seed ^ 0x62C1E709u)));
            var candidates = new List<(int Cell, float Rank)>();
            int step = Math.Max(2, _height / 96);
            for (int row = step; row < _height - step; row += step)
            for (int col = step; col < _width - step; col += step)
            {
                int cell = row * _width + col;
                if (Math.Abs(_scores[cell] - sea) < 0.20f)
                    candidates.Add((cell, random.NextSingle()));
            }
            candidates.Sort((a, b) => a.Rank != b.Rank ? a.Rank.CompareTo(b.Rank) : a.Cell.CompareTo(b.Cell));
            var chosen = new List<int>();
            var field = new ContinuousField(this);
            int target = Math.Max(1, (int)(_settings.Continents * (1 + 2 * _coastDetail)));
            foreach (var candidate in candidates)
            {
                if (chosen.Count >= target) break;
                Hex hex = HexAt(candidate.Cell);
                bool spaced = true;
                foreach (int previous in chosen)
                    if (HexGrid.Distance(hex, HexAt(previous), _width, _wrap) < _scale * 0.35f) { spaced = false; break; }
                if (!spaced) continue;
                chosen.Add(candidate.Cell);
                float radius = Math.Max(2, _scale * (0.045f + random.NextSingle() * 0.025f));
                float roll = random.NextSingle();
                var center = HexGrid.Center(hex);
                // Finite local events occupy a small fraction of the candidate coastline.
                if (roll < 0.4f) field.Apply(new[] { center }, 0.9f, radius, false);
                else if (roll < 0.85f) field.Apply(new[] { center }, -1.1f, radius, false);
                else
                {
                    int lowest = candidate.Cell;
                    for (int direction = 0; direction < 6; direction++)
                    {
                        int n = HexGrid.NeighborIndex(hex, direction, _width, _height, _wrap, Math.Max(2, (int)radius));
                        if (n >= 0 && _scores[n] < _scores[lowest]) lowest = n;
                    }
                    if (lowest != candidate.Cell)
                    {
                        var offshore = HexGrid.Center(HexAt(lowest));
                        var delta = WrappedDelta(center, offshore);
                        field.Apply(new[] { center - delta * 0.5f, center, center + delta }, -0.9f, radius * 0.35f, false);
                    }
                }
            }
        }

        private void ApplyTectonics()
        {
            if (_tectonics is null) return;
            var field = new ContinuousField(this);
            float plateScale = MathF.Sqrt((float)_width * _height / _plateCount);
            // Restore the v19 continuous ridge profile. Scale its reach with the
            // average plate size so larger maps with more plates do not accumulate
            // continental-width mountain plateaus. At 512x256 / 21 plates this is v19.
            float ridgeScale = _organicRidges ? _scale * MathF.Sqrt(21f / _plateCount) : _scale;
            foreach (var plate in _tectonics.Plates)
                field.Apply(new[] { new Vector2(plate.X, plate.Y) },
                    _strongTectonics ? (plate.Continental ? 1.45f : -0.85f) : (plate.Continental ? 0.65f : -0.25f),
                    _strongTectonics ? plateScale * 0.72f * plate.Size : _scale * 0.32f, false);
            foreach (var contact in _tectonics.Contacts)
            {
                float intensity = Math.Clamp(contact.Compression, -1.5f, 1.5f);
                if (Math.Abs(intensity) < 0.25f) continue;
                var a = new Vector2(contact.X, contact.Y);
                var b = new Vector2(contact.EndX, contact.EndY);
                // Each short boundary segment contributes a bounded amount of score.
                float amplitude = intensity * (_strongTectonics ? 0.15f : 0.08f);
                float crestStrength = _featheredRidges ? 0.6f + 0.4f * Math.Clamp(intensity / 1.4f, 0, 1) : 1f;
                var plateA = _tectonics.Plates[contact.A];
                var plateB = _tectonics.Plates[contact.B];
                float localRidgeScale = ridgeScale;
                if (_microplateTerrain)
                {
                    // Smaller plates make narrow, uneven belts and alternating
                    // uplift/depression along their locally jagged contact normals.
                    bool micro = Math.Min(plateA.Size, plateB.Size) < 0.6f;
                    if (micro)
                    {
                        float variation = FieldNoise((int)((a.X + b.X) / (2 * HexGrid.Root3)),
                            (int)((a.Y + b.Y) / 3f), 31f, _seed ^ 0x93AC71D5u);
                        localRidgeScale *= 0.45f + variation * 0.25f;
                        amplitude *= 0.75f + variation * 0.65f;
                        crestStrength *= 0.65f + variation * 0.35f;
                    }
                    // More sampling segments should not multiply the total uplift.
                    float referenceLength = MathF.Min(_width * HexGrid.Root3 / Math.Clamp(_width / 8, 16, 128),
                        _height * 1.5f / Math.Clamp(_height / 8, 8, 64));
                    amplitude *= Math.Clamp((b - a).Length() / referenceLength, 0.08f, 1.5f);
                }
                if (intensity > 0 && (!plateA.Continental || !plateB.Continental))
                {
                    var normal = Vector2.Normalize(WrappedDelta(new Vector2(plateA.X, plateA.Y), new Vector2(plateB.X, plateB.Y)));
                    // A continental side overrides oceanic crust; ocean-ocean tie is stable.
                    bool upperA = plateA.Continental || (!plateB.Continental && contact.A < contact.B);
                    Vector2 offset = normal * (_narrowRidges ? plateScale * 0.06f : localRidgeScale * 0.045f) * (upperA ? -1 : 1);
                    field.Apply(new[] { a + offset, b + offset }, amplitude, localRidgeScale * 0.035f, !_narrowRidges, crestStrength);
                    if (_narrowRidges)
                        field.Apply(new[] { a + offset, b + offset }, 0, Math.Max(_balancedRidges ? 2.4f : 1.8f, plateScale * (_balancedRidges ? 0.075f : 0.035f)), true);
                    field.Apply(new[] { a - offset * 0.4f, b - offset * 0.4f }, -amplitude * 0.7f, _scale * 0.018f, false);
                }
                else
                {
                    field.Apply(new[] { a, b }, amplitude, intensity > 0 ? localRidgeScale * 0.065f : _scale * 0.025f, intensity > 0 && !_narrowRidges, crestStrength);
                    if (_narrowRidges && intensity > 0)
                        field.Apply(new[] { a, b }, 0, Math.Max(_balancedRidges ? 2.4f : 1.8f, plateScale * (_balancedRidges ? 0.075f : 0.035f)), true);
                }
            }
        }

        private void FeatherTectonicRidges()
        {
            if (_ridges is null) return;
            // Smooth only elevation influence, never continental scores. Two
            // separable box passes create a gradual shoulder around the crest.
            int radius = Math.Max(2, (int)MathF.Round(MathF.Sqrt((float)_width * _height / _plateCount) * 0.045f));
            var smooth = new float[_ridges.Length];
            var scratch = new float[_ridges.Length];
            float divisor = 2 * radius + 1;
            float[] input = _ridges;
            for (int pass = 0; pass < 2; pass++)
            {
                System.Threading.Tasks.Parallel.For(0, _height, row =>
                {
                    int offset = row * _width;
                    int Column(int col) => _wrap ? (col % _width + _width) % _width : Math.Clamp(col, 0, _width - 1);
                    float sum = 0;
                    for (int x = -radius; x <= radius; x++) sum += input[offset + Column(x)];
                    for (int col = 0; col < _width; col++)
                    {
                        scratch[offset + col] = sum / divisor;
                        sum += input[offset + Column(col + radius + 1)] - input[offset + Column(col - radius)];
                    }
                });
                System.Threading.Tasks.Parallel.For(0, _width, col =>
                {
                    float sum = 0;
                    for (int y = -radius; y <= radius; y++) sum += scratch[Math.Clamp(y, 0, _height - 1) * _width + col];
                    for (int row = 0; row < _height; row++)
                    {
                        smooth[row * _width + col] = sum / divisor;
                        sum += scratch[Math.Clamp(row + radius + 1, 0, _height - 1) * _width + col]
                            - scratch[Math.Clamp(row - radius, 0, _height - 1) * _width + col];
                    }
                });
                input = smooth;
            }
            for (int i = 0; i < _ridges.Length; i++) _ridges[i] = Math.Clamp(_ridges[i] * 0.45f + smooth[i] * 0.55f, 0, 1);
        }

        private void SmoothTerrain(Action<GenerationProgress>? progress)
        {
            var buffer = new float[_scores.Length];
            float[] input = _scores;
            float[] output = buffer;
            for (int pass = 0; pass < 2; pass++)
            {
                VisitChunks(_chunks, (i, col, row) =>
                {
                    Hex hex = HexGrid.FromOffset(col, row);
                    int neighbors = 0;
                    float sum = 0f;
                    for (int direction = 0; direction < 6; direction++)
                    {
                        int neighbor = Neighbor(hex, direction);
                        if (neighbor >= 0)
                        {
                            sum += input[neighbor];
                            neighbors++;
                        }
                    }
                    output[i] = Interpolate(input[i], sum / neighbors, 0.44f);
                }, progress, GenerationStep.Land, 58 + pass * 6, 64 + pass * 6);
                (input, output) = (output, input);
            }
        }

        private void RefineShoreScores(float seaLevel, Action<GenerationProgress>? progress)
        {
            var buffer = new float[_scores.Length];
            float[] input = _scores;
            float[] output = buffer;
            for (int pass = 0; pass < 2; pass++)
            {
                VisitChunks(_chunks, (i, col, row) =>
                {
                    float score = input[i];
                    float fade = 1f - Smooth(Math.Clamp((Math.Abs(score - seaLevel) - 0.12f) / 0.24f, 0f, 1f));
                    if (fade == 0f)
                    {
                        output[i] = score;
                        return;
                    }

                    // Filter the classification band at multiple scales while keeping inland relief.
                    Hex hex = HexGrid.FromOffset(col, row);
                    float sum = score * 6f;
                    int weight = 6;
                    for (int direction = 0; direction < 6; direction++)
                    for (int tap = 1; tap <= 3; tap++)
                    {
                        int steps = _preserveShoreDetail ? (tap == 3 ? 4 : tap) : tap * tap;
                        int neighbor = HexGrid.NeighborIndex(hex, direction, _width, _height, _wrap, steps);
                        if (neighbor < 0)
                            continue;
                        int tapWeight = 4 - tap;
                        sum += input[neighbor] * tapWeight;
                        weight += tapWeight;
                    }
                    float strength = _preserveShoreDetail ? 0.303f : 1f;
                    output[i] = Interpolate(score, sum / weight, fade * strength);
                }, progress, GenerationStep.Land, 80 + pass * 2, 82 + pass * 2);
                (input, output) = (output, input);
            }
        }

        private float FindSculptedSeaLevel()
        {
            float low = float.PositiveInfinity;
            float high = float.NegativeInfinity;
            foreach (float score in _scores)
            {
                if (float.IsNegativeInfinity(score))
                    continue;
                low = Math.Min(low, score);
                high = Math.Max(high, score);
            }

            int target = (int)((long)_domain.PercentageAreaCount * _settings.LandPercent / 100);
            Span<int> bins = stackalloc int[HistogramSize];
            // Refine the threshold bin once, without sorting or copying a world-sized field.
            for (int pass = 0; pass < 2 && high > low; pass++)
            {
                bins.Clear();
                float range = high - low;
                foreach (float score in _scores)
                {
                    if (score < low || score > high)
                        continue;
                    int bin = Math.Min((int)((score - low) * (HistogramSize / range)), HistogramSize - 1);
                    bins[bin]++;
                }

                int binIndex = HistogramSize - 1;
                for (; binIndex > 0 && bins[binIndex] < target; binIndex--)
                    target -= bins[binIndex];
                if (pass == 1 && bins[binIndex] > 0)
                    return low + (binIndex + 1f - target / (float)bins[binIndex]) * (range / HistogramSize);
                high = low + (binIndex + 1f) * (range / HistogramSize);
                low += binIndex * (range / HistogramSize);
            }
            return low;
        }

        private bool CarveStraits(int[] centers, float seaLevel)
        {
            bool changed = false;
            for (int left = 0; left < centers.Length; left++)
            for (int right = left + 1; right < centers.Length; right++)
            {
                Hex from = HexAt(centers[left]);
                Hex to = HexAt(centers[right]);
                int distance = HexGrid.Distance(from, to, _width, _wrap);
                if (distance < _scale * 0.18f || distance > _scale * 1.25f)
                    continue;

                var bridge = new List<int>(Trace(centers[left], centers[right]));
                if (bridge.Exists(i => _scores[i] < seaLevel))
                    continue;

                int saddle = bridge[bridge.Count / 2];
                for (int step = bridge.Count / 3; step <= bridge.Count * 2 / 3; step++)
                {
                    int candidate = bridge[step];
                    if (_scores[candidate] < _scores[saddle])
                        saddle = candidate;
                }

                var delta = HexGrid.Center(to) - HexGrid.Center(from);
                if (_wrap && Math.Abs(delta.X) > _width * HexGrid.Root3 * 0.5f)
                    delta.X -= MathF.CopySign(_width * HexGrid.Root3, delta.X);
                float bearing = MathF.Atan2(delta.Y, delta.X) + MathF.PI * 0.5f;
                int entry = FindOpenWater(saddle, bearing, seaLevel);
                int exit = FindOpenWater(saddle, bearing + MathF.PI, seaLevel);
                if (entry < 0 || exit < 0 || entry == exit)
                    continue;

                int radius = Math.Clamp((int)(_scale / 90f), 0, 3);
                MarkChannel(TraceCurved(entry, saddle, 0.12f), radius, seaLevel);
                MarkChannel(TraceCurved(saddle, exit, 0.12f), radius, seaLevel);
                changed = true;
            }
            return changed;
        }

        private int FindOpenWater(int origin, float bearing, float seaLevel)
        {
            int limit = PointFrom(origin, bearing, _scale * 0.61f);
            foreach (int index in Trace(origin, limit))
            {
                if (_scores[index] < seaLevel - 0.14f)
                    return index;
            }
            return -1;
        }

        private void MarkChannel(IEnumerable<int> path, int radius, float seaLevel)
        {
            int bankRadius = _useContinuousField ? radius + Math.Max(5, (int)(_scale * 0.055f)) : radius;
            foreach (int index in path)
            {
                Hex center = HexAt(index);
                for (int q = -bankRadius; q <= bankRadius; q++)
                for (int r = Math.Max(-bankRadius, -q - bankRadius); r <= Math.Min(bankRadius, -q + bankRadius); r++)
                {
                    var (col, row) = HexGrid.ToOffset(new Hex(center.Q + q, center.R + r));
                    if (_wrap)
                        col = (col + _width) % _width;
                    if ((uint)row >= (uint)_height || (uint)col >= (uint)_width)
                        continue;
                    int cell = row * _width + col;
                    int distance = (Math.Abs(q) + Math.Abs(r) + Math.Abs(q + r)) / 2;
                    if (distance <= radius)
                    {
                        // The channel stays submerged when the land percentage is recalibrated.
                        _scores[cell] = float.NegativeInfinity;
                        if (_useContinuousField && _ridges is not null)
                            _ridges[cell] = 0f;
                        continue;
                    }
                    if (float.IsNegativeInfinity(_scores[cell]))
                        continue;
                    float noise = FieldNoise(col, row, 19f, _seed ^ 0x517CC1B7u);
                    float t = Math.Clamp((distance - radius - (noise - 0.5f) * 1.8f) / Math.Max(1f, bankRadius - radius), 0f, 1f);
                    float blend = Smooth(t);
                    float shore = seaLevel - 0.05f + (noise - 0.5f) * 0.20f;
                    float maxScore = Interpolate(shore, seaLevel + 3.2f, blend * blend);
                    _scores[cell] = Math.Min(_scores[cell], maxScore);
                    if (_ridges is not null)
                        _ridges[cell] = Math.Min(_ridges[cell], blend);
                }
            }
        }

        private int PickNear(int origin, float closest, float farthest)
        {
            float bearing = _random.NextSingle() * MathF.Tau;
            float distance = _scale * Interpolate(closest, farthest, _random.NextSingle());
            if (_useContinuousField)
            {
                float y = (origin / _width) + MathF.Sin(bearing) * distance * (HexGrid.Root3 / 1.5f);
                float margin = _height * 0.12f;
                if (y < margin || y > _height - 1 - margin)
                    bearing = -bearing;
            }
            return PointFrom(origin, bearing, distance);
        }

        private int PointFrom(int origin, float bearing, float distance)
        {
            var (sine, cosine) = MathF.SinCos(bearing);
            var center = HexGrid.Center(HexAt(origin));
            center.X += cosine * distance * HexGrid.Root3;
            center.Y += sine * distance * HexGrid.Root3;
            return IndexAt(center);
        }

        private int IndexAt(Vector2 position)
        {
            var (col, row) = HexGrid.ToOffset(HexGrid.Pick(position));
            col = _wrap ? (col % _width + _width) % _width : Math.Clamp(col, 0, _width - 1);
            return Math.Clamp(row, 0, _height - 1) * _width + col;
        }

        private IEnumerable<int> TraceCurved(int start, int end, float bend)
        {
            var origin = HexGrid.Center(HexAt(start));
            var delta = HexGrid.Center(HexAt(end)) - origin;
            float span = _width * HexGrid.Root3;
            if (_wrap && Math.Abs(delta.X) > span * 0.5f)
                delta.X -= MathF.CopySign(span, delta.X);
            var perpendicular = new Vector2(-delta.Y, delta.X);
            float phase = _random.NextSingle() * MathF.Tau;
            int segments = Math.Clamp((int)(delta.Length() / 5f), 1, 12);
            int previous = start;
            for (int segment = 1; segment <= segments; segment++)
            {
                float t = segment / (float)segments;
                float offset = bend * MathF.Sin(MathF.PI * t) *
                    (0.72f * MathF.Sin(phase + t * MathF.Tau) + 0.28f * MathF.Sin(2f * phase + t * 2f * MathF.Tau));
                int waypoint = segment == segments ? end : IndexAt(origin + t * delta + offset * perpendicular);
                foreach (int index in Trace(previous, waypoint))
                    yield return index;
                previous = waypoint;
            }
        }

        private IEnumerable<Vector2> CurvePoints(int start, int end, float bend)
        {
            Vector2 origin = HexGrid.Center(HexAt(start));
            Vector2 delta = WrappedDelta(origin, HexGrid.Center(HexAt(end)));
            var normal = new Vector2(-delta.Y, delta.X);
            float phase = _random.NextSingle() * MathF.Tau;
            const int segments = 14;
            for (int step = 0; step <= segments; step++)
            {
                float t = step / (float)segments;
                float wave = MathF.Sin(phase + t * MathF.Tau) + 0.26f * MathF.Sin(2f * phase + t * 2f * MathF.Tau);
                yield return origin + delta * t + normal * (bend * MathF.Sin(t * MathF.PI) * wave);
            }
        }

        private Vector2 WrappedDelta(Vector2 from, Vector2 to)
        {
            Vector2 delta = to - from;
            if (_wrap)
            {
                float span = _width * HexGrid.Root3;
                delta.X -= MathF.Round(delta.X / span) * span;
            }
            return delta;
        }

        private IEnumerable<int> Trace(int start, int end)
        {
            Hex destination = HexAt(end);
            int index = start;
            yield return index;
            while (index != end)
            {
                Hex hex = HexAt(index);
                int distance = HexGrid.Distance(hex, destination, _width, _wrap);
                int choices = 0;
                int next = -1;
                for (int direction = 0; direction < 6; direction++)
                {
                    int neighbor = Neighbor(hex, direction);
                    if (neighbor >= 0 && HexGrid.Distance(HexAt(neighbor), destination, _width, _wrap) < distance &&
                        _random.Next(++choices) == 0)
                        next = neighbor;
                }
                if (next < 0)
                    throw new InvalidOperationException("Não foi possível concluir um caminho na grade hexagonal.");
                index = next;
                yield return index;
            }
        }

        private Hex HexAt(int index) => HexGrid.FromOffset(index % _width, index / _width);

        private int Neighbor(Hex hex, int direction) => HexGrid.NeighborIndex(hex, direction, _width, _height, _wrap);

        private float SculptedCoastDetail(int col, int row)
        {
            float x = (col + (row & 1) * 0.5f) / _height;
            float y = row / (float)_height;
            float warp = 0.022f + 0.032f * _coastDetail;
            float wx = x + (SampleField(x, y, 3.4f, _seed ^ 0xB5297A4Du) - 0.5f) * warp
                + (SampleField(x, y, 9.8f, _seed ^ 0x1B56C4E9u) - 0.5f) * (warp * 0.38f);
            float wy = y + (SampleField(x, y, 3.4f, _seed ^ 0x68E31DA4u) - 0.5f) * warp
                + (SampleField(x, y, 9.8f, _seed ^ 0x165667B1u) - 0.5f) * (warp * 0.38f);
            float coast = 0.52f * SampleField(wx, wy, 5.2f, _seed ^ 0xEA89C12Bu)
                + 0.25f * SampleField(wx, wy, 15.7f, _seed ^ 0x67E3314Du)
                + 0.14f * SampleField(wx, wy, 41f, _seed ^ 0x9E3779B9u)
                + 0.09f * SampleField(wx, wy, 86f, _seed ^ 0x4CF5AD43u);
            if (!_islandCoasts)
                return coast;
            // Broad, sparse patches create bays and headlands along otherwise smooth shores.
            float patch = Smooth(Math.Clamp((SampleField(x, y, 4.8f, _seed ^ 0xF3A195B7u) - 0.58f) / 0.14f, 0f, 1f));
            float inlet = SampleField(wx, wy, 26f, _seed ^ 0xA97B32D1u) - 0.5f;
            return coast + patch * inlet * (0.55f + _coastDetail * 0.65f);
        }

        private float SampleField(float x, float y, float frequency, uint seed)
            => TerrainNoise(x, y, frequency, _width / (float)_height, _wrap, seed);

        private float FieldNoise(int col, int row, float frequency, uint seed)
        {
            float scale = frequency / _height;
            float x = (col + (row & 1) * 0.5f) * scale;
            float y = row * scale;
            float value = Noise(x, y, seed);
            if (!_wrap)
                return value;
            float period = _width * scale;
            return Interpolate(value, Noise(x - period, y, seed), Smooth(x / period));
        }

        // A feature's influence is a smooth function of distance in map space. The hex grid
        // samples this field without deciding which propagation path reaches a cell first.
        private sealed class ContinuousField
        {
            private readonly ContinentalSculpture _map;
            private readonly float[] _influence;
            private readonly List<int> _affected = new();
            private uint _stamp;

            public ContinuousField(ContinentalSculpture map)
            {
                _map = map;
                _influence = new float[map._scores.Length];
            }

            public void Apply(IReadOnlyList<Vector2> points, float peak, float radius, bool crest, float crestStrength = 1)
            {
                uint seed = _map._seed ^ unchecked(++_stamp * 0x9E3779B9u);
                float bearing = Sample(0, 0, seed) * MathF.Tau;
                var (sine, cosine) = MathF.SinCos(bearing);
                Vector2 stretch = points.Count == 1 ? new Vector2(cosine, sine) * 0.53f : Vector2.Zero;
                float distance = Math.Max(2.4f, radius) * HexGrid.Root3;
                if (points.Count == 1)
                    SampleSegment(points[0], points[0], stretch, distance, seed);
                else
                    for (int i = 1; i < points.Count; i++)
                        SampleSegment(points[i - 1], points[i], stretch, distance, seed);

                foreach (int index in _affected)
                {
                    float strength = _influence[index];
                    if (crest)
                    {
                        float variation = _map.FieldNoise(index % _map._width, index / _map._width, 17f, seed ^ 0xB347AA63u);
                        strength *= 0.72f + 0.26f * variation;
                        if (_map._ridges is float[] ridges)
                            ridges[index] = Math.Max(ridges[index], strength * crestStrength);
                    }
                    _map._scores[index] += peak * strength;
                    _influence[index] = 0f;
                }
                _affected.Clear();

                if (crest && _map._gentleRidges && _map._ridges is float[] shoulders)
                {
                    // Widen only the elevation profile, with the same crest height,
                    // noise and smooth distance falloff. Continental uplift scores
                    // and the land quota must retain their original footprint.
                    float shoulderDistance = distance * 1.65f;
                    for (int i = 1; i < points.Count; i++)
                        SampleSegment(points[i - 1], points[i], stretch, shoulderDistance, seed);
                    foreach (int index in _affected)
                    {
                        float variation = _map.FieldNoise(index % _map._width, index / _map._width, 17f, seed ^ 0xB347AA63u);
                        float strength = _influence[index] * (0.72f + 0.26f * variation) * crestStrength;
                        shoulders[index] = Math.Max(shoulders[index], strength);
                        _influence[index] = 0f;
                    }
                    _affected.Clear();
                }
            }

            private void SampleSegment(Vector2 start, Vector2 end, Vector2 stretch, float radius, uint seed)
            {
                Vector2 segment = _map.WrappedDelta(start, end);
                end = start + segment;
                float inverseLength = 1f / Math.Max(0.0001f, segment.LengthSquared());
                float reach = radius * (stretch == Vector2.Zero ? 1.38f : 1.62f);
                float maxDistanceSquared = reach * reach;
                int top = Math.Max(0, (int)MathF.Ceiling((Math.Min(start.Y, end.Y) - reach) / 1.5f));
                int bottom = Math.Min(_map._height - 1, (int)MathF.Floor((Math.Max(start.Y, end.Y) + reach) / 1.5f));
                for (int row = top; row <= bottom; row++)
                {
                    float offset = (row & 1) * 0.5f;
                    int left = (int)MathF.Ceiling((Math.Min(start.X, end.X) - reach) / HexGrid.Root3 - offset);
                    int right = (int)MathF.Floor((Math.Max(start.X, end.X) + reach) / HexGrid.Root3 - offset);
                    if (!_map._wrap)
                    {
                        left = Math.Max(0, left);
                        right = Math.Min(_map._width - 1, right);
                    }
                    for (int x = left; x <= right; x++)
                    {
                        var position = new Vector2((x + offset) * HexGrid.Root3, row * 1.5f);
                        Vector2 relative = position - start;
                        float t = Math.Clamp(Vector2.Dot(relative, segment) * inverseLength, 0f, 1f);
                        Vector2 delta = relative - t * segment;
                        float distanceSquared = delta.LengthSquared();
                        if (distanceSquared >= maxDistanceSquared)
                            continue;
                        int col = _map._wrap ? (x % _map._width + _map._width) % _map._width : x;
                        float shape = _map.FieldNoise(col, row, 7.7f, seed);
                        float detail = _map.FieldNoise(col, row, 21f, seed ^ 0x85EBCA6Bu);
                        float width = radius * (0.64f + 0.50f * shape + 0.22f * detail);
                        float along = Vector2.Dot(delta, stretch);
                        float falloff = 1f - MathF.Sqrt(Math.Max(0f, distanceSquared - along * along)) / width;
                        if (falloff <= 0f)
                            continue;
                        int index = row * _map._width + col;
                        if (_influence[index] == 0f)
                            _affected.Add(index);
                        _influence[index] = Math.Max(_influence[index], Smooth(falloff));
                    }
                }
            }
        }

        private sealed class Propagation
        {
            private readonly ContinentalSculpture _map;
            private readonly int[] _visited;
            private readonly (int Index, float Strength)[] _queue;
            private int _stamp;

            public Propagation(ContinentalSculpture map)
            {
                _map = map;
                _visited = new int[map._scores.Length];
                _queue = new (int, float)[map._scores.Length];
            }

            public void Apply(IEnumerable<int> seeds, float peak, float radius, bool crest = false)
            {
                int stamp = ++_stamp;
                int tail = 0;
                uint seed = _map._seed ^ unchecked((uint)stamp * 0x9E3779B9u);
                foreach (int index in seeds)
                {
                    if (_visited[index] == stamp)
                        continue;
                    _visited[index] = stamp;
                    float strength = crest
                        ? 0.70f + 0.30f * _map.FieldNoise(index % _map._width, index / _map._width, 17f, seed)
                        : 1f;
                    _queue[tail++] = (index, strength);
                }

                int axis = (int)(seed % 6);
                float decay = 1f / Math.Max(2.4f, radius);
                for (int head = 0; head < tail; head++)
                {
                    var (index, strength) = _queue[head];
                    float influence = Smooth(strength);
                    _map._scores[index] += peak * influence;
                    if (crest && _map._ridges is float[] ridges)
                        ridges[index] = Math.Max(ridges[index], influence);

                    int column = index % _map._width;
                    int row = index / _map._width;
                    Hex hex = HexGrid.FromOffset(column, row);
                    float texture = _map.FieldNoise(column, row, 6.5f, seed);
                    int rotation = (int)(Sample(column, row, seed) * 6f);
                    for (int d = 0; d < 6; d++)
                    {
                        int direction = (d + rotation) % 6;
                        int neighbor = _map.Neighbor(hex, direction);
                        if (neighbor < 0 || _visited[neighbor] == stamp)
                            continue;

                        float jitter = Sample(neighbor, direction, seed ^ 0xB347AA63u);
                        float step = (0.75f + 0.5f * texture) * (0.85f + 0.30f * jitter);
                        if (direction == axis || direction == (axis + 3) % 6)
                            step *= 0.82f;
                        step *= jitter < 0.15f ? 0.47f : jitter > 0.85f ? 1.65f : 1f;
                        float next = strength - decay * step;
                        if (next <= 0f)
                            continue;
                        _visited[neighbor] = stamp;
                        _queue[tail++] = (neighbor, next);
                    }
                }
            }
        }
    }
}
