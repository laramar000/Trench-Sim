using System.Collections.Immutable;
using Rules.Core.Geometry;
using static Rules.Tests.Geometry.Geo;

namespace Rules.Tests.Geometry;

// The charge planner of the follow-up to M4 Slice B: the shortest available route (Comprehensive Rules p. 37,
// "the model must move by the shortest route available that takes it directly towards the target ... the route you
// use to avoid the obstacles is as short as possible"), interposition judged on that route (p. 36), and friends
// crossed only when the model can move past them entirely (p. 35). Routes are compared with ReferenceRouter, a
// floating-point brute-force search that shares no code with Rules.Core.
public class ChargeRoutingTests
{
    private static readonly Length Twelve = L("12");

    private static double Inches(Length length) => length.Thousandths / 1000.0;

    private static double PathLength(ModelBody mover, ImmutableArray<Point2> path)
    {
        var total = 0.0;
        var from = mover.Centre;
        foreach (var point in path)
        {
            total += Math.Sqrt(Math.Pow(Inches(point.X - from.X), 2) + Math.Pow(Inches(point.Y - from.Y), 2));
            from = point;
        }
        return total;
    }

    // The plan is legal by the exact movement rules, never overlaps the target and is no longer than the allowance.
    private static void AssertSound(Battlefield field, ModelBody mover, ModelBody target, Length reach, IReadOnlyList<Obstacle> others, ChargePlan plan)
    {
        if (plan.Path.IsEmpty)
        {
            return;
        }
        Assert.True(plan.Path.Length <= Charging.MaxLegs);
        Assert.True(plan.Cost <= reach, $"cost {plan.Cost} exceeds reach {reach}");
        var result = Movement.CheckPath(field, mover, plan.Path, reach, others, target.Id);
        Assert.True(result.IsLegal, result.Text);
        Assert.Equal(result.Cost, plan.Cost);
        var end = mover.At(plan.Path[^1]);
        Assert.False(Measure.Overlap(end, target));
        Assert.Equal(Measure.IsWithin(end, target, Length.Inch), plan.Reached);
    }

    // A planner route is the shortest to within the polygon approximation (never below the true shortest, a
    // fraction of a percent above it).
    private static void AssertShortest(ModelBody mover, ModelBody target, IReadOnlyList<ReferenceRouter.Shape> shapes, ChargePlan plan, string scene = "")
    {
        Assert.True(plan.Reached, scene);
        var expected = ReferenceRouter.ShortestToContact(
            Inches(mover.Centre.X), Inches(mover.Centre.Y), shapes,
            Inches(target.Centre.X), Inches(target.Centre.Y), Inches(mover.Radius + target.Radius));
        var actual = Inches(plan.Cost);
        Assert.True(double.IsFinite(expected), "the reference found no route");
        Assert.True(actual >= expected - 0.003, $"planner {actual} is shorter than the brute-force {expected} {scene}");
        Assert.True(actual <= expected * 1.006 + 0.01, $"planner {actual} is longer than the brute-force {expected} {scene} path {string.Join(" ", plan.Path)}");
    }

    // ---- 1. unobstructed ground keeps the direct route ----

    [Fact]
    [Trait("Rule", "Charge Moves p. 37: on open ground the shortest route is the straight line")]
    public void AnUnobstructedChargeKeepsItsDirectRoute()
    {
        var field = Field("36");
        var mover = Body("m", "4", "6");
        var target = Body("t", "12", "6.5");
        var others = new[] { Enemy(target) };
        var plan = Charging.Plan(field, mover, target, Twelve, others);

        Assert.True(plan.Reached);
        Assert.Equal(2, plan.Path.Length);   // up to the target's 1", then the tail into contact
        foreach (var point in plan.Path)
        {
            var cross = (point.X.Thousandths - 4000L) * 500 - (point.Y.Thousandths - 6000L) * 8000;
            Assert.True(Math.Abs(cross) < 8000 * 2, $"{point} is off the line");
        }
        AssertSound(field, mover, target, Twelve, others, plan);
    }

    [Fact]
    [Trait("Rule", "Charge Moves p. 37: terrain and models that are not in the way change nothing")]
    public void ThingsThatAreNotInTheWayLeaveTheDirectPlanUntouched()
    {
        var mover = Body("m", "4", "6");
        var target = Body("t", "12", "6");
        var bare = Charging.Plan(Field("36"), mover, target, Twelve, [Enemy(target)]);

        var field = Field("36", Wall("w1", "7", "12", "8", "16"), Wall("w2", "20", "2", "22", "6"));
        var others = new[] { Enemy(target), Friend(Body("f", "6", "11")), Enemy(Body("far", "30", "30")) };
        var crowded = Charging.Plan(field, mover, target, Twelve, others);

        Assert.True(bare.Path.SequenceEqual(crowded.Path));
        Assert.Equal(bare.Cost, crowded.Cost);
        Assert.Equal(bare.Reached, crowded.Reached);
    }

    // ---- 2. terrain ----

    [Fact]
    [Trait("Rule", "Charge Moves p. 37: the route avoids terrain that cannot be crossed, as short as possible")]
    public void AWallInTheWayIsGoneRoundByTheShortestRoute()
    {
        var wall = Wall("w", "7", "3.5", "8", "9.5");
        var field = Field("36", wall);
        var mover = Body("m", "4", "6");
        var target = Body("t", "12", "6");
        var others = new[] { Enemy(target) };

        var route = Charging.CheckRoute(field, mover, target, others);
        Assert.True(route.IsLegal, route.Text);

        var plan = Charging.Plan(field, mover, target, Twelve, others);
        Assert.True(plan.Path.Length > 2, "the route must bend round the wall");
        AssertSound(field, mover, target, Twelve, others, plan);
        AssertShortest(mover, target, [ReferenceRouter.RoundedRect(7, 3.5, 8, 9.5, 0.5)], plan);
        // Longer than the straight 7", shorter than the 12" a charge can cover.
        Assert.InRange(Inches(plan.Cost), 7.2, 11.9);
    }

    [Fact]
    [Trait("Rule", "Charge Moves p. 37: a gap between two walls is used when the base fits through it")]
    public void ARouteGoesThroughAGapThatTheBaseFitsThrough()
    {
        var w1 = Wall("w1", "7", "1", "8", "5");
        var w2 = Wall("w2", "7", "7", "8", "14");   // a 2" gap on a 1" base
        var field = Field("36", w1, w2);
        var mover = Body("m", "4", "4");
        var target = Body("t", "12", "4");
        var others = new[] { Enemy(target) };

        var plan = Charging.Plan(field, mover, target, Twelve, others);
        AssertSound(field, mover, target, Twelve, others, plan);
        AssertShortest(mover, target, [ReferenceRouter.RoundedRect(7, 1, 8, 5, 0.5), ReferenceRouter.RoundedRect(7, 7, 8, 14, 0.5)], plan);
        // It passes between the walls, not round the end of either.
        Assert.Contains(plan.Path, p => p.X > L("7") && p.X < L("8") || p.X >= L("6.9") && p.X <= L("8.1") && p.Y > L("5") && p.Y < L("7"));
    }

    [Fact]
    [Trait("Rule", "Movement p. 35: a gap narrower than the base cannot be used")]
    public void AGapNarrowerThanTheBaseIsNotUsed()
    {
        var w1 = Wall("w1", "7", "1", "8", "5");
        var w2 = Wall("w2", "7", "5.8", "8", "14");   // a 0.8" gap on a 1" base
        var field = Field("36", w1, w2);
        var mover = Body("m", "4", "4");
        var target = Body("t", "12", "4");
        var others = new[] { Enemy(target) };

        var plan = Charging.Plan(field, mover, target, Twelve, others);
        AssertSound(field, mover, target, Twelve, others, plan);
        AssertShortest(mover, target, [ReferenceRouter.RoundedRect(7, 1, 8, 5, 0.5), ReferenceRouter.RoundedRect(7, 5.8, 8, 14, 0.5)], plan);
        // No point of the route is between the walls.
        Assert.DoesNotContain(plan.Path, p => p.X >= L("6.9") && p.X <= L("8.1") && p.Y > L("5.2") && p.Y < L("5.6"));
    }

    [Fact]
    [Trait("Rule", "Charge Moves p. 37: the same shortest route for terrain placed anywhere in the 12\"")]
    public void ManyRandomWallsGiveRoutesThatMatchTheBruteForceSearch()
    {
        var rng = new Random(2026);
        var routed = 0;
        for (var i = 0; i < 40; i++)
        {
            var mover = new ModelBody("m", new Point2(Length.FromThousandths(rng.Next(3000, 6000)), Length.FromThousandths(rng.Next(6000, 14000))), L("0.5"));
            var target = new ModelBody("t", new Point2(Length.FromThousandths(rng.Next(11000, 14000)), Length.FromThousandths(rng.Next(6000, 14000))), L("0.5"));
            // A wall across the middle: narrow, at a random place between them.
            var x = rng.Next(7000, 10000);
            var y1 = rng.Next(3000, 9000);
            var height = rng.Next(2000, 7000);
            var wall = Wall("w", $"{x / 1000.0:0.000}", $"{y1 / 1000.0:0.000}", $"{(x + rng.Next(500, 2000)) / 1000.0:0.000}", $"{(y1 + height) / 1000.0:0.000}");
            var field = Field("36", wall);
            var others = new[] { Enemy(target) };

            var route = Charging.CheckRoute(field, mover, target, others);
            var bounds = wall.Footprint.Bounds;
            var shape = ReferenceRouter.RoundedRect(Inches(bounds.Min.X), Inches(bounds.Min.Y), Inches(bounds.Max.X), Inches(bounds.Max.Y), 0.5);
            var expected = ReferenceRouter.ShortestToContact(
                Inches(mover.Centre.X), Inches(mover.Centre.Y), [shape], Inches(target.Centre.X), Inches(target.Centre.Y), 1.0);
            if (!route.IsLegal)
            {
                Assert.True(expected > 11.9 || double.IsInfinity(expected), $"{i}: planner refused but brute force found {expected}");
                continue;
            }
            var plan = Charging.Plan(field, mover, target, Twelve, others);
            Assert.True(plan.Reached, $"{i}");
            AssertSound(field, mover, target, Twelve, others, plan);
            AssertShortest(mover, target, [shape], plan, $"i={i} mover={mover} target={target} wall={wall.Footprint}");
            if (plan.Path.Length > 2)
            {
                routed++;
            }
        }
        Assert.True(routed >= 12, $"only {routed} scenes needed a detour");
    }

    // ---- 3. friendly bases ----

    [Fact]
    [Trait("Rule", "Movement p. 35: a model may cross a friendly model if it can move past them entirely")]
    public void AChargeCrossesAFriendWhenItsMovementTakesItPastEntirely()
    {
        var field = Field("36");
        var mover = Body("m", "4", "2");
        var target = Body("t", "13", "2");
        var friend = Body("f", "8", "2");
        var others = new[] { Enemy(target), Friend(friend) };

        // 5.2" ends at x = 9.2: past the friend, whose base spans 7 to 9 for a 0.5" mover.
        var past = Charging.Plan(field, mover, target, L("5.2"), others);
        AssertSound(field, mover, target, L("5.2"), others, past);
        Assert.Equal(L("9.2"), past.Path[^1].X);
        Assert.Equal(L("2"), past.Path[^1].Y);
        Assert.False(past.Reached);

        // A full charge goes straight over it to contact.
        var full = Charging.Plan(field, mover, target, Twelve, others);
        AssertSound(field, mover, target, Twelve, others, full);
        Assert.True(full.Reached);
        Assert.All(full.Path, p => Assert.Equal(L("2"), p.Y));
    }

    [Fact]
    [Trait("Rule", "Movement p. 35: a model cannot cross a friend it cannot move past, so the route goes round")]
    public void AChargeThatCannotGetPastAFriendGoesRoundIt()
    {
        var field = Field("36");
        var mover = Body("m", "4", "2");
        var target = Body("t", "13", "2");
        var friend = Body("f", "8", "2");
        var others = new[] { Enemy(target), Friend(friend) };

        // 4.9" would end at x = 8.9, still on the friend's base: crossing is not available.
        var plan = Charging.Plan(field, mover, target, L("4.9"), others);
        AssertSound(field, mover, target, L("4.9"), others, plan);
        Assert.False(Measure.Overlap(mover.At(plan.Path[^1]), friend));
        Assert.Contains(plan.Path, p => Math.Abs(Inches(p.Y) - 2) > 0.2);
        // And it used the movement it had, going round, rather than stopping short in front of the friend.
        Assert.True(plan.Cost >= L("4.8"), plan.Cost.ToString());
        var along = plan.Path[^1].X;
        Assert.True(along > L("7"), $"it ended at {plan.Path[^1]}");
    }

    [Fact]
    [Trait("Rule", "Movement p. 35: a friend at the contact point is gone round to reach the target")]
    public void AFriendAtTheContactPointIsGoneRoundToReachTheTarget()
    {
        var field = Field("36");
        var mover = Body("m", "4", "2");
        var target = Body("t", "13", "2");
        var friend = Body("f", "11.6", "2");   // on the straight route's point of contact
        var others = new[] { Enemy(target), Friend(friend) };

        var plan = Charging.Plan(field, mover, target, Twelve, others);
        AssertSound(field, mover, target, Twelve, others, plan);
        Assert.True(plan.Reached, $"{plan.Cost} {string.Join(" ", plan.Path)}");
        Assert.False(Measure.Overlap(mover.At(plan.Path[^1]), friend));
        AssertShortest(mover, target, [ReferenceRouter.Circle(11.6, 2, 1.0)], plan);
    }

    [Fact]
    [Trait("Rule", "Movement p. 35: with no way round a friend the charge stops short of it")]
    public void WithNoWayRoundAFriendTheChargeStopsInFrontOfIt()
    {
        // A corridor 1.4" wide with a friend in it: the base cannot go round, and 6" would end on the friend.
        var field = Field("36", Wall("south", "6", "3", "14", "4"), Wall("north", "6", "5.4", "14", "6.4"));
        var mover = Body("m", "3", "4.7");
        var target = Body("t", "16", "4.7");
        var friend = Body("f", "9", "4.7");
        var others = new[] { Enemy(target), Friend(friend) };

        Assert.True(Charging.CheckRoute(field, mover, target, others).IsLegal);
        var plan = Charging.Plan(field, mover, target, L("6"), others);
        AssertSound(field, mover, target, L("6"), others, plan);
        Assert.False(plan.Reached);
        Assert.False(Measure.Overlap(mover.At(plan.Path[^1]), friend));
        // It stops where its base touches the friend's: 1" between centres.
        Assert.True(plan.Path[^1].X >= L("7.99") && plan.Path[^1].X <= L("8"), plan.Path[^1].ToString());
        Assert.Equal(L("4.7"), plan.Path[^1].Y);
    }

    // ---- 4. cost and insufficient movement on a detour ----

    [Fact]
    [Trait("Rule", "Charging p. 36: a detour costs its full length, so the same Charge Bonus buys less")]
    public void ADetourCostsItsLengthAndAShortRollFallsShort()
    {
        var wall = Wall("w", "7", "3.5", "8", "9.5");
        var field = Field("36", wall);
        var mover = Body("m", "4", "6");
        var target = Body("t", "12", "6");
        var others = new[] { Enemy(target) };

        var full = Charging.Plan(field, mover, target, Twelve, others);
        Assert.True(full.Reached);
        Assert.True(full.Cost > L("7"));

        // Exactly enough: contact. A little less: short of it, having used what it had.
        var exact = Charging.Plan(field, mover, target, full.Cost, others);
        Assert.True(exact.Reached);
        Assert.True(full.Path.SequenceEqual(exact.Path));

        // (Finishing within 1" of the target is already a successful charge, p. 37, so "short" means over an inch short.)
        var close = Charging.Plan(field, mover, target, full.Cost - L("0.5"), others);
        AssertSound(field, mover, target, full.Cost - L("0.5"), others, close);
        Assert.True(close.Reached);
        Assert.True(close.Cost > full.Cost - L("0.52"), $"{close.Cost} of {full.Cost}");

        var short_ = Charging.Plan(field, mover, target, full.Cost - L("1.6"), others);
        AssertSound(field, mover, target, full.Cost - L("1.6"), others, short_);
        Assert.False(short_.Reached);
        Assert.True(short_.Cost > full.Cost - L("1.62"), $"{short_.Cost} of {full.Cost}");

        // Nothing like enough: it makes some progress round the wall and stops.
        var small = Charging.Plan(field, mover, target, L("3"), others);
        AssertSound(field, mover, target, L("3"), others, small);
        Assert.False(small.Reached);
        Assert.True(small.Cost > L("2.9"));

        // The straight-line distance is only 7": a model that would have made it on open ground falls short here.
        var openPlan = Charging.Plan(Field("36"), mover, target, L("7"), others);
        Assert.True(openPlan.Reached);
        Assert.False(Charging.Plan(field, mover, target, L("7"), others).Reached);
    }

    // ---- 5. interposition is judged on the planned route ----

    [Fact]
    [Trait("Rule", "Interposing Enemy Models p. 36: an enemy beside the direct line does not interpose when the route goes elsewhere")]
    public void AnEnemyBesideTheDirectLineButFarFromTheRouteDoesNotInterpose()
    {
        var wall = Wall("w", "7", "3.5", "8", "9.5");
        var mover = Body("m", "4", "6");
        var target = Body("t", "12", "6");
        var near = Body("e", "9.6", "7.6");   // 1.6" from the straight line, far from the route under the wall

        // On open ground it interposes.
        var open = Charging.CheckRoute(Field("36"), mover, target, [Enemy(target), Enemy(near)]);
        Assert.False(open.IsLegal);
        Assert.Equal(MoveRefusal.EntersEnemyZone, open.Reason);

        // With the wall the route goes under it, nowhere near the enemy: the charge can be made.
        var field = Field("36", wall);
        var others = new[] { Enemy(target), Enemy(near) };
        var route = Charging.CheckRoute(field, mover, target, others);
        Assert.True(route.IsLegal, route.Text);
        var plan = Charging.Plan(field, mover, target, Twelve, others);
        AssertSound(field, mover, target, Twelve, others, plan);
        Assert.True(plan.Reached);
    }

    [Fact]
    [Trait("Rule", "Interposing Enemy Models p. 36: an enemy by the planned route interposes although it is far from the direct line")]
    public void AnEnemyBesideTheRouteInterposesEvenWhenTheDirectLineIsFarFromIt()
    {
        var wall = Wall("w", "7", "3.5", "8", "9.5");
        var field = Field("36", wall);
        var mover = Body("m", "4", "6");
        var target = Body("t", "12", "6");
        var beside = Body("e", "7.5", "1.5");   // 4.5" from the straight line, 1.5" from the way round the wall

        var route = Charging.CheckRoute(field, mover, target, [Enemy(target), Enemy(beside)]);
        Assert.False(route.IsLegal);
        Assert.Equal(MoveRefusal.EntersEnemyZone, route.Reason);
        Assert.Equal("1.0.2 p. 36", route.RuleRef);

        // The interposing model is a fine target (the route to it is the shortest and nothing else is near).
        Assert.True(Charging.CheckRoute(field, mover, beside, [Enemy(target), Enemy(beside)]).IsLegal);
    }

    // ---- 6. no route, blocked target, movement ----

    [Fact]
    [Trait("Rule", "Charge Moves p. 37: with no route to the target there is no charge")]
    public void ATargetWalledInHasNoRoute()
    {
        var box = new[]
        {
            Wall("left", "9", "1", "10", "7"), Wall("right", "14", "1", "15", "7"),
            Wall("top", "9", "7", "15", "8"), Wall("bottom", "9", "0", "15", "1"),
        };
        var field = Field("36", box);
        var mover = Body("m", "4", "4");
        var target = Body("t", "12", "4");
        var route = Charging.CheckRoute(field, mover, target, [Enemy(target)]);
        Assert.False(route.IsLegal);
        Assert.Equal(MoveRefusal.NoRoute, route.Reason);
        Assert.True(Charging.Plan(field, mover, target, Twelve, [Enemy(target)]).Path.IsEmpty);
    }

    [Fact]
    [Trait("Rule", "Movement p. 35: an opening narrower than the base is no way in; a wide one is")]
    public void AnOpeningMustBeWideEnoughForTheBase()
    {
        Battlefield Pocket(string gapLow, string gapHigh) => Field("36",
            Wall("left-low", "9", "1", "10", gapLow), Wall("left-high", "9", gapHigh, "10", "7"),
            Wall("right", "14", "1", "15", "7"), Wall("top", "9", "7", "15", "8"), Wall("bottom", "9", "0", "15", "1"));
        var mover = Body("m", "4", "4");
        var target = Body("t", "12", "4");

        // A 0.8" opening on a 1" base: shut.
        var shut = Charging.CheckRoute(Pocket("3.6", "4.4"), mover, target, [Enemy(target)]);
        Assert.False(shut.IsLegal);
        Assert.Equal(MoveRefusal.NoRoute, shut.Reason);

        // A 1.6" opening: the base walks in.
        var others = new[] { Enemy(target) };
        var field = Pocket("3.2", "4.8");
        Assert.True(Charging.CheckRoute(field, mover, target, others).IsLegal);
        var plan = Charging.Plan(field, mover, target, Twelve, others);
        AssertSound(field, mover, target, Twelve, others, plan);
        Assert.True(plan.Reached);
    }

    [Fact]
    [Trait("Rule", "Charging p. 36: a route longer than the 12\" a charge can cover cannot be declared")]
    public void ARouteLongerThanTwelveInchesHasNoDeclaration()
    {
        var wall = Wall("w", "7", "0.9", "8", "20");   // a long wall: the way round is far over 12"
        var field = Field("36", wall);
        var mover = Body("m", "4", "10");
        var target = Body("t", "12", "10");
        var route = Charging.CheckRoute(field, mover, target, [Enemy(target)]);
        Assert.False(route.IsLegal);
        Assert.Equal(MoveRefusal.NoRoute, route.Reason);
    }

    [Fact]
    [Trait("Rule", "Charge Moves p. 37: another enemy's base next to the target is gone round inside the 1\"")]
    public void AnEnemyBesideTheTargetDoesNotBlockContactElsewhere()
    {
        var field = Field("36");
        var mover = Body("m", "4", "6");
        var target = Body("t", "12", "6");
        var beside = Body("e", "11", "6");   // sitting on the straight line's contact point, 1" from the target's edge
        // The enemy is on the route (it interposes), so the declaration is refused rather than bent round it.
        Assert.False(Charging.CheckRoute(field, mover, target, [Enemy(target), Enemy(beside)]).IsLegal);

        // An enemy that only blocks the final approach is gone round: it is beyond 1" from the approach but its
        // base is in the target's 1".
        var tucked = Body("e", "12", "7.6");   // 1.6" above the target: base 0.6" above the target's 1" zone edge? within the zone
        var others = new[] { Enemy(target), Enemy(tucked) };
        var route = Charging.CheckRoute(field, mover, target, others);
        Assert.True(route.IsLegal, route.Text);
        var plan = Charging.Plan(field, mover, target, Twelve, others);
        AssertSound(field, mover, target, Twelve, others, plan);
        Assert.True(plan.Reached);
    }

    // ---- 7. determinism ----

    [Fact]
    [Trait("Rule", "Architecture: equal-length routes are chosen the same way every time, whatever order the models come in")]
    public void EqualCostRoutesAreChosenDeterministically()
    {
        // The wall is centred on the line, so going over and under cost exactly the same.
        var wall = Wall("w", "7", "3.5", "8", "8.5");
        var field = Field("36", wall);
        var mover = Body("m", "4", "6");
        var target = Body("t", "12", "6");
        var obstacles = new List<Obstacle> { Enemy(target), Friend(Body("f1", "3", "20")), Friend(Body("f2", "5", "25")), Enemy(Body("far", "30", "30")) };

        var reference = Charging.Plan(field, mover, target, Twelve, obstacles);
        Assert.True(reference.Reached);
        for (var seed = 0; seed < 8; seed++)
        {
            var again = Charging.Plan(field, mover, target, Twelve, Shuffled(obstacles, seed));
            Assert.True(reference.Path.SequenceEqual(again.Path));
            Assert.Equal(reference.Cost, again.Cost);
        }

        var maxY = reference.Path.Max(p => p.Y);
        var minY = reference.Path.Min(p => p.Y);
        Assert.True(maxY > L("8.5") || minY < L("3.5"));
        Assert.False(maxY > L("8.5") && minY < L("3.5"), "the route went round both ends");
    }

    [Fact]
    [Trait("Rule", "Architecture: a scene and its mirror image give mirror-image routes at the same cost")]
    public void AMirrorImageSceneGivesAMirrorImageRoute()
    {
        var field = Field("36", Wall("w", "7", "3", "8", "9"));
        var mirrored = Field("36", Wall("w", "7", "11", "8", "17"));   // y -> 20 - y
        var mover = Body("m", "4", "5");
        var target = Body("t", "12", "5.6");
        var plan = Charging.Plan(field, mover, target, Twelve, [Enemy(target)]);
        var image = Charging.Plan(mirrored, Body("m", "4", "15"), Body("t", "12", "14.4"), Twelve, [Enemy(Body("t", "12", "14.4"))]);

        Assert.True(plan.Reached && image.Reached);
        Assert.Equal(plan.Cost, image.Cost);
        Assert.Equal(plan.Path.Length, image.Path.Length);
        // The mirror image is exact for the corner points; the first and last legs are lattice-rounded along the line.
        for (var i = 1; i < plan.Path.Length - 1; i++)
        {
            Assert.Equal(plan.Path[i].X, image.Path[i].X);
            Assert.Equal(L("20") - plan.Path[i].Y, image.Path[i].Y);
        }
    }

    // ---- 8. soundness over many scenes ----

    [Fact]
    [Trait("Rule", "Movement p. 35: every planned charge is legal by the exact movement rules")]
    public void PlansInCrowdedScenesAreAlwaysLegalAndWithinTheAllowance()
    {
        var rng = new Random(77);
        var detours = 0;
        for (var i = 0; i < 150; i++)
        {
            var walls = Enumerable.Range(0, rng.Next(1, 4)).Select(n =>
            {
                var x = rng.Next(5000, 22000);
                var y = rng.Next(1000, 22000);
                return Wall($"w{n}", $"{x / 1000.0:0.000}", $"{y / 1000.0:0.000}", $"{(x + rng.Next(600, 2500)) / 1000.0:0.000}", $"{(y + rng.Next(600, 5000)) / 1000.0:0.000}");
            }).ToList();
            var mover = new ModelBody("m", new Point2(Length.FromThousandths(rng.Next(2000, 5000)), Length.FromThousandths(rng.Next(3000, 15000))), L("0.5"));
            var target = new ModelBody("t", new Point2(Length.FromThousandths(rng.Next(9000, 15000)), Length.FromThousandths(rng.Next(3000, 15000))), L(i % 4 == 0 ? "0.75" : "0.5"));
            var friends = Enumerable.Range(0, rng.Next(0, 3)).Select(n => Friend(new ModelBody($"f{n}", new Point2(Length.FromThousandths(rng.Next(4000, 12000)), Length.FromThousandths(rng.Next(2000, 16000))), L("0.5"))));
            var enemies = Enumerable.Range(0, rng.Next(0, 3)).Select(n => Enemy(new ModelBody($"e{n}", new Point2(Length.FromThousandths(rng.Next(4000, 16000)), Length.FromThousandths(rng.Next(2000, 16000))), L("0.5"))));
            var others = new List<Obstacle> { Enemy(target) };
            others.AddRange(friends);
            others.AddRange(enemies);

            Battlefield field;
            try
            {
                field = Field("36", walls.ToArray());
            }
            catch (ArgumentException)
            {
                continue;
            }
            // Skip scenes where something already overlaps the mover or another model.
            if (others.Any(o => Measure.Overlap(mover, o.Body)) || walls.Any(w => w.Footprint.CircleOverlaps(mover.Centre, mover.Radius)) || Measure.IsWithin(mover, target, Length.Inch))
            {
                continue;
            }
            var reach = Length.FromThousandths(rng.Next(3000, 12001));
            var route = Charging.CheckRoute(field, mover, target, others);
            var plan = Charging.Plan(field, mover, target, reach, others);
            if (!route.IsLegal)
            {
                continue;
            }
            AssertSound(field, mover, target, reach, others, plan);
            if (plan.Path.Length > 2)
            {
                detours++;
            }
        }
        Assert.True(detours > 10, $"only {detours} detours exercised");
    }
}
