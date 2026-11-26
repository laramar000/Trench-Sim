using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.State;
using static Rules.Tests.Geometry.Geo;
using static Rules.Tests.Match.BoardPlay;
using static Rules.Tests.Match.MeleePlay;

namespace Rules.Tests.Match;

// Charge moves p. 37 and Suicidal Bravery p. 153 (U124): a charge that stops in contact may carry on, staying in contact
// with its target, to finish within 1" of another enemy model; its player chooses. A successful charge that ends within
// 1" of a second enemy model completes Suicidal Bravery.
public class ChargeCarryOnTests
{
    private static ChargeModel Charge() => new(Seat.One, A(1), B(1));

    // A1 charges B1 across a 4" gap and makes contact at (8, 2); B2 is above B1, more than 1" from that point but within 1"
    // of the top of B1.
    private static BoardPlay WithB2Above()
    {
        var play = Skirmish("4", "2", "9", "2");
        play.Put(B(2), "9", "4.3");
        play.Do(Charge(), 6);
        return play;
    }

    [Fact]
    [Trait("Rule", "Charge moves p. 37: the player is asked whether a charge that can carry on to a second enemy does")]
    public void ACarryOnIsOfferedAndNothingElseUntilItIsAnswered()
    {
        var play = WithB2Above();
        Assert.Equal(new PendingDecision(DecisionKind.CarryOnCharge, Seat.One), play.State.Pending);
        Assert.Equal([new CarryOnCharge(Seat.One), new StopCharge(Seat.One)], TurnSequence.LegalCommands(play.State));
        play.Refused(new CarryOnCharge(Seat.Two), ReasonCode.NotYourTurn);
        play.Refused(new EndActivation(Seat.One), ReasonCode.DecisionPending);
        Assert.True(Gap(play, A(1), B(1)) <= L("0.002"));
        Assert.False(Within(play, A(1), B(2), "1"));
    }

    [Fact]
    [Trait("Rule", "Charge moves p. 37 and Suicidal Bravery p. 153: carrying on keeps contact, ends within 1\" of the second enemy, and completes the deed")]
    public void CarryingOnReachesTheSecondEnemyAndCompletesSuicidalBravery()
    {
        var play = WithB2Above();
        var events = play.Do(new CarryOnCharge(Seat.One)).Events;
        var carried = events.OfType<ChargeCarriedOn>().Single();
        Assert.Equal(play.M(A(1)).Position, carried.Path[^1]);
        Assert.True(Gap(play, A(1), B(1)) <= L("0.004"));
        Assert.False(Measure.Overlap(BodyOf(play, A(1)), BodyOf(play, B(1))));
        Assert.True(Within(play, A(1), B(2), "1"));
        Assert.Contains(new GloriousDeedCompleted(Seat.One, GloriousDeed.SuicidalBravery, A(1)), events);
        Assert.Null(play.State.InProgress);
        Assert.Null(play.State.Pending);
    }

    [Fact]
    [Trait("Rule", "Charge moves p. 37: stopping leaves the model where it made contact, and no deed")]
    public void StoppingStaysInContactWithNoDeed()
    {
        var play = WithB2Above();
        var at = play.M(A(1)).Position;
        var events = play.Do(new StopCharge(Seat.One)).Events;
        Assert.Equal(new ChargeStopped(A(1)), events.OfType<ChargeStopped>().Single());
        Assert.Equal(at, play.M(A(1)).Position);
        Assert.DoesNotContain(events, e => e is GloriousDeedCompleted);
        Assert.Null(play.State.Pending);
    }

    [Fact]
    [Trait("Rule", "Charge moves p. 37: nothing is offered with no other enemy in reach")]
    public void NoOtherEnemyNoQuestion()
    {
        var play = Skirmish("4", "2", "9", "2");
        play.Do(Charge(), 6);
        Assert.Null(play.State.Pending);
        Assert.Null(play.State.InProgress);
    }

    [Fact]
    [Trait("Rule", "Suicidal Bravery p. 153: a charge that already ends within 1\" of a second enemy completes it, with no question")]
    public void ContactNextToASecondEnemyCompletesTheDeedAtOnce()
    {
        var play = Skirmish("4", "2", "9", "2");
        play.Put(B(2), "8.5", "3.85");
        var events = play.Do(Charge(), 6).Events;
        Assert.True(events.OfType<ChargeMoved>().Single().Reached);
        Assert.True(Within(play, A(1), B(2), "1"));
        Assert.Null(play.State.Pending);
        Assert.Contains(new GloriousDeedCompleted(Seat.One, GloriousDeed.SuicidalBravery, A(1)), events);
    }
}
