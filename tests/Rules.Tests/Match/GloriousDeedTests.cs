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

// Glorious Deeds (pp. 149-150; Scenario I p. 153, Scenario IV p. 163; U96, built 2026-10-26 on Jason's choice "Deeds
// only"). Whole commands through the server's own TurnSequence where the dice can be set; Deeds.Track itself for the
// deeds that need a longer game to reach.
public class GloriousDeedTests
{
    private static readonly ImmutableArray<string> Mines1And2Live = ["mine-3", "mine-4", "mine-5", "mine-6"];

    // Claim No Man's Land: A1 at (4, 2) about to act, B1 at the given spot.
    private static BoardPlay Claim(string bx = "10", string by = "2")
    {
        var play = new BoardPlay().ToActivation();
        play.Put(A(1), "4", "2").Put(B(1), bx, by);
        play.Do(new ActivateModel(Seat.One, A(1)));
        return play;
    }

    // Trench Warfare: A1 (attacking) about to act at the given spot, B1 at the other; mine-1 and mine-2 at the given spots
    // (both live), the rest out of the way along x 29.5.
    private static BoardPlay Trench(string ax, string ay, string bx, string by, Point2? mine1 = null, Point2? mine2 = null)
    {
        Point2[] first = [mine1 ?? P("29.5", "35.5"), mine2 ?? P("26.5", "35.5")];
        ImmutableArray<Point2> markers = [.. first, .. Enumerable.Range(0, Mines.Count - 2).Select(k => P("29.5", (1.5 + 3 * k).ToString(System.Globalization.CultureInfo.InvariantCulture)))];
        var play = TrenchWarfare(1).ToTrenchActivation(markers, Mines1And2Live);
        play.Put(A(1), ax, ay).Put(B(1), bx, by);
        play.Do(new ActivateModel(Seat.One, A(1)));
        return play;
    }

    private static GloriousDeedCompleted Deed(Seat seat, GloriousDeed deed, ModelId? model) => new(seat, deed, model);

    private static void Same<T>(IEnumerable<T> expected, IEnumerable<T> actual) => Assert.Equal(expected.ToArray(), actual.ToArray());

    [Fact]
    public void EachScenarioHasItsOwnDeeds()
    {
        Same([GloriousDeed.Bloodletting, GloriousDeed.HoldYourGround, GloriousDeed.LordOfWar, GloriousDeed.ResistAndBite], Deeds.For(new BoardPlay().Setup));
        Same([GloriousDeed.GoodHunting, GloriousDeed.Headshot, GloriousDeed.HoldYourGround, GloriousDeed.IntoTheTrenches, GloriousDeed.SurviveToTellTheTale], Deeds.For(TrenchWarfare().Setup));
        Assert.Empty(Deeds.For(null));
    }

    [Fact]
    [Trait("Rule", "Scenario I p. 153, Bloodletting: an attack puts the sixth Blood Marker beside an enemy")]
    public void TheSixthBloodMarkerIsBloodletting()
    {
        var play = Claim();
        play.Mark(B(1), 5);
        play.Do(new Shoot(Seat.One, A(1), B(1)), 4, 3); // a hit; the attacker may spend the target's markers
        var result = play.Do(new SpendMarkers(Seat.One, 0), 1, 1); // 2: a Minor Hit, the sixth marker
        var injured = result.Events.ToList().FindIndex(e => e is InjuryRolled);
        Assert.Equal(Deed(Seat.One, GloriousDeed.Bloodletting, A(1)), result.Events[injured + 1]);
        Assert.Equal(1, play.State.One.VictoryPoints);
        Same([new CompletedDeed(GloriousDeed.Bloodletting, Seat.One, A(1))], play.State.Deeds.Completed);
    }

    [Fact]
    public void AModelAlreadyAtSixMarkersIsNoBloodletting()
    {
        var play = Claim();
        play.Mark(B(1), 6);
        play.Do(new Shoot(Seat.One, A(1), B(1)), 4, 3);
        var result = play.Do(new SpendMarkers(Seat.One, 0), 1, 1);
        Assert.DoesNotContain(result.Events, e => e is GloriousDeedCompleted);
        Assert.Equal(0, play.State.One.VictoryPoints);
    }

    [Fact]
    [Trait("Rule", "Scenario IV p. 163, Good Hunting: a Long Range Ranged Attack takes an enemy Out of Action")]
    public void ALongRangeKillIsGoodHunting()
    {
        // 18" range: centres 10.001" apart is just over half, so Long Range. Lowest two of 6, 5, 4 = 9 hits; 5 + 4 = 9 kills.
        var play = Trench("4", "18", "14.001", "18");
        var result = play.Do(new Shoot(Seat.One, A(1), B(1)), 6, 5, 4, 5, 4);
        Assert.True(result.Events.OfType<RangedAttackDeclared>().Single().LongRange);
        var kill = result.Events.ToList().FindIndex(e => e is InjuryRolled);
        Assert.Equal(Deed(Seat.One, GloriousDeed.GoodHunting, A(1)), result.Events[kill + 1]);
        // The last enemy is gone: the match ends after the deed, and the deed's point counts.
        Assert.IsType<MatchEnded>(result.Events[kill + 2]);
        Assert.Equal(1, play.State.One.VictoryPoints);
    }

    [Fact]
    public void AShortRangeKillIsNotGoodHunting()
    {
        var play = Trench("4", "18", "14", "18");
        var result = play.Do(new Shoot(Seat.One, A(1), B(1)), 5, 4, 5, 4);
        Assert.False(result.Events.OfType<RangedAttackDeclared>().Single().LongRange);
        Assert.Equal(ModelStatus.OutOfAction, play.M(B(1)).Status);
        Assert.DoesNotContain(result.Events, e => e is GloriousDeedCompleted);
    }

    [Fact]
    [Trait("Rule", "Scenario IV p. 163, Survive to Tell the Tale: two mine Injury Rolls, neither Out of Action")]
    public void TwoMinesSurvivedInOneMoveIsSurviveToTellTheTale()
    {
        // Mines at x 18.5 and 22 on y 25: a move from 14 to 20 halts at both. Two Minor Hits (1, 1).
        var play = Trench("14", "25", "35", "3", P("18.5", "25"), P("23", "25"));
        var result = play.Do(new MoveModel(Seat.One, A(1), Path(("20", "25"))), 1, 1, 1, 1);
        var exploded = result.Events.Select((e, i) => (e, i)).Where(x => x.e is MineExploded).Select(x => x.i).ToList();
        Assert.Equal(2, exploded.Count);
        Assert.Equal(Deed(Seat.One, GloriousDeed.SurviveToTellTheTale, A(1)), result.Events[exploded[1] + 1]);
        Assert.Single(result.Events.OfType<GloriousDeedCompleted>());
        Assert.Equal(1, play.State.One.VictoryPoints);
    }

    [Fact]
    [Trait("Rule", "pp. 149-150: a deed is completed once, by whoever completes it first")]
    public void ADeedIsCompletedOnlyOnce()
    {
        var play = Claim();
        play.Mark(B(1), 5).Mark(B(2), 5).Put(B(2), "10", "4");
        play.Do(new Shoot(Seat.One, A(1), B(1)), 4, 3);
        play.Do(new SpendMarkers(Seat.One, 0), 1, 1);
        Assert.Single(play.State.Deeds.Completed);
        // A second sixth marker, on another enemy, completes nothing more.
        var deeds = play.State.Deeds;
        var before = play.State with
        {
            InProgress = new RollInProgress(RollStage.InjuryRoll, A(2), B(2), Kind: AttackKind.Ranged),
            Models = [.. play.State.Models.Select(m => m.Id == A(2) ? m with { Position = P("4", "4") } : m)],
        };
        var after = before with { InProgress = null, Models = [.. before.Models.Select(m => m.Id == B(2) ? m with { BloodMarkers = 6 } : m)] };
        var tracked = Deeds.Track(before, new InjuryRolled(A(2), B(2), Injury(1, 1)), after);
        Assert.Empty(tracked.Deeds.Owed);
        Assert.Equal(deeds.Completed, tracked.Deeds.Completed);
    }

    // ---- Deeds.Track itself, for deeds that need a longer game to reach ----

    private static InjuryRollResult Injury(int d1, int d2, bool alreadyDown = false) => InjuryRoll.Roll(new FixedDice(d1, d2), targetAlreadyDown: alreadyDown);

    // The state as an Injury Roll by attacker on target is about to be applied, and the state after it, with the target
    // taken Out of Action.
    private static (MatchState Before, MatchState After) Kill(MatchState state, ModelId attacker, ModelId target, AttackKind kind, bool freeFight = false, ModelId? active = null)
    {
        var before = state with { InProgress = new RollInProgress(RollStage.InjuryRoll, attacker, target, Kind: kind, FreeFight: freeFight), ActiveModel = active ?? attacker };
        var after = before with
        {
            InProgress = null,
            Models = [.. before.Models.Select(m => m.Id == target ? m with { Status = ModelStatus.OutOfAction, Position = null } : m)],
        };
        return (before, after);
    }

    private static MatchState Track(MatchState before, GameEvent e, MatchState after) => Deeds.Track(before, e, after);

    [Fact]
    [Trait("Rule", "Scenario I p. 153, Lord of War: one model takes two enemies Out of Action with Melee Attacks in one Turn")]
    public void TwoMeleeKillsInATurnIsLordOfWar()
    {
        var state = new BoardPlay().ToActivation().State;
        var (b1, a1) = Kill(state, A(1), B(1), AttackKind.Melee);
        var first = Track(b1, new InjuryRolled(A(1), B(1), Injury(5, 4)), a1);
        Assert.Empty(first.Deeds.Owed);
        // The second, as a free attack in the opponent's activation, still the same Turn.
        var (b2, a2) = Kill(first, A(1), B(2), AttackKind.Melee, freeFight: true, active: B(2));
        var second = Track(b2, new InjuryRolled(A(1), B(2), Injury(5, 4)), a2);
        Same([new CompletedDeed(GloriousDeed.LordOfWar, Seat.One, A(1))], second.Deeds.Owed);

        // A new Turn starts the count again; a ranged kill does not count.
        var turned = Track(first, new TurnStarted(2), first);
        var (b3, a3) = Kill(turned, A(1), B(2), AttackKind.Melee);
        Assert.Empty(Track(b3, new InjuryRolled(A(1), B(2), Injury(5, 4)), a3).Deeds.Owed);
        var (b4, a4) = Kill(first, A(1), B(2), AttackKind.Ranged);
        Assert.Empty(Track(b4, new InjuryRolled(A(1), B(2), Injury(5, 4)), a4).Deeds.Owed);
    }

    [Fact]
    [Trait("Rule", "Scenario I p. 153, Resist and Bite: a model that began its activation Down takes an enemy Out of Action in it")]
    public void AKillByAModelThatStoodUpIsResistAndBite()
    {
        var state = new BoardPlay().ToActivation().State;
        var started = Track(state, new ActivationStarted(Seat.One, A(1)), state);
        var stood = Track(started, new ModelStoodUp(A(1)), started);
        var (before, after) = Kill(stood, A(1), B(1), AttackKind.Ranged);
        Same([new CompletedDeed(GloriousDeed.ResistAndBite, Seat.One, A(1))], Track(before, new InjuryRolled(A(1), B(1), Injury(5, 4)), after).Deeds.Owed);

        // A model that began its activation Standing does not; nor does the next activation of the one that stood up.
        var (b2, a2) = Kill(started, A(1), B(1), AttackKind.Ranged);
        Assert.Empty(Track(b2, new InjuryRolled(A(1), B(1), Injury(5, 4)), a2).Deeds.Owed);
        var next = Track(stood, new ActivationStarted(Seat.One, A(2)), stood);
        var (b3, a3) = Kill(next, A(2), B(1), AttackKind.Ranged);
        Assert.Empty(Track(b3, new InjuryRolled(A(2), B(1), Injury(5, 4)), a3).Deeds.Owed);
    }

    [Fact]
    [Trait("Rule", "Scenario IV p. 163, Headshot: a model that retreated earlier in its activation kills an enemy it retreated from")]
    public void KillingAnEnemyRetreatedFromIsHeadshot()
    {
        var play = TrenchWarfare(2).ToTrenchActivation(
            [.. Enumerable.Range(0, Mines.Count).Select(k => P("29.5", (1.5 + 3 * k).ToString(System.Globalization.CultureInfo.InvariantCulture)))], Mines1And2Live);
        play.Put(A(1), "10", "18").Put(B(1), "11.4", "18").Put(B(2), "10", "30");
        var state = Track(play.State, new ActivationStarted(Seat.One, A(1)), play.State);
        var declared = Track(state, new RetreatDeclared(A(1), Path(("7", "18"))), state);
        Same([B(1)], declared.Deeds.RetreatingFrom);
        var moved = Track(declared, new RetreatResolved(A(1), Path(("7", "18")), L("3")), declared);
        Same([B(1)], moved.Deeds.RetreatedFrom);
        var (before, after) = Kill(moved, A(1), B(1), AttackKind.Ranged);
        Same([new CompletedDeed(GloriousDeed.Headshot, Seat.One, A(1))], Track(before, new InjuryRolled(A(1), B(1), Injury(5, 4)), after).Deeds.Owed);

        // Not an enemy it did not retreat from, and not when the retreat never moved (taken Down by a free attack).
        var (b2, a2) = Kill(moved, A(1), B(2), AttackKind.Ranged);
        Assert.Empty(Track(b2, new InjuryRolled(A(1), B(2), Injury(5, 4)), a2).Deeds.Owed);
        var stayed = Track(declared, new RetreatResolved(A(1), [], Length.Zero), declared);
        Assert.Empty(stayed.Deeds.RetreatedFrom);
    }

    [Fact]
    [Trait("Rule", "Scenario IV p. 163, Into the Trenches!: charge an enemy in a trench section, then kill it in melee")]
    public void ChargingIntoATrenchAndKillingIsIntoTheTrenches()
    {
        var play = Trench("28", "18", "32.5", "18"); // B1 in trench-m (x 31.5-34, y 14-22)
        var started = play.State;
        var reached = Track(started, new ChargeMoved(A(1), B(1), Path(("31", "18")), L("3"), true), started with
        {
            Models = [.. started.Models.Select(m => m.Id == A(1) ? m with { Position = P("31", "18") } : m)],
        });
        Assert.Equal(B(1), reached.Deeds.ChargedInTrench);
        var (before, after) = Kill(reached, A(1), B(1), AttackKind.Melee);
        Same([new CompletedDeed(GloriousDeed.IntoTheTrenches, Seat.One, A(1))], Track(before, new InjuryRolled(A(1), B(1), Injury(5, 4)), after).Deeds.Owed);

        // A charge at an enemy out of the trenches, or one that fell short, sets nothing.
        var open = Trench("20", "18", "26", "18");
        Assert.Null(Track(open.State, new ChargeMoved(A(1), B(1), Path(("24.9", "18")), L("4.9"), true), open.State).Deeds.ChargedInTrench);
        Assert.Null(Track(started, new ChargeMoved(A(1), B(1), Path(("29", "18")), L("1"), false), started).Deeds.ChargedInTrench);
    }

    [Fact]
    [Trait("Rule", "Scenarios I and IV, Hold Your Ground: the first warband to pass a Morale Check")]
    public void TheFirstPassedMoraleCheckIsHoldYourGround()
    {
        var state = new BoardPlay().ToActivation().State;
        var pass = SuccessRoll.Roll(new FixedDice(4, 3));
        var fail = SuccessRoll.Roll(new FixedDice(1, 1));
        Assert.Empty(Track(state, new MoraleCheckRolled(Seat.Two, fail, 1, 2, false), state).Deeds.Owed);
        var held = Track(state, new MoraleCheckRolled(Seat.Two, pass, 1, 2, false), state);
        Same([new CompletedDeed(GloriousDeed.HoldYourGround, Seat.Two, null)], held.Deeds.Owed);
    }

    [Fact]
    [Trait("Rule", "U96: a deed is announced right after the event that completed it, and a log without it is refused")]
    public void ALogMustAnnounceEveryDeedAndOnlyThoseCompleted()
    {
        var play = Claim();
        play.Mark(B(1), 5);
        play.Do(new Shoot(Seat.One, A(1), B(1)), 4, 3);
        var start = play.State;
        var log = play.Do(new SpendMarkers(Seat.One, 0), 1, 1).Events.ToList();
        static MatchState Fold(MatchState state, IEnumerable<GameEvent> events) => events.Aggregate(state, Reducer.Apply);
        var deed = log.FindIndex(e => e is GloriousDeedCompleted);
        Assert.Equal(StateHash.Compute(play.State), StateHash.Compute(Fold(start, log)));
        // Left out, the next event (here the end of the activation) is refused until the deed is announced.
        var next = new ActivationEnded(A(1), ActivationEndReason.Voluntary);
        Assert.Equal(log.Count - 1, deed);
        Fold(Fold(start, log), [next]);
        Assert.Throws<InvalidEventException>(() => Fold(start, [.. log.Take(deed), next]));
        Assert.Throws<InvalidEventException>(() => Fold(start, [.. log.Take(deed), Deed(Seat.Two, GloriousDeed.Bloodletting, B(1)), .. log.Skip(deed + 1)]));
        Assert.Throws<InvalidEventException>(() => Fold(start, [.. log.Take(deed), Deed(Seat.One, GloriousDeed.LordOfWar, A(1)), .. log.Skip(deed + 1)]));
        // A made-up deed where none was completed.
        Assert.Throws<InvalidEventException>(() => Fold(start, [.. log.Take(deed - 1), Deed(Seat.One, GloriousDeed.Bloodletting, A(1)), .. log.Skip(deed - 1)]));
    }
}
