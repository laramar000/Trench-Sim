using Roster;
using static Roster.Tests.TestFaction;

namespace Roster.Tests;

public class FactionFormatTests
{
    private static void Refused(string from, string to, string expected)
    {
        Assert.Contains(from, FactionJson);
        var loaded = Faction.Parse(FactionJson.Replace(from, to), Kit, Vocabulary);
        Assert.False(loaded.Ok);
        Assert.Contains(loaded.Problems, p => p.Contains(expected));
    }

    [Fact]
    public void TheTestFactionReadsEveryField()
    {
        var f = Testers;
        Assert.Equal(("testers", "The Testers", Alignment.Faithful, 700), (f.Id, f.Name, f.Alignment, f.BudgetDucats));
        Assert.Equal(6, f.Models.Count);
        var captain = f.Models[0];
        Assert.Equal((1, (int?)1, 6, "infantry", (int?)0, (int?)0, 0, 25, (int?)null), (captain.Min, captain.Max, captain.MovementInches, captain.MovementType, captain.Ranged, captain.Melee, captain.Armour, captain.BaseWidthMm, captain.BaseLengthMm));
        Assert.True(captain.HasKeyword("ELITE"));
        Assert.Equal(KitAccess.Listed, f.Models.Single(m => m.Id == "brute").Access);
        Assert.Equal(["pistol"], f.Models.Single(m => m.Id == "drone").StartsWith);
        var trooper = f.Models.Single(m => m.Id == "trooper");
        Assert.Equal(new Cost(5, Currency.Ducats), trooper.Prices.Single().Cost);
        Assert.Equal(2, trooper.Upgrades.Single().MaxPerWarband);
        var rifle = f.Armoury.Single(a => a.Kit == "rifle");
        Assert.Equal(new HashSet<Stipulation> { Stipulation.BayonetLug, Stipulation.ShieldCombo }, rifle.Stipulations);
        Assert.Equal([new OnlyFor("ELITE", null), new OnlyFor(null, "brute")], f.Armoury.Single(a => a.Kit == "plate").Only);
        Assert.Single(f.Variants);
        var raiders = f.As("raiders")!.Value;
        Assert.DoesNotContain(raiders.Models, m => m.Id == "scout");
        Assert.Equal(new Cost(25, Currency.Ducats), raiders.Models.Single(m => m.Id == "trooper").Cost);
        Assert.Equal(new Cost(2, Currency.Ducats), raiders.Armoury.Single(a => a.Kit == "knife").Cost);
        Assert.DoesNotContain(raiders.Armoury, a => a.Kit == "smoke");
    }

    [Theory]
    [InlineData("\"kit\": \"coat\"", "\"kit\": \"cloak\"", "battlekit \"cloak\" is not in the catalogue")]
    [InlineData("{ \"kit\": \"pistol\", \"cost\": { \"amount\": 6, \"currency\": \"ducats\" }, \"status\": \"verified\" }", "{ \"kit\": \"coat\", \"cost\": { \"amount\": 6, \"currency\": \"ducats\" }, \"status\": \"verified\" }", "listed twice in the same armoury")]
    [InlineData("\"stipulations\": [ \"headgear\" ]", "\"stipulations\": [ \"hat\" ]", "unknown stipulation \"hat\"")]
    [InlineData("\"bayonetLug\", \"shieldCombo\"", "\"bayonetLug\", \"bayonetLug\"", "stipulation \"bayonetLug\" is listed twice")]
    [InlineData("\"kit\": \"coat\", \"cost\": { \"amount\": 15, \"currency\": \"ducats\" }", "\"kit\": \"coat\", \"cost\": { \"amount\": 15, \"currency\": \"ducats\" }, \"stipulations\": [ \"bayonetLug\" ]", "only a ranged weapon can have a bayonet lug")]
    [InlineData("{ \"keyword\": \"ELITE\" }", "{ \"keyword\": \"BOSS\" }", "unknown keyword \"BOSS\"")]
    [InlineData("{ \"keyword\": \"ELITE\" }", "{ \"keyword\": \"ELITE\", \"model\": \"brute\" }", "exactly one of \"keyword\" or \"model\"")]
    [InlineData("\"kit\": \"mystery\", \"cost\": null, \"status\": \"unverified\"", "\"kit\": \"mystery\", \"cost\": null, \"status\": \"verified\"", "a verified armoury item needs its cost")]
    [InlineData("\"limit\": 2", "\"limit\": 0", "limits must be 1 or more")]
    public void BadArmouryItemsAreRefused(string from, string to, string expected) => Refused(from, to, expected);

    [Theory]
    [InlineData("\"id\": \"scout\", \"name\": \"Scout\"", "\"id\": \"scout\", \"name\": \"Captain\"", "the name \"Captain\" is used by another model")]
    [InlineData("\"id\": \"scout\", \"name\": \"Scout\"", "\"id\": \"captain\", \"name\": \"Scout\"", "the id is used twice")]
    [InlineData("\"min\": 1, \"max\": 1", "\"min\": 2, \"max\": 1", "\"min\" is more than \"max\"")]
    [InlineData("\"min\": 1, \"max\": 1", "\"min\": 0, \"max\": 0", "\"max\" of 0")]
    [InlineData("\"baseMm\": [25], \"kitAccess\": \"armoury\", \"min\": 1", "\"baseMm\": [50, 25], \"kitAccess\": \"armoury\", \"min\": 1", "the smaller number first")]
    [InlineData("\"baseMm\": [25], \"kitAccess\": \"armoury\", \"min\": 1", "\"baseMm\": [], \"kitAccess\": \"armoury\", \"min\": 1", "\"baseMm\" must be")]
    [InlineData("\"ranged\": 0, \"melee\": 0, \"armour\": 0, \"baseMm\": [25], \"kitAccess\": \"armoury\", \"min\": 1", "\"ranged\": 4, \"melee\": 0, \"armour\": 0, \"baseMm\": [25], \"kitAccess\": \"armoury\", \"min\": 1", "from -3 to +3, or null")]
    [InlineData("\"ranged\": 0, \"melee\": 0, \"armour\": 0, \"baseMm\": [25], \"kitAccess\": \"armoury\", \"min\": 1", "\"melee\": 0, \"armour\": 0, \"baseMm\": [25], \"kitAccess\": \"armoury\", \"min\": 1", "\"ranged\" is missing (write null for \"-\")")]
    [InlineData("\"ranged\": 0, \"melee\": 0, \"armour\": 0, \"baseMm\": [25], \"kitAccess\": \"armoury\", \"min\": 1", "\"ranged\": 0, \"melee\": 0, \"armour\": 1, \"baseMm\": [25], \"kitAccess\": \"armoury\", \"min\": 1", "\"armour\" must be between -3 and 0")]
    [InlineData("\"kitAccess\": \"listed\"", "\"kitAccess\": \"armoury\"", "\"listed\" is only used when")]
    [InlineData("\"listed\": [ \"maul\", \"great-pike\", \"knife\", \"coat\" ]", "\"listed\": [ \"maul\", \"great-pike\", \"knife\", \"coat\", \"relic\", \"cloak\" ]", "battlekit \"cloak\" is not in the catalogue")]
    [InlineData("\"startsWith\": [ \"pistol\" ]", "\"startsWith\": [ \"blaster\" ]", "battlekit \"blaster\" is not in the catalogue")]
    [InlineData("\"maxPerWarband\": 2", "\"maxPerWarband\": 0", "\"maxPerWarband\" must be 1 or more")]
    [InlineData("{ \"id\": \"INFILTRATOR\" }", "{ \"id\": \"SNEAKY\" }", "unknown keyword \"SNEAKY\"")]
    [InlineData("\"kitAccess\": \"none\"", "\"kitAccess\": \"some\"", "\"kitAccess\" is \"some\"")]
    [InlineData("\"alignment\": \"faithful\"", "\"alignment\": \"neutral\"", "\"alignment\" is \"neutral\"")]
    [InlineData("\"budgetDucats\": 700", "\"budgetDucats\": 0", "\"budgetDucats\" must be 1 or more")]
    public void BadModelsAndHeadersAreRefused(string from, string to, string expected) => Refused(from, to, expected);

    [Theory]
    [InlineData("\"removeModels\": [ \"scout\" ]", "\"removeModels\": [ \"wizard\" ]", "removes model \"wizard\", which the faction does not have")]
    [InlineData("\"removeArmoury\": [ \"smoke\" ]", "\"removeArmoury\": [ \"cloak\" ]", "removes \"cloak\" from the armoury")]
    [InlineData("{ \"model\": \"trooper\"", "{ \"model\": \"scout\"", "changes the cost of \"scout\"")]
    [InlineData("\"name\": \"Raider\"", "\"name\": \"Raider\"", "")]
    public void BadVariantsAreRefused(string from, string to, string expected)
    {
        if (expected == "")
        {
            Assert.True(Faction.Parse(FactionJson, Kit, Vocabulary).Ok);
            return;
        }
        Refused(from, to, expected);
    }

    [Fact]
    public void AVariantCannotAddAModelTheFactionAlreadyHas() =>
        Refused("\"id\": \"raider\", \"name\": \"Raider\"", "\"id\": \"trooper\", \"name\": \"Raider\"", "adds model \"trooper\", which the faction already has");

    [Fact]
    public void AListedItemNeedsAPrice() =>
        Refused("{ \"kit\": \"coat\", \"cost\": { \"amount\": 15, \"currency\": \"ducats\" }, \"status\": \"verified\" },", "",
            "lists \"coat\", which has no price");

    [Fact]
    public void ARosterRoundTripsThroughJson()
    {
        var roster = new WarbandRoster("Mud Rats", "testers", "raiders", 700, 6,
            [new RosterModel("captain", "Old Bill", ["pistol", "knife"], []), new RosterModel("trooper", null, [], ["sharpshooter"])]);
        var again = WarbandRoster.Parse(roster.ToJson());
        Assert.True(again.Ok, string.Join("\n", again.Problems));
        Assert.Equal(roster.ToJson(), again.Value!.ToJson());
        Assert.Equal("Old Bill", again.Value.Models[0].Name);
        Assert.Equal(["sharpshooter"], again.Value.Models[1].Upgrades);
    }

    [Theory]
    [InlineData("""{ "format": 1, "rulesVersion": "1.0.2", "name": "X", "faction": "testers", "budgetDucats": -1, "models": [] }""", "a budget cannot be negative")]
    [InlineData("""{ "format": 1, "rulesVersion": "1.0.2", "name": "X", "budgetDucats": 700, "models": [] }""", "\"faction\" is missing")]
    [InlineData("""{ "format": 1, "rulesVersion": "1.0.2", "name": "X", "faction": "testers", "budgetDucats": 700, "models": [ { "kit": [] } ] }""", "\"entry\" is missing")]
    [InlineData("""{ "format": 1, "rulesVersion": "1.6", "name": "X", "faction": "testers", "budgetDucats": 700, "models": [] }""", "supports 1.0.2 only")]
    public void BadRosterFilesAreRefused(string json, string expected)
    {
        var loaded = WarbandRoster.Parse(json);
        Assert.False(loaded.Ok);
        Assert.Contains(loaded.Problems, p => p.Contains(expected));
    }
}
