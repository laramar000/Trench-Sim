using Roster;
using static Roster.Tests.TestFaction;

namespace Roster.Tests;

public class RosterValidatorTests
{
    private static RosterCheck Check(params RosterModel[] models) => RosterValidator.Check(Band(models), Testers, Kit);

    private static void Legal(params RosterModel[] models)
    {
        var check = Check(models);
        Assert.True(check.Legal, string.Join("\n", check.Problems));
    }

    private static void Refused(string expected, params RosterModel[] models)
    {
        var check = Check(models);
        Assert.False(check.Legal);
        Assert.Contains(check.Problems, p => p.Contains(expected));
    }

    [Fact]
    public void ALegalRosterIsTotalledFromModelsKitAndUpgrades()
    {
        var check = Check(
            M("captain", "pistol", "knife", "coat", "helmet", "frag"),
            M("trooper", "rifle", "bayonet"),
            new RosterModel("trooper", "Sharp", ["rifle"], ["sharpshooter"]),
            M("drone"));
        Assert.True(check.Legal, string.Join("\n", check.Problems));
        // captain 60 + 6 + 1 + 15 + 5 + 7; trooper 20 + rifle at its own price 5 + bayonet 2; trooper 20 + 5 + 10; drone 30.
        Assert.Equal(60 + 6 + 1 + 15 + 5 + 7 + 20 + 5 + 2 + 20 + 5 + 10 + 30, check.Ducats);
        Assert.Equal(0, check.Glory);
        Assert.Contains(check.Notes, n => n.Contains("a third needs a bigger warband"));
    }

    [Fact]
    public void TheRequiredLeaderAndModelLimitsAreChecked()
    {
        Refused("must include 1 Captain", M("trooper"));
        Refused("at most 1 Captain (it has 2)", M("captain"), M("captain"));
    }

    [Fact]
    public void NoMoreThanSixEliteModels()
    {
        Legal(M("captain"), M("scout"), M("scout"), M("scout"), M("scout"), M("scout"));
        Refused("more than 6 ELITE models (it has 7)", M("captain"), M("scout"), M("scout"), M("scout"), M("scout"), M("scout"), M("scout"));
    }

    [Fact]
    public void TheBudgetIsChecked()
    {
        var models = new List<RosterModel> { M("captain") };
        models.AddRange(Enumerable.Repeat(M("trooper"), 32)); // 60 + 32 x 20 = 700
        Assert.True(RosterValidator.Check(Band([.. models]), Testers, Kit).Legal);
        models.Add(M("trooper"));
        var check = RosterValidator.Check(Band([.. models]), Testers, Kit);
        Assert.Contains("the warband costs 720 ducats, 20 more than its 700", check.Problems);
    }

    [Fact]
    public void GloryIsSpentFromItsOwnBudget()
    {
        Refused("costs 1 Glory Points, 1 more than its 0", M("captain", "relic"));
        var check = RosterValidator.Check(Band(M("captain", "relic")) with { BudgetGlory = 6 }, Testers, Kit);
        Assert.True(check.Legal);
        Assert.Equal(1, check.Glory);
        Assert.Contains(check.Notes, n => n.Contains("campaign-only"));
    }

    [Fact]
    public void AnUnresolvedCostCannotBeTaken()
    {
        Refused("Test Mystery has no verified cost yet", M("captain", "mystery"));
        Refused("Ghost has no verified cost yet", M("captain"), M("ghost"));
    }

    [Fact]
    public void HandLimitsFollowPage69()
    {
        Legal(M("captain", "pistol", "pistol"));
        Refused("one 2-handed or two 1-handed ranged weapons", M("captain", "rifle", "pistol"));
        Legal(M("captain", "knife", "knife"));
        Refused("one 2-handed or two 1-handed melee weapons", M("captain", "maul", "knife"));
    }

    [Fact]
    public void OneGrenadeTypeOneArmourOneShieldAndNoDuplicateEquipment()
    {
        Refused("only one type of grenade", M("captain", "frag", "smoke"));
        Refused("only one suit of armour", M("captain", "coat", "plate"));
        Refused("only one shield", M("captain", "shield", "shield"));
        Refused("cannot have two pieces of Test Helmet", M("captain", "helmet", "helmet"));
        Refused("only one piece of Headgear", M("captain", "helmet", "mask"));
    }

    [Fact]
    public void ShieldsLeaveOneHandForEachKindOfWeapon()
    {
        Legal(M("captain", "shield", "pistol", "knife"));
        Refused("at most one 1-handed ranged and one 1-handed melee", M("captain", "shield", "knife", "knife"));
        Legal(M("captain", "shield", "rifle", "bayonet"));
        Refused("cannot carry Test Maul with Test Shield; both need Shield Combo", M("captain", "shield", "maul"));
    }

    [Fact]
    public void ABayonetNeedsARifleWithALug()
    {
        Refused("Test Bayonet needs a ranged weapon with a Bayonet Lug", M("captain", "pistol", "bayonet"));
        Legal(M("captain", "rifle", "bayonet"));
    }

    [Fact]
    public void StrongEasesOneTwoHandedMeleeWeaponButNotACumbersomeOne()
    {
        Legal(M("captain"), M("brute", "maul", "knife"));
        Refused("one 2-handed or two 1-handed melee weapons", M("captain"), M("brute", "great-pike", "knife"));
        Refused("one 2-handed or two 1-handed melee weapons", M("captain"), M("brute", "maul", "maul"));
    }

    [Fact]
    public void OnlyStipulationsAndModelKitAccessAreChecked()
    {
        Legal(M("captain", "plate"));
        Refused("Test Plate is only for ELITE or Brute", M("captain"), M("trooper", "plate"));
        Refused("Test Plate is not available to Brute", M("captain"), M("brute", "plate"));
        Refused("Drone cannot be given battlekit", M("captain"), M("drone", "knife"));
        Refused("Test Pistol is not available to Brute", M("captain"), M("brute", "pistol"));
    }

    [Fact]
    public void WarbandAndPerModelLimitsAreChecked()
    {
        Legal(M("captain", "maul"), M("trooper", "maul"));
        Refused("Test Maul has Limit: 2 for the whole warband (it has 3)", M("captain", "maul"), M("trooper", "maul"), M("trooper", "maul"));
        Refused("at most 1 Test Frag", M("captain", "frag", "frag"));
    }

    [Fact]
    public void UpgradesAreBoughtOnceAndWithinTheirWarbandLimit()
    {
        var sharp = new RosterModel("trooper", null, [], ["sharpshooter"]);
        Legal(M("captain"), sharp, sharp);
        Refused("at most 2 Trooper can have Sharpshooter (it has 3)", M("captain"), sharp, sharp, sharp);
        Refused("Sharpshooter can be bought only once", M("captain"), new RosterModel("trooper", null, [], ["sharpshooter", "sharpshooter"]));
        Refused("Trooper has no upgrade \"nimble\"", M("captain"), new RosterModel("trooper", null, [], ["nimble"]));
    }

    [Fact]
    public void AVariantChangesModelsCostsAndArmoury()
    {
        var raiders = Band(M("captain", "knife"), M("raider"), M("trooper")) with { Variant = "raiders" };
        var check = RosterValidator.Check(raiders, Testers, Kit);
        Assert.True(check.Legal, string.Join("\n", check.Problems));
        Assert.Equal(60 + 2 + 35 + 25, check.Ducats);

        Assert.Contains("The Raiders has no warband entry \"scout\"",
            RosterValidator.Check(Band(M("captain"), M("scout")) with { Variant = "raiders" }, Testers, Kit).Problems.Select(p => p[(p.IndexOf(':') + 2)..]));
        Assert.Contains(RosterValidator.Check(Band(M("captain", "smoke")) with { Variant = "raiders" }, Testers, Kit).Problems,
            p => p.Contains("Test Smoke is not available to Captain in The Raiders"));
        Assert.Contains(RosterValidator.Check(Band(M("captain")) with { Variant = "pirates" }, Testers, Kit).Problems,
            p => p.Contains("has no variant \"pirates\""));
    }

    [Fact]
    public void UnknownEntriesAndKitAreReportedInWords()
    {
        Refused("model 2: The Testers has no warband entry \"wizard\"", M("captain"), M("wizard"));
        Refused("battlekit \"laser\" is not in the catalogue", M("captain", "laser"));
        var named = RosterValidator.Check(Band(new RosterModel("captain", "Old Bill", ["maul", "knife", "knife"], [])), Testers, Kit);
        Assert.Contains(named.Problems, p => p.StartsWith("model 1 (Old Bill), Captain:"));
    }
}
