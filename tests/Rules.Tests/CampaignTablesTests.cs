using Rules.Core.Scenarios;

namespace Rules.Tests;

// Trench-maps stage S10: the p. 96 campaign scenario tables, and the rule that only a table whose every scenario is
// built can be rolled (decision 4, U91).
public class CampaignTablesTests
{
    [Fact]
    [Trait("Rule", "Selecting a Campaign Scenario p. 96")]
    public void TheTablesAreThoseOnPage96()
    {
        Assert.Equal(["Early Campaign", "Mid-Campaign", "Endgame", "Final Battle"], CampaignTables.All.Select(t => t.Name));
        Assert.Equal(["claim-no-mans-land", "hunt-for-heroes", "the-high-ground", "relic-hunt", "supply-raid"], CampaignTables.All[0].Scenarios.AsEnumerable());
        Assert.Equal(["hunt-for-heroes", "armoured-train", "trench-warfare", "claim-no-mans-land", "dragon-hunt"], CampaignTables.All[1].Scenarios.AsEnumerable());
        Assert.Equal(["trench-warfare", "dragon-hunt", "from-below", "fields-of-glory", "dont-breathe"], CampaignTables.All[2].Scenarios.AsEnumerable());
        Assert.Equal(["the-great-war"], CampaignTables.All[3].Scenarios.AsEnumerable());
        Assert.Equal([(1, 3), (4, 8), (9, 11), (12, 12)], CampaignTables.All.Select(t => (t.FirstGame, t.LastGame)));
    }

    [Theory]
    [Trait("Rule", "Selecting a Campaign Scenario p. 96: the greater number of games played decides the table")]
    [InlineData(0, 0, CampaignStage.Early)]
    [InlineData(2, 0, CampaignStage.Early)]
    [InlineData(1, 3, CampaignStage.Mid)]
    [InlineData(7, 7, CampaignStage.Mid)]
    [InlineData(8, 2, CampaignStage.Endgame)]
    [InlineData(10, 10, CampaignStage.Endgame)]
    [InlineData(11, 9, CampaignStage.Final)]
    public void TheGamesPlayedPickTheTable(int one, int two, CampaignStage stage) =>
        Assert.Equal(stage, CampaignTables.ForNextGame(one, two)!.Stage);

    [Fact]
    public void AfterTheFinalBattleThereIsNoTable()
    {
        Assert.Null(CampaignTables.ForNextGame(12, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => CampaignTables.ForNextGame(-1, 0));
    }

    [Fact]
    [Trait("Rule", "p. 96: a D6; on a 6 the player who has played fewer games chooses")]
    public void ADieOfOneToFivePicksAScenarioAndSixIsAChoice()
    {
        var mid = CampaignTables.All[1];
        Assert.Equal("trench-warfare", CampaignTables.Roll(mid, 3).Scenario);
        Assert.Equal("dragon-hunt", CampaignTables.Roll(mid, 5).Scenario);
        Assert.True(CampaignTables.Roll(mid, 6).IsChoice);
        Assert.Throws<ArgumentOutOfRangeException>(() => CampaignTables.Roll(mid, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CampaignTables.Roll(mid, 7));
        Assert.Throws<ArgumentException>(() => CampaignTables.Roll(CampaignTables.All[3], 1));
    }

    [Fact]
    public void NoTableCanBeRolledUntilEveryScenarioOnItIsBuilt()
    {
        Assert.Equal(["claim-no-mans-land", "trench-warfare"], CampaignTables.Built.AsEnumerable());
        Assert.Empty(CampaignTables.Rollable);
        Assert.Equal(["hunt-for-heroes", "armoured-train", "dragon-hunt"], CampaignTables.NotBuilt(CampaignTables.All[1]).AsEnumerable());
        // Every built scenario is one this build plays.
        Assert.All(CampaignTables.Built, s => Assert.Contains(s, new[] { ClaimNoMansLand.ScenarioId, TrenchWarfare.ScenarioId }));
    }
}
