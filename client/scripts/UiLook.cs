using System;
using Godot;

// Graphics stage G6: the UI's colours and heading font. Iron frames and parchment text on warm charcoal, and an engraved
// blackletter (Pirata One, SIL OFL 1.1) for the section titles (COMBAT LOG, RESULT OF THE MATCH, ...), in gold. Body
// text, and every line that names a model, a seat or a score (the unit card, the result's headline), keeps Godot's plain
// font, so ids and seat letters stay easy to read. Only colours and the heading face change: every box
// keeps its margins and every button keeps its default margins, so the layout (U2, U3, U6) stays where it was; only the
// section titles are a few pixels taller. High contrast keeps its black and white look.
public static class UiLook
{
    public const string HeadingFontPath = Visuals.AssetsRoot + "ui/PirataOne-Regular.ttf";

    // Warm charcoal behind everything.
    public static readonly Color Background = new(0.105f, 0.098f, 0.088f);

    // Body text: faded parchment.
    public static readonly Color Text = new(0.91f, 0.88f, 0.8f);

    // Secondary text (the HUD's status line).
    public static readonly Color Muted = new(0.8f, 0.77f, 0.7f);

    // Boxes over the battlefield: soot, with a weathered iron frame.
    public static readonly Color Panel = new(0.11f, 0.1f, 0.09f, 0.93f);
    public static readonly Color Iron = new(0.44f, 0.42f, 0.39f);

    // Candlelit gold for the frames that need attention (the choice card, the result) and the section titles.
    public static readonly Color Gold = new(0.88f, 0.72f, 0.36f);

    private static Font? _heading;
    private static bool _looked;

    // The heading font, or null when its file is missing (the plain font is used then).
    public static Font? Heading
    {
        get
        {
            if (!_looked)
            {
                _looked = true;
                var path = ProjectSettings.GlobalizePath(HeadingFontPath);
                if (System.IO.File.Exists(path))
                {
                    var font = new FontFile();
                    if (font.LoadDynamicFont(path) == Error.Ok)
                    {
                        // Symbols the face lacks (▸ ▾) come from the plain font.
                        font.Fallbacks = [ThemeDB.FallbackFont];
                        _heading = font;
                    }
                }
            }
            return _heading;
        }
    }

    // The heading font's size for text shown at this size in the plain font: larger, because the blackletter's letters
    // are small for its size (its lines are then a few pixels taller than the plain font's).
    public const float HeadingScale = 1.3f;

    public static int HeadingSize(int size) => Heading is null || size <= 0 ? size : (int)Math.Round(size * HeadingScale);

    // Shows a label in the heading font in place of the plain font at this size (or the plain font in high
    // contrast, or when the font file is missing). A size of 0 means the theme's default size.
    public static void MakeHeading(Label label, int size, bool highContrast)
    {
        if (Heading is { } heading && !highContrast)
        {
            label.AddThemeFontOverride("font", heading);
            label.AddThemeFontSizeOverride("font_size", HeadingSize(size));
        }
        else
        {
            label.RemoveThemeFontOverride("font");
            label.AddThemeFontSizeOverride("font_size", size);
        }
    }

    // A copy of Godot's default button box of this state with new colours; its margins (so every button's size) stay.
    public static StyleBox Button(string state, Color bg, Color border)
    {
        var box = (StyleBox)ThemeDB.GetDefaultTheme().GetStylebox(state, "Button").Duplicate();
        if (box is StyleBoxFlat flat)
        {
            flat.BgColor = bg;
            flat.BorderColor = border;
        }
        return box;
    }
}
