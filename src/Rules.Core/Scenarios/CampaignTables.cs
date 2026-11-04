using System.Collections.Immutable;

namespace Rules.Core.Scenarios;

// Trench-maps stage S10: the campaign scenario tables (Digital Rulebook 1.0.2 p. 96), the only random scenario tables
// the book has (a one-off game chooses, p. 144). Decision 4 of the trench-maps plan: the server rolls one of these
// tables, and only a table whose every scenario is built can be rolled. None can yet: each table names scenarios this
// build does not play, so Rollable is empty until they are built (U91).
public enum CampaignStage
{
    Early,
    Mid,
    Endgame,
    Final,
}

// One table: the games it is for, and its scenarios in die order (a D6 roll of 1 to 5 picks one; on a 6 the player
// who has played fewer games chooses one of them, or the winner of a roll-off if tied). The Final Battle is one
// scenario and is not rolled.
public sealed record CampaignTable(CampaignStage Stage, string Name, int FirstGame, int LastGame, ImmutableArray<string> Scenarios)
{
    public bool IsRolled => Scenarios.Length > 1;
}

// What a roll on a table gives: a scenario, or a choice from the table's scenarios.
public sealed record CampaignRoll(CampaignTable Table, int Die, string? Scenario)
{
    public bool IsChoice => Scenario is null;
}

public static class CampaignTables
{
    public const string HuntForHeroes = "hunt-for-heroes";
    public const string HighGround = "the-high-ground";
    public const string RelicHunt = "relic-hunt";
    public const string SupplyRaid = "supply-raid";
    public const string ArmouredTrain = "armoured-train";
    public const string DragonHunt = "dragon-hunt";
    public const string FromBelow = "from-below";
    public const string FieldsOfGlory = "fields-of-glory";
    public const string DontBreathe = "dont-breathe";
    public const string GreatWar = "the-great-war";

    public static readonly ImmutableArray<CampaignTable> All =
    [
        new(CampaignStage.Early, "Early Campaign", 1, 3, [ClaimNoMansLand.ScenarioId, HuntForHeroes, HighGround, RelicHunt, SupplyRaid]),
        new(CampaignStage.Mid, "Mid-Campaign", 4, 8, [HuntForHeroes, ArmouredTrain, TrenchWarfare.ScenarioId, ClaimNoMansLand.ScenarioId, DragonHunt]),
        new(CampaignStage.Endgame, "Endgame", 9, 11, [TrenchWarfare.ScenarioId, DragonHunt, FromBelow, FieldsOfGlory, DontBreathe]),
        new(CampaignStage.Final, "Final Battle", 12, 12, [GreatWar]),
    ];

    // The scenarios this build plays.
    public static readonly ImmutableArray<string> Built = [ClaimNoMansLand.ScenarioId, TrenchWarfare.ScenarioId];

    // The table for the next game: count the games each player has played so far and use the greater number (p. 96).
    // Null after the Final Battle.
    public static CampaignTable? ForNextGame(int gamesPlayedOne, int gamesPlayedTwo)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(gamesPlayedOne);
        ArgumentOutOfRangeException.ThrowIfNegative(gamesPlayedTwo);
        var game = Math.Max(gamesPlayedOne, gamesPlayedTwo) + 1;
        return All.FirstOrDefault(t => t.FirstGame <= game && game <= t.LastGame);
    }

    // A D6 on a rolled table: 1 to 5 the scenario in that place, 6 a choice.
    public static CampaignRoll Roll(CampaignTable table, int die)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (!table.IsRolled)
        {
            throw new ArgumentException($"The {table.Name} is not rolled for.", nameof(table));
        }
        ArgumentOutOfRangeException.ThrowIfLessThan(die, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(die, 6);
        return new CampaignRoll(table, die, die == 6 ? null : table.Scenarios[die - 1]);
    }

    // The tables whose every scenario this build plays: the only ones the server may roll (decision 4). Empty for now.
    public static ImmutableArray<CampaignTable> Rollable => [.. All.Where(t => t.IsRolled && t.Scenarios.All(Built.Contains))];

    // What a table needs before it can be rolled.
    public static ImmutableArray<string> NotBuilt(CampaignTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return [.. table.Scenarios.Where(s => !Built.Contains(s)).Distinct()];
    }
}
