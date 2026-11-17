using System;
using Godot;

namespace Mapmaker.GodotApp;

internal sealed class MapArtwork : IDisposable
{
    private readonly Texture2DArray _ground = Load("tiles", 512, false,
        "beach", "grasslands", "temperate_forest", "rainforest", "savanna", "steppe", "desert",
        "taiga", "tundra", "alpine", "ocean", "lake", "wetland");
    private readonly Texture2DArray _relief = Load("relief", 256, true,
        "rolling_hills", "hills", "mountains", "peaks", "snowy_peaks");
    private readonly Texture2DArray _vegetation = Load("vegetation", 128, true,
        "temperate_trees", "rainforest_canopy", "taiga_pines", "savanna_trees", "desert_scrub", "wetland_reeds");

    public void Bind(ShaderMaterial material)
    {
        material.SetShaderParameter("ground_art", _ground);
        material.SetShaderParameter("relief_art", _relief);
        material.SetShaderParameter("vegetation_art", _vegetation);
    }

    public void Dispose()
    {
        _ground.Dispose();
        _relief.Dispose();
        _vegetation.Dispose();
    }

    private static Texture2DArray Load(string folder, int size, bool overlay, params string[] names)
    {
        var images = new Godot.Collections.Array<Image>();
        try
        {
            foreach (string name in names)
            {
                string path = $"res://{folder}/{name}.png";
                Image image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
                images.Add(image);
                using Image? source = ResourceLoader.Exists(path)
                    ? GD.Load<Texture2D>(path).GetImage() : Image.LoadFromFile(path);
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
