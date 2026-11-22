using System;
using Godot;
using Mapmaker.Core;

namespace Mapmaker.GodotApp;

internal partial class WindOverlay : Node2D
{
    private static readonly Color East = new("#9ce9ff");
    private static readonly Color West = new("#ffdb9c");

    internal MapDomain? Domain;

    public override void _Ready() => SetNotifyTransform(true);

    public override void _Notification(int what)
    {
        if (what == NotificationTransformChanged && Visible)
            QueueRedraw();
    }

    public override void _Draw()
    {
        if (Domain is not MapDomain domain)
            return;

        Transform2D transform = GetGlobalTransformWithCanvas();
        float zoom = transform.X.Length();
        Transform2D inverse = transform.AffineInverse();
        Rect2 viewport = GetViewportRect().Grow(32);
        Vector2 topLeft = inverse * viewport.Position;
        Vector2 bottomRight = inverse * viewport.End;
        int rowStep = Math.Max(1, Math.Min(domain.Height / 20, (int)(38 / (zoom * 1.5f))));
        int columnStep = Math.Max(1, Math.Min(domain.Width / 26, (int)(54 / (zoom * HexGrid.Root3))));
        int firstRow = Math.Max(0, (int)(topLeft.Y / (rowStep * 1.5f))) * rowStep + rowStep / 2;
        int lastRow = Math.Min(domain.Height - 1, Mathf.CeilToInt(bottomRight.Y / 1.5f));
        int firstColumn = Mathf.FloorToInt(topLeft.X / HexGrid.Root3) - 1;
        int lastColumn = Mathf.CeilToInt(bottomRight.X / HexGrid.Root3);
        float length = Mathf.Min(rowStep * 0.85f, 26f / zoom);
        float stroke = Mathf.Min(length * 0.12f, 1.7f / zoom);
        for (int row = firstRow; row <= lastRow; row += rowStep)
        {
            var (left, right) = domain.RowBounds(row);
            var (eastward, southward) = AnnualWind.DirectionWeights(row, domain.Height);
            Vector2 direction = new(
                AnnualWind.AlongLatitude * (2 * eastward - 1),
                AnnualWind.AcrossLatitude * (2 * southward - 1));
            if (direction.LengthSquared() < 0.0001f)
                continue;
            direction = direction.Normalized();
            Vector2 side = direction.Orthogonal();
            Color color = eastward >= 0.5f ? East : West;
            int first = left + Math.Min(columnStep, right - left) / 2;
            first += Math.Max(0, (firstColumn - first) / columnStep) * columnStep;
            int last = Math.Min(right - 1, lastColumn);
            for (int col = first; col <= last; col += columnStep)
            {
                var center = HexGrid.Center(HexGrid.FromOffset(col, row));
                Vector2 point = new(center.X, center.Y);
                Vector2 tip = point + direction * (length * 0.5f);
                Vector2 head = tip - direction * (length * 0.30f);
                DrawLine(point - direction * (length * 0.5f), tip, color, stroke, true);
                DrawLine(tip, head + side * (length * 0.18f), color, stroke, true);
                DrawLine(tip, head - side * (length * 0.18f), color, stroke, true);
            }
        }
    }
}
