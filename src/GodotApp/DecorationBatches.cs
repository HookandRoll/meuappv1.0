using System.Collections.Generic;
using Godot;
using Mapmaker.Core;

namespace Mapmaker.GodotApp;

internal static class DecorationBatches
{
    public static Node2D Create(DemoWorld world)
    {
        var root = new Node2D { Name = "ReliefAndVegetation" };
        var triangle = new ArrayMesh();
        var vertices = new Godot.Collections.Array();
        vertices.Resize((int)Mesh.ArrayType.Max);
        vertices[(int)Mesh.ArrayType.Vertex] = new Vector2[]
        {
            new(-0.72f, 0.52f), new(0, -0.89f), new(0.72f, 0.52f)
        };
        triangle.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, vertices);

        for (int chunkRow = 0; chunkRow < world.Height; chunkRow += DemoWorld.ChunkSize)
        for (int chunkColumn = 0; chunkColumn < world.Width; chunkColumn += DemoWorld.ChunkSize)
        {
            var instances = new List<(Vector2 Center, Relief Relief)>();
            for (int row = chunkRow; row < Mathf.Min(world.Height, chunkRow + DemoWorld.ChunkSize); row++)
            for (int column = chunkColumn; column < Mathf.Min(world.Width, chunkColumn + DemoWorld.ChunkSize); column++)
            {
                Relief relief = world[column, row].Relief;
                if (relief == Relief.None)
                    continue;
                var center = HexGrid.Center(HexGrid.FromOffset(column, row));
                instances.Add((new Vector2(center.X, center.Y), relief));
            }
            if (instances.Count == 0)
                continue;

            var mesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
                UseColors = true,
                Mesh = triangle,
                InstanceCount = instances.Count
            };
            for (int i = 0; i < instances.Count; i++)
            {
                var (center, relief) = instances[i];
                bool mountain = relief == Relief.Mountain;
                float size = mountain ? 0.88f : 0.65f;
                mesh.SetInstanceTransform2D(i, new Transform2D(
                    new Vector2(size, 0), new Vector2(0, size), center));
                mesh.SetInstanceColor(i, mountain ? new Color("#504f42") : new Color("#193c30"));
            }
            root.AddChild(new MultiMeshInstance2D { Multimesh = mesh });
        }

        return root;
    }
}
