using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.Scenarios;
using Rules.Core.State;

namespace Sim;

public sealed record SimResult(
    int Seed,
    bool Terminal,
    string Status,
    MatchOutcome? Outcome,
    int Turns,
    int VictoryPointsOne,
    int VictoryPointsTwo,
    int Commands,
    int Rejections,
    int Events,
    int Shots,
    int Hits,
    int OutOfAction,
    int Dashes,
    string FinalHash,
    string WarbandOne,
    string WarbandTwo,
    IReadOnlyList<GameEvent> Log)
{
    public string Describe()
    {
        var outcome = Outcome is { } o ? $"{(o.Winner is { } w ? $"winner {w}" : "draw")} by {o.Reason}" : "no outcome";
        return $"seed {Seed,3}: {Status,-9} {outcome,-42} turns {Turns} VP {VictoryPointsOne}-{VictoryPointsTwo} " +
               $"commands {Commands} rejected {Rejections} events {Events} shots {Shots} hits {Hits} OoA {OutOfAction} dashes {Dashes} " +
               $"[{WarbandOne} vs {WarbandTwo}] hash {FinalHash[..12]}";
    }
}

// Plays one whole Claim No Man's Land match between two bots and checks it as it goes. A match that does not end
// within the command limit, or leaves the bot with nothing the rules will accept, is a failure, never a result.
public static class Runner
{
    public const int DefaultMaxCommands = 5000;

    // Commands naming a model that does not exist (or a phase that has passed) are never legal, whatever the state.
    private static Command Junk(int which, MatchState state)
    {
        var nobody = new ModelId("zz9");
        var at = new Point2(Length.FromInches(3), Length.FromInches(3));
        return which switch
        {
            0 => new MoveModel(Seat.One, nobody, [at]),
            1 => new DashModel(Seat.Two, nobody, [at]),
            2 => new Shoot(Seat.One, nobody, nobody),
            3 => new PlaceModel(Seat.Two, nobody, at),
            4 => new ActivateModel(Seat.One, nobody),
            _ => state.Phase == Phase.PreGame ? new MoveModel(Seat.One, nobody, []) : new StartMatch(),
        };
    }

    public static SimResult Run(int seed, IReadOnlyList<Warband> warbands, int maxCommands = DefaultMaxCommands)
    {
        // Alternate which warband sits in which seat so both orders are exercised.
        var one = warbands[seed % 2 == 0 ? 0 : 1];
        var two = warbands[seed % 2 == 0 ? 1 : 0];
        var models = one.Models.Select((m, i) => new SetupModel(new ModelId($"a{i + 1}"), Seat.One, m.Profile))
            .Concat(two.Models.Select((m, i) => new SetupModel(new ModelId($"b{i + 1}"), Seat.Two, m.Profile)));
        var setup = ClaimNoMansLand.CreateSetup(models);

        var created = new MatchCreated(setup);
        var state = Reducer.Apply(MatchState.None, created);
        var log = new List<GameEvent> { created };
        var bot = new Bot(new Random(seed));
        var junk = new Random(seed ^ 0x5eed);
        var dice = new SeededDice(seed * 7919 + 1);
        var activations = new Dictionary<(int Turn, ModelId Model), int>();
        int commands = 0, rejections = 0;
        string? failure = null;

        while (state.Phase != Phase.Ended && failure is null)
        {
            if (commands >= maxCommands)
            {
                failure = $"step-limit: {maxCommands} commands without the match ending";
                break;
            }

            // Now and then offer the rules something that can never be legal: it must change nothing and roll nothing.
            if (junk.Next(8) == 0)
            {
                var probe = Junk(junk.Next(6), state);
                var rollsBefore = dice.Rolls;
                var hashBefore = StateHash.Compute(state);
                var refused = TurnSequence.Decide(state, probe, dice);
                commands++;
                rejections++;
                if (refused.IsAccepted || refused.Events.Length != 0 || dice.Rolls != rollsBefore || hashBefore != StateHash.Compute(state))
                {
                    failure = $"junk {probe.GetType().Name} was accepted, rolled dice or changed the state";
                    break;
                }
            }

            var accepted = false;
            foreach (var command in bot.Propose(state))
            {
                var rollsBefore = dice.Rolls;
                var hashBefore = StateHash.Compute(state);
                var result = TurnSequence.Decide(state, command, dice);
                commands++;
                if (!result.IsAccepted)
                {
                    rejections++;
                    if (dice.Rolls != rollsBefore || result.Events.Length != 0 || hashBefore != StateHash.Compute(state))
                    {
                        failure = $"a rejected {command.GetType().Name} rolled dice or changed the state";
                        break;
                    }
                    continue;
                }

                // Apply the events one at a time ourselves and compare with what Decide reported.
                var next = state;
                foreach (var e in result.Events)
                {
                    next = Reducer.Apply(next, e);
                    log.Add(e);
                }
                if (StateHash.Compute(next) != StateHash.Compute(result.State!))
                {
                    failure = "Decide's state differs from applying its events";
                    break;
                }
                foreach (var e in result.Events.OfType<ActivationStarted>())
                {
                    var key = (result.State!.Turn, e.Model);
                    activations[key] = activations.GetValueOrDefault(key) + 1;
                    if (activations[key] > 1)
                    {
                        failure = $"{e.Model} activated twice in turn {key.Turn}";
                    }
                }
                var broken = Invariants.Check(state, next);
                if (broken is not null)
                {
                    failure = $"invariant: {broken}";
                }
                state = next;
                accepted = true;
                break;
            }

            if (failure is null && !accepted)
            {
                failure = "stalled: the rules accepted none of the bot's commands";
            }
        }

        var terminal = failure is null && state.Phase == Phase.Ended && state.Outcome is not null;
        if (terminal)
        {
            var replayed = Reducer.Replay(log);
            if (StateHash.Compute(replayed) != StateHash.Compute(state))
            {
                failure = "replaying the log gives a different state";
                terminal = false;
            }
        }

        return new SimResult(
            seed,
            terminal,
            terminal ? "terminal" : failure ?? "unfinished",
            state.Outcome,
            state.Turn,
            state.One.VictoryPoints,
            state.Two.VictoryPoints,
            commands,
            rejections,
            log.Count,
            log.OfType<RangedAttackDeclared>().Count(),
            log.OfType<RangedSuccessRolled>().Count(r => r.Check.Outcome != Rules.Core.Dice.SuccessOutcome.Failure),
            state.Models.Count(m => m.Status == ModelStatus.OutOfAction),
            log.OfType<DashDeclared>().Count(),
            StateHash.Compute(state),
            one.Name,
            two.Name,
            log);
    }
}
