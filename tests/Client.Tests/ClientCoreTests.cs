using Client.Core;
using Match.Protocol;
using Rules.Core;
using Rules.Core.Events;
using Rules.Core.State;
using Sim;

namespace Client.Tests;

// M7.3: Client.Core builds what the screen shows from the server's state alone. These tests run whole simulated
// matches (the simulator rolls the dice, as the server would) and check the view, the event text, the names and the
// hints against the state and against the simulator's own code. Tests may use Reducer and Sim; Client.Core may not.
public class ClientCoreTests
{
    private static readonly Lazy<List<(string Scenario, List<GameEvent> Log)>> Matches = new(() =>
    {
        var all = new List<(string, List<GameEvent>)>();
        foreach (var (scenario, file) in new[] { ("ranged", "test-warbands.json"), ("melee", "melee-warbands.json") })
        {
            var warbands = Content.Load(Path.Combine(Repository.Root, "content", file));
            for (var seed = 0; seed < 40; seed++)
            {
                var result = Runner.Run(seed, warbands);
                Assert.True(result.Terminal, result.Status);
                all.Add((scenario, result.Log.ToList()));
            }
        }
        return all;
    });

    // Every state of every match, with the event that follows it (null after the last).
    private static IEnumerable<(string Scenario, MatchState State, GameEvent? Next)> States()
    {
        foreach (var (scenario, log) in Matches.Value)
        {
            var state = MatchState.None;
            for (var i = 0; i < log.Count; i++)
            {
                state = Reducer.Apply(state, log[i]);
                yield return (scenario, state, i + 1 < log.Count ? log[i + 1] : null);
            }
        }
    }

    [Fact]
    public void EventTextMatchesTheSimulatorsNarratorOnEveryEvent()
    {
        var count = 0;
        foreach (var e in Matches.Value.SelectMany(m => m.Log))
        {
            Assert.Equal(Narrator.Describe(e), EventText.Describe(e));
            count++;
        }
        Assert.True(count > 10_000, $"{count} events");
    }

    // Every event the rules can publish has its own line, including those the narrator leaves out.
    [Fact]
    public void EveryEventTypeHasItsOwnLine()
    {
        var seen = Matches.Value.SelectMany(m => m.Log).GroupBy(e => e.GetType()).ToDictionary(g => g.Key, g => g.First());
        // The bots never decline free attacks, so that event is built here.
        seen.TryAdd(typeof(FreeFightsDeclined), new FreeFightsDeclined(Seat.Two));
        var all = typeof(GameEvent).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(GameEvent)) && !t.IsAbstract).ToList();
        Assert.True(all.Count >= 30, $"{all.Count} event types");
        Assert.Empty(all.Where(t => !seen.ContainsKey(t)).Select(t => t.Name));
        foreach (var (type, e) in seen)
        {
            var line = EventText.Line(e);
            Assert.False(string.IsNullOrWhiteSpace(line));
            Assert.NotEqual(type.Name + ".", line);
        }
    }

    [Fact]
    public void EveryRollShowsItsDiceAndNothingElseClaimsDice()
    {
        var rolls = new HashSet<Type>
        {
            typeof(RollOffResolved), typeof(DashRolled), typeof(RangedSuccessRolled), typeof(MeleeSuccessRolled),
            typeof(MoraleCheckRolled), typeof(InjuryRolled), typeof(ChargeRolled), typeof(ShootingTargetRolled),
        };
        foreach (var e in Matches.Value.SelectMany(m => m.Log))
        {
            var dice = EventText.Dice(e);
            Assert.Equal(rolls.Contains(e.GetType()), dice is not null);
            var faces = e switch
            {
                RangedSuccessRolled r => r.Check.Dice.Rolled,
                MeleeSuccessRolled m => m.Check.Dice.Rolled,
                InjuryRolled i => i.Injury.Dice.Rolled,
                MoraleCheckRolled m => m.Check.Dice.Rolled,
                DashRolled d => d.Check.Dice.Rolled,
                _ => null,
            };
            if (faces is not null)
            {
                Assert.Contains($"rolled {string.Join(", ", faces)};", dice);
            }
        }
    }

    [Fact]
    public void ContentIsReadExactlyAsTheSimulatorReadsIt()
    {
        foreach (var file in new[] { "test-warbands.json", "melee-warbands.json" })
        {
            var ours = ContentNames.Parse(ContentNames.Read(file));
            var sims = Content.Load(Path.Combine(Repository.Root, "content", file));
            Assert.Equal(sims.Select(w => (w.Id, w.Name)), ours.Select(w => (w.Id, w.Name)));
            Assert.Equal(sims.SelectMany(w => w.Models).Select(m => (m.Name, m.Profile)), ours.SelectMany(w => w.Models).Select(m => (m.Name, m.Profile)));
            // The embedded copy is the repository's file.
            Assert.Equal(File.ReadAllText(Path.Combine(Repository.Root, "content", file)), ContentNames.Read(file));
        }
    }

    [Fact]
    public void NamesComeFromContentAndFallBackToIdsWhenTheyDoNotMatch()
    {
        var setups = ScenarioSetups.FromFolder(Path.Combine(Repository.Root, "content"));
        foreach (var (scenario, setup) in setups)
        {
            var names = ContentNames.For(scenario, setup);
            Assert.Null(names.Problem);
            var warbands = Content.Load(Path.Combine(Repository.Root, "content", ContentNames.FileFor(scenario)!));
            Assert.Equal($"{warbands[0].Models[0].Name} (a1)", names.Of(new ModelId("a1")));
            // The last model of Seat Two: the warband's last, or for a small test roster the last of its first models.
            var two = setup.Models.Count(m => m.Seat == Seat.Two);
            Assert.Equal($"{warbands[1].Models[two - 1].Name} (b{two})", names.Of(new ModelId($"b{two}")));
            Assert.Equal(warbands[1].Name, names.WarbandOf(Seat.Two));
        }

        // The server's setup does not match this client's content for that scenario: ids, and the reason.
        var wrong = ContentNames.For("ranged", setups["melee"]);
        Assert.NotNull(wrong.Problem);
        Assert.Contains("does not match", wrong.Problem);
        Assert.Equal("a1", wrong.Of(new ModelId("a1")));
        Assert.Equal("Seat One", wrong.WarbandOf(Seat.One));

        var unknown = ContentNames.For("no-such-scenario", setups["ranged"]);
        Assert.Contains("no content", unknown.Problem);
        Assert.Equal("b2", unknown.Of(new ModelId("b2")));
    }

    // Hints are exactly the rules' legal commands for the player's own seat, with a label, and never the other seat's.
    [Fact]
    public void HintsAreExactlyTheLegalCommandsOfTheSeat()
    {
        var any = false;
        foreach (var (scenario, state, _) in States().Where((_, i) => i % 3 == 0))
        {
            var names = ContentNames.For(scenario, state.Setup);
            foreach (var seat in new[] { Seat.One, Seat.Two })
            {
                var hints = ActionHints.For(state, seat, names);
                var legal = TurnSequence.LegalCommands(state).Where(c => CommandSeats.Of(c) == seat).ToList();
                Assert.Equal(legal, hints.Select(h => h.Command));
                Assert.All(hints, h => Assert.False(string.IsNullOrWhiteSpace(h.Label)));
                Assert.All(hints, h => Assert.DoesNotContain(nameof(Rules.Core.Commands.Command), h.Label));
                any |= hints.Count > 0;
            }
        }
        Assert.True(any);
    }

    // The view shows the server's state and nothing else, and always says whose move it is in words.
    [Fact]
    public void TheViewAgreesWithTheStateThroughWholeMatches()
    {
        foreach (var (scenario, state, _) in States())
        {
            var names = ContentNames.For(scenario, state.Setup);
            var view = MatchView.Of(state, Seat.One, names);
            Assert.Equal(state.Models.Length, view.Models.Count);
            foreach (var (model, row) in state.Models.Zip(view.Models))
            {
                Assert.Equal(model.Id, row.Id);
                Assert.Equal(model.Status, row.Status);
                Assert.Equal(model.BloodMarkers, row.BloodMarkers);
                Assert.Equal(state.ActiveModel == model.Id, row.Active);
                // Seat and status are always in the text, never only in a colour.
                Assert.StartsWith($"[{(model.Seat == Seat.One ? "A" : "B")}] ", row.Text);
                Assert.Contains(Words.Status(model.Status), row.Text);
            }
            Assert.Equal(state.One.VictoryPoints, view.Seats[0].VictoryPoints);
            Assert.Equal(state.Two.VictoryPoints, view.Seats[1].VictoryPoints);
            Assert.True(view.Seats[0].IsYou);
            Assert.False(string.IsNullOrWhiteSpace(view.Status));
            if (state.Phase is not (Phase.None or Phase.Ended))
            {
                Assert.False(string.IsNullOrWhiteSpace(view.WhoseMove));
            }
            var hints = ActionHints.For(state, Seat.One, names);
            if (hints.Count > 0)
            {
                Assert.True(view.YourMove, view.WhoseMove);
            }
            if (view.YourMove)
            {
                Assert.True(hints.Count > 0 || ActionHints.NotYetOffered(state, Seat.One) is not null, view.Status);
            }
            Assert.Equal(state.Outcome is not null, view.Outcome is not null);
            Assert.Equal(state.Pending is not null, view.Pending is not null);
            Assert.Equal(state.ActiveModel is not null, view.Activation is not null);
            Assert.Equal(state.InProgress is not null, view.Roll is not null);
        }
    }

    // The display-only copy of the deployment order agrees with who actually deploys next in real matches.
    [Fact]
    public void NextDeployerAgreesWithTheDeploymentOrder()
    {
        var checkedCount = 0;
        foreach (var (_, state, next) in States())
        {
            if (next is ModelDeployed deployed && state.Pending is null)
            {
                Assert.Equal(deployed.Seat, MatchView.NextDeployer(state));
                checkedCount++;
            }
        }
        Assert.True(checkedCount > 100, $"{checkedCount} deployments");
    }

    [Fact]
    public void TheOutcomeIsToldFromThePlayersSide()
    {
        var won = new MatchOutcome(Seat.One, OutcomeReason.VictoryPoints);
        Assert.StartsWith("You won", Words.Outcome(won, Seat.One));
        Assert.StartsWith("You lost", Words.Outcome(won, Seat.Two));
        Assert.StartsWith("Seat One won", Words.Outcome(won, null));
        Assert.StartsWith("Draw", Words.Outcome(new MatchOutcome(null, OutcomeReason.VictoryPoints), Seat.Two));
        Assert.NotEqual(Words.SeatLetter(Seat.One), Words.SeatLetter(Seat.Two));
    }

    [Fact]
    public void TheRulesGapsArePublishedForPlaytesters()
    {
        Assert.True(RulesGaps.All.Count >= 8);
        Assert.Contains(RulesGaps.All, g => g.Gap.Contains("Glorious Deeds", StringComparison.Ordinal));
        Assert.Contains(RulesGaps.All, g => g.Gap.Contains("Bloodbath", StringComparison.Ordinal));
        Assert.StartsWith(RulesGaps.Title, RulesGaps.Text);
        Assert.All(RulesGaps.All, g => Assert.Contains(g.Gap, RulesGaps.Text));
    }
}
