using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.State;
using static Rules.Tests.Geometry.Geo;
using static Rules.Tests.Match.BoardPlay;
using static Rules.Tests.Match.MeleePlay;

namespace Rules.Tests.Match;

// Charge carry-on past contact (Comprehensive Rules p. 37; U126): a charge stops as soon as it touches its target, unless
// carrying on lets it stay in contact and finish within 1" of another enemy. It is the player's choice (the flag on the
// command); without it every plan is exactly as before.
public class CarryOnTests
{
    // A1 at (4,2) charges B1 at (10,2); B2 at (10,4.2) is 2.4" from the first contact point (9,2), so not within 1" of it,
    // but 1.2" from the ring point straight above B1.
    private static BoardPlay Scene(string ax = "4", string b2y = "4.2")
    {
        var play = Skirmish(ax, "2", "10", "2");
        play.Put(B(2), "10", b2y);
        return play;
    }

    private static ChargeModel Charge(bool carryOn) => new(Seat.One, A(1), B(1)) { CarryOn = carryOn };

    [Fact]
    [Trait("Rule", "Charging p. 37: a charge may carry on in contact to finish within 1\" of another enemy (U126)")]
    public void ACarryOnSlidesRoundTheTargetToASecondEnemy()
    {
        var plain = Scene();
        var stopped = plain.Do(Charge(false), 6).Events.OfType<ChargeMoved>().Single();
        Assert.False(Within(plain, A(1), B(2), "1")); // first contact is not within 1" of B2

        var play = Scene();
        var result = play.Do(Charge(true), 6);
        var moved = result.Events.OfType<ChargeMoved>().Single();
        Assert.True(moved.Reached);
        Assert.True(moved.Path.Length > stopped.Path.Length);
        Assert.True(moved.Cost > stopped.Cost && moved.Cost <= L("12"));
        Assert.True(Gap(play, A(1), B(1)) <= L("0.002")); // still in contact with the target
        Assert.False(Measure.Overlap(BodyOf(play, A(1)), BodyOf(play, B(1))));
        Assert.True(Within(play, A(1), B(2), "1"));       // and now within 1" of the second enemy
        Assert.False(Measure.Overlap(BodyOf(play, A(1)), BodyOf(play, B(2))));
        // It slid the short way, up the near side of B1 and not round the far side.
        Assert.True(play.M(A(1)).Position!.Value.Y > L("2"));
        Assert.True(play.M(A(1)).Position!.Value.X < L("10"));
    }

    [Fact]
    [Trait("Rule", "Charging p. 37 with Scenario I Suicidal Bravery: carrying on past contact earns the deed (U123, U126)")]
    public void ACarryOnToASecondEnemyIsSuicidalBravery()
    {
        Assert.DoesNotContain(Scene().Do(Charge(false), 6).Events, e => e is GloriousDeedCompleted { Deed: GloriousDeed.SuicidalBravery });
        Assert.Contains(Scene().Do(Charge(true), 6).Events, e => e is GloriousDeedCompleted { Deed: GloriousDeed.SuicidalBravery });
    }

    [Fact]
    [Trait("Rule", "Charging p. 37: without the choice, or with nowhere to carry on to, the charge stops at contact")]
    public void WithoutTheFlagOrAQualifyingPointTheChargeStopsAtContact()
    {
        var first = Scene().Do(Charge(false), 6).Events.OfType<ChargeMoved>().Single();
        // B2 out of reach of every ring point: the flag changes nothing.
        var far = Scene(b2y: "8").Do(Charge(true), 6).Events.OfType<ChargeMoved>().Single();
        Assert.Equal(first.Path.ToArray(), far.Path.ToArray());
        Assert.Equal(first.Cost, far.Cost);
        // Too little allowance left to slide: die 1 from 2.2" back leaves under 0.2" after contact.
        var short1 = Scene(ax: "2.2").Do(Charge(true), 1).Events.OfType<ChargeMoved>().Single();
        var short0 = Scene(ax: "2.2").Do(Charge(false), 1).Events.OfType<ChargeMoved>().Single();
        Assert.Equal(short0.Path.ToArray(), short1.Path.ToArray());
    }

    [Fact]
    [Trait("Rule", "Charging p. 37: a charge already within 1\" of a second enemy at contact does not move on")]
    public void AChargeAlreadyBesideASecondEnemyStaysPut()
    {
        var flagged = Scene(b2y: "3.6").Do(Charge(true), 6).Events.OfType<ChargeMoved>().Single();
        var plain = Scene(b2y: "3.6").Do(Charge(false), 6).Events.OfType<ChargeMoved>().Single();
        Assert.Equal(plain.Path.ToArray(), flagged.Path.ToArray());
    }

    [Fact]
    [Trait("Rule", "Charging p. 37: a replay plans the charge from the declared choice and refuses any other path")]
    public void AForgedCarryOnIsRefused()
    {
        var play = Scene();
        var before = play.State;
        var events = play.Do(Charge(true), 6).Events;
        Assert.Equal(StateHash.Compute(play.State), StateHash.Compute(events.Aggregate(before, Reducer.Apply)));
        // The same path with the choice taken off the declaration: the replan stops at contact, so the move is refused.
        Assert.Throws<InvalidEventException>(() => events.Select(e => e is ChargeDeclared d ? d with { CarryOn = false } : e).Aggregate(before, Reducer.Apply));
        // And the other way: the choice declared, but the plain contact path recorded.
        var plainEvents = Scene().Do(Charge(false), 6).Events;
        Assert.Throws<InvalidEventException>(() => plainEvents.Select(e => e is ChargeDeclared d ? d with { CarryOn = true } : e).Aggregate(Scene().State, Reducer.Apply));
    }

    [Fact]
    [Trait("Rule", "Charging p. 37: the carry-on choice is part of the state hash while the charge is in progress")]
    public void TheHashCoversTheChoice()
    {
        var a = Reducer.Apply(Scene().State, new ChargeDeclared(A(1), B(1)));
        var b = Reducer.Apply(Scene().State, new ChargeDeclared(A(1), B(1)) { CarryOn = true });
        Assert.NotEqual(StateHash.Compute(a), StateHash.Compute(b));
    }
}
