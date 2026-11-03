using Roster;

namespace Roster.Tests;

// Mercenaries (Warbands of Trench Crusade pp. 172-186): ten 0-1 models hired with Glory Points, on the lists of the
// factions that can recruit them.
public class MercenaryTests
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "content", "roster");
    private static readonly KeywordVocabulary Vocabulary = KeywordVocabulary.Load(KeywordVocabulary.DefaultPath()).Value!;
    private static readonly Catalogue Catalogue = Roster.Catalogue.Load(Path.Combine(Folder, "catalogue.json"), Vocabulary).Value!;

    private static Faction Load(string id) => Faction.Load(Path.Combine(Folder, "factions", id + ".json"), Catalogue, Vocabulary).Value!;

    private static RosterCheck Check(string faction, int glory, params RosterModel[] models) =>
        RosterValidator.Check(new WarbandRoster("Test", faction, null, 2000, glory, models), Load(faction), Catalogue);

    private static RosterModel M(string entry, params string[] kit) => new(entry, null, kit, []);

    [Theory]
    [InlineData("principality", "combat-biologist communed-tank-hunter mamluke-fares mendelite-ammo-monk observer scriptural-guardian hexburner")]
    [InlineData("pilgrims", "communed-tank-hunter mendelite-ammo-monk observer scriptural-guardian sister-of-saint-kosmas hexburner")]
    [InlineData("sultanate", "combat-biologist mamluke-fares scriptural-guardian")]
    [InlineData("heretics", "goetic-warlock scriptural-guardian sin-devourer")]
    [InlineData("court", "goetic-warlock scriptural-guardian sin-devourer")]
    [InlineData("cult", "scriptural-guardian sin-devourer")]
    public void EachFactionListsItsMercenaries(string faction, string ids)
    {
        var models = Load(faction).Models;
        foreach (var id in ids.Split(' '))
        {
            var model = models.Single(m => m.Id == id);
            Assert.Equal(Currency.Glory, model.Cost!.Currency);
            Assert.Equal(1, model.Max);
        }
        Assert.Equal(ids.Split(' ').Length, models.Count(m => m.Cost!.Currency == Currency.Glory));
    }

    [Fact]
    public void AMercenaryCostsGloryAndNeedsGloryToHireIt()
    {
        var hired = Check("principality", 3, M("lieutenant"), M("combat-biologist"));
        Assert.True(hired.Legal, string.Join("; ", hired.Problems));
        Assert.Equal((70, 3), (hired.Ducats, hired.Glory));
        Assert.Contains(Check("principality", 2, M("lieutenant"), M("combat-biologist")).Problems, p => p.Contains("Glory"));
    }

    [Fact]
    public void AMercenaryIsHiredOnlyOnce() =>
        Assert.Contains(Check("pilgrims", 9, M("war-prophet"), M("observer"), M("observer")).Problems, p => p.Contains("at most 1"));

    [Fact]
    public void AMercenaryHasNoBattlekitToChange() =>
        Assert.NotEmpty(Check("pilgrims", 9, M("war-prophet"), M("observer", "pistol")).Problems);

    [Fact]
    public void ASandKnightTakesOneOfItsListedWeapons()
    {
        var check = Check("sultanate", 4, M("yuzbasi"), M("mamluke-fares", "great-sword-axe"));
        Assert.True(check.Legal, string.Join("; ", check.Problems));
    }

    [Fact]
    public void ATomeWardenBuysOnlyMeleeWeapons()
    {
        var legal = Check("heretics", 7, M("heretic-priest"), M("scriptural-guardian", "sword-axe"));
        Assert.True(legal.Legal, string.Join("; ", legal.Problems));
        Assert.NotEmpty(Check("heretics", 7, M("heretic-priest"), M("scriptural-guardian", "sword-axe", "pistol")).Problems);
        Assert.NotEmpty(Check("heretics", 7, M("heretic-priest"), M("scriptural-guardian")).Problems);
    }

    [Fact]
    public void ASisterOfMercyPlaysHerAbilitiesAndAMunitionsMonkNamesItsOwn()
    {
        var pilgrims = Load("pilgrims");
        var roster = new WarbandRoster("Test", "pilgrims", null, 700, 4, [M("war-prophet"), M("sister-of-saint-kosmas"), M("mendelite-ammo-monk")]);
        var ready = MatchReadiness.Check(roster, pilgrims, Catalogue);
        Assert.Contains(ready.PlayedWithout, w => w.Contains("Mendelite Ammo Monk") && w.Contains("Ammunition Sacrament"));
        Assert.DoesNotContain(ready.PlayedWithout, w => w.Contains("Sister of Saint Kosmas") && w.Contains("Expert Medic"));
    }
}
