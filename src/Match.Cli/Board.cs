using System.Text;
using Rules.Core.Geometry;
using Rules.Core.State;

namespace Match.Cli;

// A text picture of the client's copy of the match: one character per 2 inches, terrain as '#', zones as '.', Seat One's
// models as A, B, C... and Seat Two's as a, b, c..., in id order; the list below the map gives each letter's model.
public static class Board
{
    private const int InchesPerCell = 2;

    public static string Render(MatchState state, Seat? me)
    {
        var text = new StringBuilder();
        text.AppendLine($"Phase {state.Phase}, turn {state.Turn}" + (me is null ? "" : $", you are Seat {me}") +
            (state.Pending is { } p ? $", waiting for {p.Seat}: {p.Kind}" : "") +
            (state.ActiveModel is { } a ? $", active {a}" : ""));
        text.AppendLine($"VP One {state.One.VictoryPoints}{(state.One.Shaken ? " (Shaken)" : "")}, Two {state.Two.VictoryPoints}{(state.Two.Shaken ? " (Shaken)" : "")}");
        if (state.Outcome is { } outcome)
        {
            text.AppendLine($"Match over: {outcome.Reason}, winner {outcome.Winner?.ToString() ?? "none"}");
        }

        if (state.Setup?.Scenario?.Battlefield is { } field)
        {
            var width = (int)(field.Width.Thousandths / 1000 / InchesPerCell);
            var height = (int)(field.Height.Thousandths / 1000 / InchesPerCell);
            var grid = new char[height, width];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var centre = new Point2(Length.FromThousandths((x * InchesPerCell + 1) * 1000), Length.FromThousandths((y * InchesPerCell + 1) * 1000));
                    grid[y, x] = field.Terrain.Any(t => t.Footprint.Bounds.Contains(centre)) ? '#'
                        : field.Zones.Any(z => z.Area.Bounds.Contains(centre)) ? '.' : ' ';
                }
            }
            foreach (var model in state.Models.Where(m => m.Position is not null))
            {
                var x = (int)Math.Clamp(model.Position!.Value.X.Thousandths / 1000 / InchesPerCell, 0, width - 1);
                var y = (int)Math.Clamp(model.Position!.Value.Y.Thousandths / 1000 / InchesPerCell, 0, height - 1);
                grid[y, x] = Mark(state, model);
            }
            text.AppendLine("+" + new string('-', width) + "+");
            for (var y = height - 1; y >= 0; y--)
            {
                text.Append('|');
                for (var x = 0; x < width; x++)
                {
                    text.Append(grid[y, x]);
                }
                text.AppendLine("|");
            }
            text.AppendLine("+" + new string('-', width) + "+");
        }

        foreach (var model in state.Models)
        {
            var where = model.Position is { } at ? $"({at.X.Thousandths / 1000m:0.###},{at.Y.Thousandths / 1000m:0.###})" : model.Deployed ? "deployed" : "-";
            text.AppendLine($"  {Mark(state, model)} {model.Seat} {model.Id,-4} {model.Status,-11} {where} markers {model.BloodMarkers}{(model.ActivatedThisTurn ? " activated" : "")}");
        }
        return text.ToString();
    }

    private static char Mark(MatchState state, ModelState model)
    {
        var index = state.Models.Where(m => m.Seat == model.Seat).Select(m => m.Id).ToList().IndexOf(model.Id);
        var letter = (char)('a' + index % 26);
        return model.Seat == Seat.One ? char.ToUpperInvariant(letter) : letter;
    }
}
