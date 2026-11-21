using Client.Core;

namespace Client.Tests;

// Main menu stage M3: every text colour of the menu screens is readable on what it is drawn on (WCAG 2 contrast).
public class MenuPaletteTests
{
    [Fact]
    public void EveryTextColourIsReadableOnItsBackground()
    {
        var low = MenuPalette.Pairs
            .Select(p => (p.What, Ratio: MenuPalette.Contrast(p.Text, p.On), p.Minimum))
            .Where(p => p.Ratio < p.Minimum)
            .Select(p => $"{p.What}: {p.Ratio:0.00} < {p.Minimum}")
            .ToList();
        Assert.Empty(low);
    }

    [Fact]
    public void ContrastIsWcags()
    {
        Assert.Equal(21, MenuPalette.Contrast((1, 1, 1), (0, 0, 0)), 3);
        Assert.Equal(1, MenuPalette.Contrast((0.4f, 0.4f, 0.4f), (0.4f, 0.4f, 0.4f)), 3);
        // #777777 on white is the textbook 4.48.
        Assert.Equal(4.48, MenuPalette.Contrast((0x77 / 255f, 0x77 / 255f, 0x77 / 255f), (1, 1, 1)), 2);
    }

    [Fact]
    public void FocusIsAShapeNotOnlyAColour()
    {
        Assert.True(MenuPalette.FocusFrame > MenuPalette.Frame1);
        Assert.True(MenuPalette.FocusBar >= 4);
    }

    [Fact]
    public void TheMainActionAndExitStandApartFromPlainButtons()
    {
        Assert.True(MenuPalette.Contrast(MenuPalette.Rust, MenuPalette.Button) > 1.3);
        Assert.True(MenuPalette.Contrast(MenuPalette.DangerText, MenuPalette.Bone) > 1.3);
    }
}
