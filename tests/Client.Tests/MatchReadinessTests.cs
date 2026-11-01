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
        var builder = Principality(("lieutenant", ["pistol", "medi-kit"]));
        var ready = builder.MatchCheck();
        Assert.False(ready.Ok);
        Assert.Contains(ready.Refused, r => r.Contains("Medi-kit", StringComparison.Ordinal) && r.Contains("no equipment", StringComparison.Ordinal));
        Assert.Contains(builder.MatchLines, l => l.StartsWith("✕ Not playable yet:", StringComparison.Ordinal) && l.Contains("Medi-kit", StringComparison.Ordinal));
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
        Assert.Equal(ModelTraits.Leader | ModelTraits.Tough, Loaded(ready).Traits);
        Assert.Contains(ready.PlayedWithout, w => w.Contains("its keywords (PRINCIPALITY, ELITE)", StringComparison.Ordinal));
        Assert.Contains("\"traits\"", ready.ToContentJson("p", "P"), StringComparison.Ordinal);

        Assert.Equal(ModelTraits.NegateFear, Loaded(Of("principality", "trench-cleric", "pistol").MatchCheck()).Traits);
        Assert.Equal(ModelTraits.NegateHeavy, Loaded(Of("principality", "mechanized-heavy-infantry").MatchCheck()).Traits);
        var bull = Loaded(Of("sultanate", "brazen-bull").MatchCheck());
        Assert.Equal(ModelTraits.Fear | ModelTraits.Tough | ModelTraits.NegateHeavy, bull.Traits);
        // A model with none writes nothing new.
        var yeoman = Of("principality", "yeoman", "bolt-action-rifle").MatchCheck();
        Assert.Equal(ModelTraits.None, Loaded(yeoman).Traits);
        Assert.DoesNotContain("traits", yeoman.ToContentJson("p", "P"), StringComparison.Ordinal);
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
        Assert.Equal(ModelTraits.Leader, Loaded(flag).Traits);

        var relic = Of("heretics", "heretic-trooper", "unholy-relic").MatchCheck();
        Played(relic, "Unholy Relic");
        Assert.Equal(ModelTraits.Fear, Loaded(relic).Traits);

        var capirote = Of("pilgrims", "trench-pilgrim", "iron-capirote").MatchCheck();
        Played(capirote, "Iron Capirote");
        Assert.Equal((ModelTraits.NegateFear, WeaponKeywords.Shrapnel), (Loaded(capirote).Traits, Loaded(capirote).Negates));

        var eyes = Of("cult", "corpse-guard", "compound-eyes-helmet").MatchCheck();
        Played(eyes, "Compound Eyes Helmet");
        Assert.Equal((1, 1, WeaponKeywords.Gas | WeaponKeywords.Shrapnel), (Loaded(eyes).RangedDice, Loaded(eyes).MeleeDice, Loaded(eyes).Negates));

        var abyssinian = RosterBuilder.Shipped();
        abyssinian.ChooseVariant("abyssinian-expedition");
        abyssinian.Select(abyssinian.Add("lieutenant"));
        abyssinian.AddKit("anfarro");
        var crowned = abyssinian.MatchCheck();
        Played(crowned, "Anfarro");
        Assert.Equal((2, 3, ModelTraits.Leader | ModelTraits.Tough | ModelTraits.NegateFear), (Loaded(crowned).RangedDice, Loaded(crowned).MeleeDice, Loaded(crowned).Traits));

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
        Assert.Equal((-1, ModelTraits.NegateFear), (Loaded(smoked).InjuryDiceTaken, Loaded(smoked).Traits));
        Assert.Contains("\"injuryDiceTaken\": -1", smoked.ToContentJson("p", "P"), StringComparison.Ordinal);

        var pilgrim = Of("pilgrims", "trench-pilgrim", "zealots-draught").MatchCheck();
        Assert.Equal((-1, ModelTraits.NegateFear), (Loaded(pilgrim).InjuryDiceTaken, Loaded(pilgrim).Traits));

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

        // A Plague Thrall's NEGATE GAS and (roster stage R5b) its FEAR are played; its CULT tag is named.
        var thrall = MatchReadiness.Check(builder.Roster with { Models = [new RosterModel("plague-thrall", null, [], [])] }, builder.Faction, catalogue);
        var thrallProfile = Sim.Content.Parse(thrall.ToContentJson("t", "T")).Single().Models[0].Profile;
        Assert.Equal((WeaponKeywords.Gas, ModelTraits.Fear), (thrallProfile.Negates, thrallProfile.Traits));
        Assert.Contains(thrall.PlayedWithout, w => w.Contains("its keywords (CULT)", StringComparison.Ordinal));
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
        var medic = Principality(("lieutenant", ["bolt-action-rifle", "medi-kit"])).MatchCheck();
        Assert.Contains(medic.Refused, r => r.Contains("Medi-kit (the engine has no equipment yet)", StringComparison.Ordinal));
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
