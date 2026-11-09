using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.State;
using static Rules.Tests.Geometry.Geo;
using static Rules.Tests.Match.BoardPlay;
using static Rules.Tests.Match.MeleePlay;

namespace Rules.Tests.Match;

// October 2026 Review, stage C4b (U159): Totemic, Barbed Embrace and Goaded Forth.
public class CourtAbilityTests
{
    private static readonly ModelProfile Demon = Rifleman with { Traits = ModelTraits.Demonic };

    private static BoardPlay Arena(Func<ModelId, ModelProfile> profiles)
    {
        var scenario = new Scenario(Rules.Tests.Geometry.Geo.Field("36", [], [new DeploymentZone("west", F("0", "0", "6", "36")), new DeploymentZone("east", F("30", "0", "36", "36"))]), []);
        var play = new BoardPlay(2, profiles, scenario: scenario).ToActivation();
        play.Put(A(1), "10", "10").Put(A(2), "2", "30").Put(B(1), "16", "10").Put(B(2), "30", "30");
        return play;
    }

    [Fact]
    [Trait("Rule", "Totemic: when a friendly DEMONIC model Activates within 1in of it, place 1 SIN MARKER next to that model first")]
    public void TotemicGivesAnActivatingDemonicFriendNearbyASinMarker()
    {
        var near = Arena(id => id == A(1) ? Rifleman with { Totemic = true } : Demon);
        near.Put(A(2), "11.5", "10");
        near.Do(new ActivateModel(Seat.One, A(2)));
        Assert.Equal(1, near.M(A(2)).SinMarkers);

        var far = Arena(id => id == A(1) ? Rifleman with { Totemic = true } : Demon);
        far.Do(new ActivateModel(Seat.One, A(2)));
        Assert.Equal(0, far.M(A(2)).SinMarkers);

        var notDemonic = Arena(id => id == A(1) ? Rifleman with { Totemic = true } : Rifleman);
        notDemonic.Put(A(2), "11.5", "10");
        notDemonic.Do(new ActivateModel(Seat.One, A(2)));
        Assert.Equal(0, notDemonic.M(A(2)).SinMarkers);
    }

    [Fact]
    [Trait("Rule", "Barbed Embrace: an enemy model within 1in cannot take a Retreat ACTION")]
    public void ABarbedEmbraceHoldsAnEnemyWithinAnInch()
    {
        var held = Skirmish("4", "2", "5.4", "2", Brawler, perSide: 2);
        Assert.Null(TurnSequence.Check(held.State, new RetreatModel(Seat.One, A(1), Path(("1.5", "2")))));

        // Only an enemy's Barbed Embrace holds.
        var mixed = new BoardPlay(2, id => id == B(1) ? Brawler with { BarbedEmbrace = true } : Brawler).ToActivation();
        mixed.Put(A(1), "4", "2").Put(B(1), "5.4", "2");
        mixed.Do(new ActivateModel(Seat.One, A(1)));
        mixed.Refused(new RetreatModel(Seat.One, A(1), Path(("1.5", "2"))), ReasonCode.ActionExcluded);
    }

    [Fact]
    [Trait("Rule", "Goaded Forth: +1 DICE to the Risky roll of its Dash ACTION with a friendly Chain Fiend within 8in and in sight")]
    public void GoadedForthNeedsAFriendlyChainFiendNearbyInSight()
    {
        int Dice(string fiendX, bool fiend = true)
        {
            var play = Arena(id => id == A(1) ? Rifleman with { GoadedForth = true } : id == A(2) && fiend ? Rifleman with { Commandable = true } : Rifleman);
            play.Put(A(2), fiendX, "10");
            play.Do(new ActivateModel(Seat.One, A(1)));
            return TurnSequence.DashDice(play.State, A(1));
        }
        Assert.Equal(1, Dice("14"));
        Assert.Equal(0, Dice("25"));          // beyond 8"
        Assert.Equal(0, Dice("14", fiend: false));
    }

    [Fact]
    [Trait("Rule", "Slow: counts as Movement 3in when it takes a Dash ACTION")]
    public void SlowShortensADashAndNothingElse()
    {
        var slow = Brawler with { Slow = true };
        var play = Skirmish("4", "2", "30", "30", slow);
        var far = Path(("8.5", "2"));      // 4.5" away: inside a Brawler's Movement, outside Slow's 3"
        Assert.NotNull(TurnSequence.Check(play.State, new DashModel(Seat.One, A(1), far)));
        Assert.Null(TurnSequence.Check(play.State, new DashModel(Seat.One, A(1), Path(("6.5", "2")))));
        Assert.Null(TurnSequence.Check(play.State, new MoveModel(Seat.One, A(1), far)));

        var plain = Skirmish("4", "2", "30", "30", Brawler);
        Assert.Null(TurnSequence.Check(plain.State, new DashModel(Seat.One, A(1), far)));
        Assert.Contains("slow=yes", StateHash.Canonical(play.State), StringComparison.Ordinal);
    }

    private static TakeSelfAction Gaze(string target) => new(Seat.One, A(1), SelfActionKind.GoeticGaze) { Target = new ModelId(target) };

    [Fact]
    [Trait("Rule", "Goetic Gaze ACTION: a Success Roll; 1 BLOOD MARKER (2 on a Critical) on an enemy within 24in in Line of Sight")]
    public void AGazePlacesBloodMarkersOnAnEnemyInSight()
    {
        var play = Arena(id => id == A(1) ? Rifleman with { Gazes = true } : Rifleman);
        play.Do(new ActivateModel(Seat.One, A(1)));
        play.Refused(Gaze("A2"), ReasonCode.NotACandidate);                 // a friend
        play.Refused(Gaze("B2"), ReasonCode.NotACandidate);                 // 28" away
        play.Refused(new TakeSelfAction(Seat.One, A(1), SelfActionKind.GoeticGaze), ReasonCode.UnknownModel);
        var rolled = play.Do(Gaze("B1"), 6, 6).Events.OfType<SelfActionRolled>().Single();
        Assert.Equal((2, 2), (rolled.Placed, play.M(B(1)).BloodMarkers));
        play.Refused(Gaze("B1"), ReasonCode.ActionAlreadyTaken);

        var plain = Arena(id => id == A(1) ? Rifleman with { Gazes = true } : Rifleman);
        plain.Do(new ActivateModel(Seat.One, A(1)));
        var hit = plain.Do(Gaze("B1"), 4, 4).Events.OfType<SelfActionRolled>().Single();
        Assert.Equal((1, 1), (hit.Placed, plain.M(B(1)).BloodMarkers));

        var missed = Arena(id => id == A(1) ? Rifleman with { Gazes = true } : Rifleman);
        missed.Do(new ActivateModel(Seat.One, A(1)));
        missed.Do(Gaze("B1"), 1, 1);
        Assert.Equal(0, missed.M(B(1)).BloodMarkers);

        var none = Arena(_ => Rifleman);
        none.Do(new ActivateModel(Seat.One, A(1)));
        none.Refused(Gaze("B1"), ReasonCode.NoSuchAction);

        // Room for only one: a Critical Success fills the last place and no more.
        var full = Arena(id => id == A(1) ? Rifleman with { Gazes = true } : Rifleman);
        full.State = full.State with { Models = [.. full.State.Models.Select(m => m.Id == B(1) ? m with { BloodMarkers = 5 } : m)] };
        full.Do(new ActivateModel(Seat.One, A(1)));
        Assert.Equal(1, full.Do(Gaze("B1"), 6, 6).Events.OfType<SelfActionRolled>().Single().Placed);
        Assert.Equal(6, full.M(B(1)).BloodMarkers);
    }
}
