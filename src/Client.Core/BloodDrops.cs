using Rules.Core.State;

namespace Client.Core;

// Visual pass Stage 3 (2026-10-27): a model's Blood Markers as a row of small drops under its battlefield label, one
// filled drop for each marker the server's state gives it (ModelState.BloodMarkers, at most BloodMarkers.Max = 6,
// p. 27) and a faint empty one for each it could still take. Nothing is counted here: the number is the state's.
// Drawn as text so it is original and needs no image file: '#' blood, 'X' the dark outline, 'o' an empty drop's faint
// outline, '.' clear.
public static class BloodDrops
{
    // One drop: 5 pixels wide, 7 high, point up.
    private static readonly string[] Drop =
    [
        "..X..",
        "..X..",
        ".X#X.",
        "X###X",
        "X###X",
        "X###X",
        ".XXX.",
    ];

    public const int DropWidth = 5;
    public const int Gap = 1;
    public const int Height = 7;
    public const int Max = BloodMarkers.Max;
    public static int Width => BloodMarkers.Max * (DropWidth + Gap) - Gap;

    // The row for a model with this many markers, or null at 0 (nothing is drawn then).
    public static string[]? Row(int markers)
    {
        if (markers <= 0)
        {
            return null;
        }
        var filled = Math.Min(markers, BloodMarkers.Max);
        var rows = new string[Height];
        for (var y = 0; y < Height; y++)
        {
            var line = new char[Width];
            Array.Fill(line, '.');
            for (var d = 0; d < BloodMarkers.Max; d++)
            {
                for (var x = 0; x < DropWidth; x++)
                {
                    var c = Drop[y][x];
                    line[d * (DropWidth + Gap) + x] = d < filled ? c : c == '.' ? '.' : 'o';
                }
            }
            rows[y] = new string(line);
        }
        return rows;
    }

    // How many filled drops a row shows (for the tests and the UI check).
    public static int Filled(string[] row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return Enumerable.Range(0, BloodMarkers.Max).Count(d => row[3][d * (DropWidth + Gap) + 2] == '#');
    }
}
