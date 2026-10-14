using System.Collections.Immutable;
using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Events;
using Rules.Core.State;

namespace Rules.Tests;

// Drives a match for the tests: every accepted command must use exactly the dice it was given,
// so a test always knows which die went where.
public sealed class Play
{
    public Play(MatchSetup setup)
    {
        var created = new MatchCreated(setup);
        State = Reducer.Apply(MatchState.None, created);
        Log.Add(created);
    }

    public MatchState State { get; set; }
    public List<GameEvent> Log { get; } = [];

    // Seat One's models are A1..An, seat Two's are B1..Bn. Zones are "north" and "south".
    public static MatchSetup Setup(int one, int two, RulesProfile? profile = null, int turnLimit = 4) =>
        new(
            "1.0.2",
            "scenario-1",
            turnLimit,
            profile ?? RulesProfile.Default,
            ["north", "south"],
            Enumerable.Range(1, one).Select(i => new SetupModel(new ModelId($"A{i}"), Seat.One))
                .Concat(Enumerable.Range(1, two).Select(i => new SetupModel(new ModelId($"B{i}"), Seat.Two))));

    public static ModelId A(int n) => new($"A{n}");
    public static ModelId B(int n) => new($"B{n}");

    public DecideResult Do(Command command, params int[] dice)
    {
        var fixedDice = new FixedDice(dice);
        var result = TurnSequence.Decide(State, command, fixedDice);
        Assert.True(result.IsAccepted, result.Rejection?.Text);
        Assert.Equal(0, fixedDice.Remaining);
        State = result.State!;
        Log.AddRange(result.Events);
        return result;
    }

    // Like Do, but a rejection is returned instead of failing the test.
    public DecideResult Try(Command command, params int[] dice)
    {
        var result = TurnSequence.Decide(State, command, new FixedDice(dice));
        if (result.IsAccepted)
        {
            State = result.State!;
            Log.AddRange(result.Events);
        }
        return result;
    }

    // Deploys everything. Dice go to the last deployment only, because that is when the turn-one
    // Initiative roll-off happens (when both sides have the same number of models).
    public void DeployAll(params int[] lastDice)
    {
        while (State.Phase == Phase.Deployment)
        {
            var next = TurnSequence.LegalCommands(State)[0];
            var isLast = State.Models.Count(m => !m.Deployed) == 1;
            Do(next, isLast ? lastDice : []);
        }
    }

    // Start, choose a zone and deploy everything, then let the Initiative holder choose who activates first.
    // With equal sizes seat One wins both roll-offs (dice 6 vs 1 and 5 vs 2), so seat One deploys first and
    // holds the Initiative. With unequal sizes no roll-off is needed and the smaller side holds it.
    public Play ToActivation(Seat first = Seat.One)
    {
        var one = State.Models.Count(m => m.Seat == Seat.One);
        var two = State.Models.Count(m => m.Seat == Seat.Two);
        Do(new StartMatch(), 6, 1);
        Do(new ChooseDeploymentZone(Seat.One, "north"), one == two ? [5, 2] : []);
        DeployAll(one == two ? [6, 1] : []);
        var holder = one == two ? Seat.One : one < two ? Seat.One : Seat.Two;
        Do(new ChooseFirstActivation(holder, first));
        return this;
    }

    // Activates and ends models one after another until the Activation Phase is over. The dice go to the
    // last end-of-activation, which is what triggers the Morale Phase and anything after it.
    public void RunActivations(params int[] lastDice)
    {
        while (State.Phase == Phase.Activation)
        {
            var next = TurnSequence.LegalCommands(State)[0];
            var remaining = State.Models.Count(m => m.Status != ModelStatus.OutOfAction && !m.ActivatedThisTurn);
            var isLast = next is EndActivation && remaining == 0;
            Do(next, isLast ? lastDice : []);
        }
    }

    public void SetStatus(ModelId id, ModelStatus status) =>
        State = State with { Models = State.Models.Select(m => m.Id == id ? m with { Status = status } : m).ToImmutableArray() };

    public void SetStatus(string id, ModelStatus status) => SetStatus(new ModelId(id), status);
}
