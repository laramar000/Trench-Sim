using Roster;

namespace Roster.Tests;

// Every entry here is invented test data, not taken from any rulebook.
public class CatalogueFormatTests
{
    private static readonly KeywordVocabulary Vocabulary = KeywordVocabulary.Load(KeywordVocabulary.DefaultPath()).Value!;

    private const string Carbine = """
        { "id": "practice-carbine", "name": "Practice Carbine", "category": "ranged", "hands": 2, "rangeInches": 20,
          "cost": { "amount": 9, "currency": "ducats" },
          "keywords": [ { "id": "AUTOMATIC", "value": 2 }, { "id": "BLAST", "value": 1 }, { "id": "NEGATE", "value": "GAS" }, { "id": "CRITICAL" } ],
          "dice": 1, "injuryDice": -1,
          "rule": { "summary": "Invented rule summary.", "page": 3 },
          "restrictions": [ { "kind": "eliteOnly" }, { "kind": "limit", "value": 2 } ],
          "source": { "book": "test", "version": "1.0.2", "page": 3 }, "status": "verified", "naming": "keep" }
        """;

    private const string Sidearm = """
        { "id": "practice-sidearm", "name": "Practice Sidearm", "category": "ranged", "hands": 1, "rangeInches": 8, "alsoMelee": true,
          "cost": null, "source": { "book": "test", "version": "1.0.2", "page": 4 }, "status": "unverified", "naming": "review" }
        """;

    private const string Coat = """
        { "id": "practice-coat", "name": "Practice Coat", "category": "armour", "injuryModifier": -1, "campaignOnly": true,
          "cost": { "amount": 2, "currency": "glory" },
          "source": { "book": "test", "version": "1.0.2", "page": 5 }, "status": "discrepancy", "note": "Invented: two sources differ.", "naming": "rename" }
        """;

    private static Checked<Catalogue> Parse(params string[] entries) =>
        Catalogue.Parse($$"""{ "format": 1, "rulesVersion": "1.0.2", "entries": [ {{string.Join(",", entries)}} ] }""", Vocabulary);

    private static string With(string entry, string field, string value) =>
        entry.TrimEnd().TrimEnd('}') + $", \"{field}\": {value} }}";

    private static void Refused(string expected, params string[] entries)
    {
        var loaded = Parse(entries);
        Assert.False(loaded.Ok);
        Assert.Contains(loaded.Problems, p => p.Contains(expected));
    }

    [Fact]
    public void AWellFormedCatalogueReadsEveryField()
    {
        var loaded = Parse(Carbine, Sidearm, Coat);
        Assert.Empty(loaded.Problems);
        var carbine = loaded.Value!.Find("practice-carbine")!;
        Assert.Equal(KitCategory.Ranged, carbine.Category);
        Assert.Equal(2, carbine.Hands);
        Assert.Equal(20, carbine.RangeInches);
        Assert.Equal(new Cost(9, Currency.Ducats), carbine.Cost);
        Assert.Equal(new[] { new KeywordUse("AUTOMATIC", 2, null), new KeywordUse("BLAST", 1, null), new KeywordUse("NEGATE", null, "GAS"), new KeywordUse("CRITICAL", null, null) }, carbine.Keywords);
        Assert.Equal((1, -1, 0), (carbine.Dice, carbine.InjuryDice, carbine.InjuryModifier));
        Assert.Equal(new RuleRef("Invented rule summary.", 3), carbine.Rule);
        Assert.Equal(new[] { new Restriction(RestrictionKind.EliteOnly, null, null), new Restriction(RestrictionKind.Limit, 2, null) }, carbine.Restrictions);
        Assert.Equal(new SourceRef("test", "1.0.2", 3), carbine.Source);
        Assert.Equal((DataStatus.Verified, Naming.Keep, false), (carbine.Status, carbine.Naming, carbine.CampaignOnly));

        var sidearm = loaded.Value.Find("practice-sidearm")!;
        Assert.Null(sidearm.Cost);
        Assert.True(sidearm.AlsoMelee);
        var coat = loaded.Value.Find("practice-coat")!;
        Assert.Equal(new Cost(2, Currency.Glory), coat.Cost);
        Assert.True(coat.CampaignOnly);
        Assert.Equal(DataStatus.Discrepancy, coat.Status);
    }

    [Fact]
    public void DuplicateIdsAndNamesAreRefused()
    {
        Refused("the id is used twice", Carbine, Carbine);
        Refused("is used by another entry", Carbine, Carbine.Replace("practice-carbine", "practice-carbine-two"));
    }

    [Theory]
    [InlineData("""{ "id": "CRIT" }""", "unknown keyword \"CRIT\"")]
    [InlineData("""{ "id": "AUTOMATIC" }""", "AUTOMATIC needs a whole number")]
    [InlineData("""{ "id": "AUTOMATIC", "value": 0 }""", "AUTOMATIC needs a whole number")]
    [InlineData("""{ "id": "BLAST", "value": "3in" }""", "BLAST needs a whole number")]
    [InlineData("""{ "id": "NEGATE" }""", "NEGATE needs text")]
    [InlineData("""{ "id": "HEAVY", "value": 1 }""", "HEAVY takes no value")]
    public void BadKeywordsAreRefused(string keyword, string expected) =>
        Refused(expected, Sidearm.Replace("\"cost\": null", $"\"cost\": null, \"keywords\": [ {keyword} ]"));

    [Fact]
    public void AKeywordListedTwiceIsRefused() =>
        Refused("HEAVY is listed twice", With(Sidearm, "keywords", """[ { "id": "HEAVY" }, { "id": "HEAVY" } ]"""));

    [Theory]
    [InlineData("\"cost\": { \"amount\": -1, \"currency\": \"ducats\" }", "a cost cannot be negative")]
    [InlineData("\"cost\": { \"amount\": 5, \"currency\": \"gold\" }", "\"currency\" is \"gold\"")]
    [InlineData("\"cost\": { \"amount\": 2.5, \"currency\": \"ducats\" }", "\"amount\" must be a whole number")]
    [InlineData("\"cost\": 5", "\"cost\" must be an object")]
    public void BadCostsAreRefused(string cost, string expected) =>
        Refused(expected, Sidearm.Replace("\"cost\": null", cost));

    // Stage 4: a piece's price is set by each faction's armoury, so a verified catalogue profile needs no cost of its own.
    [Fact]
    public void AVerifiedEntryNeedsNoCostOfItsOwn() =>
        Assert.Empty(Parse(Sidearm.Replace("\"unverified\"", "\"verified\"")).Problems);

    [Fact]
    public void ADiscrepancyMustSayWhatDisagrees() =>
        Refused("a discrepancy needs a \"note\"", Coat.Replace(", \"note\": \"Invented: two sources differ.\"", ""));

    [Theory]
    [InlineData("\"hands\": 2", "\"hands\": 3", "\"hands\" must be 1 or 2")]
    [InlineData("\"hands\": 2, ", "", "weapon needs \"hands\"")]
    [InlineData("\"rangeInches\": 20,", "", "needs \"rangeInches\"")]
    [InlineData("\"rangeInches\": 20", "\"rangeInches\": 0", "must be 1 or more")]
    [InlineData("\"category\": \"ranged\"", "\"category\": \"artillery\"", "\"category\" is \"artillery\"")]
    [InlineData("\"id\": \"practice-carbine\"", "\"id\": \"Practice_Carbine\"", "an id is lower-case letters")]
    [InlineData("\"name\": \"Practice Carbine\"", "\"name\": \" Practice Carbine\"", "spaces at its start or end")]
    [InlineData("\"dice\": 1", "\"dice\": 4", "between -3 and +3")]
    [InlineData("\"status\": \"verified\"", "\"status\": \"checked\"", "\"status\" is \"checked\"")]
    [InlineData("\"naming\": \"keep\"", "\"naming\": \"cleared\"", "\"naming\" is \"cleared\"")]
    [InlineData("{ \"kind\": \"limit\", \"value\": 2 }", "{ \"kind\": \"limit\" }", "\"value\" is missing")]
    [InlineData("{ \"kind\": \"eliteOnly\" }", "{ \"kind\": \"other\" }", "\"text\" is missing")]
    [InlineData("\"page\": 3 }, \"status\"", "\"page\": 0 }, \"status\"", "the source page must be 1 or more")]
    [InlineData("\"version\": \"1.0.2\", \"page\": 3", "\"page\": 3", "\"version\" is missing")]
    public void MalformedFieldsAreRefused(string from, string to, string expected)
    {
        Assert.Contains(from, Carbine);
        Refused(expected, Carbine.Replace(from, to));
    }

    [Theory]
    [InlineData("\"category\": \"armour\"", "\"category\": \"armour\", \"hands\": 1", "armour does not take \"hands\"")]
    [InlineData("\"category\": \"armour\"", "\"category\": \"armour\", \"rangeInches\": 6", "only ranged, grenade and special entries have a range")]
    [InlineData("\"category\": \"armour\"", "\"category\": \"armour\", \"alsoMelee\": true", "only a ranged weapon can also be used in melee")]
    public void NonWeaponsCannotHaveWeaponFields(string from, string to, string expected) =>
        Refused(expected, Coat.Replace(from, to));

    [Fact]
    public void AMissingSourceIsRefused() =>
        Refused("\"source\" (book, version, page) is missing", Sidearm.Replace("\"source\": { \"book\": \"test\", \"version\": \"1.0.2\", \"page\": 4 }, ", ""));

    [Fact]
    public void ACatalogueForAnotherRulesVersionIsRefused()
    {
        var loaded = Catalogue.Parse("""{ "format": 1, "rulesVersion": "1.6", "entries": [] }""", Vocabulary);
        Assert.False(loaded.Ok);
        Assert.Contains(loaded.Problems, p => p.Contains("supports 1.0.2 only"));
    }

    [Fact]
    public void OneBadEntryRefusesTheWholeCatalogue()
    {
        var loaded = Parse(Carbine, Sidearm.Replace("\"hands\": 1", "\"hands\": 5"));
        Assert.Null(loaded.Value);
        Assert.Single(loaded.Problems);
        Assert.StartsWith("entry \"practice-sidearm\"", loaded.Problems[0]);
    }
}
