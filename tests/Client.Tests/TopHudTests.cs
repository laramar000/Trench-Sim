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

    // Stage K4: while you place your models the line says where, from the battlefield's own placing (Interaction.Prompt).
    [Fact]
    public void WhilePlacingTheLineSaysWhichModelAndWhere()
    {
        var state = States.First(s => s.Phase == Phase.Deployment && s.Pending is null && s.Setup?.Scenario is not null && MatchView.NextDeployer(s) is not null);
        var seat = MatchView.NextDeployer(state)!.Value;
        var interaction = new Interaction();
        interaction.Update(state, seat);
        var prompt = interaction.Prompt;
        Assert.NotNull(prompt);
        var parts = TopHud.Of(MatchView.Of(state, seat, Names), BoardScene.Of(state, seat, Names), [], seat, false, prompt).Split(" · ");
        Assert.Equal(prompt, parts[parts.ToList().IndexOf(MatchView.Of(state, seat, Names).Phase) + 1]);
        Assert.DoesNotContain(" · Place ", Hud(state, seat));
    }

    [Fact]
    public void TheLineSaysTheSeatWhoseMoveTheTurnPhaseAndVictoryPointsAndNoCountOfUnitsLeft()
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
                // Stage U2: each seat's VP by seat letter, the same in both windows.
                Assert.Contains($"VP A {state.SeatFor(Seat.One).VictoryPoints} – B {state.SeatFor(Seat.Two).VictoryPoints}", parts);
                // Stage K3: the activating model and what it has done, exactly while one activates.
                if (state.ActiveModel is { } active)
                {
                    Assert.Equal($"activating {Names.Of(active)}: done {Words.Actions(state.Actions)}", parts[4]);
                }
                else
                {
                    Assert.DoesNotContain("activating ", line);
                }
                Assert.DoesNotContain("selected", line);
                // Stage U2: the count of units left to activate is gone.
                Assert.DoesNotContain("to activate", line);
                counted++;
            }
        }
        Assert.True(counted > 1000, $"only {counted} lines");
    }

    // Battlefield UI 3, stage U2: the top-centre is exactly whose move it is and the scores, from the server's state;
    // the placing prompt, the model activating and a waiting command are on the status line; nothing is lost.
    [Fact]
    public void TheCentreIsOnlyWhoseMoveAndTheScoresAndTheRestIsOnTheStatusLine()
    {
        var checkedStates = 0;
        foreach (var state in States.Where(s => s.Setup?.Scenario is not null).Take(3000))
        {
            foreach (var seat in new Seat?[] { Seat.One, Seat.Two, null })
            {
                foreach (var waiting in new[] { false, true })
                {
                    var view = MatchView.Of(state, seat, Names);
                    var places = TopHud.Split(view, BoardScene.Of(state, seat, Names), [], seat, waiting, "a prompt");
                    var move = view.YourMove ? "YOUR MOVE" : view.WhoseMove.StartsWith("Opponent's move", StringComparison.Ordinal) ? "OPPONENT'S MOVE" : view.WhoseMove.TrimEnd('.');
                    var expected = view.Turn.Length > 0
                        ? $"{move} · VP A {state.SeatFor(Seat.One).VictoryPoints} – B {state.SeatFor(Seat.Two).VictoryPoints}"
                        : move;
                    Assert.Equal(expected, places.Centre);
                    var status = places.Status.Split(" · ");
                    Assert.Equal("a prompt", status[0]);
                    Assert.Equal(view.ActivatingShort is { } activating ? new[] { "a prompt", activating } : new[] { "a prompt" }, status.Where(p => p != TopHud.Waiting.Split(" · ")[0] && p != TopHud.Waiting.Split(" · ")[1]));
                    Assert.Equal(waiting, places.Status.EndsWith(TopHud.Waiting, StringComparison.Ordinal));
                    Assert.DoesNotContain("a prompt", places.Centre);
                    Assert.DoesNotContain(TopHud.Waiting, places.Centre);
                    checkedStates++;
                }
            }
        }
        Assert.True(checkedStates > 1000, $"{checkedStates} states");
    }

    [Fact]
    public void BothSeatsAgree()
    {
        foreach (var state in States.Where(s => s.Setup?.Scenario is not null && s.Turn > 0).Take(3000))
        {
            string Neutral(Seat seat) => string.Join("|", Hud(state, seat).Split(" · ").Skip(2));
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
        // The objectives of the last scoring come from the log's own line (shown in an objective's panel, QoL H4).
        var log = new List<LogLine> { new(40, "Turn 1 scoring: obj-1 to One.", null), new(41, "--- Turn 2 ---", null) };
        Assert.Equal("obj-1 to One", TopHud.LastScoring(log));
        Assert.Null(TopHud.LastScoring([new(41, "--- Turn 2 ---", null)]));
    }

    // QoL H4 (decision D1): the HUD says who would control each Objective if it were scored now, by the rules' own test,
    // and at each end-of-turn scoring that is exactly what the rules score.
    [Fact]
    public void TheObjectivesNowAreTheRulesOwnAndMatchEveryScoring()
    {
        var (shown, scorings) = (0, 0);
        foreach (var (_, log) in ClientCoreTests.Matches.Value)
        {
            var state = MatchState.None;
            foreach (var e in log)
            {
                if (e is Rules.Core.Events.ObjectivesScored scored)
                {
                    foreach (var result in scored.Results)
                    {
                        Assert.Equal(result.Controller, Rules.Core.Objectives.ControllerNow(state, result.ObjectiveId));
                        Assert.Equal(result.Controller is null ? 0 : Rules.Core.Objectives.PointsPerObjective, result.Points);
                    }
                    scorings++;
                }
                state = Rules.Core.Reducer.Apply(state, e);
            }
        }
        foreach (var state in States.Where(s => s.Setup?.Scenario is { ObjectiveIds.Length: > 0 } && s.Turn > 0).Take(2000))
        {
            var scene = BoardScene.Of(state, Seat.One, ContentNames.None)!;
            var expected = string.Join(" ", state.Setup!.Scenario!.ObjectiveIds.Order(StringComparer.Ordinal)
                .Select(id => $"◆{id[4..]} {(Rules.Core.Objectives.ControllerNow(state, id) is { } c ? Words.SeatLetter(c) : "–")}"));
            Assert.Contains($" · objectives now: {expected}", Hud(state, Seat.One), StringComparison.Ordinal);
            Assert.All(scene.Terrain.Where(t => t.Objective), t => Assert.Equal(Rules.Core.Objectives.ControllerNow(state, t.Id), t.Controller));
            shown++;
        }
        Assert.True(scorings > 50 && shown > 500, $"{scorings} scorings, {shown} HUDs");
    }
}
