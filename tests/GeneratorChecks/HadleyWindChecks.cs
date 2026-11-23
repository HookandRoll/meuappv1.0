using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    internal static void CheckHadleyWind(bool large = false)
    {
        var defaults = new HadleyWindSettings();
        Require(defaults.Iterations == 80 && defaults.TimeStep > 0 && defaults.VerticalExchange > 0, "Hadley defaults were zeroed");
        var manifest = CheckManifest(128, 64, true);
        manifest = manifest with { Generation = manifest.Generation with { Stage = GenerationStage.Relief } };
        var ocean = new WorldData(manifest);
        Array.Fill(ocean.Water, WaterType.Ocean);
        var timer = Stopwatch.StartNew();
        var field = HadleyWindSimulation.Simulate(ocean);
        CheckMass(field);
        var repeated = HadleyWindSimulation.Simulate(ocean);
        Require(field.Vx.SequenceEqual(repeated.Vx) && field.UpperVy.SequenceEqual(repeated.UpperVy)
            && field.VerticalFlow.SequenceEqual(repeated.VerticalFlow) && field.LowerMass.SequenceEqual(repeated.LowerMass), "Hadley is nondeterministic");
        int north = 27 * field.GridWidth + 64, south = 36 * field.GridWidth + 64;
        Require(field.Vy[north] > 0 && field.UpperVy[north] < 0 && field.Vy[south] < 0 && field.UpperVy[south] > 0,
            "Hadley surface/upper return circulation did not close");
        Require(field.VerticalFlow[31 * field.GridWidth + 64] > 0 && field.VerticalFlow[22 * field.GridWidth + 64] < 0,
            "Hadley failed equatorial ascent or subtropical descent");
        Require(field.Vx[north] < 0 && field.Vx[south] < 0, "Coriolis trade winds have the wrong hemisphere sign");
        for (int y = 0; y < 64; y++)
        {
            int a = y * 128 + 64, b = (63 - y) * 128 + 64;
            Require(Math.Abs(field.Vy[a] + field.Vy[b]) < 0.001f, "Symmetric ocean acquired a hemispheric bias");
        }
        Require(field.Sample(-1, 0) == default && field.Sample(128, 0) == default, "Hadley sampled outside the map");
        Require(field.Sample(64, 27, HadleyAtmosphereLayer.Upper).Vy < 0, "Upper inspection sampled surface winds");
        Require(field.Vx[0] == field.Vx[127], "Homogeneous ocean has a wrap seam");

        var isolated = HadleyWindSimulation.Simulate(ocean, defaults with { PressureForce = 0, Iterations = 1 });
        for (int i = 0; i < isolated.Vx.Length; i++)
            Require(Math.Abs(isolated.LowerMass[i] + isolated.UpperMass[i] - 2) < 1e-6f, "Vertical exchange created air");
        var disabled = HadleyWindSimulation.Simulate(ocean, defaults with { PressureForce = 0, ConvectiveLift = 0 });
        Require(disabled.VerticalFlow.All(w => w == 0) && disabled.LowerMass.All(m => m == 1), "Unforced layers generated circulation");

        var mountains = new WorldData(manifest);
        Array.Fill(mountains.Water, WaterType.Ocean);
        for (int y = 18; y < 42; y++)
        for (int x = 55; x < 64; x++)
        {
            int i = y * 128 + x;
            mountains.Water[i] = WaterType.None;
            mountains.Elevation[i] = (short)(x == 60 ? 6000 : 2500);
        }
        var beforeHeights = (short[])mountains.Elevation.Clone(); var beforeWater = (WaterType[])mountains.Water.Clone();
        var affected = HadleyWindSimulation.Simulate(mountains);
        CheckMass(affected);
        Require(!affected.Vx.SequenceEqual(field.Vx) && !affected.UpperVx.SequenceEqual(field.UpperVx), "Terrain did not affect coupled winds");
        Require(beforeHeights.SequenceEqual(mountains.Elevation) && beforeWater.SequenceEqual(mountains.Water)
            && mountains.Rainfall.All(p => p == 0) && mountains.Geography is null, "Hadley modified world generation arrays");
        var unblocked = HadleyWindSimulation.Simulate(mountains, defaults with { OrographicDeflection = 0, OrographicPressure = 0, MountainDrag = 0 });
        Require(!unblocked.Vy.SequenceEqual(affected.Vy), "Mountain blocking had no effect");
        var closed = HadleyWindSimulation.Simulate(new WorldData(manifest with { HorizontalWrap = false }));
        CheckMass(closed);
        var stress = HadleyWindSimulation.Simulate(mountains, defaults with { Iterations = 400 });
        CheckMass(stress);

        foreach (var invalid in new[] { default(HadleyWindSettings), defaults with { TimeStep = float.NaN },
            defaults with { MaxGridWidth = 1024 }, defaults with { Diffusion = 1 } })
        {
            bool rejected = false;
            try { HadleyWindSimulation.Simulate(ocean, invalid); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Require(rejected, "Invalid Hadley coefficients accepted");
        }
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        bool canceled = false;
        try { HadleyWindSimulation.Simulate(ocean, cancellationToken: cancellation.Token); } catch (OperationCanceledException) { canceled = true; }
        Require(canceled, "Hadley ignored cancellation");
        var wider = new WorldData(manifest with { Width = 512, Height = 256 }); Array.Fill(wider.Water, WaterType.Ocean);
        var fine = HadleyWindSimulation.Simulate(wider, new HadleyWindSettings(MaxGridWidth: 512, MaxGridHeight: 256));
        CheckMass(fine);
        Require(fine.GridWidth == 512 && fine.GridHeight == 256, "Hadley silently capped the 512x256 grid");
        var sample = fine.Sample(256, 108);
        Require(sample.Vy > 0 && fine.Sample(256, 108, HadleyAtmosphereLayer.Upper).Vy < 0, "Finer grid reversed the circulation");
        Console.WriteLine($"PASS Hadley two layers: defaults, determinism, mass, return flow, Coriolis, terrain, stability, cancellation, 128x64/512x256 ({timer.Elapsed.TotalSeconds:F2}s)");

        if (large)
        {
            var size = MapDomain.ExpandedSize(4096, 2048);
            var atlasManifest = manifest with { Width = size.Width, Height = size.Height, HorizontalWrap = false,
                Generation = manifest.Generation with { Extent = MapExtent.World, ReferenceWidth = 4096, ReferenceHeight = 2048 } };
            var atlas = new WorldData(atlasManifest); Array.Fill(atlas.Water, WaterType.Ocean);
            timer.Restart();
            var atlasField = HadleyWindSimulation.Simulate(atlas);
            CheckMass(atlasField);
            Require(atlasField.GridWidth == 512 && atlasField.GridHeight == 256, "Large atlas did not choose 512x256");
            var bounds = atlas.Domain.RowBounds(0);
            Require(atlasField.Sample(bounds.Item1, 0).Speed > 0 && atlasField.Sample(0, 0) == default,
                "Curved atlas mapping lost its edge or included unused corners");
            Console.WriteLine($"PASS Hadley atlas {atlas.Width}x{atlas.Height}: 512x256, {timer.Elapsed.TotalSeconds:F2}s, retained={GC.GetTotalMemory(true) / 1048576.0:F2} MiB");
        }

        static void CheckMass(HadleyWindField f)
        {
            double total = f.LowerMass.Sum(m => (double)m) + f.UpperMass.Sum(m => (double)m);
            Require(Math.Abs(total - f.Vx.Length * 2d) / (f.Vx.Length * 2d) < 1e-5, "Atmospheric mass was not conserved");
            foreach (var values in new[] { f.Vx, f.Vy, f.UpperVx, f.UpperVy, f.Pressure, f.UpperPressure,
                f.VerticalFlow, f.LowerMass, f.UpperMass, f.Vorticity, f.UpperVorticity })
                Require(values.All(float.IsFinite), "Nonfinite atmospheric value");
            Require(f.LowerMass.All(m => m > 0) && f.UpperMass.All(m => m > 0), "Negative atmospheric mass");
        }
    }
}
