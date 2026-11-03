using Rules.Core.Geometry;
using Rules.Core.State;

namespace Rules.Core;

// Field Shrines (Battlekit p. 82; U127), read-only, for the client and the simulator: the rules themselves are in Board,
// the reducer and the turn sequence.
public static class FieldShrines
{
    // Each shrine's base as an Impassable obstacle for a path check or a charge plan.
    public static IEnumerable<Obstacle> Obstacles(MatchState state) => Board.ShrineObstacles(state);

    // The deployed model whose shrine its player has yet to deploy or decline, if any.
    public static ModelState? CarrierWaiting(MatchState state, Seat seat) => Board.ShrineCarrierWaiting(state, seat);

    // One legal spot for the seat's shrine, if its zone has any.
    public static Point2? SuggestedSpot(MatchState state, Seat seat) => Board.SuggestedShrine(state, seat);

    // What the Morale half test adds to the seat's warband size: 3 per shrine, at most 9.
    public static int MoraleBonus(MatchState state, Seat seat) => Board.ShrineMoraleBonus(state, seat);
}
