using System;
using System.IO;
using System.Linq;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    internal static void CheckPolarOcean()
    {
        foreach (bool wrap in new[] { false, true })
        foreach (GenerationStage stage in new[] { GenerationStage.Blank, GenerationStage.Land, GenerationStage.Relief, GenerationStage.Terrain })
        {
            var m = CheckManifest(256, 128, wrap) with { GeneratorVersion = 36 };
            m = m with { Generation = m.Generation with { Stage = stage, Extent = MapExtent.World } };
            var central = Create(m);
            int rows = PolarRowsFor(m.Height), shift = rows * m.Width;
            var paddedManifest = m with { Height = m.Height + 2 * rows, Generation = m.Generation with { PolarOceanRows = rows } };
            var padded = Create(paddedManifest);
            if (padded.Width != 256 || padded.Height != 144) throw new Exception("Polar rows changed the central dimensions.");
            if (!central.Water.AsSpan().SequenceEqual(padded.Water.AsSpan(shift, central.Count)) ||
                !central.Elevation.AsSpan().SequenceEqual(padded.Elevation.AsSpan(shift, central.Count)) ||
                !central.Biomes.AsSpan().SequenceEqual(padded.Biomes.AsSpan(shift, central.Count)) ||
                !central.Temperature.AsSpan().SequenceEqual(padded.Temperature.AsSpan(shift, central.Count)) ||
                !central.Rainfall.AsSpan().SequenceEqual(padded.Rainfall.AsSpan(shift, central.Count)))
                throw new Exception("Adding polar space changed central geography or climate.");
            if (stage == GenerationStage.Blank) continue;
            int land = padded.Water.Skip(shift).Take(central.Count).Count(w => w == WaterType.None);
            if (land != central.Count * m.Generation.LandPercent / 100) throw new Exception("Polar ocean was included in the land quota.");
            for (int col = 0; col < padded.Width; col++)
                if (padded.Water[col] != WaterType.Ocean || padded.Water[(padded.Height - 1) * padded.Width + col] != WaterType.Ocean)
                    throw new Exception("No ocean space remains at the flat polar edges.");
            if (padded.Tectonics!.Plates[0].Y != central.Tectonics!.Plates[0].Y + rows * 1.5f)
                throw new Exception("Plate coordinates were not shifted with the central map.");
            foreach (var river in ReadRivers(padded))
                for (int j = 1; j < river.Path.Length; j++)
                    if (RiverDirection(padded, river.Path[j - 1], river.Path[j]) < 0) throw new Exception("Polar river has a broken edge.");
            string path = Path.Combine(Path.GetTempPath(), $"polar-{Guid.NewGuid():N}.world");
            try
            {
                WorldFile.Save(path, padded, null);
                var loaded = WorldFile.Load(path);
                if (loaded.Manifest.Generation.PolarOceanRows != rows || !loaded.Water.SequenceEqual(padded.Water) ||
                    !loaded.RiverEdges.SequenceEqual(padded.RiverEdges)) throw new Exception("Polar metadata or rivers lost on save/load.");
            }
            finally { File.Delete(path); }
            if (stage == GenerationStage.Terrain)
            {
                var before = (BiomeType[])padded.Biomes.Clone();
                RegenerateRegion(padded, new(20, 0, 35, rows + 20), RegenerationMode.Everything, HexLocks.Biome);
                if (!before.SequenceEqual(padded.Biomes)) throw new Exception("Polar/core regional regeneration is not deterministic.");
                int locked = shift + 30 * padded.Width + 30;
                padded.Biomes[locked] = BiomeType.Alpine; padded.LockMask[locked] |= (ushort)HexLocks.Biome;
                RegenerateRegion(padded, new(20, rows + 20, 30, 30), RegenerationMode.Everything, HexLocks.Biome);
                if (padded.Biomes[locked] != BiomeType.Alpine) throw new Exception("Polar regeneration ignored a core biome lock.");
                var waterBefore = (WaterType[])padded.Water.Clone();
                var elevationBefore = (short[])padded.Elevation.Clone();
                RegenerateRegion(padded, new(0, 0, 50, rows + 25), RegenerationMode.KeepWaterAndElevation);
                if (!waterBefore.SequenceEqual(padded.Water) || !elevationBefore.SequenceEqual(padded.Elevation))
                    throw new Exception("Polar regeneration changed preserved water or elevation.");
                RebuildRivers(padded);
            }
        }
        var maximum = CheckManifest(4096, 2048, false) with { GeneratorVersion = 36 };
        maximum = maximum with { Height = 2304, Generation = maximum.Generation with { Extent = MapExtent.World, PolarOceanRows = 128 } };
        WorldData.Validate(maximum);
        Console.WriteLine("PASS: real polar rows, exact central terrain/climate/land quota, plates/rivers, stages, wrap, save/load, regional regeneration and locks; max 4096x2304 supported");
    }
}
