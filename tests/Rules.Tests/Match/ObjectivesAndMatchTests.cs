using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Events;
using Rules.Core.State;
using static Rules.Tests.Geometry.Geo;
using static Rules.Tests.Match.BoardPlay;

namespace Rules.Tests.Match;

public class ObjectiveControlTests
{
    private static BoardPlay Table() => new BoardPlay(perSide: 3).ToActivation();

    private static Seat? Controller(BoardPlay play) => Board.Controller(play.State, "obj-5"); // x 16 to 20, y 16 to 20

    [Fact]
    [Trait("Rule", "Scenario I p. 152: controlling Objectives")]
    public void AnEmptyObjectiveIsControlledByNobody()
    {
        Assert.Null(Controller(Table()));
    }

    [Fact]
    [Trait("Rule", "Scenario I p. 152: more friendly models on, in or within 1\" than enemy models")]
    public void MoreModelsWithinAnInchControlsAndEqualNumbersControlNothing()
    {
        var play = Table();
        play.Put(A(1), "15.2", "18").Put(A(2), "18", "15.2").Put(B(1), "21", "18"); // 2 against 1, none on the piece
        Assert.Equal(Seat.One, Controller(play));
        play.Put(B(2), "18", "21"); // 2 against 2
        Assert.Null(Controller(play));
        play.Put(B(3), "21", "21"); // 2 against 3
        Assert.Equal(Seat.Two, Controller(play));
    }

    [Fact]
    [Trait("Rule", "Scenario I p. 152: models on the piece beat numbers within 1\"")]
    public void AModelOnThePieceControlsDespiteTheOpponentsGreaterNumbersNearby()
    {
        var play = Table();
        play.Put(A(1), "18", "18").Put(B(1), "21", "18").Put(B(2), "18", "21");
        Assert.Equal(Seat.One, Controller(play));
    }

    [Fact]
    [Trait("Rule", "Scenario I p. 152: when both have models on the piece the count decides")]
    public void WhenBothSidesAreOnThePieceTheCountDecides()
    {
        var play = Table();
        play.Put(A(1), "17", "17").Put(B(1), "19", "19").Put(B(2), "19", "17");
        Assert.Equal(Seat.Two, Controller(play));
    }

    [Theory]
    [InlineData("14.5", true)]   // the base's edge (15.0) is exactly 1" from the piece edge (16)
    [InlineData("14.499", false)] // a thousandth further
    [Trait("Rule", "Scenario I p. 152: within 1\" is measured from the base")]
    public void WithinAnInchIsMeasuredFromTheBase(string x, bool controls)
    {
        var play = Table();
        play.Put(A(1), x, "18");
        Assert.Equal(controls ? Seat.One : null, Controller(play));
    }

    [Fact]
    [Trait("Rule", "Models p. 50: Down models count, Out of Action models are off the table")]
    public void DownModelsCountAndOutOfActionModelsDoNot()
    {
        var play = Table();
        play.Put(A(1), "18", "18").SetStatus(A(1), ModelStatus.Down);
        Assert.Equal(Seat.One, Controller(play));
        play.SetStatus(A(1), ModelStatus.OutOfAction);
        Assert.Null(Controller(play));
    }
}

public class FullMatchScriptTests
{
    // Hand-checked: seat One walks A1 onto the rubble obj-1 in turn one and sits there; seat Two's B1 never leaves its zone.
    private static BoardPlay ScriptedMatch()
    {
        var play = new BoardPlay(perSide: 1).ToActivation();
        play.Do(new ActivateModel(Seat.One, A(1)));
        play.Do(new MoveModel(Seat.One, A(1), Path(("8", "3"))));
        play.Do(new DashModel(Seat.One, A(1), Path(("12", "5"))), 4, 3);
        play.Do(new EndActivation(Seat.One));
        play.Do(new ActivateModel(Seat.Two, B(1)));
        play.Do(new EndActivation(Seat.Two), 6, 1); // end of turn 1: scoring, then turn 2's Initiative roll-off
        Assert.Equal(2, play.State.One.VictoryPoints);
        for (var turn = 2; turn <= 4; turn++)
        {
            play.Do(new ChooseFirstActivation(Seat.One, Seat.One));
            play.Do(new ActivateModel(Seat.One, A(1)));
            play.Do(new EndActivation(Seat.One));
            play.Do(new ActivateModel(Seat.Two, B(1)));
            play.Do(new EndActivation(Seat.Two), turn < 4 ? [6, 1] : []);
        }
        return play;
    }

    [Fact]
    [Trait("Rule", "Scenario I p. 153: 2 VP per Objective controlled at each Turn's end; more VPs wins after Turn 4")]
    public void AScriptedMatchEndsAfterFourTurnsOnVictoryPoints()
    {
        var play = ScriptedMatch();

        Assert.Equal(Phase.Ended, play.State.Phase);
        Assert.Equal(4, play.State.Turn);
        Assert.Equal((8, 0), (play.State.One.VictoryPoints, play.State.Two.VictoryPoints));
        Assert.Equal(new MatchOutcome(Seat.One, OutcomeReason.VictoryPoints), play.State.Outcome);
        Assert.Equal(4, play.Log.OfType<ObjectivesScored>().Count());
        var first = play.Log.OfType<ObjectivesScored>().First();
        Assert.Equal(5, first.Results.Length);
        Assert.Equal(Seat.One, first.Results.Single(r => r.ObjectiveId == "obj-1").Controller);
        Assert.All(first.Results.Where(r => r.ObjectiveId != "obj-1"), r => Assert.Equal((null, 0), (r.Controller, r.Points)));
    }

    [Fact]
    [Trait("Rule", "Architecture: event sourcing")]
    public void ReplayingTheLogGivesTheSameStateAndEveryPrefixHashMatches()
    {
        var play = ScriptedMatch();
        var state = MatchState.None;
        var live = new Dictionary<int, string>();
        for (var i = 0; i < play.Log.Count; i++)
        {
            state = Reducer.Apply(state, play.Log[i]);
            live[i] = StateHash.Compute(state);
        }
        Assert.Equal(StateHash.Compute(play.State), StateHash.Compute(Reducer.Replay(play.Log)));
        foreach (var length in new[] { 1, 5, play.Log.Count / 2, play.Log.Count - 1 })
        {
            Assert.Equal(live[length - 1], StateHash.Compute(Reducer.Replay(play.Log.Take(length))));
        }
    }

    [Fact]
    [Trait("Rule", "Scenario I p. 153: equal Victory Points are not a win (provisional draw)")]
    public void EqualVictoryPointsAreADraw()
    {
        var play = new BoardPlay(perSide: 1, turnLimit: 1).ToActivation();
        play.Put(A(1), "12", "6").Put(B(1), "24", "6"); // each on its own Objective piece
        play.Do(new ActivateModel(Seat.One, A(1)));
        play.Do(new EndActivation(Seat.One));
        play.Do(new ActivateModel(Seat.Two, B(1)));
        play.Do(new EndActivation(Seat.Two));

        Assert.Equal(Phase.Ended, play.State.Phase);
        Assert.Equal((2, 2), (play.State.One.VictoryPoints, play.State.Two.VictoryPoints));
        Assert.Equal(new MatchOutcome(null, OutcomeReason.VictoryPoints), play.State.Outcome);
    }

    [Fact]
    [Trait("Rule", "Winning p. 153: a fleeing warband loses at once, with no scoring")]
    public void AWarbandThatFleesLosesBeforeTheTurnIsScored()
    {
        var play = new BoardPlay(perSide: 2).ToActivation();
        play.Put(A(2), "18", "18").SetStatus(A(1), ModelStatus.OutOfAction);
        play.Do(new ActivateModel(Seat.One, A(2)));
        play.Do(new EndActivation(Seat.One));
        play.Do(new ActivateModel(Seat.Two, B(1)));
        play.Do(new EndActivation(Seat.Two));
        play.Do(new ActivateModel(Seat.Two, B(2)));
        play.Do(new EndActivation(Seat.Two), 1, 1); // seat One's Morale Check fails
        Assert.Equal(new PendingDecision(DecisionKind.FailedMorale, Seat.One), play.State.Pending);
        play.Do(new ResolveFailedMorale(Seat.One, MoraleFailureChoice.SoundRetreat));

        Assert.Equal(new MatchOutcome(Seat.Two, OutcomeReason.OpponentFled), play.State.Outcome);
        Assert.Empty(play.Log.OfType<ObjectivesScored>());
        Assert.Equal((0, 0), (play.State.One.VictoryPoints, play.State.Two.VictoryPoints));
    }

    [Fact]
    [Trait("Rule", "Architecture: the reducer checks every event")]
    public void AForgedScoreIsRejectedByTheReducer()
    {
        var play = ScriptedMatch();
        var turnOne = play.Log.TakeWhile(e => e is not ObjectivesScored).ToList();
        var forged = new ObjectivesScored(1, play.Log.OfType<ObjectivesScored>().First().Results.Select(r => r with { Controller = Seat.Two, Points = 2 }));
        Assert.Throws<InvalidEventException>(() => Reducer.Replay([.. turnOne, forged]));
    }

    [Fact]
    [Trait("Rule", "Architecture: the reducer checks every event")]
    public void AForgedMoveOrInjuryIsRejectedByTheReducer()
    {
        var play = new BoardPlay(perSide: 1).ToActivation();
        play.Do(new ActivateModel(Seat.One, A(1)));
        var tooFar = new ModelMoved(A(1), MoveKind.Move, Path(("30", "3")), L("28"));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(play.State, tooFar));
        var wrongCost = new ModelMoved(A(1), MoveKind.Move, Path(("4", "3")), L("1"));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(play.State, wrongCost));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(play.State, new ModelStoodUp(A(1))));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(play.State, new ModelDeployed(Seat.One, A(1), P("3", "3"))));
    }
}
