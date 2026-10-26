using Client.Core;
using Rules.Core;
using Rules.Core.Events;
using Rules.Core.State;
using Sim;

namespace Client.Tests;

// Proposal J, stage J3b: the Scenario card repeats Rules.Core.ScenarioRules, each objective row names the controller the
// rules' own test gives, and the PREVIEW line equals what the server scored when each turn actually ended.
public class ScenarioCardTests
{
    [Fact]
    public void TheCardIsTheRulesOwnDescriptionAndRowsAndPreviewFollowTheRules()
    {
        var (previews, completedLines) = (0, 0);
        foreach (var path in new[] { Content.DefaultPath(), Content.MeleePath() })
        {
            var warbands = Content.Load(path);
            for (var seed = 1; seed <= 15; seed++)
            {
                var log = Runner.Run(seed, warbands).Log;
                var state = MatchState.None;
                foreach (var e in log)
                {
                    if (e is ObjectivesScored scored)
                    {
                        // The state just before the server's scoring: the PREVIEW line is exactly what it then scored.
                        foreach (var you in new Seat?[] { Seat.One, Seat.Two, null })
                        {
                            var card = ScenarioCard.Of(state, you);
                            Assert.NotNull(card.Projection);
                            int Got(Seat s) => scored.Results.Where(r => r.Controller == s).Sum(r => r.Points);
                            var expected = you is { } me
                                ? $"{ScenarioCard.PreviewPrefix}you +{Got(me)}, opponent +{Got(me == Seat.One ? Seat.Two : Seat.One)}"
                                : $"{ScenarioCard.PreviewPrefix}{Words.Seat(Seat.One)} +{Got(Seat.One)}, {Words.Seat(Seat.Two)} +{Got(Seat.Two)}";
                            Assert.Equal(expected, card.Projection);
                            Assert.Equal(scored.Results.OrderBy(r => r.ObjectiveId, StringComparer.Ordinal).Select(r => r.Controller), card.Rows.Select(r => r.Controller));
                            previews++;
                        }
                    }
                    state = Reducer.Apply(state, e);
                    if (state.Setup is null)
                    {
                        continue;
                    }
                    var view = ScenarioCard.Of(state, Seat.One);
                    var rules = ScenarioRules.Of(state.Setup)!;
                    Assert.All(rules.Scoring.Concat(rules.MatchEnds).Concat(rules.Provisional), line => Assert.Contains("  " + line, view.Lines));
                    Assert.Equal(rules.ObjectiveIds.Order(StringComparer.Ordinal), view.Rows.Select(r => r.ObjectiveId));
                    Assert.All(view.Rows, r => Assert.Equal(Objectives.ControllerNow(state, r.ObjectiveId), r.Controller));
                    // Glorious Deeds (U96): a line per deed of the scenario, naming who completed it, if anyone has.
                    var deedLines = view.Lines.SkipWhile(l => l != ScenarioCard.DeedsHeading).Skip(1).TakeWhile(l => l.StartsWith("  ", StringComparison.Ordinal)).ToList();
                    Assert.Equal(Deeds.For(state.Setup).Length, deedLines.Count);
                    foreach (var (deed, line) in Deeds.For(state.Setup).Zip(deedLines))
                    {
                        var done = state.Deeds.Completed.FirstOrDefault(c => c.Deed == deed);
                        Assert.StartsWith($"  {Deeds.Name(deed)}: ", line, StringComparison.Ordinal);
                        Assert.EndsWith(done is null ? "not yet completed" : $"completed by {Words.Seat(done.Seat)}{(done.Seat == Seat.One ? " (you)" : "")}", line, StringComparison.Ordinal);
                        completedLines += done is null ? 0 : 1;
                    }
                }
                Assert.Null(ScenarioCard.Of(state, Seat.One).Projection);
            }
        }
        Assert.True(previews > 100, $"{previews} previews");
        Assert.True(completedLines > 0, "no deed line showed a completed deed");
    }

    [Fact]
    public void AScenarioTheRulesDoNotDescribeSaysSo()
    {
        var card = ScenarioCard.Of(MatchState.None, Seat.One);
        Assert.Equal([ScenarioCard.NotAvailable], card.Lines);
        Assert.Empty(card.Rows);
        Assert.Null(card.Projection);
    }
}
