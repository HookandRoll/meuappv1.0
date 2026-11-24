using System;
using System.IO;
using System.Linq;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    internal static void CheckHexRainSpread()
    {
        foreach (bool wrap in new[] { false, true })
        {
            var world = new WorldData(CheckManifest(64, 32, wrap) with { GeneratorVersion = HexRainSpreadVersion });
            var source = new float[world.Count];
            int center = 16 * world.Width + 32;
            source[center] = 1000;
            var copy = (float[])source.Clone();
            var result = Enumerable.Range(0, world.Count).Select(i => SpreadHexRain(world, source, i)).ToArray();
            Require(result[center] == 750 && result.Sum(v => (double)v) == 2250 && result.Count(v => v > 0) == 7,
                "Rain impulse did not produce 75% at center and 25% at exactly six neighbors");
            var hex = HexGrid.FromOffset(32, 16);
            for (int d = 0; d < 6; d++)
                Require(result[HexGrid.NeighborIndex(hex, d, world.Width, world.Height, wrap)] == 250,
                    "Rain share differs by hex direction");
            Require(source.SequenceEqual(copy), "Rain spread modified its immutable source");
            Array.Fill(source, 1000);
            Require(SpreadHexRain(world, source, center) == 2250, "Uniform rain did not receive the literal 225% gain");
            Array.Clear(source); source[16 * world.Width] = 1000;
            result = Enumerable.Range(0, world.Count).Select(i => SpreadHexRain(world, source, i)).ToArray();
            Require((result[16 * world.Width + world.Width - 1] > 0) == wrap,
                "Rain spread ignored the horizontal wrap setting");
            if (wrap) Require(result.Sum(v => (double)v) == 2250, "Wrapped impulse lost rain shares");
            else
            {
                Array.Clear(source); source[0] = 1000;
                result = Enumerable.Range(0, world.Count).Select(i => SpreadHexRain(world, source, i)).ToArray();
                Require(result[0] == 750 && result.Count(v => v > 0) == 3 && result.Sum(v => (double)v) == 1250,
                    "Closed corner reallocated shares outside the map");
            }
        }
        var expanded = MapDomain.ExpandedSize(128, 64);
        var atlasManifest = CheckManifest(expanded.Width, expanded.Height, false) with
        {
            GeneratorVersion = HexRainSpreadVersion,
            Generation = new(TerrainAlgorithm.ContinentalSculpt, 3, 25, 80, GenerationStage.Terrain,
                MapExtent.World, ReferenceWidth: 128, ReferenceHeight: 64)
        };
        var atlas = new WorldData(atlasManifest); var ghosts = new float[atlas.Count];
        for (int i = 0; i < ghosts.Length; i++) if (!atlas.Domain.Contains(i)) ghosts[i] = 5000;
        Require(Enumerable.Range(0, atlas.Count).All(i => SpreadHexRain(atlas, ghosts, i) == 0),
            "Atlas footprint leaked rain from inactive cells");

        var manifest = CheckManifest(96, 64, false) with { GeneratorVersion = HexRainSpreadVersion };
        var current = Create(manifest);
        var previous = Create(manifest with { GeneratorVersion = HumidityBoostVersion });
        Require(current.Elevation.SequenceEqual(previous.Elevation) && current.Water.SequenceEqual(previous.Water)
            && current.Temperature.SequenceEqual(previous.Temperature)
            && current.ClimateWind!.Vx.SequenceEqual(previous.ClimateWind!.Vx)
            && current.ClimateWind.UpperVy.SequenceEqual(previous.ClimateWind.UpperVy),
            "Hex rainfall bonus changed terrain, reference temperature or the atmosphere");
        var climate = HadleyCoupledClimateSimulation.Simulate(previous, HadleySettingsFor(previous));
        var baseRain = new float[previous.Count];
        for (int row = 0; row < previous.Height; row++) for (int col = 0; col < previous.Width; col++)
        {
            float x = (col + 0.5f) / previous.Width * climate.Wind.GridWidth - 0.5f;
            float y = (row + 0.5f) / previous.Height * climate.Wind.GridHeight - 0.5f;
            baseRain[row * previous.Width + col] = (float)Math.Min(5000,
                HadleyWindSimulation.SampleGrid(climate.Moisture.AnnualRain, climate.Wind.GridWidth,
                    climate.Wind.GridHeight, climate.Wind.WrapHorizontal, x, y) * HadleyRainScale);
        }
        for (int i = 0; i < current.Count; i++)
        {
            ushort expected = (ushort)MathF.Round(Math.Min(5000, SpreadHexRain(current, baseRain, i)));
            Require(current.Rainfall[i] == expected, "Normal generation bypassed hex spreading or its final rain cap");
        }
        Require(climate.Moisture.RelativeBudgetError < 1e-5
            && !current.Rainfall.SequenceEqual(previous.Rainfall), "Rain bonus is inactive or atmosphere lost water conservation");
        string path = Path.Combine(Path.GetTempPath(), $"hex-rain-spread-{Guid.NewGuid():N}.world");
        try
        {
            WorldFile.Save(path, current, null); var loaded = WorldFile.Load(path);
            RegenerateRegion(loaded, new WorldRegion(0, 0, loaded.Width, loaded.Height));
            Require(loaded.Rainfall.SequenceEqual(current.Rainfall) && loaded.Biomes.SequenceEqual(current.Biomes),
                "v71 save/load or regeneration changed the single-pass recipe");
            var region = new WorldRegion(32, 24, 8, 8);
            int locked = region.Row * loaded.Width + region.Column, outside = loaded.Width * 40 + 70;
            for (int y = 24; y < 32; y++) for (int x = 32; x < 40; x++) loaded.Rainfall[y * loaded.Width + x] = 9999;
            loaded.Rainfall[locked] = 7777; loaded.LockMask[locked] = (ushort)HexLocks.Rainfall;
            loaded.Rainfall[outside] = 6666;
            RegenerateRegion(loaded, region, RegenerationMode.Everything, HexLocks.Rainfall);
            for (int i = 0; i < loaded.Count; i++)
                Require(loaded.Rainfall[i] == (i == locked ? 7777 : i == outside ? 6666 : current.Rainfall[i]),
                    "Regional rain spread ignored selection/locks or reused already-spread rain");
        }
        finally { File.Delete(path); }
        Console.WriteLine("PASS hex rain spread: 1000 -> 750 + six 250, literal 225% gain, one immutable pass, closed edges, wrap, Atlas mask, normal generation/cap, unchanged atmosphere, v71 save/load, full and regional regeneration/locks");
    }
}
