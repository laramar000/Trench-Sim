using Rules.Core;
using Rules.Core.Events;
using Rules.Core.Scenarios;
using Rules.Core.State;
using Sim;

namespace Rules.Tests;

// Proposal J3a: the scenario description the client shows carries the rules' own numbers, matches what the simulated
// matches actually score and how they end, and exists only for the scenario the rules implement.
public class ScenarioRulesTests
{
    [Fact]
    public void ClaimNoMansLandIsDescribedWithTheRulesOwnNumbers()
    {
        var setup = RealSetup();
        var rules = ScenarioRules.Of(setup)!;
        Assert.Equal(ClaimNoMansLand.ScenarioId, rules.Id);
        Assert.Equal(Scoring.PointsPerObjective, rules.PointsPerObjective);
        Assert.Equal(Objectives.PointsPerObjective, rules.PointsPerObjective);
        Assert.Equal(setup.TurnLimit, rules.TurnLimit);
        Assert.Equal(setup.Scenario!.ObjectiveIds, rules.ObjectiveIds);
        Assert.Contains(rules.Scoring, s => s.Contains($"{Scoring.PointsPerObjective} Victory Points", StringComparison.Ordinal));
        Assert.Contains(rules.MatchEnds, s => s.StartsWith($"After Turn {setup.TurnLimit}:", StringComparison.Ordinal));
        Assert.Equal(3, rules.MatchEnds.Count);
        Assert.All(new[] { "U32", "U36", "U40" }, u => Assert.Contains(rules.Provisional, p => p.Contains(u, StringComparison.Ordinal)));
    }

    [Fact]
    public void OtherSetupsHaveNoDescription()
    {
        Assert.Null(ScenarioRules.Of(null));
        Assert.Null(ScenarioRules.Of(Play.Setup(2, 2)));
        var setup = RealSetup();
        Assert.NotNull(ScenarioRules.Of(setup));
        var other = new MatchSetup(setup.RulesVersion, "some-other-scenario", setup.TurnLimit, setup.Profile, setup.ZoneIds, setup.Models, setup.Scenario);
        Assert.Null(ScenarioRules.Of(other));
    }

    [Fact]
    public void SimulatedMatchesScoreAndEndAsDescribed()
    {
        var reasons = new HashSet<OutcomeReason>();
        var scorings = 0;
        foreach (var path in new[] { Content.DefaultPath(), Content.MeleePath() })
        {
            var warbands = Content.Load(path);
            for (var seed = 1; seed <= 20; seed++)
            {
                var log = Runner.Run(seed, warbands).Log;
                var rules = ScenarioRules.Of(((MatchCreated)log[0]).Setup)!;
                foreach (var scored in log.OfType<ObjectivesScored>())
                {
                    scorings++;
                    Assert.Equal(rules.ObjectiveIds.Order(StringComparer.Ordinal), scored.Results.Select(r => r.ObjectiveId).Order(StringComparer.Ordinal));
                    Assert.All(scored.Results, r => Assert.Equal(r.Controller is null ? 0 : rules.PointsPerObjective, r.Points));
                    Assert.InRange(scored.Turn, 1, rules.TurnLimit);
                }
                var ended = log.OfType<MatchEnded>().Single();
                reasons.Add(ended.Reason);
                Assert.Contains(ended.Reason, new[] { OutcomeReason.VictoryPoints, OutcomeReason.AllEnemiesOutOfAction, OutcomeReason.OpponentFled });
            }
        }
        Assert.True(scorings > 50, $"{scorings} scorings");
    }

    private static MatchSetup RealSetup() => ((MatchCreated)Runner.Run(1, Content.Load(Content.DefaultPath())).Log[0]).Setup;
}
