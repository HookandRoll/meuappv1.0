using System;
using System.Collections.Generic;
using System.Linq;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    internal static void CheckWaterMargins() => ContinentalSculpture.CheckWaterMargins();

    private sealed partial class ContinentalSculpture
    {
        public static void CheckWaterMargins()
        {
            foreach (bool wrap in new[] { false, true })
            {
                var manifest = CheckManifest(384, 256, wrap);
                (WaterType[] Water, int Changed) Run(int seed)
                {
                    var sculpt = new ContinentalSculpture(manifest with { Seed = seed });
                    var water = new WaterType[384 * 256];
                    for (int y = 0; y < 256; y++)
                    for (int x = 0; x < 384; x++)
                    {
                        float a = (x - 100) / 43f, b = (y - 110) / 65f;
                        float c = (x - 230) / 55f, d = (y - 130) / 38f;
                        bool wet = a * a + b * b < 1 || c * c + d * d < 1 || x > 340;
                        // One-cell outlet to the open sea, plus a small island.
                        wet |= y == 130 && x >= 230;
                        if ((x - 100) * (x - 100) + (y - 110) * (y - 110) < 49) wet = false;
                        int i = y * 384 + x;
                        water[i] = wet ? WaterType.Lake : WaterType.None;
                        sculpt._scores[i] = wet ? -0.06f : 0.06f;
                    }
                    int[] Areas(bool land) => ComponentAreas(water, land, manifest);
                    var landBefore = Areas(true); int wetBefore = Areas(false).Length;
                    var original = (WaterType[])water.Clone();
                    sculpt.SculptWaterMargins(water, 0);
                    if (!landBefore.SequenceEqual(Areas(true)) || wetBefore != Areas(false).Length)
                        throw new Exception("Shore sculpt changed island areas or land/water connectivity.");
                    int changed = water.Where((v, i) => v != original[i]).Count();
                    if (changed < 100) throw new Exception($"Shore sculpt did not remodel margins: {changed}.");
                    return (water, changed);
                }
                var first = Run(1234); var second = Run(1234); var other = Run(-77);
                if (!first.Water.SequenceEqual(second.Water) || first.Water.SequenceEqual(other.Water))
                    throw new Exception("Shore noise is not deterministic and seed dependent.");

                var channelSculpt = new ContinentalSculpture(manifest);
                var channel = new bool[384 * 256]; var fixedCore = new bool[channel.Length];
                for (int y = 0; y < 256; y++) channel[y * 384 + 192] = true;
                for (int y = 0; y < 256; y++) fixedCore[y * 384 + 200] = true;
                channelSculpt.WidenNaturalChannels(channel, fixedCore, 5);
                var widths = Enumerable.Range(10, 236).Select(y => Enumerable.Range(y * 384, 384).Count(i => channel[i])).ToArray();
                if (widths.Max() - widths.Min() < 3 || channel.Where((v, i) => v && fixedCore[i]).Any())
                    throw new Exception("Channel width remained uniform or eroded a protected core.");
                var channelWater = channel.Select(v => v ? WaterType.Lake : WaterType.None).ToArray();
                if (ComponentAreas(channelWater, false, manifest).Length != 1)
                    throw new Exception("Channel widening created disconnected water.");
                Console.WriteLine($"PASS: water margins wrap={wrap}, {first.Changed} remodeled cells, exact island areas, outlets/topology, repeatable seeds; channel width={widths.Min()}..{widths.Max()}");
            }
        }

        private static int[] ComponentAreas(WaterType[] water, bool land, WorldManifest manifest)
        {
            var seen = new bool[water.Length]; var sizes = new List<int>(); var queue = new Queue<int>();
            for (int start = 0; start < water.Length; start++)
            {
                if (seen[start] || (water[start] == WaterType.None) != land) continue;
                int size = 0; seen[start] = true; queue.Enqueue(start);
                while (queue.TryDequeue(out int i))
                {
                    size++; var hex = HexGrid.FromOffset(i % manifest.Width, i / manifest.Width);
                    for (int d = 0; d < 6; d++)
                    {
                        int n = HexGrid.NeighborIndex(hex, d, manifest.Width, manifest.Height, manifest.HorizontalWrap);
                        if (n < 0 || seen[n] || (water[n] == WaterType.None) != land) continue;
                        seen[n] = true; queue.Enqueue(n);
                    }
                }
                sizes.Add(size);
            }
            return sizes.OrderBy(n => n).ToArray();
        }
    }
}
