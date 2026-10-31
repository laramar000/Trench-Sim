using Client.Core;

namespace Client.Tests;

// Roster builder stage 3: the screen's logic, on the shipped (invented) practice data.
public class RosterBuilderTests
{
    [Fact]
    public void TheShippedPracticeDataLoads()
    {
        var builder = RosterBuilder.Shipped();
        Assert.Equal([new RosterBuilder.FactionChoice("practice", "Practice Company")], builder.Factions);
        Assert.Equal(["Captain", "Marksman", "Soldier", "Bruiser"], builder.Entries.Select(e => e.Name));
        Assert.Equal("required, 1", builder.Entries[0].LimitText);
        Assert.Equal("60 ducats", builder.Entries[0].CostText);
        Assert.Equal([null, new RosterBuilder.FactionChoice("practice-raiders", "Practice Raiders")], builder.Variants);
    }

    [Fact]
    public void AnEmptyWarbandSaysItNeedsItsCaptain()
    {
        var builder = RosterBuilder.Shipped();
        Assert.Equal("0 of 700 ducats · 1 problem", builder.TotalText);
        Assert.Contains("✕ Practice Company must include 1 Captain.", builder.CheckLines);
    }

    [Fact]
    public void AddingModelsKitAndUpgradesUpdatesTheTotal()
    {
        var builder = RosterBuilder.Shipped();
        builder.Add("practice-captain");
        builder.AddKit("practice-pistol");
        builder.AddKit("practice-club");
        var soldier = builder.Add("practice-soldier");
        Assert.Equal(soldier, builder.Selected);
        Assert.Contains(builder.KitChoices, k => k.Id == "practice-rifle" && k.CostText == "5 ducats"); // the soldier's own price
        builder.AddKit("practice-rifle");
        builder.ToggleUpgrade("practice-scout");
        Assert.True(builder.Upgrades.Single().Bought);
        var check = builder.Check();
        Assert.True(check.Legal, string.Join("; ", check.Problems));
        Assert.Equal(60 + 6 + 3 + 25 + 5 + 10, check.Ducats);
        Assert.Equal("109 of 700 ducats · legal", builder.TotalText);
        Assert.Equal(["1. Captain: Practice Pistol, Practice Club", "2. Soldier: Practice Rifle"], builder.Models.Select(m => m.Title));
        Assert.Equal(["69 ducats", "40 ducats"], builder.Models.Select(m => m.CostText));
        Assert.Contains(builder.CheckLines, l => l.Contains("not verified against the book yet"));
    }

    [Fact]
    public void RefusalsAreShownInWords()
    {
        var builder = RosterBuilder.Shipped();
        builder.Add("practice-captain");
        builder.AddKit("practice-rifle");
        builder.AddKit("practice-pistol");
        builder.Add("practice-captain");
        Assert.False(builder.Check().Legal);
        Assert.Contains(builder.CheckLines, l => l.StartsWith("✕ Model 1, Captain: can carry one 2-handed or two 1-handed ranged weapons", StringComparison.Ordinal));
        Assert.Contains("✕ Practice Company can include at most 1 Captain (it has 2).", builder.CheckLines);
    }

    [Fact]
    public void ListedKitAndTheSelectionFollowTheModel()
    {
        var builder = RosterBuilder.Shipped();
        builder.Add("practice-captain");
        builder.Add("practice-bruiser");
        Assert.Equal(["practice-maul", "practice-club", "practice-coat", "practice-helmet"], builder.KitChoices.Select(k => k.Id));
        builder.AddKit("practice-maul");
        builder.AddKit("practice-club");
        Assert.True(builder.Check().Legal); // STRONG: the maul counts as 1-handed
        Assert.Equal(["Practice Maul", "Practice Club"], builder.Carried.Select(c => c.Name));
        builder.RemoveKit(0);
        Assert.Equal(["Practice Club"], builder.Carried.Select(c => c.Name));
        builder.Select(0);
        Assert.Empty(builder.Carried);
        builder.Remove(0);
        Assert.Equal(0, builder.Selected);
        Assert.Equal("1. Bruiser: Practice Club", builder.Models.Single().Title);
        builder.Remove(0);
        Assert.Equal(-1, builder.Selected);
        Assert.Empty(builder.KitChoices);
    }

    [Fact]
    public void AVariantStartsAnEmptyWarbandWithItsOwnList()
    {
        var builder = RosterBuilder.Shipped();
        builder.Add("practice-captain");
        builder.ChooseVariant("practice-raiders");
        Assert.Empty(builder.Models);
        Assert.Equal("Practice Raiders", builder.ListName);
        Assert.Equal(["Captain", "Soldier", "Bruiser", "Raider"], builder.Entries.Select(e => e.Name));
        Assert.Equal("20 ducats", builder.Entries.Single(e => e.Id == "practice-soldier").CostText);
        builder.Add("practice-captain");
        Assert.DoesNotContain(builder.KitChoices, k => k.Id == "practice-carbine");
    }

    [Fact]
    public void ARosterIsSavedAndLoadedBack()
    {
        var folder = Path.Combine(Path.GetTempPath(), "roster-builder-" + Guid.NewGuid().ToString("N"));
        try
        {
            var builder = RosterBuilder.Shipped();
            builder.Name = "Mud / Rats";
            builder.ChooseVariant("practice-raiders");
            builder.Add("practice-captain");
            builder.AddKit("practice-pistol");
            builder.Add("practice-raider");
            var path = builder.Save(folder);
            Assert.Equal("Mud _ Rats.roster.json", Path.GetFileName(path));
            Assert.Equal(["Mud _ Rats.roster.json"], RosterBuilder.Saved(folder));

            var again = RosterBuilder.Shipped();
            Assert.Null(again.Load(folder, "Mud _ Rats.roster.json"));
            Assert.Equal(("Mud / Rats", "practice-raiders"), (again.Name, again.Variant));
            Assert.Equal(builder.Models, again.Models);
            Assert.Equal(builder.TotalText, again.TotalText);

            File.WriteAllText(Path.Combine(folder, "bad.roster.json"), "{ not json");
            Assert.StartsWith("This file cannot be read", again.Load(folder, "bad.roster.json"));
            File.WriteAllText(Path.Combine(folder, "other.roster.json"),
                """{ "format": 1, "rulesVersion": "1.0.2", "name": "X", "faction": "elsewhere", "budgetDucats": 700, "models": [] }""");
            Assert.Equal("This roster is for a faction this version does not have (elsewhere).", again.Load(folder, "other.roster.json"));
            Assert.Equal("Mud / Rats", again.Name); // a refused file changes nothing
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
