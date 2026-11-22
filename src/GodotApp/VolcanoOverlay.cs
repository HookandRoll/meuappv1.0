using System;
using System.Collections.Generic;
using Godot;
using Mapmaker.Core;

namespace Mapmaker.GodotApp;

// Sparse landmarks have their own pass, after every mountain/vegetation symbol.
internal partial class VolcanoOverlay : Node2D
{
    private readonly List<(Vector2 Position, int Variant)> _centers = new();
    private Texture2D[]? _textures;
    private float _detailZoom;
    internal int CenterCount => _centers.Count;
    internal Vector2 SymbolSize => new(Mathf.Lerp(6 * HexGrid.Root3, 1.35f * HexGrid.Root3, _detailZoom),
        Mathf.Lerp(9, 2.48f, _detailZoom));

    internal void SetWorld(WorldData world, MapArtwork? artwork)
    {
        _centers.Clear();
        if (artwork is not null && _textures is null)
        {
            _textures = new Texture2D[3];
            for (int v = 0; v < 3; v++) _textures[v] = artwork.CreateVolcanoTexture(v);
        }
        if (world.Manifest.Generation.Stage != GenerationStage.Blank)
            for (int i = 0; i < world.Count; i++)
                if ((world.Features[i] & GeographicFeatures.Volcano) != 0 && world.Water[i] == WaterType.None && world.Domain.Contains(i))
                {
                    var p = HexGrid.Center(HexGrid.FromOffset(i % world.Width, i / world.Width));
                    uint hash = unchecked((uint)i * 0x9E3779B9u ^ (uint)world.Manifest.Seed);
                    _centers.Add((new(p.X, p.Y - 0.08f), (int)(hash % 3)));
                }
        QueueRedraw();
    }

    internal void SetZoom(float detailZoom) { _detailZoom = detailZoom; QueueRedraw(); }
    public override void _Draw()
    {
        if (_textures is null) return;
        Vector2 size = SymbolSize;
        foreach (var center in _centers)
            DrawTextureRect(_textures[center.Variant], new Rect2(center.Position - size * 0.5f, size), false);
    }

    public override void _ExitTree()
    {
        if (_textures is not null) foreach (var texture in _textures) texture.Dispose();
        _textures = null;
    }
}
