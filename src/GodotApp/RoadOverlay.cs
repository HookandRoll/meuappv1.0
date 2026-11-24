using System;
using System.Collections.Generic;
using Godot;
using Mapmaker.Core;

namespace Mapmaker.GodotApp;

// Modular arms meet at the shared edge midpoint; meshes are grouped by chunk.
internal sealed partial class RoadOverlay : Node2D
{
    private readonly List<ArrayMesh> _meshes = new();
    private ushort[]? _roads;
    internal void SetWorld(WorldData? world, bool force = false)
    {
        if (!force && ReferenceEquals(_roads, world?.RoadEdges)) return;
        _roads = world?.RoadEdges;
        foreach (Node child in GetChildren()) { RemoveChild(child); child.QueueFree(); }
        foreach (ArrayMesh mesh in _meshes) mesh.Dispose();
        _meshes.Clear();
        if (world is null) return;
        var chunks = new Dictionary<int, (List<Vector3> Vertices, List<Color> Colors)>();
        for (int cell = 0; cell < world.Count; cell++)
        {
            if (world.RoadEdges[cell] == 0 || !world.Domain.Contains(cell)) continue;
            int chunk = cell / world.Width / WorldData.ChunkSize * world.Chunks.Columns + cell % world.Width / WorldData.ChunkSize;
            if (!chunks.TryGetValue(chunk, out var geometry))
                chunks.Add(chunk, geometry = (new(), new()));
            Hex hex = HexGrid.FromOffset(cell % world.Width, cell / world.Width);
            var center = HexGrid.Center(hex);
            var a = new Vector2(center.X, center.Y);
            for (int d = 0; d < 6; d++)
            {
                int tier = HexGrid.EdgeValue(world.RoadEdges[cell], d);
                if (tier == 0) continue;
                // Unwrapped neighbor geometry also gives the correct arm at a map border.
                Hex delta = d switch { 0 => new(1, 0), 1 => new(0, 1), 2 => new(-1, 1),
                    3 => new(-1, 0), 4 => new(0, -1), _ => new(1, -1) };
                var neighbor = HexGrid.Center(new Hex(hex.Q + delta.Q, hex.R + delta.R));
                var b = (a + new Vector2(neighbor.X, neighbor.Y)) * 0.5f;
                float width = tier switch { 1 => 0.025f, 2 => 0.045f, _ => 0.065f };
                Color color = tier switch { 1 => new("#94734c"), 2 => new("#b89061"), _ => new("#c8bc9e") };
                var normal = (b - a).Normalized().Orthogonal() * width;
                Vector2[] points = { a - normal, a + normal, b + normal, a - normal, b + normal, b - normal };
                foreach (var p in points) { geometry.Vertices.Add(new Vector3(p.X, p.Y, 0)); geometry.Colors.Add(color); }
            }
        }
        foreach (var geometry in chunks.Values)
        {
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = geometry.Vertices.ToArray();
            arrays[(int)Mesh.ArrayType.Color] = geometry.Colors.ToArray();
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            _meshes.Add(mesh);
            AddChild(new MeshInstance2D { Mesh = mesh });
        }
    }

    public override void _ExitTree()
    {
        foreach (var mesh in _meshes) mesh.Dispose();
        _meshes.Clear();
    }
}
