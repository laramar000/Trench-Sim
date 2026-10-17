using Client.Core;
using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.State;

namespace Client.Tests;

// Proposal G, step G3: the one line above the battlefield (it replaces the QoL stage C status strip). It says only what
// the server's state, the log and the battlefield's scene hold, and both seats' lines agree (each from its own side).
public class TopHudTests
{
    private static List<MatchState> States => PreviewAgreementTests.States.Value;
    private static readonly ContentNames Names = ContentNames.None;

    private static string Hud(MatchState state, Seat? seat, IReadOnlyList<LogLine>? log = null, bool waiting = false) =>
        TopHud.Of(MatchView.Of(state, seat, Names), BoardScene.Of(state, seat, Names), log ?? [], seat, waiting);

    [Fact]
    public void TheLineSaysTheSeatWhoseMoveTheTurnPhaseVictoryPointsAndWhoCanStillActivate()
    {
        var counted = 0;
        foreach (var state in States.Where(s => s.Setup?.Scenario is not null && s.Turn > 0).Take(4000))
        {
            foreach (var seat in new[] { Seat.One, Seat.Two })
            {
                var view = MatchView.Of(state, seat, Names);
                var line = Hud(state, seat);
                var parts = line.Split(" · ");
                Assert.Equal($"[{Words.SeatLetter(seat)}] {Words.Seat(seat).ToUpperInvariant()}", parts[0]);
                // Whose move in words, not by colour alone.
                Assert.Equal(view.YourMove ? "YOUR MOVE" : view.WhoseMove.StartsWith("Opponent's move", StringComparison.Ordinal) ? "OPPONENT'S MOVE" : view.WhoseMove.TrimEnd('.'), parts[1]);
                Assert.Equal(view.Turn, parts[2]);
                Assert.Equal(view.Phase, parts[3]);
                Assert.Contains($"VP you {state.SeatFor(seat).VictoryPoints}, opponent {state.SeatFor(seat.Opponent()).VictoryPoints}", line);
                Assert.DoesNotContain("selected", line);
                if (state.Phase != Phase.Activation)
                {
                    Assert.DoesNotContain("to activate", line);
                    continue;
                }
                bool CanActivate(ModelState m) => m.Deployed && m.Status != ModelStatus.OutOfAction && !m.ActivatedThisTurn && m.Id != state.ActiveModel;
                var mine = state.Models.Count(m => m.Seat == seat && m.Position is not null && CanActivate(m));
                var theirs = state.Models.Count(m => m.Seat != seat && m.Position is not null && CanActivate(m));
                Assert.Contains($"to activate: you {mine}, opponent {theirs}", line);
                // For the seat to activate, "to activate" is the rules' own list of activations.
                if (state.ActiveModel is null && state.Pending is null && TurnSequence.NextActivator(state) == seat)
                {
                    Assert.Equal("YOUR MOVE", parts[1]);
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
            string Neutral(Seat seat) => string.Join("|", Hud(state, seat)
                .Replace($"you {state.SeatFor(seat).VictoryPoints}, opponent {state.SeatFor(seat.Opponent()).VictoryPoints}", "VPS", StringComparison.Ordinal)
                .Split(" · ").Skip(2).Where(p => !p.StartsWith("to activate", StringComparison.Ordinal)));
            Assert.Equal(Neutral(Seat.One), Neutral(Seat.Two));
        }
    }

    [Fact]
    public void AWatcherTheLastScoringAndAWaitingCommandAreShown()
    {
        var state = States.First(s => s.Phase == Phase.Activation && s.Pending is null && s.Setup?.Scenario is not null);
        Assert.StartsWith("WATCHING · ", Hud(state, null));
        Assert.DoesNotContain(TopHud.Waiting, Hud(state, Seat.One));
        Assert.EndsWith(TopHud.Waiting, Hud(state, Seat.One, waiting: true));
        // The objectives of the last scoring come from the log's own line.
        var log = new List<LogLine> { new(40, "Turn 1 scoring: obj-1 to One.", null), new(41, "--- Turn 2 ---", null) };
        Assert.Contains("objectives: obj-1 to One", Hud(state, Seat.One, log));
    }
}
