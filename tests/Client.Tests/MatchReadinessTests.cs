using Client.Core;
using Roster;
using Rules.Core.State;

namespace Client.Tests;

// Roster stage 5a: a legal roster becomes the engine's warband content, and what the engine cannot play is named.
public class MatchReadinessTests
{
    private static RosterBuilder Principality(params (string Entry, string[] Kit)[] models)
    {
        var builder = RosterBuilder.Shipped();
        foreach (var (entry, kit) in models)
        {
            builder.Select(builder.Add(entry));
            foreach (var k in kit)
            {
                builder.AddKit(k);
            }
        }
        return builder;
    }

    [Fact]
    public void ALegalRosterBecomesWarbandContentTheEngineLoads()
    {
        var builder = Principality(("lieutenant", ["pistol", "sword-axe", "standard-armour"]), ("yeoman", ["bolt-action-rifle"]));
        var ready = builder.MatchCheck();
        Assert.True(ready.Ok, string.Join("; ", ready.Refused.Concat(ready.RosterProblems)));
        var loaded = Sim.Content.Parse(ready.ToContentJson("my-band", "My Band")).Single();
        Assert.Equal(("my-band", "My Band", 2), (loaded.Id, loaded.Name, loaded.Models.Count));
        var lieutenant = loaded.Models[0].Profile;
        Assert.Equal((12_000, -1, true, true), (lieutenant.RangedRange.Thousandths, lieutenant.Armour, lieutenant.HasMeleeWeapon, lieutenant.RangedAlsoMelee));
        var yeoman = loaded.Models[1].Profile;
        Assert.Equal((24_000, 0, false), (yeoman.RangedRange.Thousandths, yeoman.Armour, yeoman.HasMeleeWeapon));
        Assert.StartsWith("For a match: ready (2 models)", builder.MatchLines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void KitTheEngineCannotPlayIsRefusedByName()
    {
        var builder = Principality(("lieutenant", ["pistol", "frag-grenades"]));
        var ready = builder.MatchCheck();
        Assert.False(ready.Ok);
        Assert.Contains(ready.Refused, r => r.Contains("Frag Grenades", StringComparison.Ordinal) && r.Contains("no grenades", StringComparison.Ordinal));
        Assert.Contains(builder.MatchLines, l => l.StartsWith("✕ Not playable yet:", StringComparison.Ordinal) && l.Contains("Frag Grenades", StringComparison.Ordinal));
    }

    private static RosterBuilder Of(string faction, string entry, params string[] kit)
    {
        var builder = RosterBuilder.Shipped();
        builder.ChooseFaction(faction);
        builder.Select(builder.Add(entry));
        foreach (var k in kit)
        {
            builder.AddKit(k);
        }
        return builder;
    }

    private static ModelProfile Loaded(MatchReady ready) => Sim.Content.Parse(ready.ToContentJson("r3", "R3")).Single().Models[0].Profile;

    // Roster stage R3 (U100): two pistols are two ranged weapons, and (no Melee Weapon) both may fight.
    [Fact]
    public void ASecondRangedWeaponIsPlayed()
    {
        var ready = Principality(("lieutenant", ["pistol", "pistol"])).MatchCheck();
        Assert.True(ready.Ok, string.Join("; ", ready.Refused.Concat(ready.RosterProblems)));
        var profile = Loaded(ready);
        Assert.Equal((2, 2, 12_000), (profile.Weapons(AttackKind.Ranged), profile.Weapons(AttackKind.Melee), profile.SecondRangedWeapon!.Range.Thousandths));
        Assert.True(profile.SecondRangedWeapon.AlsoMelee);
        Assert.True(profile.SecondMeleeWeapon!.Keywords.HasFlag(WeaponKeywords.Pistol));
    }

    [Fact]
    public void APistolIsTheOffHandWeaponBesideASword()
    {
        var ready = Principality(("lieutenant", ["pistol", "sword-axe"])).MatchCheck();
        Assert.True(ready.Ok, string.Join("; ", ready.Refused.Concat(ready.RosterProblems)));
        var profile = Loaded(ready);
        Assert.Equal((1, 2), (profile.Weapons(AttackKind.Ranged), profile.Weapons(AttackKind.Melee)));
        Assert.True(profile.MeleeWeaponKeywords.HasFlag(WeaponKeywords.Critical));
        Assert.Equal(WeaponKeywords.Pistol, profile.SecondMeleeWeapon!.Keywords);
        Assert.Equal(0, profile.SecondMeleeWeapon.Range.Thousandths);
        // A Yeoman with a rifle and a knife has one weapon of each kind.
        var plain = Loaded(Principality(("yeoman", ["bolt-action-rifle", "trench-knife"])).MatchCheck());
        Assert.Equal((1, 1), (plain.Weapons(AttackKind.Ranged), plain.Weapons(AttackKind.Melee)));
    }

    [Fact]
    public void CleaveBlockAndImperviousArePlayed()
    {
        var axe = Of("heretics", "heretic-priest", "executioners-axe").MatchCheck();
        Assert.Empty(axe.Refused);
        Assert.Equal(2, Loaded(axe).MeleeWeaponCleave);
        Assert.DoesNotContain(axe.PlayedWithout, w => w.Contains("CLEAVE", StringComparison.Ordinal));

        var polearm = Principality(("lieutenant", ["polearm"])).MatchCheck();
        Assert.True(polearm.Ok, string.Join("; ", polearm.Refused.Concat(polearm.RosterProblems)));
        Assert.True(Loaded(polearm).HasBlock);
        Assert.DoesNotContain(polearm.PlayedWithout, w => w.Contains("BLOCK", StringComparison.Ordinal));

        var armour = Of("sultanate", "yuzbasi", "damascus-armour").MatchCheck();
        Assert.Empty(armour.Refused);
        Assert.Equal((-2, -2), (Loaded(armour).Armour, Loaded(armour).ImperviousArmour));
        Assert.DoesNotContain(armour.PlayedWithout, w => w.Contains("IMPERVIOUS", StringComparison.Ordinal));

        // Plain armour is not IMPERVIOUS, and no new field is written for a model without these.
        var standard = Principality(("lieutenant", ["pistol", "standard-armour"])).MatchCheck();
        Assert.Equal(0, Loaded(standard).ImperviousArmour);
        var json = standard.ToContentJson("plain", "Plain");
        Assert.All(new[] { "secondRangedWeapon", "secondMeleeWeapon", "meleeWeaponCleave", "imperviousArmour" }, f => Assert.DoesNotContain(f, json, StringComparison.Ordinal));
    }

    [Fact]
    public void WhatTheEngineLeavesOutIsNamed()
    {
        var ready = Principality(("lieutenant", ["automatic-rifle"])).MatchCheck();
        Assert.True(ready.Ok);
        Assert.Contains(ready.PlayedWithout, w => w.EndsWith("Automatic Rifle's AUTOMATIC", StringComparison.Ordinal));
        Assert.Contains(ready.PlayedWithout, w => w.Contains("its keywords", StringComparison.Ordinal));
    }

    [Fact]
    public void TheModelsAndWeaponsDiceArePlayed()
    {
        // Roster stage R1: the Lieutenant's Ranged +2 and Melee +2, the Sniper Rifle's +1 DICE, the Great Sword's +1
        // INJURY DICE and the Trench Knife's -1 DICE travel in the content and are no longer listed as played without.
        var builder = Principality(("lieutenant", ["sniper-rifle", "great-sword-axe"]), ("yeoman", ["shotgun", "trench-knife"]));
        var ready = builder.MatchCheck();
        Assert.True(ready.Ok, string.Join("; ", ready.Refused.Concat(ready.RosterProblems)));
        var json = ready.ToContentJson("dice", "Dice");
        var loaded = Sim.Content.Parse(json).Single();
        var lieutenant = loaded.Models[0].Profile;
        Assert.Equal((2, 2, 1, 0, 0, 1), (lieutenant.RangedDice, lieutenant.MeleeDice, lieutenant.RangedWeaponDice,
            lieutenant.RangedWeaponInjuryDice, lieutenant.MeleeWeaponDice, lieutenant.MeleeWeaponInjuryDice));
        Assert.Equal((3, 2), (lieutenant.RangedAttackDice, lieutenant.MeleeAttackDice));
        var yeoman = loaded.Models[1].Profile;
        Assert.Equal((0, 0, 1, -1), (yeoman.RangedDice, yeoman.MeleeDice, yeoman.RangedWeaponDice, yeoman.MeleeWeaponDice));
        Assert.Equal(lieutenant, ContentNames.Parse(json).Single().Models[0].Profile);
        Assert.DoesNotContain(ready.PlayedWithout, w => w.Contains("DICE", StringComparison.Ordinal));
        Assert.DoesNotContain("\"meleeDice\"", Principality(("yeoman", ["bolt-action-rifle"])).MatchCheck().ToContentJson("y", "Y"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheWeaponKeywordsTheEngineKnowsArePlayed()
    {
        // Roster stage R2: the Sniper Rifle's CRITICAL and RISKY, the Great Sword's CRITICAL and the Pistol's PISTOL travel
        // in the content, and since stage R2b the Great Sword's HEAVY too.
        var ready = Principality(("lieutenant", ["sniper-rifle", "great-sword-axe"]), ("lieutenant", ["pistol"])).MatchCheck();
        Assert.Empty(ready.Refused);
        var json = ready.ToContentJson("k", "K");
        var models = Sim.Content.Parse(json).Single().Models;
        Assert.Equal((WeaponKeywords.Critical | WeaponKeywords.Risky, WeaponKeywords.Critical | WeaponKeywords.Heavy), (models[0].Profile.RangedWeaponKeywords, models[0].Profile.MeleeWeaponKeywords));
        Assert.Equal(WeaponKeywords.Pistol, models[1].Profile.RangedWeaponKeywords);
        Assert.Equal(models[0].Profile, ContentNames.Parse(json).Single().Models[0].Profile);
        Assert.DoesNotContain(ready.PlayedWithout, w => new[] { "CRITICAL", "RISKY", "PISTOL", "HEAVY" }.Any(k => w.Contains(k, StringComparison.Ordinal)));
        // Roster stage R2b: the Machine Gun's HEAVY and RELOAD and the Automatic Rifle's ASSAULT are played.
        var guns = Principality(("lieutenant", ["machine-gun"]), ("lieutenant", ["automatic-rifle"])).MatchCheck();
        var gunModels = Sim.Content.Parse(guns.ToContentJson("g", "G")).Single().Models;
        Assert.Equal((WeaponKeywords.Heavy | WeaponKeywords.Reload, WeaponKeywords.Assault),
            (gunModels[0].Profile.RangedWeaponKeywords, gunModels[1].Profile.RangedWeaponKeywords));
    }

    [Fact]
    public void AModelWithRangedDashTakesNoRangedWeaponIntoAMatch()
    {
        // A Plague Thrall's Ranged is "-" (it may not make Ranged Attacks): a ranged weapon written into its roster by
        // hand is refused by name, not played.
        var builder = RosterBuilder.Shipped();
        builder.ChooseFaction("cult");
        var vocabulary = KeywordVocabulary.Parse(ContentNames.Read("roster/keywords.json")).Value!;
        var catalogue = Catalogue.Parse(ContentNames.Read("roster/catalogue.json"), vocabulary).Value!;
        var roster = builder.Roster with { Models = [new RosterModel("plague-thrall", null, ["bolt-action-rifle"], [])] };
        var ready = MatchReadiness.Check(roster, builder.Faction, catalogue);
        Assert.Contains(ready.Refused, r => r.Contains("Ranged is \"-\"", StringComparison.Ordinal));
        Assert.Empty(ready.Models);
    }

    [Fact]
    public void AnOvalBaseIsRefused()
    {
        var builder = RosterBuilder.Shipped();
        builder.ChooseFaction("court");
        builder.Add("praetor");
        builder.Add("pit-locust");
        Assert.Contains(builder.MatchCheck().Refused, r => r.Contains("30x60 mm oval base", StringComparison.Ordinal));
    }

    [Fact]
    public void AnIllegalRosterIsNotReady()
    {
        var builder = Principality(("yeoman", []));
        var ready = builder.MatchCheck();
        Assert.False(ready.Ok);
        Assert.StartsWith("For a match: fix the problems above first.", builder.MatchLines[0], StringComparison.Ordinal);
    }
}
