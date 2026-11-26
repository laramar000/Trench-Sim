using Rules.Core.Geometry;
using static Rules.Tests.Geometry.Geo;

namespace Rules.Tests.Geometry;

// Trench-maps stage S3 with line-of-sight stage L7: trench sections (U80), being in one (U81), distances across the
// edge (U82), and sight and cover with the ground solid around a trench (U83).
public class TrenchSightTests
{
    private static TerrainBlock Trench(string id, string depth, params Rect[] parts) =>
        new(id, parts.Length == 1 ? Footprint.Of(parts[0]) : Footprint.Join(parts), TerrainProfile.Low) { Depth = L(depth) };

    // A straight trench 2.5" wide along y = 10 .. 12.5, from x = 4 to 20, 1" deep.
    private static Battlefield Straight(string depth = "1", params TerrainBlock[] more) =>
        Field("24", [Trench("trench", depth, R("4", "10", "20", "12.5")), .. more]);

    private static SightResult See(Battlefield field, ModelBody a, ModelBody b) => HeightSight.Between(field, a, b);

    [Fact]
    public void AModelMoreThanHalfInTheTrenchStandsOnItsFloor()
    {
        var field = Straight();
        var inside = Body("a", "12", "11.25");
        Assert.Equal("trench", Trenches.Of(field, inside)?.Id);
        Assert.Equal(L("-1"), SightHeights.StandingHeight(field, inside, Elevation.Of(field, inside)));
        // Centre on the rim: exactly half in, so on the ground.
        var rim = Body("b", "12", "10");
        Assert.Null(Trenches.Of(field, rim));
        Assert.Equal(Length.Zero, SightHeights.StandingHeight(field, rim, Elevation.Of(field, rim)));
        // Just over half.
        Assert.NotNull(Trenches.Of(field, Body("c", "12", "10.01")));
    }

    [Fact]
    public void AShooterOnTheGroundSeesOnlyTheHeadOfAModelInTheTrenchWhichHasCover()
    {
        var field = Straight();
        var shooter = Body("s", "12", "2");
        var target = Body("t", "12", "11.25");
        var sight = See(field, shooter, target);
        Assert.Equal(Visibility.Partial, sight.Visibility);
        Assert.Equal(3, sight.Visible);
        // Legs and body hidden by the trench's near edge, the head seen.
        Assert.All(sight.Rays.Take(6), r => Assert.Equal(["trench"], r.Obstructing));
        Assert.All(sight.Rays.Skip(6), r => Assert.Empty(r.Obstructing));
        Assert.Equal(L("-0.7"), sight.Rays[0].ToHeight);
        var cover = Cover.Of(field, shooter, target);
        Assert.True(cover.InCover);
        Assert.Equal("trench", cover.TerrainId);
    }

    [Fact]
    public void FromTheRimTheWholeModelInTheTrenchIsSeenWithoutCover()
    {
        var field = Straight();
        var shooter = Body("s", "12", "9.5");
        var target = Body("t", "12", "11.25");
        Assert.Equal(Visibility.Full, See(field, shooter, target).Visibility);
        Assert.False(Cover.Of(field, shooter, target).InCover);
        // And they are within 1" of each other across the edge: trench depth is not measured (U82).
        Assert.True(Measure.IsWithin(shooter, target, Length.Inch));
    }

    [Fact]
    public void TwoModelsAlongTheSameTrenchSeeEachOtherInFull()
    {
        var field = Straight();
        Assert.Equal(Visibility.Full, See(field, Body("a", "6", "11.25"), Body("b", "18", "11.25")).Visibility);
        Assert.False(Cover.Of(field, Body("a", "6", "11.25"), Body("b", "18", "11.25")).InCover);
    }

    [Fact]
    public void AModelInAOneInchTrenchSeesOutOverTheEdge()
    {
        var field = Straight();
        var sight = See(field, Body("a", "12", "11.25"), Body("b", "12", "20"));
        Assert.Equal(Visibility.Full, sight.Visibility);
        Assert.Equal(L("0.1"), sight.Rays[0].FromHeight);
    }

    [Fact]
    public void AModelDeepInATrenchCannotSeeOutAndItsOwnTrenchIsNamed()
    {
        var field = Straight("2.5");
        var sight = See(field, Body("a", "12", "11.25"), Body("b", "12", "20"));
        Assert.Equal(Visibility.None, sight.Visibility);
        Assert.All(sight.Rays, r => Assert.Equal(["trench"], r.Obstructing));
        // The model on the ground looking in is just as blind, and the trench gives no cover to a model it hides fully.
        var back = Cover.Of(field, Body("b", "12", "20"), Body("a", "12", "11.25"));
        Assert.Equal(Visibility.None, back.Sight.Visibility);
        Assert.False(back.InCover);
    }

    [Fact]
    public void TheGroundInsideABendBlocksSightAroundTheCorner()
    {
        // An L: an arm up the west side and a foot along the south, 2.5" wide, 1" deep.
        var field = Field("24", [Trench("bend", "1", R("4", "4", "6.5", "16"), R("6.5", "4", "18", "6.5"))]);
        var shooter = Body("a", "5.25", "15");
        var target = Body("b", "17", "5.25");
        var sight = See(field, shooter, target);
        // Head points at 0.1" above the ground on both sides: the head rays pass over the inside corner. Legs and body
        // pass through the ground there.
        Assert.Equal(Visibility.Partial, sight.Visibility);
        Assert.Equal(3, sight.Visible);
        Assert.All(sight.Rays.Take(6), r => Assert.Equal(["bend"], r.Obstructing));
        Assert.True(Cover.Of(field, shooter, target).InCover);
    }

    [Fact]
    public void AModelOnATowerIsElevatedOverAModelInATrench()
    {
        var tower = new TerrainBlock("tower", F("10", "16", "13", "19"), TerrainProfile.Wall, surface: SurfaceLevel.High, walkable: true)
        {
            Climb = new Climb(L("2"), [new Ladder("tower-w", P("10", "17.5"))]),
        };
        var field = Straight("1", tower);
        var high = new ModelBody("a", P("11.5", "17.5"), L("0.5"), L("2"));
        var low = Body("b", "12", "11.25");
        // 2" up against 1" down: 3", which counts (p. 43, U79).
        Assert.True(SightHeights.IsElevated(field, high, low));
        Assert.False(SightHeights.IsElevated(field, high, Body("c", "12", "21")));
        // From the tower the whole model in the trench is seen.
        Assert.Equal(Visibility.Full, See(field, high, low).Visibility);
    }

    [Theory]
    [InlineData("0.5")]
    [InlineData("0.984")]
    [InlineData("1.2")]
    public void MovingIntoAcrossAndAlongATrenchCostsOnlyTheDistance(string radius)
    {
        // The sides are crossed as Open terrain (p. 62, U80): no extra cost, any base size, along a bend too.
        var field = Field("24", [Trench("bend", "1", R("4", "4", "6.5", "16"), R("6.5", "4", "18", "6.5"))]);
        var mover = Body("m", "5.25", "20", radius);
        var across = Movement.CheckPath(field, mover, [P("5.25", "10"), P("5.25", "1")], L("19"), []);
        Assert.True(across.IsLegal, across.Text);
        Assert.Equal(L("19"), across.Cost);
        var along = Movement.CheckPath(field, Body("m", "5.25", "15", radius), [P("5.25", "5.25"), P("17", "5.25")], L("21.5"), []);
        Assert.True(along.IsLegal, along.Text);
        Assert.Equal(L("21.5"), along.Cost);
    }

    [Fact]
    public void TrenchesAreCheckedWhenTheyAreMade()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Trench("t", "0.499", R("0", "0", "4", "2")));
        Assert.Throws<ArgumentOutOfRangeException>(() => Trench("t", "3", R("0", "0", "4", "2")));
        Trench("t", "2.999", R("0", "0", "4", "2"));
        Assert.Throws<ArgumentException>(() => new TerrainBlock("t", F("0", "0", "4", "2"), TerrainProfile.Cover) { Depth = L("1") });
        Assert.Throws<ArgumentException>(() => new TerrainBlock("t", F("0", "0", "4", "2"), TerrainProfile.Low, TerrainKind.Impassable, walkable: false) { Depth = L("1") });
        // One depth per battlefield; nothing on top of a trench, but touching is fine (a network).
        Assert.Throws<ArgumentException>(() => Field("24", [Trench("a", "1", R("0", "0", "4", "2")), Trench("b", "1.5", R("4", "0", "8", "2"))]));
        Assert.Throws<ArgumentException>(() => Field("24", [Trench("a", "1", R("0", "0", "4", "2")), Wall("w", "3", "1", "5", "3")]));
        var network = Field("24", [Trench("a", "1", R("0", "0", "4", "2")), Trench("b", "1", R("4", "0", "8", "2")), Wall("w", "0", "2", "4", "3")]);
        Assert.Equal(L("1"), network.TrenchDepth);
        // A base on the seam between two sections is in the trench.
        Assert.Equal("a", Trenches.Of(network, Body("m", "4", "1"))?.Id);
    }

    [Fact]
    public void WithoutTrenchesNothingIsBelowTheGround()
    {
        var field = Field("24", [Wall("w", "10", "0", "11", "24")]);
        Assert.Null(field.TrenchDepth);
        Assert.Null(Trenches.Of(field, Body("a", "5", "5")));
        Assert.DoesNotContain(See(field, Body("a", "5", "5"), Body("b", "15", "5")).Rays, r => r.Obstructing.Contains(HeightSight.GroundId));
    }

    // Line-of-sight review 2026-10-25 (Stage 1): trench-edge cover as U83 has it today, at the depths U80 allows. The
    // trench is the straight one above (y 10 to 12.5). "Rim" is a ground model touching the trench's edge (centre y 9.5);
    // "far" is a ground model 8" back (centre y 2). Targets stand mid-trench (11.25) or against the near wall (10.6).
    [Theory]
    [InlineData("0.5", "9.5", "11.25", Visibility.Full, 9, false, true)]
    [InlineData("0.5", "2", "11.25", Visibility.Partial, 6, true, false)]
    [InlineData("1", "9.5", "11.25", Visibility.Full, 9, false, true)]
    [InlineData("1", "9.5", "10.6", Visibility.Full, 9, false, true)]
    [InlineData("1", "2", "11.25", Visibility.Partial, 3, true, false)]
    [InlineData("2", "9.5", "11.25", Visibility.Full, 9, false, true)]
    [InlineData("2", "9.5", "10.6", Visibility.Partial, 6, true, true)]
    [InlineData("2", "2", "11.25", Visibility.None, 0, false, false)]
    public void TrenchEdgeCoverByDepthAndDistance(string depth, string attackerY, string targetY, Visibility seen, int points, bool cover, bool withinAnInch)
    {
        var field = Straight(depth);
        var attacker = Body("a", "12", attackerY);
        var target = Body("t", "12", targetY);
        var result = Cover.Of(field, attacker, target);
        Assert.Equal((seen, points), (result.Sight.Visibility, result.Sight.Visible));
        Assert.Equal(cover, result.InCover);
        Assert.Equal(cover ? "trench" : null, result.TerrainId);
        Assert.Equal(withinAnInch, Measure.IsWithin(attacker, target, Length.Inch));
    }

    [Fact]
    public void AModelInTheTrenchSeesOutInFullWhileOnlyItsHeadIsSeen()
    {
        // 1" deep: the far shooter sees 3 of the model's points (cover); the model sees all of the shooter, who has no cover.
        var field = Straight();
        var inside = Body("t", "12", "11.25");
        var outside = Body("a", "12", "2");
        Assert.Equal(3, HeightSight.Between(field, outside, inside).Visible);
        var back = Cover.Of(field, inside, outside);
        Assert.Equal(Visibility.Full, back.Sight.Visibility);
        Assert.False(back.InCover);
    }

    [Fact]
    public void AModelExactlyHalfInTheTrenchStandsOnTheGroundWithoutTrenchCover()
    {
        // U81: exactly half in is not in; it stands on the ground, and a trench is never cover to a model on the ground.
        var field = Straight();
        var straddler = Body("t", "12", "10");
        Assert.Null(Trenches.Of(field, straddler));
        var result = Cover.Of(field, Body("a", "12", "2"), straddler);
        Assert.Equal(Visibility.Full, result.Sight.Visibility);
        Assert.False(result.InCover);
    }

    [Fact]
    [Trait("Rule", "OPEN QUESTION T1 (review 2026-10-25): not decided; this pins today's result only")]
    public void T1_AtTheRimOfAVeryDeepTrenchAModelWithinAnInchCannotBeSeen()
    {
        // A 2.9" trench (the deepest U80 allows). The attacker touches the edge from outside; the target hugs the near wall
        // inside. They are within 1" (U82), but the attacker's eye is above its base centre, 0.5" back from the edge, and
        // every ray to the target's points passes through the solid ground at the rim, so there is no Line of Sight and a
        // Melee Attack would be refused (p. 45). No map has a trench deeper than 1".
        var field = Straight("2.9");
        var attacker = Body("a", "12", "9.5");
        var target = Body("t", "12", "10.6");
        Assert.True(Measure.IsWithin(attacker, target, Length.Inch));
        var result = Cover.Of(field, attacker, target);
        Assert.Equal(Visibility.None, result.Sight.Visibility);
        Assert.False(result.InCover);
        Assert.All(result.Sight.Rays, r => Assert.Equal(["trench"], r.Obstructing));
        // Mid-trench the same attacker sees the whole model.
        Assert.Equal(Visibility.Full, HeightSight.Between(field, attacker, Body("m", "12", "11.25")).Visibility);
    }
}
