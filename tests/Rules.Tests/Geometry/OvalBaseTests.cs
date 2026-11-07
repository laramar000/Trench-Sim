using Rules.Core.Geometry;
using static Rules.Tests.Geometry.Geo;

namespace Rules.Tests.Geometry;

// Roster stage R8a (U132): an oval base is a capsule along a table axis (radius = half the width, half span = how far each
// end's centre lies from the centre). Distances run between the nearest points of the bases, exactly as for round ones.
public class OvalBaseTests
{
    // 1" wide and 2" long.
    private static ModelBody Oval(string id, string x, string y, BaseAxis axis = BaseAxis.Y) =>
        new(id, P(x, y), L("0.5"), default, L("0.5"), axis);

    [Fact]
    public void ARoundBaseIsAnOvalWithNoSpan()
    {
        var round = Body("r", "5", "5");
        Assert.True(round.IsRound);
        Assert.Equal((round.Radius, round.Radius), (round.ExtentX, round.ExtentY));
        Assert.True(Oval("o", "5", "5").WithAxis(BaseAxis.X).ExtentX > Oval("o", "5", "5").ExtentX);
        Assert.Equal(BaseAxis.Y, new ModelBody("r", P("1", "1"), L("0.5"), default, default, BaseAxis.X).Axis);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ModelBody("o", P("1", "1"), L("0.5"), default, Length.FromThousandths(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ModelBody("o", P("1", "1"), L("0.5"), default, L("0.5"), (BaseAxis)7));
    }

    [Fact]
    public void RoundBodiesAreMeasuredAsBefore()
    {
        var rng = new Random(7);
        for (var i = 0; i < 300; i++)
        {
            var a = new ModelBody("a", new Point2(Length.FromThousandths(rng.Next(0, 9000)), Length.FromThousandths(rng.Next(0, 9000))), Length.FromThousandths(rng.Next(100, 900)));
            var b = new ModelBody("b", new Point2(Length.FromThousandths(rng.Next(0, 9000)), Length.FromThousandths(rng.Next(0, 9000))), Length.FromThousandths(rng.Next(100, 900)));
            var reach = (Int128)(a.Radius + b.Radius).Thousandths;
            var c2 = (Int128)ExactMath.DistanceSquared(a.Centre, b.Centre);
            Assert.Equal(c2 < reach * reach, Measure.Overlap(a, b));
            Assert.Equal(c2 <= reach * reach, Measure.InContact(a, b));
            Assert.Equal(c2 <= (reach + 1000) * (reach + 1000), Measure.IsWithin(a, b, Length.Inch));
        }
    }

    [Fact]
    public void DistanceRunsFromTheEndOfTheOval()
    {
        var oval = Oval("o", "10", "10");
        // Core runs y 9.5 to 10.5, so a round base centred 2" past the end is 1" from the edge.
        Assert.True(Measure.IsWithin(oval, Body("r", "10", "12.5"), L("1")));
        Assert.False(Measure.IsWithin(oval, Body("r", "10", "12.501"), L("1")));
        Assert.Equal(L("1"), Measure.Gap(oval, Body("r", "10", "12.5")));
        // The same oval lying along X reaches only 0.5" from its centre along Y.
        Assert.False(Measure.IsWithin(oval.WithAxis(BaseAxis.X), Body("r", "10", "12.5"), L("1")));
        Assert.True(Measure.IsWithin(oval.WithAxis(BaseAxis.X), Body("r", "10", "12.5"), L("1.5")));
        // Beside the long side the distance is across the width only.
        Assert.True(Measure.IsWithin(oval, Body("r", "11.5", "10.4"), L("0.5")));
        Assert.False(Measure.IsWithin(oval, Body("r", "11.5", "10.4"), L("0.499")));
    }

    [Fact]
    public void ContactAndOverlapAreMeasuredFromTheCapsule()
    {
        var oval = Oval("o", "10", "10");
        var touching = Body("t", "10", "11.5");
        Assert.True(Measure.InContact(oval, touching));
        Assert.False(Measure.Overlap(oval, touching));
        Assert.True(Measure.Overlap(oval, Body("t", "10", "11.499")));
        Assert.False(Measure.InContact(oval, Body("t", "10", "11.501")));
        // Two ovals end to end, and side by side.
        Assert.True(Measure.InContact(oval, Oval("p", "10", "12")));
        Assert.False(Measure.Overlap(oval, Oval("p", "10", "12")));
        Assert.True(Measure.Overlap(oval, Oval("p", "10", "11.999")));
        Assert.True(Measure.InContact(oval, Oval("p", "11", "10")));
        Assert.False(Measure.InContact(oval, Oval("p", "11.501", "10", BaseAxis.X)));
        Assert.True(Measure.InContact(oval, Oval("p", "11.5", "10", BaseAxis.X)));
    }

    [Fact]
    public void ACornerGapIsMeasuredAlongTheDiagonal()
    {
        // The end centre (10, 10.5) and a round base at (11, 11.5): centres sqrt(2) apart, minus the two radii.
        var oval = Oval("o", "10", "10");
        var other = Body("r", "11", "11.5");
        Assert.True(Measure.IsWithin(oval, other, L("0.415")));
        Assert.False(Measure.IsWithin(oval, other, L("0.414")));
        Assert.Equal(L("0.415"), Measure.Gap(oval, other));
    }

    [Fact]
    public void DifferentLevelsUseTheCapsuleToo()
    {
        var oval = Oval("o", "10", "10");
        var above = new ModelBody("u", P("10", "12.5"), L("0.5"), L("1"));
        // Horizontal gap 1", height 1": sqrt(2) apart.
        Assert.True(Measure.IsWithin(oval, above, L("1.415")));
        Assert.False(Measure.IsWithin(oval, above, L("1.414")));
        Assert.False(Measure.InContact(oval, new ModelBody("u", P("10", "10"), L("0.5"), L("1"))));
    }

    [Fact]
    public void AnOvalAgainstTerrainAndZones()
    {
        var wall = Wall("w", "10", "13", "14", "14");
        var field = Field("24", [wall], [new DeploymentZone("west", F("0", "0", "6", "24"))]);
        // The long end reaches 1" from the centre: a wall 2.5" away is hit by the oval but not by a round base.
        Assert.False(Placement.Check(field, Oval("o", "11", "12.4"), []).IsLegal);
        Assert.Equal(PlacementRefusal.InBlockingTerrain, Placement.Check(field, Oval("o", "11", "12.4"), []).Reason);
        Assert.True(Placement.Check(field, Body("r", "11", "12.4"), []).IsLegal);
        Assert.True(Placement.Check(field, Oval("o", "11", "11.999"), []).IsLegal);
        // A zone holds an oval whose box is wholly inside, touching allowed.
        Assert.True(Placement.Check(field, Oval("o", "3", "5"), [], "west").IsLegal);
        Assert.True(Placement.Check(field, Oval("o", "5.5", "5"), [], "west").IsLegal);
        Assert.Equal(PlacementRefusal.OutsideZone, Placement.Check(field, Oval("o", "5.501", "5"), [], "west").Reason);
        Assert.True(Placement.Check(field, Oval("o", "5", "5", BaseAxis.X), [], "west").IsLegal);
        Assert.Equal(PlacementRefusal.OutsideZone, Placement.Check(field, Oval("o", "5.001", "5", BaseAxis.X), [], "west").Reason);
        Assert.True(Placement.Check(field, Oval("o", "4.5", "5", BaseAxis.X), [], "west").IsLegal);
        // Another base in the way, end to end.
        Assert.Equal(PlacementRefusal.OverlapsBase, Placement.Check(field, Oval("o", "3", "5"), [Body("r", "3", "6.4")]).Reason);
    }

    [Fact]
    public void MoreThanHalfMustBeOnTheBattlefield()
    {
        var field = Field("24");
        // One edge: exact, the centre decides.
        Assert.Equal(PlacementRefusal.OffBattlefield, Placement.Check(field, Oval("o", "10", "0"), []).Reason);
        Assert.True(Placement.Check(field, Oval("o", "10", "0.001"), []).IsLegal);
        Assert.False(Placement.Check(field, Oval("o", "10", "-0.001"), []).IsLegal);
        // Lying along X the same oval reaches only 0.5" below the centre, so y 0.4 is fine and x 0 is not.
        Assert.True(Placement.Check(field, Oval("o", "10", "0.4", BaseAxis.X), []).IsLegal);
        Assert.False(Placement.Check(field, Oval("o", "0", "10", BaseAxis.X), []).IsLegal);
    }

    // A corner crosses two edges: the area inside is bracketed numerically. A Y-axis oval at (a, b) is the mirror image of
    // an X-axis oval at (b, a) on a square table, and both agree with a plain grid count where the answer is not close.
    [Fact]
    public void ACornerIsDecidedByArea()
    {
        var area = F("0", "0", "24", "24");
        for (var a = 0; a <= 120; a += 10)
        {
            for (var b = 0; b <= 120; b += 10)
            {
                var y = new ModelBody("o", new Point2(Length.FromThousandths(a * 10), Length.FromThousandths(b * 10)), L("0.5"), default, L("0.5"), BaseAxis.Y);
                var x = new ModelBody("o", new Point2(Length.FromThousandths(b * 10), Length.FromThousandths(a * 10)), L("0.5"), default, L("0.5"), BaseAxis.X);
                var decision = area.MoreThanHalfInside(y);
                Assert.Equal(decision, area.MoreThanHalfInside(x));
                var share = InsideShare(a * 10, b * 10);
                if (share > 0.52)
                {
                    Assert.Equal(AreaDecision.MoreThanHalf, decision);
                }
                else if (share < 0.48)
                {
                    Assert.Equal(AreaDecision.HalfOrLess, decision);
                }
            }
        }
    }

    // The share of a 1" by 2" Y-axis oval (thousandths) centred at (cx, cy) that lies on the table (x, y >= 0), by grid count.
    private static double InsideShare(int cx, int cy)
    {
        const int n = 400;
        int inside = 0, total = 0;
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < n; j++)
            {
                double px = cx - 500 + (i + 0.5) * 1000.0 / n, py = cy - 1000 + (j + 0.5) * 2000.0 / n;
                // Core: x = cx, y in [cy - 500, cy + 500]; radius 500.
                var dy = Math.Max(0, Math.Abs(py - cy) - 500);
                var dx = px - cx;
                if (dx * dx + dy * dy > 500.0 * 500.0)
                {
                    continue;
                }
                total++;
                if (px >= 0 && py >= 0)
                {
                    inside++;
                }
            }
        }
        return (double)inside / total;
    }

    [Fact]
    public void JoinedFootprintsAndWithinWorkForOvals()
    {
        var joined = Footprint.Join([R("0", "0", "4", "2"), R("0", "2", "2", "6")]);
        // Fits in the long arm, not across the notch.
        Assert.True(joined.Inside(Oval("o", "1", "4")));
        Assert.False(joined.Inside(Oval("o", "3", "3", BaseAxis.Y)));
        Assert.True(joined.Within(Oval("o", "5.2", "1"), L("0.7")));
        Assert.False(joined.Within(Oval("o", "5.2", "1"), L("0.6")));
        Assert.True(joined.Touches(Oval("o", "4.5", "1")));
        Assert.False(joined.Overlaps(Oval("o", "4.5", "1")));
        Assert.True(joined.Overlaps(Oval("o", "4.4", "1")));
        Assert.Equal(AreaDecision.MoreThanHalf, joined.MoreThanHalfInside(Oval("o", "1", "3.5")));
    }
}
