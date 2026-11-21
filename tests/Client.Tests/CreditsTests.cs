using Client.Core;

namespace Client.Tests;

// Stage M1: the Credits screen names every author and licence the art list (client/assets/ASSETS.md) gives, so a new
// asset cannot ship without its credit (ASSETS.md: CC-BY credit is given "here and in the game's credits").
public class CreditsTests
{
    private static readonly string Words = string.Join("\n", Credits.Entries.Select(e => $"{e.Heading}: {e.Text}"));

    [Fact]
    public void EveryAssetAuthorAndLicenceIsCredited()
    {
        var rows = File.ReadAllLines(Path.Combine(Repository.Root, "client", "assets", "ASSETS.md"))
            .Where(l => l.StartsWith("| `", StringComparison.Ordinal))
            .Select(l => l.Split('|'))
            .ToList();
        Assert.NotEmpty(rows);
        foreach (var cells in rows)
        {
            var (author, licence) = (cells[3].Trim(), cells[4].Trim());
            // Each named person or group (the parts before any bracketed role), and the licence.
            foreach (var name in author.Split(';').Select(a => a.Split('(')[0].Trim()).Where(a => a.Length > 0))
            {
                foreach (var person in name.Split(',').Select(p => p.Trim()))
                {
                    Assert.Contains(person, Words, StringComparison.Ordinal);
                }
            }
            Assert.Contains(licence == "Original" ? "Original" : licence, Words, StringComparison.Ordinal);
        }
    }

    // Jason 2026-10-29: the game says it is an unofficial, non-profit fan project with no tie to Trench Crusade.
    [Fact]
    public void TheFanProjectNoticeIsShown()
    {
        var notice = Assert.Single(Credits.Entries, e => e.Heading == Credits.NoticeHeading).Text;
        foreach (var words in new[] { "unofficial", "non-profit fan project", "not affiliated with", "endorsed", "Trench Crusade", "not for sale" })
        {
            Assert.Contains(words, notice, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheEngineAndRuntimeAreCredited()
    {
        Assert.Contains("Godot Engine", Words, StringComparison.Ordinal);
        Assert.Contains(".NET", Words, StringComparison.Ordinal);
        Assert.All(Credits.Entries, e => Assert.False(string.IsNullOrWhiteSpace(e.Text)));
    }
}
