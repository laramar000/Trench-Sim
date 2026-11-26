using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.State;
using static Rules.Tests.Geometry.Geo;
using static Rules.Tests.Match.BoardPlay;

namespace Rules.Tests.Match;

// Bloodbath Rolls (p. 47, U123): before an Injury Roll the attacker may spend 6 of the target's Blood Markers (3 if it is
// Down) for a Bloodbath Roll, which keeps one more die: 3D6, or 4D6 with DEADLY.
public class BloodbathTests
{
    private static BoardPlay Duel(Func<ModelId, ModelProfile>? profiles = null)
    {
        var play = new BoardPlay(2, profiles).ToActivation();
        play.Put(A(1), "4", "2").Put(B(1), "10", "2");
        play.Do(new ActivateModel(Seat.One, A(1)));
        return play;
    }

    private static Shoot Fire() => new(Seat.One, A(1), B(1));

    private static SpendMarkers Bloodbath(int count) => new(Seat.One, count) { Bloodbath = true };

    [Fact]
    [Trait("Rule", "Bloodbath Rolls p. 47: not offered with fewer than 6 markers on a standing target")]
    public void FiveMarkersAreNotEnough()
    {
        var play = Duel();
        play.Mark(B(1), 5);
        play.Do(Fire(), 4, 3);
        Assert.DoesNotContain(TurnSequence.LegalCommands(play.State), c => c is SpendMarkers { Bloodbath: true });
        play.Refused(Bloodbath(5), ReasonCode.InvalidMarkerCount);
    }

    [Fact]
    [Trait("Rule", "Bloodbath Rolls p. 47: 6 markers buy a roll of 3D6, all three added; the markers are not INJURY DICE")]
    public void SixMarkersBuyAThreeDiceRoll()
    {
        var play = Duel();
        play.Mark(B(1), 6);
        play.Do(Fire(), 4, 3);
        Assert.Contains(Bloodbath(6), TurnSequence.LegalCommands(play.State));
        play.Refused(Bloodbath(5), ReasonCode.InvalidMarkerCount);
        play.Refused(new SpendMarkers(Seat.Two, 6) { Bloodbath = true }, ReasonCode.NotYourTurn);

        var events = play.Do(Bloodbath(6), 3, 2, 2).Events;
        Assert.Equal(MarkerSpendPurpose.Bloodbath, events.OfType<MarkersSpent>().Single().Purpose);
        var rolled = events.OfType<InjuryRolled>().Single();
        Assert.True(rolled.Bloodbath);
        Assert.Equal((0, 3, 7, InjuryOutcome.Down), (rolled.Injury.NetDice, rolled.Injury.Dice.Kept.Count, rolled.Injury.Total, rolled.Injury.Outcome));
        Assert.Equal(1, play.M(B(1)).BloodMarkers); // 6 spent, then 1 for the Down result
    }

    [Fact]
    [Trait("Rule", "Bloodbath Rolls p. 47: a Down target's Bloodbath Roll costs 3 markers")]
    public void ADownTargetCostsThree()
    {
        var play = Duel();
        play.Mark(B(1), 4).SetStatus(B(1), ModelStatus.Down);
        play.Do(Fire(), 4, 3);
        Assert.Contains(Bloodbath(3), TurnSequence.LegalCommands(play.State));
        play.Refused(Bloodbath(4), ReasonCode.InvalidMarkerCount);

        var rolled = play.Do(Bloodbath(3), 1, 1, 1).Events.OfType<InjuryRolled>().Single();
        Assert.Equal((3, 3), (rolled.Injury.Dice.Kept.Count, rolled.Injury.Total));
        Assert.Equal(2, play.M(B(1)).BloodMarkers); // 4 - 3 spent + 1 for the Minor Hit
    }

    [Fact]
    [Trait("Rule", "Bloodbath Rolls p. 47: with DEADLY the Bloodbath Roll is 4D6, all four added")]
    public void ADeadlyBloodbathRollIsFourDice()
    {
        var play = Duel(id => id.Value == "A1" ? Rifleman with { RangedWeaponKeywords = WeaponKeywords.Deadly } : Rifleman);
        play.Mark(B(1), 6);
        play.Do(Fire(), 4, 3);
        var rolled = play.Do(Bloodbath(6), 1, 1, 1, 2).Events.OfType<InjuryRolled>().Single();
        Assert.Equal((4, 5, InjuryOutcome.MinorHit), (rolled.Injury.Dice.Kept.Count, rolled.Injury.Total, rolled.Injury.Outcome));
    }

    [Fact]
    [Trait("Rule", "Bloodbath Rolls p. 47: an ordinary spend is still offered beside it, and keeps two dice")]
    public void AnOrdinarySpendStaysTwoDice()
    {
        var play = Duel();
        play.Mark(B(1), 6);
        play.Do(Fire(), 4, 3);
        var rolled = play.Do(new SpendMarkers(Seat.One, 6), 1, 1, 1, 1, 1, 1, 1, 1).Events.OfType<InjuryRolled>().Single();
        Assert.False(rolled.Bloodbath);
        Assert.Equal((6, 2), (rolled.Injury.NetDice, rolled.Injury.Dice.Kept.Count));
    }
}
