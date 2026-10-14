using System.Collections.Immutable;
using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.State;
using static Rules.Tests.Geometry.Geo;
using static Rules.Tests.Match.BoardPlay;
using static Rules.Tests.Match.MeleePlay;

namespace Rules.Tests.Match;

// A charge round an obstacle in a whole game: the declaration before any die, the recorded path, replay and hash,
// and a reducer that replans the move and refuses any path but the shortest one (Comprehensive Rules pp. 36-37).
public class ChargeRoutingPlayTests
{
    // Impassable wire between the charger and its target; the gap behind it is open.
    private static readonly Scenario Wired = WithTerrain(Wire("wire", "7", "1.5", "8", "5.5"));

    private static ChargeModel Charge() => new(Seat.One, A(1), B(1));

    private static MatchState Apply(MatchState state, IEnumerable<GameEvent> events) => events.Aggregate(state, Reducer.Apply);

    private static void Rejects(MatchState state, GameEvent forged) =>
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(state, forged));

    [Fact]
    [Trait("Rule", "Charge Moves p. 37: the charge goes round the wire by the shortest route and finishes in contact")]
    public void AChargeGoesRoundAnObstacleAndFinishesInContact()
    {
        var play = SkirmishWith(Wired, "4", "2", "12", "2");
        var moved = play.Do(Charge(), 6).Events.OfType<ChargeMoved>().Single();

        Assert.True(moved.Reached);
        Assert.True(moved.Path.Length > 2, "the route bends round the wire");
        Assert.Equal(play.M(A(1)).Position, moved.Path[^1]);
        Assert.True(Within(play, A(1), B(1), "1"));
        Assert.False(Measure.Overlap(BodyOf(play, A(1)), BodyOf(play, B(1))));
        Assert.True(moved.Cost > L("7"), "longer than the straight 7 inches");
        // No leg of the path touches the wire.
        var field = play.Setup.Scenario!.Battlefield;
        var body = BodyOf(Skirmish("4", "2", "12", "2"), A(1));
        Assert.True(Movement.CheckPath(field, body, moved.Path, L("12"), [Enemy(BodyOf(play, B(1)))], "B1").IsLegal);
    }

    [Fact]
    [Trait("Rule", "Charging p. 36: the same Charge Bonus buys less distance on a detour")]
    public void ADetourCanTurnASuccessfulChargeIntoAShortOne()
    {
        // Gap of 9" between the bases: a bonus of 2 reaches exactly 1" from the target on open ground.
        var open = Skirmish("4", "2", "14", "2");
        Assert.True(open.Do(Charge(), 2).Events.OfType<ChargeMoved>().Single().Reached);

        var wired = SkirmishWith(Wired, "4", "2", "14", "2");
        var moved = wired.Do(Charge(), 2).Events.OfType<ChargeMoved>().Single();
        Assert.False(moved.Reached);
        Assert.True(moved.Cost <= L("8"));
        Assert.False(Within(wired, A(1), B(1), "1"));
        Assert.True(wired.State.Actions.HasFlag(ActionsTaken.Charge));
    }

    [Fact]
    [Trait("Rule", "Charging p. 36: a target within 12\" and in sight can be declared even if the way round is longer than any charge")]
    public void ADetourLongerThanAnyChargeIsDeclaredAndFallsShort()
    {
        // A wire 14" long between them: round it is well over the 12" a charge can cover.
        var longWire = WithTerrain(Wire("long", "7", "0", "8", "14"));
        var play = SkirmishWith(longWire, "4", "2", "12", "2");
        var before = play.State;
        var result = play.Do(Charge(), 6);   // Movement plus a Charge Bonus of 6, capped at 12"
        var moved = result.Events.OfType<ChargeMoved>().Single();
        Assert.False(moved.Reached);
        Assert.False(moved.Path.IsEmpty);
        Assert.True(moved.Cost <= L("12") && moved.Cost > L("11"), moved.Cost.ToString());
        Assert.True(play.State.Actions.HasFlag(ActionsTaken.Charge));
        Assert.Equal(play.M(A(1)).Position, moved.Path[^1]);
        // The recorded move replays, from the state before it, to the same hash.
        Assert.Equal(StateHash.Compute(play.State), StateHash.Compute(Apply(before, result.Events)));
        // The path is the first 12" of the route, short of the wire's top: the model is still on its side of the wire.
        var field = play.Setup.Scenario!.Battlefield;
        var body = BodyOf(Skirmish("4", "2", "12", "2"), A(1));
        Assert.True(Movement.CheckPath(field, body, moved.Path, L("12"), [Enemy(BodyOf(play, B(1)))], "B1").IsLegal);
    }

    [Fact]
    [Trait("Rule", "Charge Moves p. 37: a target with no route cannot be declared, and no die is rolled")]
    public void ATargetWithNoRouteIsRefusedBeforeTheDie()
    {
        var walled = WithTerrain(
            Wire("w-left", "10", "1.5", "11", "6.5"), Wire("w-right", "13", "1.5", "14", "6.5"),
            Wire("w-top", "10", "6.5", "14", "7.5"), Wire("w-bottom", "10", "0.5", "14", "1.5"));
        var play = SkirmishWith(walled, "4", "4", "12", "4");
        play.Refused(Charge(), ReasonCode.ChargeRouteBlocked);
        play.Refused(new ChargeModel(Seat.One, A(1), B(2)), ReasonCode.TargetOutOfRange);
    }

    [Fact]
    [Trait("Rule", "Interposing Enemy Models p. 36: the enemy that matters is the one by the route that is taken")]
    public void InterpositionFollowsTheRoundTheWireRoute()
    {
        // B2 sits beside the straight line but nowhere near the way round the wire: the charge on B1 is allowed.
        var away = SkirmishWith(Wired, "4", "2", "12", "2");
        away.Put(B(2), "9.6", "3.6");
        Assert.True(away.Do(Charge(), 6).Events.OfType<ChargeMoved>().Single().Reached);

        // B2 right by the way round (under the wire) interposes, although it is 1.2" from the straight line.
        var beside = SkirmishWith(Wired, "4", "2", "12", "2");
        beside.Put(B(2), "7.5", "0.7");
        beside.Refused(Charge(), ReasonCode.ChargeRouteBlocked);
    }

    [Fact]
    [Trait("Rule", "Architecture: the reducer replans the charge and refuses any path but the shortest")]
    public void TheReducerRefusesForgedAndNonShortestChargePaths()
    {
        var play = SkirmishWith(Wired, "4", "2", "12", "2");
        var honest = TurnSequence.Decide(play.State, Charge(), new FixedDice(6)).Events;
        var moved = (ChargeMoved)honest[2];
        var afterRoll = Apply(play.State, honest.Take(2));
        var field = play.Setup.Scenario!.Battlefield;
        var body = BodyOf(play, A(1));
        Length CostOf(IReadOnlyList<Point2> path) => Movement.Cost(field, body, path);

        // Straight through the wire, with the matching cost.
        var straight = ImmutableArray.Create(P("10", "2"), P("11", "2"));
        Rejects(afterRoll, new ChargeMoved(A(1), B(1), straight, CostOf(straight), true));

        // Legal but not shortest: an extra waypoint pushes the route out, and the cost is right for that path.
        var wide = honest.OfType<ChargeMoved>().Single().Path.ToList();
        var detour = wide.Select((p, i) => i == 0 ? new Point2(p.X, p.Y - L("0.4")) : p).ToList();
        detour.Insert(0, P("5", "1.6"));
        Assert.True(Movement.CheckPath(field, body, detour, L("12"), [Enemy(BodyOf(play, B(1)))], "B1").IsLegal, "the forged path should be legal");
        var detoured = ImmutableArray.CreateRange(detour);
        Rejects(afterRoll, new ChargeMoved(A(1), B(1), detoured, CostOf(detoured), true));

        // The same route with an extra point on a straight leg: same shape, same cost, but not the planner's own path.
        var start = P("4", "2");
        var midpoint = new Point2(Length.FromThousandths((start.X.Thousandths + moved.Path[0].X.Thousandths) / 2), Length.FromThousandths((start.Y.Thousandths + moved.Path[0].Y.Thousandths) / 2));
        var padded = moved.Path.Insert(0, midpoint);
        Assert.Equal(moved.Cost, CostOf(padded));
        Rejects(afterRoll, new ChargeMoved(A(1), B(1), padded, moved.Cost, true));

        // A path that stops early, or one that reports another cost or outcome.
        var early = moved.Path.RemoveAt(moved.Path.Length - 1);
        Rejects(afterRoll, new ChargeMoved(A(1), B(1), early, CostOf(early), false));
        Rejects(afterRoll, new ChargeMoved(A(1), B(1), moved.Path, moved.Cost + L("0.001"), true));
        Rejects(afterRoll, new ChargeMoved(A(1), B(1), moved.Path, moved.Cost, false));
        Rejects(afterRoll, new ChargeMoved(A(1), B(1), [], Length.Zero, false));

        // A path of more legs than any charge can have is not even a valid event.
        var tooMany = ImmutableArray.CreateRange(Enumerable.Range(0, Charging.MaxLegs + 1).Select(_ => P("5", "2")));
        Assert.Throws<ArgumentException>(() => new ChargeMoved(A(1), B(1), tooMany, L("1"), false));

        // The honest move is accepted and puts the model where the path ends.
        Assert.Equal(moved.Path[^1], Reducer.Apply(afterRoll, moved).FindModel(A(1))!.Position);
    }

    [Fact]
    [Trait("Rule", "Architecture: a routed charge's events rebuild the same state and hash, in any run")]
    public void ARoutedChargeReplaysToTheSameHash()
    {
        // Put() is test set-up, not an event, so the events are replayed onto the state they started from.
        var play = SkirmishWith(Wired, "4", "2", "12", "2");
        var before = play.State;
        var result = play.Do(Charge(), 6);
        Assert.Equal(StateHash.Compute(play.State), StateHash.Compute(Apply(before, result.Events)));

        // The same game played twice gives the same events, path for path.
        var again = SkirmishWith(Wired, "4", "2", "12", "2");
        again.Do(Charge(), 6);
        Assert.True(play.Log.OfType<ChargeMoved>().Single().Path.SequenceEqual(again.Log.OfType<ChargeMoved>().Single().Path));
        Assert.Equal(StateHash.Compute(play.State), StateHash.Compute(again.State));
    }
}
