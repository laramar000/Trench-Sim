namespace Client.Core;

// Main menu stage M3 (2026-10-28): the colours of the screens before a match (main menu, Play, Settings, Credits). Grim
// and plain: a charcoal ground, weathered brown panels, bone text and a rust-red accent for the main action (Play, Start
// hot-seat, Host, Join) and for Exit. Colours are (red, green, blue) from 0 to 1, in the sRGB the UI draws in.
// MenuPaletteTests holds every text colour to a readable contrast on the colours it is drawn on.
public static class MenuPalette
{
    // The ground behind the menu's panel: the UI's warm charcoal (UiLook.Background), so the match screen matches.
    public static readonly (float R, float G, float B) Ground = (0.105f, 0.098f, 0.088f);

    // The panel the menu's column sits on, and its thin frame.
    public static readonly (float R, float G, float B) Panel = (0.165f, 0.135f, 0.105f);
    public static readonly (float R, float G, float B) Frame = (0.38f, 0.31f, 0.23f);

    // Dividers under the title and between sections.
    public static readonly (float R, float G, float B) Divider = (0.38f, 0.31f, 0.23f);

    // Text.
    public static readonly (float R, float G, float B) Bone = (0.90f, 0.86f, 0.76f);
    public static readonly (float R, float G, float B) Muted = (0.72f, 0.68f, 0.60f);

    // A plain button: darker brown, lighter when pointed at, darkest when pressed, and nearly flat when disabled.
    public static readonly (float R, float G, float B) Button = (0.215f, 0.175f, 0.135f);
    public static readonly (float R, float G, float B) ButtonHover = (0.29f, 0.235f, 0.18f);
    public static readonly (float R, float G, float B) ButtonPressed = (0.12f, 0.10f, 0.08f);
    public static readonly (float R, float G, float B) ButtonDisabled = (0.175f, 0.145f, 0.115f);
    public static readonly (float R, float G, float B) DisabledText = (0.60f, 0.56f, 0.50f);

    // The main action: rust red, with bone text.
    public static readonly (float R, float G, float B) Rust = (0.47f, 0.15f, 0.10f);
    public static readonly (float R, float G, float B) RustHover = (0.56f, 0.19f, 0.13f);
    public static readonly (float R, float G, float B) RustPressed = (0.33f, 0.10f, 0.07f);
    public static readonly (float R, float G, float B) RustFrame = (0.68f, 0.30f, 0.20f);

    // Danger (Exit): rust-coloured words on a plain button.
    public static readonly (float R, float G, float B) DangerText = (0.97f, 0.60f, 0.50f);

    // Keyboard focus is a shape, not only a colour: a 2 px bone frame and a thick bar down the left edge.
    public static readonly (float R, float G, float B) Focus = Bone;
    public const int FocusFrame = 2;
    public const int FocusBar = 6;

    // A frame 1 px wide on every other state.
    public const int Frame1 = 1;

    // The smallest contrast (WCAG 2) of normal text on what it is drawn on, and of disabled text.
    public const double TextContrast = 4.5;
    public const double DisabledContrast = 3.0;

    // The pairs MenuPaletteTests checks: each text colour on each colour it is drawn on.
    public static IReadOnlyList<(string What, (float R, float G, float B) Text, (float R, float G, float B) On, double Minimum)> Pairs { get; } =
    [
        ("text on the ground", Bone, Ground, TextContrast),
        ("text on the panel", Bone, Panel, TextContrast),
        ("secondary text on the panel", Muted, Panel, TextContrast),
        ("button", Bone, Button, TextContrast),
        ("button pointed at", Bone, ButtonHover, TextContrast),
        ("button pressed", Bone, ButtonPressed, TextContrast),
        ("disabled button", DisabledText, ButtonDisabled, DisabledContrast),
        ("main action", Bone, Rust, TextContrast),
        ("main action pointed at", Bone, RustHover, TextContrast),
        ("main action pressed", Bone, RustPressed, TextContrast),
        ("Exit", DangerText, Button, TextContrast),
        ("Exit pointed at", DangerText, ButtonHover, TextContrast),
        ("focus frame on the panel", Focus, Panel, 3.0),
    ];

    // WCAG 2 contrast ratio of two sRGB colours (1 to 21).
    public static double Contrast((float R, float G, float B) a, (float R, float G, float B) b)
    {
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    public static double Luminance((float R, float G, float B) c)
    {
        static double Linear(float v) => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        return 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
    }
}
