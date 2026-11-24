using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Mapmaker.Core;

namespace Mapmaker.GodotApp;

public partial class MapView
{
    private readonly List<int> _networkPath = new();
    private bool _networkDrawing;
    private bool _networkRiver;
    private int _networkLevel;
    private Line2D? _networkPreview;

    private void StartNetwork(Vector2I offset)
    {
        if (_world is not WorldData world || !IsOnWorld(offset) || world.Manifest.Generation.Stage < GenerationStage.Land) return;
        _networkDrawing = true;
        _networkRiver = _editorPanel.NetworkRiver;
        _networkLevel = _editorPanel.NetworkLevel;
        // Legacy river recipes support three classes; their saved recipe stays unchanged.
        if (_networkRiver && world.Manifest.GeneratorVersion < 23) _networkLevel = Math.Min(3, _networkLevel);
        _networkPath.Clear();
        _networkPath.Add(offset.Y * world.Width + offset.X);
        _networkPreview ??= new Line2D { Width = 0.09f, Antialiased = true, ZIndex = 10 };
        if (_networkPreview.GetParent() is null) _map.AddChild(_networkPreview);
        _networkPreview.DefaultColor = _networkLevel == 0 ? new Color("#e49a78") : _networkRiver ? new Color("#76bbcf") : new Color("#d6ad5c");
        _networkPreview.ClearPoints();
        AppendNetworkPoint(_networkPath[0]);
    }

    private void AppendNetworkPoint(int cell)
    {
        var center = HexGrid.Center(HexGrid.FromOffset(cell % _world!.Width, cell / _world.Width));
        _networkPreview!.AddPoint(new Vector2(center.X, center.Y));
    }

    private void ContinueNetwork(Vector2I offset)
    {
        if (!_networkDrawing || _world is not WorldData world || !IsOnWorld(offset)) return;
        int previous = _networkPath[^1], target = offset.Y * world.Width + offset.X;
        if (previous == target) return;
        Hex from = HexGrid.FromOffset(previous % world.Width, previous / world.Width);
        Hex to = HexGrid.FromOffset(offset.X, offset.Y);
        int distance = HexGrid.Distance(from, to, world.Width, world.Manifest.HorizontalWrap);
        if (world.Manifest.HorizontalWrap)
        {
            Hex best = to;
            foreach (int shift in new[] { -world.Width, world.Width })
            {
                var candidate = new Hex(to.Q + shift, to.R);
                if (HexGrid.Distance(from, candidate, world.Width, false) < HexGrid.Distance(from, best, world.Width, false)) best = candidate;
            }
            to = best;
        }
        for (int step = 1; step <= distance; step++)
        {
            var hex = HexGrid.Pick(System.Numerics.Vector2.Lerp(HexGrid.Center(from), HexGrid.Center(to), step / (float)distance));
            var (column, row) = HexGrid.ToOffset(hex);
            if (world.Manifest.HorizontalWrap) column = (column % world.Width + world.Width) % world.Width;
            int cell = row * world.Width + column;
            if (!world.Domain.Contains(column, row) || NetworkEditor.Direction(world, _networkPath[^1], cell) < 0) break;
            int existing = _networkPath.IndexOf(cell);
            if (existing >= 0) _networkPath.RemoveRange(existing + 1, _networkPath.Count - existing - 1);
            else _networkPath.Add(cell);
        }
        _networkPreview!.ClearPoints();
        foreach (int cell in _networkPath) AppendNetworkPoint(cell);
    }

    private void FinishNetwork()
    {
        if (!_networkDrawing) return;
        _networkDrawing = false;
        _networkPreview?.ClearPoints();
        if (_world is not WorldData world || _history is null) return;
        if (_networkRiver) NetworkEditor.River(world, _networkPath, _networkLevel, _selection, _history.Capture);
        else for (int i = 1; i < _networkPath.Count; i++)
            NetworkEditor.Road(world, _networkPath[i - 1], _networkPath[i], _networkLevel, _selection, _history.Capture);
        _networkPath.Clear();
        if (_history.Commit())
        {
            _roadOverlay.SetWorld(world, force: !_networkRiver);
            EditChanged(false, false, previewReady: true, preserveClimateWind: true);
        }
    }
}
