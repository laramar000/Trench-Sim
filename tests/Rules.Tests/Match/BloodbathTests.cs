using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.State;
using static Rules.Tests.Geometry.Geo;
using static Rules.Tests.Match.BoardPlay;

namespace Rules.Tests.Match;

// Bloodbath Rolls (p. 47; U124): spending 6 of the target's Blood Markers (3 if it is Down) turns an Injury Roll into 3D6
// added together (4D6 for a DEADLY weapon); any markers beyond the cost are +1 INJURY DICE each.
public class BloodbathTests
{
    private static readonly ModelProfile DeadlyGun = new(L("6"), L("0.5"), L("18")) { RangedWeaponKeywords = WeaponKeywords.Deadly };

    // A1 is activated at (4,2) and B1 stands at (10,2): a hit leaves the attacker's choice about B1's markers pending.
    private static BoardPlay Duel(Func<ModelId, ModelProfile>? profiles = null)
    {
        var play = new BoardPlay(2, profiles).ToActivation();
        play.Put(A(1), "4", "2").Put(B(1), "10", "2");
        play.Do(new ActivateModel(Seat.One, A(1)));
        return play;
    }

    private static Shoot Fire() => new(Seat.One, A(1), B(1));

    private static SpendMarkers Bloodbath(int count) => new(Seat.One, count) { Bloodbath = true };

    private static MatchState Fold(MatchState state, IEnumerable<GameEvent> events) => events.Aggregate(state, Reducer.Apply);

    [Fact]
    [Trait("Rule", "Bloodbath Rolls p. 47: 6 Blood Markers, or 3 if the target is Down")]
    public void AStandingTargetNeedsSixMarkersAndADownOneThree()
    {
        var five = Duel();
        five.Mark(B(1), 5).Do(Fire(), 4, 3);
        Assert.DoesNotContain(TurnSequence.LegalCommands(five.State), c => c is SpendMarkers { Bloodbath: true });
        five.Refused(Bloodbath(5), ReasonCode.InvalidMarkerCount);

        var six = Duel();
        six.Mark(B(1), 6).Do(Fire(), 4, 3);
        Assert.Equal(new[] { Bloodbath(6) }, TurnSequence.LegalCommands(six.State).OfType<SpendMarkers>().Where(c => c.Bloodbath).ToArray());
        six.Refused(Bloodbath(5), ReasonCode.InvalidMarkerCount);

        var down = Duel();
        down.SetStatus(B(1), ModelStatus.Down).Mark(B(1), 4).Do(Fire(), 4, 3);
        Assert.Equal(new[] { Bloodbath(3), Bloodbath(4) }, TurnSequence.LegalCommands(down.State).OfType<SpendMarkers>().Where(c => c.Bloodbath).ToArray());
        down.Refused(Bloodbath(2), ReasonCode.InvalidMarkerCount);
    }

    [Fact]
    [Trait("Rule", "Bloodbath Rolls p. 47: roll 3D6 and add all 3 dice together")]
    public void ABloodbathRollAddsThreeDiceTogether()
    {
        var play = Duel();
        play.Mark(B(1), 6).Do(Fire(), 4, 3);
        var result = play.Do(Bloodbath(6), 1, 2, 3);

        var spent = result.Events.OfType<MarkersSpent>().Single();
        Assert.Equal((6, MarkerSpendPurpose.Bloodbath), (spent.Count, spent.Purpose));
        var rolled = result.Events.OfType<InjuryRolled>().Single();
        Assert.True(rolled.Bloodbath);
        Assert.Equal((3, 3, 6, 0), (rolled.Injury.Dice.Rolled.Count, rolled.Injury.Dice.Kept.Count, rolled.Injury.Total, rolled.Injury.NetDice));
        Assert.Equal(InjuryOutcome.MinorHit, rolled.Injury.Outcome); // two dice would have been 3; three give 6
        Assert.Equal(1, play.M(B(1)).BloodMarkers); // all six spent, then the Minor Hit's own marker
    }

    [Fact]
    [Trait("Rule", "Bloodbath Rolls p. 47: a Down target costs 3 markers, and the Injury Roll is made against a Down model")]
    public void ADownTargetCostsThree()
    {
        var play = Duel();
        play.SetStatus(B(1), ModelStatus.Down).Mark(B(1), 3).Do(Fire(), 4, 3);
        var rolled = play.Do(Bloodbath(3), 2, 3, 2).Events.OfType<InjuryRolled>().Single();
        Assert.True(rolled.Bloodbath);
        Assert.True(rolled.Injury.TargetAlreadyDown);
        Assert.Equal(7, rolled.Injury.Total);
        Assert.Equal(InjuryOutcome.Down, rolled.Injury.Outcome);
        Assert.Equal(2, play.M(B(1)).BloodMarkers); // a Down result on a Down model places 2
    }

    [Fact]
    [Trait("Rule", "Bloodbath Rolls p. 47: INJURY DICE are added as usual, keeping the 3 highest")]
    public void MarkersBeyondTheCostAreInjuryDice()
    {
        var play = Duel();
        play.SetStatus(B(1), ModelStatus.Down).Mark(B(1), 5).Do(Fire(), 4, 3);
        var rolled = play.Do(Bloodbath(5), 1, 1, 6, 5, 4).Events.OfType<InjuryRolled>().Single();
        Assert.Equal((2, 5, 3, 15), (rolled.Injury.NetDice, rolled.Injury.Dice.Rolled.Count, rolled.Injury.Dice.Kept.Count, rolled.Injury.Total));
        Assert.Equal(InjuryOutcome.OutOfAction, rolled.Injury.Outcome);
        Assert.Equal(ModelStatus.OutOfAction, play.M(B(1)).Status);
    }

    [Fact]
    [Trait("Rule", "Bloodbath Rolls p. 47: DEADLY makes it 4D6 added together")]
    public void ADeadlyBloodbathRollIsFourDice()
    {
        var play = Duel(id => id.Value == "A1" ? DeadlyGun : Rifleman);
        play.Mark(B(1), 6).Do(Fire(), 4, 3);
        var rolled = play.Do(Bloodbath(6), 1, 2, 3, 4).Events.OfType<InjuryRolled>().Single();
        Assert.True(rolled.Bloodbath);
        Assert.Equal((4, 4, 10), (rolled.Injury.Dice.Rolled.Count, rolled.Injury.Dice.Kept.Count, rolled.Injury.Total));
        Assert.True(rolled.Injury.AllFour);
    }

    [Fact]
    [Trait("Rule", "Bloodbath Rolls p. 47: only on an Injury Roll, never on a Success Roll")]
    public void ABloodbathCannotAnswerASuccessRollDecision()
    {
        var play = Duel();
        play.Mark(A(1), 6).Do(Fire());
        Assert.Equal(DecisionKind.SpendMarkersOnSuccessRoll, play.State.Pending!.Kind);
        play.Refused(new SpendMarkers(Seat.Two, 6) { Bloodbath = true }, ReasonCode.InvalidMarkerCount);
        Assert.DoesNotContain(TurnSequence.LegalCommands(play.State), c => c is SpendMarkers { Bloodbath: true });
    }

    [Fact]
    [Trait("Rule", "Bloodbath Rolls p. 47: a replay accepts only what the decision allowed")]
    public void ForgedBloodbathLogsAreRefused()
    {
        var play = Duel();
        play.Mark(B(1), 6).Do(Fire(), 4, 3);
        var pending = play.State;
        var events = play.Do(Bloodbath(6), 1, 2, 3).Events;
        Assert.Equal(StateHash.Compute(play.State), StateHash.Compute(Fold(pending, events)));

        // The Bloodbath flag removed from the roll, or kept without the spend, or the spend short of its cost.
        var rolled = events.OfType<InjuryRolled>().Single();
        Assert.Throws<InvalidEventException>(() => Fold(pending, events.Select(e => e is InjuryRolled ? rolled with { Bloodbath = false } : e)));
        Assert.Throws<InvalidEventException>(() => Fold(pending, events.Select(e => e is MarkersSpent m ? new MarkersSpent(m.Spender, m.Model, m.Count, MarkerSpendPurpose.RaiseInjuryRoll) : e)));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(pending, new MarkersSpent(Seat.One, B(1), 5, MarkerSpendPurpose.Bloodbath)));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(pending, new MarkersSpent(Seat.One, B(1), 6, MarkerSpendPurpose.ReduceSuccessRoll)));
    }

    [Fact]
    [Trait("Rule", "Bloodbath Rolls p. 47: the state hash tells a Bloodbath Roll from an ordinary one")]
    public void TheHashCoversTheBloodbathFlag()
    {
        var play = Duel();
        play.Mark(B(1), 6).Do(Fire(), 4, 3);
        var plain = play.State;
        var converted = Reducer.Apply(plain, new MarkersSpent(Seat.One, B(1), 6, MarkerSpendPurpose.Bloodbath));
        var raised = Reducer.Apply(plain, new MarkersSpent(Seat.One, B(1), 6, MarkerSpendPurpose.RaiseInjuryRoll));
        Assert.True(converted.InProgress!.Bloodbath);
        Assert.NotEqual(StateHash.Compute(converted), StateHash.Compute(raised));
    }
}
