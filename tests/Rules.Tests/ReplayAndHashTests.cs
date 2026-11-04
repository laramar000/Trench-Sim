using System.Collections.Immutable;
using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.State;

namespace Rules.Tests;

public class ReplayAndHashTests
{
    private static Play Opening() => new Play(Play.Setup(2, 2)).ToActivation();

    // ---------- Reducer ----------

    [Fact]
    [Trait("Rule", "Architecture: state changes only through events")]
    public void ReplayingTheLogRebuildsTheSameStateWithoutAnyDice()
    {
        var play = Opening();
        play.RunActivations(5, 2);

        var rebuilt = Reducer.Replay(play.Log);

        Assert.Equal(StateHash.Canonical(play.State), StateHash.Canonical(rebuilt));
        Assert.Equal(StateHash.Compute(play.State), StateHash.Compute(rebuilt));
    }

    [Fact]
    [Trait("Rule", "Architecture: state changes only through events")]
    public void ReplayingEveryPrefixMatchesTheStateAfterEachCommand()
    {
        var play = new Play(Play.Setup(2, 2));
        var state = MatchState.None;
        foreach (var e in play.Log)
        {
            state = Reducer.Apply(state, e);
        }
        var commands = new (Command, int[])[]
        {
            (new StartMatch(), [6, 1]),
            (new ChooseDeploymentZone(Seat.One, "north"), [5, 2]),
        };
        foreach (var (command, dice) in commands)
        {
            var before = play.Log.Count;
            play.Do(command, dice);
            state = play.Log.Skip(before).Aggregate(state, Reducer.Apply);
            Assert.Equal(StateHash.Compute(play.State), StateHash.Compute(state));
        }
    }

    [Fact]
    public void ApplyDoesNotChangeTheStateItWasGiven()
    {
        var play = Opening();
        var before = StateHash.Canonical(play.State);

        Reducer.Apply(play.State, new ActivationStarted(Seat.One, Play.A(1)));

        Assert.Equal(before, StateHash.Canonical(play.State));
    }

    [Fact]
    public void ApplyingTheSameEventToTheSameStateTwiceGivesTheSameResult()
    {
        var play = Opening();
        var e = new ActivationStarted(Seat.One, Play.A(1));

        Assert.Equal(
            StateHash.Compute(Reducer.Apply(play.State, e)),
            StateHash.Compute(Reducer.Apply(play.State, e)));
    }

    [Fact]
    public void EventsThatCannotFollowTheStateAreRefused()
    {
        var created = new MatchCreated(Play.Setup(1, 1));
        var state = Reducer.Apply(MatchState.None, created);

        Assert.Throws<InvalidEventException>(() => Reducer.Apply(MatchState.None, new TurnStarted(1)));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(state, created));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(state, new ActivationStarted(Seat.One, Play.A(1))));
    }

    [Fact]
    public void NothingFollowsTheEndOfTheMatch()
    {
        var play = new Play(Play.Setup(3, 3)).ToActivation();
        foreach (var id in new[] { "A1", "A2", "B1", "B2" })
        {
            play.SetStatus(id, ModelStatus.Down);
        }
        play.State = play.State with { Two = play.State.Two with { Shaken = true } };
        play.RunActivations();
        play.Do(new ChooseMoraleOrder(Seat.One, Seat.Two), 1, 1);

        Assert.Equal(Phase.Ended, play.State.Phase);
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(play.State, new TurnStarted(2)));
        Assert.Equal(ReasonCode.MatchEnded, play.Try(new StartMatch()).Rejection!.Code);
    }

    // Every command in every phase of a scripted match: a command that is not legal is refused with no
    // events, no state change and no dice (FixedDice with no values throws if anything is rolled).
    [Fact]
    [Trait("Rule", "Architecture: rejected commands change nothing")]
    public void EveryIllegalCommandInEveryPhaseIsRefusedWithoutRollingDice()
    {
        var play = new Play(Play.Setup(3, 3));
        var snapshots = new List<MatchState> { play.State };
        play.Do(new StartMatch(), 6, 1);
        snapshots.Add(play.State);
        play.Do(new ChooseDeploymentZone(Seat.One, "north"), 5, 2);
        snapshots.Add(play.State);
        while (play.State.Phase == Phase.Deployment)
        {
            play.Do(TurnSequence.LegalCommands(play.State)[0], play.State.Models.Count(m => !m.Deployed) == 1 ? [6, 1] : []);
            snapshots.Add(play.State);
        }
        play.Do(new ChooseFirstActivation(Seat.One, Seat.One));
        snapshots.Add(play.State);
        play.Do(new ActivateModel(Seat.One, Play.A(1)));
        snapshots.Add(play.State);
        play.SetStatus("A2", ModelStatus.Down);
        play.SetStatus("B1", ModelStatus.Down);
        play.SetStatus("B2", ModelStatus.Down);
        play.Do(new EndActivation(Seat.One));
        play.RunActivations(1, 1);  // seat Two alone must check, and fails
        snapshots.Add(play.State);
        play.Do(new ResolveFailedMorale(Seat.Two, MoraleFailureChoice.BecomeShaken));
        snapshots.Add(play.State);

        var seats = new[] { Seat.One, Seat.Two };
        var models = new[] { Play.A(1), Play.A(2), Play.B(1), new ModelId("nobody") };
        var all = new List<Command> { new StartMatch(), new ChooseDeploymentZone(Seat.One, "north"), new ChooseDeploymentZone(Seat.Two, "moon") };
        foreach (var seat in seats)
        {
            all.Add(new EndActivation(seat));
            all.AddRange(seats.Select(first => new ChooseFirstActivation(seat, first)));
            all.AddRange(seats.Select(first => new ChooseMoraleOrder(seat, first)));
            all.AddRange(Enum.GetValues<MoraleFailureChoice>().Select(c => new ResolveFailedMorale(seat, c)));
            all.AddRange(models.Select(m => new DeployModel(seat, m)));
            all.AddRange(models.Select(m => new ActivateModel(seat, m)));
        }

        var checkedCount = 0;
        foreach (var state in snapshots)
        {
            var legal = TurnSequence.LegalCommands(state);
            var before = StateHash.Compute(state);
            foreach (var command in all.Where(c => !legal.Contains(c)))
            {
                var result = TurnSequence.Decide(state, command, new FixedDice());
                Assert.False(result.IsAccepted, $"{command} was accepted in {state.Phase}");
                Assert.Empty(result.Events);
                Assert.Equal(before, StateHash.Compute(state));
                checkedCount++;
            }
            foreach (var command in legal)
            {
                Assert.True(TurnSequence.Decide(state, command, new SeededDice(5)).IsAccepted, $"{command} was refused in {state.Phase}");
            }
        }
        Assert.True(checkedCount > 100);
    }

    // ---------- State hash ----------

    [Fact]
    [Trait("Rule", "Architecture: canonical state hash")]
    public void TheHashIsStableAcrossRepeatedCalls()
    {
        var play = Opening();

        Assert.Equal(StateHash.Compute(play.State), StateHash.Compute(play.State));
        Assert.Matches("^[0-9a-f]{64}$", StateHash.Compute(play.State));
    }

    [Fact]
    public void TheHashIgnoresTheOrderModelsAreListedIn()
    {
        var play = Opening();
        var reversed = play.State with { Models = [.. play.State.Models.Reverse()] };

        Assert.Equal(StateHash.Compute(play.State), StateHash.Compute(reversed));
    }

    [Fact]
    public void EveryPartOfTheStateChangesTheHash()
    {
        var s = Opening().State;
        var variants = new Dictionary<string, MatchState>
        {
            ["phase"] = s with { Phase = Phase.Morale },
            ["turn"] = s with { Turn = s.Turn + 1 },
            ["turnEnded"] = s with { TurnEnded = !s.TurnEnded },
            ["initiativeHolder"] = s with { InitiativeHolder = Seat.Two },
            ["firstActivator"] = s with { FirstActivator = Seat.Two },
            ["activeModel"] = s with { ActiveModel = Play.A(1) },
            ["pending"] = s with { Pending = new PendingDecision(DecisionKind.FailedMorale, Seat.One) },
            ["shaken"] = s with { One = s.One with { Shaken = true } },
            ["fled"] = s with { Two = s.Two with { Fled = true } },
            ["status"] = s with { Models = [.. s.Models.Select(m => m.Id == Play.A(1) ? m with { Status = ModelStatus.Down } : m)] },
            ["activated"] = s with { Models = [.. s.Models.Select(m => m.Id == Play.B(1) ? m with { ActivatedThisTurn = true } : m)] },
            ["outcome"] = s with { Outcome = new MatchOutcome(Seat.One, OutcomeReason.OpponentFled) },
            ["profile"] = s with { Setup = Play.Setup(2, 2, new RulesProfile(MoraleOrder: MoraleOrderRule.InitiativeHolderFirst)) },
            ["turnLimit"] = s with { Setup = Play.Setup(2, 2, turnLimit: 5) },
            ["deploymentFirst"] = s with { DeploymentFirst = s.DeploymentFirst!.Value.Opponent() },
            ["lastDeployer"] = s with { LastDeployer = s.LastDeployer!.Value.Opponent() },
            ["lastActivator"] = s with { LastActivator = Seat.Two },
            ["moraleFirst"] = s with { MoraleFirst = Seat.Two },
            ["moraleChecked"] = s with { One = s.One with { MoraleChecked = true } },
            ["moraleOutcome"] = s with { One = s.One with { MoraleOutcome = SuccessOutcome.Failure } },
            ["mustFlee"] = s with { Two = s.Two with { MustFlee = true } },
            ["zone"] = s with { One = s.One with { ZoneId = "south" } },
            ["deployed"] = s with { Models = [.. s.Models.Select(m => m.Id == Play.B(2) ? m with { Deployed = false } : m)] },
            ["rollOff"] = s with { RollOff = new RollOffResult(RollOffPurpose.Initiative, Seat.One) },
            ["rollOff.purpose"] = s with { RollOff = new RollOffResult(RollOffPurpose.DeploymentOrder, Seat.One) },
            ["rollOff.winner"] = s with { RollOff = new RollOffResult(RollOffPurpose.Initiative, Seat.Two) },
            ["mustEndActivation"] = s with { MustEndActivation = true },
        };
        var baseline = StateHash.Compute(s);

        foreach (var (name, variant) in variants)
        {
            Assert.NotEqual(baseline, StateHash.Compute(variant));
            Assert.True(variants.Values.Count(v => StateHash.Compute(v) == StateHash.Compute(variant)) == 1, name);
        }
    }

    // The canonical text and its hash are fixed on purpose: if either changes, StateHash.Version must be
    // bumped and every stored hash recorded under the old version is no longer comparable.
    // Re-pinned once, deliberately, for Version 2 (M4 adds positions, Blood Markers, the action ledger, the roll
    // in progress, Victory Points and the scenario geometry), and again for Version 3 (Slice B adds the Melee Weapon
    // to the profile lines, the retreat in progress and the new roll details), and for Version 4 (reducer hardening
    // adds the unused roll-off result, the forced end of an activation and the forced flee). The Version 4 opening
    // state, with those three additions taken out, still hashes to the Version 3 pin 3dbdc503...c69b. Version 5 (Glorious
    // Deeds, U96) adds deed lines only once a deed has something to record, so these two states change only in their
    // first line, trench-state=5.
    [Fact]
    [Trait("Rule", "Architecture: canonical state hash")]
    public void GoldenCanonicalTextForAFreshMatch()
    {
        var state = Reducer.Replay([new MatchCreated(Play.Setup(1, 1))]);

        Assert.Equal(GoldenFreshText, StateHash.Canonical(state).ReplaceLineEndings("\n"));
        Assert.Equal(GoldenFreshHash, StateHash.Compute(state));
    }

    [Fact]
    [Trait("Rule", "Architecture: canonical state hash")]
    public void GoldenHashAfterAScriptedOpening()
    {
        var play = new Play(Play.Setup(2, 2)).ToActivation(Seat.Two);
        play.Do(new ActivateModel(Seat.Two, Play.B(1)));

        Assert.Equal(GoldenOpeningHash, StateHash.Compute(play.State));
    }

    private const string GoldenFreshText = """
trench-state=5
rules=1.0.2
scenario=scenario-1
turnLimit=4
initiativeCount=ExcludeDownAndOutOfAction
moraleOrder=InitiativeHolderChooses
zones=north,south
phase=PreGame
turn=0
turnEnded=0
deploymentFirst=-
lastDeployer=-
initiativeHolder=-
firstActivator=-
lastActivator=-
activeModel=-
moraleFirst=-
pending=-
actions=0
movementHalved=0
scoredTurn=0
inProgress=-
retreat=-
outcome=-
rollOff=-
mustEndActivation=0
seat.One=zone=-,shaken=0,fled=0,checked=0,outcome=-,vp=0,mustFlee=0
seat.Two=zone=-,shaken=0,fled=0,checked=0,outcome=-,vp=0,mustFlee=0
model=A1,One,Standing,deployed=0,activated=0,at=-,markers=0
model=B1,Two,Standing,deployed=0,activated=0,at=-,markers=0
""" + "\n";
    private const string GoldenFreshHash = "1475f99d3542a7ad14baa98093ebb5cd8854e1fb349935d0f81b085e14572cb8";
    private const string GoldenOpeningHash = "73a91c713035320b0b74e0400ca534d2ae3f4f5a60b33f6f8c8f7c8b27f737c3";
}
