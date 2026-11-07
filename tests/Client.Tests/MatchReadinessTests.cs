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
        var builder = Principality(("lieutenant", ["pistol", "binoculars"]));
        var ready = builder.MatchCheck();
        Assert.False(ready.Ok);
        Assert.Contains(ready.Refused, r => r.Contains("Binoculars", StringComparison.Ordinal) && r.Contains("no equipment", StringComparison.Ordinal));
        Assert.Contains(builder.MatchLines, l => l.StartsWith("✕ Not playable yet:", StringComparison.Ordinal) && l.Contains("Binoculars", StringComparison.Ordinal));
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
    public void AGrenadeAndNegateArePlayed()
    {
        // Roster stage R4a (U101): Frag Grenades are the model's grenade, with SHRAPNEL, ASSAULT and the two IGNOREs, and
        // (R4b, U102) BLAST 2". A Gas Mask and a Combat Helmet are equipment that is only NEGATE: played.
        var ready = Principality(("lieutenant", ["bolt-action-rifle", "frag-grenades", "gas-mask", "combat-helmet"])).MatchCheck();
        Assert.True(ready.Ok, string.Join("; ", ready.Refused.Concat(ready.RosterProblems)));
        var json = ready.ToContentJson("g", "G");
        var profile = Sim.Content.Parse(json).Single().Models[0].Profile;
        Assert.Equal(profile, ContentNames.Parse(json).Single().Models[0].Profile);
        var grenade = profile.Grenade!;
        Assert.Equal((8_000, WeaponKeywords.Assault | WeaponKeywords.IgnoreCover | WeaponKeywords.IgnoreLongRange | WeaponKeywords.Shrapnel),
            (grenade.Range.Thousandths, grenade.Keywords));
        Assert.Equal(WeaponKeywords.Gas | WeaponKeywords.Shrapnel, profile.Negates);
        Assert.Equal(2, grenade.Blast);
        Assert.DoesNotContain(ready.PlayedWithout, w => w.Contains("BLAST", StringComparison.Ordinal));
        Assert.Contains("\"blast\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"blast\"", Principality(("yeoman", ["bolt-action-rifle"])).MatchCheck().ToContentJson("y", "Y"), StringComparison.Ordinal);
        Assert.DoesNotContain(ready.PlayedWithout, w => w.Contains("Gas Mask", StringComparison.Ordinal) || w.Contains("Combat Helmet", StringComparison.Ordinal) || w.Contains("SHRAPNEL", StringComparison.Ordinal));
        Assert.DoesNotContain("\"grenade\"", Principality(("yeoman", ["bolt-action-rifle"])).MatchCheck().ToContentJson("y", "Y"), StringComparison.Ordinal);
        Assert.DoesNotContain("\"negates\"", Principality(("yeoman", ["bolt-action-rifle"])).MatchCheck().ToContentJson("y", "Y"), StringComparison.Ordinal);
    }

    [Fact]
    public void BlastAndScatterArePlayed()
    {
        // Roster stages R4b and R4c (U102, U103): a Grenade Launcher's BLAST 3" on the gun, a Satchel Charge's BLAST 3" and
        // SCATTER on the grenade; neither is named under "plays without" (its CONSUMABLE still is).
        var ready = Principality(("yeoman", ["grenade-launcher"]), ("lieutenant", ["bolt-action-rifle", "satchel-charge"])).MatchCheck();
        Assert.True(ready.Ok, string.Join("; ", ready.Refused.Concat(ready.RosterProblems)));
        var models = Sim.Content.Parse(ready.ToContentJson("b", "B")).Single().Models;
        Assert.Equal(models.Select(m => m.Profile), ContentNames.Parse(ready.ToContentJson("b", "B")).Single().Models.Select(m => m.Profile));
        Assert.Equal(3, models[0].Profile.RangedWeaponBlast);
        Assert.Equal(WeaponKeywords.None, models[0].Profile.RangedWeaponKeywords & WeaponKeywords.Scatter);
        var profile = models[1].Profile;
        Assert.Equal(0, profile.RangedWeaponBlast);
        Assert.Equal(3, profile.Grenade!.Blast);
        Assert.Equal(WeaponKeywords.Scatter, profile.Grenade.Keywords & WeaponKeywords.Scatter);
        Assert.Equal(WeaponKeywords.Scatter, profile.Using(AttackKind.Ranged, ModelProfile.GrenadeWeapon).RangedWeaponKeywords & WeaponKeywords.Scatter);
        Assert.DoesNotContain(ready.PlayedWithout, w => w.Contains("BLAST", StringComparison.Ordinal) || w.Contains("SCATTER", StringComparison.Ordinal));
        Assert.Contains(ready.PlayedWithout, w => w.Contains("CONSUMABLE", StringComparison.Ordinal));
    }

    [Fact]
    public void DeadlyIsPlayed()
    {
        // Roster stage R5a (U104): the Reliquary Walker's Catherine Wheel is DEADLY; it is played, not named.
        var builder = RosterBuilder.Shipped();
        builder.ChooseFaction("pilgrims");
        builder.Add("war-prophet");
        builder.Add("reliquary-walker");
        var ready = builder.MatchCheck();
        Assert.True(ready.Ok, string.Join("; ", ready.Refused.Concat(ready.RosterProblems)));
        var profile = Sim.Content.Parse(ready.ToContentJson("p", "P")).Single().Models[1].Profile;
        // Its Bonebreaker Mace is the first Melee Weapon, the Catherine Wheel the second.
        Assert.Equal(WeaponKeywords.None, profile.AttackKeywords(AttackKind.Melee) & WeaponKeywords.Deadly);
        Assert.Equal(WeaponKeywords.Deadly, profile.Using(AttackKind.Melee, 1).AttackKeywords(AttackKind.Melee) & WeaponKeywords.Deadly);
        Assert.DoesNotContain(ready.PlayedWithout, w => w.Contains("DEADLY", StringComparison.Ordinal));
    }

    // Roster stage R5b (U106): the model keywords FEAR, NEGATE FEAR, TOUGH, STRONG (NEGATE HEAVY) and LEADER are played.
    [Fact]
    public void ModelKeywordsArePlayedAsTraits()
    {
        var lieutenant = Of("principality", "lieutenant", "pistol");
        var ready = lieutenant.MatchCheck();
        // Roster stage R6a (U117): the faction keyword PRINCIPALITY is played too (Onward Christian Soldiers! looks for it).
        Assert.Equal(ModelTraits.Leader | ModelTraits.Tough | ModelTraits.Principality, Loaded(ready).Traits);
        Assert.Contains(ready.PlayedWithout, w => w.Contains("its keywords (ELITE)", StringComparison.Ordinal));
        Assert.Contains("\"traits\"", ready.ToContentJson("p", "P"), StringComparison.Ordinal);

        Assert.Equal(ModelTraits.NegateFear | ModelTraits.Principality, Loaded(Of("principality", "trench-cleric", "pistol").MatchCheck()).Traits);
        Assert.Equal(ModelTraits.NegateHeavy | ModelTraits.Principality, Loaded(Of("principality", "mechanized-heavy-infantry").MatchCheck()).Traits);
        var bull = Loaded(Of("sultanate", "brazen-bull").MatchCheck());
        Assert.Equal(ModelTraits.Fear | ModelTraits.Tough | ModelTraits.NegateHeavy, bull.Traits);
        // A model with none writes nothing new (a Sultanate model: its faction keyword plays no rule yet).
        var azeb = Of("sultanate", "azeb").MatchCheck();
        Assert.Equal(ModelTraits.None, Loaded(azeb).Traits);
        Assert.DoesNotContain("traits", azeb.ToContentJson("s", "S"), StringComparison.Ordinal);
    }

    // Roster stage R5b (U106): equipment whose rule is a keyword or dice is played in full; the rest is still refused.
    [Fact]
    public void EquipmentThatGivesKeywordsOrDiceIsPlayed()
    {
        static void Played(MatchReady ready, string piece)
        {
            Assert.Empty(ready.Refused);
            Assert.DoesNotContain(ready.PlayedWithout, w => w.Contains(piece, StringComparison.Ordinal));
        }

        var flag = Of("principality", "yeoman", "troop-flag").MatchCheck();
        Played(flag, "Troop Flag");
        Assert.DoesNotContain(flag.RosterProblems, p => p.Contains("HELD", StringComparison.Ordinal));
        Assert.Equal(ModelTraits.Leader | ModelTraits.Principality, Loaded(flag).Traits);

        var relic = Of("heretics", "heretic-trooper", "unholy-relic").MatchCheck();
        Played(relic, "Unholy Relic");
        Assert.Equal(ModelTraits.Fear, Loaded(relic).Traits);

        var capirote = Of("pilgrims", "trench-pilgrim", "iron-capirote").MatchCheck();
        Played(capirote, "Iron Capirote");
        Assert.Equal((ModelTraits.NegateFear | ModelTraits.Pilgrim, WeaponKeywords.Shrapnel), (Loaded(capirote).Traits, Loaded(capirote).Negates));

        var eyes = Of("cult", "corpse-guard", "compound-eyes-helmet").MatchCheck();
        Played(eyes, "Compound Eyes Helmet");
        Assert.Equal((1, 1, WeaponKeywords.Gas | WeaponKeywords.Shrapnel), (Loaded(eyes).RangedDice, Loaded(eyes).MeleeDice, Loaded(eyes).Negates));

        var abyssinian = RosterBuilder.Shipped();
        abyssinian.ChooseVariant("abyssinian-expedition");
        abyssinian.Select(abyssinian.Add("lieutenant"));
        abyssinian.AddKit("anfarro");
        var crowned = abyssinian.MatchCheck();
        Played(crowned, "Anfarro");
        Assert.Equal((2, 3, ModelTraits.Leader | ModelTraits.Tough | ModelTraits.NegateFear | ModelTraits.Principality), (Loaded(crowned).RangedDice, Loaded(crowned).MeleeDice, Loaded(crowned).Traits));

        var monastery = RosterBuilder.Shipped();
        monastery.ChooseFaction("pilgrims");
        monastery.ChooseVariant("mountain-monastery-pilgrimage");
        monastery.Select(monastery.Add("reliquary-walker"));
        monastery.AddKit("sacred-geometry");
        var geometry = monastery.MatchCheck();
        Assert.Empty(geometry.Refused);
        Assert.DoesNotContain(geometry.PlayedWithout, w => w.Contains("Sacred Geometry", StringComparison.Ordinal));
        Assert.Equal(1, Loaded(geometry).RangedDice);

        var cabal = RosterBuilder.Shipped();
        cabal.ChooseFaction("sultanate");
        cabal.ChooseVariant("cabal-of-the-mountain");
        cabal.Select(cabal.Add("azeb"));
        cabal.AddKit("assassins-leaf");
        Played(cabal.MatchCheck(), "Assassin");
        Assert.Equal(ModelTraits.NegateHeavy, Loaded(cabal.MatchCheck()).Traits);

        // The Elixir of Life makes a model TOUGH, but does nothing for an ARTIFICIAL one (it is named).
        var house = RosterBuilder.Shipped();
        house.ChooseFaction("sultanate");
        house.ChooseVariant("house-of-wisdom");
        house.Select(house.Add("azeb"));
        house.AddKit("elixir-of-life");
        Played(house.MatchCheck(), "Elixir");
        Assert.Equal(ModelTraits.Tough, Loaded(house.MatchCheck()).Traits);
        var lion = RosterBuilder.Shipped();
        lion.ChooseFaction("sultanate");
        lion.ChooseVariant("house-of-wisdom");
        lion.Select(lion.Add("alchemical-lion"));
        lion.AddKit("elixir-of-life");
        var artificial = lion.MatchCheck();
        Assert.Empty(artificial.Refused);
        Assert.Contains(artificial.PlayedWithout, w => w.Contains("Elixir of Life (no effect on an ARTIFICIAL model", StringComparison.Ordinal));
        Assert.Equal(ModelTraits.None, Loaded(artificial).Traits);
    }

    // Roster stage R5c (U107): AMMUNITION goes on the one gun that can take it; Holy Smoke, Zealot's Draught and Grovelling
    // Devotees change Injury Rolls.
    [Fact]
    public void AmmunitionIsLoadedIntoItsGun()
    {
        static RosterBuilder Listed(string faction, string? variant, string entry, params string[] kit)
        {
            var b = RosterBuilder.Shipped();
            b.ChooseFaction(faction);
            b.ChooseVariant(variant);
            b.Select(b.Add(entry));
            foreach (var k in kit)
            {
                b.AddKit(k);
            }
            return b;
        }

        // A keyword: the gun gains it; the piece is played, not named.
        var fire = Listed("heretics", null, "heretic-trooper", "bolt-action-rifle", "incendiary-ammunition").MatchCheck();
        Assert.Empty(fire.Refused);
        Assert.DoesNotContain(fire.PlayedWithout, w => w.Contains("Incendiary Ammunition", StringComparison.Ordinal));
        Assert.Equal(WeaponKeywords.Fire, Loaded(fire).RangedWeaponKeywords);
        var piercing = Listed("principality", "hibernian-rangers", "yeoman", "bolt-action-rifle", "armour-piercing-bullets").MatchCheck();
        Assert.Equal(WeaponKeywords.ArmourPiercing, Loaded(piercing).RangedWeaponKeywords);
        var dumDum = Listed("principality", "highland-assault-detachment", "yeoman", "bolt-action-rifle", "dum-dum-bullets").MatchCheck();
        Assert.Equal(WeaponKeywords.Critical, Loaded(dumDum).RangedWeaponKeywords);

        // +1 DICE: Alchemical Ammunition goes on a Jezzail (or an Alaybozan, Halberd-Gun or Siege Jezzail), and on no other gun.
        var jezzail = Listed("sultanate", null, "azeb", "jezzail", "alchemical-ammunition").MatchCheck();
        Assert.Empty(jezzail.Refused);
        Assert.DoesNotContain(jezzail.PlayedWithout, w => w.Contains("Alchemical", StringComparison.Ordinal));
        Assert.Equal(1, Loaded(jezzail).RangedWeaponDice);
        var musket = Listed("sultanate", null, "azeb", "musket", "alchemical-ammunition").MatchCheck();
        Assert.Contains(musket.PlayedWithout, w => w.Contains("Alchemical Ammunition (no ranged weapon it can be used with", StringComparison.Ordinal));
        Assert.Equal(0, Loaded(musket).RangedWeaponDice);

        // Not on a FIRE gun; with two guns that could take it the choice is the player's at deployment: refused.
        var flamer = Listed("heretics", null, "heretic-trooper", "flamethrower", "incendiary-ammunition").MatchCheck();
        Assert.Contains(flamer.PlayedWithout, w => w.Contains("no ranged weapon it can be used with", StringComparison.Ordinal));
        var both = Listed("heretics", null, "heretic-trooper", "pistol", "silenced-pistol", "incendiary-ammunition");
        both.Select(both.Add("heretic-priest"));
        both.AddKit("pistol");
        both.AddKit("silenced-pistol");
        both.AddKit("incendiary-ammunition");
        Assert.Contains(both.MatchCheck().Refused, r => r.Contains("which of its guns it is used for is chosen at deployment", StringComparison.Ordinal));
        // With the flamethrower as the other gun, only the pistol can take it.
        var one = Listed("heretics", null, "heretic-priest", "pistol", "flamethrower", "incendiary-ammunition").MatchCheck();
        Assert.Empty(one.Refused);
        var priest = Loaded(one);
        Assert.Equal(WeaponKeywords.Fire, (priest.RangedAlsoMelee ? priest.RangedWeaponKeywords : priest.SecondRangedWeapon!.Keywords) & WeaponKeywords.Fire);
    }

    [Fact]
    public void SmokeDraughtAndDevoteesArePlayed()
    {
        var cleric = RosterBuilder.Shipped();
        cleric.ChooseVariant("prussian-stormtroopers");
        cleric.Select(cleric.Add("trench-cleric"));
        cleric.AddKit("holy-smoke");
        var smoked = cleric.MatchCheck();
        Assert.Empty(smoked.Refused);
        Assert.DoesNotContain(smoked.PlayedWithout, w => w.Contains("Holy Smoke", StringComparison.Ordinal));
        Assert.Equal((-1, ModelTraits.NegateFear | ModelTraits.Principality), (Loaded(smoked).InjuryDiceTaken, Loaded(smoked).Traits));
        Assert.Contains("\"injuryDiceTaken\": -1", smoked.ToContentJson("p", "P"), StringComparison.Ordinal);

        var pilgrim = Of("pilgrims", "trench-pilgrim", "zealots-draught").MatchCheck();
        Assert.Equal((-1, ModelTraits.NegateFear | ModelTraits.Pilgrim), (Loaded(pilgrim).InjuryDiceTaken, Loaded(pilgrim).Traits));

        var guard = Of("cult", "corpse-guard", "grovelling-devotee", "grovelling-devotee");
        Assert.DoesNotContain(guard.Check().Problems, p => p.Contains("Devotee", StringComparison.Ordinal));
        var followed = guard.MatchCheck();
        Assert.Empty(followed.Refused);
        Assert.Equal(2, Loaded(followed).MeleeInjuryBonus);
        Assert.Equal(1, Loaded(Of("cult", "corpse-guard", "grovelling-devotee").MatchCheck()).MeleeInjuryBonus);
        Assert.Contains(Of("cult", "corpse-guard", "grovelling-devotee", "grovelling-devotee", "grovelling-devotee").Check().Problems, p => p.Contains("at most 2 Grovelling Devotee", StringComparison.Ordinal));
    }

    // Roster stage R5d (U108): the Crucifix, the Mountaineer Kit, the Musical Instrument and the Cloak of the Mountain.
    [Fact]
    public void RiskyAndCoverBattlekitIsPlayed()
    {
        var task = RosterBuilder.Shipped();
        task.ChooseFaction("principality");
        task.ChooseVariant("pontifical-task-force");
        task.Select(task.Add("lieutenant"));
        task.AddKit("pontiffs-crucifix");
        var blessed = task.MatchCheck();
        Assert.Empty(blessed.Refused);
        Assert.DoesNotContain(blessed.PlayedWithout, w => w.Contains("Crucifix", StringComparison.Ordinal));
        Assert.True(Loaded(blessed).KeepsGoingAfterRisky);
        Assert.Contains("\"keepsGoingAfterRisky\": true", blessed.ToContentJson("p", "P"), StringComparison.Ordinal);

        var officer = Of("sultanate", "yuzbasi", "mountaineer-kit", "musical-instrument", "cloak-of-the-mountain");
        Assert.Empty(officer.Check().Problems);
        var ready = officer.MatchCheck();
        Assert.Empty(ready.Refused);
        Assert.DoesNotContain(ready.PlayedWithout, w => w.Contains("Mountaineer", StringComparison.Ordinal) || w.Contains("Musical", StringComparison.Ordinal) || w.Contains("Cloak", StringComparison.Ordinal));
        var profile = Loaded(ready);
        Assert.Equal((false, 1, true, true), (profile.KeepsGoingAfterRisky, profile.ClimbDice, profile.Fanfare, profile.BlendsIn));

        // Without them nothing is written.
        var plain = Of("sultanate", "yuzbasi").MatchCheck().ToContentJson("p", "P");
        foreach (var field in new[] { "keepsGoingAfterRisky", "climbDice", "fanfare", "blendsIn" })
        {
            Assert.DoesNotContain(field, plain, StringComparison.Ordinal);
        }
    }

    // Roster stage R5e (U109): the Urn of the Bitter Ashes and the Sacrificial Lamb.
    [Fact]
    public void UrnAndLambArePlayed()
    {
        var procession = RosterBuilder.Shipped();
        procession.ChooseFaction("cult");
        procession.ChooseVariant("mourning-procession");
        procession.Select(procession.Add("executor"));
        procession.AddKit("standard-armour");
        procession.AddKit("urn-of-the-bitter-ashes");
        var urn = procession.MatchCheck();
        Assert.Empty(urn.Refused);
        Assert.DoesNotContain(urn.PlayedWithout, w => w.Contains("Urn", StringComparison.Ordinal));
        Assert.True(Loaded(urn).SinisterWhispers);
        Assert.Contains("\"sinisterWhispers\": true", urn.ToContentJson("p", "P"), StringComparison.Ordinal);

        var cavalcade = RosterBuilder.Shipped();
        cavalcade.ChooseFaction("pilgrims");
        cavalcade.ChooseVariant("lambsblood-cavalcade");
        cavalcade.Select(cavalcade.Add("trench-pilgrim"));
        cavalcade.AddKit("sacrificial-lamb");
        Assert.DoesNotContain(cavalcade.Check().Problems, p => p.Contains("Sacrificial Lamb", StringComparison.Ordinal));
        var lamb = cavalcade.MatchCheck();
        Assert.Empty(lamb.Refused);
        Assert.DoesNotContain(lamb.PlayedWithout, w => w.Contains("Sacrificial Lamb", StringComparison.Ordinal));
        Assert.True(Loaded(lamb).WardedByBlood);

        var plain = Of("pilgrims", "trench-pilgrim").MatchCheck().ToContentJson("p", "P");
        Assert.DoesNotContain("sinisterWhispers", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("wardedByBlood", plain, StringComparison.Ordinal);
    }

    // Roster stage R5f (U110): a shield's COVER and a Shovel's Dug In.
    [Fact]
    public void CoverFromShieldsAndShovelsIsPlayed()
    {
        var shield = Of("principality", "lieutenant", "machine-armour", "heavy-ballistic-shield").MatchCheck();
        Assert.Empty(shield.Refused);
        Assert.DoesNotContain(shield.PlayedWithout, w => w.Contains("COVER", StringComparison.Ordinal));
        Assert.True(Loaded(shield).HasCover);
        Assert.Contains("\"hasCover\": true", shield.ToContentJson("p", "P"), StringComparison.Ordinal);

        // U111: a model with no other Melee Weapon fights with its Shovel; with one, it is named.
        var digger = Of("principality", "yeoman", "shovel").MatchCheck();
        Assert.Empty(digger.Refused);
        Assert.DoesNotContain(digger.PlayedWithout, w => w.Contains("Shovel", StringComparison.Ordinal));
        Assert.Equal((false, true, true), (Loaded(digger).HasCover, Loaded(digger).DigsIn, Loaded(digger).HasMeleeWeapon));
        Assert.Equal(WeaponKeywords.None, Loaded(digger).MeleeWeaponKeywords);
        var armed = Of("principality", "yeoman", "trench-club", "shovel").MatchCheck();
        Assert.Empty(armed.Refused);
        Assert.Contains("model 1, Yeoman: Shovel used as a 2-handed Melee Weapon (it fights with its other Melee Weapon)", armed.PlayedWithout);
        Assert.True(Loaded(armed).DigsIn);

        // The Bronze Shovel is a Trench Club with its HEAVY (no Charge Bonus) when it is the Melee Weapon.
        var bull = Of("sultanate", "brazen-bull", "bronze-shovel").MatchCheck();
        Assert.Empty(bull.Refused);
        Assert.DoesNotContain(bull.PlayedWithout, w => w.Contains("Bronze Shovel", StringComparison.Ordinal));
        Assert.Equal((true, true), (Loaded(bull).DigsIn, Loaded(bull).HasMeleeWeapon));
        Assert.Equal(WeaponKeywords.Heavy, Loaded(bull).MeleeWeaponKeywords);

        var plain = Of("principality", "yeoman").MatchCheck().ToContentJson("p", "P");
        Assert.DoesNotContain("hasCover", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("digsIn", plain, StringComparison.Ordinal);
    }

    // Roster stage R5g (U112): the Crown of Hellfire and the Damned Pact.
    [Fact]
    public void CrownAndPactArePlayed()
    {
        var crown = Of("court", "hell-knight", "crown-of-hellfire").MatchCheck();
        Assert.True(crown.Refused.Count == 0, string.Join("; ", crown.Refused));
        Assert.DoesNotContain(crown.PlayedWithout, w => w.Contains("Crown", StringComparison.Ordinal));
        Assert.Equal((true, false), (Loaded(crown).BurnsAtActivationEnd, Loaded(crown).BurnsWhenTakenOut));
        Assert.Contains("\"burnsAtActivationEnd\": true", crown.ToContentJson("p", "P"), StringComparison.Ordinal);

        var pact = Of("heretics", "heretic-trooper", "damned-pact").MatchCheck();
        Assert.Empty(pact.Refused);
        Assert.DoesNotContain(pact.PlayedWithout, w => w.Contains("Damned Pact", StringComparison.Ordinal));
        Assert.Equal((false, true), (Loaded(pact).BurnsAtActivationEnd, Loaded(pact).BurnsWhenTakenOut));

        var plain = Of("heretics", "heretic-trooper").MatchCheck().ToContentJson("p", "P");
        Assert.DoesNotContain("burns", plain, StringComparison.Ordinal);
    }

    // Roster stage R5h (U113): a Medi-kit gives the Treat ACTION; Holy Water of Lalibela +1 DICE to it.
    [Fact]
    public void MediKitAndHolyWaterArePlayed()
    {
        var medic = Of("principality", "lieutenant", "medi-kit").MatchCheck();
        Assert.True(medic.Refused.Count == 0, string.Join("; ", medic.Refused));
        Assert.DoesNotContain(medic.PlayedWithout, w => w.Contains("Medi-kit", StringComparison.Ordinal));
        Assert.Equal((true, false), (Loaded(medic).Treats, Loaded(medic).HealingWaters));
        Assert.Contains("\"treats\": true", medic.ToContentJson("p", "P"), StringComparison.Ordinal);

        var abyssinian = RosterBuilder.Shipped();
        abyssinian.ChooseVariant("abyssinian-expedition");
        abyssinian.Select(abyssinian.Add("lieutenant"));
        abyssinian.AddKit("medi-kit");
        abyssinian.AddKit("holy-water-of-lalibela");
        var watered = abyssinian.MatchCheck();
        Assert.True(watered.Refused.Count == 0, string.Join("; ", watered.Refused));
        Assert.DoesNotContain(watered.PlayedWithout, w => w.Contains("Holy Water", StringComparison.Ordinal));
        Assert.Equal((true, true), (Loaded(watered).Treats, Loaded(watered).HealingWaters));

        var plain = Of("principality", "lieutenant").MatchCheck().ToContentJson("p", "P");
        Assert.DoesNotContain("treats", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("healingWaters", plain, StringComparison.Ordinal);
    }

    // Field Shrine (U127): a carrier is played with its shrine to deploy; it is no longer named as played without.
    [Fact]
    public void AFieldShrineIsPlayed()
    {
        var carrier = Of("principality", "lieutenant", "field-shrine").MatchCheck();
        Assert.True(carrier.Refused.Count == 0, string.Join("; ", carrier.Refused));
        Assert.DoesNotContain(carrier.PlayedWithout, w => w.Contains("Field Shrine", StringComparison.Ordinal));
        Assert.True(Loaded(carrier).FieldShrine);
        Assert.Contains("\"fieldShrine\": true", carrier.ToContentJson("p", "P"), StringComparison.Ordinal);
        Assert.DoesNotContain("fieldShrine", Of("principality", "lieutenant").MatchCheck().ToContentJson("p", "P"), StringComparison.Ordinal);
    }

    // Roster stage R5i (U114): Piston Legs give the Grind to Dust ACTION.
    [Fact]
    public void PistonLegsArePlayed()
    {
        var monastery = RosterBuilder.Shipped();
        monastery.ChooseFaction("pilgrims");
        monastery.ChooseVariant("mountain-monastery-pilgrimage");
        monastery.Select(monastery.Add("reliquary-walker"));
        monastery.AddKit("piston-legs");
        var legs = monastery.MatchCheck();
        Assert.True(legs.Refused.Count == 0, string.Join("; ", legs.Refused));
        Assert.DoesNotContain(legs.PlayedWithout, w => w.Contains("Piston Legs", StringComparison.Ordinal));
        Assert.True(Loaded(legs).GrindsToDust);
        Assert.Contains("\"grindsToDust\": true", legs.ToContentJson("p", "P"), StringComparison.Ordinal);
        Assert.DoesNotContain("grindsToDust", Of("pilgrims", "trench-pilgrim").MatchCheck().ToContentJson("p", "P"), StringComparison.Ordinal);
    }

    // Roster stage R5j (U115): the Martyrdom Device upgrade gives the Trigger Martyrdom Device ACTION.
    [Fact]
    public void TheMartyrdomDeviceIsPlayed()
    {
        var builder = RosterBuilder.Shipped();
        builder.ChooseFaction("pilgrims");
        builder.Select(builder.Add("ecclesiastic-prisoner"));
        var plain = builder.MatchCheck();
        Assert.False(Loaded(plain).Martyrdom);
        Assert.DoesNotContain("martyrdom", plain.ToContentJson("p", "P"), StringComparison.Ordinal);
        builder.ToggleUpgrade("martyrdom-device");
        var device = builder.MatchCheck();
        Assert.True(device.Refused.Count == 0, string.Join("; ", device.Refused));
        Assert.DoesNotContain(device.PlayedWithout, w => w.Contains("Martyrdom Device", StringComparison.Ordinal));
        Assert.True(Loaded(device).Martyrdom);
        Assert.Contains("\"martyrdom\": true", device.ToContentJson("p", "P"), StringComparison.Ordinal);
    }

    // Roster stage R5k (U116): a Blessed Icon or an Unholy Trinket is a Talisman, used once per game after a failed Risky roll.
    [Fact]
    public void TalismansArePlayed()
    {
        var icon = Of("pilgrims", "trench-pilgrim", "blessed-icon").MatchCheck();
        Assert.True(icon.Refused.Count == 0, string.Join("; ", icon.Refused));
        Assert.DoesNotContain(icon.PlayedWithout, w => w.Contains("Blessed Icon", StringComparison.Ordinal));
        Assert.Equal(1, Loaded(icon).Talismans);
        Assert.Contains("\"talismans\": 1", icon.ToContentJson("p", "P"), StringComparison.Ordinal);

        var trinket = Of("heretics", "heretic-trooper", "unholy-trinket").MatchCheck();
        Assert.True(trinket.Refused.Count == 0, string.Join("; ", trinket.Refused));
        Assert.DoesNotContain(trinket.PlayedWithout, w => w.Contains("Unholy Trinket", StringComparison.Ordinal));
        Assert.Equal(1, Loaded(trinket).Talismans);

        Assert.DoesNotContain("talismans", Of("pilgrims", "trench-pilgrim").MatchCheck().ToContentJson("p", "P"), StringComparison.Ordinal);
    }

    // Roster stage R6a (U117): New Antioch unit abilities a match plays; the others are still named.
    [Fact]
    public void NewAntiochAbilitiesArePlayed()
    {
        void Played(MatchReady ready, params string[] names)
        {
            Assert.True(ready.Refused.Count == 0, string.Join("; ", ready.Refused));
            Assert.All(names, n => Assert.DoesNotContain(ready.PlayedWithout, w => w.Contains(n, StringComparison.Ordinal)));
        }
        var priest = Of("principality", "blind-marksman", "sniper-rifle").MatchCheck();
        // Roster stage R6b (U118): and its Aim ACTION.
        Played(priest, "Absolute Faith", "Aim ACTION");
        Assert.Equal((true, true), (Loaded(priest).ShotsIgnoreMarkers, Loaded(priest).Aims));
        Assert.Contains("\"aims\": true", priest.ToContentJson("p", "P"), StringComparison.Ordinal);
        Assert.Contains("\"shotsIgnoreMarkers\": true", priest.ToContentJson("p", "P"), StringComparison.Ordinal);

        var medic = Of("principality", "combat-medic").MatchCheck();
        Played(medic, "Expert Medic", "Finish the Fallen");
        Assert.Equal((true, true, true), (Loaded(medic).ExpertMedic, Loaded(medic).FinishesTheFallen, Loaded(medic).Treats));

        var cleric = Of("principality", "trench-cleric", "pistol").MatchCheck();
        Played(cleric, "Onward Christian Soldiers!");
        Assert.True(Loaded(cleric).InspiresFaithful);

        // Assault Drill: the HEAVY of 1 Melee Weapon has no effect, so the Charge Bonus is rolled.
        var trooper = Of("principality", "shock-trooper", "great-hammer-maul").MatchCheck();
        Played(trooper, "Assault Drill", "Shock Charge");
        Assert.Equal((true, WeaponKeywords.None, false), (Loaded(trooper).ExtraChargeDie, Loaded(trooper).MeleeWeaponKeywords & WeaponKeywords.Heavy, Loaded(trooper).NoChargeBonus));
        Assert.True(Loaded(Of("principality", "yeoman", "great-hammer-maul").MatchCheck()).NoChargeBonus);

        // Battlefield Demolition: the same for its Satchel Charge.
        var engineer = Of("principality", "combat-engineer", "pistol", "satchel-charge").MatchCheck();
        Played(engineer, "Battlefield Demolition", "Fortify ACTION");
        Assert.True(Loaded(engineer).Fortifies);
        // The Sultanate's Fortify ACTION too (Warbands p. 85).
        var sapper = Of("sultanate", "sapper").MatchCheck();
        Played(sapper, "Fortify ACTION");
        Assert.Equal((true, false), (Loaded(sapper).Fortifies, Loaded(sapper).Aims));
        Assert.Equal(WeaponKeywords.None, Loaded(engineer).Grenade!.Keywords & WeaponKeywords.Heavy);
        Assert.NotEqual(WeaponKeywords.None, Loaded(Of("principality", "yeoman", "pistol", "satchel-charge").MatchCheck()).Grenade!.Keywords & WeaponKeywords.Heavy);

        var plain = Of("principality", "yeoman", "pistol").MatchCheck().ToContentJson("p", "P");
        Assert.All(new[] { "shotsIgnoreMarkers", "expertMedic", "finishesTheFallen", "extraChargeDie", "inspiresFaithful", "aims", "fortifies" },
            f => Assert.DoesNotContain(f, plain, StringComparison.Ordinal));
    }

    // Roster stage R6c (U119): abilities that change a number, played by name.
    [Fact]
    public void NumberAbilitiesArePlayed()
    {
        ModelProfile Played(string faction, string unit, string ability, params string[] kit)
        {
            var ready = Of(faction, unit, kit).MatchCheck();
            Assert.DoesNotContain(ready.PlayedWithout, w => w.Contains(ability, StringComparison.Ordinal));
            return Loaded(ready);
        }
        Assert.True(Played("cult", "plague-knight", "Undead Fortitude", "trench-club").UndeadFortitude);
        Assert.True(Played("heretics", "night-commando", "Stealth Generator", "pistol").Stealthy);
        Assert.True(Played("heretics", "bombard-witch", "Abiotic Life").AbioticLife);
        var prophet = Played("pilgrims", "war-prophet", "Memento Mori", "trench-club");
        Assert.Equal((true, false), (prophet.MementoMori, prophet.Traits.HasFlag(ModelTraits.Tough)));
        var nun = Played("pilgrims", "stigmatic-nun", "Agile", "trench-club");
        Assert.Equal((true, false), (nun.Agile, nun.SwiftDash));
        Assert.True(Played("pilgrims", "ecclesiastic-prisoner", "Mad Dash", "trench-club").SwiftDash);
        Assert.True(Played("sultanate", "yuzbasi", "Mubarizun", "trench-club").HuntsTheTough);
        Assert.True(Played("sultanate", "janissary", "Counter-Charge", "trench-club").CounterCharge);
        var giant = Played("pilgrims", "sanctified-giant", "Nailed Cross");
        Assert.True(giant.Negates.HasFlag(WeaponKeywords.Gas));
        var bull = Of("sultanate", "brazen-bull").MatchCheck();
        Assert.DoesNotContain(bull.PlayedWithout, w => w.Contains("Artificial Life", StringComparison.Ordinal));
        Assert.Equal(-1, Loaded(bull).InjuryDiceTaken);

        var plain = Of("principality", "yeoman", "pistol").MatchCheck().ToContentJson("p", "P");
        Assert.All(new[] { "agile", "swiftDash", "quickToRise", "undeadFortitude", "abioticLife", "stealthy", "huntsTheTough", "counterCharge", "mementoMori" },
            f => Assert.DoesNotContain(f, plain, StringComparison.Ordinal));
    }

    // Roster stage R6d (U120): an ability that fights without a weapon is the attack of a model with no Melee Weapon; with
    // one, it fights with that and the ability's attack is named. Trample and Overwhelming Horde travel as flags.
    [Fact]
    public void AttacksWithoutAWeaponArePlayed()
    {
        var lion = Of("sultanate", "alchemical-lion").MatchCheck();
        Assert.DoesNotContain(lion.PlayedWithout, w => w.Contains("Teeth and Claws", StringComparison.Ordinal));
        var lionProfile = Loaded(lion);
        Assert.Equal((true, 0, 0, 1), (lionProfile.HasMeleeWeapon, lionProfile.MeleeInjuryModifier, lionProfile.MeleeWeaponDice, lionProfile.MeleeAttacksWith(0)));

        var lord = Loaded(Of("cult", "bloated-lord").MatchCheck());
        Assert.Equal((true, 2), (lord.HasMeleeWeapon, lord.MeleeWeaponCleave));
        var armedLord = Of("cult", "bloated-lord", "trench-club").MatchCheck();
        Assert.Contains(armedLord.PlayedWithout, w => w.Contains("Crushing Blows's attack without a weapon (it fights with its Melee Weapon)", StringComparison.Ordinal));
        Assert.Equal(1, Loaded(armedLord).MeleeAttacksWith(0));

        var thrall = Loaded(Of("cult", "plague-thrall").MatchCheck());
        Assert.Equal((true, true), (thrall.HasMeleeWeapon, thrall.Horde));
        Assert.False(Loaded(Of("cult", "plague-thrall", "trench-club").MatchCheck()).Horde);

        Assert.Equal(-1, Loaded(Of("pilgrims", "ecclesiastic-prisoner").MatchCheck()).MeleeWeaponDice);

        var amalgam = Loaded(Of("cult", "amalgam").MatchCheck());
        Assert.Equal((true, -2), (amalgam.Tramples, amalgam.InjuryDiceTaken));
        Assert.True(Loaded(Of("sultanate", "brazen-bull").MatchCheck()).Tramples);

        // Oval bases are played (roster stage R8d, U132): nothing is refused for the base.
        Assert.DoesNotContain(Of("cult", "plague-hound").MatchCheck().Refused, r => r.Contains("oval base", StringComparison.Ordinal));
        Assert.DoesNotContain(Of("court", "pit-locust").MatchCheck().Refused, r => r.Contains("oval base", StringComparison.Ordinal));

        var plain = Of("principality", "yeoman", "pistol").MatchCheck().ToContentJson("p", "P");
        Assert.DoesNotContain("tramples", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("horde", plain, StringComparison.Ordinal);
    }

    // Roster stage R6e (U121): Enforced Orthodoxy and Laying on of Hands, played by name.
    [Fact]
    public void HelpingActionsArePlayed()
    {
        var castigator = Of("pilgrims", "castigator", "trench-club").MatchCheck();
        Assert.DoesNotContain(castigator.PlayedWithout, w => w.Contains("Enforced Orthodoxy", StringComparison.Ordinal));
        Assert.True(Loaded(castigator).EnforcesOrthodoxy);
        var prophet = Of("pilgrims", "war-prophet", "trench-club").MatchCheck();
        Assert.DoesNotContain(prophet.PlayedWithout, w => w.Contains("Laying on of Hands", StringComparison.Ordinal));
        Assert.True(Loaded(prophet).LaysOnHands);
        var plain = Of("principality", "yeoman", "pistol").MatchCheck().ToContentJson("p", "P");
        Assert.DoesNotContain("enforcesOrthodoxy", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("laysOnHands", plain, StringComparison.Ordinal);
    }

    // Roster stage R7d (U131): Bodyguard is played, and the PILGRIM keyword it looks for is a played trait.
    [Fact]
    public void BodyguardIsPlayed()
    {
        var giant = Of("pilgrims", "sanctified-giant").MatchCheck();
        Assert.DoesNotContain(giant.PlayedWithout, w => w.Contains("Bodyguard", StringComparison.Ordinal));
        var profile = Loaded(giant);
        Assert.True(profile.Bodyguard);
        Assert.True(profile.Traits.HasFlag(ModelTraits.Pilgrim));
        var plain = Of("principality", "yeoman", "pistol").MatchCheck().ToContentJson("p", "P");
        Assert.DoesNotContain("bodyguard", plain, StringComparison.Ordinal);
    }

    // Roster stage R9a (U134): the Loudspeakers and Puppet Master ACTIONs are played by name.
    [Fact]
    public void ActionsThatMoveOtherModelsArePlayed()
    {
        var prophet = Of("pilgrims", "war-prophet", "trench-club").MatchCheck();
        Assert.DoesNotContain(prophet.PlayedWithout, w => w.Contains("Loudspeakers", StringComparison.Ordinal));
        Assert.True(Loaded(prophet).Loudspeakers);
        var priest = Of("heretics", "heretic-priest", "trench-club").MatchCheck();
        Assert.DoesNotContain(priest.PlayedWithout, w => w.Contains("Puppet Master", StringComparison.Ordinal));
        Assert.True(Loaded(priest).PuppetMaster);
        var plain = Of("principality", "yeoman", "pistol").MatchCheck().ToContentJson("p", "P");
        Assert.DoesNotContain("loudspeakers", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("puppetMaster", plain, StringComparison.Ordinal);
    }

    // Roster stage R10 (U135): INFECTION MARKERS weapons and the God is With Us! ACTION are played.
    [Fact]
    public void InfectionWeaponsAndTheBlessingActionArePlayed()
    {
        var knight = Of("cult", "plague-knight", "plague-blade").MatchCheck();
        Assert.DoesNotContain(knight.Refused, r => r.Contains("INFECTION", StringComparison.Ordinal));
        Assert.DoesNotContain(knight.PlayedWithout, w => w.Contains("INFECTION MARKERS", StringComparison.Ordinal));
        Assert.True(Loaded(knight).MeleeWeaponKeywords.HasFlag(Rules.Core.State.WeaponKeywords.InfectionMarkers));
        var cleric = Of("principality", "trench-cleric", "pistol").MatchCheck();
        Assert.DoesNotContain(cleric.PlayedWithout, w => w.Contains("God is With Us", StringComparison.Ordinal));
        Assert.True(Loaded(cleric).Blesses);
        var plain = Of("principality", "yeoman", "pistol").MatchCheck().ToContentJson("p", "P");
        Assert.DoesNotContain("blesses", plain, StringComparison.Ordinal);
    }

    // Roster stage R12 (U137): the Arise and be Healed! ACTION of the Trench Cleric in the Principality's variants is played.
    [Fact]
    public void AriseAndBeHealedIsPlayed()
    {
        var builder = RosterBuilder.Shipped();
        builder.ChooseFaction("principality");
        builder.ChooseVariant("pontifical-task-force");
        builder.Select(builder.Add("trench-cleric"));
        var ready = builder.MatchCheck();
        Assert.DoesNotContain(ready.PlayedWithout, w => w.Contains("Arise and be Healed", StringComparison.Ordinal));
        var cleric = Loaded(ready);
        Assert.True(cleric.Heals && cleric.Blesses);
        Assert.DoesNotContain("heals", Of("principality", "yeoman", "pistol").MatchCheck().ToContentJson("p", "P"), StringComparison.Ordinal);
    }

    // Roster stage R11 (U136): the three Court spells bought as upgrades are known by the model; the others are still named.
    [Fact]
    public void CourtSpellsAreKnown()
    {
        ModelProfile Praetor(string variant, params string[] spells)
        {
            var builder = RosterBuilder.Shipped();
            builder.ChooseFaction("court");
            builder.ChooseVariant(variant);
            builder.Select(builder.Add("praetor"));
            foreach (var spell in spells)
            {
                builder.ToggleUpgrade(spell);
            }
            var ready = builder.MatchCheck();
            Assert.Empty(ready.Refused);
            return Loaded(ready);
        }
        var envy = Praetor("dedicated-to-envy", "coveted-position", "what-is-yours-is-mine");
        Assert.Equal(SpellKind.CovetedPosition | SpellKind.WhatIsYoursIsMine, envy.Spells);
        Assert.Equal(SpellKind.ExquisitePain, Praetor("dedicated-to-lust", "exquisite-pain").Spells);
        // A spell the engine does not play yet is named, and gives nothing.
        var greed = RosterBuilder.Shipped();
        greed.ChooseFaction("court");
        greed.ChooseVariant("dedicated-to-greed");
        greed.Select(greed.Add("praetor"));
        greed.ToggleUpgrade("black-heart");
        Assert.Contains(greed.MatchCheck().PlayedWithout, w => w.Contains("Black Heart", StringComparison.Ordinal));
        Assert.Equal(SpellKind.None, Loaded(greed.MatchCheck()).Spells);
    }

    // Roster stage R6f (U122): the upgrades the engine plays: Legionnaire, Zealot Strength (and a Trench Pilgrim's 32 mm
    // base), Janissary Veteran, Fearsome and Kavass; the others are still named.
    [Fact]
    public void UpgradesArePlayed()
    {
        ModelProfile With(string faction, string? variant, string entry, string upgrade, params string[] kit)
        {
            var builder = RosterBuilder.Shipped();
            builder.ChooseFaction(faction);
            builder.ChooseVariant(variant);
            builder.Select(builder.Add(entry));
            foreach (var k in kit)
            {
                builder.AddKit(k);
            }
            builder.ToggleUpgrade(upgrade);
            Assert.Equal([upgrade], builder.Roster.Models.Single().Upgrades);
            var ready = builder.MatchCheck();
            Assert.True(ready.Refused.Count == 0, string.Join("; ", ready.Refused));
            Assert.DoesNotContain(ready.PlayedWithout, w => w.EndsWith(": " + UpgradeName(faction, upgrade), StringComparison.Ordinal) || w.Contains(UpgradeName(faction, upgrade) + "'s keywords", StringComparison.Ordinal));
            return Loaded(ready);
        }
        static string UpgradeName(string faction, string id) => id switch
        {
            "legionnaire-ranged" => "Legionnaire (Ranged +1)",
            "legionnaire-melee" => "Legionnaire (Melee +1)",
            "zealot-strength" => "Zealot Strength",
            "janissary-veteran" => "Janissary Veteran",
            "fear" => "Fearsome",
            _ => "Kavass",
        };
        var plainTrooper = Loaded(Of("heretics", "heretic-trooper", "trench-club").MatchCheck());
        Assert.Equal(plainTrooper.RangedDice + 1, With("heretics", null, "heretic-trooper", "legionnaire-ranged", "trench-club").RangedDice);
        Assert.Equal(plainTrooper.MeleeDice + 1, With("heretics", null, "heretic-trooper", "legionnaire-melee", "trench-club").MeleeDice);
        var castigator = With("pilgrims", null, "castigator", "zealot-strength", "trench-club");
        Assert.True(castigator.Traits.HasFlag(ModelTraits.NegateHeavy));
        var pilgrim = With("pilgrims", null, "trench-pilgrim", "zealot-strength", "trench-club");
        Assert.Equal(Loaded(Of("pilgrims", "castigator", "trench-club").MatchCheck()).BaseRadius, pilgrim.BaseRadius);
        var veteran = With("sultanate", null, "yuzbasi", "janissary-veteran", "trench-club");
        Assert.Equal((true, true), (veteran.CounterCharge, veteran.Traits.HasFlag(ModelTraits.NegateHeavy)));
        Assert.True(With("sultanate", null, "alchemical-lion", "fear").Traits.HasFlag(ModelTraits.Fear));
        Assert.Equal(0, With("sultanate", "house-of-wisdom", "azeb", "kavass", "trench-club").MeleeDice);
        // An upgrade the engine does not play is still named.
        var mole = Of("principality", "yeoman", "pistol");
        mole.ToggleUpgrade("trench-mole");
        Assert.Contains(mole.MatchCheck().PlayedWithout, w => w.Contains("Trench Mole", StringComparison.Ordinal));
    }

    // HELD (p. 55): a Troop Flag takes a hand, so the model carries one 1-handed weapon or a shield.
    [Fact]
    public void HeldKitLeavesOneHand()
    {
        Assert.DoesNotContain(Of("principality", "lieutenant", "pistol", "troop-flag").Check().Problems, p => p.Contains("HELD", StringComparison.Ordinal));
        Assert.Contains(Of("principality", "lieutenant", "pistol", "sword-axe", "troop-flag").Check().Problems, p => p.Contains("(HELD) it can carry only one 1-handed weapon or a shield", StringComparison.Ordinal));
        Assert.Contains(Of("principality", "yeoman", "bolt-action-rifle", "troop-flag").Check().Problems, p => p.Contains("(HELD)", StringComparison.Ordinal));
    }

    [Fact]
    public void ASecondGrenadeAndGrenadesWithoutRangedAreRefused()
    {
        var two = Principality(("lieutenant", ["frag-grenades", "incendiary-grenades"])).MatchCheck();
        Assert.Contains(two.Refused, r => r.Contains("Incendiary Grenades (a model carries one type of grenade, p. 69)", StringComparison.Ordinal));

        var builder = RosterBuilder.Shipped();
        builder.ChooseFaction("cult");
        var vocabulary = KeywordVocabulary.Parse(ContentNames.Read("roster/keywords.json")).Value!;
        var catalogue = Catalogue.Parse(ContentNames.Read("roster/catalogue.json"), vocabulary).Value!;
        var thrower = builder.Roster with { Models = [new RosterModel("plague-thrall", null, ["frag-grenades"], [])] };
        Assert.Contains(MatchReadiness.Check(thrower, builder.Faction, catalogue).Refused, r => r.Contains("it may not throw grenades", StringComparison.Ordinal));

        // A Plague Thrall's NEGATE GAS and (roster stage R5b) its FEAR are played, and (R6a) its CULT tag.
        var thrall = MatchReadiness.Check(builder.Roster with { Models = [new RosterModel("plague-thrall", null, [], [])] }, builder.Faction, catalogue);
        var thrallProfile = Sim.Content.Parse(thrall.ToContentJson("t", "T")).Single().Models[0].Profile;
        Assert.Equal((WeaponKeywords.Gas, ModelTraits.Fear | ModelTraits.Cult), (thrallProfile.Negates, thrallProfile.Traits));
        Assert.DoesNotContain(thrall.PlayedWithout, w => w.Contains("its keywords", StringComparison.Ordinal));
    }

    [Fact]
    public void DemonicIsNegateFireAndAPiecesOwnRuleIsNamed()
    {
        var builder = RosterBuilder.Shipped();
        builder.ChooseFaction("court");
        builder.Add("praetor");
        var ready = builder.MatchCheck();
        Assert.Equal(WeaponKeywords.Fire, Sim.Content.Parse(ready.ToContentJson("c", "C")).Single().Models[0].Profile.Negates);
        Assert.DoesNotContain(ready.PlayedWithout, w => w.Contains("DEMONIC", StringComparison.Ordinal));

        // Incendiary Grenades: FIRE is played, its own rule (a Critical gives IGNORE ARMOUR, p. 78) is named.
        var fire = Principality(("lieutenant", ["incendiary-grenades"])).MatchCheck();
        Assert.True(fire.Ok, string.Join("; ", fire.Refused.Concat(fire.RosterProblems)));
        Assert.Equal(WeaponKeywords.Fire, Sim.Content.Parse(fire.ToContentJson("f", "F")).Single().Models[0].Profile.Grenade!.Keywords & WeaponKeywords.Fire);
        Assert.Contains(fire.PlayedWithout, w => w.Contains("Incendiary Grenades's own rule (p. 78)", StringComparison.Ordinal));
        var medic = Principality(("lieutenant", ["bolt-action-rifle", "binoculars"])).MatchCheck();
        Assert.Contains(medic.Refused, r => r.Contains("Binoculars (the engine has no equipment yet)", StringComparison.Ordinal));
    }

    // Roster stage R8d (U132): a 30x60 mm oval base is a capsule with a 15 mm radius and a 15 mm half span.
    [Fact]
    public void AnOvalBaseIsPlayed()
    {
        var builder = RosterBuilder.Shipped();
        builder.ChooseFaction("court");
        builder.Add("praetor");
        builder.Add("pit-locust");
        var ready = builder.MatchCheck();
        Assert.DoesNotContain(ready.Refused, r => r.Contains("oval base", StringComparison.Ordinal));
        var json = ready.ToContentJson("r8", "R8");
        Assert.Contains("\"baseLengthMm\":60", json.Replace(" ", ""), StringComparison.Ordinal);
        var locust = Sim.Content.Parse(json).Single().Models.Select(m => m.Profile).Single(p => p.IsOval);
        Assert.Equal(Rules.Core.Geometry.Length.FromThousandths(Rules.Core.Geometry.Length.FromMillimetres(30).Thousandths / 2), locust.BaseRadius);
        Assert.Equal(Rules.Core.Geometry.Length.FromThousandths((Rules.Core.Geometry.Length.FromMillimetres(60).Thousandths - Rules.Core.Geometry.Length.FromMillimetres(30).Thousandths) / 2), locust.BaseHalfSpan);
        // A round-based roster writes no length at all.
        Assert.DoesNotContain("baseLengthMm", Of("principality", "yeoman", "pistol").MatchCheck().ToContentJson("p", "P"), StringComparison.Ordinal);
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
