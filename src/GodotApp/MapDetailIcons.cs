using System;
using Mapmaker.Core;

namespace Mapmaker.GodotApp;

internal static class MapDetailIcons
{
    internal const int IconIdMask = 31;
    internal const int CloseZoomFlag = 32;

    private enum Icon : byte
    {
        None, Grama, Arbusto, Tronco, PlantaRolante, Cogumelos, Flores, VitoriaRegia, Coqueiro, Samambaia,
        ArvoreSeca, PedraGrama, PedraNeve, Caverna, FonteTermal, Ossada, Conchas, Recife,
        Tatu, Escorpiao, Lobo, Veado, Urso, Harpia, Cabra, Jacare, Caranguejo, Peixe, Baleia
    }

    // Texture layers follow the one-based icon IDs stored in the map texture.
    internal static readonly string[] FileNames =
    {
        "graminha", "arbusto", "arvore_caida", "planta_rolante", "cogumelos", "flores_silvestres",
        "vitoria_regia", "coqueiro", "samambaia", "arvore_seca", "pedra_com_grama", "pedra_com_neve",
        "caverna", "fonte_termal", "ossada", "conchas", "recife_de_corais", "tatu", "escorpiao",
        "lobo", "veado", "urso", "harpia", "cabra_da_montanha", "jacare", "caranguejo",
        "peixe_pulando", "cauda_de_baleia"
    };

    internal static byte[] Build(WorldData world, byte[] terrain)
    {
        var icons = new byte[world.Count];
        const int spacing = 7;
        bool hasDepth = world.Manifest.Generation.Stage >= GenerationStage.Relief;
        var above = new Icon[(world.Width + spacing - 1) / spacing];
        for (int top = 0; top < world.Height; top += spacing)
        {
            Icon left = Icon.None;
            int bottom = Math.Min(top + spacing, world.Height);
            for (int start = 0; start < world.Width; start += spacing)
            {
                int block = start / spacing;
                uint choice = Rank(top * world.Width + start, world.Manifest.Seed);
                int end = Math.Min(start + spacing, world.Width);
                Icon selected = Icon.None;
                if (choice % 6 != 0)
                {
                    int primary = PickCell(world, terrain, start, top, end, bottom, choice, hasDepth);
                    if (primary >= 0)
                    {
                        selected = PickIcon(world, primary, left, above[block]);
                        icons[primary] = (byte)selected;
                    }

                    int close = PickCell(world, terrain, start, top, end, bottom, choice, hasDepth, true, primary);
                    if (close >= 0)
                        icons[close] = (byte)((byte)PickIcon(world, close, selected, above[block]) | CloseZoomFlag);
                }
                left = selected;
                above[block] = selected;
            }
        }
        return icons;
    }

    private static int PickCell(WorldData world, byte[] terrain, int left, int top, int right, int bottom,
        uint choice, bool hasDepth, bool close = false, int primary = -1)
    {
        int best = -1;
        uint bestRank = 0;
        bool bestSharesHex = false;
        bool favorShared = close && (choice & 3u) == 0;
        int primaryRow = primary / world.Width;
        int primaryColumn = primary % world.Width;
        for (int row = top; row < bottom; row++)
        for (int column = left; column < right; column++)
        {
            int cell = row * world.Width + column;
            int symbols = terrain[cell * 4 + 3] & 0xc0;
            if ((!close && symbols != 0) ||
                (primary >= 0 && Math.Abs(row - primaryRow) <= 1 && Math.Abs(column - primaryColumn) <= 1))
                continue;
            // Keep ocean symbols sparser than land symbols, even at close zoom.
            if (world.Water[cell] == WaterType.Ocean)
            {
                bool shallow = hasDepth && world.Elevation[cell] >= -180;
                if (close ? !shallow || choice % 4 != 1 : choice % (shallow ? 4u : 13u) != 1)
                    continue;
            }
            bool sharesHex = favorShared && symbols == 0xc0;
            uint rank = Rank(cell, world.Manifest.Seed ^ (close ? 0x209AE43 : 0x1978E67));
            if (best < 0 || (sharesHex != bestSharesHex ? sharesHex : rank > bestRank))
            {
                best = cell;
                bestRank = rank;
                bestSharesHex = sharesHex;
            }
        }
        return best;
    }

    private static Icon PickIcon(WorldData world, int cell, Icon left, Icon above)
    {
        ReadOnlySpan<Icon> options = Habitat(world, cell);
        int first = (int)(Rank(cell, world.Manifest.Seed ^ 0x1768B3A) % (uint)options.Length);
        Icon selected = options[first];
        for (int i = 1; i < options.Length && (selected == left || selected == above); i++)
            selected = options[(first + i) % options.Length];
        return selected;
    }

    private static ReadOnlySpan<Icon> Habitat(WorldData world, int cell)
    {
        short temperature = world.Temperature[cell];
        bool climate = world.Manifest.Generation.Stage == GenerationStage.Terrain;
        switch (world.Water[cell])
        {
            case WaterType.Wetland:
                return temperature >= 145 ? [Icon.Jacare, Icon.VitoriaRegia, Icon.Peixe, Icon.Caranguejo] : [Icon.Peixe];
            case WaterType.Lake:
                return temperature >= 145 ? [Icon.VitoriaRegia, Icon.Peixe] : [Icon.Peixe];
            case WaterType.Ocean:
                if (world.Elevation[cell] < -1050)
                    return [Icon.Peixe, Icon.Baleia];
                return climate && temperature >= 185 && world.Elevation[cell] >= -180
                    ? [Icon.Peixe, Icon.Recife] : [Icon.Peixe];
        }

        BiomeType biome = world.Biomes[cell];
        if (biome == BiomeType.Beach)
            return temperature >= 165
                ? [Icon.Conchas, Icon.Caranguejo, Icon.Coqueiro] : [Icon.Conchas, Icon.Caranguejo, Icon.PedraGrama];
        if (world.Relief[cell] >= ReliefType.Mountains)
            return biome is BiomeType.Alpine or BiomeType.Tundra || (climate && temperature <= 0)
                ? [Icon.PedraNeve, Icon.Cabra, Icon.Caverna, Icon.FonteTermal]
                : [Icon.Cabra, Icon.Caverna, Icon.PedraGrama, Icon.FonteTermal, Icon.Urso];
        if (biome == BiomeType.None && world.IsCoast(cell))
            return [Icon.Conchas, Icon.PedraGrama, Icon.Caranguejo];

        return biome switch
        {
            BiomeType.TemperateForest => [Icon.Arbusto, Icon.Tronco, Icon.Cogumelos, Icon.Cogumelos, Icon.Veado, Icon.Lobo, Icon.Urso],
            BiomeType.Rainforest => [Icon.Samambaia, Icon.Tronco, Icon.Arbusto, Icon.Samambaia, Icon.Harpia, Icon.Tatu],
            BiomeType.Taiga => [Icon.Tronco, Icon.Cogumelos, Icon.PedraNeve, Icon.Arbusto, Icon.Lobo, Icon.Urso],
            BiomeType.Tundra or BiomeType.Alpine => [Icon.PedraNeve, Icon.PedraNeve, Icon.Arbusto, Icon.ArvoreSeca, Icon.Lobo],
            BiomeType.Desert => [Icon.PlantaRolante, Icon.Ossada, Icon.ArvoreSeca, Icon.PlantaRolante, Icon.Escorpiao],
            BiomeType.Steppe => [Icon.Grama, Icon.PedraGrama, Icon.ArvoreSeca, Icon.PlantaRolante, Icon.Ossada],
            BiomeType.Savanna => [Icon.Grama, Icon.Arbusto, Icon.ArvoreSeca, Icon.Flores, Icon.Tatu],
            _ => [Icon.Grama, Icon.PedraGrama, Icon.Flores, Icon.Grama, Icon.Arbusto, Icon.Veado]
        };
    }

    private static uint Rank(int cell, int seed)
    {
        uint value = unchecked((uint)cell * 0x9E3779B9u + (uint)seed);
        value = (value ^ (value >> 16)) * 0x7FEB352Du;
        value = (value ^ (value >> 15)) * 0x846CA68Bu;
        return value ^ (value >> 16);
    }
}
