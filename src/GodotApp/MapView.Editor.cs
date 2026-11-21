using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Mapmaker.Core;

namespace Mapmaker.GodotApp;

internal enum BrushKind { Biome, Relief, Water, Elevation }
internal enum ElevationTool { Raise, Lower, Set, Smooth, Flatten }
internal enum BrushShape { Hexagon, Circle, Square, Diamond }
internal readonly record struct BrushSettings(BrushKind Kind, int Value, int Radius, int Meters, BrushShape Shape, float EdgeStrength);

public partial class MapView
{
    private enum EditAction { Undo = 101, Redo, RegenerateSelection = 106, RegenerateWorld }
    private static readonly Vector2I NoHex = new(-1, -1);
    private static readonly StringName SelectionBounds = new("selection_bounds");
    private static readonly StringName BrushCenter = new("brush_center");

    private PopupMenu _editMenu = null!;
    private ImageTexture? _lockTexture;
    private UndoManager? _history;
    private WorldData? _editSource;
    private UndoManager? _sourceHistory;
    private WorldRegion? _sourceSelection;
    private bool _sourceDirty;
    private ConfirmationDialog _editorConfirmation = null!;
    private Action? _confirmedEditorAction;
    private bool _saving;
    private bool _saveFinished;
    private ulong _saveStarted;
    private EditorPreview? _editorPreview;
    private int _draftRevision;
    private bool _draftTerrainChanged;
    private bool _draftLocksChanged;
    private bool _draftClimateChanged;
    private bool _initiallyDirty;
    private int _savedRevision;
    private WorldRegion? _selection;
    private bool _lockSummaryDirty = true;
    private HexLocks _allSelectionLocks;
    private HexLocks _anySelectionLocks;
    private Vector2I? _dragStart;
    private Vector2I? _lastBrushCenter;
    private BrushSettings? _stroke;
    private short _flattenHeight;
    private readonly HashSet<int> _strokeCells = new();
    private readonly List<(int Q, int R, float Weight)> _brushFootprint = new();
    private readonly Dictionary<int, (short Original, float Weight)> _strokeElevations = new();
    private uint _strokeSeed;

    private void BindEditor()
    {
        _editorPanel.FileActionRequested += RequestEditorFileAction;
        _editorConfirmation = new ConfirmationDialog { Title = "Confirmar edição" };
        AddChild(_editorConfirmation);
        _editorConfirmation.GetOkButton().Text = "Sim";
        _editorConfirmation.GetCancelButton().Text = "Não";
        _editorConfirmation.Confirmed += () =>
        {
            Action? action = _confirmedEditorAction;
            _confirmedEditorAction = null;
            _editorConfirmation.Hide();
            action?.Invoke();
        };
        _editorConfirmation.Canceled += () => _confirmedEditorAction = null;
        _editorPanel.HistoryRequested += EditHistory;
        _editorPanel.LockRequested += SetLocks;
        _editorPanel.RegenerationTargetChanged += RefreshEditor;
        _editorPanel.RegenerationRequested += RequestRegeneration;
        _editorPanel.HexEditRequested += EditSingleHex;
        _editorPanel.PageChanged += () =>
        {
            FinishGesture();
            _material.SetShaderParameter(BrushCenter, NoHex);
            if (_editorPanel.ActiveSection == 0 && _selection is WorldRegion region)
                SelectHex(new Vector2I(region.Column, region.Row));
            if (_editorPanel.ActiveSection == 1)
                SelectHex(NoHex);
        };
    }

    private void StartEditorDraft(WorldData source)
    {
        WorldData draft = source.CreateEditCopy();
        _editorPreview = new EditorPreview(draft);
        _editSource = source;
        _sourceHistory = _history;
        _sourceDirty = _dirty;
        _sourceSelection = _selection;
        _draftRevision = _history!.Revision;
        _draftTerrainChanged = _draftLocksChanged = false;
        _draftClimateChanged = false;
        _world = draft;
        _history = _history.Fork(draft);
        _material.SetShaderParameter("editor_data", _editorPreview.Texture);
        _material.SetShaderParameter("editor_band_height", WorldData.ChunkSize);
        _material.SetShaderParameter("editor_preview", true);
    }

    private async Task FinishEditorAsync()
    {
        if (_currentPath is string path)
            await SaveWorldAsync(path);
        else
            ShowSaveAsDialog();
    }

    private async Task<bool> ApplyEditorDraftAsync(GlobalDataTextures.Buffers? prepared = null)
    {
        if (_editSource is not WorldData source || _world is not WorldData draft || _history is null)
            return false;

        bool changed = _draftRevision != _history.Revision;
        if (changed && _draftTerrainChanged && _textures is not null)
        {
            GlobalDataTextures.Buffers buffers = prepared ?? await Task.Run(() => GlobalDataTextures.BuildBuffers(draft));
            ReplaceTerrainTextures(draft, buffers);
        }
        if (_draftLocksChanged)
        {
            RefreshLockTexture();
        }

        ReleaseEditorPreview();
        SetEditorPanelVisible(false);
        RefreshInspection();
        return changed;
    }

    private void ReleaseEditorPreview()
    {
        if (_editorPreview is null)
            return;
        _material.SetShaderParameter("editor_preview", false);
        _material.SetShaderParameter("editor_data", default(Variant));
        _material.SetShaderParameter("editor_climate", false);
        _material.SetShaderParameter("editor_climate_data", default(Variant));
        _editorPreview.Dispose();
        _editorPreview = null;
        _editSource = null;
        _sourceHistory = null;
    }

    private void RequestEditorFileAction(int action)
    {
        if (_busy || _editSource is null)
            return;
        FinishGesture();
        _editorConfirmation.DialogText = action switch
        {
            0 when _currentPath is not null => "Tem certeza que deseja salvar a edição por cima do arquivo atual?",
            0 => "Tem certeza que deseja salvar a edição? Você escolherá o arquivo.",
            1 => "Tem certeza que deseja salvar a edição em um novo arquivo?",
            _ => "Tem certeza que deseja cancelar a edição? As mudanças desta sessão serão descartadas."
        };
        _confirmedEditorAction = action switch
        {
            0 => () => _ = FinishEditorAsync(),
            1 => ShowSaveAsDialog,
            _ => CancelEditorDraft
        };
        _editorConfirmation.PopupCentered();
    }

    private void CancelEditorDraft()
    {
        if (_busy || _editSource is not WorldData source || _sourceHistory is null)
            return;
        FinishGesture();
        WorldRegion? selection = _sourceSelection;
        _world = source;
        _riverOverlay.World = source;
        _history = _sourceHistory;
        _dirty = _sourceDirty;
        ReleaseEditorPreview();
        SetEditorPanelVisible(false);
        _lockSummaryDirty = true;
        SetSelection(selection);
        RefreshInterface();
    }

    private void UpdateSaveProgress()
    {
        if (!_saving)
            return;
        double seconds = (Time.GetTicksMsec() - _saveStarted) / 1000.0;
        double progress = _saveFinished ? 100 : Math.Min(95, seconds / 3.0 * 95);
        _editorPanel.ShowSaveProgress(true, progress, 0.35f + 0.65f * (0.5f + 0.5f * MathF.Sin((float)seconds * 7)));
    }

    private void RefreshEditorClimate()
    {
        if (!_draftClimateChanged || _editorPreview is null || _world is not WorldData world || _layer is not (2 or 3))
            return;
        _material.SetShaderParameter("editor_climate_data", _editorPreview.RefreshClimate(world));
        _material.SetShaderParameter("editor_climate", true);
        _draftClimateChanged = false;
    }

    private void ResetEditor(WorldData world)
    {
        _history = new UndoManager(world);
        _initiallyDirty = true;
        _savedRevision = 0;
        _dragStart = null;
        _stroke = null;
        _lastBrushCenter = null;
        _strokeCells.Clear();
        _strokeElevations.Clear();
        _material.SetShaderParameter(BrushCenter, NoHex);
        RefreshLockTexture();
        SelectHex(NoHex);
    }

    private void MarkEditorSaved()
    {
        _initiallyDirty = false;
        _savedRevision = _history?.Revision ?? 0;
        _dirty = false;
    }

    private void RefreshEditor()
    {
        if (_history is null || _world is not WorldData world)
            return;

        bool selected = _selection.HasValue;
        bool enabled = !_busy;
        foreach (var (action, available) in new[]
        {
            (EditAction.Undo, _history.CanUndo), (EditAction.Redo, _history.CanRedo),
            (EditAction.RegenerateSelection, selected && world.Manifest.Generation.Stage != GenerationStage.Blank),
            (EditAction.RegenerateWorld, world.Manifest.Generation.Stage != GenerationStage.Blank)
        })
            _editMenu.SetItemDisabled(_editMenu.GetItemIndex((int)action), !enabled || !available);

        if (_lockSummaryDirty)
        {
            _allSelectionLocks = selected ? HexLocks.All : HexLocks.None;
            _anySelectionLocks = HexLocks.None;
            if (_selection is WorldRegion region)
            {
                foreach (int i in region.Cells(world.Width))
                {
                    if (!world.Domain.Contains(i)) continue;
                    var locks = (HexLocks)world.LockMask[i] & HexLocks.All;
                    _allSelectionLocks &= locks;
                    _anySelectionLocks |= locks;
                    if (_allSelectionLocks == 0 && _anySelectionLocks == HexLocks.All)
                        break;
                }
            }
            _lockSummaryDirty = false;
        }
        _editorPanel.UpdateActions(world.Manifest.Generation.Stage, enabled, selected,
            _history.CanUndo, _history.CanRedo, _allSelectionLocks, _anySelectionLocks);
    }

    private void SelectHex(Vector2I offset) =>
        SetSelection(IsOnWorld(offset) ? new WorldRegion(offset.X, offset.Y, 1, 1) : null);

    private void SetSelection(WorldRegion? region, bool dragging = false)
    {
        _selection = region;
        _lockSummaryDirty = true;
        _selected = region is WorldRegion single && single.Width == 1 && single.Height == 1
            ? new Vector2I(single.Column, single.Row) : NoHex;
        _editorPanel.UpdateSelection(region is WorldRegion area ? area.Width * area.Height : 0);
        _material.SetShaderParameter(SelectionBounds, region is WorldRegion bounds
            ? new Vector4I(bounds.Column, bounds.Row, bounds.Column + bounds.Width, bounds.Row + bounds.Height)
            : new Vector4I(-1, -1, -1, -1));
        RefreshInspection();
        if (!dragging)
            RefreshEditor();
    }

    private Vector2I PickAt(Vector2 position)
    {
        Vector2 local = _map.ToLocal(position);
        var (column, row) = HexGrid.ToOffset(HexGrid.Pick(new System.Numerics.Vector2(local.X, local.Y)));
        return new Vector2I(column, row);
    }

    private bool IsOnWorld(Vector2I offset) =>
        _world is WorldData world && world.Domain.Contains(offset.X, offset.Y);

    private bool CanPaint(BrushSettings brush)
    {
        if (_world is not WorldData world)
            return false;
        GenerationStage stage = world.Manifest.Generation.Stage;
        return brush.Kind switch
        {
            BrushKind.Biome => stage >= GenerationStage.Terrain,
            BrushKind.Relief or BrushKind.Elevation => stage >= GenerationStage.Relief,
            _ => stage >= GenerationStage.Land
        };
    }

    private void StartMapEdit(Vector2 position)
    {
        Vector2I offset = PickAt(position);
        int section = _editorPanel.Visible ? _editorPanel.ActiveSection : 0;
        if (section != 1)
        {
            SelectHex(offset);
            if (_editorPanel.Visible && _editorPanel.RegionTool && IsOnWorld(offset))
                _dragStart = offset;
            return;
        }

        BrushSettings brush = _editorPanel.Brush;
        if (!CanPaint(brush) || !IsOnWorld(offset) || _world is not WorldData world)
            return;
        _stroke = brush;
        _strokeSeed++;
        BuildBrushFootprint(brush);
        _flattenHeight = world.Elevation[offset.Y * world.Width + offset.X];
        ContinueStroke(offset);
    }

    public override void _Input(InputEvent input)
    {
        if (_busy)
            return;
        if (input is InputEventMouseMotion motion)
        {
            UpdateBrushCursor(motion.Position);
            if (ContinueGesture(motion.Position))
                GetViewport().SetInputAsHandled();
        }
        else if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } release &&
                 ContinueGesture(release.Position))
        {
            FinishGesture();
            GetViewport().SetInputAsHandled();
        }
    }

    private bool ContinueGesture(Vector2 position)
    {
        Rect2 canvas = MapCanvasRect();
        if (_dragStart is Vector2I start && _world is WorldData world)
        {
            Vector2I end = PickAt(position.Clamp(canvas.Position, canvas.End));
            end = new Vector2I(Math.Clamp(end.X, 0, world.Width - 1), Math.Clamp(end.Y, 0, world.Height - 1));
            WorldRegion next = new(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y),
                Math.Abs(start.X - end.X) + 1, Math.Abs(start.Y - end.Y) + 1);
            if (_selection != next)
                SetSelection(next, dragging: true);
            return true;
        }
        if (!_stroke.HasValue)
            return false;

        Vector2I offset = PickAt(position);
        if (canvas.HasPoint(position) && IsOnWorld(offset))
            ContinueStroke(offset);
        else
            _lastBrushCenter = null;
        return true;
    }

    private void UpdateBrushCursor(Vector2 position)
    {
        Vector2I offset = NoHex;
        if (_editorPanel.Visible && _editorPanel.ActiveSection == 1 && MapCanvasRect().HasPoint(position))
        {
            BrushSettings brush = _stroke ?? _editorPanel.Brush;
            Vector2I picked = PickAt(position);
            if (CanPaint(brush) && IsOnWorld(picked))
            {
                offset = picked;
                _material.SetShaderParameter("brush_radius", brush.Radius);
                _material.SetShaderParameter("brush_shape", (int)brush.Shape);
                _material.SetShaderParameter("brush_strength", brush.EdgeStrength);
            }
        }
        _material.SetShaderParameter(BrushCenter, offset);
    }

    private void ContinueStroke(Vector2I offset)
    {
        if (_stroke is not BrushSettings brush || _world is not WorldData world || _lastBrushCenter == offset)
            return;
        Hex to = HexGrid.FromOffset(offset.X, offset.Y);
        if (_lastBrushCenter is Vector2I previous)
        {
            Hex from = HexGrid.FromOffset(previous.X, previous.Y);
            int distance = HexGrid.Distance(from, to, world.Width, false);
            for (int step = 1; step < distance; step++)
            {
                float fraction = step / (float)distance;
                ApplyDab(world, brush, HexGrid.Pick(System.Numerics.Vector2.Lerp(
                    HexGrid.Center(from), HexGrid.Center(to), fraction)));
            }
        }
        ApplyDab(world, brush, to);
        _lastBrushCenter = offset;
    }

    private void BuildBrushFootprint(BrushSettings brush)
    {
        _brushFootprint.Clear();
        int radius = brush.Radius;
        int bound = brush.Shape == BrushShape.Hexagon ? radius : (int)Math.Ceiling(radius * 1.58);
        for (int r = -bound; r <= bound; r++)
        for (int q = -bound; q <= bound; q++)
        {
            float x = Math.Abs(q + r * 0.5f);
            float y = Math.Abs(r * 0.8660254f);
            float distance = brush.Shape switch
            {
                BrushShape.Hexagon => Math.Max(Math.Abs(q + r), Math.Max(Math.Abs(q), Math.Abs(r))),
                BrushShape.Circle => MathF.Sqrt(x * x + y * y),
                BrushShape.Square => Math.Max(x, y),
                _ => x + y
            };
            if (distance > radius + 0.0001f)
                continue;
            float weight = Mathf.Lerp(1f, brush.EdgeStrength, Math.Min(1f, distance / Math.Max(1, radius)));
            _brushFootprint.Add((q, r, weight));
        }
    }

    private void ApplyDab(WorldData world, BrushSettings brush, Hex center)
    {
        foreach (var (q, r, weight) in _brushFootprint)
        {
            var (column, row) = HexGrid.ToOffset(new Hex(center.Q + q, center.R + r));
            if (world.Manifest.HorizontalWrap)
                column = (column % world.Width + world.Width) % world.Width;
            if (!world.Domain.Contains(column, row) ||
                (_selection is WorldRegion region && !region.Contains(column, row)) || weight <= 0)
                continue;

            int i = row * world.Width + column;
            var locks = (HexLocks)world.LockMask[i];
            if (brush.Kind != BrushKind.Elevation && !AcceptCategoricalPaint(i, weight))
                continue;

            switch (brush.Kind)
            {
                case BrushKind.Biome when (locks & HexLocks.Biome) == 0 && world.Water[i] == WaterType.None &&
                                          world.Biomes[i] != (BiomeType)brush.Value:
                    _history!.Capture(i);
                    world.Biomes[i] = (BiomeType)brush.Value;
                    break;
                case BrushKind.Relief when (locks & HexLocks.Relief) == 0 && world.Relief[i] != (ReliefType)brush.Value:
                    _history!.Capture(i);
                    world.Relief[i] = (ReliefType)brush.Value;
                    break;
                case BrushKind.Water when (locks & HexLocks.Water) == 0 && world.Water[i] != (WaterType)brush.Value:
                    _history!.Capture(i);
                    world.Water[i] = (WaterType)brush.Value;
                    _strokeCells.Add(i);
                    break;
                case BrushKind.Elevation when (locks & HexLocks.Elevation) == 0:
                    if (!PaintElevation(world, i, brush, weight))
                        continue;
                    break;
                default:
                    continue;
            }
            _editorPreview?.UpdateCell(world, i);
        }
    }

    private bool AcceptCategoricalPaint(int index, float strength)
    {
        if (strength >= 1f)
            return true;
        // Keep each cell's threshold stable across the stroke, including overlapping dabs.
        uint rank = unchecked((uint)index * 0x9E3779B9u + _strokeSeed * 0x85EBCA6Bu);
        rank = unchecked((rank ^ (rank >> 16)) * 0x7FEB352Du);
        return ((rank ^ (rank >> 15)) >> 8) < strength * 16777216f;
    }

    private bool PaintElevation(WorldData world, int i, BrushSettings brush, float weight)
    {
        if (!_strokeElevations.TryGetValue(i, out var previous))
            previous = (world.Elevation[i], 0);
        if (previous.Weight >= weight)
            return false;

        int current = previous.Original;
        _strokeElevations[i] = (previous.Original, weight);
        int target = (ElevationTool)brush.Value switch
        {
            ElevationTool.Raise => current + brush.Meters,
            ElevationTool.Lower => current - brush.Meters,
            ElevationTool.Set => brush.Meters,
            ElevationTool.Flatten => _flattenHeight,
            _ => NeighborHeight(world, i, current)
        };
        short elevation = (short)Math.Clamp((int)Math.Round(current + (target - current) * weight),
            short.MinValue, short.MaxValue);
        if (elevation == world.Elevation[i])
            return false;
        _history!.Capture(i);
        world.Elevation[i] = elevation;
        _strokeCells.Add(i);
        return true;
    }

    private int NeighborHeight(WorldData world, int i, int current)
    {
        Hex hex = HexGrid.FromOffset(i % world.Width, i / world.Width);
        int total = current;
        int count = 1;
        for (int direction = 0; direction < 6; direction++)
        {
            int neighbor = HexGrid.NeighborIndex(hex, direction, world.Width, world.Height, world.Manifest.HorizontalWrap);
            if (neighbor < 0)
                continue;
            total += _strokeElevations.TryGetValue(neighbor, out var original) ? original.Original : world.Elevation[neighbor];
            count++;
        }
        return (int)Math.Round((double)total / count);
    }

    private void FinishGesture()
    {
        if (_dragStart.HasValue)
        {
            _dragStart = null;
            RefreshEditor();
        }
        if (_stroke is not BrushSettings brush)
            return;
        _stroke = null;
        _lastBrushCenter = null;
        if (_world is WorldData world)
        {
            if (brush.Kind == BrushKind.Water)
                foreach (int i in _strokeCells)
                    if (((HexLocks)world.LockMask[i] & HexLocks.Flags) == 0) world.RefreshCoast(i);
            if (brush.Kind is BrushKind.Elevation or BrushKind.Water)
                TerrainClassifier.ReclassifyAround(world, _strokeCells, i => _history!.Capture(i),
                    i => _editorPreview?.UpdateCell(world, i), _selection);
        }
        _strokeCells.Clear();
        _strokeElevations.Clear();
        if (_history!.Commit())
            EditChanged(updateTerrain: true, updateLocks: false, previewReady: true);
    }

    private void SetLocks(HexLocks field, bool locked)
    {
        if (_busy || _world is not WorldData world || _history is null || _selection is not WorldRegion region)
            return;
        FinishGesture();
        foreach (int i in region.Cells(world.Width))
        {
            if (!world.Domain.Contains(i)) continue;
            var current = (HexLocks)world.LockMask[i];
            var next = locked ? current | field : current & ~field;
            if (next == current)
                continue;
            _history.Capture(i);
            world.LockMask[i] = (ushort)next;
            _editorPreview?.UpdateCell(world, i);
        }
        if (_history.Commit())
            EditChanged(updateTerrain: false, updateLocks: true, previewReady: true);
    }

    private void EditSingleHex(HexLocks field, int value)
    {
        if (_busy || _world is not WorldData world || _history is null || _selected == NoHex ||
            !MapEditorPanel.FieldAvailable(world.Manifest.Generation.Stage, field))
            return;
        int i = _selected.Y * world.Width + _selected.X;
        if (((HexLocks)world.LockMask[i] & field) != 0)
            return;
        _history.Capture(i);
        switch (field)
        {
            case HexLocks.Biome: world.Biomes[i] = (BiomeType)value; break;
            case HexLocks.Relief: world.Relief[i] = (ReliefType)value; break;
            case HexLocks.Landform: world.Landforms[i] = (LandformType)value; break;
            case HexLocks.Water:
                world.Water[i] = (WaterType)value;
                world.RefreshCoast(i);
                break;
            case HexLocks.Elevation: world.Elevation[i] = (short)value; break;
            case HexLocks.Temperature: world.Temperature[i] = (short)value; break;
            case HexLocks.Rainfall: world.Rainfall[i] = (ushort)value; break;
        }
        if (field is HexLocks.Elevation or HexLocks.Water)
            TerrainClassifier.ReclassifyAround(world, new[] { i }, cell => _history.Capture(cell),
                cell => _editorPreview?.UpdateCell(world, cell));
        _editorPreview?.UpdateCell(world, i);
        if (_history.Commit())
            EditChanged(true, false, previewReady: true,
                updateClimate: field is HexLocks.Temperature or HexLocks.Rainfall);
    }

    private void EditHistory(bool redo)
    {
        if (_busy || _history is null)
            return;
        FinishGesture();
        if (redo ? _history.Redo() : _history.Undo())
            EditChanged(updateTerrain: true, updateLocks: true, updateClimate: true);
    }

    private void RequestRegeneration()
    {
        if (_busy || _world is not WorldData world || world.Manifest.Generation.Stage == GenerationStage.Blank)
            return;
        int target = _editorPanel.RegenerationTarget;
        WorldRegion? region = target == 1 ? new WorldRegion(0, 0, world.Width, world.Height) : _selection;
        if (region is WorldRegion bounds)
            _ = RegenerateAsync(bounds, _editorPanel.Mode, target == 2 ? _editorPanel.Field : HexLocks.None);
    }

    private async Task RegenerateAsync(WorldRegion region, RegenerationMode mode, HexLocks field)
    {
        if (_world is not WorldData world || _history is not UndoManager history)
            return;
        await RunOperation("Regenerando mapa…", async () =>
        {
            _reportedProgress = 0;
            UpdateGenerationStatus();
            _generationStatus.Show();
            RefreshInterface();
            try
            {
                bool changed = await Task.Run(() =>
                {
                    history.Capture(region);
                    try
                    {
                        WorldGenerator.RegenerateRegion(world, region, mode, field, ReportGenerationProgress);
                        return history.Commit();
                    }
                    catch
                    {
                        history.Cancel();
                        throw;
                    }
                });
                if (changed)
                    EditChanged(updateTerrain: true, updateLocks: false, updateClimate: true);
                else
                    RefreshInspection();
            }
            finally
            {
                _generationStatus.Hide();
            }
        });
    }

    private void EditChanged(bool updateTerrain, bool updateLocks, bool previewReady = false, bool updateClimate = false)
    {
        _riverOverlay.World = _world;
        _riverOverlay.QueueRedraw();
        _dirty = _initiallyDirty || _history!.Revision != _savedRevision;
        _lockSummaryDirty |= updateLocks;
        if (_world is WorldData world)
        {
            if (_editorPreview is not null)
            {
                _draftTerrainChanged |= updateTerrain;
                _draftLocksChanged |= updateLocks;
                _draftClimateChanged |= updateClimate;
                if (!previewReady)
                    _editorPreview.Rebuild(world);
                _editorPreview.Refresh(0, force: true);
                RefreshEditorClimate();
            }
            else
            {
                if (updateTerrain && _textures is not null)
                    ReplaceTerrainTextures(world, GlobalDataTextures.BuildBuffers(world));
                if (updateLocks)
                    RefreshLockTexture();
            }
        }
        RefreshInspection();
        RefreshInterface();
    }

    private void RefreshLockTexture()
    {
        if (_world is not WorldData world)
            return;
        var bytes = new byte[world.Count];
        for (int i = 0; i < bytes.Length; i++)
        {
            var locks = (HexLocks)world.LockMask[i];
            bytes[i] = (locks & HexLocks.All) == HexLocks.All ? (byte)2 : locks != 0 ? (byte)1 : (byte)0;
        }
        using Image image = Image.CreateFromData(world.Width, world.Height, false, Image.Format.R8, bytes);
        if (_lockTexture is not null && _lockTexture.GetWidth() == world.Width && _lockTexture.GetHeight() == world.Height)
        {
            _lockTexture.Update(image);
            return;
        }
        ImageTexture? old = _lockTexture;
        _lockTexture = ImageTexture.CreateFromImage(image);
        _material.SetShaderParameter("lock_data", _lockTexture);
        old?.Dispose();
    }
}
