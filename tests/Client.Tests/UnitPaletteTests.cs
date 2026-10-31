using Client.Core;

namespace Client.Tests;

// Visual pass Stage 1 (2026-10-27): a base no longer outshines its figure. Its top is a muted sandstone, darker and far
// less saturated than either seat colour, and only the rim carries the seat colour, itself muted.
public class UnitPaletteTests
{
    // The client's seat colours (BoardView.SeatColour, normal look) and the grey wash an activated model gets.
    private static readonly (float R, float G, float B)[] Seats = [(0.36f, 0.56f, 0.92f), (0.86f, 0.30f, 0.26f), (0.416f, 0.536f, 0.752f), (0.716f, 0.38f, 0.356f)];

    [Fact]
    public void TheBaseTopIsDarkerAndDullerThanEverySeatColourButLighterThanTheMud()
    {
        Assert.All(Seats, seat =>
        {
            Assert.True(UnitPalette.Value(UnitPalette.BaseTop) < UnitPalette.Value(seat) * 0.8f);
            Assert.True(UnitPalette.Saturation(UnitPalette.BaseTop) < 0.3f);
        });
        // The ground (BoardView.GroundColour, 0.25, 0.22, 0.17) and low terrain (0.36, 0.32, 0.25) are clearly darker.
        Assert.True(UnitPalette.Value(UnitPalette.BaseTop) > 0.36f + 0.08f);
    }

    [Fact]
    public void TheRimKeepsTheSeatsHueButIsDarkerAndLessSaturated()
    {
        foreach (var seat in Seats)
        {
            var rim = UnitPalette.Rim(seat);
            Assert.True(UnitPalette.Value(rim) < UnitPalette.Value(seat));
            Assert.True(UnitPalette.Saturation(rim) < UnitPalette.Saturation(seat));
            // The same channel is strongest, so blue stays blue and red stays red.
            static int Strongest((float R, float G, float B) c) => c.R >= c.G && c.R >= c.B ? 0 : c.G >= c.B ? 1 : 2;
            Assert.Equal(Strongest(seat), Strongest(rim));
        }
        // The two sides' rims stay easy to tell apart.
        var (blue, red) = (UnitPalette.Rim(Seats[0]), UnitPalette.Rim(Seats[1]));
        Assert.True(Math.Abs(blue.B - red.B) > 0.2f && Math.Abs(blue.R - red.R) > 0.2f);
    }

    [Fact]
    public void TheOutlinesAreDarkAndTheSelectedOneIsDistinct()
    {
        Assert.True(UnitPalette.Value((UnitPalette.Outline.R, UnitPalette.Outline.G, UnitPalette.Outline.B)) < 0.1f);
        Assert.True(UnitPalette.Value((UnitPalette.SelectedOutline.R, UnitPalette.SelectedOutline.G, UnitPalette.SelectedOutline.B)) > 0.4f);
        Assert.InRange(UnitPalette.OutlineWidth, 0.005f, 0.03f);
        Assert.InRange(UnitPalette.UnitLightEnergy, 0.1f, 1f);
    }
}
