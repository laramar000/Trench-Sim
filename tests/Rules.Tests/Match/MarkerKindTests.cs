using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.State;
using static Rules.Tests.Geometry.Geo;
using static Rules.Tests.Match.BoardPlay;

namespace Rules.Tests.Match;

// Roster stage R10 (U135): INFECTION and BLESSING MARKERS are BLOOD MARKERS in another colour: up to 6 of each, and every kind
// counts when markers are spent. An INFECTION MARKERS weapon places INFECTION MARKERS; a model with any gains 1 more when it
// activates; a God is With Us! ACTION places a BLESSING MARKER.
public class MarkerKindTests
{
    private static readonly ModelProfile Plague = Rifleman with { RangedWeaponKeywords = WeaponKeywords.InfectionMarkers };

    private static BoardPlay Duel(ModelProfile attacker, int perSide = 2)
    {
        var play = new BoardPlay(perSide, id => id == A(1) ? attacker : Rifleman).ToActivation();
        play.Put(A(1), "10", "10").Put(A(2), "2", "30").Put(B(1), "16", "10").Put(B(2), "30", "30");
        play.Do(new ActivateModel(Seat.One, A(1)));
        return play;
    }

    [Fact]
    public void AnInfectionWeaponPlacesInfectionMarkersInsteadOfBloodMarkers()
    {
        var play = Duel(Plague);
        // A hit (4+3) and a Minor Hit (1+2): 1 marker, and it is an INFECTION MARKER.
        var result = play.Do(new Shoot(Seat.One, A(1), B(1)), 4, 3, 1, 2);
        var injury = result.Events.OfType<InjuryRolled>().Single();
        Assert.True(injury.Infection);
        Assert.Equal((0, 1, 0), (play.M(B(1)).BloodMarkers, play.M(B(1)).InfectionMarkers, play.M(B(1)).BlessingMarkers));
        // An ordinary weapon places BLOOD MARKERS and says so.
        var plain = Duel(Rifleman);
        var normal = plain.Do(new Shoot(Seat.One, A(1), B(1)), 4, 3, 1, 2);
        Assert.False(normal.Events.OfType<InjuryRolled>().Single().Infection);
        Assert.Equal((1, 0), (plain.M(B(1)).BloodMarkers, plain.M(B(1)).InfectionMarkers));
    }

    [Fact]
    public void InfectionMarkersStopAtSix()
    {
        var play = Duel(Plague);
        play.State = play.State with { Models = [.. play.State.Models.Select(m => m.Id == B(1) ? m with { InfectionMarkers = 6, BloodMarkers = 2 } : m)] };
        // The attacker is offered the target's markers (all kinds count): decline them.
        play.Do(new Shoot(Seat.One, A(1), B(1)), 4, 3);
        play.Do(new SpendMarkers(Seat.One, 0), 1, 2);
        Assert.Equal((2, 6), (play.M(B(1)).BloodMarkers, play.M(B(1)).InfectionMarkers));
    }

    [Fact]
    public void AModelWithAnInfectionMarkerGainsOneMoreWhenItActivates()
    {
        var play = new BoardPlay(2).ToActivation();
        play.Put(A(1), "10", "10").Put(A(2), "2", "30").Put(B(1), "30", "10").Put(B(2), "30", "30");
        play.State = play.State with { Models = [.. play.State.Models.Select(m => m.Id == A(1) ? m with { InfectionMarkers = 2 } : m.Id == A(2) ? m with { BlessingMarkers = 1 } : m)] };
        play.Do(new ActivateModel(Seat.One, A(1)));
        Assert.Equal(3, play.M(A(1)).InfectionMarkers);
        play.Do(new EndActivation(Seat.One));
        play.Do(new ActivateModel(Seat.Two, B(1)));
        play.Do(new EndActivation(Seat.Two));
        // No infection, no spread; blessing markers do not spread.
        play.Do(new ActivateModel(Seat.One, A(2)));
        Assert.Equal((0, 1), (play.M(A(2)).InfectionMarkers, play.M(A(2)).BlessingMarkers));
        // Six is the most.
        var six = new BoardPlay(2).ToActivation();
        six.Put(A(1), "10", "10").Put(A(2), "2", "30");
        six.State = six.State with { Models = [.. six.State.Models.Select(m => m.Id == A(1) ? m with { InfectionMarkers = 6 } : m)] };
        six.Do(new ActivateModel(Seat.One, A(1)));
        Assert.Equal(6, six.M(A(1)).InfectionMarkers);
    }

    [Fact]
    public void EveryKindCountsWhenMarkersAreSpentBloodFirst()
    {
        var play = Duel(Rifleman);
        play.State = play.State with { Models = [.. play.State.Models.Select(m => m.Id == B(1) ? m with { BloodMarkers = 1, InfectionMarkers = 1, BlessingMarkers = 2 } : m)] };
        play.Do(new Shoot(Seat.One, A(1), B(1)), 4, 3);
        // The offer is for all four markers; spending three takes the blood, the infection and one blessing.
        Assert.Equal(new PendingDecision(DecisionKind.SpendMarkersOnInjuryRoll, Seat.One), play.State.Pending);
        Assert.Equal(Enumerable.Range(0, 5).Select(n => new SpendMarkers(Seat.One, n)), TurnSequence.LegalCommands(play.State).OfType<SpendMarkers>().Where(c => !c.Bloodbath));
        play.Do(new SpendMarkers(Seat.One, 3), 6, 6, 6, 6, 6);
        Assert.Equal((0, 0, 1), (play.M(B(1)).BloodMarkers, play.M(B(1)).InfectionMarkers, play.M(B(1)).BlessingMarkers));
        Assert.Equal(new ModelState(B(1), Seat.Two).WithoutMarkers(0), new ModelState(B(1), Seat.Two));
    }

    private static TakeSelfAction Bless(string target) => new(Seat.One, A(1), SelfActionKind.GodIsWithUs) { Target = new ModelId(target) };

    [Fact]
    [Trait("Rule", "God is With Us! p. 29: a Risky roll; on a Success 1 BLESSING MARKER on itself or a friend within 6\"")]
    public void GodIsWithUsPlacesABlessingMarker()
    {
        var play = Duel(Rifleman with { Blesses = true });
        play.Put(A(2), "14", "10");
        var result = play.Do(Bless("A2"), 6, 6);
        var rolled = result.Events.OfType<SelfActionRolled>().Single();
        Assert.Equal((A(2), 1), (rolled.Target, rolled.Placed));
        Assert.Equal(1, play.M(A(2)).BlessingMarkers);
        // Itself, too; once per activation; a failure places nothing and ends the activation.
        play.Refused(Bless("A1"), ReasonCode.ActionAlreadyTaken);
        var self = Duel(Rifleman with { Blesses = true });
        self.Do(Bless("A1"), 6, 6);
        Assert.Equal(1, self.M(A(1)).BlessingMarkers);
        var failed = Duel(Rifleman with { Blesses = true });
        var events = failed.Do(Bless("A1"), 1, 1).Events;
        Assert.Equal(0, failed.M(A(1)).BlessingMarkers);
        Assert.Contains(events, e => e is ActivationEnded { Reason: ActivationEndReason.RiskyFailure });
    }

    [Fact]
    public void GodIsWithUsIsCheckedAtTheTarget()
    {
        var play = Duel(Rifleman with { Blesses = true });
        play.Refused(Bless("B1"), ReasonCode.NotACandidate);        // an enemy
        play.Refused(Bless("A2"), ReasonCode.NotACandidate);        // 28" off
        play.Refused(new TakeSelfAction(Seat.One, A(1), SelfActionKind.GodIsWithUs), ReasonCode.UnknownModel);
        Duel(Rifleman).Refused(Bless("A1"), ReasonCode.NoSuchAction);
        var full = Duel(Rifleman with { Blesses = true });
        full.State = full.State with { Models = [.. full.State.Models.Select(m => m.Id == A(1) ? m with { BlessingMarkers = 6 } : m)] };
        full.Refused(Bless("A1"), ReasonCode.NotACandidate);
    }

    [Fact]
    public void TheStateHashSeesTheKindsOnlyWhenTheyAreThere()
    {
        var play = Duel(Rifleman);
        var plain = StateHash.Compute(play.State);
        Assert.DoesNotContain("infection", StateHash.Canonical(play.State), StringComparison.Ordinal);
        play.State = play.State with { Models = [.. play.State.Models.Select(m => m.Id == B(1) ? m with { InfectionMarkers = 1 } : m)] };
        Assert.NotEqual(plain, StateHash.Compute(play.State));
        play.State = play.State with { Models = [.. play.State.Models.Select(m => m.Id == B(1) ? m with { InfectionMarkers = 0, BlessingMarkers = 1 } : m)] };
        Assert.Contains("blessing=1", StateHash.Canonical(play.State), StringComparison.Ordinal);
    }

    private static TakeSelfAction Arise(string target) => new(Seat.One, A(1), SelfActionKind.AriseAndBeHealed) { Target = new ModelId(target) };

    // Roster stage R12 (U137): Arise and be Healed! (Warbands p. 35).
    [Fact]
    [Trait("Rule", "Arise and be Healed! p. 35: a Risky roll; on a Success itself or a friend within 3\" stands up free and loses up to D3 BLOOD and/or INFECTION MARKERS")]
    public void AriseAndBeHealedStandsAModelUpAndTakesMarkersOff()
    {
        var play = Duel(Rifleman with { Heals = true });
        play.Put(A(2), "12", "10");
        play.SetStatus(A(2), ModelStatus.Down);
        play.State = play.State with { Models = [.. play.State.Models.Select(m => m.Id == A(2) ? m with { BloodMarkers = 1, InfectionMarkers = 3 } : m)] };
        // Risky roll 6+6, then the D6: 5 is a D3 of 3.
        var rolled = play.Do(Arise("A2"), 6, 6, 5).Events.OfType<SelfActionRolled>().Single();
        Assert.Equal((3, 3, A(2)), (rolled.Die, rolled.Removed, rolled.StoodUp.Single()));
        // Blood first, then infection: 1 blood and 2 infection come off.
        Assert.Equal((ModelStatus.Standing, 0, 1), (play.M(A(2)).Status, play.M(A(2)).BloodMarkers, play.M(A(2)).InfectionMarkers));
        play.Refused(Arise("A1"), ReasonCode.ActionAlreadyTaken);
    }

    [Fact]
    public void AriseAndBeHealedChecksItsTargetAndRoll()
    {
        var play = Duel(Rifleman with { Heals = true });
        play.Refused(Arise("A1"), ReasonCode.NotACandidate);          // standing, no markers: nothing to do
        play.Refused(Arise("B1"), ReasonCode.NotACandidate);          // an enemy
        play.Refused(Arise("A2"), ReasonCode.NotACandidate);          // too far
        Duel(Rifleman).Refused(Arise("A1"), ReasonCode.NoSuchAction);
        // On a friend: a D6 of 1 or 2 is a D3 of 1; a failure does nothing and ends the activation.
        var self = Duel(Rifleman with { Heals = true });
        self.Put(A(2), "12", "10").Mark(A(2), 2);
        Assert.Equal(1, self.Do(Arise("A2"), 6, 6, 2).Events.OfType<SelfActionRolled>().Single().Removed);
        Assert.Equal(1, self.M(A(2)).BloodMarkers);
        var failed = Duel(Rifleman with { Heals = true });
        failed.Put(A(2), "12", "10").Mark(A(2), 2);
        var events = failed.Do(Arise("A2"), 1, 1).Events;
        Assert.Equal(2, failed.M(A(2)).BloodMarkers);
        Assert.Contains(events, e => e is ActivationEnded { Reason: ActivationEndReason.RiskyFailure });
        // A forged result is refused: a die that is not a D3, or a different number of markers.
        var ready = Duel(Rifleman with { Heals = true });
        ready.Put(A(2), "12", "10").Mark(A(2), 2);
        var declared = Reducer.Apply(ready.State, new SelfActionDeclared(A(1), SelfActionKind.AriseAndBeHealed) { Target = A(2) });
        var check = Rules.Core.Dice.SuccessRoll.Roll(new FixedDice(6, 6), 0, 0, risky: true);
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(declared, new SelfActionRolled(A(1), SelfActionKind.AriseAndBeHealed, check) { Target = A(2), Die = 0 }));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(declared, new SelfActionRolled(A(1), SelfActionKind.AriseAndBeHealed, check) { Target = A(2), Die = 2, Removed = 1 }));
        Reducer.Apply(declared, new SelfActionRolled(A(1), SelfActionKind.AriseAndBeHealed, check) { Target = A(2), Die = 2, Removed = 2 });
    }
}
