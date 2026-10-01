using Godot;

namespace BoardEmpire.Core;

public enum ButtonKind
{
    Primary,
    Secondary,
    Ghost,
    Danger,
}

/// <summary>Design tokens. Every colour in the interface comes from here.</summary>
public static class Tokens
{
    public static Color Bg, BgDeep, Panel, PanelHi, Line, Text, Muted, Accent, AccentInk, Good, Bad, Info;

    public static readonly Color[] Players =
    {
        new("#ef5b5b"), new("#4ea3ff"), new("#3ecf8e"), new("#f2b33d"),
        new("#b47bff"), new("#ff8a3d"), new("#3fd6d0"), new("#ff7ac2"),
    };

    public static readonly string[] TokenNames = { "Rocket", "Top Hat", "Bolt", "Crown", "Gem", "Compass", "Anchor", "Star" };

    public static void Apply(bool highContrast)
    {
        if (highContrast)
        {
            Bg = new Color("#000000");
            BgDeep = new Color("#000000");
            Panel = new Color("#101010");
            PanelHi = new Color("#242424");
            Line = new Color("#ffffff");
            Text = new Color("#ffffff");
            Muted = new Color("#d6d6d6");
            Accent = new Color("#ffe14a");
            AccentInk = new Color("#000000");
            Good = new Color("#5dff9e");
            Bad = new Color("#ff7a7a");
            Info = new Color("#8fe3ff");
        }
        else
        {
            Bg = new Color("#0f1420");
            BgDeep = new Color("#0a0e17");
            Panel = new Color("#1a2233");
            PanelHi = new Color("#243049");
            Line = new Color("#2f3c59");
            Text = new Color("#e8ecf4");
            Muted = new Color("#8b96ad");
            Accent = new Color("#f2b33d");
            AccentInk = new Color("#1a1300");
            Good = new Color("#3ecf8e");
            Bad = new Color("#ef5b5b");
            Info = new Color("#69c8ec");
        }
    }

    public static Color Player(int id) => id >= 0 ? Players[id % Players.Length] : Muted;
}

/// <summary>Programmatic theme and small factories so screens read like layout descriptions.</summary>
public static class Ui
{
    public static Font Mono = null!, MonoBold = null!, Body = null!, BodyBold = null!;
    public static float Scale = 1f;
    public static Action? ClickSound;

    public static int Px(int size) => Mathf.RoundToInt(size * Scale);

    public static void LoadFonts()
    {
        var mono = GD.Load<FontFile>("res://assets/fonts/SpaceMono-Regular.ttf");
        var monoBold = GD.Load<FontFile>("res://assets/fonts/SpaceMono-Bold.ttf");
        var outfit = GD.Load<FontFile>("res://assets/fonts/Outfit-Variable.ttf");
        var devanagari = GD.Load<FontFile>("res://assets/fonts/NotoSansDevanagari-Variable.ttf");
        var system = new SystemFont { FontNames = new[] { "Noto Color Emoji", "Noto Sans", "sans-serif" } };

        Font Weight(FontFile font, int weight)
        {
            int tag = (int)TextServerManager.GetPrimaryInterface().NameToTag("wght");
            return new FontVariation
            {
                BaseFont = font,
                VariationOpentype = new Godot.Collections.Dictionary { { tag, weight } },
                Fallbacks = new Godot.Collections.Array<Font> { devanagari, system },
            };
        }

        Body = Weight(outfit, 420);
        BodyBold = Weight(outfit, 650);
        mono.Fallbacks = new Godot.Collections.Array<Font> { outfit, devanagari, system };
        monoBold.Fallbacks = new Godot.Collections.Array<Font> { outfit, devanagari, system };
        Mono = mono;
        MonoBold = monoBold;
    }

    public static StyleBoxFlat Box(Color bg, int radius = 12, Color? border = null, int borderWidth = 0, int padX = 16, int padY = 10)
    {
        var box = new StyleBoxFlat
        {
            BgColor = bg,
            CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius,
            CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
            ContentMarginLeft = padX, ContentMarginRight = padX, ContentMarginTop = padY, ContentMarginBottom = padY,
            AntiAliasing = true,
        };
        if (border is { } b && borderWidth > 0)
        {
            box.BorderColor = b;
            box.BorderWidthLeft = box.BorderWidthRight = box.BorderWidthTop = box.BorderWidthBottom = borderWidth;
        }
        return box;
    }

    public static Theme BuildTheme(Settings settings)
    {
        Tokens.Apply(settings.HighContrast);
        Scale = settings.TextScale;
        int border = settings.HighContrast ? 2 : 1;
        var theme = new Theme { DefaultFont = Body, DefaultFontSize = Px(19) };

        theme.SetColor("font_color", "Label", Tokens.Text);
        theme.SetStylebox("panel", "PanelContainer", Box(Tokens.Panel, 16, Tokens.Line, border, 18, 16));
        theme.SetStylebox("panel", "Panel", Box(Tokens.Panel, 16, Tokens.Line, border));
        theme.SetStylebox("panel", "PopupPanel", Box(Tokens.Panel, 16, Tokens.Line, border, 18, 16));

        foreach (string type in new[] { "Button", "OptionButton", "MenuButton" })
        {
            theme.SetFont("font", type, MonoBold);
            theme.SetFontSize("font_size", type, Px(16));
            theme.SetColor("font_color", type, Tokens.Text);
            theme.SetColor("font_hover_color", type, Tokens.Text);
            theme.SetColor("font_pressed_color", type, Tokens.Text);
            theme.SetColor("font_focus_color", type, Tokens.Text);
            theme.SetColor("font_disabled_color", type, Tokens.Muted with { A = 0.6f });
            theme.SetStylebox("normal", type, Box(Tokens.PanelHi, 12, Tokens.Line, border, 20, 13));
            theme.SetStylebox("hover", type, Box(Tokens.PanelHi.Lightened(0.08f), 12, Tokens.Muted, border, 20, 13));
            theme.SetStylebox("pressed", type, Box(Tokens.PanelHi.Darkened(0.15f), 12, Tokens.Accent, 2, 20, 13));
            theme.SetStylebox("disabled", type, Box(Tokens.Panel, 12, Tokens.Line with { A = 0.5f }, border, 20, 13));
            theme.SetStylebox("focus", type, Box(Colors.Transparent, 12, Tokens.Accent, 2, 20, 13));
        }

        var field = Box(Tokens.BgDeep, 10, Tokens.Line, border, 14, 10);
        theme.SetStylebox("normal", "LineEdit", field);
        theme.SetStylebox("focus", "LineEdit", Box(Tokens.BgDeep, 10, Tokens.Accent, 2, 14, 10));
        theme.SetStylebox("read_only", "LineEdit", field);
        theme.SetColor("font_color", "LineEdit", Tokens.Text);
        theme.SetColor("font_placeholder_color", "LineEdit", Tokens.Muted);
        theme.SetColor("caret_color", "LineEdit", Tokens.Accent);
        theme.SetFont("font", "LineEdit", Mono);

        theme.SetStylebox("panel", "PopupMenu", Box(Tokens.PanelHi, 10, Tokens.Line, border, 8, 8));
        theme.SetStylebox("hover", "PopupMenu", Box(Tokens.Accent with { A = 0.25f }, 6));
        theme.SetColor("font_color", "PopupMenu", Tokens.Text);
        theme.SetColor("font_hover_color", "PopupMenu", Tokens.Text);
        theme.SetFont("font", "PopupMenu", Body);
        theme.SetFontSize("font_size", "PopupMenu", Px(17));
        theme.SetConstant("v_separation", "PopupMenu", 12);

        theme.SetColor("font_color", "CheckButton", Tokens.Text);
        theme.SetColor("font_color", "CheckBox", Tokens.Text);
        theme.SetFont("font", "CheckButton", Body);
        theme.SetFont("font", "CheckBox", Body);

        var track = Box(Tokens.BgDeep, 6, Tokens.Line, border, 0, 4);
        theme.SetStylebox("slider", "HSlider", track);
        theme.SetStylebox("grabber_area", "HSlider", Box(Tokens.Accent, 6, null, 0, 0, 4));
        theme.SetStylebox("grabber_area_highlight", "HSlider", Box(Tokens.Accent, 6, null, 0, 0, 4));
        theme.SetStylebox("background", "ProgressBar", track);
        theme.SetStylebox("fill", "ProgressBar", Box(Tokens.Accent, 6, null, 0, 0, 4));

        theme.SetStylebox("panel", "ScrollContainer", new StyleBoxEmpty());
        theme.SetStylebox("panel", "TooltipPanel", Box(Tokens.PanelHi, 8, Tokens.Line, border, 10, 6));
        theme.SetColor("font_color", "TooltipLabel", Tokens.Text);
        theme.SetConstant("separation", "VBoxContainer", 10);
        theme.SetConstant("separation", "HBoxContainer", 10);
        theme.SetConstant("h_separation", "GridContainer", 10);
        theme.SetConstant("v_separation", "GridContainer", 10);
        return theme;
    }

    // ------------------------------------------------------------------ factories

    public static Label Label(string text, int size = 18, Color? color = null, bool mono = false, bool bold = false)
    {
        var label = new Label { Text = text };
        label.AddThemeFontOverride("font", mono ? (bold ? MonoBold : Mono) : (bold ? BodyBold : Body));
        label.AddThemeFontSizeOverride("font_size", Px(size));
        label.AddThemeColorOverride("font_color", color ?? Tokens.Text);
        return label;
    }

    /// <summary>Small uppercase caption in the monospace face, used for every field label.</summary>
    public static Label Caption(string text, Color? color = null)
    {
        var label = Label(text.ToUpperInvariant(), 13, color ?? Tokens.Muted, mono: true);
        label.AddThemeConstantOverride("outline_size", 0);
        return label;
    }

    public static Label Wrapped(string text, int size = 17, Color? color = null)
    {
        var label = Label(text, size, color);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return label;
    }

    public static Button Button(string text, Action onPress, ButtonKind kind = ButtonKind.Secondary, int minWidth = 0)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(Px(minWidth), Px(52)), FocusMode = Control.FocusModeEnum.None };
        Style(button, kind);
        button.Pressed += () =>
        {
            ClickSound?.Invoke();
            onPress();
        };
        return button;
    }

    public static void Style(Button button, ButtonKind kind)
    {
        Color bg, ink, edge;
        switch (kind)
        {
            case ButtonKind.Primary:
                bg = Tokens.Accent;
                ink = Tokens.AccentInk;
                edge = Tokens.Accent;
                break;
            case ButtonKind.Danger:
                bg = Tokens.Bad.Darkened(0.35f);
                ink = Tokens.Text;
                edge = Tokens.Bad;
                break;
            case ButtonKind.Ghost:
                bg = Colors.Transparent;
                ink = Tokens.Muted;
                edge = Colors.Transparent;
                break;
            default:
                return;
        }
        button.AddThemeStyleboxOverride("normal", Box(bg, 12, edge, 1, 18, 12));
        button.AddThemeStyleboxOverride("hover", Box(kind == ButtonKind.Ghost ? Tokens.PanelHi : bg.Lightened(0.1f), 12, edge, 1, 18, 12));
        button.AddThemeStyleboxOverride("pressed", Box(kind == ButtonKind.Ghost ? Tokens.PanelHi : bg.Darkened(0.12f), 12, edge, 1, 18, 12));
        foreach (string c in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
            button.AddThemeColorOverride(c, ink);
    }

    public static PanelContainer Panel(Control? child = null, Color? bg = null, int pad = 16, int radius = 16, Color? border = null)
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", Box(bg ?? Tokens.Panel, radius, border ?? Tokens.Line, 1, pad, pad));
        if (child != null) panel.AddChild(child);
        return panel;
    }

    public static VBoxContainer VBox(int separation = 10, params Control[] children)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", separation);
        foreach (var c in children) box.AddChild(c);
        return box;
    }

    public static HBoxContainer HBox(int separation = 10, params Control[] children)
    {
        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", separation);
        foreach (var c in children) box.AddChild(c);
        return box;
    }

    public static Control Spacer(bool horizontal = true) => new Control
    {
        SizeFlagsHorizontal = horizontal ? Control.SizeFlags.ExpandFill : Control.SizeFlags.Fill,
        SizeFlagsVertical = horizontal ? Control.SizeFlags.Fill : Control.SizeFlags.ExpandFill,
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    public static Control Gap(int width = 0, int height = 0) => new Control
    {
        CustomMinimumSize = new Vector2(width, height), MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    public static MarginContainer Margin(Control child, int left = 24, int top = 24, int right = 24, int bottom = 24)
    {
        var m = new MarginContainer();
        m.AddThemeConstantOverride("margin_left", left);
        m.AddThemeConstantOverride("margin_top", top);
        m.AddThemeConstantOverride("margin_right", right);
        m.AddThemeConstantOverride("margin_bottom", bottom);
        m.AddChild(child);
        return m;
    }

    public static T Expand<T>(this T control, bool horizontal = true, bool vertical = false) where T : Control
    {
        if (horizontal) control.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        if (vertical) control.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        return control;
    }

    public static T Full<T>(this T control) where T : Control
    {
        control.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        return control;
    }

    public static T MinSize<T>(this T control, int width, int height) where T : Control
    {
        control.CustomMinimumSize = new Vector2(width, height);
        return control;
    }

    public static ScrollContainer Scroll(Control child)
    {
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        scroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        child.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(child);
        return scroll;
    }

    public static OptionButton Options(IEnumerable<string> items, int selected, Action<int> onSelect)
    {
        var option = new OptionButton { CustomMinimumSize = new Vector2(Px(160), Px(48)), FocusMode = Control.FocusModeEnum.None };
        foreach (string item in items) option.AddItem(item);
        option.Selected = selected;
        option.ItemSelected += index => onSelect((int)index);
        return option;
    }

    public static CheckButton Toggle(string text, bool value, Action<bool> onToggle)
    {
        var check = new CheckButton { Text = text, ButtonPressed = value, FocusMode = Control.FocusModeEnum.None };
        check.AddThemeFontSizeOverride("font_size", Px(17));
        check.Toggled += on => onToggle(on);
        return check;
    }

    public static HSlider Slider(float value, Action<float> onChange, float min = 0, float max = 1, float step = 0.05f)
    {
        var slider = new HSlider
        {
            MinValue = min, MaxValue = max, Step = step, Value = value,
            CustomMinimumSize = new Vector2(Px(220), Px(36)), FocusMode = Control.FocusModeEnum.None,
        };
        slider.ValueChanged += v => onChange((float)v);
        return slider;
    }

    public static LineEdit Input(string text, string placeholder, Action<string>? onChange = null, int maxLength = 24)
    {
        var edit = new LineEdit
        {
            Text = text, PlaceholderText = placeholder, MaxLength = maxLength,
            CustomMinimumSize = new Vector2(Px(220), Px(48)),
        };
        edit.AddThemeFontSizeOverride("font_size", Px(17));
        if (onChange != null) edit.TextChanged += t => onChange(t);
        return edit;
    }

    /// <summary>Coloured chip used for player identity and status tags.</summary>
    public static PanelContainer Chip(string text, Color color, bool filled = false)
    {
        var label = Label(text, 13, filled ? Tokens.AccentInk : color, mono: true, bold: true);
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", Box(filled ? color : color with { A = 0.14f }, 8, color, 1, 10, 4));
        panel.AddChild(label);
        return panel;
    }

    public static string Money(string currency, int amount) => $"{currency}{amount:N0}";
}
