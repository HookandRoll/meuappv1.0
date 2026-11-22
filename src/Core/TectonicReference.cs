using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Numerics;
using System.Linq;

namespace Mapmaker.Core;

// Compact creation-time reference. Coordinates are in hex-center world units.
public enum PlateKind { Normal, Arctic, Antarctic, OceanicMargin }
public sealed record PlateReference(float X, float Y, float Vx, float Vy, bool Continental, float Size = 1f, PlateKind Kind = PlateKind.Normal)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsPolar => Kind is PlateKind.Arctic or PlateKind.Antarctic;
}
public sealed record PlateContact(float X, float Y, float EndX, float EndY, float Compression, float Shear, int A, int B);
public sealed record TectonicReference(ImmutableArray<PlateReference> Plates, ImmutableArray<PlateContact> Contacts)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public int NormalPlateCount => Plates.Count(p => p.Kind == PlateKind.Normal);
    internal int GenerationPlateCount(WorldManifest m) => m.GeneratorVersion >= 39 ? NormalPlateCount : Plates.Length;
    internal void Validate(int width, int height)
    {
        if (Plates.IsDefault || Contacts.IsDefault || Plates.Length is < 1 or > 128 || Contacts.Length > 32768)
            throw new System.IO.InvalidDataException("Referência tectônica inválida.");
        bool Point(float x, float y) => float.IsFinite(x) && float.IsFinite(y) && x >= 0 && y >= 0 &&
            x <= width * HexGrid.Root3 + 1 && y <= height * 1.5f + 1;
        foreach (var p in Plates)
            if (p is null || !Point(p.X, p.Y) || !float.IsFinite(p.Vx) || !float.IsFinite(p.Vy) || Math.Abs(p.Vx) > 4 || Math.Abs(p.Vy) > 4 || !float.IsFinite(p.Size) || p.Size is < 0.15f or > 3f || !Enum.IsDefined(p.Kind) || (p.Kind != PlateKind.Normal && p.Continental))
                throw new System.IO.InvalidDataException("Placa tectônica inválida.");
        int arctic = Plates.Count(p => p.Kind == PlateKind.Arctic), antarctic = Plates.Count(p => p.Kind == PlateKind.Antarctic);
        if ((arctic != 0 || antarctic != 0) && (arctic != 1 || antarctic != 1))
            throw new System.IO.InvalidDataException("As placas polares devem formar um par único.");
        foreach (var c in Contacts)
            if (c is null || !Point(c.X, c.Y) || !Point(c.EndX, c.EndY) || (uint)c.A >= Plates.Length || (uint)c.B >= Plates.Length ||
                !float.IsFinite(c.Compression) || !float.IsFinite(c.Shear))
                throw new System.IO.InvalidDataException("Fronteira tectônica inválida.");
    }

    internal static Vector2 WarpPoint(WorldManifest m, Vector2 p, int count)
    {
        float width = m.Width * HexGrid.Root3, height = m.Height * 1.5f;
        float scale = Math.Min(width, height);
        bool varied = m.GeneratorVersion >= 32;
        var legacy = new Vector2(
            MathF.Sin(p.X / width * MathF.Tau * 2 + MathF.Sin(p.Y / height * 7)) * scale * 0.035f,
            MathF.Cos(p.X / width * MathF.Tau * 3 + p.Y / height * 5) * scale * 0.035f);
        if (m.GeneratorVersion < 31) return p + legacy;
        // Continuous seeded domain warp bends the actual ownership field, so
        // relief and the tectonic overlay share the same irregular boundaries.
        float aspect = width / height;
        float spacing = MathF.Sqrt(width * height / count);
        uint seed = unchecked((uint)m.Seed);
        float Noise(float frequency, uint salt) => WorldGenerator.TerrainNoise(
            p.X / height, p.Y / height, frequency, aspect, m.HorizontalWrap, seed ^ salt) - 0.5f;
        var broad = new Vector2(
            Noise(4f, 0x48B3A29Du) * 0.50f + Noise(11f, 0x9D71C543u) * 0.16f,
            Noise(4f, 0x72E4F139u) * 0.50f + Noise(11f, 0xA3C95B27u) * 0.16f) * spacing;
        if (!varied) return p + legacy + broad;
        // Fine-scale displacement gives corners and short broken runs while
        // keeping the broad bends. Frequencies follow plate spacing.
        float frequency = height / spacing;
        float AngularNoise(float f, uint salt) => WorldGenerator.TerrainNoise(
            p.X / height, p.Y / height, f, aspect, m.HorizontalWrap, seed ^ salt, angular: true) - 0.5f;
        var jagged = new Vector2(
            AngularNoise(frequency * 6f, 0x87D1A635u) * 0.23f + AngularNoise(frequency * 14f, 0xCB46F921u) * 0.10f,
            AngularNoise(frequency * 6f, 0x2F93B847u) * 0.23f + AngularNoise(frequency * 14f, 0x61AE753Du) * 0.10f) * spacing;
        return p + legacy + broad + jagged;
    }

    internal Vector2 ContactNormal(WorldManifest m, PlateContact contact) =>
        BoundaryNormal(m, Plates, contact.A, contact.B, new(contact.X, contact.Y), new(contact.EndX, contact.EndY));

    private static Vector2 BoundaryNormal(WorldManifest m, IReadOnlyList<PlateReference> plates,
        int a, int b, Vector2 start, Vector2 end)
    {
        Vector2 tangent = end - start;
        Vector2 normal = Vector2.Normalize(new Vector2(-tangent.Y, tangent.X));
        Vector2 middle = (start + end) * 0.5f;
        float width = m.Width * HexGrid.Root3;
        int count = m.GeneratorVersion >= 39 ? plates.Count(p => p.Kind == PlateKind.Normal) : plates.Count;
        float Difference(Vector2 point)
        {
            PlateKind kind = plates[a].IsPolar ? plates[a].Kind : plates[b].IsPolar ? plates[b].Kind : PlateKind.Normal;
            if (kind != PlateKind.Normal)
            {
                float depth = PolarPlateBands.Depth(m, point.X, kind);
                float signed = kind == PlateKind.Arctic ? point.Y - depth : m.Height * 1.5f - point.Y - depth;
                return plates[a].IsPolar ? signed : -signed;
            }
            bool marginA = plates[a].Kind == PlateKind.OceanicMargin, marginB = plates[b].Kind == PlateKind.OceanicMargin;
            if (marginA != marginB)
            {
                var margin = marginA ? plates[a] : plates[b];
                float signed = OceanicMarginBands.SignedDistance(m, point, margin.X < width * 0.5f ? -1 : 1);
                return marginA ? signed : -signed;
            }
            point = WarpPoint(m, point, count);
            float Distance(int id)
            {
                var p = plates[id];
                Vector2 delta = point - new Vector2(p.X, p.Y);
                if (m.HorizontalWrap) delta.X -= MathF.Round(delta.X / width) * width;
                return delta.LengthSquared() / (p.Size * p.Size);
            }
            return Distance(a) - Distance(b);
        }
        // Orient A -> B using the ownership field. Center-to-center orientation
        // is wrong on the far side of a weighted microplate or a folded boundary.
        float epsilon = Math.Min(width, m.Height * 1.5f) * 0.0001f;
        return Difference(middle + normal * epsilon) < Difference(middle - normal * epsilon) ? -normal : normal;
    }

    internal static TectonicReference Create(WorldManifest m)
    {
        var random = new Random(m.Seed ^ 0x173AC92);
        var domain = new MapDomain(m);
        float width = m.Width * HexGrid.Root3, height = m.Height * 1.5f;
        int count = m.GeneratorVersion >= 19
            ? Math.Clamp(6 + (int)MathF.Round(MathF.Sqrt((float)m.Width * m.Height) / 24f), 10, 96)
            : Math.Clamp(m.Generation.Continents * 3 + 4, 7, 28);
        bool varied = m.GeneratorVersion >= 32;
        if (varied) count = Math.Clamp(8 + (int)MathF.Round(MathF.Sqrt((float)m.Width * m.Height) / 100f), 12, 40);
        int majorCount = varied ? Math.Clamp(count / 2, 6, 14) : count;
        if (m.GeneratorVersion >= 33)
        {
            // Seed-dependent hierarchy, instead of the same evenly packed layout
            // with only a different outline. Large oceans and uneven plate sizes
            // are allowed to change the global arrangement of the continents.
            count = Math.Clamp(count + random.Next(-2, 7), 12, 44);
            count = Math.Max(count, m.Generation.Continents * 2 + 2);
            majorCount = Math.Clamp(Math.Max(count / 2, m.Generation.Continents + 1), 6, 18);
        }
        var polarBands = PolarPlateBands.Enabled(m) ? new PolarPlateBands(m) : null;
        var margins = OceanicMarginBands.Enabled(m) ? new OceanicMarginBands(m) : null;
        int fragments = margins is null ? 0 : Math.Clamp(m.Height / 80, 6, 12);
        int arcticId = count, antarcticId = count + 1;
        int marginStart = count + (polarBands is null ? 0 : 2);
        var plates = new PlateReference[marginStart + fragments * 2];
        Vector2 Delta(Vector2 a, Vector2 b)
        {
            var d = b - a;
            if (m.HorizontalWrap) d.X -= MathF.Round(d.X / width) * width;
            return d;
        }
        for (int i = 0; i < count; i++)
        {
            Vector2 best = default; float bestDistance = -1;
            for (int attempt = 0; attempt < (m.GeneratorVersion >= 33 ? (i < majorCount ? 6 : 1) : 32); attempt++)
            {
                var point = new Vector2(random.NextSingle() * width, random.NextSingle() * height);
                while (domain.IsAtlas && (!domain.ContainsPoint(point) || (polarBands is not null && polarBands.KindAt(point) != PlateKind.Normal) || (margins is not null && margins.SideAt(point) != 0)))
                    point = new Vector2(random.NextSingle() * width, random.NextSingle() * height);
                float nearest = float.MaxValue;
                for (int j = 0; j < i; j++) nearest = Math.Min(nearest, Delta(point, new(plates[j].X, plates[j].Y)).LengthSquared());
                if (nearest > bestDistance) { best = point; bestDistance = nearest; }
            }
            if (varied && i >= majorCount)
            {
                // Microplates cluster near contacts between major plates rather
                // than dividing the entire planet into evenly sized cells.
                int host = random.Next(majorCount);
                int neighbor = Enumerable.Range(0, majorCount).Where(j => j != host)
                    .OrderBy(j => Delta(new(plates[host].X, plates[host].Y), new(plates[j].X, plates[j].Y)).LengthSquared())
                    .ElementAt(random.Next(Math.Min(3, majorCount - 1)));
                var origin = new Vector2(plates[host].X, plates[host].Y);
                best = origin + Delta(origin, new(plates[neighbor].X, plates[neighbor].Y)) * (0.35f + random.NextSingle() * 0.3f);
                float spread = MathF.Sqrt(width * height / majorCount) * 0.12f;
                best += new Vector2(random.NextSingle() - 0.5f, random.NextSingle() - 0.5f) * spread;
                best.X = m.HorizontalWrap ? (best.X % width + width) % width : Math.Clamp(best.X, 0, width);
                best.Y = Math.Clamp(best.Y, 0, height);
            }
            if (domain.IsAtlas) best = domain.ClampPoint(best);
            if (polarBands is not null)
                best.Y = Math.Clamp(best.Y, polarBands.North(best.X) + height * 0.015f,
                    height - polarBands.South(best.X) - height * 0.015f);
            if (margins is not null)
            {
                var bounds = margins.Bounds(best.Y);
                best.X = Math.Clamp(best.X, bounds.Left + width * 0.01f, bounds.Right - width * 0.01f);
            }
            float angle = random.Next(6) * MathF.PI / 3;
            Vector2 velocity = new(MathF.Cos(angle), MathF.Sin(angle));
            if (random.NextSingle() < 0.65f)
            {
                float secondary = angle + (random.Next(2) == 0 ? -1 : 1) * MathF.PI / 3;
                velocity += new Vector2(MathF.Cos(secondary), MathF.Sin(secondary)) * (0.3f + random.NextSingle() * 0.6f);
            }
            velocity = Vector2.Normalize(velocity) * (0.55f + random.Next(3) * 0.5f);
            bool continental = random.NextSingle() < 0.45f;
            float size = varied ? (i < majorCount ? 1.15f + random.NextSingle() * 0.65f : 0.24f + random.NextSingle() * 0.22f) : 1f;
            if (m.GeneratorVersion >= 33 && i < majorCount) size = 1.05f + random.NextSingle() * 1.6f;
            if (varied && i < majorCount) continental = (i & 1) == 0;
            if (m.GeneratorVersion >= 53 && domain.IsAtlas && i >= majorCount && continental)
            {
                // A minority of continental microplates retain broad island
                // shelves; most form smaller island nuclei in a world atlas.
                uint islandHash = unchecked((uint)m.Seed ^ (uint)i * 0x9E3779B9u);
                islandHash = unchecked((islandHash ^ (islandHash >> 16)) * 0x7FEB352Du);
                if (islandHash % 4 != 0) size = Math.Max(0.15f, size * 0.55f);
            }
            plates[i] = new(best.X, best.Y, velocity.X, velocity.Y, continental, size);
        }
        if (m.GeneratorVersion >= 33)
        {
            var order = Enumerable.Range(0, majorCount).ToArray();
            var crustRandom = new Random(m.Seed ^ 0x37A9C51);
            crustRandom.Shuffle(order);
            var continental = new HashSet<int>();
            if (m.GeneratorVersion >= 54 && domain.IsAtlas)
            {
                // Start with a seeded plate, then leave room for oceanic crust
                // between continental centers, especially around large plates.
                while (continental.Count < m.Generation.Continents)
                {
                    float Rank(int candidate)
                    {
                        var a = plates[candidate];
                        float nearest = continental.Min(other =>
                        {
                            var b = plates[other];
                            return Delta(new(a.X, a.Y), new(b.X, b.Y)).Length() / (1 + 0.15f * (a.Size + b.Size));
                        });
                        return nearest;
                    }
                    int next = continental.Count == 0 ? order[0] : order.Where(i => !continental.Contains(i))
                        .OrderByDescending(Rank).First();
                    continental.Add(next);
                }
            }
            else continental.UnionWith(order.Take(m.Generation.Continents));
            for (int i = 0; i < majorCount; i++)
                plates[i] = plates[i] with { Continental = continental.Contains(i) };
        }
        if (polarBands is not null)
        {
            plates[arcticId] = new(width * 0.5f, height * 0.02f, 0, -2.5f, false, 1.6f, PlateKind.Arctic);
            plates[antarcticId] = new(width * 0.5f, height * 0.98f, 0, 2.5f, false, 1.6f, PlateKind.Antarctic);
        }
        if (margins is not null)
        {
            var marginRandom = new Random(m.Seed ^ 0x1A6B937);
            for (int sideIndex = 0; sideIndex < 2; sideIndex++)
            for (int j = 0; j < fragments; j++)
            {
                float y = height * (0.13f + (j + 0.35f + marginRandom.NextSingle() * 0.3f) / fragments * 0.74f);
                var bounds = margins.Bounds(y);
                int side = sideIndex == 0 ? -1 : 1;
                Vector2 p = domain.ClampPoint(new(side < 0 ? bounds.Left - width * 0.025f : bounds.Right + width * 0.025f, y));
                plates[marginStart + sideIndex * fragments + j] = new(p.X, p.Y, side * (0.7f + marginRandom.NextSingle()),
                    (marginRandom.NextSingle() - 0.5f) * 1.6f, false, 0.28f + marginRandom.NextSingle() * 0.18f, PlateKind.OceanicMargin);
            }
        }
        // A bounded auxiliary grid, independent of the final hex count.
        int columns = Math.Clamp(m.Width / 8, 16, 128), rows = Math.Clamp(m.Height / 8, 8, 64);
        if (varied)
        {
            columns = Math.Clamp(m.Width / 4, 64, 192);
            rows = Math.Clamp(m.Height / 4, 32, 96);
        }
        Vector2 Warp(Vector2 p) => WarpPoint(m, p, m.GeneratorVersion >= 39 ? count : plates.Length);
        float Distance(Vector2 warped, int plate) => Delta(warped, new(plates[plate].X, plates[plate].Y)).LengthSquared()
            / (plates[plate].Size * plates[plate].Size);
        Vector2 ContactNormal(int a, int b, Vector2 start, Vector2 end)
        {
            Vector2 centers = Delta(new(plates[a].X, plates[a].Y), new(plates[b].X, plates[b].Y));
            if (m.GeneratorVersion >= 33) return BoundaryNormal(m, plates, a, b, start, end);
            if (!varied) return Vector2.Normalize(centers);
            Vector2 tangent = end - start;
            Vector2 normal = Vector2.Normalize(new Vector2(-tangent.Y, tangent.X));
            return Vector2.Dot(normal, centers) < 0 ? -normal : normal;
        }
        int Owner(Vector2 p)
        {
            PlateKind polarKind = polarBands?.KindAt(p) ?? PlateKind.Normal;
            if (polarKind == PlateKind.Arctic) return arcticId;
            if (polarKind == PlateKind.Antarctic) return antarcticId;
            int side = margins?.SideAt(p) ?? 0;
            p = Warp(p);
            int owner = 0; float nearest = float.MaxValue;
            int first = side == 0 ? 0 : marginStart + (side < 0 ? 0 : fragments);
            int last = side == 0 ? count : first + fragments;
            for (int j = first; j < last; j++)
            {
                float distance = Distance(p, j);
                if (distance < nearest) { nearest = distance; owner = j; }
            }
            return owner;
        }
        var contacts = new List<PlateContact>();
        void Add(int a, int b, Vector2 start, Vector2 end)
        {
            if (a == b || !domain.Clip(ref start, ref end) || (end - start).LengthSquared() < 0.00001f) return;
            Vector2 normal = ContactNormal(a, b, start, end);
            Vector2 relative = new(plates[a].Vx - plates[b].Vx, plates[a].Vy - plates[b].Vy);
            float compression = Vector2.Dot(relative, normal);
            if (plates[a].Kind != PlateKind.Normal || plates[b].Kind != PlateKind.Normal)
                compression = PolarPlateBands.ContactExtension(compression);
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
                Vector2 warped = Warp(point);
                bool sideA;
                if (margins is not null && (plates[pa].Kind == PlateKind.OceanicMargin || plates[pb].Kind == PlateKind.OceanicMargin))
                    sideA = Owner(point) == pa;
                else if (polarBands is not null && (plates[pa].IsPolar || plates[pb].IsPolar))
                {
                    PlateKind polarKind = plates[pa].IsPolar ? plates[pa].Kind : plates[pb].Kind;
                    bool insidePolar = polarBands.SignedDistance(point, polarKind) <= 0;
                    sideA = plates[pa].IsPolar ? insidePolar : !insidePolar;
                }
                else sideA = Distance(warped, pa) <= Distance(warped, pb);
                if (sideA) low = middle; else high = middle;
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
            if (plates[i].Kind != PlateKind.Normal) continue;
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
            Vector2 normal = ContactNormal(c.A, c.B, new(c.X, c.Y), new(c.EndX, c.EndY));
            Vector2 relative = new(plates[c.A].Vx - plates[c.B].Vx, plates[c.A].Vy - plates[c.B].Vy);
            float compression = Vector2.Dot(relative, normal);
            if (plates[c.A].Kind != PlateKind.Normal || plates[c.B].Kind != PlateKind.Normal)
                compression = PolarPlateBands.ContactExtension(compression);
            contacts[i] = c with { Compression = compression, Shear = (relative - compression * normal).Length() };
        }
        return new(plates.ToImmutableArray(), contacts.ToImmutableArray());
    }
}
