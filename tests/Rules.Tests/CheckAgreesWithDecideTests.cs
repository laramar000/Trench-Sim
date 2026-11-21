using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Events;
using Rules.Core.State;
using Sim;

namespace Rules.Tests;

// Proposal J, stage J2a: TurnSequence.Check, the read-only door the client's previews ask, gives exactly the answer
// Decide gives (the same rejection, or none when Decide accepts), for every Fight, Shoot, Charge and Activate command at
// every enemy, and the legal commands, in every state of simulated ranged and melee matches. It never rolls a die.
public class CheckAgreesWithDecideTests
{
    [Fact]
    public void CheckGivesDecidesAnswerWithoutRolling()
    {
        var (accepted, refused) = (0, 0);
        foreach (var path in new[] { Content.DefaultPath(), Content.MeleePath() })
        {
            var warbands = Content.Load(path);
            for (var seed = 0; seed < 6; seed++)
            {
                var one = warbands[seed % 2];
                var two = warbands[(seed + 1) % 2];
                var models = one.Models.Select((m, i) => new SetupModel(new ModelId($"a{i + 1}"), Seat.One, m.Profile))
                    .Concat(two.Models.Select((m, i) => new SetupModel(new ModelId($"b{i + 1}"), Seat.Two, m.Profile)));
                var state = Reducer.Apply(MatchState.None, new MatchCreated(Rules.Core.Scenarios.ClaimNoMansLand.CreateSetup(models)));
                var bot = new Bot(new Random(seed));
                var play = new SeededDice(seed * 31 + 5);
                // Between commands, as the server holds the match when a command arrives.
                for (var step = 0; step < 3000 && state.Phase != Phase.Ended; step++)
                {
                    foreach (var command in Commands(state).Concat(bot.Propose(state).Take(8)))
                    {
                        var dice = new SeededDice(seed);
                        var answer = TurnSequence.Check(state, command);
                        Assert.Equal(0, dice.Rolls);
                        var decided = TurnSequence.Decide(state, command, dice);
                        Assert.Equal(decided.Rejection, answer);
                        Assert.Equal(decided.IsAccepted, answer is null);
                        accepted += answer is null ? 1 : 0;
                        refused += answer is null ? 0 : 1;
                    }
                    var next = bot.Propose(state).Select(c => TurnSequence.Decide(state, c, play)).FirstOrDefault(r => r.IsAccepted);
                    Assert.NotNull(next);
                    state = next.State!;
                }
            }
        }
        Assert.True(accepted > 200 && refused > 2000, $"{accepted} accepted, {refused} refused");
    }

    private static IEnumerable<Command> Commands(MatchState state)
    {
        foreach (var legal in TurnSequence.LegalCommands(state))
        {
            yield return legal;
        }
        if (state.ActiveModel is not { } active || state.FindModel(active) is not { } actor)
        {
            yield break;
        }
        foreach (var other in state.Models)
        {
            yield return new FightModel(actor.Seat, active, other.Id);
            yield return new Shoot(actor.Seat, active, other.Id);
            yield return new ChargeModel(actor.Seat, active, other.Id);
            yield return new FightModel(actor.Seat.Opponent(), active, other.Id);
        }
    }
}
