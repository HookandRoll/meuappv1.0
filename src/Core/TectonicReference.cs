using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Numerics;
using System.Linq;

namespace Mapmaker.Core;

// Compact creation-time reference. Coordinates are in hex-center world units.
public sealed record PlateReference(float X, float Y, float Vx, float Vy, bool Continental);
public sealed record PlateContact(float X, float Y, float EndX, float EndY, float Compression, float Shear, int A, int B);
public sealed record TectonicReference(ImmutableArray<PlateReference> Plates, ImmutableArray<PlateContact> Contacts)
{
    internal void Validate(int width, int height)
    {
        if (Plates.IsDefault || Contacts.IsDefault || Plates.Length is < 1 or > 128 || Contacts.Length > 32768)
            throw new System.IO.InvalidDataException("Referência tectônica inválida.");
        bool Point(float x, float y) => float.IsFinite(x) && float.IsFinite(y) && x >= 0 && y >= 0 &&
            x <= width * HexGrid.Root3 + 1 && y <= height * 1.5f + 1;
        foreach (var p in Plates)
            if (p is null || !Point(p.X, p.Y) || !float.IsFinite(p.Vx) || !float.IsFinite(p.Vy) || Math.Abs(p.Vx) > 4 || Math.Abs(p.Vy) > 4)
                throw new System.IO.InvalidDataException("Placa tectônica inválida.");
        foreach (var c in Contacts)
            if (c is null || !Point(c.X, c.Y) || !Point(c.EndX, c.EndY) || (uint)c.A >= Plates.Length || (uint)c.B >= Plates.Length ||
                !float.IsFinite(c.Compression) || !float.IsFinite(c.Shear))
                throw new System.IO.InvalidDataException("Fronteira tectônica inválida.");
    }

    internal static TectonicReference Create(WorldManifest m)
    {
        var random = new Random(m.Seed ^ 0x173AC92);
        float width = m.Width * HexGrid.Root3, height = m.Height * 1.5f;
        int count = m.GeneratorVersion >= 19
            ? Math.Clamp(6 + (int)MathF.Round(MathF.Sqrt((float)m.Width * m.Height) / 24f), 10, 96)
            : Math.Clamp(m.Generation.Continents * 3 + 4, 7, 28);
        var plates = new PlateReference[count];
        Vector2 Delta(Vector2 a, Vector2 b)
        {
            var d = b - a;
            if (m.HorizontalWrap) d.X -= MathF.Round(d.X / width) * width;
            return d;
        }
        for (int i = 0; i < count; i++)
        {
            Vector2 best = default; float bestDistance = -1;
            for (int attempt = 0; attempt < 32; attempt++)
            {
                var point = new Vector2(random.NextSingle() * width, random.NextSingle() * height);
                float nearest = float.MaxValue;
                for (int j = 0; j < i; j++) nearest = Math.Min(nearest, Delta(point, new(plates[j].X, plates[j].Y)).LengthSquared());
                if (nearest > bestDistance) { best = point; bestDistance = nearest; }
            }
            float angle = random.Next(6) * MathF.PI / 3;
            Vector2 velocity = new(MathF.Cos(angle), MathF.Sin(angle));
            if (random.NextSingle() < 0.65f)
            {
                float secondary = angle + (random.Next(2) == 0 ? -1 : 1) * MathF.PI / 3;
                velocity += new Vector2(MathF.Cos(secondary), MathF.Sin(secondary)) * (0.3f + random.NextSingle() * 0.6f);
            }
            velocity = Vector2.Normalize(velocity) * (0.55f + random.Next(3) * 0.5f);
            plates[i] = new(best.X, best.Y, velocity.X, velocity.Y, random.NextSingle() < 0.45f);
        }
        // A bounded auxiliary grid, independent of the final hex count.
        int columns = Math.Clamp(m.Width / 8, 16, 128), rows = Math.Clamp(m.Height / 8, 8, 64);
        float scale = Math.Min(width, height);
        Vector2 Warp(Vector2 p) => p + new Vector2(
            MathF.Sin(p.X / width * MathF.Tau * 2 + MathF.Sin(p.Y / height * 7)) * scale * 0.035f,
            MathF.Cos(p.X / width * MathF.Tau * 3 + p.Y / height * 5) * scale * 0.035f);
        float Distance(Vector2 p, int plate) => Delta(Warp(p), new(plates[plate].X, plates[plate].Y)).LengthSquared();
        int Owner(Vector2 p)
        {
            int owner = 0; float nearest = float.MaxValue;
            for (int j = 0; j < count; j++)
            {
                float distance = Distance(p, j);
                if (distance < nearest) { nearest = distance; owner = j; }
            }
            return owner;
        }
        var contacts = new List<PlateContact>();
        void Add(int a, int b, Vector2 start, Vector2 end)
        {
            if (a == b || (end - start).LengthSquared() < 0.00001f) return;
            Vector2 normal = Vector2.Normalize(Delta(new(plates[a].X, plates[a].Y), new(plates[b].X, plates[b].Y)));
            Vector2 relative = new(plates[a].Vx - plates[b].Vx, plates[a].Vy - plates[b].Vy);
            float compression = Vector2.Dot(relative, normal);
            float shear = (relative - normal * compression).Length();
            contacts.Add(new(start.X, start.Y, end.X, end.Y, compression, shear, a, b));
        }
        Vector2 Crossing(Vector2 a, Vector2 b, int pa, int pb)
        {
            float low = 0, high = 1;
            for (int iteration = 0; iteration < 10; iteration++)
            {
                float middle = (low + high) * 0.5f;
                Vector2 point = Vector2.Lerp(a, b, middle);
                if (Distance(point, pa) <= Distance(point, pb)) low = middle; else high = middle;
            }
            return Vector2.Lerp(a, b, (low + high) * 0.5f);
        }
        void Triangle(Vector2 a, Vector2 b, Vector2 c)
        {
            int pa = Owner(a), pb = Owner(b), pc = Owner(c);
            if (pa == pb && pb == pc) return;
            if (pa == pb) Add(pa, pc, Crossing(a, c, pa, pc), Crossing(b, c, pb, pc));
            else if (pa == pc) Add(pa, pb, Crossing(a, b, pa, pb), Crossing(c, b, pc, pb));
            else if (pb == pc) Add(pb, pa, Crossing(b, a, pb, pa), Crossing(c, a, pc, pa));
            else
            {
                Vector2 ab = Crossing(a, b, pa, pb), bc = Crossing(b, c, pb, pc), ca = Crossing(c, a, pc, pa);
                Vector2 middle = (ab + bc + ca) / 3;
                Add(pa, pb, ab, middle); Add(pb, pc, bc, middle); Add(pc, pa, ca, middle);
            }
        }
        for (int y = 0; y < rows; y++)
        for (int x = 0; x < columns; x++)
        {
            Vector2 a = new(x * width / columns, y * height / rows);
            Vector2 b = new((x + 1) * width / columns, y * height / rows);
            Vector2 c = new((x + 1) * width / columns, (y + 1) * height / rows);
            Vector2 d = new(x * width / columns, (y + 1) * height / rows);
            Triangle(a, b, c); Triangle(a, c, d);
        }
        // Simultaneous speed adjustment from actual neighboring plates. Far-away
        // seeds never create local forces; direction remains the selected hex blend.
        var adjusted = (PlateReference[])plates.Clone();
        for (int i = 0; i < plates.Length; i++)
        {
            int plateId = i;
            var neighbors = contacts.Where(c => c.A == plateId || c.B == plateId)
                .Select(c => c.A == plateId ? c.B : c.A).Distinct()
                .OrderBy(j => Delta(new(plates[i].X, plates[i].Y), new(plates[j].X, plates[j].Y)).LengthSquared()).Take(5).ToArray();
            float resistance = 0;
            foreach (int j in neighbors)
            {
                Vector2 normal = Vector2.Normalize(Delta(new(plates[i].X, plates[i].Y), new(plates[j].X, plates[j].Y)));
                resistance += Vector2.Dot(new Vector2(plates[i].Vx - plates[j].Vx, plates[i].Vy - plates[j].Vy), normal);
            }
            float factor = Math.Clamp(1 - resistance / Math.Max(1, neighbors.Length) * 0.12f, 0.7f, 1.3f);
            adjusted[i] = plates[i] with { Vx = plates[i].Vx * factor, Vy = plates[i].Vy * factor };
        }
        plates = adjusted;
        for (int i = 0; i < contacts.Count; i++)
        {
            var c = contacts[i];
            Vector2 normal = Vector2.Normalize(Delta(new(plates[c.A].X, plates[c.A].Y), new(plates[c.B].X, plates[c.B].Y)));
            Vector2 relative = new(plates[c.A].Vx - plates[c.B].Vx, plates[c.A].Vy - plates[c.B].Vy);
            float compression = Vector2.Dot(relative, normal);
            contacts[i] = c with { Compression = compression, Shear = (relative - compression * normal).Length() };
        }
        return new(plates.ToImmutableArray(), contacts.ToImmutableArray());
    }
}
