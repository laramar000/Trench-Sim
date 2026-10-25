using Client.Core;
using Rules.Core.Geometry;
using Xunit;

namespace Client.Tests;

// Line-of-sight stage L2: terrain is drawn at the heights the rules' sight uses, so the picture and the rules agree.
public class BoardSceneHeightTests
{
    [Fact]
    public void DisplayHeightsAreTheRulesSightHeights()
    {
        Assert.Equal(TableRect.Inches(SightHeights.Low), DisplayHeight.Low);
        Assert.Equal(TableRect.Inches(SightHeights.Cover), DisplayHeight.Cover);
        Assert.Equal(TableRect.Inches(SightHeights.Wall), DisplayHeight.Wall);
        foreach (var profile in Enum.GetValues<TerrainProfile>())
        {
            var piece = new TerrainBlock("p", Footprint.Of(new Rect(new Point2(Length.Zero, Length.Zero), new Point2(Length.Inch, Length.Inch))), profile,
                TerrainKind.Open);
            Assert.Equal(TableRect.Inches(SightHeights.TopOf(piece)), DisplayHeight.Of(profile));
        }
    }
}

// Line-of-sight stage L3: the dice preview names the Elevated position bonus with its page.
public class ElevatedDicePreviewTests
{
    [Fact]
    public void TheHitRollShowsTheBonusDie()
    {
        var dice = new DicePreview(1, ["long range"], false, 0, 0, 0, 0) { HitBonus = 1 };
        var lines = dice.Lines(new Rules.Core.State.ModelId("a1"), new Rules.Core.State.ModelId("b1"));
        Assert.Equal("Hit roll: +1 DICE (elevated position, 3\" or more above the target, p. 43)", lines[0]);
        Assert.Equal("Hit roll: -1 DICE (long range)", lines[1]);
        Assert.DoesNotContain(new DicePreview(0, [], false, 0, 0, 0, 0).Lines(new("a1"), new("b1")), l => l.Contains("elevated", StringComparison.Ordinal));
    }
}
