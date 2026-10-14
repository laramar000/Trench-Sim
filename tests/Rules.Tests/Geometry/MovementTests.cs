using Rules.Core.Geometry;
using static Rules.Tests.Geometry.Geo;

namespace Rules.Tests.Geometry;

public class MovementTests
{
    private static MoveResult Move(Battlefield field, ModelBody mover, string allowance, IEnumerable<Obstacle>? others = null, params string[][] waypoints) =>
        Movement.CheckPath(field, mover, waypoints.Select(w => P(w[0], w[1])).ToList(), L(allowance), others ?? []);

    private static string[] W(string x, string y) => [x, y];

    [Fact]
    [Trait("Rule", "1.0.2 p.35 path no longer than Movement (confirmed)")]
    public void ExactAllowanceIsLegalAndOneThousandthMoreIsNot()
    {
        var mover = Body("m", "2", "2");
        Assert.True(Move(Field(), mover, "6", null, W("8", "2")).IsLegal);
        var refused = Move(Field(), mover, "5.999", null, W("8", "2"));
        Assert.Equal(MoveRefusal.PathTooLong, refused.Reason);
        Assert.Equal(L("6"), refused.Cost);
        Assert.True(Move(Field(), mover, "5", null, W("5", "6")).IsLegal);     // 3-4-5 diagonal, exact
        Assert.False(Move(Field(), mover, "4.999", null, W("5", "6")).IsLegal);
    }

    [Fact]
    [Trait("Rule", "1.0.2 p.35 segments joined by waypoints; U12 for rounding")]
    public void WaypointLegsAreAdded()
    {
        var result = Move(Field(), Body("m", "2", "2"), "7", null, W("5", "2"), W("5", "6"));
        Assert.True(result.IsLegal);
        Assert.Equal(L("7"), result.Cost);
    }

    [Fact]
    [Trait("Rule", "Difficult terrain: every 1\" counts as 2\" (keyword text only, not visually checked); U8 partial crossing")]
    public void DifficultTerrainDoublesOnlyThePartInsideIt()
    {
        var field = Field("24", Rough("rubble", "4", "0", "6", "24"));
        var mover = Body("m", "2", "2");
        Assert.Equal(L("8"), Movement.Cost(field, mover, [P("8", "2")]));   // 2" inside counts 4": 4 + 4
        Assert.Equal(L("4"), Movement.Cost(field, mover, [P("5", "2")]));   // 1" inside: 2 + 2
        Assert.Equal(L("2"), Movement.Cost(field, mover, [P("4", "2")]));   // stops at the edge: no doubling
        Assert.True(Move(field, mover, "8", null, W("8", "2")).IsLegal);
        Assert.Equal(MoveRefusal.PathTooLong, Move(field, mover, "7.999", null, W("8", "2")).Reason);
    }

    [Fact]
    public void OverlappingDifficultPiecesAreNotDoubleCounted()
    {
        var field = Field("24", Rough("a", "4", "0", "6", "24"), Rough("b", "5", "0", "7", "24"));
        Assert.Equal(L("9"), Movement.Cost(field, Body("m", "2", "2"), [P("8", "2")])); // 3" inside (4..7) counts 6: 3 + 6
    }

    [Fact]
    [Trait("Rule", "U12: one deterministic reading of rounding; not a rulebook requirement")]
    public void ManyShortLegsCostTheSameAsOneLongLegToTheThousandth()
    {
        var field = Field();
        var mover = Body("m", "2", "2");

        // 300 legs of (0.001, 0.001): true length 300 * sqrt(2) thousandths = 424.264..., so 0.425".
        var steps = Enumerable.Range(1, 300).Select(i => new Point2(L("2") + Length.FromThousandths(i), L("2") + Length.FromThousandths(i))).ToList();
        var split = Movement.Cost(field, mover, steps);
        var single = Movement.Cost(field, mover, [steps[^1]]);
        Assert.Equal(L("0.425"), split);
        Assert.Equal(single, split);

        // Rounding every leg up to a thousandth (the first version) would have charged 300 * 0.002 = 0.600".
        Assert.NotEqual(L("0.600"), split);
        Assert.True(Movement.CheckPath(field, mover, steps, L("0.425"), []).IsLegal);
        Assert.Equal(MoveRefusal.PathTooLong, Movement.CheckPath(field, mover, steps, L("0.424"), []).Reason);

        // 1,000 legs of a thousandth along an axis are exactly 1".
        var along = Enumerable.Range(1, 1000).Select(i => new Point2(L("2") + Length.FromThousandths(i), L("2"))).ToList();
        Assert.Equal(L("1"), Movement.Cost(field, mover, along));
    }

    [Fact]
    public void SplittingAPathNeverCostsLessThanTheSingleLegAndNeverMuchMore()
    {
        var field = Field();
        var mover = Body("m", "2", "2");
        var rng = new Random(7);
        for (var i = 0; i < 100; i++)
        {
            var end = new Point2(L("2") + Length.FromThousandths(rng.Next(1, 9000)), L("2") + Length.FromThousandths(rng.Next(1, 9000)));
            var parts = rng.Next(2, 20);
            var steps = Enumerable.Range(1, parts)
                .Select(k => new Point2(
                    Length.FromThousandths(2000 + (end.X.Thousandths - 2000) * k / parts),
                    Length.FromThousandths(2000 + (end.Y.Thousandths - 2000) * k / parts)))
                .ToList();
            var single = Movement.Cost(field, mover, [steps[^1]]);
            var split = Movement.Cost(field, mover, steps);
            Assert.True(split >= single - L("0.001"), $"split {split} single {single}");
            Assert.True(split <= single + L("0.001"), $"split {split} single {single}");
        }
    }

    [Fact]
    [Trait("Rule", "1.0.2 p.35 cannot leave the battlefield; p.31 more-than-half")]
    public void WaypointsMustKeepMoreThanHalfTheBaseOnTheTable()
    {
        var mover = Body("m", "2", "2");
        Assert.Equal(MoveRefusal.OffBattlefield, Move(Field(), mover, "10", null, W("-1", "2")).Reason);
        Assert.Equal(MoveRefusal.OffBattlefield, Move(Field(), mover, "10", null, W("0", "2")).Reason);
        Assert.True(Move(Field(), mover, "10", null, W("0.001", "2")).IsLegal);
        Assert.Equal(MoveRefusal.EmptyPath, Move(Field(), mover, "10").Reason);
    }

    [Fact]
    [Trait("Rule", "1.0.2 p.31 impassable terrain (confirmed); U9, U14 for Wall and the 1\" threshold")]
    public void BlockingTerrainStopsTheSweptBaseButWalkableTerrainDoesNot()
    {
        var mover = Body("m", "2", "10.5");
        var wall = Field("24", Wall("w", "5", "0", "6", "10"));
        Assert.True(Move(wall, mover, "10", null, W("8", "10.5")).IsLegal);                       // base just grazes the wall's corner
        Assert.Equal(MoveRefusal.CrossesBlockingTerrain, Move(wall, Body("m", "2", "10.499"), "10", null, W("8", "10.499")).Reason);
        Assert.Equal(MoveRefusal.CrossesBlockingTerrain, Move(wall, Body("m", "2", "2"), "10", null, W("8", "2")).Reason);

        Assert.True(Move(Field("24", Barricade("b", "5", "0", "6", "10")), Body("m", "2", "2"), "6", null, W("8", "2")).IsLegal); // 1" or less is Open (p. 39)
        Assert.Equal(MoveRefusal.CrossesBlockingTerrain, Move(Field("24", Tower("t", "5", "0", "6", "10")), Body("m", "2", "2"), "6", null, W("8", "2")).Reason);
        Assert.Equal(MoveRefusal.CrossesBlockingTerrain, Move(Field("24", new TerrainBlock("i", F("5", "0", "6", "10"), TerrainProfile.Low, TerrainKind.Impassable)), Body("m", "2", "2"), "6", null, W("8", "2")).Reason);
    }

    [Fact]
    [Trait("Rule", "U18: platforms cost nothing to enter or leave; no climbing or falling")]
    public void WalkablePlatformIsCrossedAtOrdinaryCost()
    {
        var field = Field("24", Platform("p", "5", "0", "9", "10"));
        var result = Move(field, Body("m", "2", "2"), "8", null, W("10", "2"));
        Assert.True(result.IsLegal);
        Assert.Equal(L("8"), result.Cost);
    }

    [Fact]
    [Trait("Rule", "p.35 base must pass between terrain: a gap exactly the width of the base is passable")]
    public void GapExactlyTheBaseWidthPassesAndAThousandthLessDoesNot()
    {
        var exact = Field("24", Wall("a", "5", "0", "6", "9"), Wall("b", "5", "10", "6", "24"));
        Assert.True(Move(exact, Body("m", "2", "9.5"), "10", null, W("8", "9.5")).IsLegal);
        var narrow = Field("24", Wall("a", "5", "0", "6", "9"), Wall("b", "5", "9.999", "6", "24"));
        Assert.Equal(MoveRefusal.CrossesBlockingTerrain, Move(narrow, Body("m", "2", "9.5"), "10", null, W("8", "9.5")).Reason);
    }

    [Fact]
    [Trait("Rule", "1.0.2 p.35 not through enemy bases; no moving within 1\" of an enemy unless charging (confirmed)")]
    public void EnemyBasesAndTheOneInchZone()
    {
        var mover = Body("m", "2", "2");
        var enemy = Body("e", "5", "2");
        var through = Move(Field(), mover, "10", [Enemy(enemy)], W("8", "2"));
        Assert.Equal(MoveRefusal.CrossesEnemyBase, through.Reason);
        Assert.Equal("e", through.SubjectId);

        // Passing with bases exactly touching is not through the base, but it is inside the 1" zone.
        var graze = Move(Field(), mover, "10", [Enemy(Body("e", "5", "3"))], W("8", "2"));
        Assert.Equal(MoveRefusal.EntersEnemyZone, graze.Reason);

        // Ending with exactly 1" between bases is within 1" ("within" includes 1.000); a thousandth further is outside.
        var zoneEnemy = Body("e", "8", "2");
        Assert.Equal(MoveRefusal.EntersEnemyZone, Move(Field(), mover, "10", [Enemy(zoneEnemy)], W("6", "2")).Reason);
        Assert.True(Move(Field(), mover, "10", [Enemy(zoneEnemy)], W("5.999", "2")).IsLegal);
        Assert.True(Move(Field(), mover, "10", [Enemy(Body("e", "5", "3.001"))], W("8", "2")).IsLegal == false); // still within 1" of the path
        Assert.True(Move(Field(), mover, "10", [Enemy(Body("e", "5", "4.001"))], W("8", "2")).IsLegal);
    }

    [Fact]
    [Trait("Rule", "1.0.2 p.35 a model starting within 1\" of an enemy must stay within it (or retreat, not in M3)")]
    public void StartingWithinOneInchOfAnEnemyMeansStayingWithinIt()
    {
        var mover = Body("m", "2", "2");
        var enemy = Enemy(Body("e", "3.5", "2"));
        Assert.True(Move(Field(), mover, "5", [enemy], W("2", "1.5")).IsLegal);
        var left = Move(Field(), mover, "5", [enemy], W("0.5", "2"));
        Assert.Equal(MoveRefusal.LeavesEnemyZone, left.Reason);
    }

    [Fact]
    [Trait("Rule", "1.0.2 p.35 friendly models can be passed if fully cleared; U10 for the software reading")]
    public void FriendlyBasesCanBePassedButNotEndedOn()
    {
        var mover = Body("m", "2", "2");
        var friend = Friend(Body("f", "5", "2"));
        Assert.True(Move(Field(), mover, "10", [friend], W("8", "2")).IsLegal);
        Assert.Equal(MoveRefusal.EndsOnBase, Move(Field(), mover, "10", [friend], W("5.5", "2")).Reason);
        Assert.True(Move(Field(), mover, "10", [friend], W("6", "2")).IsLegal); // touching, not overlapping
    }

    [Fact]
    [Trait("Rule", "Determinism: input order never changes the answer")]
    public void ResultsDoNotDependOnTheOrderOfTerrainOrObstacles()
    {
        var terrain = new[]
        {
            Wall("w1", "10", "0", "11", "5"), Wall("w2", "10", "7", "11", "24"), Rough("r1", "4", "0", "6", "24"),
            Barricade("b1", "14", "0", "15", "24"), Platform("p1", "18", "0", "22", "6"),
        };
        var others = new[] { Enemy(Body("e1", "16", "10")), Friend(Body("f1", "8", "2")), Enemy(Body("e2", "9", "20")), Friend(Body("f2", "12", "6")) };
        var mover = Body("m", "2", "2");
        var paths = new[]
        {
            new[] { P("8", "2"), P("12", "6") }, new[] { P("20", "3") }, new[] { P("9.5", "6") }, new[] { P("3", "3"), P("8", "9"), P("12", "6") },
        };
        foreach (var path in paths)
        {
            var baseline = Movement.CheckPath(new Battlefield(L("24"), L("24"), terrain, []), mover, path, L("30"), others);
            for (var seed = 0; seed < 25; seed++)
            {
                var field = new Battlefield(L("24"), L("24"), Shuffled(terrain, seed), []);
                Assert.Equal(baseline, Movement.CheckPath(field, mover, path, L("30"), Shuffled(others, seed + 100)));
            }
        }
    }

    [Fact]
    public void LargestTableCostsDoNotOverflow()
    {
        var field = new Battlefield(L("200"), L("200"), [Rough("r", "0", "0", "200", "200")], []);
        var mover = Body("m", "0.5", "0.5");
        var cost = Movement.Cost(field, mover, [P("199.5", "199.5")]);
        var expected = (long)Math.Ceiling(Math.Sqrt(2.0) * 199_000 * 2); // test-side reference: whole table is Difficult, so double
        Assert.InRange(cost.Thousandths, expected - 1, expected + 1);
        var zigzag = Enumerable.Range(0, 100).Select(i => i % 2 == 0 ? P("199.5", "0.5") : P("0.5", "199.5")).ToList();
        Assert.True(Movement.Cost(Field("200"), mover, zigzag).Thousandths > 0);
    }
}
