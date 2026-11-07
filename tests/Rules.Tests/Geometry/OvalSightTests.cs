using Rules.Core.Geometry;
using static Rules.Tests.Geometry.Geo;

namespace Rules.Tests.Geometry;

// Roster stage R8c (U132): oval bases in sight, cover, blasts, mines, platforms.
public class OvalSightTests
{
    private static ModelBody Oval(string id, string x, string y, BaseAxis axis = BaseAxis.Y) =>
        new(id, P(x, y), L("0.5"), default, L("0.5"), axis);

    [Fact]
    public void AnOvalSeenFromTheSideIsWiderThanSeenFromTheEnd()
    {
        var attackerSide = Body("a", "20", "10");
        var attackerEnd = Body("a", "10", "20");
        var oval = Oval("t", "10", "10");
        // From the side (along x) the across-line half width is Radius + HalfSpan; from the end it is the Radius.
        var side = HeightSight.Sample(attackerSide, oval);
        var end = HeightSight.Sample(attackerEnd, oval);
        Assert.Equal(oval.Centre, side[1]);
        Assert.Equal(1000L * SightHeights.SideThousandths / 1000, Math.Abs(side[0].Y.Thousandths - oval.Centre.Y.Thousandths));
        Assert.Equal(500L * SightHeights.SideThousandths / 1000, Math.Abs(end[0].X.Thousandths - oval.Centre.X.Thousandths));
        // A round target is as it was.
        Assert.Equal(500L * SightHeights.SideThousandths / 1000, Math.Abs(HeightSight.Sample(attackerSide, Body("t", "10", "10"))[0].Y.Thousandths - 10000));
        // Lying along x the same oval is the other way about.
        var flat = HeightSight.Sample(attackerSide, oval.WithAxis(BaseAxis.X));
        Assert.Equal(500L * SightHeights.SideThousandths / 1000, Math.Abs(flat[0].Y.Thousandths - 10000));
    }

    [Fact]
    public void CoverNeedsAPieceAsWideAsTheOvalAcrossTheLine()
    {
        var oval = Oval("t", "10", "10");
        var attacker = new Point2(L("20"), L("10"));
        // Seen from the east the oval is 2" across; from the north it is 1".
        Assert.False(F("9", "9.2", "11", "10.8").IsAtLeastAsWideAcross(attacker, oval.Centre, oval));
        Assert.True(F("9", "9", "11", "11").IsAtLeastAsWideAcross(attacker, oval.Centre, oval));
        Assert.True(F("9", "9.2", "11", "10.8").IsAtLeastAsWideAcross(attacker, oval.Centre, oval.WithAxis(BaseAxis.X)));
        Assert.True(F("9.5", "9", "10.5", "11").IsAtLeastAsWideAcross(new Point2(L("10"), L("20")), oval.Centre, oval));
        Assert.False(F("9.6", "9", "10.4", "11").IsAtLeastAsWideAcross(new Point2(L("10"), L("20")), oval.Centre, oval));
        // Diagonally the width across the line is 2 (Radius + HalfSpan * 0.7071) = 1.7071": a square piece needs a side of 1.2071".
        var diagonal = new Point2(L("20"), L("20"));
        Assert.True(F("9", "9", "10.21", "10.21").IsAtLeastAsWideAcross(diagonal, oval.Centre, oval));
        Assert.False(F("9", "9", "10.2", "10.2").IsAtLeastAsWideAcross(diagonal, oval.Centre, oval));
    }

    [Fact]
    public void ABlastReachesAnOvalThroughItsCore()
    {
        var oval = Oval("o", "10", "10");
        // From 1.5" past the end of the core the base is 1" away; the centre is 2" off.
        Assert.True(Blast.Reaches(P("10", "12"), Length.Zero, oval, Length.Zero, L("1")));
        Assert.False(Blast.Reaches(P("10", "12.001"), Length.Zero, oval, Length.Zero, L("1")));
        Assert.False(Blast.Reaches(P("10", "12"), Length.Zero, oval.WithAxis(BaseAxis.X), Length.Zero, L("1")));
        Assert.True(Blast.Reaches(P("10", "11.5"), Length.Zero, oval.WithAxis(BaseAxis.X), Length.Zero, L("1")));
        // A round base is as before.
        Assert.True(Blast.Reaches(P("10", "11.5"), Length.Zero, Body("o", "10", "10"), Length.Zero, L("1")));
        Assert.False(Blast.Reaches(P("10", "11.501"), Length.Zero, Body("o", "10", "10"), Length.Zero, L("1")));
    }

    [Fact]
    public void AnOvalOnAPlatformEdgeIsOnTheGround()
    {
        var field = Field("24", Platform("p", "10", "10", "14", "14"));
        Assert.Equal(ModelLevel.Ground, Elevation.Of(field, Oval("o", "12", "10")).Level);
        Assert.Equal(ModelLevel.High, Elevation.Of(field, Oval("o", "12", "10.001")).Level);
        Assert.Equal(ModelLevel.Ground, Elevation.Of(field, Oval("o", "12", "9.999")).Level);
        // Lying along x the oval at the same spot is the same by symmetry about its centre.
        Assert.Equal(ModelLevel.High, Elevation.Of(field, Oval("o", "12", "10.001", BaseAxis.X)).Level);
    }

    // The exact mine entry against a dense sample of positions along the leg.
    [Fact]
    public void MineEntryAgreesWithDenseSampling()
    {
        var rng = new Random(5);
        const int steps = 4000;
        for (var scene = 0; scene < 500; scene++)
        {
            Length Pick(int lo, int hi) => Length.FromThousandths(rng.Next(lo, hi));
            var span = rng.Next(3) == 0 ? Length.Zero : Pick(200, 900);
            var mover = new ModelBody("m", new Point2(Pick(2000, 8000), Pick(2000, 8000)), Pick(250, 700), default, span, rng.Next(2) == 0 ? BaseAxis.X : BaseAxis.Y);
            var end = new Point2(Pick(2000, 8000), Pick(2000, 8000));
            var marker = new Point2(Pick(2000, 8000), Pick(2000, 8000));
            if (Mines.IsWithin(mover, mover.Centre, marker))
            {
                continue;
            }
            var reach = (Int128)(mover.Radius + Mines.MarkerRadius + Mines.TriggerRange).Thousandths;
            var exact = Mines.EntryOf(mover, mover.Centre, end, marker, reach);
            double GapAt(double t)
            {
                var x = mover.Centre.X.Thousandths + (end.X.Thousandths - mover.Centre.X.Thousandths) * t;
                var y = mover.Centre.Y.Thousandths + (end.Y.Thousandths - mover.Centre.Y.Thousandths) * t;
                var (x0, x1, y0, y1) = mover.At(mover.Centre).CoreBox();
                var shiftX = x - mover.Centre.X.Thousandths;
                var shiftY = y - mover.Centre.Y.Thousandths;
                var dx = Math.Max(0, Math.Max(x0 + shiftX - marker.X.Thousandths, marker.X.Thousandths - (x1 + shiftX)));
                var dy = Math.Max(0, Math.Max(y0 + shiftY - marker.Y.Thousandths, marker.Y.Thousandths - (y1 + shiftY)));
                return Math.Sqrt(dx * dx + dy * dy);
            }
            var r = (double)reach;
            var upTo = exact is { } found ? (double)found.Num / (double)found.Den : 2.0;
            // Nothing before the exact entry is inside by a clear margin.
            for (var i = 0; i <= steps && (double)i / steps < upTo; i++)
            {
                Assert.True(GapAt((double)i / steps) >= r - 2.5, $"scene {scene}: the samples are inside at {(double)i / steps} before the entry {upTo}");
            }
            if (exact is { } t)
            {
                Assert.True(upTo <= 1.0 + 1e-9, $"scene {scene}: entry {upTo} beyond the leg");
                // And at the entry itself the base is at the edge of the reach.
                Assert.True(Math.Abs(GapAt(upTo) - r) <= 3.5, $"scene {scene}: at the entry {upTo} the gap is {GapAt(upTo)}, not {r}");
            }
        }
    }
}
