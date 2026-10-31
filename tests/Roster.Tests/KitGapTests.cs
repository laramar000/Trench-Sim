using Roster;

namespace Roster.Tests;

// Stage 4 gaps: the Trench Dog and its options (core rulebook p. 142), the Amalgam's and Fallen Saint's extra arms
// (Warbands of Trench Crusade pp. 140, 170) and kit bought from another faction's armoury (Weapon Collections p. 95,
// Corrupt Merchants p. 120, Envious Eyes p. 156, Knight of the Rotten Cross p. 128), on the shipped lists.
public class KitGapTests
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "content", "roster");
    private static readonly KeywordVocabulary Vocabulary = KeywordVocabulary.Load(KeywordVocabulary.DefaultPath()).Value!;
    private static readonly Catalogue Catalogue = Roster.Catalogue.Load(Path.Combine(Folder, "catalogue.json"), Vocabulary).Value!;

    private static Faction Load(string id)
    {
        var loaded = Faction.Load(Path.Combine(Folder, "factions", id + ".json"), Catalogue, Vocabulary);
        Assert.True(loaded.Ok, string.Join("\n", loaded.Problems));
        return loaded.Value!;
    }

    private static RosterCheck Check(string faction, string? variant, int glory, params RosterModel[] models)
    {
        var list = Load(faction);
        return RosterValidator.Check(new WarbandRoster("Test", faction, variant, 2000, glory, models), list, Catalogue);
    }

    private static RosterModel M(string entry, string[] kit, params string[] upgrades) => new(entry, null, kit, upgrades);

    private static void Legal(RosterCheck check) => Assert.True(check.Legal, string.Join("; ", check.Problems));

    private static void Refused(RosterCheck check, string words) =>
        Assert.True(check.Problems.Any(p => p.Contains(words)), $"no problem with \"{words}\": {string.Join("; ", check.Problems)}");

    // ---- Trench Dog ----

    [Fact]
    public void ATrenchDogWithBothOptionsCostsThreeGlory()
    {
        var check = Check("principality", null, 3, M("lieutenant", ["trench-dog", "loyal-hound", "mercy-dog"]));
        Legal(check);
        Assert.Equal((70, 3), (check.Ducats, check.Glory));
    }

    [Fact]
    public void ADogOptionNeedsTheDog() =>
        Refused(Check("principality", null, 3, M("lieutenant", ["mercy-dog"])), "needs Trench Dog");

    [Fact]
    public void ATrenchDogHasOneSpecialTraining() =>
        Refused(Check("principality", null, 3, M("lieutenant", ["trench-dog", "mercy-dog", "guard-dog"])), "at most 1 from Trench Dog training");

    [Theory]
    [InlineData("principality", "hellhound")]
    [InlineData("principality", "martyrdom-dog")]
    [InlineData("pilgrims", "mercy-dog")]
    [InlineData("cult", "guard-dog")]
    [InlineData("court", "guard-dog")]
    public void ATrainingIsOnlyForTheWarbandsItNames(string faction, string training)
    {
        var list = Load(faction);
        Assert.DoesNotContain(list.Armoury, a => a.Kit == training);
    }

    [Theory]
    [InlineData("principality", "mercy-dog")]
    [InlineData("pilgrims", "martyrdom-dog")]
    [InlineData("sultanate", "guard-dog")]
    [InlineData("heretics", "hellhound")]
    [InlineData("cult", "hellhound")]
    [InlineData("court", "hellhound")]
    public void EveryListPricesTheDogAndItsTraining(string faction, string training)
    {
        var list = Load(faction);
        foreach (var kit in new[] { "trench-dog", "loyal-hound", training })
            Assert.Equal(new Cost(1, Currency.Glory), list.Armoury.Single(a => a.Kit == kit && a.From is null).Cost);
    }

    [Fact]
    public void TrenchWraithsHaveNoDog()
    {
        var wraiths = Load("heretics").As("trench-wraiths")!.Value;
        Assert.DoesNotContain(wraiths.Armoury, a => a.Kit is "trench-dog" or "loyal-hound" or "guard-dog" or "hellhound");
    }

    // ---- Amalgam: six arms ----

    [Fact]
    public void AnAmalgamCarriesSixWeaponsWithTwoHeavyOnesAsOneHanded() =>
        Legal(Check("cult", null, 0, M("amalgam", ["machine-gun", "great-hammer-maul", "pistol", "pistol", "sword-axe", "trench-knife"])));

    [Fact]
    public void AnAmalgamCannotCarrySevenWeapons() =>
        Refused(Check("cult", null, 0, M("amalgam", ["machine-gun", "great-hammer-maul", "pistol", "pistol", "sword-axe", "trench-knife", "trench-club"])),
            "has 6 arms");

    [Fact]
    public void AnAmalgamsThirdHeavyWeaponUsesTwoArms() =>
        Refused(Check("cult", null, 0, M("amalgam", ["machine-gun", "great-hammer-maul", "great-sword-axe", "pistol", "pistol", "sword-axe"])),
            "need 7");

    [Fact]
    public void AnAmalgamCannotCarryTheSameWeaponThreeTimes() =>
        Refused(Check("cult", null, 0, M("amalgam", ["pistol", "pistol", "pistol"])), "more than twice");

    [Fact]
    public void AnAmalgamsShieldTakesAnArmWithoutShieldCombo() =>
        Legal(Check("cult", null, 0, M("amalgam", ["trench-shield", "shotgun", "pistol", "pistol", "sword-axe"])));

    [Fact]
    public void AnAmalgamStillCannotHaveArmour() =>
        Refused(Check("cult", null, 0, M("amalgam", ["pistol", "standard-armour"])), "cannot be given armour");

    // ---- Fallen Saint: three arms ----

    [Fact]
    public void AFallenSaintCarriesThreeOneHandedMeleeWeapons() =>
        Legal(Check("court", null, 0, M("fallen-saint", ["sword-axe", "trench-knife", "trench-club"])));

    [Fact]
    public void AFallenSaintCarriesTwoOneHandedAndOneTwoHanded() =>
        Legal(Check("court", null, 0, M("fallen-saint", ["sword-axe", "trench-knife", "great-sword-axe"])));

    [Fact]
    public void AFallenSaintCarriesOnlyOneTwoHandedWeapon() =>
        Refused(Check("court", null, 0, M("fallen-saint", ["great-sword-axe", "hellblade", "sword-axe"])), "at most 1 2-handed weapon");

    [Fact]
    public void AFallenSaintCannotCarryFourWeapons() =>
        Refused(Check("court", null, 0, M("fallen-saint", ["sword-axe", "trench-knife", "trench-club", "headtaker"])), "has 3 arms");

    // ---- Kit from another faction ----

    [Fact]
    public void TheHouseOfWisdomBuysOnePieceFromEachNamedArmoury()
    {
        var check = Check("sultanate", "house-of-wisdom", 0,
            M("alchemist", ["bolt-action-rifle@principality"]), M("azeb", ["blunderbuss@pilgrims"]));
        Legal(check);
        Assert.Equal(55 + 25 + 10 + 5, check.Ducats);
    }

    [Fact]
    public void TheHouseOfWisdomBuysOnlyOnePieceFromThePrincipality() =>
        Refused(Check("sultanate", "house-of-wisdom", 0,
            M("alchemist", ["bolt-action-rifle@principality"]), M("azeb", ["shotgun@principality"])), "at most 1 for the whole warband");

    [Fact]
    public void TheStandardSultanateCannotBuyFromThePrincipality() =>
        Refused(Check("sultanate", null, 0, M("yuzbasi", ["bolt-action-rifle@principality"])), "from \"principality\" is not available");

    [Fact]
    public void GildedKnightsBuyFromThePrincipalityAndTheSultanate() =>
        Legal(Check("heretics", "gilded-knights", 0, M("heretic-priest", ["bolt-action-rifle@principality", "sword-axe@sultanate"])));

    [Fact]
    public void EnviousEyesNeedsThePower()
    {
        Legal(Check("court", "dedicated-to-envy", 0, M("praetor", ["shotgun@pilgrims"], "envious-eyes")));
        Refused(Check("court", "dedicated-to-envy", 0, M("praetor", ["shotgun@pilgrims"])), "needs Envious Eyes");
        Refused(Check("court", "dedicated-to-envy", 0, M("praetor", ["shotgun@pilgrims", "pistol@sultanate"], "envious-eyes")), "at most 1 from Envious Eyes");
    }

    [Fact]
    public void AKnightOfTheRottenCrossBuysOneWeapon()
    {
        Legal(Check("cult", null, 0, M("plague-knight", ["standard-armour", "automatic-rifle@principality"], "knight-of-the-rotten-cross")));
        Refused(Check("cult", null, 0, M("plague-knight", ["standard-armour", "automatic-rifle@principality"])), "needs Knight of the Rotten Cross");
        var cult = Load("cult");
        Assert.All(cult.Armoury.Where(a => a.Group == "rotten-cross"),
            a => Assert.Contains(Catalogue.Find(a.Kit)!.Category, new[] { KitCategory.Ranged, KitCategory.Melee }));
        Assert.DoesNotContain(cult.As("mourning-procession")!.Value.Armoury, a => a.Group == "rotten-cross");
    }

    [Fact]
    public void BorrowedRowsKeepTheirOwnArmourysPrice()
    {
        var principality = Load("principality");
        var wisdom = Load("sultanate").As("house-of-wisdom")!.Value;
        foreach (var row in wisdom.Armoury.Where(a => a.From == "principality"))
            Assert.Equal(principality.Armoury.Single(a => a.Kit == row.Kit && a.From is null).Cost, row.Cost);
    }
}

// The new list fields are checked when a list loads.
public class KitGroupLoadingTests
{
    private static readonly KeywordVocabulary Vocabulary = KeywordVocabulary.Load(KeywordVocabulary.DefaultPath()).Value!;
    private static readonly Catalogue Catalogue = Roster.Catalogue.Load(
        Path.Combine(AppContext.BaseDirectory, "content", "roster", "catalogue.json"), Vocabulary).Value!;

    private const string Template = """
        {"format":1,"rulesVersion":"1.0.2","id":"t","name":"T","alignment":"faithful","budgetDucats":700,
         "source":{"book":"b","version":"1.0.2","page":1},"status":"verified","naming":"keep","kitGroups":GROUPS,
         "models":[{"id":"m","name":"M","cost":{"amount":10,"currency":"ducats"},"movementInches":6,"movementType":"infantry",
           "ranged":0,"melee":0,"armour":0,"baseMm":[25],"keywords":[],"kitAccess":"armoury",
           "upgrades":[{"id":"u","name":"U","cost":{"amount":5,"currency":"ducats"}}]KITRULES,
           "source":{"book":"b","version":"1.0.2","page":1},"status":"verified","naming":"keep"}],
         "armoury":ARMOURY,"variants":[]}
        """;

    private static IReadOnlyList<string> Problems(string armoury, string groups = "[]", string kitRules = "") =>
        Faction.Parse(Template.Replace("ARMOURY", armoury).Replace("GROUPS", groups).Replace("KITRULES", kitRules), Catalogue, Vocabulary).Problems;

    private const string Pistol = """{"kit":"pistol","cost":{"amount":6,"currency":"ducats"},"status":"verified"}""";

    [Fact]
    public void TheSameKitFromTwoArmouriesLoads() =>
        Assert.Empty(Problems("[" + Pistol + """,{"kit":"pistol","cost":{"amount":6,"currency":"ducats"},"from":"other","group":"g","status":"verified"}]""",
            """[{"id":"g","name":"G","limit":1}]"""));

    [Fact]
    public void TheSameKitTwiceFromOneArmouryIsRefused() =>
        Assert.Contains(Problems("[" + Pistol + "," + Pistol + "]"), p => p.Contains("listed twice"));

    [Fact]
    public void AGroupMustBeDefined() =>
        Assert.Contains(Problems("""[{"kit":"pistol","cost":{"amount":6,"currency":"ducats"},"group":"nope","status":"verified"}]"""),
            p => p.Contains("kit group \"nope\""));

    [Fact]
    public void AGroupsUpgradeMustExist() =>
        Assert.Contains(Problems("[" + Pistol + "]", """[{"id":"g","name":"G","needsUpgrade":"missing"}]"""), p => p.Contains("upgrade \"missing\""));

    [Fact]
    public void NeededKitMustBeInTheList() =>
        Assert.Contains(Problems("""[{"kit":"loyal-hound","cost":{"amount":1,"currency":"glory"},"needsKit":"trench-dog","status":"verified"}]"""),
            p => p.Contains("needs \"trench-dog\""));

    [Fact]
    public void ArmsNeedAtLeastThree() =>
        Assert.Contains(Problems("[" + Pistol + "]", "[]", ""","kitRules":{"arms":{"count":2,"twoHandedArms":2}}"""), p => p.Contains("3 or more arms"));
}
