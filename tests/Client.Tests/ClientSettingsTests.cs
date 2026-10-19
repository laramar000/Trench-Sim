using Client.Core;

namespace Client.Tests;

// QoL I2: settings survive a save and load; anything missing, unknown or out of range falls back to its default.
public class ClientSettingsTests
{
    [Fact]
    public void EverySettingSurvivesASaveAndLoad()
    {
        for (var text = 0; text < ClientSettings.TextScales.Length; text++)
        {
            for (var dice = 0; dice < ClientSettings.DiceChoices; dice++)
            {
                for (var scale = 0; scale < ClientSettings.InterfaceScales.Length; scale++)
                {
                    foreach (var flag in new[] { false, true })
                    {
                        var settings = new ClientSettings(text, flag, dice, !flag, scale, flag);
                        Assert.Equal(settings, ClientSettings.Parse(settings.Format()));
                    }
                }
            }
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage\n===\ntext=9\ndice=-1\nscale=abc\ncontrast=7\ntopdown=2\nsound=5\nunknown=1")]
    public void ABrokenFileGivesTheDefaults(string? text) => Assert.Equal(ClientSettings.Default, ClientSettings.Parse(text));

    [Fact]
    public void KnownValuesAreKeptWhenOthersAreBroken()
    {
        var settings = ClientSettings.Parse("text=2\r\ndice=x\ncontrast=1\n");
        Assert.Equal(new ClientSettings(TextSize: 2, HighContrast: true), settings);
    }
}
