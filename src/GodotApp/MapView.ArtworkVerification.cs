using System;
using Godot;

namespace Mapmaker.GodotApp;

public partial class MapView
{
    private static void VerifyArtworkResources()
    {
        Require(MapArtwork.Variants == 3, "Shader e atlas precisam usar três variações.");
        var groups = new[]
        {
            ("tiles", false, new[] { "beach", "grasslands", "temperate_forest", "rainforest", "savanna", "steppe", "desert", "taiga", "tundra", "alpine", "shallow_ocean", "lake", "wetland", "deep_ocean" }),
            ("relief", true, new[] { "rolling_hills", "hills", "mountains", "peaks", "snowy_peaks" }),
            ("vegetation", true, new[] { "temperate_trees", "rainforest_canopy", "taiga_pines", "savanna_trees", "desert_scrub", "wetland_reeds" }),
            ("Icons", true, MapDetailIcons.FileNames)
        };
        int count = 0;
        foreach (var (folder, overlay, names) in groups)
        foreach (string name in names)
        for (int variant = 1; variant <= MapArtwork.Variants; variant++)
        {
            string path = $"res://{folder}/{name}{(variant == 1 ? "" : "_v" + variant)}.png";
            Require(ResourceLoader.Exists(path), "Arte ausente no pacote: " + path);
            using Texture2D texture = GD.Load<Texture2D>(path);
            using Image source = texture.GetImage();
            if (source.IsCompressed()) source.Decompress();
            Require(!source.IsEmpty() && source.GetWidth() > 0 && source.GetUsedRect().HasArea(), "Arte vazia: " + path);
            if (overlay) Require(source.DetectAlpha() != Image.AlphaMode.None, "Arte de sobreposição sem transparência: " + path);
            count++;
        }
        GD.Print($"ARTWORK PASSED: {count} required textures, 3 variants, nonempty images and overlay transparency verified.");
    }
}
