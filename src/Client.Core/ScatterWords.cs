using Rules.Core;
using Rules.Core.Geometry;
using Rules.Core.State;

namespace Client.Core;

// Roster stage R4c (U103): how the decision bar names a point a missed SCATTER attack may land at: its compass
// direction from the target, or (R5a) from the point it was aimed at (east and south are positive x and y, as for the cursor), how far, and who the blast there
// would hit, worked out by the rules (TurnSequence.BlastHitsAt).
public static class ScatterWords
{
    private static readonly string[] Compass = ["east", "south-east", "south", "south-west", "west", "north-west", "north", "north-east"];

    public static string Direction(MatchState state, Point2 at)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (TurnSequence.ScatterOrigin(state) is not { } centre)
        {
            return "here";
        }
        var degrees = Math.Atan2(TableRect.Inches(at.Y) - TableRect.Inches(centre.Y), TableRect.Inches(at.X) - TableRect.Inches(centre.X)) * 180 / Math.PI;
        var sector = (int)Math.Round(((degrees % 360) + 360) % 360 / 45) % 8;
        var exact = Math.Abs(Math.IEEERemainder(degrees, 45)) < 0.5;
        return (exact ? "" : "about ") + Compass[sector];
    }

    public static string Where(MatchState state, Point2 at)
    {
        ArgumentNullException.ThrowIfNull(state);
        var inches = state.InProgress?.ScatterInches ?? 0;
        var target = state.InProgress?.Target is { } t ? UnitPanel.ShortId(t)
            : state.InProgress?.BlastAt is { } aimed ? $"the aimed point {TablePoint.Of(aimed)}" : "the target";
        return $"{inches}\" {Direction(state, at)} of {target}, at {TablePoint.Of(at)}";
    }

    public static string Hits(MatchState state, Point2 at, Func<ModelId, string> name)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(name);
        if (state.InProgress is not { } progress)
        {
            return "hits nobody";
        }
        var hit = TurnSequence.BlastHitsAt(state, progress.Actor, at, progress.Weapon);
        var attacker = state.FindModel(progress.Actor)?.Seat;
        return hit.Count == 0 ? "hits nobody" : "hits " + string.Join(", ", hit.Select(h => name(h) + (state.FindModel(h)?.Seat == attacker ? " (the attacker's)" : "")));
    }
}
