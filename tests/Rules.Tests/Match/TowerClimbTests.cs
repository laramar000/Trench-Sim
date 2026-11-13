using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.Scenarios;
using Rules.Core.State;
using static Rules.Tests.Geometry.Geo;
using static Rules.Tests.Match.BoardPlay;
using static Rules.Tests.Match.MeleePlay;

namespace Rules.Tests.Match;

// Towers proposal, stage T6: climbing straight up or down a tower's side away from its ladders (Climbing Sheer Surfaces,
// 1.0.2 p. 39; U75, U76), through the server's own TurnSequence. The south tower stands at x 16.5-19.5", y 5-8", 2" high,
// with ladders at y 6.5. For a 1" base a climb up the west side at y 5.8 goes from (16, 5.8) to (17, 5.8). Two dice of 7
// or more pass a Success Roll; 4+3 passes and 3+3 fails.
public class TowerClimbTests
{
    private static readonly Scenario Towers = ClaimNoMansLand.CreateWithTowers();

    private static BoardPlay On(string ax, string ay, string bx = "30", string by = "30") => SkirmishWith(Towers, ax, ay, bx, by);

    private static ModelBody Body(BoardPlay play, ModelId id) => Board.BodyOf(play.State, play.M(id))!;

    [Fact]
    [Trait("Rule", "Climbing Sheer Surfaces p. 39: a Risky Success Roll; on a Success the model climbs and carries on")]
    public void AClimbUpTheSideRollsFirstThenMakesTheWholeMove()
    {
        var play = On("15", "5.8");
        var result = play.Do(new MoveModel(Seat.One, A(1), Path(("16", "5.8"), ("17", "5.8"), ("18", "5.8"))), 4, 3);
        Assert.Equal([typeof(SideClimbDeclared), typeof(SideClimbRolled), typeof(ModelMoved)], result.Events.Select(e => e.GetType()));
        Assert.True(result.Events.OfType<SideClimbRolled>().Single().Check.Risky);
        var moved = result.Events.OfType<ModelMoved>().Single();
        Assert.Equal(MoveKind.Move, moved.Kind);
        Assert.Equal(L("5"), moved.Cost); // 1" to the side, 1" over the edge, 2" up, 1" on the platform (U66, U75)
        Assert.Equal(P("18", "5.8"), play.M(A(1)).Position);
        Assert.Equal(L("2"), Body(play, A(1)).Elevation);
        Assert.Equal(A(1), play.State.ActiveModel);
        Assert.True(play.State.Actions.HasFlag(ActionsTaken.Move));
        Assert.Null(play.State.InProgress);
        play.Refused(new MoveModel(Seat.One, A(1), Path(("18", "6"))), ReasonCode.ActionAlreadyTaken);
    }

    [Fact]
    [Trait("Rule", "Climbing Sheer Surfaces p. 39: on a Failure the model moves no further and its Activation ends")]
    public void AFailedClimbStopsTheModelWhereTheClimbStartsAndEndsTheActivation()
    {
        var play = On("15", "5.8");
        var result = play.Do(new MoveModel(Seat.One, A(1), Path(("16", "5.8"), ("17", "5.8"), ("18", "5.8"))), 3, 3);
        Assert.Equal([typeof(SideClimbDeclared), typeof(SideClimbRolled), typeof(ModelMoved), typeof(ActivationEnded)], result.Events.Select(e => e.GetType()));
        var moved = result.Events.OfType<ModelMoved>().Single();
        Assert.Equal(Path(("16", "5.8")).ToArray(), moved.Path.ToArray());
        Assert.Equal(L("1"), moved.Cost);
        Assert.Equal(ActivationEndReason.RiskyFailure, result.Events.OfType<ActivationEnded>().Single().Reason);
        Assert.Equal(P("16", "5.8"), play.M(A(1)).Position);
        Assert.Equal(Length.Zero, Body(play, A(1)).Elevation);
        Assert.Null(play.State.ActiveModel);
    }

    [Fact]
    public void AFailedClimbFromTheSideItselfMovesNothing()
    {
        var play = On("16", "5.8");
        var result = play.Do(new MoveModel(Seat.One, A(1), Path(("17", "5.8"), ("18", "5.8"))), 3, 3);
        Assert.Equal([typeof(SideClimbDeclared), typeof(SideClimbRolled), typeof(ActivationEnded)], result.Events.Select(e => e.GetType()));
        Assert.Equal(P("16", "5.8"), play.M(A(1)).Position);
        Assert.Null(play.State.ActiveModel);
    }

    [Fact]
    [Trait("Rule", "Climbing Sheer Surfaces p. 39: a model may Climb up or down")]
    public void AClimbDownTheSideIsRolledToo()
    {
        var play = On("18", "7.4");
        var result = play.Do(new MoveModel(Seat.One, A(1), Path(("17", "7.4"), ("16", "7.4"), ("15", "7.4"))), 5, 2);
        Assert.Single(result.Events.OfType<SideClimbRolled>());
        Assert.Equal(P("15", "7.4"), play.M(A(1)).Position);
        Assert.Equal(Length.Zero, Body(play, A(1)).Elevation);
    }

    [Fact]
    [Trait("Rule", "Climbing Sheer Surfaces p. 39: the model must have enough movement to clear the entire surface")]
    public void AClimbTheMoveCannotFinishIsRefusedBeforeAnyDie()
    {
        // 4" to the side, 1" over the edge and 2" up is 7", more than the 6" Movement.
        var play = On("12", "5.8");
        var refusal = play.Refused(new MoveModel(Seat.One, A(1), Path(("16", "5.8"), ("17", "5.8"))), ReasonCode.MoveIllegal);
        Assert.StartsWith("The path costs 7.000\"", refusal.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ALadderClimbStillNeedsNoRoll()
    {
        var play = On("15", "6.5");
        var result = play.Do(new MoveModel(Seat.One, A(1), Path(("16", "6.5"), ("17", "6.5"), ("18", "6.5"))));
        Assert.Empty(result.Events.OfType<SideClimbDeclared>());
        Assert.Equal(P("18", "6.5"), play.M(A(1)).Position);
    }

    [Fact]
    public void AMoveStraightOntoTheTowerIsRefusedWithTheClimbingReason()
    {
        var play = On("15", "6");
        var refusal = play.Refused(new MoveModel(Seat.One, A(1), Path(("18", "6"))), ReasonCode.MoveIllegal);
        Assert.Equal(Movement.SideClimbText, refusal.Text);
        // A slanted climb (not straight up the side) is refused the same way.
        refusal = play.Refused(new MoveModel(Seat.One, A(1), Path(("16", "6"), ("17", "6.2"))), ReasonCode.MoveIllegal);
        Assert.Equal(Movement.SideClimbText, refusal.Text);
    }

    [Fact]
    [Trait("Rule", "Blood Markers p. 28: the opponent may spend them as -1 DICE on a Success Roll")]
    public void TheOpponentDecidesAboutMarkersBeforeTheClimbRoll()
    {
        var play = On("15", "5.8");
        play.Mark(A(1), 2);
        var declared = play.Do(new MoveModel(Seat.One, A(1), Path(("16", "5.8"), ("17", "5.8")))); // no dice yet
        Assert.Equal([typeof(SideClimbDeclared), typeof(DecisionRequested)], declared.Events.Select(e => e.GetType()));
        Assert.Equal(new PendingDecision(DecisionKind.SpendMarkersOnSuccessRoll, Seat.Two), play.State.Pending);
        // The climb in progress is part of the state hash (a match with no climb in progress hashes as before).
        Assert.Contains(":climb=1", StateHash.Canonical(play.State), StringComparison.Ordinal);
        // One marker spent: one penalty die, three rolled and the lowest two kept (3 and 1: a failure).
        var result = play.Do(new SpendMarkers(Seat.Two, 1), 6, 3, 1);
        var roll = result.Events.OfType<SideClimbRolled>().Single();
        Assert.Equal(-1, roll.Check.NetDice);
        Assert.Equal(Rules.Core.Dice.SuccessOutcome.Failure, roll.Check.Outcome);
        Assert.Equal(P("16", "5.8"), play.M(A(1)).Position);
        Assert.Null(play.State.ActiveModel);
    }

    [Fact]
    public void ADashCanClimbAfterItsOwnRollAndTheMoveStaysFree()
    {
        var play = On("15", "5.8");
        var result = play.Do(new DashModel(Seat.One, A(1), Path(("16", "5.8"), ("17", "5.8"))), 4, 3, 5, 5);
        Assert.Equal(
            [typeof(DashDeclared), typeof(DashRolled), typeof(SideClimbDeclared), typeof(SideClimbRolled), typeof(ModelMoved)],
            result.Events.Select(e => e.GetType()));
        Assert.Equal(MoveKind.Dash, result.Events.OfType<SideClimbDeclared>().Single().Kind);
        Assert.Equal(MoveKind.Dash, result.Events.OfType<ModelMoved>().Single().Kind);
        Assert.Equal(P("17", "5.8"), play.M(A(1)).Position);
        Assert.False(play.State.Actions.HasFlag(ActionsTaken.Move));
        play.Do(new MoveModel(Seat.One, A(1), Path(("18", "6"))));
        Assert.Equal(L("2"), Body(play, A(1)).Elevation);
    }

    [Fact]
    [Trait("Rule", "Provisional U76: a Retreat does not climb a side")]
    public void ARetreatCannotClimbTheSide()
    {
        var play = On("16", "5.8", "15", "5.8");
        Assert.True(Board.IsEngaged(play.State, play.M(A(1))));
        var refusal = play.Refused(new RetreatModel(Seat.One, A(1), Path(("17", "5.8"), ("18", "5.8"))), ReasonCode.MoveIllegal);
        Assert.Equal(Movement.SideClimbOnlyMoveText, refusal.Text);
    }

    [Fact]
    [Trait("Rule", "Provisional U76: a failed climb must leave the model somewhere it can stop")]
    public void AClimbThatWouldStopOnAFriendIsRefused()
    {
        var play = On("14.5", "5.8");
        play.Put(A(2), "16", "5");
        var refusal = play.Refused(new MoveModel(Seat.One, A(1), Path(("16", "5.8"), ("17", "5.8"))), ReasonCode.MoveIllegal);
        Assert.Equal(Movement.SideClimbStopText, refusal.Text);
    }

    [Fact]
    [Trait("Rule", "Provisional U76: one climb up or down a side per move")]
    public void TwoSideClimbsInOneMoveAreRefused()
    {
        var mover = new ModelBody("A1", P("15.5", "5.8"), L("0.5"));
        var result = Movement.CheckPath(Towers.Battlefield, mover, Path(("16", "5.8"), ("17", "5.8"), ("19", "5.8"), ("20", "5.8")), L("20"), [], sideClimb: true);
        Assert.Equal(MoveRefusal.SideClimbNotAllowed, result.Reason);
        Assert.Equal(Movement.SideClimbOnceText, result.Text);
        // One climb is fine, and the result says where the climb reaches.
        var once = Movement.CheckPath(Towers.Battlefield, mover, Path(("16", "5.8"), ("17", "5.8"), ("18", "5.8")), L("20"), [], sideClimb: true);
        Assert.True(once.IsLegal);
        Assert.Equal(1, once.SideClimb);
        // Without sideClimb (a Charge's planner, a Retreat) the same path is refused.
        Assert.Equal(MoveRefusal.SideClimbNotAllowed, Movement.CheckPath(Towers.Battlefield, mover, Path(("16", "5.8"), ("17", "5.8")), L("20"), []).Reason);
    }

    [Fact]
    [Trait("Rule", "Architecture: a forged log cannot skip the climb roll")]
    public void AMoveUpTheSideWithoutItsRollIsAnInvalidEvent()
    {
        var play = On("15", "5.8");
        var path = Path(("16", "5.8"), ("17", "5.8"));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(play.State, new ModelMoved(A(1), MoveKind.Move, path, L("4"))));
        // And a declared climb moves only after its roll.
        var declared = Reducer.Apply(play.State, new SideClimbDeclared(A(1), MoveKind.Move, path));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(declared, new ModelMoved(A(1), MoveKind.Move, path, L("4"))));
    }
}
