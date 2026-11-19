using Godot;
using Mapmaker.Core;
namespace Mapmaker.GodotApp;

public partial class TectonicOverlay : Node2D
{
    public TectonicReference? Reference { get; set; }
    public float SymbolSize { get; set; } = 5;
    public override void _Draw()
    {
        if (Reference is not { } reference) return;
        int counter = 0;
        foreach (var c in reference.Contacts)
        {
            Vector2 a = new(c.X, c.Y), b = new(c.EndX, c.EndY);
            Color color = c.Compression > 0.25f ? new Color("ef9677") : c.Compression < -0.25f ? new Color("81dce1") : new Color("d1cba3");
            DrawLine(a, b, color, 0.8f, true);
            if (++counter % 5 != 0) continue;
            Vector2 p = (a + b) * 0.5f;
            float r = SymbolSize * 0.45f;
            if (c.Compression < -0.25f) DrawArc(p, r, 0, Mathf.Tau, 16, color, 1, true);
            else if (c.Compression > 0.25f)
            {
                DrawLine(p - new Vector2(r, r), p + new Vector2(r, r), color, 1, true);
                DrawLine(p - new Vector2(r, -r), p + new Vector2(r, -r), color, 1, true);
            }
        }
        foreach (var plate in reference.Plates)
        {
            Vector2 origin = new(plate.X, plate.Y), direction = new Vector2(plate.Vx, plate.Vy).Normalized();
            Vector2 tip = origin + direction * SymbolSize * 3;
            DrawLine(origin, tip, Colors.White, 1.4f, true);
            Vector2 normal = new(-direction.Y, direction.X);
            DrawLine(tip, tip - direction * SymbolSize + normal * SymbolSize * 0.5f, Colors.White, 1.4f, true);
            DrawLine(tip, tip - direction * SymbolSize - normal * SymbolSize * 0.5f, Colors.White, 1.4f, true);
        }
    }
}
