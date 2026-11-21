using Roster;

namespace Roster.Tests;

// Roster stage R6f (U122): upgrade rules the builder checks: the two Legionnaire upgrades and Kavass with Light Skirmisher
// exclude each other on one model, and a warband never has more Legionnaires than plain Heretic Troopers (Warbands pp. 112,
// 84).
public class UpgradeRuleTests
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "content", "roster");
    private static readonly KeywordVocabulary Vocabulary = KeywordVocabulary.Load(KeywordVocabulary.DefaultPath()).Value!;
    private static readonly Catalogue Catalogue = Roster.Catalogue.Load(Path.Combine(Folder, "catalogue.json"), Vocabulary).Value!;

    private static RosterCheck Check(string faction, string? variant, params RosterModel[] models)
    {
        var loaded = Faction.Load(Path.Combine(Folder, "factions", faction + ".json"), Catalogue, Vocabulary);
        Assert.True(loaded.Ok, string.Join("\n", loaded.Problems));
        return RosterValidator.Check(new WarbandRoster("Test", faction, variant, 2000, 0, models), loaded.Value!, Catalogue);
    }

    private static RosterModel M(string entry, params string[] upgrades) => new(entry, null, [], upgrades);

    private static bool Has(RosterCheck check, string words) => check.Problems.Any(p => p.Contains(words, StringComparison.Ordinal));

    [Fact]
    public void NeverMoreLegionnairesThanPlainTroopers()
    {
        const string ratio = "never have more Legionnaires than plain Troopers";
        Assert.False(Has(Check("heretics", null, M("heretic-trooper", "legionnaire-ranged"), M("heretic-trooper")), ratio));
        Assert.True(Has(Check("heretics", null, M("heretic-trooper", "legionnaire-ranged"), M("heretic-trooper", "legionnaire-melee"), M("heretic-trooper")), ratio));
        Assert.True(Has(Check("heretics", null, M("heretic-trooper", "legionnaire-melee")), ratio));
        Assert.False(Has(Check("heretics", null, M("heretic-trooper")), ratio));
    }

    [Fact]
    public void ExclusiveUpgradesCannotShareAModel()
    {
        const string both = "cannot both be bought for one model";
        Assert.True(Has(Check("heretics", null, M("heretic-trooper", "legionnaire-ranged", "legionnaire-melee"), M("heretic-trooper"), M("heretic-trooper")), both));
        Assert.True(Has(Check("sultanate", "house-of-wisdom", M("azeb", "kavass", "light-skirmisher")), both));
        Assert.False(Has(Check("sultanate", "house-of-wisdom", M("azeb", "kavass"), M("azeb", "light-skirmisher")), both));
    }
}
