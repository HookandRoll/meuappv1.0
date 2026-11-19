using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;
using Mapmaker.Core;
namespace Mapmaker.GodotApp;

// Batched ribbons: one mesh per occupied chunk, shared animated material.
public partial class RiverOverlay : Node2D
{
    private WorldData? _world;
    private JsonElement _source;
    private ShaderMaterial? _water;
    public WorldData? World
    {
        get => _world;
        set
        {
            bool changed = value is not null && (_world is null || value.Width != _world.Width ||
                value.Height != _world.Height || !_source.Equals(value.SparseEntities));
            _world = value;
            if (changed) { _source = value!.SparseEntities; Rebuild(); }
        }
    }
    private sealed class Geometry
    {
        public readonly List<Vector3> Vertices = new();
        public readonly List<Vector2> UV = new();
        public readonly List<Color> Colors = new();
        public readonly List<int> Indices = new();
    }
    internal void BindTerrain(Texture2D texture, WorldData world)
    {
        _water ??= new ShaderMaterial { Shader = GD.Load<Shader>("res://src/GodotApp/River.gdshader") };
        _water.SetShaderParameter("terrain_data", texture);
        _water.SetShaderParameter("map_size", new Vector2I(world.Width, world.Height));
        _water.SetShaderParameter("horizontal_wrap", world.Manifest.HorizontalWrap);
    }
    internal void Invalidate() => _source = default;

    private void Rebuild()
    {
        foreach (Node child in GetChildren()) { RemoveChild(child); child.QueueFree(); }
        if (_world is not { } world) return;
        _water ??= new ShaderMaterial { Shader = GD.Load<Shader>("res://src/GodotApp/River.gdshader") };
        float span = world.Width * HexGrid.Root3;
        Vector2 Center(int cell)
        {
            var p = HexGrid.Center(HexGrid.FromOffset(cell % world.Width, cell / world.Width));
            if (world.Water[cell] != WaterType.None) return new(p.X, p.Y);
            var anchor = new Vector2(p.X, p.Y);
            Vector2 adjacentSum = Vector2.Zero;
            int adjacentCount = 0;
            Hex hex = HexGrid.FromOffset(cell % world.Width, cell / world.Width);
            for (int d = 0; d < 6; d++)
            {
                if (HexGrid.EdgeValue(world.RiverEdges[cell], d) == 0) continue;
                int n = HexGrid.NeighborIndex(hex, d, world.Width, world.Height, world.Manifest.HorizontalWrap);
                if (n < 0) continue;
                var np = HexGrid.Center(HexGrid.FromOffset(n % world.Width, n / world.Width));
                var other = new Vector2(np.X, np.Y);
                if (world.Manifest.HorizontalWrap) other.X -= Mathf.Round((other.X - anchor.X) / span) * span;
                adjacentSum += other; adjacentCount++;
            }
            // B-spline-style corner relaxation for ordinary degree-two points.
            // Sources and confluences keep their canonical common anchor.
            if (adjacentCount == 2) anchor = anchor * 0.25f + adjacentSum * 0.375f;
            float phase = (world.Manifest.Seed & 1023) * 0.013f;
            float x = anchor.X, y = anchor.Y;
            float spanX = world.Width * HexGrid.Root3;
            float frequency = world.Manifest.HorizontalWrap ? Mathf.Tau * Math.Max(1, world.Width / 12) / spanX : 0.28f;
            int tier = 0;
            for (int d = 0; d < 6; d++) tier = Math.Max(tier, HexGrid.EdgeValue(world.RiverEdges[cell], d));
            float amplitude = world.Relief[cell] >= ReliefType.Mountains ? 0.09f : 0.55f - tier * 0.08f;
            return new(x + amplitude * Mathf.Sin(x * frequency + Mathf.Sin(y * 0.31f) + phase),
                y + amplitude * Mathf.Sin(x * frequency * 2 + y * 0.24f + phase));
        }
        Vector2 Near(Vector2 origin, Vector2 p)
        {
            if (world.Manifest.HorizontalWrap) p.X -= Mathf.Round((p.X - origin.X) / span) * span;
            return p;
        }
        var chunks = new Dictionary<int, Geometry>();
        foreach (var river in WorldGenerator.ReadRivers(world))
        {
            float length = 0;
            for (int j = 0; j < river.Path.Length; j++)
            {
                int cell = river.Path[j];
                Vector2 center = Center(cell);
                Vector2 start = j == 0 ? center : (center + Near(center, Center(river.Path[j - 1]))) * 0.5f;
                Vector2 end = j + 1 == river.Path.Length ? center : (center + Near(center, Center(river.Path[j + 1]))) * 0.5f;
                float Width(int edge)
                {
                    double area = Math.Sqrt(3) * 0.5 * world.Manifest.HexDistance * world.Manifest.HexDistance;
                    double reference = area * 1000 * 9000 * Math.Max(1, Math.Sqrt(world.Height / 256.0));
                    float flow = (float)(river.AnnualFlow[edge] / reference);
                    int level = world.Manifest.GeneratorVersion >= 23 ? river.Tiers[edge]
                        : flow < 2 ? 1 : flow < 5 ? 2 : flow < 15 ? 3 : flow < 50 ? 4 : 5;
                    float low = level switch { 1 => 1, 2 => 2, 3 => 5, 4 => 15, _ => 50 };
                    float high = level switch { 1 => 2, 2 => 5, 3 => 15, 4 => 50, _ => 300 };
                    float thin = level switch { 1 => 0.065f, 2 => 0.09f, 3 => 0.14f, 4 => 0.21f, _ => 0.31f };
                    float thick = level switch { 1 => 0.09f, 2 => 0.14f, 3 => 0.21f, 4 => 0.31f, _ => 0.50f };
                    float t = Math.Clamp((MathF.Log(1 + flow) - MathF.Log(1 + low)) / (MathF.Log(1 + high) - MathF.Log(1 + low)), 0, 1);
                    return Mathf.Lerp(thin, thick, t);
                }
                float startWidth = Width(Math.Max(0, j - 1));
                float endWidth = Width(Math.Min(j, river.Tiers.Length - 1));
                bool headwater = j == 0 && world.HexToRivers.TryGetValue(cell, out var crossings) && crossings.Count == 1;
                int chunk = cell / world.Width / WorldData.ChunkSize * world.Chunks.Columns + cell % world.Width / WorldData.ChunkSize;
                if (!chunks.TryGetValue(chunk, out var geometry)) chunks[chunk] = geometry = new();
                if (world.Water[cell] != WaterType.None)
                {
                    bool entry = j > 0 && world.Water[river.Path[j - 1]] == WaterType.None;
                    bool exit = j + 1 < river.Path.Length && world.Water[river.Path[j + 1]] == WaterType.None;
                    if (!entry && !exit) { length += (center - start).Length() + (end - center).Length(); continue; }
                    if (!entry) start = center;
                    if (!exit) end = center;
                }
                Vector2 previous = start;
                Vector2 along = (end - start).Normalized();
                Vector2 inDirection = (center - start).Normalized(), outDirection = (end - center).Normalized();
                // Two Hermite spans pass through the shared junction anchor. Curves
                // retain common sockets and tangents across hex/chunk boundaries.
                Vector2 Curve(float t)
                {
                    bool first = t <= 0.5f;
                    float u = first ? t * 2 : (t - 0.5f) * 2;
                    Vector2 a = first ? start : center, b = first ? center : end;
                    float distance = a.DistanceTo(b);
                    Vector2 ta = (first ? inDirection : along) * distance;
                    Vector2 tb = (first ? along : outDirection) * distance;
                    return (2*u*u*u - 3*u*u + 1)*a + (u*u*u - 2*u*u + u)*ta +
                        (-2*u*u*u + 3*u*u)*b + (u*u*u - u*u)*tb;
                }
                for (int step = 0; step <= 12; step++)
                {
                    float t = step / 12f;
                    Vector2 point = Curve(t);
                    length += (point - previous).Length(); previous = point;
                    Vector2 tangent = Curve(Math.Min(1, t + 0.005f)) - Curve(Math.Max(0, t - 0.005f));
                    if (tangent.LengthSquared() < 0.0001f) tangent = end - start;
                    tangent = tangent.Normalized();
                    if (step == 0) point -= tangent * 0.015f;
                    else if (step == 12) point += tangent * 0.015f;
                    Vector2 normal = new(-tangent.Y, tangent.X);
                    float width = Mathf.Lerp(startWidth, endWidth, t);
                    if (headwater) width *= Mathf.SmoothStep(0.06f, 1, t);
                    float half = width * 0.5f;
                    int vertex = geometry.Vertices.Count;
                    geometry.Vertices.Add(new Vector3(point.X + normal.X * half, point.Y + normal.Y * half, 0));
                    geometry.Vertices.Add(new Vector3(point.X - normal.X * half, point.Y - normal.Y * half, 0));
                    Color size = new(Math.Clamp(width / 0.50f, 0, 1), 1, 1, 1);
                    geometry.Colors.Add(size); geometry.Colors.Add(size);
                    geometry.UV.Add(new Vector2(0, length)); geometry.UV.Add(new Vector2(1, length));
                    if (step > 0)
                    {
                        geometry.Indices.Add(vertex - 2); geometry.Indices.Add(vertex - 1); geometry.Indices.Add(vertex);
                        geometry.Indices.Add(vertex); geometry.Indices.Add(vertex - 1); geometry.Indices.Add(vertex + 1);
                    }
                }
            }
        }
        foreach (var geometry in chunks.Values)
        {
            if (geometry.Indices.Count == 0) continue;
            var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = geometry.Vertices.ToArray();
            arrays[(int)Mesh.ArrayType.TexUV] = geometry.UV.ToArray();
            arrays[(int)Mesh.ArrayType.Color] = geometry.Colors.ToArray();
            arrays[(int)Mesh.ArrayType.Index] = geometry.Indices.ToArray();
            var mesh = new ArrayMesh(); mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            AddChild(new MeshInstance2D { Mesh = mesh, Material = _water });
        }
    }
}
