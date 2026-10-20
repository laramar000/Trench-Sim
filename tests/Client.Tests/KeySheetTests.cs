using Client.Core;

namespace Client.Tests;

// QoL I1: the key sheet names every key and mouse use the battlefield and window handle.
public class KeySheetTests
{
    [Fact]
    public void TheSheetNamesEveryKey()
    {
        var text = KeySheet.Text;
        foreach (var key in new[] { "Arrows", "+ and -", "wheel", "right-drag", "Q and E", "T:", "F:", "Shift+F", "Home", "Double-click", "N and P", "J and Shift+J",
            "O and Shift+O", "A:", "L place", "M move", "D dash", "R retreat", "S shoot", "C charge", "X fight", "Enter", "Shift+click", "Space", "Backspace", "Esc",
            "right-click", "F1", "F2", "?:", "combat-log row", "K climbs" })
        {
            Assert.Contains(key, text, StringComparison.Ordinal);
        }
        Assert.All(KeySheet.Groups, g => Assert.NotEmpty(g.Lines));
    }
}
