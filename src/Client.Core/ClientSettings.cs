using System.Globalization;

namespace Client.Core;

// QoL I2 (sound: I3, off by default): what a player chose in the Menu, kept between launches in a small text file ("key=value" lines). A missing,
// unknown or out-of-range value falls back to its default, so a broken file never stops the client. No match data.
public sealed record ClientSettings(int TextSize = 0, bool HighContrast = false, int Dice = 1, bool TopDown = false, int InterfaceScale = 0, bool Sound = false)
{
    public static readonly float[] TextScales = [1f, 1.25f, 1.5f];
    public static readonly float[] InterfaceScales = [1f, 1.25f, 1.5f];
    public const int DiceChoices = 4;

    public static ClientSettings Default { get; } = new();

    public string Format() => string.Join("\n",
        $"text={TextSize}",
        $"contrast={(HighContrast ? 1 : 0)}",
        $"dice={Dice}",
        $"topdown={(TopDown ? 1 : 0)}",
        $"scale={InterfaceScale}",
        $"sound={(Sound ? 1 : 0)}") + "\n";

    public static ClientSettings Parse(string? text)
    {
        var values = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in (text ?? "").Split('\n'))
        {
            var parts = line.Trim().Split('=', 2);
            if (parts.Length == 2 && int.TryParse(parts[1].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                values[parts[0].Trim()] = value;
            }
        }
        int Pick(string key, int count, int fallback) => values.TryGetValue(key, out var v) && v >= 0 && v < count ? v : fallback;
        var d = Default;
        return new ClientSettings(
            Pick("text", TextScales.Length, d.TextSize),
            Pick("contrast", 2, d.HighContrast ? 1 : 0) == 1,
            Pick("dice", DiceChoices, d.Dice),
            Pick("topdown", 2, d.TopDown ? 1 : 0) == 1,
            Pick("scale", InterfaceScales.Length, d.InterfaceScale),
            Pick("sound", 2, d.Sound ? 1 : 0) == 1);
    }
}
