using System.Collections.Immutable;

namespace Rules.Core.Geometry;

// Roster stage R9a (U134): a move an ACTION makes another model take (Loudspeakers, Puppet Master), as opposed to a Move the
// model makes itself. The model's base is carried in a straight line from its centre towards a point, up to a distance, and
// stops at the last place the line is clear: it never overlaps another base or terrain it cannot cross, never ends with more
// than half of it off the table, and never enters a Mine Marker's reach (a forced move triggers no mine, so it stops short of
// one). Zones of enemy models do not matter: the model is moved, it does not walk. Bases at another height than the mover
// are not in its way. A model standing on a tower or platform is not moved (U134).
public static class ForcedMoves
{
    // Where the base ends. `others` are the other bases on the table; `markers` the Mine Markers in play.
    public static Point2 Stop(
        Battlefield battlefield,
        ModelBody mover,
        Point2 toward,
        Length max,
        IReadOnlyList<ModelBody> others,
        IEnumerable<MineMarker> markers)
    {
        ArgumentNullException.ThrowIfNull(battlefield);
        ArgumentNullException.ThrowIfNull(mover);
        ArgumentNullException.ThrowIfNull(others);
        ArgumentNullException.ThrowIfNull(markers);
        var from = mover.Centre;
        var dx = toward.X.Thousandths - from.X.Thousandths;
        var dy = toward.Y.Thousandths - from.Y.Thousandths;
        if ((dx == 0 && dy == 0) || max <= Length.Zero || mover.Elevation != Length.Zero)
        {
            return from;
        }
        var scaled = ExactMath.FloorSqrt(((Int128)dx * dx + (Int128)dy * dy) * 1_000_000);

        // The lattice point nearest the one that far along the line (thousandths of an inch of travel).
        Point2 At(long distance)
        {
            if (distance <= 0)
            {
                return from;
            }
            return new Point2(
                Length.FromThousandths(from.X.Thousandths + Offset(dx, distance)),
                Length.FromThousandths(from.Y.Thousandths + Offset(dy, distance)));
        }

        long Offset(long delta, long distance)
        {
            var numerator = (Int128)delta * distance * 1000;
            var half = scaled / 2;
            return (long)(numerator >= 0 ? (numerator + half) / scaled : -((-numerator + half) / scaled));
        }

        var blocking = battlefield.Terrain.Where(t => !t.IsWalkable || t.IsTower).ToList();
        var level = others.Where(o => o.Elevation == mover.Elevation).ToList();
        var reach = mover.Radius;
        var mines = markers.ToList();

        bool Clear(long distance)
        {
            var to = At(distance);
            if (battlefield.Area.MoreThanHalfInside(mover.At(to)) != AreaDecision.MoreThanHalf)
            {
                return false;
            }
            if (blocking.Any(t => t.Footprint.SweptBodyOverlaps(mover, from, to)))
            {
                return false;
            }
            return !level.Any(o => Shapes.SweptNear(mover, from, to, o, reach + o.Radius, strict: true));
        }

        bool NoMine(long distance) => mines.Count == 0 || Mines.Stops(battlefield, mover, [At(distance)], mines, others).IsEmpty;

        // Both tests only get harder further along the line, so the last clear distance is found by halving.
        var best = max.Thousandths;
        long Last(Func<long, bool> ok, long limit)
        {
            if (ok(limit))
            {
                return limit;
            }
            long lo = 0, hi = limit;
            while (hi - lo > 1)
            {
                var mid = (lo + hi) / 2;
                if (ok(mid))
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }
            return lo;
        }

        var clear = Last(Clear, best);
        var free = Last(d => NoMine(d), clear);
        return At(free);
    }

    // The nearest of the models, by the gap between bases (ties to the lowest id), or null when there is none.
    public static ModelBody? Nearest(ModelBody from, IEnumerable<ModelBody> candidates) =>
        candidates.OrderBy(c => Measure.Gap(from, c)).ThenBy(c => c.Id, StringComparer.Ordinal).FirstOrDefault();
}
