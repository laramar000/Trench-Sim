using Client.Core;

namespace Client.Tests;

// QoL I1: the key sheet names every key and mouse use the battlefield and window handle. Stage K1: each entry keeps its
// keys apart from what they do, so the sheet draws them as key caps.
public class KeySheetTests
{
    [Fact]
    public void TheSheetNamesEveryKey()
    {
        var keys = KeySheet.Keys.ToHashSet(StringComparer.Ordinal);
        foreach (var key in new[] { "Arrows", "+", "-", "Wheel", "Right-drag", "Q", "E", "T", "F", "Shift+F", "Home", "Double-click", "Click", "Click a log row",
            "N", "P", "J", "Shift+J", "O", "Shift+O", "A", "L", "M", "D", "R", "S", "C", "X", "K", "Enter", "Shift+click", "Space", "Confirm", "Backspace",
            "Esc", "Right-click", "F1", "F2", "?", "G", "1", "2", "3", "Confirm charge" })
        {
            Assert.Contains(key, keys);
        }
        Assert.All(KeySheet.Groups, g => Assert.NotEmpty(g.Entries));
    }

    [Fact]
    public void EachEntryHasKeysAndWordsAndThePlainTextNamesThemTogether()
    {
        foreach (var (group, entries) in KeySheet.Groups)
        {
            Assert.Contains(group.ToUpperInvariant(), KeySheet.Text, StringComparison.Ordinal);
            foreach (var entry in entries)
            {
                Assert.NotEmpty(entry.Keys);
                Assert.All(entry.Keys, k => Assert.False(string.IsNullOrWhiteSpace(k)));
                Assert.False(string.IsNullOrWhiteSpace(entry.What));
                Assert.Contains($"  {string.Join(" / ", entry.Keys)}: {entry.What}", KeySheet.Lines());
            }
        }
        // The actions keep their keys as before (fight is X: F frames the table).
        Assert.Contains(KeySheet.Groups.SelectMany(g => g.Entries), e => e.What == "fight" && e.Keys.SequenceEqual(["X"]));
        Assert.Contains(KeySheet.Groups.SelectMany(g => g.Entries), e => e.What == "fit the table" && e.Keys.SequenceEqual(["F"]));
    }
}
