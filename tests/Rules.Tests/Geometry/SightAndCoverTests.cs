using Rules.Core.Geometry;
using static Rules.Tests.Geometry.Geo;

namespace Rules.Tests.Geometry;

internal sealed class FixedSampler(params Point2[] points) : ISightSampler
{
    public IReadOnlyList<Point2> Sample(ModelBody attacker, ModelBody target) => points;
}

public class ElevationTests
{
    [Fact]
    [Trait("Rule", "1.0.2 p.38 'in terrain' = more than half the base (confirmed); high ground classification U17 provisional")]
    public void HighGroundNeedsMoreThanHalfTheBaseOnAWalkablePlatform()
    {
        var field = Field("24", Platform("p", "10", "10", "14", "14"));
        Assert.Equal(new ModelPosition(ModelLevel.High, "p"), Elevation.Of(field, Body("m", "12", "12")));
        Assert.Equal(new ModelPosition(ModelLevel.High, "p"), Elevation.Of(field, Body("m", "10.001", "12")));
        Assert.Equal(ModelPosition.OnGround, Elevation.Of(field, Body("m", "10", "12")));      // exactly half
        Assert.Equal(ModelPosition.OnGround, Elevation.Of(field, Body("m", "9", "12")));
        Assert.Equal(new ModelPosition(ModelLevel.High, "p"), Elevation.Of(field, Body("m", "13.999", "12")));
        Assert.Equal(ModelPosition.OnGround, Elevation.Of(field, Body("m", "14", "12")));
    }

    [Fact]
    [Trait("Rule", "U17")]
    public void TowersBarricadesAndWallsNeverGiveHighGround()
    {
        var field = Field("24", Tower("t", "2", "2", "6", "6"), Barricade("b", "8", "2", "10", "4"), Wall("w", "12", "2", "14", "4"));
        Assert.All(new[] { "4,4", "9,3", "13,3" }, c =>
        {
            var xy = c.Split(',');
            Assert.Equal(ModelPosition.OnGround, Elevation.Of(field, Body("m", xy[0], xy[1])));
        });
    }

    [Fact]
    [Trait("Rule", "U17: corner case needs the area test, not the centre")]
    public void PlatformCornerUsesTheAreaTest()
    {
        var field = Field("24", Platform("p", "10", "10", "14", "14"));
        Assert.Equal(AreaDecision.MoreThanHalf, field.Terrain[0].Footprint.MoreThanHalfInside(P("10.172", "10.172"), L("0.5")));
        Assert.Equal(ModelPosition.OnGround, Elevation.Of(field, Body("m", "10.17", "10.17")));
        Assert.Equal(ModelLevel.High, Elevation.Of(field, Body("m", "10.172", "10.172")).Level);
        Assert.Equal(ModelPosition.OnGround, Elevation.Of(field, Body("m", "10.171", "10.171"))); // too close to call counts as ground
    }
}

public class SightRulesTests
{
    private static readonly ModelPosition Ground = ModelPosition.OnGround;
    private static readonly ModelPosition OnP = new(ModelLevel.High, "p");
    private static readonly ModelPosition OnQ = new(ModelLevel.High, "q");

    [Fact]
    [Trait("Rule", "Revision 3 section 10.4: the single obstruction rule; provisional U19-U21")]
    public void TruthTable()
    {
        var low = new TerrainBlock("low", F("1", "1", "2", "2"), TerrainProfile.Low);
        var cover = Barricade("cover", "1", "1", "2", "2");
        var ground = Wall("ground", "1", "1", "2", "2");
        var p = Platform("p", "1", "1", "2", "2");
        var tower = Tower("tower", "1", "1", "2", "2");

        // Low and Cover never obstruct, whatever the levels.
        foreach (var piece in new[] { low, cover })
        {
            foreach (var (a, t) in new[] { (Ground, Ground), (OnP, Ground), (Ground, OnP), (OnP, OnQ) })
            {
                Assert.False(SightRules.Obstructs(piece, a, t));
            }
        }

        // A ground Wall obstructs unless the attacker has high ground (U20); the target's level changes nothing (U21).
        Assert.True(SightRules.Obstructs(ground, Ground, Ground));
        Assert.True(SightRules.Obstructs(ground, Ground, OnP));
        Assert.False(SightRules.Obstructs(ground, OnP, Ground));
        Assert.False(SightRules.Obstructs(ground, OnP, OnQ));

        // A platform never obstructs its own occupant, in either direction (U19); other platforms do.
        Assert.False(SightRules.Obstructs(p, OnP, Ground));
        Assert.False(SightRules.Obstructs(p, Ground, OnP));
        Assert.False(SightRules.Obstructs(p, OnP, OnP));
        Assert.True(SightRules.Obstructs(p, Ground, Ground));
        Assert.True(SightRules.Obstructs(p, OnQ, Ground));
        Assert.True(SightRules.Obstructs(p, Ground, OnQ));
        Assert.True(SightRules.Obstructs(p, OnQ, OnQ));

        // A tower is High but nobody stands on it: it obstructs everyone, high-ground attackers included.
        foreach (var (a, t) in new[] { (Ground, Ground), (OnP, Ground), (Ground, OnP), (OnP, OnQ) })
        {
            Assert.True(SightRules.Obstructs(tower, a, t));
        }
    }
}

public class LineOfSightTests
{
    [Fact]
    [Trait("Rule", "1.0.2 p.30 see any part of the target; partial line of sight (confirmed); sampling U2/U16 provisional")]
    public void FullPartialAndNone()
    {
        var attacker = Body("a", "4", "12");
        var target = Body("t", "10", "12");
        Assert.Equal(Visibility.Full, LineOfSight.Between(Field(), attacker, target).Visibility);

        var wide = LineOfSight.Between(Field("24", Wall("w", "6", "10", "7", "14")), attacker, target);
        Assert.Equal(Visibility.None, wide.Visibility);
        Assert.Equal(0, wide.Visible);

        var slit = LineOfSight.Between(Field("24", Wall("w", "6", "11.9", "7", "12.1")), attacker, target);
        Assert.Equal(Visibility.Partial, slit.Visibility);
        Assert.Equal((2, 4), (slit.Visible, slit.Total));
        Assert.Equal("w", slit.Rays[0].BlockedBy);
    }

    [Fact]
    [Trait("Rule", "U13 grazing does not block")]
    public void TouchingAnEdgeOrCornerDoesNotBlock()
    {
        var attacker = Body("a", "4", "12");
        var target = Body("t", "10", "12");
        // A wall whose lower edge lies along the central rays: those rays graze it, the upper rays cross it.
        var edge = LineOfSight.Between(Field("24", Wall("w", "6", "12", "7", "14")), attacker, target);
        Assert.Equal(Visibility.Partial, edge.Visibility);
        Assert.Equal((3, 4), (edge.Visible, edge.Total));

        // A ray that touches only a corner: (4,12) to (10,18) meets the wall at (6,14) and leaves.
        var corner = LineOfSight.Between(Field("24", Wall("w", "6", "13", "7", "14")), attacker, target, new FixedSampler(P("10", "18")));
        Assert.Equal(Visibility.Full, corner.Visibility);
        Assert.Empty(corner.Rays[0].Crossed);
        // One thousandth further in and it crosses.
        var crossing = LineOfSight.Between(Field("24", Wall("w", "6", "13", "7", "14.001")), attacker, target, new FixedSampler(P("10", "18")));
        Assert.Equal(Visibility.None, crossing.Visibility);
    }

    [Fact]
    [Trait("Rule", "U16 sampling is replaceable")]
    public void AnotherSamplerChangesTheOutcomeWithoutChangingTheRest()
    {
        var field = Field("24", Wall("w", "6", "11.9", "7", "12.1"));
        var attacker = Body("a", "4", "12");
        var target = Body("t", "10", "12");
        Assert.Equal(Visibility.Full, LineOfSight.Between(field, attacker, target, new FixedSampler(P("10", "12.5"))).Visibility);
        Assert.Equal(Visibility.None, LineOfSight.Between(field, attacker, target, new FixedSampler(P("10", "12"))).Visibility);
    }

    [Fact]
    [Trait("Rule", "U19 own platform does not obstruct, both directions; provisional abstraction")]
    public void ThePlatformADefenderOrShooterStandsOnNeverBlocks()
    {
        var field = Field("24", Platform("p", "10", "10", "14", "14"));
        var onRoof = Body("r", "12", "12");
        var onGround = Body("g", "18", "12");

        var down = LineOfSight.Between(field, onRoof, onGround);   // attacker on the platform
        Assert.Equal(Visibility.Full, down.Visibility);
        Assert.All(down.Rays, ray => Assert.Contains("p", ray.Crossed)); // the rays do pass through it ...
        Assert.All(down.Rays, ray => Assert.Empty(ray.Obstructing));   // ... but it does not obstruct

        var up = LineOfSight.Between(field, onGround, onRoof);     // target on the platform
        Assert.Equal(Visibility.Full, up.Visibility);
        Assert.All(up.Rays, ray => Assert.Contains("p", ray.Crossed));
        Assert.All(up.Rays, ray => Assert.Empty(ray.Obstructing));
    }

    [Fact]
    [Trait("Rule", "U19 only the occupant's own platform is exempt")]
    public void AnotherPlatformBetweenTheModelsStillBlocks()
    {
        var field = Field("24", Platform("p", "10", "10", "14", "14"), Platform("q", "15", "10", "17", "14"));
        var result = LineOfSight.Between(field, Body("g", "20", "12"), Body("r", "12", "12"));
        Assert.Equal(Visibility.None, result.Visibility);
        Assert.All(result.Rays, ray => Assert.Equal("q", ray.BlockedBy));
    }

    [Fact]
    [Trait("Rule", "U20 provisional XCOM-style abstraction; U21 asymmetry")]
    public void HighGroundAttackersSeeOverGroundWallsButNotTheOtherWayAround()
    {
        var field = Field("24", Platform("p", "2", "10", "5", "14"), Platform("q", "10", "10", "14", "14"), Wall("w", "6", "8", "7", "16"));
        var shooterOnP = Body("a", "3.5", "12");
        var groundBeyondWall = Body("g", "9", "12");
        var targetOnQ = Body("r", "12", "12");

        Assert.Equal(Visibility.Full, LineOfSight.Between(field, shooterOnP, groundBeyondWall).Visibility);  // high -> ground: over the wall
        Assert.Equal(Visibility.Full, LineOfSight.Between(field, shooterOnP, targetOnQ).Visibility);         // high -> high
        Assert.Equal(Visibility.None, LineOfSight.Between(field, groundBeyondWall, shooterOnP).Visibility);  // ground -> high: wall blocks
        Assert.Equal(Visibility.None, LineOfSight.Between(field, Body("g3", "5.5", "12"), Body("x", "9", "12")).Visibility); // ground -> ground blocked
    }

    [Fact]
    [Trait("Rule", "U20: towers are High, so high-ground attackers are still blocked")]
    public void TowersBlockHighGroundAttackers()
    {
        var field = Field("24", Platform("p", "2", "10", "5", "14"), Tower("t", "8", "6", "11", "18"));
        Assert.Equal(Visibility.None, LineOfSight.Between(field, Body("a", "3.5", "12"), Body("g", "14", "12")).Visibility);
    }

    [Fact]
    [Trait("Rule", "U3 other models never block")]
    public void TheApiTakesNoOtherModelsSoTheyCannotBlock()
    {
        var parameters = typeof(LineOfSight).GetMethod(nameof(LineOfSight.Between))!.GetParameters().Select(p => p.ParameterType).ToArray();
        Assert.Equal([typeof(Battlefield), typeof(ModelBody), typeof(ModelBody), typeof(ISightSampler)], parameters);
    }
}

public class CoverTests
{
    [Fact]
    [Trait("Rule", "1.0.2 p.38 cover: wide as base, touching, between, partially blocks (confirmed wording); readings U4, U5, U14, U15 provisional")]
    public void BarricadeTheTargetStandsAgainstGivesCoverWithoutBlockingSight()
    {
        var field = Field("24", Barricade("b", "6", "11.5", "7", "12.5"));
        var result = Cover.Of(field, Body("a", "3", "12"), Body("t", "7.5", "12"));
        Assert.Equal(Visibility.Full, result.Sight.Visibility);
        Assert.True(result.InCover);
        Assert.Equal("b", result.TerrainId);
    }

    [Fact]
    [Trait("Rule", "p.38 as wide as its base (U4)")]
    public void WidthEqualToTheBaseQualifiesAndAThousandthLessDoesNot()
    {
        var attacker = Body("a", "3", "12");
        var target = Body("t", "7.5", "12");
        Assert.True(Cover.Of(Field("24", Barricade("b", "6", "11.5", "7", "12.5")), attacker, target).InCover);
        Assert.False(Cover.Of(Field("24", Barricade("b", "6", "11.5", "7", "12.499")), attacker, target).InCover);
    }

    [Fact]
    [Trait("Rule", "p.38 at least 1/2\" high: carried by the profile (U14)")]
    public void LowTerrainNeverGivesCover()
    {
        var field = Field("24", new TerrainBlock("low", F("6", "11.5", "7", "12.5"), TerrainProfile.Low));
        Assert.False(Cover.Of(field, Body("a", "3", "12"), Body("t", "7.5", "12")).InCover);
    }

    [Fact]
    [Trait("Rule", "p.38 on or in contact with the terrain (U15)")]
    public void TheTargetMustTouchThePiece()
    {
        var field = Field("24", Barricade("b", "5", "11.5", "6", "12.5"));
        var attacker = Body("a", "3", "12");
        Assert.False(Cover.Of(field, attacker, Body("t", "7.5", "12")).InCover);     // 1" gap
        Assert.True(Cover.Of(field, attacker, Body("t", "6.5", "12")).InCover);      // touching
        Assert.True(Cover.Of(field, attacker, Body("t", "5.8", "12")).InCover);      // overlapping
    }

    [Fact]
    [Trait("Rule", "p.38 the piece must be between the models")]
    public void APieceBehindTheTargetOrBesideTheLineGivesNoCover()
    {
        var attacker = Body("a", "3", "12");
        var target = Body("t", "7.5", "12");
        Assert.False(Cover.Of(Field("24", Barricade("b", "8", "11", "9", "13")), attacker, target).InCover);      // behind
        Assert.False(Cover.Of(Field("24", Barricade("b", "6", "13.5", "7", "15")), attacker, target).InCover);    // beside
    }

    [Fact]
    [Trait("Rule", "Cover is separate from line of sight (Revision 3)")]
    public void ABlockingWallThatDoesNotTouchTheTargetGivesPartialSightButNoCover()
    {
        var result = Cover.Of(Field("24", Wall("w", "6", "11.9", "7", "12.1")), Body("a", "4", "12"), Body("t", "10", "12"));
        Assert.Equal(Visibility.Partial, result.Sight.Visibility);
        Assert.False(result.InCover);
    }

    [Fact]
    public void NoLineOfSightMeansNoCover()
    {
        var result = Cover.Of(Field("24", Wall("w", "6", "10", "7", "14")), Body("a", "4", "12"), Body("t", "10", "12"));
        Assert.Equal(Visibility.None, result.Sight.Visibility);
        Assert.False(result.InCover);
    }

    // A wall beside the target seen at an angle: some rays reach the target, some are blocked.
    private static Battlefield CornerScene(params TerrainBlock[] extra) =>
        new Battlefield(L("24"), L("24"), extra.Append(Wall("w", "6", "12", "9", "16")), []);

    [Fact]
    [Trait("Rule", "p.38 partially blocks (U5)")]
    public void AWallCornerGivesPartialSightAndCover()
    {
        var result = Cover.Of(CornerScene(), Body("a", "3", "14"), Body("t", "7.5", "11.5"));
        Assert.Equal(Visibility.Partial, result.Sight.Visibility);
        Assert.Equal((1, 5), (result.Sight.Visible, result.Sight.Total));
        Assert.True(result.InCover);
        Assert.Equal("w", result.TerrainId);
    }

    [Fact]
    [Trait("Rule", "U20, U22: an ignored ground Wall neither blocks nor gives cover to a high-ground attacker")]
    public void HighGroundAttackerGetsNoObstructionAndNoCoverFromAGroundWall()
    {
        var field = CornerScene(Platform("p", "2", "13", "4", "15"));
        var attacker = Body("a", "3", "14");
        var target = Body("t", "7.5", "11.5");
        Assert.Equal(ModelLevel.High, Elevation.Of(field, attacker).Level);
        var result = Cover.Of(field, attacker, target);
        Assert.Equal(Visibility.Full, result.Sight.Visibility);
        Assert.All(result.Sight.Rays, r => Assert.Empty(r.Obstructing));
        Assert.Contains(result.Sight.Rays, r => r.Crossed.Contains("w")); // the rays do cross the wall; it just is not an obstruction
        Assert.False(result.InCover);
        Assert.Null(result.TerrainId);
    }

    [Fact]
    [Trait("Rule", "U23: Cover-profile pieces give cover at either level")]
    public void CoverPiecesStillGiveCoverAgainstHighGroundAttackers()
    {
        var field = Field("24", Platform("p", "2", "10", "5", "14"), Barricade("b", "6", "11.5", "7", "12.5"));
        var result = Cover.Of(field, Body("a", "3.5", "12"), Body("t", "7.5", "12"));
        Assert.Equal(ModelLevel.High, result.Sight.Attacker.Level);
        Assert.True(result.InCover);
        Assert.Equal("b", result.TerrainId);
    }

    [Fact]
    [Trait("Rule", "U19 a platform is not cover for its own occupant")]
    public void AnOccupantDoesNotGetCoverFromItsOwnPlatform()
    {
        var field = Field("24", Platform("p", "10", "10", "14", "14"));
        var result = Cover.Of(field, Body("g", "18", "12"), Body("r", "12", "12"));
        Assert.Equal(Visibility.Full, result.Sight.Visibility);
        Assert.False(result.InCover);
    }

    [Fact]
    [Trait("Rule", "U5: lowest id is reported when several pieces qualify")]
    public void LowestIdWinsWhateverTheInputOrder()
    {
        var pieces = new[] { Barricade("zeta", "6", "11.5", "7", "12.5"), Barricade("alpha", "6", "11.5", "7", "12.5"), Barricade("mid", "6", "11.5", "7", "12.5") };
        for (var seed = 0; seed < 10; seed++)
        {
            var field = new Battlefield(L("24"), L("24"), Shuffled(pieces, seed), []);
            Assert.Equal("alpha", Cover.Of(field, Body("a", "3", "12"), Body("t", "7.5", "12")).TerrainId);
        }
    }
}

public class SightConsistencyAndDeterminismTests
{
    private static Battlefield RandomField(Random rng)
    {
        var pieces = new List<TerrainBlock>();
        var count = rng.Next(1, 7);
        for (var i = 0; i < count; i++)
        {
            var x = rng.Next(1, 20);
            var y = rng.Next(1, 20);
            var w = rng.Next(1, 4);
            var h = rng.Next(1, 4);
            var f = Footprint.Of(new Rect(P(x.ToString(), y.ToString()), P((x + w).ToString(), (y + h).ToString())));
            pieces.Add(rng.Next(5) switch
            {
                0 => new TerrainBlock($"t{i}", f, TerrainProfile.Low),
                1 => new TerrainBlock($"t{i}", f, TerrainProfile.Cover),
                2 => new TerrainBlock($"t{i}", f, TerrainProfile.Wall),
                3 => new TerrainBlock($"t{i}", f, TerrainProfile.Wall, TerrainKind.Open, SurfaceLevel.High),
                _ => new TerrainBlock($"t{i}", f, TerrainProfile.Wall, TerrainKind.Open, SurfaceLevel.High, false),
            });
        }
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                return new Battlefield(L("24"), L("24"), pieces, []);
            }
            catch (ArgumentException)
            {
                pieces.RemoveAt(pieces.Count - 1); // drop a piece that made two platforms overlap
            }
        }
        return Field();
    }

    [Fact]
    [Trait("Rule", "Revision 3 section 10.4: line of sight and cover cannot contradict each other")]
    public void CoverAndLineOfSightAgreeOnRandomScenes()
    {
        var rng = new Random(1234);
        var covered = 0;
        var partial = 0;
        for (var scene = 0; scene < 400; scene++)
        {
            var field = RandomField(rng);
            var attacker = new ModelBody("a", P(rng.Next(1, 23).ToString(), rng.Next(1, 23).ToString()), L("0.5"));
            var target = new ModelBody("t", new Point2(Length.FromThousandths(rng.Next(1000, 23000)), Length.FromThousandths(rng.Next(1000, 23000))), L("0.5"));
            if (Measure.Overlap(attacker, target))
            {
                continue;
            }
            var sight = LineOfSight.Between(field, attacker, target);
            var cover = Cover.Of(field, attacker, target);

            Assert.Equal(sight.Visibility, cover.Sight.Visibility);
            Assert.Equal((sight.Visible, sight.Total), (cover.Sight.Visible, cover.Sight.Total));
            Assert.Equal(Elevation.Of(field, attacker), sight.Attacker);
            Assert.Equal(Elevation.Of(field, target), sight.Target);
            partial += sight.Visibility == Visibility.Partial ? 1 : 0;

            foreach (var ray in sight.Rays)
            {
                foreach (var id in ray.Crossed)
                {
                    var piece = field.Terrain.Single(t => t.Id == id);
                    Assert.Equal(SightRules.Obstructs(piece, sight.Attacker, sight.Target), ray.Obstructing.Contains(id));
                }
                if (sight.Attacker.Level == ModelLevel.High)
                {
                    Assert.DoesNotContain(ray.Obstructing, id => field.Terrain.Single(t => t.Id == id).Surface == SurfaceLevel.Ground);
                }
            }

            if (sight.Visibility == Visibility.None)
            {
                Assert.False(cover.InCover);
            }
            if (cover.InCover)
            {
                covered++;
                var piece = field.Terrain.Single(t => t.Id == cover.TerrainId);
                Assert.NotEqual(TerrainProfile.Low, piece.Profile);
                Assert.True(piece.Footprint.CircleTouches(target.Centre, target.Radius));
                Assert.True(piece.Profile == TerrainProfile.Cover
                    ? sight.Rays.Any(r => r.Crossed.Contains(piece.Id))
                    : sight.Rays.Any(r => r.Obstructing.Contains(piece.Id)));
            }
        }
        Assert.True(covered > 0 && partial > 0, "the random scenes should exercise cover and partial sight");
    }

    [Fact]
    [Trait("Rule", "Determinism: input order never changes sight or cover")]
    public void TerrainOrderNeverChangesTheResult()
    {
        var terrain = new[]
        {
            Wall("w1", "6", "12", "9", "16"), Barricade("b1", "6", "8", "7", "10"), Platform("p1", "2", "13", "4", "15"), Tower("t1", "14", "2", "16", "20"),
            Wall("w2", "11", "11", "12", "13"),
        };
        var pairs = new[]
        {
            (Body("a", "3", "14"), Body("t", "7.5", "11.5")), (Body("a", "3", "9"), Body("t", "7.5", "9")), (Body("a", "10", "12"), Body("t", "13", "12")), (Body("a", "3", "14"), Body("t", "20", "10")),
        };
        foreach (var (attacker, target) in pairs)
        {
            var baseline = Cover.Of(new Battlefield(L("24"), L("24"), terrain, []), attacker, target);
            for (var seed = 0; seed < 25; seed++)
            {
                var result = Cover.Of(new Battlefield(L("24"), L("24"), Shuffled(terrain, seed), []), attacker, target);
                Assert.Equal((baseline.InCover, baseline.TerrainId, baseline.Sight.Visibility, baseline.Sight.Visible, baseline.Sight.Total),
                    (result.InCover, result.TerrainId, result.Sight.Visibility, result.Sight.Visible, result.Sight.Total));
                Assert.Equal(baseline.Sight.Rays.Select(r => (r.To, string.Join(",", r.Crossed), string.Join(",", r.Obstructing))),
                    result.Sight.Rays.Select(r => (r.To, string.Join(",", r.Crossed), string.Join(",", r.Obstructing))));
            }
        }
    }

    [Fact]
    public void SamePositionsGiveTheSameAnswerEveryTime()
    {
        var field = Field("24", Wall("w", "6", "12", "9", "16"));
        var first = Cover.Of(field, Body("a", "3", "14"), Body("t", "7.5", "11.5"));
        for (var i = 0; i < 10; i++)
        {
            var again = Cover.Of(field, Body("a", "3", "14"), Body("t", "7.5", "11.5"));
            Assert.Equal((first.InCover, first.Sight.Visible, first.Sight.Total), (again.InCover, again.Sight.Visible, again.Sight.Total));
        }
    }

    [Fact]
    public void ExtremeCoordinatesDoNotOverflowInSight()
    {
        var field = new Battlefield(L("200"), L("200"), [Wall("w", "100", "0", "101", "200")], []);
        var result = LineOfSight.Between(field, Body("a", "0.5", "100"), Body("t", "199.5", "100"));
        Assert.Equal(Visibility.None, result.Visibility);
        Assert.Equal(Visibility.Full, LineOfSight.Between(field, Body("a", "0.5", "100"), Body("t", "99", "199.5")).Visibility);
    }
}
