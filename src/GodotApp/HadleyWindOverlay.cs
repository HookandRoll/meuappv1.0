using System;
using Godot;
using Mapmaker.Core;

namespace Mapmaker.GodotApp;

internal partial class HadleyWindOverlay : Node2D
{
    private static readonly Color Rising = new("#ff9e64");
    private static readonly Color Neutral = new("#9ce9ff");
    private static readonly Color Sinking = new("#96a8ff");

    internal MapDomain? Domain;
    internal HadleyWindField? Field;
    internal HadleyAtmosphereLayer Layer;

    public override void _Ready() => SetNotifyTransform(true);

    public override void _Notification(int what)
    {
        if (what == NotificationTransformChanged && Visible)
            QueueRedraw();
    }

    public override void _Draw()
    {
        if (Domain is not MapDomain domain || Field is not HadleyWindField field)
            return;

        Transform2D transform = GetGlobalTransformWithCanvas();
        float zoom = transform.X.Length();
        Transform2D inverse = transform.AffineInverse();
        Rect2 viewport = GetViewportRect().Grow(32);
        Vector2 topLeft = inverse * viewport.Position;
        Vector2 bottomRight = inverse * viewport.End;

        int rowStep = Math.Max(1, Math.Min(domain.Height / 22, (int)(34 / (zoom * 1.5f))));
        int columnStep = Math.Max(1, Math.Min(domain.Width / 28, (int)(48 / (zoom * HexGrid.Root3))));
        int firstRow = Math.Max(0, (int)(topLeft.Y / (rowStep * 1.5f))) * rowStep + rowStep / 2;
        int lastRow = Math.Min(domain.Height - 1, Mathf.CeilToInt(bottomRight.Y / 1.5f));
        int firstColumn = Mathf.FloorToInt(topLeft.X / HexGrid.Root3) - 1;
        int lastColumn = Mathf.CeilToInt(bottomRight.X / HexGrid.Root3);
        float maxLength = Mathf.Min(rowStep * 0.92f, 28f / zoom);
        float baseStroke = Mathf.Min(maxLength * 0.12f, 1.8f / zoom);

        for (int row = firstRow; row <= lastRow; row += rowStep)
        {
            var (left, right) = domain.RowBounds(row);
            int first = left + Math.Min(columnStep, right - left) / 2;
            first += Math.Max(0, (firstColumn - first) / columnStep) * columnStep;
            int last = Math.Min(right - 1, lastColumn);

            for (int col = first; col <= last; col += columnStep)
            {
                HadleyWindSample sample = field.Sample(col, row, Layer);
                if (sample.Speed < 0.02f)
                    continue;

                Vector2 direction = new Vector2(sample.Vx, sample.Vy) / sample.Speed;
                Vector2 side = direction.Orthogonal();

                float speedScale = Math.Clamp(0.35f + sample.Speed * 0.65f, 0.35f, 1.20f);
                float length = maxLength * speedScale;
                float stroke = baseStroke * Math.Clamp(0.8f + sample.Speed * 0.25f, 0.8f, 1.3f);

                Color color = sample.VerticalFlow >= 0f
                    ? Neutral.Lerp(Rising, Math.Clamp(sample.VerticalFlow / 0.04f, 0f, 1f))
                    : Neutral.Lerp(Sinking, Math.Clamp(-sample.VerticalFlow / 0.04f, 0f, 1f));

                var center = HexGrid.Center(HexGrid.FromOffset(col, row));
                Vector2 point = new(center.X, center.Y);
                Vector2 tail = point - direction * (length * 0.48f);
                Vector2 tip = point + direction * (length * 0.52f);
                Vector2 head = tip - direction * (length * 0.30f);

                DrawLine(tail, tip, color, stroke, true);
                DrawLine(tip, head + side * (length * 0.18f), color, stroke, true);
                DrawLine(tip, head - side * (length * 0.18f), color, stroke, true);
            }
        }
    }
}
