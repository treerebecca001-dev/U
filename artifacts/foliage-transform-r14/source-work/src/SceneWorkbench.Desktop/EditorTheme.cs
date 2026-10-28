using Godot;

namespace SceneWorkbench.Desktop;

public static class EditorTheme
{
    public static readonly Color Canvas = new("202020"), Panel = new("292929"), Raised = new("353535"),
        Border = new("181818"), Text = new("e2e2e2"), Muted = new("b4b4b4"), Selection = new("3c6e9e"),
        Focus = new("83bdf7"), Warning = new("dcc58c"), Error = new("ef9a9a");

    public static Theme Create()
    {
        var theme = new Theme { DefaultFontSize = 14, DefaultFont = new SystemFont { FontNames = ["Microsoft JhengHei UI", "Segoe UI", "Noto Sans TC"] } };
        foreach (var type in new[] { "Label", "Button", "LineEdit", "TextEdit", "Tree", "PopupMenu", "OptionButton", "CheckBox", "TabContainer" })
        {
            theme.SetColor("font_color", type, Text);
            theme.SetColor("font_hover_color", type, Text);
            theme.SetColor("font_focus_color", type, Text);
            theme.SetColor("font_disabled_color", type, Muted);
        }
        theme.SetStylebox("panel", "PanelContainer", Box(Panel));
        foreach (var type in new[] { "Button", "OptionButton" })
        {
            theme.SetStylebox("normal", type, Box(Raised, 8, 5));
            theme.SetStylebox("hover", type, Box(Selection.Darkened(.35f), 8, 5));
            theme.SetStylebox("pressed", type, Box(Selection, 8, 5));
            theme.SetStylebox("disabled", type, Box(Panel, 8, 5));
            theme.SetStylebox("focus", type, Outline());
        }
        foreach (var type in new[] { "LineEdit", "TextEdit" })
        {
            theme.SetStylebox("normal", type, Box(Canvas, 6, 5));
            theme.SetStylebox("read_only", type, Box(Panel, 6, 5));
            theme.SetStylebox("focus", type, Outline());
            theme.SetColor("caret_color", type, Text);
            theme.SetColor("selection_color", type, Selection);
        }
        theme.SetStylebox("panel", "Tree", Box(Canvas));
        theme.SetStylebox("selected", "Tree", Box(Selection, 2, 2));
        theme.SetStylebox("selected_focus", "Tree", Box(Selection, 2, 2));
        theme.SetStylebox("cursor", "Tree", Outline());
        theme.SetStylebox("panel", "PopupMenu", Box(Panel, 6, 6));
        theme.SetStylebox("hover", "PopupMenu", Box(Selection, 4, 3));
        theme.SetStylebox("panel", "TabContainer", Box(Panel));
        theme.SetStylebox("tab_selected", "TabContainer", Box(Raised, 10, 6));
        theme.SetStylebox("tab_unselected", "TabContainer", Box(Panel, 10, 6));
        foreach (var type in new[] { "VScrollBar", "HScrollBar" })
        {
            theme.SetStylebox("scroll", type, Box(Canvas, 4, 4));
            theme.SetStylebox("grabber", type, Box(Raised.Lightened(.2f), 4, 4));
            theme.SetStylebox("grabber_highlight", type, Box(Muted, 4, 4));
            theme.SetStylebox("grabber_pressed", type, Box(Selection, 4, 4));
        }
        theme.SetConstant("v_separation", "VBoxContainer", 5);
        theme.SetConstant("h_separation", "HBoxContainer", 5);
        theme.SetConstant("v_separation", "Tree", 5);
        return theme;
    }

    public static StyleBoxFlat Box(Color color, float horizontal = 0, float vertical = 0) => new()
    {
        BgColor = color, BorderColor = Border, BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1,
        ContentMarginLeft = horizontal, ContentMarginRight = horizontal, ContentMarginTop = vertical, ContentMarginBottom = vertical
    };
    private static StyleBoxFlat Outline() => new() { BgColor = Colors.Transparent, DrawCenter = false, BorderColor = Focus, BorderWidthBottom = 2, BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2 };

    public static Button Button(string text, Action action, string tooltip = "")
    {
        var button = new Button { Text = text, TooltipText = tooltip, FocusMode = Control.FocusModeEnum.All };
        button.Pressed += action;
        return button;
    }
    public static Label Label(string text, Color? color = null)
    {
        var label = new Label { Text = text };
        if (color.HasValue) label.AddThemeColorOverride("font_color", color.Value);
        return label;
    }
    public static VBoxContainer PanelBody(string title)
    {
        var body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        var header = Label(title);
        header.AddThemeFontSizeOverride("font_size", 14);
        body.AddChild(header);
        return body;
    }
}
