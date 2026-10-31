using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.State;
using Sim;
using static Rules.Tests.Match.BoardPlay;
using static Rules.Tests.Match.MeleePlay;

namespace Rules.Tests.Match;

// A saved match record (M6) is replayed by the reducer alone, so the reducer must refuse any log that Decide could not
// have produced: a roll-off removed, repeated, altered or moved; a turn past the limit; play after the match is over;
// and any event put in place of one the rules make compulsory. Each test edits a real simulated log (or a scripted one)
// and checks that replaying the edit throws InvalidEventException, while the untouched log still replays.
public class ForgedLogTests
{
    private static readonly IReadOnlyList<Warband> Ranged = Content.Load(Content.DefaultPath());
    private static readonly IReadOnlyList<Warband> Melee = Content.Load(Content.MeleePath());

    // Seeds whose matches contain the event a test needs. Each test asserts that the event is really there, so a change
    // to the bots or the rules that moves it fails loudly instead of testing nothing.
    private const int RangedFirstSeed = 1;       // all three roll-offs, a failed Dash, a Down model standing, marker offers
    private const int RangedTieSeed = 6;         // a deployment-order roll-off decided after a tie
    private const int RangedInitiativeTie = 2;   // an Initiative roll-off decided after a tie
    private const int RangedShakenSeed = 5;      // a Risky shot that fails, a Shaken warband recovering, a failed Morale choice
    private const int RangedFleeSeed = 21;       // a Shaken warband fails its check and flees
    private const int MeleeEliminationSeed = 366;  // the last enemy model is taken Out of Action
    private const int MeleeTakenDownSeed = 10;   // a retreating model is taken Down by a free attack

    private static List<GameEvent> Log(int seed, bool melee = false) => [.. Runner.Run(seed, melee ? Melee : Ranged).Log];

    private static void Refused(IEnumerable<GameEvent> log, string what)
    {
        try
        {
            Reducer.Replay(log);
        }
        catch (InvalidEventException)
        {
            return;
        }
        Assert.Fail($"{what} was accepted.");
    }

    private static List<GameEvent> Without(List<GameEvent> log, int index) => [.. log.Where((_, i) => i != index)];

    private static List<GameEvent> Replace(List<GameEvent> log, int index, GameEvent e) => [.. log.Select((x, i) => i == index ? e : x)];

    private static List<GameEvent> Insert(List<GameEvent> log, int index, GameEvent e)
    {
        var copy = log.ToList();
        copy.Insert(index, e);
        return copy;
    }

    private static List<GameEvent> Swap(List<GameEvent> log, int index)
    {
        var copy = log.ToList();
        (copy[index], copy[index + 1]) = (copy[index + 1], copy[index]);
        return copy;
    }

    private static int IndexOf(List<GameEvent> log, Func<GameEvent, bool> match)
    {
        var index = log.FindIndex(e => match(e));
        Assert.True(index >= 0, "The log does not contain the event this test needs.");
        return index;
    }

    // ---- D1: roll-offs ----

    private static RollOffResolved Flipped(RollOffResolved r) =>
        new(r.Purpose, r.Rounds.Select(x => new RollOffRound(x.SeatTwo, x.SeatOne)), r.Winner.Opponent());

    [Fact]
    [Trait("Rule", "Roll-offs p. 31: a recorded roll-off is needed, used once, and decides what it was rolled for")]
    public void EveryRollOffInARealMatchIsRemovedDuplicatedAlteredOrMovedOnlyAtTheCostOfTheLog()
    {
        var purposes = new HashSet<RollOffPurpose>();
        var ties = 0;
        foreach (var seed in new[] { RangedFirstSeed, RangedInitiativeTie, RangedTieSeed })
        {
            var log = Log(seed);
            Reducer.Replay(log);
            for (var i = 0; i < log.Count; i++)
            {
                if (log[i] is not RollOffResolved roll)
                {
                    continue;
                }
                purposes.Add(roll.Purpose);
                ties += roll.Rounds.Length > 1 ? 1 : 0;
                Refused(Without(log, i), $"seed {seed}: the {roll.Purpose} roll-off removed");
                Refused(Insert(log, i + 1, roll), $"seed {seed}: the {roll.Purpose} roll-off repeated");
                Refused(Replace(log, i, Flipped(roll)), $"seed {seed}: the {roll.Purpose} roll-off with the other seat winning");
                Refused(Swap(log, i), $"seed {seed}: the {roll.Purpose} roll-off moved after the event it decides");
                if (i > 0)
                {
                    Refused(Swap(log, i - 1), $"seed {seed}: the {roll.Purpose} roll-off moved before the event that calls for it");
                }
            }
        }
        // The Roles roll-off is only in Trench Warfare (stage S6); Server.Tests.TrenchWarfareTests forges it the same way.
        Assert.Equal(Enum.GetValues<RollOffPurpose>().Where(p => p != RollOffPurpose.Roles).ToHashSet(), purposes);
        Assert.True(ties >= 2, $"only {ties} tied roll-offs were checked");
    }

    // A deserializer might build an event without running its constructor; the reducer reads the winner from the dice
    // itself, so a record whose stated winner or rounds are inconsistent is still refused.
    private static RollOffResolved Unchecked(RollOffPurpose purpose, Seat winner, params (int One, int Two)[] rounds)
    {
        var e = (RollOffResolved)RuntimeHelpers.GetUninitializedObject(typeof(RollOffResolved));
        void Set(string name, object value) =>
            typeof(RollOffResolved).GetField($"<{name}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(e, value);
        Set(nameof(RollOffResolved.Purpose), purpose);
        Set(nameof(RollOffResolved.Winner), winner);
        Set(nameof(RollOffResolved.Rounds), rounds.Select(r => new RollOffRound(r.One, r.Two)).ToImmutableArray());
        return e;
    }

    [Fact]
    [Trait("Rule", "Roll-offs p. 31: the winner is the higher last die; only ties are rolled again")]
    public void ARollOffWhoseWinnerDisagreesWithItsDiceIsRefused()
    {
        var created = Reducer.Apply(MatchState.None, new MatchCreated(Play.Setup(2, 2)));

        var fair = Reducer.Apply(created, Unchecked(RollOffPurpose.DeploymentZone, Seat.Two, (3, 3), (2, 5)));
        Assert.Equal(new RollOffResult(RollOffPurpose.DeploymentZone, Seat.Two), fair.RollOff);

        Assert.Throws<InvalidEventException>(() => Reducer.Apply(created, Unchecked(RollOffPurpose.DeploymentZone, Seat.One, (2, 5))));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(created, Unchecked(RollOffPurpose.DeploymentZone, Seat.One, (4, 4))));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(created, Unchecked(RollOffPurpose.DeploymentZone, Seat.One, (5, 2), (6, 1))));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(created, Unchecked(RollOffPurpose.DeploymentZone, Seat.One)));
    }

    [Fact]
    [Trait("Rule", "Scenario I p. 152 / Initiative p. 32: the roll-off winner, after a tie too, is the seat that chooses or starts")]
    public void TheRollOffLoserCannotChooseTheZoneDeployFirstOrHoldTheInitiative()
    {
        // Seat Two wins every roll-off, each after one tied round.
        var play = new Play(Play.Setup(2, 2));
        play.Do(new StartMatch(), 3, 3, 2, 5);
        play.Do(new ChooseDeploymentZone(Seat.Two, "north"), 4, 4, 1, 6);
        play.DeployAll(2, 2, 3, 6);
        var log = play.Log;
        Reducer.Replay(log);

        var zone = IndexOf(log, e => e is DecisionRequested { Kind: DecisionKind.ChooseDeploymentZone });
        Assert.Equal(Seat.Two, ((DecisionRequested)log[zone]).Seat);
        Refused(Replace(log, zone, new DecisionRequested(DecisionKind.ChooseDeploymentZone, Seat.One)), "the zone choice given to the loser");

        var order = IndexOf(log, e => e is DeploymentOrderSet);
        Assert.Equal(new DeploymentOrderSet(Seat.Two), log[order]);
        Refused(Replace(log, order, new DeploymentOrderSet(Seat.One)), "the roll-off loser deploying first");

        var initiative = IndexOf(log, e => e is InitiativeDetermined);
        var held = (InitiativeDetermined)log[initiative];
        Assert.Equal(Seat.Two, held.Holder);
        Assert.Equal(held.CountSeatOne, held.CountSeatTwo);
        Refused(Replace(log, initiative, new InitiativeDetermined(held.CountSeatOne, held.CountSeatTwo, Seat.One)), "the Initiative given to the roll-off loser");
    }

    [Fact]
    [Trait("Rule", "Scenario I p. 152 / Initiative p. 32: a roll-off happens only where counts are equal")]
    public void UnequalSidesNeitherRollOffNorLetTheWrongSeatGoFirst()
    {
        // Three models against two: seat One deploys first, seat Two holds the Initiative, and no roll-off is involved.
        var play = new Play(Play.Setup(3, 2));
        play.Do(new StartMatch(), 6, 1);
        play.Do(new ChooseDeploymentZone(Seat.One, "north"));
        var zoned = play.State;
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(zoned, new DeploymentOrderSet(Seat.Two)));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(zoned, new RollOffResolved(RollOffPurpose.DeploymentOrder, [new RollOffRound(1, 6)], Seat.Two)));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(zoned, new RollOffResolved(RollOffPurpose.Initiative, [new RollOffRound(1, 6)], Seat.Two)));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(zoned, new RollOffResolved(RollOffPurpose.DeploymentZone, [new RollOffRound(1, 6)], Seat.Two)));

        play.DeployAll();
        var log = play.Log;
        Assert.DoesNotContain(log, e => e is RollOffResolved { Purpose: not RollOffPurpose.DeploymentZone });
        var initiative = IndexOf(log, e => e is InitiativeDetermined);
        Refused(Replace(log, initiative, new InitiativeDetermined(3, 2, Seat.One)), "the larger side holding the Initiative");
        Refused(Insert(log, initiative, new RollOffResolved(RollOffPurpose.Initiative, [new RollOffRound(6, 1)], Seat.One)), "an Initiative roll-off between unequal sides");
    }

    // ---- D2, D3: the turn limit and the end of the match ----

    [Fact]
    [Trait("Rule", "Scenario I p. 153: the match ends after the final turn; no turn follows it")]
    public void TheFinalTurnEndsTheMatchAndNoTurnFollowsIt()
    {
        var log = Log(RangedFirstSeed);
        var ended = Assert.IsType<MatchEnded>(log[^1]);
        Assert.Equal(OutcomeReason.VictoryPoints, ended.Reason);
        var last = IndexOf(log, e => e is TurnEnded { Turn: 4 });
        Assert.Equal(log.Count - 2, last);
        Reducer.Replay(log[..^1]);   // the state just before MatchEnded is reachable

        Refused(Replace(log, log.Count - 1, new TurnStarted(5)), "a fifth turn of four");
        Refused(Replace(log, log.Count - 1, new MatchEnded(ended.Winner, OutcomeReason.AllEnemiesOutOfAction)), "the final turn ended by elimination");
        Refused(Replace(log, log.Count - 1, new MatchEnded(ended.Winner?.Opponent() ?? Seat.One, OutcomeReason.VictoryPoints)), "the wrong winner on points");
        Refused(Insert(log, log.Count, ended), "a second MatchEnded");
        Refused(Swap(log, log.Count - 2), "MatchEnded before the final TurnEnded");
        var earlier = IndexOf(log, e => e is TurnEnded { Turn: 2 });
        Refused(Insert(log, earlier + 1, new MatchEnded(null, OutcomeReason.VictoryPoints)), "MatchEnded after turn 2 of 4");

        // The same without a battlefield (the M2 setups): FinalTurnComplete, and still no turn after the last.
        var plain = new Play(Play.Setup(1, 1, turnLimit: 1)).ToActivation();
        plain.RunActivations();
        Assert.Equal(new MatchEnded(null, OutcomeReason.FinalTurnComplete), plain.Log[^1]);
        Refused(Replace(plain.Log, plain.Log.Count - 1, new TurnStarted(2)), "a second turn of one");
        Refused(Replace(plain.Log, plain.Log.Count - 1, new MatchEnded(null, OutcomeReason.VictoryPoints)), "points without a battlefield");
    }

    [Fact]
    [Trait("Rule", "Scenario I p. 153: a warband with no models left on the battlefield has lost, at once")]
    public void AnEliminationEndsTheMatchAtOnce()
    {
        var log = Log(MeleeEliminationSeed, melee: true);
        var ended = Assert.IsType<MatchEnded>(log[^1]);
        Assert.Equal(OutcomeReason.AllEnemiesOutOfAction, ended.Reason);
        var injury = Assert.IsType<InjuryRolled>(log[^2]);
        Assert.Equal(InjuryOutcome.OutOfAction, injury.Injury.Outcome);
        var before = Reducer.Replay(log[..^1]);   // the decisive Injury Roll itself is fine
        var active = before.ActiveModel!.Value;

        Refused(Replace(log, log.Count - 1, new ActivationEnded(active, ActivationEndReason.Voluntary)), "play continuing after an elimination");
        Refused(Insert(log, log.Count - 1, new ActivationEnded(active, ActivationEndReason.Voluntary)), "an event between the elimination and MatchEnded");
        Refused(Replace(log, log.Count - 1, new MatchEnded(ended.Winner!.Value.Opponent(), OutcomeReason.AllEnemiesOutOfAction)), "the eliminated side winning");
        Refused(Replace(log, log.Count - 1, new MatchEnded(ended.Winner, OutcomeReason.VictoryPoints)), "an elimination scored as points");
        Refused(Swap(log, log.Count - 2), "MatchEnded before the Injury Roll that decides it");
        Refused(Insert(log, log.Count, ended), "a second MatchEnded");
    }

    [Fact]
    [Trait("Rule", "Morale p. 50: a warband that flees loses, at once")]
    public void AFleeEndsTheMatchAtOnce()
    {
        var log = Log(RangedFleeSeed);
        var ended = Assert.IsType<MatchEnded>(log[^1]);
        Assert.Equal(OutcomeReason.OpponentFled, ended.Reason);
        var fled = Assert.IsType<WarbandFled>(log[^2]);
        Reducer.Replay(log[..^1]);

        Refused(Replace(log, log.Count - 1, new TurnEnded(Reducer.Replay(log[..^1]).Turn)), "the turn ending after a warband fled");
        Refused(Replace(log, log.Count - 1, new MatchEnded(fled.Seat, OutcomeReason.OpponentFled)), "the fleeing side winning");
        Refused(Replace(log, log.Count - 1, new MatchEnded(fled.Seat.Opponent(), OutcomeReason.VictoryPoints)), "a flee scored as points");
        Refused(Swap(log, log.Count - 2), "MatchEnded before the warband fled");
    }

    // ---- D4: a failed Risky roll ends the activation ----

    [Fact]
    [Trait("Rule", "Dash p. 34: a failed Dash ends the activation, and nothing else may happen first")]
    public void AFailedDashEndsTheActivationAndNothingElse()
    {
        var log = Log(RangedFirstSeed);
        var roll = IndexOf(log, e => e is DashRolled { Check.Outcome: SuccessOutcome.Failure });
        var end = Assert.IsType<ActivationEnded>(log[roll + 1]);
        Assert.Equal(ActivationEndReason.RiskyFailure, end.Reason);
        var after = Reducer.Replay(log[..(roll + 1)]);
        Assert.True(after.MustEndActivation);

        Refused(Without(log, roll + 1), "a failed Dash followed by whatever came next");
        Refused(Replace(log, roll + 1, end with { Reason = ActivationEndReason.Voluntary }), "a failed Dash ended voluntarily");
        var position = after.FindModel(end.Model)!.Position!.Value;
        Refused(Insert(log, roll + 1, new ModelMoved(end.Model, MoveKind.Move, [position], Rules.Core.Geometry.Length.Zero)), "a Move after a failed Dash");
        Refused(Insert(log, roll + 1, new DecisionRequested(DecisionKind.SpendMarkersOnSuccessRoll, Seat.One)), "a decision after a failed Dash");

        // And the other way round: an activation cannot claim a Risky failure it never had.
        var voluntary = IndexOf(log, e => e is ActivationEnded { Reason: ActivationEndReason.Voluntary });
        Refused(Replace(log, voluntary, ((ActivationEnded)log[voluntary]) with { Reason = ActivationEndReason.RiskyFailure }), "a Risky failure that never happened");
    }

    [Fact]
    [Trait("Rule", "Shaken p. 50: a Shaken warband's failed attack roll is a Risky failure and ends the activation")]
    public void AFailedRiskyAttackEndsTheActivation()
    {
        var log = Log(RangedShakenSeed);
        var shot = IndexOf(log, e => e is RangedSuccessRolled { Check.RiskyFailure: true });
        Assert.Equal(ActivationEndReason.RiskyFailure, Assert.IsType<ActivationEnded>(log[shot + 1]).Reason);
        Refused(Without(log, shot + 1), "a failed Risky shot followed by whatever came next");
        Refused(Replace(log, shot + 1, ((ActivationEnded)log[shot + 1]) with { Reason = ActivationEndReason.Voluntary }), "a failed Risky shot ended voluntarily");

        // A Shaken Brawler's Melee Attack, failed on two 1s.
        var play = Skirmish("4", "2", "5.4", "2").Shaken(Seat.One);
        var fight = TurnSequence.Decide(play.State, new FightModel(Seat.One, A(1), B(1)), new FixedDice(1, 1)).Events;
        Assert.True(Assert.IsType<MeleeSuccessRolled>(fight[^2]).Check.RiskyFailure);
        Assert.Equal(new ActivationEnded(A(1), ActivationEndReason.RiskyFailure), fight[^1]);
        var lost = fight.Take(fight.Length - 1).Aggregate(play.State, Reducer.Apply);
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(lost, new ActivationEnded(A(1), ActivationEndReason.Voluntary)));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(lost, new RetreatDeclared(A(1), Path(("1.5", "2")))));
        Assert.False(Reducer.Apply(lost, fight[^1]).MustEndActivation);
    }

    [Fact]
    [Trait("Rule", "U49: a failed free attack simply misses; the retreating model's activation goes on")]
    public void AFailedRiskyFreeAttackDoesNotEndAnyActivation()
    {
        var play = Skirmish("4", "2", "5.4", "2").Shaken(Seat.Two);
        play.Do(new RetreatModel(Seat.One, A(1), Path(("1.5", "2"))));
        var free = TurnSequence.Decide(play.State, new FreeFight(Seat.Two, B(1)), new FixedDice(1, 1)).Events;
        Assert.True(free.OfType<MeleeSuccessRolled>().Single().Check.RiskyFailure);
        Assert.DoesNotContain(free, e => e is ActivationEnded);
        Assert.False(free.Aggregate(play.State, Reducer.Apply).MustEndActivation);
    }

    // ---- D5: Blood Marker offers ----

    [Fact]
    [Trait("Rule", "U52: nobody spends a friendly model's markers on the Injury Roll of a shot forced onto it")]
    public void NobodyIsAskedToSpendAFriendlyModelsMarkersOnItsInjuryRoll()
    {
        var play = new BoardPlay(3, Everyone(Gunman)).ToActivation();
        play.Put(A(1), "4", "2").Put(B(1), "12", "2").Put(A(2), "12", "3.2").Put(A(3), "2", "20").Put(B(2), "30", "30").Put(B(3), "30", "32");
        play.Mark(A(2), 2);
        play.Do(new ActivateModel(Seat.One, A(1)));
        var forced = play.State;
        foreach (var e in TurnSequence.Decide(forced, new Shoot(Seat.One, A(1), B(1)), new FixedDice(2)).Events)
        {
            forced = Reducer.Apply(forced, e);
        }
        forced = Reducer.Apply(forced, new FriendlyTargetChosen(A(1), A(2), false, false, null));
        var hit = Reducer.Apply(forced, new RangedSuccessRolled(A(1), A(2), SuccessRoll.Roll(new FixedDice(4, 3), 0, 0, false)));
        Assert.Equal((RollStage.InjuryRoll, true), (hit.InProgress!.Stage, hit.InProgress.FriendlyFire));

        Assert.Throws<InvalidEventException>(() => Reducer.Apply(hit, new DecisionRequested(DecisionKind.SpendMarkersOnInjuryRoll, Seat.One)));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(hit, new DecisionRequested(DecisionKind.SpendMarkersOnInjuryRoll, Seat.Two)));
        var injured = Reducer.Apply(hit, new InjuryRolled(A(1), A(2), InjuryRoll.Roll(new FixedDice(3, 3), 0, 0, [0, 0], false)));
        Assert.Null(injured.InProgress);
    }

    [Fact]
    [Trait("Rule", "Blood Markers pp. 27-28: the opponent is always asked before a roll the markers could change")]
    public void AMarkerOfferCannotBeLeftOut()
    {
        var log = Log(RangedFirstSeed);
        foreach (var kind in new[] { DecisionKind.SpendMarkersOnSuccessRoll, DecisionKind.SpendMarkersOnInjuryRoll })
        {
            var offer = IndexOf(log, e => e is DecisionRequested d && d.Kind == kind);
            Assert.IsType<MarkersSpent>(log[offer + 1]);
            Refused(Without(Without(log, offer + 1), offer), $"a roll with its {kind} offer and answer left out");
        }
    }

    // ---- D6: compulsory follow-ups ----

    [Fact]
    [Trait("Rule", "U30: a Down model stands up as soon as its activation starts")]
    public void ADownModelStandsUpAsSoonAsItActivates()
    {
        var log = Log(RangedFirstSeed);
        var up = IndexOf(log, e => e is ModelStoodUp);
        Assert.IsType<ActivationStarted>(log[up - 1]);
        Refused(Without(log, up), "a Down model's activation without standing up");
        Refused(Replace(log, up, new ActivationEnded(((ModelStoodUp)log[up]).Model, ActivationEndReason.Voluntary)), "a Down model ending its activation before standing");
        Refused(Swap(log, up), "standing up after the first action");
    }

    [Fact]
    [Trait("Rule", "Down p. 49: a model taken Down during its own activation ends it")]
    public void AModelTakenDownInItsOwnRetreatEndsItsActivation()
    {
        var log = Log(MeleeTakenDownSeed, melee: true);
        var end = IndexOf(log, e => e is ActivationEnded { Reason: ActivationEndReason.TakenDown });
        Assert.IsType<RetreatResolved>(log[end - 1]);
        Assert.False(((RetreatResolved)log[end - 1]).Moved);
        Refused(Without(log, end), "a model taken Down that goes on with its activation");
        Refused(Replace(log, end, ((ActivationEnded)log[end]) with { Reason = ActivationEndReason.Voluntary }), "an activation ended voluntarily after being taken Down");
    }

    [Fact]
    [Trait("Rule", "Morale p. 50: a Shaken warband that passes recovers at once")]
    public void AShakenWarbandThatPassesRecovers()
    {
        var log = Log(RangedShakenSeed);
        var recovered = IndexOf(log, e => e is WarbandRecovered);
        // The check comes just before, or before the Glorious Deed it completed (Hold Your Ground, U96).
        var check = Assert.IsType<MoraleCheckRolled>(log[..recovered].Last(e => e is not GloriousDeedCompleted));
        Assert.True(check.WasShaken);
        Refused(Without(log, recovered), "a passed check by a Shaken warband with no recovery");
    }

    [Fact]
    [Trait("Rule", "Morale p. 50: a warband that fails chooses at once; one that was already Shaken flees")]
    public void AFailedMoraleCheckIsAnsweredAtOnce()
    {
        var log = Log(RangedShakenSeed);
        var asked = IndexOf(log, e => e is DecisionRequested { Kind: DecisionKind.FailedMorale });
        Assert.IsType<MoraleCheckRolled>(log[asked - 1]);
        Refused(Without(log, asked), "a failed Morale Check with no choice offered");

        var flee = Log(RangedFleeSeed);
        var fled = IndexOf(flee, e => e is WarbandFled { Reason: FledReason.FailedWhileShaken });
        var failed = Assert.IsType<MoraleCheckRolled>(flee[fled - 1]);
        Assert.True(failed.WasShaken);
        var state = Reducer.Replay(flee[..fled]);
        Assert.True(state.SeatFor(failed.Seat).MustFlee);
        foreach (var instead in new GameEvent[]
        {
            new DecisionRequested(DecisionKind.FailedMorale, failed.Seat),
            new TurnEnded(state.Turn),
            new MatchEnded(failed.Seat.Opponent(), OutcomeReason.OpponentFled),
            new WarbandFled(failed.Seat.Opponent(), FledReason.FailedWhileShaken),
        })
        {
            Assert.Throws<InvalidEventException>(() => Reducer.Apply(state, instead));
        }
        Assert.False(Reducer.Apply(state, flee[fled]).SeatFor(failed.Seat).MustFlee);
    }

    // ---- T2: every single-event edit of a real log ----

    // Removing, repeating or swapping any one event of a real match either breaks the log (InvalidEventException, never
    // any other exception) or leaves an alternative history the rules allow. The only removals that survive are an
    // optional Move ACTION (the model simply did not move) and the last event (a log cut short is a match in progress).
    [Fact]
    [Trait("Rule", "Architecture: replay refuses edited logs")]
    public void EverySingleEventEditOfARealMatchIsRefusedUnlessItIsAnOptionalMove()
    {
        var skipped = new List<string>();
        var duplicated = new List<string>();
        foreach (var (seed, melee) in new[] { (1, false), (2, false), (5, false), (21, false), (1, true), (10, true) })
        {
            var log = Log(seed, melee);
            for (var i = 0; i < log.Count; i++)
            {
                if (Accepted(Without(log, i)) && !(log[i] is ModelMoved { Kind: MoveKind.Move } || i == log.Count - 1))
                {
                    skipped.Add($"{(melee ? "melee" : "ranged")} {seed} #{i} {log[i].GetType().Name}");
                }
                if (Accepted(Insert(log, i + 1, log[i])))
                {
                    duplicated.Add($"{(melee ? "melee" : "ranged")} {seed} #{i} {log[i].GetType().Name}");
                }
                if (i + 1 < log.Count)
                {
                    Accepted(Swap(log, i));   // must not crash; an accepted swap is a legal reordering of two independent steps
                }
            }
        }
        Assert.Empty(skipped);
        Assert.Empty(duplicated);

        static bool Accepted(List<GameEvent> edited)
        {
            try
            {
                Reducer.Replay(edited);
                return true;
            }
            catch (InvalidEventException)
            {
                return false;
            }
        }
    }

    // ---- D8: unknown commands ----

    private sealed record NotARealCommand : Command;

    [Fact]
    [Trait("Rule", "Architecture: rejected commands change nothing")]
    public void AnUnknownCommandTypeIsRejectedInEveryPhaseWithoutDiceOrEvents()
    {
        var play = new Play(Play.Setup(2, 2));
        var states = new List<MatchState> { MatchState.None, play.State };
        play.Do(new StartMatch(), 6, 1);
        states.Add(play.State);
        play.Do(new ChooseDeploymentZone(Seat.One, "north"), 5, 2);
        play.DeployAll(6, 1);
        play.Do(new ChooseFirstActivation(Seat.One, Seat.One));
        states.Add(play.State);
        play.Do(new ActivateModel(Seat.One, Play.A(1)));
        states.Add(play.State);
        foreach (var state in states)
        {
            var before = StateHash.Compute(state);
            var result = TurnSequence.Decide(state, new NotARealCommand(), new FixedDice());
            Assert.False(result.IsAccepted);
            Assert.Equal(ReasonCode.UnknownCommand, result.Rejection!.Code);
            Assert.Empty(result.Events);
            Assert.Equal(before, StateHash.Compute(state));
        }
    }
}
