using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Events;
using Rules.Core.State;
using static Rules.Tests.Geometry.Geo;
using static Rules.Tests.Match.BoardPlay;

namespace Rules.Tests.Match;

// Roster stage R7d (U131): Bodyguard (core rulebook pp. 54, 136): a model may take a hit meant for a friendly PILGRIM (or
// CULT) model within 1" (not against BLAST). It is asked before the Injury Roll, after the hit.
public class BodyguardTests
{
    private static readonly ModelProfile Pilgrim = Rifleman with { Traits = ModelTraits.Pilgrim };
    private static readonly ModelProfile Guard = Rifleman with { Traits = ModelTraits.Pilgrim, Bodyguard = true };

    // A1 (4, 2) shoots B1 (10, 2); B2 stands beside it at (10, y) and is the Bodyguard unless profiles say otherwise.
    private static BoardPlay Duel(string guardY = "3.5", ModelProfile? guard = null, ModelProfile? hit = null)
    {
        var play = new BoardPlay(2, id => id == A(1) ? Rifleman : id == B(1) ? hit ?? Pilgrim : id == B(2) ? guard ?? Guard : Rifleman).ToActivation();
        play.Put(A(1), "4", "2").Put(A(2), "2", "30").Put(B(1), "10", "2").Put(B(2), "10", guardY);
        play.Do(new ActivateModel(Seat.One, A(1)));
        return play;
    }

    private static Shoot Fire => new(Seat.One, A(1), B(1));

    [Fact]
    [Trait("Rule", "Bodyguard p. 54: after the hit, before the Injury Roll, the hit model's player may let the Bodyguard take it")]
    public void AHitOffersTheBodyguard()
    {
        var play = Duel();
        var hit = play.Do(Fire, 4, 3);
        Assert.Equal(new DecisionRequested(DecisionKind.UseBodyguard, Seat.Two), hit.Events[^1]);
        Assert.DoesNotContain(hit.Events, e => e is InjuryRolled);
        Assert.Equal<Command>([new ChooseBodyguard(Seat.Two, B(2)), new ChooseBodyguard(Seat.Two, null)], TurnSequence.LegalCommands(play.State));
        play.Refused(new ChooseBodyguard(Seat.One, B(2)), ReasonCode.NotYourTurn);
        play.Refused(new ChooseBodyguard(Seat.Two, A(2)), ReasonCode.NotACandidate);
        play.Refused(new ChooseBodyguard(Seat.Two, B(1)), ReasonCode.NotACandidate);
        play.Refused(new SpendMarkers(Seat.One, 0), ReasonCode.WrongDecision);
    }

    [Fact]
    [Trait("Rule", "Bodyguard p. 54: the Bodyguard takes the hit: its Injury Roll is made, not the hit model's")]
    public void TheBodyguardTakesTheInjuryRoll()
    {
        var play = Duel();
        play.Do(Fire, 4, 3);
        var taken = play.Do(new ChooseBodyguard(Seat.Two, B(2)), 6, 6);
        Assert.Equal(new BodyguardAnswered(B(1), B(2)), taken.Events[0]);
        var injury = taken.Events.OfType<InjuryRolled>().Single();
        Assert.Equal((A(1), B(2)), (injury.Attacker, injury.Target));
        Assert.Equal(ModelStatus.OutOfAction, play.M(B(2)).Status);
        Assert.Equal(ModelStatus.Standing, play.M(B(1)).Status);
        Assert.Null(play.State.Pending);
    }

    [Fact]
    [Trait("Rule", "Bodyguard p. 54: the player may keep the hit on the model it was meant for")]
    public void DecliningLeavesTheHitOnTheOriginalTarget()
    {
        var play = Duel();
        play.Do(Fire, 4, 3);
        var kept = play.Do(new ChooseBodyguard(Seat.Two, null), 6, 6);
        Assert.Equal(new BodyguardAnswered(B(1), null), kept.Events[0]);
        Assert.Equal(B(1), kept.Events.OfType<InjuryRolled>().Single().Target);
        Assert.Equal(ModelStatus.Standing, play.M(B(2)).Status);
    }

    [Fact]
    [Trait("Rule", "Bodyguard p. 54: only a standing friend within 1\" with Bodyguard and the same PILGRIM or CULT keyword")]
    public void NoOfferWithoutAnEligibleBodyguard()
    {
        // Too far (centres 3" apart, edges 2"), a different faction, a model without Bodyguard, a Down friend.
        foreach (var play in new[]
        {
            Duel(guardY: "5"),
            Duel(guard: Guard with { Traits = ModelTraits.Cult }),
            Duel(guard: Pilgrim),
            Duel(hit: Rifleman),
        })
        {
            var result = play.Do(Fire, 4, 3, 6, 6);
            Assert.DoesNotContain(result.Events, e => e is DecisionRequested { Kind: DecisionKind.UseBodyguard });
            Assert.Equal(B(1), result.Events.OfType<InjuryRolled>().Single().Target);
        }
        var down = Duel();
        down.SetStatus(B(2), ModelStatus.Down);
        Assert.Equal(B(1), down.Do(Fire, 4, 3, 6, 6).Events.OfType<InjuryRolled>().Single().Target);
    }

    [Fact]
    [Trait("Rule", "Bodyguard p. 136: a CULT Bodyguard guards CULT models")]
    public void ACultBodyguardGuardsCultModels()
    {
        var cult = Rifleman with { Traits = ModelTraits.Cult };
        var play = Duel(guard: cult with { Bodyguard = true }, hit: cult);
        Assert.Equal(new DecisionRequested(DecisionKind.UseBodyguard, Seat.Two), play.Do(Fire, 4, 3).Events[^1]);
    }

    [Fact]
    [Trait("Rule", "Blood Markers p. 28: the markers the attacker may spend are those of whoever takes the hit")]
    public void TheMarkersAreTheOnesOfWhoeverTakesTheHit()
    {
        var play = Duel();
        play.Mark(B(2), 2);
        play.Do(Fire, 4, 3);
        var taken = play.Do(new ChooseBodyguard(Seat.Two, B(2)));
        Assert.Equal(new DecisionRequested(DecisionKind.SpendMarkersOnInjuryRoll, Seat.One), taken.Events[^1]);
        play.Do(new SpendMarkers(Seat.One, 1), 6, 6, 6);
        Assert.Equal(1, play.M(B(2)).BloodMarkers);

        // Markers on the model that was hit are not spent when the Bodyguard takes the hit.
        var other = Duel();
        other.Mark(B(1), 2);
        other.Do(Fire, 4, 3);
        var guarded = other.Do(new ChooseBodyguard(Seat.Two, B(2)), 6, 6);
        Assert.DoesNotContain(guarded.Events, e => e is DecisionRequested);
        Assert.Equal(2, other.M(B(1)).BloodMarkers);
    }

    [Fact]
    [Trait("Rule", "Bodyguard: the Injury Roll uses the Bodyguard's own Armour")]
    public void TheInjuryRollUsesTheBodyguardsArmour()
    {
        var play = Duel(guard: new ModelProfile(L("6"), L("0.5"), L("18"), armour: -2) { Traits = ModelTraits.Pilgrim, Bodyguard = true });
        play.Do(Fire, 4, 3);
        var injury = play.Do(new ChooseBodyguard(Seat.Two, B(2)), 4, 4).Events.OfType<InjuryRolled>().Single().Injury;
        Assert.Equal(-2, injury.Modifier);
    }

    [Fact]
    [Trait("Rule", "Bodyguard: a forged answer naming a model that cannot guard is refused")]
    public void AForgedAnswerIsRefused()
    {
        var play = Duel();
        play.Do(Fire, 4, 3);
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(play.State, new BodyguardAnswered(B(1), A(2))));
    }

    [Fact]
    [Trait("Rule", "Bodyguard p. 54: a Melee Attack's hit too; the Injury Roll is the Bodyguard's, without the Down bonus")]
    public void AMeleeHitOffersTheBodyguardToo()
    {
        var play = new BoardPlay(2, id => id == A(1) ? MeleePlay.Brawler : id == B(1) ? Pilgrim : id == B(2) ? Guard : Rifleman).ToActivation();
        play.Put(A(1), "10", "2").Put(A(2), "2", "30").Put(B(1), "11", "2").Put(B(2), "11", "3.5");
        play.SetStatus(B(1), ModelStatus.Down);
        play.Do(new ActivateModel(Seat.One, A(1)));
        play.Do(new FightModel(Seat.One, A(1), B(1)), 4, 3);
        Assert.Equal(new PendingDecision(DecisionKind.UseBodyguard, Seat.Two), play.State.Pending);
        var injury = play.Do(new ChooseBodyguard(Seat.Two, B(2)), 4, 4).Events.OfType<InjuryRolled>().Single();
        Assert.Equal((B(2), 0, false), (injury.Target, injury.Injury.NetDice, injury.Injury.TargetAlreadyDown));
    }
}
