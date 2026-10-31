using Client.Core;
using Godot;

// Main menu stage M3 (2026-10-28): the theme of the screens before a match, from MenuPalette's colours. Plain and grim:
// weathered brown panels with thin frames on charcoal, bone text, rust red for the main action and for Exit. Every state
// differs by more than colour: pointed at is lighter with a bone frame, pressed is darker and its words drop 1 px,
// disabled is flat and frameless, and keyboard focus adds a 2 px frame with a thick bar down the left edge. No pictures,
// no animation. The buttons keep Godot's own content margins, so sizes match the rest of the game.
public static class MenuLook
{
    public static Color Color((float R, float G, float B) c, float a = 1f) => new(c.R, c.G, c.B, a);

    public static Theme Theme(int fontSize)
    {
        var theme = new Theme { DefaultFontSize = fontSize };
        var bone = Color(MenuPalette.Bone);
        foreach (var type in new[] { "Label", "Button", "LineEdit", "OptionButton", "CheckButton", "SpinBox" })
        {
            theme.SetColor("font_color", type, bone);
        }
        foreach (var type in new[] { "Button", "OptionButton", "CheckButton" })
        {
            foreach (var state in new[] { "font_hover_color", "font_pressed_color", "font_focus_color", "font_hover_pressed_color" })
            {
                theme.SetColor(state, type, bone);
            }
            theme.SetColor("font_disabled_color", type, Color(MenuPalette.DisabledText));
        }

        // Words on buttons: the plain font, a little spaced, like stencilled lettering.
        var spaced = new FontVariation { BaseFont = ThemeDB.FallbackFont };
        spaced.SetSpacing(TextServer.SpacingType.Glyph, 1);
        theme.SetFont("font", "Button", spaced);

        foreach (var type in new[] { "Button", "OptionButton" })
        {
            Boxes(theme, type, MenuPalette.Button, MenuPalette.ButtonHover, MenuPalette.ButtonPressed, MenuPalette.Frame);
        }
        theme.SetStylebox("focus", "CheckButton", Focus());

        // The main action: rust red.
        theme.SetTypeVariation("MenuPrimary", "Button");
        Boxes(theme, "MenuPrimary", MenuPalette.Rust, MenuPalette.RustHover, MenuPalette.RustPressed, MenuPalette.RustFrame);

        // Exit: rust words on a plain button.
        theme.SetTypeVariation("MenuDanger", "Button");
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color", "font_hover_pressed_color" })
        {
            theme.SetColor(state, "MenuDanger", Color(MenuPalette.DangerText));
        }

        // The roster placeholder: it looks disabled (flat, frameless, dim words) yet can be pressed for its note.
        theme.SetTypeVariation("MenuMuted", "Button");
        var muted = Box(MenuPalette.ButtonDisabled, null);
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed" })
        {
            theme.SetStylebox(state, "MenuMuted", muted);
        }
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color", "font_hover_pressed_color" })
        {
            theme.SetColor(state, "MenuMuted", Color(MenuPalette.DisabledText));
        }

        // Text fields: a dark well with a thin frame.
        var well = Box(MenuPalette.ButtonPressed, MenuPalette.Frame, "LineEdit");
        theme.SetStylebox("normal", "LineEdit", well);
        theme.SetStylebox("read_only", "LineEdit", well);
        theme.SetStylebox("focus", "LineEdit", Focus());
        theme.SetColor("font_placeholder_color", "LineEdit", Color(MenuPalette.Muted));
        theme.SetColor("caret_color", "LineEdit", bone);

        // The panel, and thin dividers.
        theme.SetTypeVariation("MenuPanel", "PanelContainer");
        var panel = new StyleBoxFlat { BgColor = Color(MenuPalette.Panel), BorderColor = Color(MenuPalette.Frame) };
        panel.SetBorderWidthAll(MenuPalette.Frame1);
        panel.SetCornerRadiusAll(2);
        panel.SetContentMarginAll(22);
        theme.SetStylebox("panel", "MenuPanel", panel);
        theme.SetStylebox("separator", "HSeparator", new StyleBoxLine { Color = Color(MenuPalette.Divider), Thickness = 1 });
        theme.SetConstant("separation", "HSeparator", 14);
        return theme;
    }

    // Normal, pointed at, pressed, pressed while pointed at, disabled and focus boxes for one button type.
    private static void Boxes(Theme theme, string type, (float, float, float) normal, (float, float, float) hover, (float, float, float) pressed, (float, float, float) frame)
    {
        theme.SetStylebox("normal", type, Box(normal, frame));
        theme.SetStylebox("hover", type, Box(hover, MenuPalette.Bone));
        var down = Box(pressed, frame);
        // Pressed: the words sink 1 px, so it reads without colour.
        down.ContentMarginTop += 1;
        down.ContentMarginBottom -= 1;
        theme.SetStylebox("pressed", type, down);
        theme.SetStylebox("hover_pressed", type, down);
        theme.SetStylebox("disabled", type, Box(MenuPalette.ButtonDisabled, null));
        theme.SetStylebox("focus", type, Focus());
    }

    // Godot's own box for that state of a Button (or another type), with these colours; its margins stay.
    private static StyleBoxFlat Box((float R, float G, float B) bg, (float R, float G, float B)? frame, string type = "Button")
    {
        var box = ThemeDB.GetDefaultTheme().GetStylebox("normal", type).Duplicate() as StyleBoxFlat ?? new StyleBoxFlat();
        box.BgColor = Color(bg);
        box.SetCornerRadiusAll(2);
        if (frame is { } f)
        {
            box.BorderColor = Color(f);
            box.SetBorderWidthAll(MenuPalette.Frame1);
        }
        else
        {
            box.SetBorderWidthAll(0);
        }
        return box;
    }

    // Keyboard focus, drawn over the button: a 2 px bone frame and a 6 px bar down the left edge.
    public static StyleBoxFlat Focus()
    {
        var box = new StyleBoxFlat { DrawCenter = false, BorderColor = Color(MenuPalette.Focus) };
        box.SetBorderWidthAll(MenuPalette.FocusFrame);
        box.BorderWidthLeft = MenuPalette.FocusBar;
        box.SetCornerRadiusAll(2);
        box.SetExpandMarginAll(2);
        return box;
    }
}
