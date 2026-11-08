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

    // Roster stage R10 (U135): INFECTION and BLESSING MARKERS are drops of their own colour in the same row.
    [Fact]
    public void InfectionAndBlessingMarkersAreDropsOfTheirOwnKind()
    {
        var row = BloodDrops.Row(1, 2, 1)!;
        Assert.Equal(4, BloodDrops.Filled(row));
        // One blood, two infection, one blessing, in that order; the row is the usual six drops wide.
        var centres = Enumerable.Range(0, 6).Select(d => row[3][d * (BloodDrops.DropWidth + BloodDrops.Gap) + 2]).ToArray();
        Assert.Equal(['#', 'g', 'g', 'b', 'o', 'o'], centres);
        Assert.All(row, line => Assert.Equal(BloodDrops.Width, line.Length));
        // Blood alone is exactly the row it always was; several kinds can need more room.
        Assert.Equal(BloodDrops.Row(3), BloodDrops.Row(3, 0, 0));
        Assert.Equal(BloodDrops.WidthOf(12), BloodDrops.Row(6, 6, 0)![0].Length);
        Assert.Equal(18, BloodDrops.Filled(BloodDrops.Row(6, 6, 6)!));
        Assert.Null(BloodDrops.Row(0, 0, 0));
    }

    // October 2026 Review (U150): SIN MARKERS are a fourth kind of drop, after the blessing.
    [Fact]
    public void SinMarkersAreDropsOfTheirOwnKindAfterTheBlessing()
    {
        var row = BloodDrops.Row(1, 0, 1, 2)!;
        Assert.Equal(4, BloodDrops.Filled(row));
        var centres = Enumerable.Range(0, 6).Select(d => row[3][d * (BloodDrops.DropWidth + BloodDrops.Gap) + 2]).ToArray();
        Assert.Equal(['#', 'b', 'k', 'k', 'o', 'o'], centres);
        Assert.Equal(BloodDrops.Row(2, 1, 1), BloodDrops.Row(2, 1, 1, 0));
        Assert.Equal(24, BloodDrops.Filled(BloodDrops.Row(6, 6, 6, 6)!));
        Assert.NotNull(BloodDrops.Row(0, 0, 0, 1));
    }
}
