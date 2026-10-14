using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Events;
using Rules.Core.State;

namespace Rules.Tests;

// Seeded random play: a supplement to the rule-by-rule tests, not a replacement. It proves that following
// LegalCommands never wedges the machine, that illegal commands are refused cleanly, and that a log
// always replays to the same state.
public class RandomPlayTests
{
    private sealed record Run(MatchState State, List<GameEvent> Log, int Commands, int Rejections);

    private static Run Play(int seed, int one, int two, bool injectInjuries)
    {
        var rng = new Random(seed);
        var dice = new SeededDice(seed * 7919 + 1);
        var setup = Rules.Tests.Play.Setup(one, two, turnLimit: 4);
        var created = new MatchCreated(setup);
        var state = Reducer.Apply(MatchState.None, created);
        var log = new List<GameEvent> { created };
        var rejections = 0;
        var commands = 0;
        var activationsThisTurn = new Dictionary<(int, ModelId), int>();

        while (state.Phase != Phase.Ended && commands < 2000)
        {
            // Now and then try something that is probably illegal: it must change nothing and roll nothing.
            if (rng.Next(4) == 0)
            {
                var junk = RandomCommand(rng, state);
                var before = StateHash.Compute(state);
                var rolls = dice.Rolls;
                var attempt = TurnSequence.Decide(state, junk, dice);
                if (!attempt.IsAccepted)
                {
                    rejections++;
                    Assert.Equal(before, StateHash.Compute(state));
                    Assert.Equal(rolls, dice.Rolls);
                    Assert.Empty(attempt.Events);
                    continue;
                }
                state = Accept(attempt, log, state, activationsThisTurn);
                commands++;
                continue;
            }

            if (injectInjuries && state.Phase == Phase.Activation && state.ActiveModel is null && rng.Next(6) == 0)
            {
                // Stands in for the injuries of a later milestone: a test-only status change, so no replay check.
                var victim = state.Models[rng.Next(state.Models.Length)];
                state = state with { Models = [.. state.Models.Select(m => m.Id == victim.Id ? m with { Status = rng.Next(2) == 0 ? ModelStatus.Down : ModelStatus.OutOfAction } : m)] };
            }

            var legal = TurnSequence.LegalCommands(state);
            Assert.NotEmpty(legal);
            var result = TurnSequence.Decide(state, legal[rng.Next(legal.Count)], dice);
            Assert.True(result.IsAccepted, result.Rejection?.Text);
            state = Accept(result, log, state, activationsThisTurn);
            commands++;
        }
        return new Run(state, log, commands, rejections);
    }

    private static MatchState Accept(DecideResult result, List<GameEvent> log, MatchState before, Dictionary<(int, ModelId), int> activations)
    {
        Assert.Equal(StateHash.Compute(result.Events.Aggregate(before, Reducer.Apply)), StateHash.Compute(result.State!));
        log.AddRange(result.Events);
        foreach (var e in result.Events.OfType<ActivationStarted>())
        {
            var key = (result.State!.Turn, e.Model);
            activations[key] = activations.GetValueOrDefault(key) + 1;
            Assert.True(activations[key] <= 1, $"{e.Model.Value} activated twice in turn {key.Item1}");
        }
        return result.State!;
    }

    private static Command RandomCommand(Random rng, MatchState state)
    {
        var seat = rng.Next(2) == 0 ? Seat.One : Seat.Two;
        var model = state.Models.Length > 0 ? state.Models[rng.Next(state.Models.Length)].Id : new ModelId("none");
        return rng.Next(8) switch
        {
            0 => new StartMatch(),
            1 => new ChooseDeploymentZone(seat, rng.Next(2) == 0 ? "north" : "nowhere"),
            2 => new DeployModel(seat, model),
            3 => new ChooseFirstActivation(seat, seat),
            4 => new ActivateModel(seat, model),
            5 => new EndActivation(seat),
            6 => new ResolveFailedMorale(seat, MoraleFailureChoice.BecomeShaken),
            _ => new ChooseMoraleOrder(seat, seat),
        };
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(2, 2, 2)]
    [InlineData(3, 3, 3)]
    [InlineData(4, 3, 5)]
    [InlineData(5, 6, 2)]
    [InlineData(6, 4, 4)]
    [InlineData(7, 1, 5)]
    [InlineData(8, 5, 5)]
    [InlineData(9, 3, 2)]
    [InlineData(10, 2, 6)]
    public void LegalPlayAlwaysReachesTheEndAndReplaysExactly(int seed, int one, int two)
    {
        var run = Play(seed, one, two, injectInjuries: false);

        Assert.Equal(Phase.Ended, run.State.Phase);
        Assert.NotNull(run.State.Outcome);
        Assert.Equal(StateHash.Compute(run.State), StateHash.Compute(Reducer.Replay(run.Log)));
    }

    [Theory]
    [InlineData(21)]
    [InlineData(22)]
    [InlineData(23)]
    [InlineData(24)]
    [InlineData(25)]
    [InlineData(26)]
    public void PlayWithCasualtiesAlwaysReachesTheEnd(int seed)
    {
        var run = Play(seed, 4, 4, injectInjuries: true);

        Assert.Equal(Phase.Ended, run.State.Phase);
    }

    [Fact]
    public void TheSameSeedGivesTheSameMatch()
    {
        var a = Play(99, 4, 5, injectInjuries: false);
        var b = Play(99, 4, 5, injectInjuries: false);

        Assert.Equal(a.Log.Select(e => e.GetType().Name), b.Log.Select(e => e.GetType().Name));
        Assert.Equal(StateHash.Compute(a.State), StateHash.Compute(b.State));
    }

    [Fact]
    public void RandomPlayExercisesRejectionsAndMorale()
    {
        var runs = Enumerable.Range(21, 6).Select(s => Play(s, 4, 4, injectInjuries: true)).ToList();

        Assert.Contains(runs, r => r.Rejections > 0);
        Assert.Contains(runs, r => r.Log.OfType<MoraleCheckRolled>().Any());
    }
}
