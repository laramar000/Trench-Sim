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
