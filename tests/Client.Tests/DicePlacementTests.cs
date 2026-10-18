using Client.Core;

namespace Client.Tests;

// Visible dice rolls (QoL stage F3): a box of dice goes beside its model only where it stays inside the battlefield and
// covers nothing to avoid; otherwise (or when the model is off screen) it goes in the tray.
public class DicePlacementTests
{
    private static readonly ScreenBox Field = new(0, 0, 1000, 700);

    [Fact]
    public void ItGoesRightOfTheModelWhenThatIsFree()
    {
        var box = DicePlacement.Beside(400, 300, 200, 80, Field, [ScreenBox.Around(400, 300, 14)]);
        Assert.Equal(new ScreenBox(426, 260, 200, 80), box);
    }

    [Fact]
    public void ItTriesLeftAboveAndBelowInTurnAndNeverCoversWhatItMustAvoid()
    {
        var model = ScreenBox.Around(400, 300, 14);
        var right = new ScreenBox(430, 290, 20, 20);
        Assert.Equal(new ScreenBox(174, 260, 200, 80), DicePlacement.Beside(400, 300, 200, 80, Field, [model, right]));
        var left = new ScreenBox(300, 280, 20, 20);
        Assert.Equal(new ScreenBox(300, 194, 200, 80), DicePlacement.Beside(400, 300, 200, 80, Field, [model, right, left]));
        var above = new ScreenBox(390, 200, 20, 20);
        Assert.Equal(new ScreenBox(300, 326, 200, 80), DicePlacement.Beside(400, 300, 200, 80, Field, [model, right, left, above]));
        var below = new ScreenBox(390, 350, 20, 20);
        Assert.Null(DicePlacement.Beside(400, 300, 200, 80, Field, [model, right, left, above, below]));
    }

    [Fact]
    public void AModelOffScreenOrAtTheEdgeSendsTheBoxToTheTray()
    {
        Assert.Null(DicePlacement.Beside(-20, 300, 200, 80, Field, []));
        Assert.Null(DicePlacement.Beside(500, 760, 200, 80, Field, []));
        // In a corner nothing fits inside the battlefield.
        Assert.Null(DicePlacement.Beside(5, 5, 600, 600, Field, []));
    }

    [Fact]
    public void ItKeepsItsPlaceWhileThatStillFits()
    {
        var kept = new ScreenBox(174, 260, 200, 80);
        Assert.Equal(kept, DicePlacement.Beside(400, 300, 200, 80, Field, [], kept));
        // Once something comes over it, it moves.
        Assert.Equal(new ScreenBox(426, 260, 200, 80), DicePlacement.Beside(400, 300, 200, 80, Field, [new ScreenBox(200, 280, 10, 10)], kept));
    }
}
