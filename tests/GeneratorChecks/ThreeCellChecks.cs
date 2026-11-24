using System;
using System.IO;
using System.Linq;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    internal static void CheckMoistureTransport()
    {
        double landRain = 1 - HadleyCoupledClimateSimulation.CloudRetention(0.25, 3, 0, 0.5);
        double oceanRain = 1 - HadleyCoupledClimateSimulation.CloudRetention(0.25, 3, 1, 0.5);
        double coastRain = 1 - HadleyCoupledClimateSimulation.CloudRetention(0.25, 3, 0.5, 0.5);
        Require(Math.Abs(oceanRain / landRain - 0.5) < 1e-12
            && Math.Abs(coastRain / landRain - 0.75) < 1e-12,
            "Ocean/coastal cloud loss is not proportional to ocean coverage");
        var world = new WorldData(CheckManifest(128, 64, true) with { GeneratorVersion = MoistureTransportVersion });
        Array.Fill(world.Water, WaterType.Ocean); Array.Fill(world.Elevation, (short)-1000);
        Array.Fill(world.Temperature, (short)250);
        var winds = new HadleyWindSettings(Cells: new AtmosphericCellSettings());
        var duration = new HadleyMoistureSettings(WarmupSteps: 40, SampleSteps: 40);
        var revised = HadleyCoupledClimateSimulation.Simulate(world, winds, duration);
        var legacy = HadleyCoupledClimateSimulation.Simulate(world, winds, duration, coupling: new HadleyCoupledSettings());
        Require(Math.Abs(revised.Moisture.InitialWater / legacy.Moisture.InitialWater - 1.5) < 1e-10,
            "Vapor capacity did not increase initial storage by 50 percent");
        Require(revised.Moisture.Coupled!.Settings is { VaporCapacityMultiplier: 1.5, OceanRainShare: 0.5 }
            && revised.Moisture.Coupled.CloudWater > 0 && revised.Moisture.RelativeBudgetError < 1e-5,
            "New moisture recipe lost retained clouds or violated conservation");
        var oldWorld = new WorldData(world.Manifest with { GeneratorVersion = ThreeCellVersion });
        Array.Copy(world.Water, oldWorld.Water, world.Count); Array.Copy(world.Elevation, oldWorld.Elevation, world.Count);
        Array.Copy(world.Temperature, oldWorld.Temperature, world.Count);
        var oldRecipe = HadleyCoupledClimateSimulation.Simulate(oldWorld, winds, duration);
        Require(oldRecipe.Moisture.AnnualRain.SequenceEqual(legacy.Moisture.AnnualRain),
            "v65 recipe changed retroactively");
        foreach (var invalid in new[] { new HadleyCoupledSettings(VaporCapacityMultiplier: double.NaN),
            new HadleyCoupledSettings(OceanRainShare: -0.1) })
        {
            bool rejected = false;
            try { HadleyCoupledClimateSimulation.Simulate(world, winds, duration, coupling: invalid); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Require(rejected, "Invalid moisture transport settings accepted");
        }
        Console.WriteLine($"PASS Moisture transport: ocean loss 50%, mixed coast 75%, vapor storage +50%, retained clouds, water conservation, v65 unchanged; rain {legacy.Moisture.AnnualRain.Average():F3}->{revised.Moisture.AnnualRain.Average():F3}; vapor {legacy.Moisture.Coupled!.Vapor.Average():F3}->{revised.Moisture.Coupled.Vapor.Average():F3}");
    }

    internal static void CheckThreeCells()
    {
        var manifest = CheckManifest(128, 64, true);
        var ocean = new WorldData(manifest);
        Array.Fill(ocean.Water, WaterType.Ocean); Array.Fill(ocean.Elevation, (short)-1000);
        Array.Fill(ocean.Temperature, (short)250);
        var cells = new AtmosphericCellSettings();
        var settings = new HadleyWindSettings(Iterations: 400, Cells: cells);
        var wind = HadleyWindSimulation.Simulate(ocean, settings);
        ValidateAir(wind);
        CheckDirections(wind);
        var repeat = HadleyWindSimulation.Simulate(ocean, settings);
        Require(wind.Vx.SequenceEqual(repeat.Vx) && wind.UpperVy.SequenceEqual(repeat.UpperVy)
            && wind.VerticalFlow.SequenceEqual(repeat.VerticalFlow), "Three-cell winds are nondeterministic");
        foreach (float boundary in new[] { cells.TropicalEdgeDegrees, cells.PolarEdgeDegrees })
            Require(Math.Abs(HadleyWindSimulation.CellPressure(boundary - 0.001f, cells)
                - HadleyWindSimulation.CellPressure(boundary + 0.001f, cells)) < 0.00001,
                "Cell transitions have a pressure discontinuity");
        var noFerrel = cells with { FerrelStrength = 0 };
        Require(HadleyWindSimulation.CellPressure(30, noFerrel) == HadleyWindSimulation.CellPressure(60, noFerrel),
            "Ferrel strength does not control its pressure contrast");
        var zero = HadleyWindSimulation.Simulate(ocean, settings with
            { Cells = new AtmosphericCellSettings(0, 0, 0) });
        Require(zero.Vx.All(v => v == 0) && zero.Vy.All(v => v == 0)
            && zero.VerticalFlow.All(v => v == 0), "Disabled cell forcing still generated winds");
        foreach (var invalid in new[] { cells with { FerrelStrength = float.NaN }, cells with { PolarStrength = -1 },
            cells with { TropicalEdgeDegrees = 70 }, cells with { PolarEdgeDegrees = 20 } })
        {
            bool rejected = false;
            try { HadleyWindSimulation.Simulate(ocean, settings with { Cells = invalid }); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Require(rejected, "Invalid cell settings accepted");
        }
        var duration = new HadleyMoistureSettings(WarmupSteps: 80, SampleSteps: 80);
        var coupled = HadleyCoupledClimateSimulation.Simulate(ocean, settings, duration);
        var single = HadleyCoupledClimateSimulation.Simulate(ocean, settings with { Cells = null, UseZonalPressureBands = false }, duration);
        ValidateAir(coupled.Wind); CheckDirections(coupled.Wind);
        Require(coupled.Wind.Settings.Cells == cells && coupled.Wind.Settings.UseZonalPressureBands
            && coupled.Moisture.RelativeBudgetError < 1e-5, "Coupled solver discarded cells or violated water conservation");
        double subpolar = BandRain(coupled.Moisture.AnnualRain, 55, 65);
        double singleSubpolar = BandRain(single.Moisture.AnnualRain, 55, 65);
        Require(subpolar > singleSubpolar && subpolar > 0, "Subpolar convergence did not change simulated rain");

        var terrain = new WorldData(manifest);
        Array.Fill(terrain.Water, WaterType.Ocean); Array.Fill(terrain.Elevation, (short)-1000);
        Array.Fill(terrain.Temperature, (short)250);
        for (int y = 14; y < 54; y++) for (int x = 58; x < 65; x++)
        { int i = y * terrain.Width + x; terrain.Water[i] = WaterType.None; terrain.Elevation[i] = 3000; }
        var affected = HadleyWindSimulation.Simulate(terrain, settings);
        Require(!affected.Vx.SequenceEqual(wind.Vx) && !affected.UpperVy.SequenceEqual(wind.UpperVy),
            "Cell profile overrode interaction with continents and mountains");
        ValidateAir(HadleyWindSimulation.Simulate(terrain, settings with { Iterations = 400 }));
        var fineOcean = new WorldData(CheckManifest(512, 256, true));
        Array.Fill(fineOcean.Water, WaterType.Ocean); Array.Fill(fineOcean.Temperature, (short)250);
        ValidateAir(HadleyWindSimulation.Simulate(fineOcean, settings with { MaxGridWidth = 512, MaxGridHeight = 256 }));

        var mainManifest = manifest with { GeneratorVersion = CloudTransportVersion, HorizontalWrap = false };
        var world = Create(mainManifest);
        Require(world.ClimateWind is { Settings.Cells: not null, Settings.UseZonalPressureBands: true },
            "Main creation bypassed the three-cell atmosphere");
        var direct = HadleyCoupledClimateSimulation.Simulate(world, HadleySettingsFor(world));
        for (int i = 0; i < world.Count; i++)
        {
            ushort expected = (ushort)MathF.Round((float)Math.Min(5000, direct.Moisture.AnnualRain[i] * HadleyRainScale));
            Require(world.Rainfall[i] == expected, "Main rain contains a latitude mask outside the coupled simulation");
        }
        string path = Path.Combine(Path.GetTempPath(), $"three-cells-{Guid.NewGuid():N}.world");
        try
        {
            WorldFile.Save(path, world, null); var loaded = WorldFile.Load(path);
            Require(UsesThreeCellCirculation(loaded.Manifest) && loaded.Rainfall.SequenceEqual(world.Rainfall),
                "Save/load lost the three-cell recipe");
            var region = new WorldRegion(16, 16, 8, 8);
            for (int y = 16; y < 24; y++) for (int x = 16; x < 24; x++) loaded.Rainfall[y * loaded.Width + x] = 60000;
            loaded.LockMask[16 * loaded.Width + 16] = (ushort)HexLocks.Rainfall;
            RegenerateRegion(loaded, region, RegenerationMode.Everything, HexLocks.Rainfall);
            for (int i = 0; i < loaded.Count; i++)
                Require(loaded.Rainfall[i] == (i == 16 * loaded.Width + 16 ? 60000 : world.Rainfall[i]),
                    "Three-cell regeneration changed its recipe, selection or locks");
        }
        finally { File.Delete(path); }
        Console.WriteLine($"PASS Three cells: opposite Ferrel/return flow, polar/equatorial convergence, smooth configurable pressure, zero forcing, determinism, terrain, 400 steps, 512x256, water/air conservation, main rain without extra mask, save/load and regional locks; subpolar rain {singleSubpolar:F3}->{subpolar:F3}");

        static void ValidateAir(HadleyWindField f)
        {
            double error = Math.Abs(f.LowerMass.Sum(v => (double)v) + f.UpperMass.Sum(v => (double)v) - 2d * f.Vx.Length) / (2d * f.Vx.Length);
            Require(error < 1e-5 && f.LowerMass.All(v => float.IsFinite(v) && v > 0)
                && f.UpperMass.All(v => float.IsFinite(v) && v > 0)
                && f.Vx.Concat(f.Vy).Concat(f.UpperVx).Concat(f.UpperVy).All(float.IsFinite), "Invalid three-cell air mass/state");
        }
        static void CheckDirections(HadleyWindField f)
        {
            foreach (var (row, direction) in new[] { (37, -1), (48, 1), (58, -1) })
            {
                int i = row * f.GridWidth + f.GridWidth / 2;
                Require(f.Vy[i] * direction > 0 && f.UpperVy[i] * direction < 0,
                    $"Cell at row {row} did not close its surface/upper circulation: lower={f.Vy[i]}, upper={f.UpperVy[i]}");
            }
            Require(f.VerticalFlow[32 * f.GridWidth] > 0 && f.VerticalFlow[53 * f.GridWidth] > 0
                && f.VerticalFlow[42 * f.GridWidth] < 0 && f.VerticalFlow[63 * f.GridWidth] < 0,
                "Three-cell vertical convergence has the wrong sign");
        }
        static double BandRain(float[] rain, double minimum, double maximum)
            => Enumerable.Range(0, 64).Where(y => Math.Abs((y + 0.5) / 64 * 2 - 1) * 90 >= minimum
                && Math.Abs((y + 0.5) / 64 * 2 - 1) * 90 < maximum).Average(y => rain[y * 128 + 64]);
    }
}
