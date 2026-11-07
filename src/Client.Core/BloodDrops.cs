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
    // Roster stage R10 (U135): INFECTION and BLESSING MARKERS are drops too, in the same row after the blood: 'g' (drawn green)
    // and 'b' (drawn bright blue). The row has room for 6 drops, or for all the model's markers when it has more (up to 6 of
    // each kind, 18). With blood alone it is exactly the row it always was.
    public static string[]? Row(int blood, int infection = 0, int blessing = 0)
    {
        blood = Math.Clamp(blood, 0, BloodMarkers.Max);
        infection = Math.Clamp(infection, 0, BloodMarkers.Max);
        blessing = Math.Clamp(blessing, 0, BloodMarkers.Max);
        var filled = blood + infection + blessing;
        if (filled <= 0)
        {
            return null;
        }
        var cells = Math.Max(BloodMarkers.Max, filled);
        var rows = new string[Height];
        for (var y = 0; y < Height; y++)
        {
            var line = new char[WidthOf(cells)];
            Array.Fill(line, '.');
            for (var d = 0; d < cells; d++)
            {
                var fill = d < blood ? '#' : d < blood + infection ? 'g' : d < filled ? 'b' : '\0';
                for (var x = 0; x < DropWidth; x++)
                {
                    var c = Drop[y][x];
                    line[d * (DropWidth + Gap) + x] = fill == '\0' ? c == '.' ? '.' : 'o' : c == '#' ? fill : c;
                }
            }
            rows[y] = new string(line);
        }
        return rows;
    }

    public static int WidthOf(int drops) => drops * (DropWidth + Gap) - Gap;

    // How many filled drops a row shows (for the tests and the UI check).
    public static int Filled(string[] row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var cells = (row[0].Length + Gap) / (DropWidth + Gap);
        return Enumerable.Range(0, cells).Count(d => row[3][d * (DropWidth + Gap) + 2] is '#' or 'g' or 'b');
    }
}
