using System;
using Godot;

namespace Mapmaker.GodotApp;

internal sealed class MapArtwork : IDisposable
{
    internal const int Variants = 3;

    private readonly Texture2DArray _ground = Load("tiles", 512, false,
        "beach", "grasslands", "temperate_forest", "rainforest", "savanna", "steppe", "desert",
        "taiga", "tundra", "alpine", "shallow_ocean", "lake", "wetland", "deep_ocean", "swamp");
    private readonly Texture2DArray _relief = Load("relief", 256, true,
        "rolling_hills", "hills", "mountains", "peaks", "snowy_peaks", "escarpment");
    private readonly Texture2DArray _vegetation = Load("vegetation", 256, true,
        "temperate_trees", "rainforest_canopy", "taiga_pines", "savanna_trees", "desert_scrub", "wetland_reeds", "swamp_trees");
    private readonly Texture2DArray _details = Load("Icons", 128, true, MapDetailIcons.FileNames);

    private readonly Texture2DArray _catalog = LoadCatalog();

    private static Texture2DArray LoadCatalog()
    {
        // 256 x 256 addresses every ushort ID without relying on enum order.
        byte[] biomes = new byte[256 * 256 * 4], relief = new byte[biomes.Length], icons = new byte[biomes.Length];
        for (int id = 0; id <= ushort.MaxValue; id++)
        {
            int i = id * 4;
            biomes[i] = 1; // Unknown ground -> grass fallback.
            biomes[i + 2] = biomes[i + 3] = 255; // No vegetation.
            relief[i] = relief[i + 1] = 255; // No relief symbol.
            icons[i] = icons[i + 1] = 255; // No detail symbol.
        }
        var ground = new[] { Mapmaker.Core.BiomeType.Beach, Mapmaker.Core.BiomeType.Grassland,
            Mapmaker.Core.BiomeType.TemperateForest, Mapmaker.Core.BiomeType.Rainforest,
            Mapmaker.Core.BiomeType.Savanna, Mapmaker.Core.BiomeType.Steppe, Mapmaker.Core.BiomeType.Desert,
            Mapmaker.Core.BiomeType.Taiga, Mapmaker.Core.BiomeType.Tundra, Mapmaker.Core.BiomeType.Alpine };
        for (int art = 0; art < ground.Length; art++) biomes[(int)ground[art] * 4] = (byte)art;
        biomes[(int)Mapmaker.Core.BiomeType.Swamp * 4] = 14;
        biomes[(int)Mapmaker.Core.BiomeType.Swamp * 4 + 2] = 6;
        biomes[(int)Mapmaker.Core.BiomeType.Swamp * 4 + 3] = 0;
        var vegetation = new[] { Mapmaker.Core.BiomeType.TemperateForest, Mapmaker.Core.BiomeType.Rainforest,
            Mapmaker.Core.BiomeType.Taiga, Mapmaker.Core.BiomeType.Savanna, Mapmaker.Core.BiomeType.Desert };
        for (int art = 0; art < vegetation.Length; art++)
        {
            int i = (int)vegetation[art] * 4 + 2;
            biomes[i] = (byte)art; biomes[i + 1] = 0;
        }
        var shapes = new[] { Mapmaker.Core.ReliefType.Rolling, Mapmaker.Core.ReliefType.Hills,
            Mapmaker.Core.ReliefType.Mountains, Mapmaker.Core.ReliefType.Peak };
        for (int art = 0; art < shapes.Length; art++)
        {
            int i = (int)shapes[art] * 4;
            relief[i] = (byte)art; relief[i + 1] = 0;
        }
        for (int art = 0; art < MapDetailIcons.FileNames.Length; art++)
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(icons.AsSpan((art + 1) * 4, 2), (ushort)art);
        var images = new Godot.Collections.Array<Image>();
        var catalog = new Texture2DArray();
        try
        {
            images.Add(Image.CreateFromData(256, 256, false, Image.Format.Rgba8, biomes));
            images.Add(Image.CreateFromData(256, 256, false, Image.Format.Rgba8, relief));
            images.Add(Image.CreateFromData(256, 256, false, Image.Format.Rgba8, icons));
            if (catalog.CreateFromImages(images) != Error.Ok) throw new InvalidOperationException("Catálogo de artes inválido.");
            return catalog;
        }
        catch { catalog.Dispose(); throw; }
        finally { foreach (var image in images) image.Dispose(); }
    }

    public void Bind(ShaderMaterial material)
    {
        material.SetShaderParameter("ground_art", _ground);
        material.SetShaderParameter("art_catalog", _catalog);
        material.SetShaderParameter("relief_art", _relief);
        material.SetShaderParameter("vegetation_art", _vegetation);
        material.SetShaderParameter("detail_art", _details);
    }

    public void Dispose()
    {
        _ground.Dispose();
        _catalog.Dispose();
        _relief.Dispose();
        _vegetation.Dispose();
        _details.Dispose();
    }

    private static Texture2DArray Load(string folder, int size, bool overlay, params string[] names)
    {
        var images = new Godot.Collections.Array<Image>();
        try
        {
            foreach (string name in names)
            {
                for (int v = 1; v <= Variants; v++)
                {
                    string fileName = v == 1 ? name : $"{name}_v{v}";
                    string path = $"res://{folder}/{fileName}.png";
                    string fallbackPath = $"res://{folder}/{name}.png";
                    Image image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
                    images.Add(image);

                    using Image? source = LoadSourceImage(path) ?? LoadSourceImage(fallbackPath);
                    if (source is not null && !source.IsEmpty())
                    {
                        if (source.IsCompressed())
                            source.Decompress();
                        source.Convert(Image.Format.Rgba8);
                        if (overlay)
                            PlaceOverlay(source, image, size);
                        else
                        {
                            if (source.GetWidth() != size || source.GetHeight() != size)
                                source.Resize(size, size, Image.Interpolation.Lanczos);
                            image.BlitRect(source, new Rect2I(0, 0, size, size), Vector2I.Zero);
                        }
                    }
                    image.FixAlphaEdges();
                    image.GenerateMipmaps();
                }
            }

            var texture = new Texture2DArray();
            Error error = texture.CreateFromImages(images);
            if (error != Error.Ok)
            {
                texture.Dispose();
                throw new InvalidOperationException($"Erro ao carregar a camada {folder}: {error}.");
            }
            return texture;
        }
        finally
        {
            foreach (Image image in images)
                image.Dispose();
        }
    }

    private static Image? LoadSourceImage(string resPath)
    {
        string globalPath = ProjectSettings.GlobalizePath(resPath);
        if (System.IO.File.Exists(globalPath))
        {
            var img = new Image();
            if (img.Load(globalPath) == Error.Ok && !img.IsEmpty())
                return img;
            img.Dispose();
        }
        if (ResourceLoader.Exists(resPath))
            return GD.Load<Texture2D>(resPath)?.GetImage();
        return null;
    }

    private static void PlaceOverlay(Image source, Image target, int size)
    {
        Rect2I bounds = source.GetUsedRect();
        if (!bounds.HasArea())
            return;

        using Image art = source.GetRegion(bounds);
        float scale = (size - 4f) / Math.Max(bounds.Size.X, bounds.Size.Y);
        var fitted = new Vector2I(Math.Max(1, (int)MathF.Round(bounds.Size.X * scale)),
            Math.Max(1, (int)MathF.Round(bounds.Size.Y * scale)));
        art.Resize(fitted.X, fitted.Y, Image.Interpolation.Lanczos);
        target.BlitRect(art, new Rect2I(Vector2I.Zero, fitted), (new Vector2I(size, size) - fitted) / 2);
    }
}
