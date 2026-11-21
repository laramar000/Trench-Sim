using Client.Core;
using Rules.Core.State;
using Xunit;

namespace Client.Tests;

// Visual pass Stage 3: the drop row under a model's label.
public class BloodDropsTests
{
    [Fact]
    public void NoMarkersDrawsNothing()
    {
        Assert.Null(BloodDrops.Row(0));
        Assert.Null(BloodDrops.Row(-1));
    }

    [Fact]
    public void EachMarkerFillsOneDropUpToTheRulesMaximum()
    {
        for (var markers = 1; markers <= BloodMarkers.Max + 2; markers++)
        {
            var row = BloodDrops.Row(markers)!;
            Assert.Equal(BloodDrops.Height, row.Length);
            Assert.All(row, line => Assert.Equal(BloodDrops.Width, line.Length));
            Assert.Equal(Math.Min(markers, BloodMarkers.Max), BloodDrops.Filled(row));
            // Every drop the model could still take is drawn empty, so the row's length shows the maximum.
            var empty = Enumerable.Range(0, BloodMarkers.Max).Count(d => row[3][d * (BloodDrops.DropWidth + BloodDrops.Gap) + 2] == 'o');
            Assert.Equal(BloodMarkers.Max - Math.Min(markers, BloodMarkers.Max), empty);
        }
    }

    [Fact]
    public void TheRowUsesOnlyItsFourMarks()
    {
        foreach (var line in BloodDrops.Row(3)!)
        {
            Assert.All(line, c => Assert.Contains(c, "#Xo."));
        }
    }
}
