using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Events;
using Rules.Core.State;

namespace Rules.Tests;

// A command carrying a value that is not a member of its enum (a cast number, a damaged message) must be
// refused like any other illegal command: no events, no dice, no change, and never read as a valid default.
public class MalformedCommandTests
{
    private const Seat NoSuchSeat = (Seat)7;
    private const MoraleFailureChoice NoSuchChoice = (MoraleFailureChoice)9;

    // Initiative holder (seat One, with fewer models) is asked who activates first.
    private static Play AtFirstActivationChoice()
    {
        var play = new Play(Play.Setup(2, 3));
        play.Do(new StartMatch(), 6, 1);
        play.Do(new ChooseDeploymentZone(Seat.One, "north"));
        play.DeployAll();
        return play;
    }

    // Both warbands have lost half their models, so the Initiative holder is asked who checks first.
    private static Play AtMoraleOrderChoice()
    {
        var play = new Play(Play.Setup(3, 3)).ToActivation();
        foreach (var id in new[] { "A1", "A2", "B1", "B2" })
        {
            play.SetStatus(id, ModelStatus.Down);
        }
        play.RunActivations();
        return play;
    }

    // Seat One alone must check and fails (1 and 1), so it is asked to become Shaken or sound the retreat.
    private static Play AtFailedMoraleChoice()
    {
        var play = new Play(Play.Setup(3, 3)).ToActivation();
        play.SetStatus("A1", ModelStatus.Down);
        play.SetStatus("A2", ModelStatus.Down);
        play.RunActivations(1, 1);
        return play;
    }

    private static void AssertRefusedUntouched(Play play, Command command, DecisionKind pendingKind, Seat pendingSeat)
    {
        Assert.Equal(new PendingDecision(pendingKind, pendingSeat), play.State.Pending);
        var hashBefore = StateHash.Compute(play.State);
        var canonicalBefore = StateHash.Canonical(play.State);
        var dice = new FixedDice(4, 3, 4, 3);

        var result = TurnSequence.Decide(play.State, command, dice);

        Assert.False(result.IsAccepted);
        Assert.Equal(ReasonCode.InvalidChoice, result.Rejection!.Code);
        Assert.Empty(result.Events);
        Assert.Null(result.State);
        Assert.Equal(4, dice.Remaining);
        Assert.Equal(hashBefore, StateHash.Compute(play.State));
        Assert.Equal(canonicalBefore, StateHash.Canonical(play.State));
        Assert.False(play.State.One.Fled || play.State.Two.Fled);
        Assert.Null(play.State.Outcome);
        Assert.Equal(new PendingDecision(pendingKind, pendingSeat), play.State.Pending);
        Assert.DoesNotContain(play.Log, e => e is WarbandFled or MatchEnded);
    }

    [Fact]
    [Trait("Rule", "1.0.2 The Sequence of Play p.32")]
    public void AnUndefinedFirstActivatorIsRefused()
    {
        var play = AtFirstActivationChoice();
        var holder = play.State.Pending!.Seat;

        AssertRefusedUntouched(play, new ChooseFirstActivation(holder, NoSuchSeat), DecisionKind.ChooseFirstActivation, holder);

        Assert.Null(play.State.FirstActivator);
        play.Do(new ChooseFirstActivation(holder, Seat.Two));
        Assert.Equal(Seat.Two, play.State.FirstActivator);
    }

    [Fact]
    [Trait("Rule", "1.0.2 Morale Phase p.50")]
    public void AnUndefinedMoraleOrderSeatIsRefusedBeforeAnyDieIsRolled()
    {
        var play = AtMoraleOrderChoice();
        var holder = play.State.Pending!.Seat;

        AssertRefusedUntouched(play, new ChooseMoraleOrder(holder, NoSuchSeat), DecisionKind.ChooseMoraleOrder, holder);

        Assert.Null(play.State.MoraleFirst);
        play.Do(new ChooseMoraleOrder(holder, Seat.One), 4, 3, 1, 1);
        Assert.Equal(Seat.One, play.Log.OfType<MoraleCheckRolled>().First().Seat);
    }

    [Theory]
    [Trait("Rule", "1.0.2 Sounding the Retreat p.50")]
    [InlineData(9)]
    [InlineData(-1)]
    [InlineData(2)]
    public void AnUndefinedFailedMoraleChoiceNeverSoundsTheRetreat(int value)
    {
        var play = AtFailedMoraleChoice();
        var seat = play.State.Pending!.Seat;

        AssertRefusedUntouched(play, new ResolveFailedMorale(seat, (MoraleFailureChoice)value), DecisionKind.FailedMorale, seat);

        Assert.False(play.State.SeatFor(seat).Shaken);
        play.Do(new ResolveFailedMorale(seat, MoraleFailureChoice.BecomeShaken));
        Assert.True(play.State.SeatFor(seat).Shaken);
    }

    [Fact]
    [Trait("Rule", "Architecture: rejected commands change nothing")]
    public void TheNamedUndefinedValueIsReallyUndefined()
    {
        Assert.False(Enum.IsDefined(NoSuchSeat));
        Assert.False(Enum.IsDefined(NoSuchChoice));
    }

    // The reducer is the second line of defence for logs that did not come through Decide.
    [Fact]
    [Trait("Rule", "Architecture: events validate before they change state")]
    public void TheReducerRefusesUndefinedSeatsInEvents()
    {
        var first = AtFirstActivationChoice();
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(first.State, new FirstActivationChosen(NoSuchSeat)));

        var order = AtMoraleOrderChoice();
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(order.State, new MoraleOrderSet(NoSuchSeat, Chosen: true)));
    }
}
