using System.Collections.Immutable;
using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.Scenarios;
using Rules.Core.State;
using static Rules.Tests.Geometry.Geo;
using static Rules.Tests.Match.BoardPlay;

namespace Rules.Tests.Match;

// Trench Warfare's Mine Markers (1.0.2 p. 162 Slaughter Mines, p. 56 MINED, p. 57 SHRAPNEL; U93), through the server's
// own TurnSequence. Seat Two defends the east zone; its Mine Placement Area is x 18-30. A 1" base comes within 3" of a
// 1" marker when the centres are 4" apart. Unless a test says otherwise mine-1 stands at (20, 25) on the attacker's
// way, and mine-2 to mine-12 along x 29.5, out of the way.
public class MineTests
{
    private static readonly ImmutableArray<string> Mine1IsADud = ["mine-1", "mine-2", "mine-3", "mine-4"];
    private static readonly ImmutableArray<string> Mine1IsLive = ["mine-2", "mine-3", "mine-4", "mine-5"];

    private static ImmutableArray<Point2> Markers(params Point2[] first) =>
        [.. first, .. Enumerable.Range(0, Mines.Count - first.Length).Select(k => P("29.5", (1.5 + 3 * k).ToString(System.Globalization.CultureInfo.InvariantCulture)))];

    private static BoardPlay Play(ImmutableArray<string> duds, int perSide = 1, Func<ModelId, ModelProfile>? profileFor = null, params Point2[] first) =>
        TrenchWarfare(perSide, profileFor).ToTrenchActivation(Markers(first.Length == 0 ? [P("20", "25")] : first), duds);

    private static BoardPlay Active(BoardPlay play, ModelId id, string x, string y)
    {
        play.Put(id, x, y);
        play.Do(new ActivateModel(Seat.One, id));
        return play;
    }

    private static Type[] Kinds(DecideResult result) => [.. result.Events.Select(e => e.GetType())];

    [Fact]
    [Trait("Rule", "Trench Warfare p. 162: after the attacker is chosen the defender sets up 12 Mine Markers and secretly picks 4 duds")]
    public void TheDefenderLaysTheMinesBeforeAnyoneDeploys()
    {
        var play = TrenchWarfare();
        play.Do(new StartMatch(), 6, 1);
        var chosen = play.Do(new ChooseAttacker(Seat.One, Seat.One));
        Assert.Equal(new DecisionRequested(DecisionKind.LayMines, Seat.Two), chosen.Events[^1]);
        Assert.Equal(R("18", "0", "30", "36"), Board.MinePlacementArea(play.State));
        play.Refused(new PlaceModel(Seat.One, A(1), P("2", "3")), ReasonCode.DecisionPending);
        play.Refused(new LayMines(Seat.One, Markers(), Mine1IsADud), ReasonCode.NotYourTurn);

        var suggested = Assert.IsType<LayMines>(Assert.Single(TurnSequence.LegalCommands(play.State)));
        var laid = play.Do(suggested);
        var e = Assert.IsType<MinesLaid>(laid.Events[0]);
        Assert.Equal(Enumerable.Range(1, 12).Select(i => $"mine-{i}"), e.Markers.Select(m => m.Id));
        Assert.Equal(new DeploymentOrderSet(Seat.One), laid.Events[^1]);
        Assert.Equal(12, play.State.Mines.Length);
        Assert.True(play.State.MinesLaid && play.State.DudsKnown);
        Assert.Equal(4, play.State.Duds.Length);
    }

    [Theory]
    [Trait("Rule", "Trench Warfare p. 162: 12 markers within the Mine Placement Area (p. 161 map), 4 of them duds")]
    [InlineData("eleven")]
    [InlineData("outside")]
    [InlineData("overlap")]
    [InlineData("wall")]
    [InlineData("three duds")]
    [InlineData("same dud twice")]
    [InlineData("unknown dud")]
    public void AnIllegalLayoutIsRefused(string what)
    {
        var play = TrenchWarfare(1, null, Wall("wall", "24", "30", "25", "34"));
        play.Do(new StartMatch(), 6, 1);
        play.Do(new ChooseAttacker(Seat.One, Seat.One));
        var markers = Markers(P("20", "25"));
        var duds = Mine1IsADud;
        switch (what)
        {
            case "eleven": markers = markers.RemoveAt(11); break;
            case "outside": markers = markers.SetItem(0, P("17.6", "25")); break;
            case "overlap": markers = markers.SetItem(1, P("20.9", "25")); break;
            case "wall": markers = markers.SetItem(0, P("24.5", "33")); break;
            case "three duds": duds = duds.RemoveAt(3); break;
            case "same dud twice": duds = duds.SetItem(3, "mine-1"); break;
            case "unknown dud": duds = duds.SetItem(3, "mine-13"); break;
        }
        play.Refused(new LayMines(Seat.Two, markers, duds), ReasonCode.MinesIllegal);
        // Touching markers, and a marker right at the area's edge, are fine.
        play.Do(new LayMines(Seat.Two, Markers(P("18.5", "25"), P("19.5", "25")), Mine1IsADud));
    }

    [Fact]
    [Trait("Rule", "Slaughter Mines p. 162: the move halts within 3\"; a dud does nothing and the model carries on")]
    public void ADudHaltsTheMoveThenTheModelCarriesOn()
    {
        var play = Active(Play(Mine1IsADud), A(1), "14", "25");
        var result = play.Do(new MoveModel(Seat.One, A(1), Path(("20", "25"))));
        Assert.Equal([typeof(ModelMoved), typeof(MineRevealed), typeof(MoveResumed)], Kinds(result));
        Assert.Equal(new MineRevealed("mine-1", A(1), live: false), result.Events[1]);
        Assert.Equal(Path(("20", "25")).ToArray(), ((MoveResumed)result.Events[2]).Path.ToArray());
        Assert.Equal(P("20", "25"), play.M(A(1)).Position);
        Assert.DoesNotContain(play.State.Mines, m => m.Id == "mine-1");
        Assert.Equal(11, play.State.Mines.Length);
        Assert.Null(play.State.MineStop);
        Assert.Equal(A(1), play.State.ActiveModel);
        Assert.True(play.State.Actions.HasFlag(ActionsTaken.Move));
    }

    [Fact]
    public void TheModelHaltsWhereItsBaseFirstComesWithinThreeInches()
    {
        var play = Active(Play(Mine1IsADud), A(1), "14", "25");
        var state = Reducer.Apply(play.State, new ModelMoved(A(1), MoveKind.Move, Path(("20", "25")), L("6")));
        Assert.Equal(P("16", "25"), state.FindModel(A(1))!.Position);
        var stop = state.MineStop!;
        Assert.Equal(new MineStopPoint("mine-1", P("16", "25"), 0), Assert.Single(stop.Stops));
        Assert.False(stop.Revealed);
        // Nothing else may happen until the marker is revealed.
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(state, new ActivationEnded(A(1), ActivationEndReason.Voluntary)));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(state, new MoveResumed(A(1), Path(("20", "25")))));
    }

    [Fact]
    [Trait("Rule", "MINED p. 56 and SHRAPNEL p. 57: an Injury Roll, then 1 extra Blood Marker; still Standing, it carries on")]
    public void ALiveMineMakesAnInjuryRollWithShrapnel()
    {
        var play = Active(Play(Mine1IsLive), A(1), "14", "25");
        var result = play.Do(new MoveModel(Seat.One, A(1), Path(("20", "25"))), 2, 2);
        Assert.Equal([typeof(ModelMoved), typeof(MineRevealed), typeof(MineExploded), typeof(MoveResumed)], Kinds(result));
        Assert.True(((MineRevealed)result.Events[1]).Live);
        var injury = ((MineExploded)result.Events[2]).Injury;
        Assert.Equal(InjuryOutcome.MinorHit, injury.Outcome);
        Assert.Equal(0, injury.Modifier);
        Assert.Equal(2, play.M(A(1)).BloodMarkers); // 1 for the Minor Hit, 1 for SHRAPNEL
        Assert.Equal(ModelStatus.Standing, play.M(A(1)).Status);
        Assert.Equal(P("20", "25"), play.M(A(1)).Position);
    }

    [Fact]
    [Trait("Rule", "SHRAPNEL p. 57: the extra Blood Marker comes even on No Effect; armour counts as usual")]
    public void ShrapnelMarksEvenOnNoEffect()
    {
        var armoured = new ModelProfile(L("6"), L("0.5"), L("18"), armour: -1);
        var play = Active(Play(Mine1IsLive, 1, _ => armoured), A(1), "14", "25");
        var result = play.Do(new MoveModel(Seat.One, A(1), Path(("20", "25"))), 1, 1);
        var injury = result.Events.OfType<MineExploded>().Single().Injury;
        Assert.Equal(-1, injury.Modifier);
        Assert.Equal(InjuryOutcome.NoEffect, injury.Outcome);
        Assert.Equal(1, play.M(A(1)).BloodMarkers);
    }

    [Fact]
    [Trait("Rule", "Slaughter Mines p. 162: a model taken Down stops where it halted, and its activation ends (p. 49)")]
    public void AModelTakenDownStopsWhereItHalted()
    {
        var play = Active(Play(Mine1IsLive), A(1), "14", "25");
        var result = play.Do(new MoveModel(Seat.One, A(1), Path(("20", "25"))), 4, 4);
        Assert.Equal([typeof(ModelMoved), typeof(MineRevealed), typeof(MineExploded), typeof(ActivationEnded)], Kinds(result));
        Assert.Equal(ActivationEndReason.TakenDown, ((ActivationEnded)result.Events[^1]).Reason);
        Assert.Equal(ModelStatus.Down, play.M(A(1)).Status);
        Assert.Equal(P("16", "25"), play.M(A(1)).Position);
        Assert.Null(play.State.MineStop);
        Assert.Null(play.State.ActiveModel);
    }

    [Fact]
    public void AMineThatTakesOutTheLastModelEndsTheMatch()
    {
        var play = Active(Play(Mine1IsLive), A(1), "14", "25");
        var result = play.Do(new MoveModel(Seat.One, A(1), Path(("20", "25"))), 5, 5);
        Assert.Equal(new MatchEnded(Seat.Two, OutcomeReason.AllEnemiesOutOfAction), result.Events[^1]);
        Assert.Equal(Phase.Ended, play.State.Phase);
    }

    [Fact]
    [Trait("Rule", "Slaughter Mines p. 162: only moving within 3\" sets a marker off; starting there does not, re-entering does")]
    public void AMarkerAlreadyWithinThreeInchesGoesOffOnlyWhenTheModelComesBack()
    {
        var play = Active(Play(Mine1IsADud), A(1), "17", "25");
        var away = play.Do(new MoveModel(Seat.One, A(1), Path(("15", "25"))));
        Assert.Equal([typeof(ModelMoved)], Kinds(away));

    }

    [Fact]
    public void ReEnteringOnALaterLegSetsItOff()
    {
        var play = Active(Play(Mine1IsADud), A(1), "15.5", "25");
        // Out to 14.5 (5.5" away), then back to 17: it comes within 3" again at x 16.
        var result = play.Do(new MoveModel(Seat.One, A(1), Path(("14.5", "25"), ("17", "25"))));
        Assert.Equal([typeof(ModelMoved), typeof(MineRevealed), typeof(MoveResumed)], Kinds(result));
        var moved = TurnSequence.Decide(Active(Play(Mine1IsADud), A(1), "15.5", "25").State, new MoveModel(Seat.One, A(1), Path(("14.5", "25"), ("17", "25"))), new FixedDice());
        var halted = Reducer.Apply(Active(Play(Mine1IsADud), A(1), "15.5", "25").State, moved.Events[0]);
        Assert.Equal(new MineStopPoint("mine-1", P("16", "25"), 1), halted.MineStop!.Current);
        Assert.Equal(Path(("17", "25")).ToArray(), ((MoveResumed)result.Events[2]).Path.ToArray());
    }

    [Fact]
    [Trait("Rule", "Slaughter Mines p. 162: two markers reached at once halt the model twice, in marker order")]
    public void TwoMarkersReachedAtOnceAreRevealedOneAfterTheOther()
    {
        var play = Active(Play(Mine1IsADud, 1, null, P("20", "27"), P("20", "23")), A(1), "14", "25");
        var result = play.Do(new MoveModel(Seat.One, A(1), Path(("18", "25"))));
        Assert.Equal([typeof(ModelMoved), typeof(MineRevealed), typeof(MoveResumed), typeof(MineRevealed), typeof(MoveResumed)], Kinds(result));
        Assert.Equal("mine-1", ((MineRevealed)result.Events[1]).Marker);
        Assert.Empty(((MoveResumed)result.Events[2]).Path);
        Assert.Equal("mine-2", ((MineRevealed)result.Events[3]).Marker);
        Assert.Equal(P("18", "25"), play.M(A(1)).Position);
    }

    [Fact]
    [Trait("Rule", "U93: a model halts only where it could stand; on a friend's base it halts at the waypoint before")]
    public void AHaltOnAFriendsBaseFallsBackToTheWaypointBefore()
    {
        var play = Play(Mine1IsADud, perSide: 2);
        play.Put(A(2), "16", "25");
        Active(play, A(1), "12", "25");
        var moved = TurnSequence.Decide(play.State, new MoveModel(Seat.One, A(1), Path(("14", "25"), ("17.5", "25"))), new FixedDice());
        Assert.True(moved.IsAccepted, moved.Rejection?.Text);
        var halted = Reducer.Apply(play.State, moved.Events[0]);
        Assert.Equal(new MineStopPoint("mine-1", P("14", "25"), 1), halted.MineStop!.Current);
        play.Do(new MoveModel(Seat.One, A(1), Path(("14", "25"), ("17.5", "25"))));
        Assert.Equal(P("17.5", "25"), play.M(A(1)).Position);
    }

    [Fact]
    [Trait("Rule", "Slaughter Mines p. 162: a Dash halts like a Move")]
    public void ADashHaltsAtAMineAfterItsRoll()
    {
        var play = Active(Play(Mine1IsADud), A(1), "14", "25");
        var result = play.Do(new DashModel(Seat.One, A(1), Path(("20", "25"))), 4, 3);
        Assert.Equal([typeof(DashDeclared), typeof(DashRolled), typeof(ModelMoved), typeof(MineRevealed), typeof(MoveResumed)], Kinds(result));
        Assert.Equal(P("20", "25"), play.M(A(1)).Position);
    }

    [Fact]
    [Trait("Rule", "Slaughter Mines p. 162: a charge halts at a mine and then carries on to its target")]
    public void AChargeHaltsAtAMineThenReachesItsTarget()
    {
        var play = Play(Mine1IsADud, 1, null, P("19", "28"));
        play.Put(B(1), "21", "25");
        Active(play, A(1), "16", "25");
        var result = play.Do(new ChargeModel(Seat.One, A(1), B(1)), 3);
        Assert.Equal([typeof(ChargeDeclared), typeof(ChargeRolled), typeof(ChargeMoved), typeof(MineRevealed), typeof(MoveResumed)], Kinds(result));
        var charge = result.Events.OfType<ChargeMoved>().Single();
        Assert.True(charge.Reached);
        Assert.Equal(charge.Path[^1], play.M(A(1)).Position);
    }

    [Fact]
    [Trait("Rule", "Slaughter Mines p. 162: a retreat halts at a mine too")]
    public void ARetreatHaltsAtAMine()
    {
        var play = Play(Mine1IsADud);
        play.Put(B(1), "13", "25");
        Active(play, A(1), "14", "25");
        var result = play.Do(new RetreatModel(Seat.One, A(1), Path(("18", "25"))));
        Assert.Equal([typeof(RetreatDeclared), typeof(RetreatResolved), typeof(MineRevealed), typeof(MoveResumed)], Kinds(result));
        Assert.Equal(P("18", "25"), play.M(A(1)).Position);
    }

    [Fact]
    [Trait("Rule", "U75 with p. 162: a failed side climb that passes a mine halts, carries on to the climb's start, then ends the activation")]
    public void AFailedClimbThatPassesAMineEndsTheActivationAfterTheMove()
    {
        // The south tower of the towers layout stands at x 16.5-19.5, y 5-8; a mine at (16, 1.85) is just over 4" from
        // (15, 5.8) and just under 4" from (16, 5.8). Mines are put on this Claim map directly, as test set-up.
        var play = new BoardPlay(scenario: ClaimNoMansLand.CreateWithTowers()).ToActivation();
        play.State = play.State with
        {
            Mines = [new MineMarker("mine-1", P("16", "1.85"))],
            MinesLaid = true,
            DudsKnown = true,
            Duds = ["mine-1"],
        };
        play.Put(A(1), "15", "5.8");
        play.Do(new ActivateModel(Seat.One, A(1)));
        var result = play.Do(new MoveModel(Seat.One, A(1), Path(("16", "5.8"), ("17", "5.8"), ("18", "5.8"))), 3, 3);
        Assert.Equal([typeof(SideClimbDeclared), typeof(SideClimbRolled), typeof(ModelMoved), typeof(MineRevealed), typeof(MoveResumed), typeof(ActivationEnded)], Kinds(result));
        Assert.Equal(ActivationEndReason.RiskyFailure, ((ActivationEnded)result.Events[^1]).Reason);
        Assert.Equal(P("16", "5.8"), play.M(A(1)).Position);
        Assert.Null(play.State.ActiveModel);
    }

    [Fact]
    [Trait("Rule", "U93: the duds are secret; the state hash leaves them out, so a view without them hashes the same")]
    public void AViewWithoutTheDudsHashesTheSameAtEveryEvent()
    {
        var play = Active(Play(Mine1IsLive), A(1), "14", "25");
        var start = play.State;
        var result = play.Do(new MoveModel(Seat.One, A(1), Path(("20", "25"))), 2, 2);
        Assert.DoesNotContain("dud", StateHash.Canonical(play.State), StringComparison.OrdinalIgnoreCase);

        var full = start;
        var secret = start with { Duds = [], DudsKnown = false };
        Assert.Equal(StateHash.Compute(full), StateHash.Compute(secret));
        foreach (var e in result.Events)
        {
            full = Reducer.Apply(full, e);
            secret = Reducer.Apply(secret, e);
            Assert.Equal(StateHash.Compute(full), StateHash.Compute(secret));
        }
        Assert.Equal(StateHash.Compute(play.State), StateHash.Compute(secret));

        // And from the start of the match: the secret copy of MinesLaid gives the same hashes as the whole one.
        var whole = MatchState.None;
        var hidden = MatchState.None;
        foreach (var e in play.Log.TakeWhile(e => e is not ActivationStarted))
        {
            whole = Reducer.Apply(whole, e);
            hidden = Reducer.Apply(hidden, e is MinesLaid laid ? laid.Secret() : e);
            Assert.Equal(StateHash.Compute(whole), StateHash.Compute(hidden));
        }
        Assert.True(whole.DudsKnown);
        Assert.False(hidden.DudsKnown);
        Assert.Empty(hidden.Duds);
    }

    [Fact]
    [Trait("Rule", "U93: a log that calls a dud live, or skips a halt, is refused by a view that knows the duds")]
    public void AForgedRevealIsRefused()
    {
        var play = Active(Play(Mine1IsADud), A(1), "14", "25");
        var start = play.State;
        var result = play.Do(new MoveModel(Seat.One, A(1), Path(("20", "25"))));
        var halted = Reducer.Apply(start, result.Events[0]);
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(halted, new MineRevealed("mine-1", A(1), live: true)));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(halted, new MineRevealed("mine-2", A(1), live: false)));
        // A view that does not know the duds takes the server's word.
        var secret = start with { Duds = [], DudsKnown = false };
        var told = Reducer.Apply(Reducer.Apply(secret, result.Events[0]), new MineRevealed("mine-1", A(1), live: true));
        Assert.True(told.MineStop!.Live);
        Assert.Throws<InvalidOperationException>(() => TurnSequence.Decide(secret, new MoveModel(Seat.One, A(1), Path(("20", "25"))), new FixedDice()));
    }

    [Fact]
    public void TheDudsOfASecretCopyAreGone()
    {
        var laid = new MinesLaid(Seat.Two, [.. Markers(P("20", "25")).Select((p, i) => new MineMarker(Mines.IdOf(i), p))], Mine1IsADud);
        Assert.Empty(laid.Secret().Duds);
        Assert.Equal(laid.Markers, laid.Secret().Markers);
        Assert.Throws<ArgumentException>(() => new MinesLaid(Seat.Two, laid.Markers, ["mine-1"]));
        Assert.Throws<ArgumentException>(() => new MinesLaid(Seat.Two, laid.Markers.RemoveAt(0), []));
    }
}
