using Godot;

namespace Mapmaker.GodotApp;

internal static class MenuTheme
{
    private static Texture2D? _wood;
    internal static Font TitleFont => GD.Load<FontFile>("res://assets/fonts/faemoot4o.ttf");

    internal static StyleBoxTexture Wood(float horizontal = 16, float vertical = 8) => new()
    {
        Texture = _wood ??= GD.Load<Texture2D>("res://assets/ui/wood-menu.png"),
        TextureMarginLeft = 8, TextureMarginRight = 8, TextureMarginTop = 8, TextureMarginBottom = 8,
        ContentMarginLeft = horizontal, ContentMarginRight = horizontal,
        ContentMarginTop = vertical, ContentMarginBottom = vertical,
        ModulateColor = new Color(0.86f, 0.86f, 0.86f)
    };

    internal static Theme Create()
    {
        var theme = new Theme
        {
            DefaultFont = GD.Load<FontFile>("res://assets/fonts/faemoot4s.ttf"), DefaultFontSize = 18
        };
        foreach (string type in new[] { "PanelContainer", "PopupMenu", "AcceptDialog" })
            theme.SetStylebox("panel", type, Wood());
        theme.SetStylebox("embedded_border", "Window", Wood(12, 12));
        theme.SetFont("title_font", "Window", GD.Load<FontFile>("res://assets/fonts/faemoot4o.ttf"));
        theme.SetColor("font_color", "Label", new Color("#ebe2cc"));
        theme.SetColor("font_color", "Button", new Color("#ebe2cc"));
        theme.SetColor("font_hover_color", "Button", new Color("#f3d592"));
        return theme;
    }

    internal static void Apply(Node node, Theme theme)
    {
        if (node is Control control) control.Theme = theme;
        if (node is Window window) window.Theme = theme;
        foreach (Node child in node.GetChildren()) Apply(child, theme);
    }
}
