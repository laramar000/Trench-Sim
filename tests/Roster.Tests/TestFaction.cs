using Roster;

namespace Roster.Tests;

// An invented catalogue and faction for the stage 2 tests. None of it comes from any rulebook.
internal static class TestFaction
{
    public static readonly KeywordVocabulary Vocabulary = KeywordVocabulary.Load(KeywordVocabulary.DefaultPath()).Value!;

    private static string Entry(string id, string name, string category, string extra = "") =>
        $$"""{ "id": "{{id}}", "name": "{{name}}", "category": "{{category}}"{{extra}}, "source": { "book": "test", "version": "1.0.2", "page": 1 }, "status": "verified", "naming": "keep", "cost": { "amount": 1, "currency": "ducats" } }""";

    public static readonly string CatalogueJson = $$"""
        { "format": 1, "rulesVersion": "1.0.2", "entries": [
          {{Entry("rifle", "Test Rifle", "ranged", ", \"hands\": 2, \"rangeInches\": 24")}},
          {{Entry("pistol", "Test Pistol", "ranged", ", \"hands\": 1, \"rangeInches\": 12, \"alsoMelee\": true")}},
          {{Entry("bayonet", "Test Bayonet", "melee", ", \"hands\": 2, \"keywords\": [ { \"id\": \"CUMBERSOME\" } ]")}},
          {{Entry("maul", "Test Maul", "melee", ", \"hands\": 2")}},
          {{Entry("great-pike", "Test Pike", "melee", ", \"hands\": 2, \"keywords\": [ { \"id\": \"CUMBERSOME\" } ]")}},
          {{Entry("knife", "Test Knife", "melee", ", \"hands\": 1")}},
          {{Entry("shield", "Test Shield", "shield", ", \"injuryModifier\": -1")}},
          {{Entry("coat", "Test Coat", "armour", ", \"injuryModifier\": -1")}},
          {{Entry("plate", "Test Plate", "armour", ", \"injuryModifier\": -2")}},
          {{Entry("helmet", "Test Helmet", "equipment")}},
          {{Entry("mask", "Test Mask", "equipment")}},
          {{Entry("frag", "Test Frag", "grenade", ", \"rangeInches\": 8")}},
          {{Entry("smoke", "Test Smoke", "grenade", ", \"rangeInches\": 6")}},
          {{Entry("relic", "Test Relic", "equipment", ", \"campaignOnly\": true")}},
          { "id": "mystery", "name": "Test Mystery", "category": "equipment", "cost": null, "source": { "book": "test", "version": "1.0.2", "page": 2 }, "status": "unverified", "naming": "review" }
        ] }
        """;

    public static readonly Catalogue Kit = Catalogue.Parse(CatalogueJson, Vocabulary).Value!;

    public static string Model(string id, string name, string cost, string extra = "", string access = "armoury") =>
        $$"""{ "id": "{{id}}", "name": "{{name}}", "cost": {{cost}}, "movementInches": 6, "movementType": "infantry", "ranged": 0, "melee": 0, "armour": 0, "baseMm": [25], "kitAccess": "{{access}}"{{extra}}, "source": { "book": "test", "version": "1.0.2", "page": 5 }, "status": "verified", "naming": "keep" }""";

    private static string D(int n) => $$"""{ "amount": {{n}}, "currency": "ducats" }""";

    private static string Item(string kit, int cost, string extra = "") =>
        $$"""{ "kit": "{{kit}}", "cost": {{D(cost)}}{{extra}}, "status": "verified" }""";

    public static readonly string FactionJson = $$"""
        { "format": 1, "rulesVersion": "1.0.2", "id": "testers", "name": "The Testers", "alignment": "faithful", "budgetDucats": 700,
          "source": { "book": "test", "version": "1.0.2", "page": 4 }, "status": "verified", "naming": "keep",
          "specialRules": [ { "name": "Test Rule", "summary": "Invented.", "page": 4 } ],
          "models": [
            {{Model("captain", "Captain", D(60), ", \"min\": 1, \"max\": 1, \"keywords\": [ { \"id\": \"ELITE\" }, { \"id\": \"LEADER\" } ]")}},
            {{Model("scout", "Scout", D(40), ", \"max\": 6, \"keywords\": [ { \"id\": \"ELITE\" } ]")}},
            {{Model("trooper", "Trooper", D(20), ", \"prices\": [ { \"kit\": \"rifle\", \"cost\": " + D(5) + " } ], \"upgrades\": [ { \"id\": \"sharpshooter\", \"name\": \"Sharpshooter\", \"cost\": " + D(10) + ", \"maxPerWarband\": 2, \"keywords\": [ { \"id\": \"INFILTRATOR\" } ], \"note\": \"Invented: a third needs a bigger warband.\" } ]")}},
            {{Model("brute", "Brute", D(50), ", \"keywords\": [ { \"id\": \"STRONG\" } ], \"listed\": [ \"maul\", \"great-pike\", \"knife\", \"coat\" ]", "listed")}},
            {{Model("drone", "Drone", D(30), ", \"startsWith\": [ \"pistol\" ]", "none")}},
            {{Model("ghost", "Ghost", "null", "").Replace("\"status\": \"verified\"", "\"status\": \"unverified\"")}}
          ],
          "armoury": [
            {{Item("rifle", 10, ", \"stipulations\": [ \"bayonetLug\", \"shieldCombo\" ]")}},
            {{Item("pistol", 6)}},
            {{Item("bayonet", 2, ", \"stipulations\": [ \"needsBayonetLug\", \"shieldCombo\" ]")}},
            {{Item("maul", 10, ", \"limit\": 2")}},
            {{Item("great-pike", 7)}},
            {{Item("knife", 1)}},
            {{Item("shield", 10, ", \"stipulations\": [ \"shieldCombo\" ]")}},
            {{Item("coat", 15)}},
            {{Item("plate", 40, ", \"only\": [ { \"keyword\": \"ELITE\" }, { \"model\": \"brute\" } ]")}},
            {{Item("helmet", 5, ", \"stipulations\": [ \"headgear\" ]")}},
            {{Item("mask", 5, ", \"stipulations\": [ \"headgear\" ]")}},
            {{Item("frag", 7, ", \"limitPerModel\": 1")}},
            {{Item("smoke", 5)}},
            { "kit": "relic", "cost": { "amount": 1, "currency": "glory" }, "status": "verified" },
            { "kit": "mystery", "cost": null, "status": "unverified" }
          ],
          "variants": [
            { "id": "raiders", "name": "The Raiders", "removeModels": [ "scout" ],
              "addModels": [ {{Model("raider", "Raider", D(35), ", \"max\": 3")}} ],
              "modelCosts": [ { "model": "trooper", "cost": {{D(25)}} } ],
              "removeArmoury": [ "smoke" ], "addArmoury": [ {{Item("knife", 2)}} ],
              "source": { "book": "test", "version": "1.0.2", "page": 9 }, "status": "verified", "naming": "keep" }
          ] }
        """;

    public static Faction Testers => Faction.Parse(FactionJson, Kit, Vocabulary) is { Ok: true } f
        ? f.Value!
        : throw new InvalidOperationException(string.Join("\n", Faction.Parse(FactionJson, Kit, Vocabulary).Problems));

    public static WarbandRoster Band(params RosterModel[] models) => new("Test band", "testers", null, 700, 0, models);

    public static RosterModel M(string entry, params string[] kit) => new(entry, null, kit, []);
}
