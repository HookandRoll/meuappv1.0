using System;
using System.Collections.Generic;
using System.Numerics;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    private sealed class ContinentalSculpture
    {
        private readonly int _width;
        private readonly int _height;
        private readonly bool _wrap;
        private readonly bool _useContinuousField;
        private readonly bool _refineShore;
        private readonly bool _preserveShoreDetail;
        private readonly bool _finishShoreline;
        private readonly bool _removeIslandRibbons;
        private readonly bool _roundShallowInlets;
        private readonly GenerationSettings _settings;
        private readonly float _scale;
        private readonly float _coastDetail;
        private readonly uint _seed;
        private readonly Random _random;
        private readonly ChunkGrid _chunks;
        private readonly float[] _scores;
        private readonly float[]? _ridges;

        public ContinentalSculpture(WorldManifest manifest)
        {
            _width = manifest.Width;
            _height = manifest.Height;
            _wrap = manifest.HorizontalWrap;
            _useContinuousField = manifest.GeneratorVersion == 7;
            _refineShore = manifest.GeneratorVersion >= 8;
            _preserveShoreDetail = manifest.GeneratorVersion >= 9;
            _finishShoreline = manifest.GeneratorVersion >= 10;
            _removeIslandRibbons = manifest.GeneratorVersion >= 11;
            _roundShallowInlets = manifest.GeneratorVersion >= 12;
            _settings = manifest.Generation;
            _scale = MathF.Min(_height, MathF.Sqrt((float)_width * _height / _settings.Continents));
            _coastDetail = _settings.CoastDetail * 0.01f;
            _seed = unchecked((uint)manifest.Seed);
            _random = new Random(unchecked((int)(_seed ^ 0x4F1BBCDCu)));
            _chunks = new ChunkGrid(_width, _height);
            _scores = new float[_width * _height];
            _ridges = _settings.Stage >= GenerationStage.Relief ? new float[_scores.Length] : null;
        }

        public (float[] Scores, float[]? Ridges, float SeaLevel) Build(Action<GenerationProgress>? progress)
        {
            int[] centers = PlaceCenters();
            Sculpt(centers, progress);
            SmoothTerrain(progress);
            VisitChunks(_chunks, (i, col, row) =>
            {
                float detail = 0.64f * FieldNoise(col, row, 5.2f, _seed ^ 0xEA89C12Bu)
                    + 0.26f * FieldNoise(col, row, 15.7f, _seed ^ 0x67E3314Du)
                    + 0.10f * FieldNoise(col, row, 43f, _seed ^ 0x9E3779B9u);
                float polarDistance = Math.Min(row, _height - 1 - row) / (float)_height;
                float polar = Smooth(Math.Clamp(polarDistance / 0.09f, 0f, 1f));
                _scores[i] = Interpolate(-1.25f, _scores[i] + (detail - 0.5f) * (0.23f + 0.68f * _coastDetail), polar);
            }, progress, GenerationStep.Land, 70, 80);

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
            progress?.Invoke(new(GenerationStep.Land, 85));
            return (_scores, _ridges, seaLevel);
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
                ApplyLine(start, finish, bend, 0.24f, 0.67f, _scale * 0.052f, crest: true);

                int riftStart = PickNear(center, 0.15f, 0.32f);
                int riftEnd = PickNear(center, 0.31f, 0.54f);
                ApplyLine(riftStart, riftEnd, null, 0.18f, -0.60f, _scale * 0.072f, crest: false);
                progress?.Invoke(new(GenerationStep.Land, 36 + ++done * 22 / centers.Length));
            }
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

            int target = (int)((long)_scores.Length * _settings.LandPercent / 100);
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
                MarkChannel(TraceCurved(entry, saddle, 0.12f), radius);
                MarkChannel(TraceCurved(saddle, exit, 0.12f), radius);
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

        private void MarkChannel(IEnumerable<int> path, int radius)
        {
            foreach (int index in path)
            {
                Hex center = HexAt(index);
                for (int q = -radius; q <= radius; q++)
                for (int r = Math.Max(-radius, -q - radius); r <= Math.Min(radius, -q + radius); r++)
                {
                    var (col, row) = HexGrid.ToOffset(new Hex(center.Q + q, center.R + r));
                    if (_wrap)
                        col = (col + _width) % _width;
                    // The channel stays submerged when the land percentage is recalibrated.
                    if ((uint)row < (uint)_height && (uint)col < (uint)_width)
                        _scores[row * _width + col] = float.NegativeInfinity;
                }
            }
        }

        private int PickNear(int origin, float closest, float farthest) =>
            PointFrom(origin, _random.NextSingle() * MathF.Tau, _scale * Interpolate(closest, farthest, _random.NextSingle()));

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

            public void Apply(IReadOnlyList<Vector2> points, float peak, float radius, bool crest)
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
                            ridges[index] = Math.Max(ridges[index], strength);
                    }
                    _map._scores[index] += peak * strength;
                    _influence[index] = 0f;
                }
                _affected.Clear();
            }

            private void SampleSegment(Vector2 start, Vector2 end, Vector2 stretch, float radius, uint seed)
            {
                Vector2 segment = _map.WrappedDelta(start, end);
                end = start + segment;
                float inverseLength = 1f / Math.Max(0.0001f, segment.LengthSquared());
                float reach = radius * (stretch == Vector2.Zero ? 1.24f : 1.46f);
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
                        float width = radius * (0.76f + 0.36f * shape + 0.11f * detail);
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
