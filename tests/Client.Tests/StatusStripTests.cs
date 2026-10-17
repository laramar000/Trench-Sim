using Client.Core;
using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.State;

namespace Client.Tests;

// QoL stage C: the status line over the battlefield says only what the server's state, the log and the battlefield's
// own selection hold, and both seats' lines agree (each from its own side).
public class StatusStripTests
{
    private static List<MatchState> States => PreviewAgreementTests.States.Value;
    private static readonly ContentNames Names = ContentNames.None;

    private static string Strip(MatchState state, Seat seat, IReadOnlyList<LogLine>? log = null, ModelShape? selected = null, ModelId? chosen = null, bool waiting = false) =>
        StatusStrip.Of(MatchView.Of(state, seat, Names), BoardScene.Of(state, seat, Names), log ?? [], seat, selected, chosen, waiting);

    [Fact]
    public void TheLineSaysTheTurnPhaseVictoryPointsAndWhoCanStillActivate()
    {
        var counted = 0;
        foreach (var state in States.Where(s => s.Setup?.Scenario is not null && s.Turn > 0).Take(4000))
        {
            foreach (var seat in new[] { Seat.One, Seat.Two })
            {
                var view = MatchView.Of(state, seat, Names);
                var line = Strip(state, seat);
                Assert.StartsWith($"{view.Turn} · {view.Phase} · ", line);
                Assert.Contains($"VP you {state.SeatFor(seat).VictoryPoints}, opponent {state.SeatFor(seat.Opponent()).VictoryPoints}", line);
                if (state.Phase != Phase.Activation)
                {
                    Assert.DoesNotContain("can still activate", line);
                    continue;
                }
                bool CanActivate(ModelState m) => m.Deployed && m.Status != ModelStatus.OutOfAction && !m.ActivatedThisTurn && m.Id != state.ActiveModel;
                var mine = state.Models.Count(m => m.Seat == seat && m.Position is not null && CanActivate(m));
                var theirs = state.Models.Count(m => m.Seat != seat && m.Position is not null && CanActivate(m));
                Assert.Contains($"can still activate: you {mine}, opponent {theirs}", line);
                // For the seat to activate, "can still activate" is the rules' own list of activations.
                if (state.ActiveModel is null && state.Pending is null && TurnSequence.NextActivator(state) == seat)
                {
                    Assert.Equal(TurnSequence.LegalCommands(state).OfType<ActivateModel>().Count(), mine);
                    counted++;
                }
            }
        }
        Assert.True(counted > 300, $"only {counted} states");
    }

    [Fact]
    public void BothSeatsAgree()
    {
        foreach (var state in States.Where(s => s.Setup?.Scenario is not null && s.Turn > 0).Take(3000))
        {
            var one = Strip(state, Seat.One);
            var two = Strip(state, Seat.Two);
            string Neutral(string line, Seat seat) => line
                .Replace($"you {state.SeatFor(seat).VictoryPoints}, opponent {state.SeatFor(seat.Opponent()).VictoryPoints}", "VPS", StringComparison.Ordinal)
                .Split(" · ").Where(p => !p.StartsWith("can still", StringComparison.Ordinal) && !p.Contains("move", StringComparison.Ordinal)).Aggregate((a, b) => a + "|" + b);
            Assert.Equal(Neutral(one, Seat.One), Neutral(two, Seat.Two));
        }
    }

    [Fact]
    public void TheSelectedModelsStateAndAWaitingCommandAreShown()
    {
        var state = States.First(s => s.Phase == Phase.Activation && s.ActiveModel is null && s.Pending is null && s.Setup?.Scenario is not null
            && TurnSequence.NextActivator(s) is { } next && s.Models.Any(m => m.Seat == next && m.ActivatedThisTurn));
        var seat = TurnSequence.NextActivator(state)!.Value;
        var scene = BoardScene.Of(state, seat, Names)!;
        var eligible = scene.Models.First(m => m.Yours && m.Eligible);
        var done = scene.Models.First(m => m.Yours && m.Activated);
        Assert.Contains($"selected {eligible.Id.Value}: can activate", Strip(state, seat, selected: eligible));
        Assert.Contains($"selected {eligible.Id.Value}: chosen, not activated yet", Strip(state, seat, selected: eligible, chosen: eligible.Id));
        Assert.Contains($"selected {done.Id.Value}: activated this turn", Strip(state, seat, selected: done));
        Assert.DoesNotContain("waiting for the server", Strip(state, seat));
        Assert.EndsWith("command sent, waiting for the server", Strip(state, seat, waiting: true));
        // The last scoring comes from the log's own line.
        var log = new List<LogLine> { new(40, "Turn 1 scoring: obj-1 to One.", null), new(41, "--- Turn 2 ---", null) };
        Assert.Contains("last scoring: obj-1 to One", Strip(state, seat, log));
    }
}
