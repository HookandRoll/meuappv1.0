using System;
using System.IO;
using System.Linq;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    internal static void CheckBalancedRain()
    {
        double ratio = 40.0 / 58.5;
        foreach (var (width, height) in new[] { (128, 64), (256, 128), (512, 256), (1024, 512), (2048, 1024), (4096, 2048) })
        foreach (bool atlas in new[] { false, true })
        {
            var size = MapDomain.ExpandedSize(width, height);
            var manifest = CheckManifest(atlas ? size.Width : width, atlas ? size.Height : height, false) with
            {
                GeneratorVersion = BalancedRainVersion,
                Generation = new(TerrainAlgorithm.ContinentalSculpt, 3, 25, 80, GenerationStage.Terrain,
                    atlas ? MapExtent.World : MapExtent.Region, ReferenceWidth: atlas ? width : 0, ReferenceHeight: atlas ? height : 0)
            };
            WorldData.Validate(manifest);
            var settings = HadleyCoupledClimateSimulation.CouplingFor(manifest);
            var previous = HadleyCoupledClimateSimulation.CouplingFor(manifest with { GeneratorVersion = HexRainSpreadVersion });
            Require(settings.VaporCapacityMultiplier * 30 == 40
                && settings == previous with { VaporCapacityMultiplier = 40.0 / 30 },
                "Capacity 40 depends on map size or altered unrelated atmospheric settings");
            foreach (var surface in new[] { (1f, 0f, 0f, 0f), (0f, 1f, 0f, 0f), (0f, 0f, 1f, 0f), (0f, 0f, 0f, 1f) })
            {
                double before = HadleyCoupledClimateSimulation.AquaticEvaporation(58.5, 58.5 * 0.55,
                    surface.Item1, surface.Item2, surface.Item3, surface.Item4, 0.18f, 0.25f, previous);
                double after = HadleyCoupledClimateSimulation.AquaticEvaporation(40, 40 * 0.55,
                    surface.Item1, surface.Item2, surface.Item3, surface.Item4, 0.18f, 0.25f, settings);
                Require(Math.Abs(after / before - ratio) < 1e-12, "Vapor supply was not reduced in the same proportion as storage");
            }
        }
        foreach (bool wrap in new[] { false, true })
        {
            var world = new WorldData(CheckManifest(64, 32, wrap) with { GeneratorVersion = BalancedRainVersion });
            var source = new float[world.Count]; int center = 16 * world.Width + 32; source[center] = 1000;
            var copy = (float[])source.Clone();
            var result = Enumerable.Range(0, world.Count).Select(i => SpreadHexRain(world, source, i)).ToArray();
            var origin = HexGrid.FromOffset(32, 16);
            for (int i = 0; i < result.Length; i++)
            {
                int distance = HexGrid.Distance(origin, HexGrid.FromOffset(i % world.Width, i / world.Width), world.Width, wrap);
                float expected = distance switch { 0 => 500, 1 => 150, 2 => 50, _ => 0 };
                Require(Math.Abs(result[i] - expected) < 1e-4, "Second ring missed, duplicated or misweighted a hex");
            }
            Require(result.Count(v => v > 0) == 19 && Math.Abs(result.Sum(v => (double)v) - 2000) < 1e-3,
                "Rain impulse did not cover exactly 1 + 6 + 12 hexes with total 200%");
            Require(source.SequenceEqual(copy), "Two-ring spread modified its original source");
            Array.Fill(source, 1000);
            Require(Math.Abs(SpreadHexRain(world, source, center) - 2000) < 1e-3, "Uniform rain does not total 200% before cap");
            Array.Clear(source); source[16 * world.Width] = 1000;
            result = Enumerable.Range(0, world.Count).Select(i => SpreadHexRain(world, source, i)).ToArray();
            Require((result[16 * world.Width + world.Width - 2] > 0) == wrap, "Second ring crossed a closed edge or ignored wrap");
            if (wrap) Require(result.Count(v => v > 0) == 19 && Math.Abs(result.Sum(v => (double)v) - 2000) < 1e-3,
                "Wrapped second ring lost or duplicated contributors");
        }
        var sizeAtlas = MapDomain.ExpandedSize(128, 64);
        var atlasManifest = CheckManifest(sizeAtlas.Width, sizeAtlas.Height, false) with
        {
            GeneratorVersion = BalancedRainVersion,
            Generation = new(TerrainAlgorithm.ContinentalSculpt, 3, 25, 80, GenerationStage.Terrain,
                MapExtent.World, ReferenceWidth: 128, ReferenceHeight: 64)
        };
        var atlasWorld = new WorldData(atlasManifest); var ghosts = new float[atlasWorld.Count];
        for (int i = 0; i < ghosts.Length; i++) if (!atlasWorld.Domain.Contains(i)) ghosts[i] = 5000;
        Require(Enumerable.Range(0, ghosts.Length).All(i => SpreadHexRain(atlasWorld, ghosts, i) == 0),
            "Second rain ring leaked from outside the Atlas footprint");

        var smallManifest = CheckManifest(96, 64, false) with { GeneratorVersion = BalancedRainVersion };
        var wind = new HadleyWindSettings(Cells: new AtmosphericCellSettings());
        foreach (short temperature in new short[] { -300, 0, 250, 450 })
        {
            var ocean = new WorldData(smallManifest);
            Array.Fill(ocean.Water, WaterType.Ocean); Array.Fill(ocean.Temperature, temperature);
            var duration = new HadleyMoistureSettings(WarmupSteps: 0, SampleSteps: 1);
            var lower = HadleyCoupledClimateSimulation.Simulate(ocean, wind, duration);
            var old = HadleyCoupledClimateSimulation.Simulate(ocean, wind, duration,
                coupling: HadleyCoupledClimateSimulation.CouplingFor(smallManifest with { GeneratorVersion = HexRainSpreadVersion }));
            Require(Math.Abs(lower.Moisture.InitialWater / old.Moisture.InitialWater - ratio) < 1e-10,
                "Initial vapor storage did not fall proportionally in both layers");
        }
        var emptyWorld = new WorldData(smallManifest);
        var empty = HadleyCoupledClimateSimulation.Simulate(emptyWorld, wind,
            new HadleyMoistureSettings(WarmupSteps: 10, SampleSteps: 10, EvaporationRelaxation: 0),
            coupling: HadleyCoupledClimateSimulation.CouplingFor(smallManifest) with { InitialRelativeHumidity = 0, InitialSoilWater = 0 });
        Require(empty.Moisture.FinalWater == 0 && empty.Moisture.PrecipitatedWater == 0, "Reduced atmosphere created water without a source");

        var generated = Create(smallManifest);
        var climate = HadleyCoupledClimateSimulation.Simulate(generated, HadleySettingsFor(generated));
        var baseRain = new float[generated.Count];
        for (int row = 0; row < generated.Height; row++) for (int col = 0; col < generated.Width; col++)
        {
            float x = (col + 0.5f) / generated.Width * climate.Wind.GridWidth - 0.5f;
            float y = (row + 0.5f) / generated.Height * climate.Wind.GridHeight - 0.5f;
            baseRain[row * generated.Width + col] = (float)Math.Min(5000, HadleyWindSimulation.SampleGrid(
                climate.Moisture.AnnualRain, climate.Wind.GridWidth, climate.Wind.GridHeight, climate.Wind.WrapHorizontal, x, y) * HadleyRainScale);
        }
        for (int i = 0; i < generated.Count; i++)
            Require(generated.Rainfall[i] == (ushort)MathF.Round(Math.Min(5000, SpreadHexRain(generated, baseRain, i))),
                "v72 generation bypassed two-ring spreading or its final cap");
        Require(climate.Moisture.RelativeBudgetError < 1e-5, "Capacity 40 violated the atmospheric water budget");
        string path = Path.Combine(Path.GetTempPath(), $"balanced-rain-{Guid.NewGuid():N}.world");
        try
        {
            WorldFile.Save(path, generated, null); var loaded = WorldFile.Load(path);
            RegenerateRegion(loaded, new WorldRegion(0, 0, loaded.Width, loaded.Height));
            Require(loaded.Rainfall.SequenceEqual(generated.Rainfall) && loaded.Biomes.SequenceEqual(generated.Biomes),
                "v72 save/load or regeneration changed the seeded recipe");
            var region = new WorldRegion(32, 24, 8, 8);
            int locked = region.Row * loaded.Width + region.Column, outside = 40 * loaded.Width + 70;
            for (int y = 24; y < 32; y++) for (int x = 32; x < 40; x++) loaded.Rainfall[y * loaded.Width + x] = 9999;
            loaded.Rainfall[locked] = 7777; loaded.LockMask[locked] = (ushort)HexLocks.Rainfall; loaded.Rainfall[outside] = 6666;
            RegenerateRegion(loaded, region, RegenerationMode.Everything, HexLocks.Rainfall);
            for (int i = 0; i < loaded.Count; i++)
                Require(loaded.Rainfall[i] == (i == locked ? 7777 : i == outside ? 6666 : generated.Rainfall[i]),
                    "Second-ring regional regeneration ignored its selection/locks or reused modified rain");
        }
        finally { File.Delete(path); }
        Console.WriteLine($"PASS balanced rain: capacity 40 and proportional supply on all sizes/modes, both layers/four temperatures, 500 + six 150 + twelve 50, 19 unique hexes, 200% total, closed edges/wrap/Atlas, immutable pass, cap, water budget, empty system, v72 save/load/regeneration/locks; error {climate.Moisture.RelativeBudgetError:E3}");
    }
}
