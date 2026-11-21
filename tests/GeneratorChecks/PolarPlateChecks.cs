using System;
using System.IO;
using System.Linq;
using System.Numerics;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    internal static void CheckMandatoryPolarPlates()
    {
        foreach (int seed in new[] { 1234, 1, -77 })
        foreach (bool wrap in new[] { false, true })
        foreach (int land in new[] { 10, 38, 65 })
        foreach (int version in new[] { 38, 39, Version })
        {
            var size = MapDomain.ExpandedSize(128, 64);
            var m = CheckManifest(size.Width, size.Height, wrap) with { Seed = seed, GeneratorVersion = version };
            m = m with { Generation = m.Generation with { Extent = MapExtent.World,
                ReferenceWidth = 128, ReferenceHeight = 64, LandPercent = land } };
            var world = Create(m);
            var tectonics = world.Tectonics!;
            tectonics.Validate(world.Width, world.Height);
            int regularCount = TectonicReference.Create(m with { Generation = m.Generation with { Extent = MapExtent.Region } }).NormalPlateCount;
            int margins = tectonics.Plates.Count(p => p.Kind == PlateKind.OceanicMargin);
            if (tectonics.NormalPlateCount != regularCount || tectonics.Plates.Length != regularCount + 2 + margins ||
                tectonics.GenerationPlateCount(m) != (version >= 39 ? regularCount : regularCount + 2))
                throw new Exception("Polar plates changed the regular plate budget or generation count.");
            if (tectonics.Plates.Count(p => p.Kind == PlateKind.Arctic) != 1 ||
                tectonics.Plates.Count(p => p.Kind == PlateKind.Antarctic) != 1 ||
                tectonics.Plates.Any(p => p.Kind != PlateKind.Normal && p.Continental))
                throw new Exception("Missing, duplicated or continental polar plates.");
            var polarContacts = tectonics.Contacts.Where(c => tectonics.Plates[c.A].IsPolar || tectonics.Plates[c.B].IsPolar).ToArray();
            if (polarContacts.Length < 4 || polarContacts.Any(c => c.Compression > -0.25f))
                throw new Exception("Polar plate borders must permanently diverge.");
            var bands = new PolarPlateBands(m);
            var oceanMargins = OceanicMarginBands.Enabled(m) ? new OceanicMarginBands(m) : null;
            if (oceanMargins is not null)
            {
                var fragments = tectonics.Plates.Select((p, id) => (p, id)).Where(x => x.p.Kind == PlateKind.OceanicMargin).ToArray();
                if (fragments.Length < 12 || fragments.Any(x => x.p.Continental || x.p.Size >= 0.6f ||
                    oceanMargins.SideAt(new(x.p.X, x.p.Y)) == 0 ||
                    !tectonics.Contacts.Any(c => c.A == x.id || c.B == x.id)))
                    throw new Exception("Curved sides lack real fragmented oceanic plates.");
                if (fragments.Count(x => x.p.X < world.Width * HexGrid.Root3 * 0.5f) < 6 ||
                    fragments.Count(x => x.p.X > world.Width * HexGrid.Root3 * 0.5f) < 6)
                    throw new Exception("Oceanic fragments must cover both curved sides.");
            }
            else if (margins != 0) throw new Exception("Margin plates leaked into an old generator.");
            if (tectonics.Plates.Any(p => p.IsPolar && bands.KindAt(new(p.X, p.Y)) != p.Kind))
                throw new Exception("Polar plate markers are outside their own ocean band.");
            int nonPolarCount = 0;
            for (int row = 0; row < world.Height; row++)
            for (int col = 0; col < world.Width; col++)
            {
                if (!world.Domain.Contains(col, row)) continue;
                var point = HexGrid.Center(HexGrid.FromOffset(col, row));
                PlateKind kind = bands.KindAt(point);
                int cell = row * world.Width + col;
                bool margin = oceanMargins?.SideAt(point) != 0 && oceanMargins is not null;
                if (margin && (world.Water[cell] != WaterType.Ocean || world.Elevation[cell] >= 0 || world.Biomes[cell] != BiomeType.None))
                    throw new Exception("Land or an island reached the reserved oceanic margin.");
                if (oceanMargins is not null &&
                    (!world.Domain.Contains(col - 1, row) || !world.Domain.Contains(col + 1, row)) &&
                    kind == PlateKind.Normal && !margin)
                    throw new Exception("A curved edge is outside the oceanic plate belt.");
                if ((row == 0 && kind != PlateKind.Arctic) || (row == world.Height - 1 && kind != PlateKind.Antarctic))
                    throw new Exception("The full flat border is not covered by a polar plate.");
                if (kind == PlateKind.Normal) { nonPolarCount++; continue; }
                int i = row * world.Width + col;
                if (world.Water[i] != WaterType.Ocean || world.Elevation[i] >= 0 || world.Biomes[i] != BiomeType.None)
                    throw new Exception("A mandatory polar plate acquired land, mountains or an island.");
            }
            float[] depths = Enumerable.Range(world.Width / 4, world.Width / 2).Select(c => bands.North(c * HexGrid.Root3)).ToArray();
            if (depths.Max() - depths.Min() < world.Height * 1.5f * 0.005f) throw new Exception("Polar border is too straight.");
            int percentageArea = version >= 39 ? nonPolarCount : world.ActiveCount;
            if (nonPolarCount >= world.ActiveCount || world.Domain.PercentageAreaCount != percentageArea ||
                world.Water.Count(w => w == WaterType.None) != percentageArea * land / 100)
                throw new Exception("Incorrect land/sea quota or polar ocean included in the v39 denominator.");
            if (version == Version && seed == 1234 && !wrap)
                Console.WriteLine($"QUOTA: target={land}%, land={world.Water.Count(w => w == WaterType.None)}, nonpolar={nonPolarCount}, actual={100.0 * world.Water.Count(w => w == WaterType.None) / nonPolarCount:F3}%, margin plates={margins}");
            var regenerated = Create(m);
            if (!tectonics.Plates.SequenceEqual(regenerated.Tectonics!.Plates) || !tectonics.Contacts.SequenceEqual(regenerated.Tectonics.Contacts))
                throw new Exception("Polar plate generation is not deterministic.");
            RegenerateRegion(regenerated, new(world.Width / 4, 0, world.Width / 2, world.Height / 5), RegenerationMode.Everything, HexLocks.Biome);
            if (!world.Biomes.SequenceEqual(regenerated.Biomes) || !world.Water.SequenceEqual(regenerated.Water) ||
                !world.Elevation.SequenceEqual(regenerated.Elevation)) throw new Exception("Polar regeneration differs from initial generation.");
            string path = Path.Combine(Path.GetTempPath(), $"polar-plates-{Guid.NewGuid():N}.world");
            try
            {
                WorldFile.Save(path, world, null);
                var loaded = WorldFile.Load(path);
                if (loaded.Domain.PercentageAreaCount != percentageArea ||
                    !tectonics.Plates.SequenceEqual(loaded.Tectonics!.Plates) || !tectonics.Contacts.SequenceEqual(loaded.Tectonics.Contacts))
                    throw new Exception("Polar plate identities were lost on save/load.");
            }
            finally { File.Delete(path); }
        }
        var legacy = Create(CheckManifest(128, 64, false) with { GeneratorVersion = 37,
            Generation = CheckManifest(128, 64, false).Generation with { Extent = MapExtent.World } });
        var region = Create(CheckManifest(128, 64, false));
        if (legacy.Tectonics!.Plates.Any(p => p.Kind != PlateKind.Normal) || region.Tectonics!.Plates.Any(p => p.Kind != PlateKind.Normal))
            throw new Exception("Mandatory polar plates leaked into legacy Worlds or Regions.");
        Console.WriteLine("PASS: fragmented oceanic sides, no land on curved edges, exact nonpolar land quota; v38/v39 compatibility, polar plates, seeds/wrap, regeneration and save/load");
    }
}
