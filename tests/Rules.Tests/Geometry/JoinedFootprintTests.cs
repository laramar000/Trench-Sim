using Rules.Core.Geometry;
using static Rules.Tests.Geometry.Geo;

namespace Rules.Tests.Geometry;

// Trench-maps stage S2: footprints of joined rectangles. A rectangle cut into parts must answer every exact question
// exactly as the whole rectangle does (the seams are inside the shape), and L, T and plus shapes get their own cases.
public class JoinedFootprintTests
{
    private static Footprint Join(params Rect[] parts) => Footprint.Join(parts);

    // 10 x 4 at (2, 3), whole and cut three ways: in two side by side, in two one above the other, and in four
    // meeting at one point.
    private static readonly Footprint Whole = F("2", "3", "12", "7");

    private static readonly Footprint[] Cuts =
    [
        Join(R("2", "3", "6", "7"), R("6", "3", "12", "7")),
        Join(R("2", "3", "12", "5"), R("2", "5", "12", "7")),
        Join(R("2", "3", "6", "5"), R("6", "3", "12", "5"), R("2", "5", "6", "7"), R("6", "5", "12", "7")),
    ];

    private static Point2 At(Random rng, int lo, int hi) =>
        new(Length.FromThousandths(rng.Next(lo, hi) / 250 * 250 + (rng.Next(4) == 0 ? rng.Next(250) : 0)),
            Length.FromThousandths(rng.Next(lo, hi) / 250 * 250 + (rng.Next(4) == 0 ? rng.Next(250) : 0)));

    [Fact]
    public void ARectangleCutIntoPartsAnswersEveryExactQuestionAsTheWholeRectangle()
    {
        var rng = new Random(20261025);
        for (var i = 0; i < 4000; i++)
        {
            // Points on a quarter-inch grid most of the time, so seams, edges and corners are hit often.
            var a = At(rng, 0, 14000);
            var b = At(rng, 0, 14000);
            var r = Length.FromThousandths(rng.Next(1, 8) * 250);
            var h1 = Length.FromThousandths(rng.Next(0, 4) * 500);
            var h2 = Length.FromThousandths(rng.Next(0, 4) * 500);
            foreach (var cut in Cuts)
            {
                Assert.Equal(Whole.CircleOverlaps(a, r), cut.CircleOverlaps(a, r));
                Assert.Equal(Whole.CircleTouches(a, r), cut.CircleTouches(a, r));
                Assert.Equal(Whole.CircleWithin(a, r, Length.Inch), cut.CircleWithin(a, r, Length.Inch));
                Assert.Equal(Whole.CircleInside(a, r), cut.CircleInside(a, r));
                Assert.Equal(Whole.ContainsInside(a), cut.ContainsInside(a));
                Assert.Equal(Whole.SweptCircleOverlaps(a, b, r), cut.SweptCircleOverlaps(a, b, r));
                Assert.Equal(Whole.SegmentCrossesInside(a, b), cut.SegmentCrossesInside(a, b));
                Assert.Equal(Whole.SegmentCrossesBoxInside(a, h1, b, h2, Length.Zero, L("0.75")), cut.SegmentCrossesBoxInside(a, h1, b, h2, Length.Zero, L("0.75")));
                Assert.Equal(Whole.IsAtLeastAsWideAcross(a, b, r * 2), cut.IsAtLeastAsWideAcross(a, b, r * 2));
                Assert.Equal(Movement.LegCostMicro(a, b, [Whole]), Movement.LegCostMicro(a, b, [cut]));
                // More than half: never more than half where the whole says not, and the same answer whenever the
                // whole rectangle's answer is not near the line.
                var whole = Whole.MoreThanHalfInside(a, r);
                var parts = cut.MoreThanHalfInside(a, r);
                if (whole != AreaDecision.MoreThanHalf)
                {
                    Assert.NotEqual(AreaDecision.MoreThanHalf, parts);
                }
                if (Whole.CircleInside(a, r) || !Whole.CircleOverlaps(a, r))
                {
                    Assert.Equal(whole, parts);
                }
            }
        }
    }

    [Fact]
    public void AnEdgeOrSeamIsAnsweredExactly()
    {
        var cut = Cuts[0];
        // Along the seam x = 6: inside the shape, though inside neither part.
        Assert.True(cut.SegmentCrossesInside(P("6", "1"), P("6", "9")));
        Assert.True(cut.ContainsInside(P("6", "5")));
        // Along the outer edge y = 3: touching only.
        Assert.False(cut.SegmentCrossesInside(P("0", "3"), P("14", "3")));
        Assert.False(cut.ContainsInside(P("6", "3")));
        // Where four parts meet, at (6, 5), the point is inside.
        Assert.True(Cuts[2].ContainsInside(P("6", "5")));
        Assert.True(Cuts[2].SegmentCrossesInside(P("6", "5"), P("6", "5.001")));
        // A base straddling the seam is wholly inside and more than half in.
        Assert.True(cut.CircleInside(P("6", "5"), L("1")));
        Assert.Equal(AreaDecision.MoreThanHalf, cut.MoreThanHalfInside(P("6", "5"), L("1")));
    }

    // An L: a 10 x 2 foot along the bottom and a 2 x 8 arm up the left side, meeting at (0..2, 2).
    private static readonly Footprint Ell = Join(R("0", "0", "10", "2"), R("0", "2", "2", "10"));

    [Fact]
    public void AnLShapeHasAnInsideCornerThatIsNotPartOfIt()
    {
        Assert.Equal(new Rect(P("0", "0"), P("10", "10")), Ell.Bounds);
        // The notch above the foot and right of the arm is outside.
        Assert.False(Ell.ContainsInside(P("5", "5")));
        Assert.False(Ell.CircleOverlaps(P("5", "5"), L("1")));
        // A segment across the notch from the arm's edge to the foot's edge touches both, but never the inside.
        Assert.False(Ell.SegmentCrossesInside(P("2", "6"), P("6", "2")));
        // One that cuts the inside corner does.
        Assert.True(Ell.SegmentCrossesInside(P("1", "6"), P("6", "1")));
        // A base in the elbow is inside; one hanging into the notch is not.
        Assert.True(Ell.CircleInside(P("1", "1"), L("1")));
        Assert.False(Ell.CircleInside(P("1.5", "1.5"), L("1")));
        // In the notch, 1" above the foot and 1" right of the arm: touches both at 1", overlaps neither.
        Assert.True(Ell.CircleTouches(P("3", "3"), L("1")));
        Assert.False(Ell.CircleTouches(P("3", "3"), L("0.999")));
        Assert.False(Ell.CircleOverlaps(P("3", "3"), L("1")));
        Assert.True(Ell.CircleWithin(P("3", "3"), L("0.5"), L("0.5")));
        Assert.False(Ell.CircleWithin(P("3", "3"), L("0.5"), L("0.499")));
    }

    [Fact]
    public void MoreThanHalfAtAJunctionCountsEveryPart()
    {
        // A base of radius 1 centred on the inside corner of the L: three quarters of it is in the L, but under half is
        // in either part on its own (a quarter in the arm, a half exactly in the foot).
        var centre = P("2", "2");
        Assert.NotEqual(AreaDecision.MoreThanHalf, BaseArea.MoreThanHalfInside(centre, L("1"), R("0", "0", "10", "2")));
        Assert.NotEqual(AreaDecision.MoreThanHalf, BaseArea.MoreThanHalfInside(centre, L("1"), R("0", "2", "2", "10")));
        Assert.Equal(AreaDecision.MoreThanHalf, Ell.MoreThanHalfInside(centre, L("1")));
        // Its opposite, at the outside corner: a quarter.
        Assert.Equal(AreaDecision.HalfOrLess, Ell.MoreThanHalfInside(P("10", "0"), L("1")));
        // On the outer edge of the arm, at x = 0, exactly half: not more than half, whichever way it is decided.
        Assert.NotEqual(AreaDecision.MoreThanHalf, Ell.MoreThanHalfInside(P("0", "6"), L("0.5")));
    }

    [Fact]
    public void TAndPlusShapesBehaveAtTheirJunctions()
    {
        // A T: a bar 12 x 2 along the top, a stem 2 x 6 down the middle.
        var tee = Join(R("0", "6", "12", "8"), R("5", "0", "7", "6"));
        Assert.True(tee.CircleInside(P("6", "6.5"), L("1")));
        Assert.False(tee.CircleInside(P("6", "5"), L("1.5")));
        Assert.True(tee.SegmentCrossesInside(P("6", "-1"), P("6", "9")));
        Assert.False(tee.SegmentCrossesInside(P("0", "5"), P("5", "6")));
        Assert.Equal(AreaDecision.MoreThanHalf, tee.MoreThanHalfInside(P("5", "6"), L("1")));

        // A plus: a crossroads of two 2" wide roads, the middle cut as its own part.
        var plus = Join(R("4", "4", "6", "6"), R("0", "4", "4", "6"), R("6", "4", "10", "6"), R("4", "0", "6", "4"), R("4", "6", "6", "10"));
        Assert.True(plus.ContainsInside(P("4", "4.5")));
        Assert.False(plus.ContainsInside(P("4", "4")));
        Assert.True(plus.ContainsInside(P("5", "5")));
        // A base in the middle stays on the roads until it reaches into a corner square, past the square root of 2.
        Assert.True(plus.CircleInside(P("5", "5"), L("1.414")));
        Assert.False(plus.CircleInside(P("5", "5"), L("1.415")));
        // Diagonal through the middle: inside. Corner to corner of the middle square's notch: grazes corners only.
        Assert.True(plus.SegmentCrossesInside(P("0", "0"), P("10", "10")));
        Assert.False(plus.SegmentCrossesInside(P("2", "4"), P("4", "2")));
        Assert.False(plus.CircleOverlaps(P("2", "2"), L("1")));
        // A leg straight through the crossroads in Difficult terrain costs double once, not more where parts meet.
        var straight = Movement.LegCostMicro(P("-1", "5"), P("11", "5"), []);
        Assert.Equal(straight + 10_000_000, Movement.LegCostMicro(P("-1", "5"), P("11", "5"), [plus]));
    }

    [Fact]
    public void PartsAreKeptInOneOrderAndCompareByShape()
    {
        var one = Join(R("0", "0", "10", "2"), R("0", "2", "2", "10"));
        var other = Join(R("0", "2", "2", "10"), R("0", "0", "10", "2"));
        Assert.Equal(one, other);
        Assert.Equal(one.GetHashCode(), other.GetHashCode());
        Assert.True(one.Parts.SequenceEqual(other.Parts));
        Assert.NotEqual(Footprint.Of(R("0", "0", "10", "2")), one);
        Assert.Equal(Footprint.Of(R("0", "0", "10", "2")), Join(R("0", "0", "10", "2")));
        Assert.Equal("[(0.000, 0.000) to (10.000, 2.000)] + [(0.000, 2.000) to (2.000, 10.000)]", one.ToString().Replace("\"", "", StringComparison.Ordinal));
    }

    [Fact]
    public void BadPartsAreRefused()
    {
        Assert.Throws<ArgumentException>(() => Footprint.Join([]));
        // Overlapping.
        Assert.Throws<ArgumentException>(() => Join(R("0", "0", "4", "2"), R("3", "0", "6", "2")));
        // Apart, or touching only at a corner.
        Assert.Throws<ArgumentException>(() => Join(R("0", "0", "2", "2"), R("3", "0", "5", "2")));
        Assert.Throws<ArgumentException>(() => Join(R("0", "0", "2", "2"), R("2", "2", "4", "4")));
        // Too many.
        Assert.Throws<ArgumentException>(() => Footprint.Join(Enumerable.Range(0, Footprint.MaxParts + 1).Select(i => R($"{i}", "0", $"{i + 1}", "1"))));
        Footprint.Join(Enumerable.Range(0, Footprint.MaxParts).Select(i => R($"{i}", "0", $"{i + 1}", "1")));
    }

    [Fact]
    public void RaisedPiecesAndZonesStayOneRectangle()
    {
        Assert.Throws<ArgumentException>(() => new TerrainBlock("roof", Ell, TerrainProfile.Wall, TerrainKind.Open, SurfaceLevel.High));
        Assert.Throws<ArgumentException>(() => new DeploymentZone("z", Ell));
        // A ground piece may be joined: a Wall, a barricade, rough ground.
        var wall = new TerrainBlock("wall", Ell, TerrainProfile.Wall);
        var rough = new TerrainBlock("rough", Join(R("12", "0", "16", "2"), R("12", "2", "14", "6")), TerrainProfile.Low, TerrainKind.Difficult);
        var field = new Battlefield(L("24"), L("24"), [wall, rough], []);
        Assert.Equal(2, field.Terrain.Length);
        // Off the table.
        Assert.Throws<ArgumentException>(() => new Battlefield(L("9"), L("24"), [wall], []));
    }

    [Fact]
    public void AJoinedWallBlocksBasesAndSightAlongItsSeam()
    {
        // A wall cut in two along x = 6: a base may not be placed on the seam, and sight along the seam is blocked.
        var wall = new TerrainBlock("wall", Cuts[0], TerrainProfile.Wall);
        var field = new Battlefield(L("24"), L("24"), [wall], []);
        var sight = HeightSight.Between(field, Body("a", "6", "1"), Body("b", "6", "9"));
        Assert.Equal(Visibility.None, sight.Visibility);
        Assert.Contains(sight.Rays, r => r.Obstructing.Contains("wall"));
        Assert.False(Placement.Check(field, Body("c", "6", "5"), []).IsLegal);
    }
}
