using System;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    private static int SmallBiomeLimit(int height) => Math.Clamp(height / 32, 6, 32);

    // Bounded connected-component searches keep this pass linear in map size and
    // avoid allocating a world-sized flood-fill queue. Rivers add local habitats later.
    private static void ConsolidateSmallBiomes(GenerationArea area, bool[] openPlains,
        Action<GenerationProgress>? progress)
    {
        int limit = SmallBiomeLimit(area.WorldHeight);
        var source = (BiomeType[])area.Biomes.Clone();
        var state = new byte[area.Count]; // 1: large or clipped, 2: small, 3: handled
        var queue = new int[limit + 1];
        int width = area.Chunks.Width;
        bool Eligible(int i) => area.Water[i] == WaterType.None &&
            source[i] is not (BiomeType.None or BiomeType.Beach or BiomeType.Alpine);
        Hex HexAt(int i) => area.WorldHex(i % width, i / width);

        for (int start = 0; start < area.Count; start++)
        {
            if (state[start] != 0 || !Eligible(start)) continue;
            int count = 1;
            queue[0] = start;
            bool large = false;
            for (int head = 0; head < count && !large; head++)
            {
                Hex hex = HexAt(queue[head]);
                for (int direction = 0; direction < 6; direction++)
                {
                    int next = area.Neighbor(hex, direction);
                    // A clipped component may continue outside a regional window.
                    if (next < 0) { large = true; break; }
                    if (!Eligible(next) || source[next] != source[start]) continue;
                    if (state[next] == 1) { large = true; break; }
                    if (Array.IndexOf(queue, next, 0, count) >= 0) continue;
                    queue[count++] = next;
                    if (count > limit) { large = true; break; }
                }
            }
            for (int j = 0; j < count; j++) state[queue[j]] = large ? (byte)1 : (byte)2;
        }

        var votes = new int[Enum.GetValues<BiomeType>().Length];
        for (int start = 0; start < area.Count; start++)
        {
            if (state[start] != 2) continue;
            Array.Clear(votes);
            queue[0] = start;
            state[start] = 3;
            int count = 1;
            for (int head = 0; head < count; head++)
            {
                Hex hex = HexAt(queue[head]);
                for (int direction = 0; direction < 6; direction++)
                {
                    int next = area.Neighbor(hex, direction);
                    if (next < 0 || !Eligible(next)) continue;
                    if (source[next] == source[start])
                    {
                        if (state[next] == 2) { state[next] = 3; queue[count++] = next; }
                    }
                    else if (state[next] == 1) votes[(int)source[next]]++;
                }
            }
            BiomeType replacement = BiomeType.None;
            int best = 0;
            for (int candidate = 0; candidate < votes.Length; candidate++)
            {
                if (votes[candidate] <= best) continue;
                var biome = (BiomeType)candidate;
                bool suitable = true;
                for (int j = 0; j < count && suitable; j++)
                {
                    int i = queue[j];
                    suitable = !(openPlains[i] && Biomes.IsForest(biome)) &&
                        FitsBiomeClimate(biome, area.Temperature[i], area.Rainfall[i]);
                }
                if (suitable) { replacement = biome; best = votes[candidate]; }
            }
            if (replacement != BiomeType.None)
                for (int j = 0; j < count; j++) area.Biomes[queue[j]] = replacement;
        }
        progress?.Invoke(new(GenerationStep.Biomes, 100));
    }

    // Small overlaps at thresholds allow ecotones to join their surrounding region.
    private static bool FitsBiomeClimate(BiomeType biome, int temperature, int rainfall) => biome switch
    {
        BiomeType.Tundra => temperature <= -25,
        BiomeType.Taiga => temperature is >= -85 and <= 75 && rainfall >= 320,
        BiomeType.Desert => temperature >= 80 && rainfall < 360,
        BiomeType.Steppe => temperature > -85 && rainfall < 580,
        BiomeType.Rainforest => temperature >= 195 && rainfall >= 1400,
        BiomeType.Savanna => temperature >= 195 && rainfall is >= 380 and < 1200,
        BiomeType.TemperateForest => temperature is > 15 and < 255 && rainfall >= 850,
        BiomeType.Grassland => temperature is > 15 and < 255 && rainfall is >= 380 and < 1150,
        _ => false
    };
}
