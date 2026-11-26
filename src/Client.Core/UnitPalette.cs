namespace Client.Core;

// Visual pass Stage 1 (2026-10-27, Jason chose a muted base with a seat-colour rim, a light just for the units and a
// thin dark outline): the colours a model's base and outline are drawn with. Decoration only: nothing here reads or
// changes a rule. Colours are (red, green, blue) from 0 to 1.
public static class UnitPalette
{
    // The base's top: pale weathered sandstone, lit like the ground. Clearly lighter than the mud and the grey deployment
    // strips (runs 317 and 318's readability check found darker stones too close to them from the usual camera) yet
    // dull, so the dark figure on it stands out and the base does not shout.
    public static readonly (float R, float G, float B) BaseTop = (0.56f, 0.51f, 0.42f);

    // The thin rim around the base's edge: the seat colour (washed grey once activated), muted toward the mud so it
    // reads as the side's colour without outshining the figure.
    public const float RimMute = 0.15f;

    // How far inside the base's edge the top starts, in inches: the coloured rim is the band from here out to 0.03" from
    // the edge (the dark lip). Wide enough to read at the usual camera distance.
    public const float RimWidth = 0.17f;

    public static (float R, float G, float B) Rim((float R, float G, float B) seat)
    {
        static float Mix(float a, float b, float t) => a + (b - a) * t;
        return (Mix(seat.R, BaseTop.R, RimMute) * 0.92f, Mix(seat.G, BaseTop.G, RimMute) * 0.92f, Mix(seat.B, BaseTop.B, RimMute) * 0.92f);
    }

    // The thin outline around each figure, and the selected figure's: a dark edge, warmer and lighter when selected.
    public static readonly (float R, float G, float B, float A) Outline = (0.05f, 0.045f, 0.04f, 1f);

    public static readonly (float R, float G, float B, float A) SelectedOutline = (0.62f, 0.48f, 0.16f, 1f);

    // How far the outline stands out from the figure's surface, in the figure's own units (it is 1.2 tall).
    public const float OutlineWidth = 0.012f;

    // The light that falls on the units only (from the camera's side, so the side facing the player is lit): strength
    // and colour. The ground and terrain keep the sun and ambient light alone.
    public const float UnitLightEnergy = 0.8f;

    public static readonly (float R, float G, float B) UnitLightColour = (0.96f, 0.93f, 0.86f);

    // Brightness (0 to 1) and saturation (0 to 1) of a colour, for the tests.
    public static float Value((float R, float G, float B) c) => Math.Max(c.R, Math.Max(c.G, c.B));

    public static float Saturation((float R, float G, float B) c)
    {
        var max = Value(c);
        var min = Math.Min(c.R, Math.Min(c.G, c.B));
        return max <= 0 ? 0 : (max - min) / max;
    }
}
