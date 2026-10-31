using Roster;

namespace Roster.Tests;

// Stage 4: the official data shipped in content/roster loads with no problems. The values themselves are checked
// against their pages when entered; these tests pin a sample of them so an accidental edit shows up.
public class ShippedDataTests
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "content", "roster");
    private static readonly KeywordVocabulary Vocabulary = KeywordVocabulary.Load(KeywordVocabulary.DefaultPath()).Value!;

    private static Catalogue LoadCatalogue()
    {
        var loaded = Catalogue.Load(Path.Combine(Folder, "catalogue.json"), Vocabulary);
        Assert.True(loaded.Ok, string.Join("\n", loaded.Problems));
        return loaded.Value!;
    }

    public static TheoryData<string> FactionFiles()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(Path.Combine(Folder, "factions"), "*.json").Order())
            data.Add(Path.GetFileName(file));
        return data;
    }

    private static Faction LoadFaction(string file)
    {
        var loaded = Faction.Load(Path.Combine(Folder, "factions", file), LoadCatalogue(), Vocabulary);
        Assert.True(loaded.Ok, string.Join("\n", loaded.Problems));
        return loaded.Value!;
    }

    [Fact]
    public void TheCatalogueLoads() => LoadCatalogue();

    [Theory]
    [MemberData(nameof(FactionFiles))]
    public void EveryFactionListLoads(string file) => LoadFaction(file);

    [Theory]
    [MemberData(nameof(FactionFiles))]
    public void AnEmptyRosterForEveryListNamesWhatItMustInclude(string file)
    {
        var faction = LoadFaction(file);
        foreach (var variant in faction.Variants.Select(v => (string?)v.Id).Prepend(null))
        {
            var check = RosterValidator.Check(new WarbandRoster("Test", faction.Id, variant, 700, 0, []), faction, LoadCatalogue());
            Assert.Equal(0, check.Ducats);
            Assert.All(check.Problems, p => Assert.Contains("must include", p));
        }
    }

    [Fact]
    public void ACorePieceKeepsItsProfile()
    {
        var mg = LoadCatalogue().Find("machine-gun")!;
        Assert.Equal((KitCategory.Ranged, 2, 36), (mg.Category, mg.Hands, mg.RangeInches));
        Assert.Equal(new[] { new KeywordUse("AUTOMATIC", 3, null), new KeywordUse("HEAVY", null, null), new KeywordUse("RELOAD", null, null) }, mg.Keywords);
        Assert.Equal(72, mg.Source.Page);
    }

    [Fact]
    public void ThePrincipalityKeepsItsBookValues()
    {
        var faction = LoadFaction("principality.json");
        var lieutenant = faction.Models.Single(m => m.Id == "lieutenant");
        Assert.Equal((new Cost(70, Currency.Ducats), 1, 1, 2, 2, 32), (lieutenant.Cost, lieutenant.Min, lieutenant.Max, lieutenant.Ranged, lieutenant.Melee, lieutenant.BaseWidthMm));
        var mg = faction.Armoury.Single(a => a.Kit == "machine-gun");
        Assert.Equal((new Cost(50, Currency.Ducats), (int?)2), (mg.Cost, mg.Limit));
        Assert.Equal(5, faction.Variants.Count);
        var papal = faction.Variants.Single(v => v.Id == "pontifical-task-force");
        Assert.Equal((500, 11), (papal.BudgetDucats, papal.BudgetGlory));
    }
    [Fact]
    public void ThePilgrimsKeepTheirBookValues()
    {
        var faction = LoadFaction("pilgrims.json");
        var walker = faction.Models.Single(m => m.Id == "reliquary-walker");
        Assert.Equal((new Cost(140, Currency.Ducats), (int?)null, 2, -3, 60), (walker.Cost, walker.Ranged, walker.Melee, walker.Armour, walker.BaseWidthMm));
        Assert.Equal(KitAccess.None, walker.Access);
        var nun = faction.Models.Single(m => m.Id == "stigmatic-nun");
        Assert.Equal((8, 4), (nun.MovementInches, nun.Max));
        Assert.Equal(new Cost(2, Currency.Glory), faction.Armoury.Single(a => a.Kit == "machine-gun").Cost);
        Assert.Equal(3, faction.Variants.Count);
        var cavalcade = faction.Variants.Single(v => v.Id == "lambsblood-cavalcade");
        Assert.Contains(cavalcade.ModelCosts, c => c.Kit == "sanctified-giant" && c.Cost == new Cost(3, Currency.Glory));
    }

    [Fact]
    public void AStigmaticNunCannotCarryARifle()
    {
        var catalogue = LoadCatalogue();
        var faction = LoadFaction("pilgrims.json");
        var roster = new WarbandRoster("Test", faction.Id, null, 700, 0,
            [new RosterModel("war-prophet", null, [], []), new RosterModel("stigmatic-nun", null, ["bolt-action-rifle"], [])]);
        var check = RosterValidator.Check(roster, faction, catalogue);
        Assert.Contains(check.Problems, p => p.Contains("Bolt-Action Rifle"));
    }
    [Fact]
    public void TheSultanateKeepsItsBookValues()
    {
        var faction = LoadFaction("sultanate.json");
        var bull = faction.Models.Single(m => m.Id == "brazen-bull");
        Assert.Equal((new Cost(100, Currency.Ducats), 0, 2, 60, "HEAVY"), (bull.Cost, bull.Ranged, bull.Melee, bull.BaseWidthMm, bull.KitRules?.WeaponsNeedKeyword));
        Assert.Equal((new Cost(35, Currency.Ducats), (int?)2), (faction.Armoury.Single(a => a.Kit == "sniper-rifle").Cost, faction.Armoury.Single(a => a.Kit == "sniper-rifle").Limit));
        Assert.Equal(3, faction.Variants.Count);
        var cabal = faction.As("cabal-of-the-mountain")!.Value;
        Assert.Equal(new Cost(95, Currency.Ducats), cabal.Models.Single(m => m.Id == "master-assassin").Cost);
        Assert.DoesNotContain(cabal.Models, m => m.Id == "yuzbasi");
    }

    [Fact]
    public void TheHereticsKeepTheirBookValues()
    {
        var faction = LoadFaction("heretics.json");
        var beast = faction.Models.Single(m => m.Id == "assault-beast");
        Assert.Equal((new Cost(145, Currency.Ducats), 8, -3, 50), (beast.Cost, beast.MovementInches, beast.Armour, beast.BaseWidthMm));
        var wretched = faction.Models.Single(m => m.Id == "wretched");
        Assert.Equal(((int?)10, (bool?)true), (wretched.KitRules?.MaxCostDucats, wretched.KitRules?.MustHaveWeapon));
        var greed = faction.As("gilded-knights")!.Value;
        Assert.DoesNotContain(greed.Armoury, a => a.Kit is "flamethrower" or "frag-grenades" or "incendiary-ammunition");
        Assert.Contains(greed.Armoury, a => a.Kit == "grenade-launcher");
    }

    [Fact]
    public void TheCultAndTheCourtKeepTheirBookValues()
    {
        var cult = LoadFaction("cult.json");
        var lord = cult.Models.Single(m => m.Id == "bloated-lord");
        Assert.Equal((new Cost(130, Currency.Ducats), 1, 4, 50), (lord.Cost, lord.Ranged, lord.Melee, lord.BaseWidthMm));
        var hound = cult.Models.Single(m => m.Id == "plague-hound");
        Assert.Equal((30, (int?)60), (hound.BaseWidthMm, hound.BaseLengthMm));
        var court = LoadFaction("court.json");
        var praetor = court.Models.Single(m => m.Id == "praetor");
        Assert.Equal((new Cost(115, Currency.Ducats), 8, 3, 3), (praetor.Cost, praetor.MovementInches, praetor.Ranged, praetor.Melee));
        Assert.Equal(7, court.Variants.Count);
        var wrath = court.As("dedicated-to-wrath")!.Value;
        Assert.Contains(wrath.Models.Single(m => m.Id == "praetor").Upgrades, u => u.Id == "blind-rage");
        Assert.DoesNotContain(praetor.Upgrades, u => u.Id == "blind-rage");
        Assert.DoesNotContain(praetor.Upgrades, u => u.Id == "burning-inferno"); // Sorcerers only
    }

    [Fact]
    public void ABloatedLordNeedsNoPlagueKnight()
    {
        var catalogue = LoadCatalogue();
        var cult = LoadFaction("cult.json");
        var check = RosterValidator.Check(new WarbandRoster("Test", cult.Id, null, 700, 0, [new RosterModel("bloated-lord", null, [], [])]), cult, catalogue);
        Assert.True(check.Legal, string.Join("; ", check.Problems));
    }
}
