using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.State;
using static Rules.Tests.Geometry.Geo;
using static Rules.Tests.Match.BoardPlay;

namespace Rules.Tests.Match;

// Roster stage R9a (U134): the Loudspeakers ACTION (friends within 8" move 3" towards the nearest visible enemy) and the Puppet
// Master ACTION (one other model in sight within 12" is moved D6" in a straight line). Both are Risky; the moves are worked out
// by the rules from the positions and checked when the log is replayed.
public class ForcedMoveTests
{
    private static readonly ModelProfile Caller = Rifleman with { Loudspeakers = true };
    private static readonly ModelProfile Puppeteer = Rifleman with { PuppetMaster = true };

    private static Scenario Open(params TerrainBlock[] terrain) =>
        new(Field("36", terrain, [new DeploymentZone("west", F("0", "0", "6", "36")), new DeploymentZone("east", F("30", "0", "36", "36"))]), []);

    // Seat One's A1 (the actor) at (10, 10), A2..A4 and B1..B4 as the test puts them; A1 is activated.
    private static BoardPlay Ready(ModelProfile actor, params TerrainBlock[] terrain)
    {
        var play = new BoardPlay(4, id => id == A(1) ? actor : Rifleman, scenario: Open(terrain)).ToActivation();
        play.Put(A(1), "10", "10").Put(A(2), "14", "10").Put(A(3), "10", "17.5").Put(A(4), "10", "30")
            .Put(B(1), "25", "10").Put(B(2), "30", "30").Put(B(3), "34", "34").Put(B(4), "34", "2");
        play.Do(new ActivateModel(Seat.One, A(1)));
        return play;
    }

    private static TakeSelfAction Loud => new(Seat.One, A(1), SelfActionKind.Loudspeakers);

    private static double Distance(Point2 a, Point2 b) =>
        Math.Sqrt(Math.Pow(a.X.Thousandths - b.X.Thousandths, 2) + Math.Pow(a.Y.Thousandths - b.Y.Thousandths, 2)) / 1000.0;

    [Fact]
    [Trait("Rule", "Loudspeakers p. 53: friends within 8\" move up to 3\" towards the nearest visible enemy")]
    public void FriendsWithinEightInchesAdvanceThreeInches()
    {
        var play = Ready(Caller);
        var before3 = play.M(A(3)).Position!.Value;
        var result = play.Do(Loud, 6, 6, 6, 6);
        var moved = result.Events.OfType<ModelsMoved>().Single();
        Assert.Equal((A(1), 0), (moved.Source, moved.Die));
        // A2 (4" east of A1, 14" from B1's edge) goes 3" straight at B1; A3 (6.5" off) 3" towards B1; A4 (20" off) and A1 stay.
        Assert.Equal([A(2), A(3)], moved.Moves.Select(m => m.Model));
        Assert.Equal(P("17", "10"), play.M(A(2)).Position);
        Assert.InRange(Distance(before3, play.M(A(3)).Position!.Value), 2.998, 3.002);
        Assert.True(Distance(play.M(A(3)).Position!.Value, P("25", "10")) < Distance(before3, P("25", "10")));
        Assert.Equal(P("10", "30"), play.M(A(4)).Position);
        Assert.Equal(P("10", "10"), play.M(A(1)).Position);
        Assert.Equal(P("25", "10"), play.M(B(1)).Position);
    }

    [Fact]
    [Trait("Rule", "Loudspeakers p. 53: a Risky roll; a Failure moves nobody and ends the activation")]
    public void AFailureMovesNobody()
    {
        var play = Ready(Caller);
        var events = play.Do(Loud, 1, 1, 1, 1).Events;
        Assert.DoesNotContain(events, e => e is ModelsMoved);
        Assert.Contains(events, e => e is ActivationEnded { Reason: ActivationEndReason.RiskyFailure });
        Assert.Equal(P("14", "10"), play.M(A(2)).Position);
    }

    [Fact]
    public void ANearbyFriendStopsAtContactAndAWallStopsTheMove()
    {
        // B1 sits 3.6" past A2: the friend stops where the bases touch; a wall across the way stops another short.
        var play = Ready(Caller, Wall("w", "12.2", "16", "12.4", "19"));
        play.Put(B(1), "18.6", "10");
        play.Do(Loud, 6, 6, 6, 6);
        // A2 (14, 10) towards (18.6, 10): contact at x 17.6 is 3.6" away, so the full 3" fits: 17.0.
        Assert.Equal(P("17", "10"), play.M(A(2)).Position);
        // A3 heads for B1 (down and right) and is not hindered by the wall off its line.
        Assert.True(play.M(A(3)).Position!.Value.X > L("10"));

        var close = Ready(Caller);
        close.Put(B(1), "16.2", "10");
        close.Do(Loud, 6, 6, 6, 6);
        // Contact with B1 (radius 0.5 each) is at x 15.2.
        Assert.Equal(P("15.2", "10"), close.M(A(2)).Position);
        Assert.False(Measure.Overlap(Board.BodyOf(close.State, close.M(A(2)))!, Board.BodyOf(close.State, close.M(B(1)))!));
    }

    [Fact]
    public void ANoVisibleEnemyMeansNoMove()
    {
        // A wall that hides every enemy from A2: it stays.
        var play = Ready(Caller, Wall("w", "15", "0", "16", "36"));
        play.Do(Loud, 6, 6, 6, 6);
        Assert.Equal(P("14", "10"), play.M(A(2)).Position);
    }

    [Fact]
    public void OnlyAModelWithTheActionMayTakeIt()
    {
        var play = Ready(Rifleman);
        play.Refused(Loud, ReasonCode.NoSuchAction);
        var good = Ready(Caller);
        good.Refused(new TakeSelfAction(Seat.One, A(1), SelfActionKind.Loudspeakers) { Target = A(2) }, ReasonCode.InvalidChoice);
        good.Refused(new TakeSelfAction(Seat.One, A(1), SelfActionKind.Loudspeakers) { Direction = P("12", "10") }, ReasonCode.InvalidChoice);
        good.Do(Loud, 6, 6, 6, 6);
        good.Refused(Loud, ReasonCode.ActionAlreadyTaken);
    }

    private static TakeSelfAction Puppet(string target, string x, string y) =>
        new(Seat.One, A(1), SelfActionKind.PuppetMaster) { Target = new ModelId(target), Direction = P(x, y) };

    [Fact]
    [Trait("Rule", "Puppet Master p. 109: a Risky roll; one other model in sight within 12\" moves D6\" in a straight line")]
    public void ThePuppetMasterMovesAnyModelD6Inches()
    {
        var play = Ready(Puppeteer);
        play.Put(B(1), "15", "10");
        var result = play.Do(Puppet("B1", "20", "10"), 6, 6, 4);
        var moved = result.Events.OfType<ModelsMoved>().Single();
        Assert.Equal(4, moved.Die);
        Assert.Equal(new ForcedMove(B(1), P("19", "10")), Assert.Single(moved.Moves));
        Assert.Equal(P("19", "10"), play.M(B(1)).Position);
        // A friend, too, and not the model itself.
        var friend = Ready(Puppeteer);
        friend.Do(Puppet("A2", "14", "20"), 6, 6, 2);
        Assert.Equal(P("14", "12"), friend.M(A(2)).Position);
    }

    [Fact]
    public void AModelInTheWayStopsThePushAtContact()
    {
        var play = Ready(Puppeteer);
        play.Put(B(1), "15", "10").Put(B(2), "17.5", "10");
        play.Do(Puppet("B1", "20", "10"), 6, 6, 5);
        Assert.Equal(P("16.5", "10"), play.M(B(1)).Position);
        // A wall stops it the same way.
        var wall = Ready(Puppeteer, Wall("w", "17", "5", "18", "15"));
        wall.Put(B(1), "15", "10");
        wall.Do(Puppet("B1", "20", "10"), 6, 6, 6);
        Assert.Equal(P("16.5", "10"), wall.M(B(1)).Position);
    }

    [Fact]
    public void TheTableEdgeStopsThePush()
    {
        var play = Ready(Puppeteer);
        play.Put(B(1), "6", "10");
        play.Do(Puppet("B1", "0", "10"), 6, 6, 6);
        // More than half of the base must stay on the table: the centre stops just inside the edge.
        var at = play.M(B(1)).Position!.Value;
        Assert.InRange(at.X.Thousandths, 1, 2);
        Assert.Equal(L("10"), at.Y);
    }

    [Fact]
    public void AMineMarkerIsNeverTriggeredByAPush()
    {
        var play = Ready(Puppeteer);
        play.Put(B(1), "15", "10");
        play.State = play.State with { Mines = [new MineMarker("mine-1", P("22", "10"))] };
        play.Do(Puppet("B1", "20", "10"), 6, 6, 6);
        var body = Board.BodyOf(play.State, play.M(B(1)))!;
        // The base stops just outside the marker's reach: a hair further would be inside it.
        Assert.False(Mines.IsWithin(body, body.Centre, P("22", "10")));
        Assert.True(Mines.IsWithin(body, new Point2(body.Centre.X + Length.FromThousandths(20), body.Centre.Y), P("22", "10")));
        Assert.True(body.Centre.X < L("19"));
        Assert.DoesNotContain(play.Log, e => e is MineExploded);
    }

    [Fact]
    public void ThePuppetMastersTargetsAreChecked()
    {
        var play = Ready(Puppeteer);
        play.Put(B(1), "15", "10");
        play.Refused(Puppet("A1", "20", "10"), ReasonCode.NotACandidate);        // not itself
        play.Refused(Puppet("A4", "10", "20"), ReasonCode.NotACandidate);        // 20" off
        play.Refused(Puppet("B1", "15", "10"), ReasonCode.InvalidChoice);        // no direction
        play.Refused(new TakeSelfAction(Seat.One, A(1), SelfActionKind.PuppetMaster), ReasonCode.UnknownModel);
        play.Refused(new TakeSelfAction(Seat.One, A(1), SelfActionKind.PuppetMaster) { Target = B(1) }, ReasonCode.InvalidChoice);
        // Out of sight behind a wall.
        var blind = Ready(Puppeteer, Wall("w", "12", "5", "13", "15"));
        blind.Put(B(1), "15", "10");
        blind.Refused(Puppet("B1", "20", "10"), ReasonCode.NotACandidate);
    }

    [Fact]
    public void ForgedLogsAreRefused()
    {
        var play = Ready(Caller);
        var before = play.State;
        var events = play.Do(Loud, 6, 6, 6, 6).Events;
        var rolled = events.TakeWhile(e => e is not ModelsMoved).ToList();
        var state = before;
        foreach (var e in rolled)
        {
            state = Reducer.Apply(state, e);
        }
        // The moves are owed: nothing else may come first, and they must be what the rules say.
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(state, new ActivationEnded(A(1), ActivationEndReason.Voluntary)));
        var genuine = events.OfType<ModelsMoved>().Single();
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(state, genuine with { Moves = [genuine.Moves[0] with { To = P("20", "10") }, genuine.Moves[1]] }));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(state, genuine with { Moves = [genuine.Moves[0]] }));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(state, genuine with { Die = 3 }));
        var done = Reducer.Apply(state, genuine);
        Assert.Null(done.ForcedMovesOwed);
        Assert.Equal(StateHash.Compute(play.State), StateHash.Compute(done));
    }
}
