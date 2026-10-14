using System.Collections.Immutable;

namespace Rules.Core.Geometry;

// What a charge move did: the waypoints the model followed (empty if it could not move at all), what they cost,
// and whether it finished within 1" of its target (a successful charge, p. 37).
public sealed record ChargePlan(ImmutableArray<Point2> Path, Length Cost, bool Reached);

// The charge move (Movement pp. 36-37). The rules say the model "must move by the shortest route available that takes
// it directly towards the target" and "must stop as soon as it moves into contact". The route is therefore a single
// straight line from the charger's centre towards the target's centre (U46), cut where the charge runs out of
// movement, where it would touch the target, or where it would end on a friendly base. Contact between two round
// bases is not a whole number of thousandths in general, so the stop is the last point that does not overlap the
// target (U47). The line is cut into two legs at the first point within 1" of the target: the first leg is
// checked against every enemy (Interposing Enemy Models, p. 36), the tail only against bases and terrain.
// Everything is integer arithmetic; the same functions plan the move and check the recorded event.
public static class Charging
{
    // A charging model's Movement cannot be raised above 12" by the Charge Bonus (p. 36).
    public static readonly Length MaxMovement = Length.FromInches(12);

    private static readonly Length Unlimited = Length.FromInches(1000);

    // Can the straight route all the way to the target be moved along at all, whatever the Charge Bonus turns out to be?
    // A refusal here is a refusal of the declaration: no die is rolled for it.
    public static MoveResult CheckRoute(Battlefield battlefield, ModelBody mover, ModelBody target, IReadOnlyList<Obstacle> others)
    {
        var line = Line.Of(mover, target);
        // Where the charge ends depends on the Charge Bonus, so a friendly base in the way is dealt with by stopping
        // short of it when the move is planned, not by refusing the declaration.
        return Movement.CheckPath(battlefield, mover, line.FullPath, Unlimited, others.Where(o => o.Relation == Relation.Enemy).ToList(), target.Id);
    }

    public static ChargePlan Plan(Battlefield battlefield, ModelBody mover, ModelBody target, Length reach, IReadOnlyList<Obstacle> others)
    {
        var line = Line.Of(mover, target);
        var friends = others.Where(o => o.Relation == Relation.Friendly).Select(o => o.Body).ToList();

        // The farthest distance along the line the allowance reaches (cost never decreases along the line).
        long low = 0;
        long high = line.Contact;
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (Movement.Cost(battlefield, mover, [line.At(mid)]) <= reach)
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }

        var stop = low;
        while (stop > 0)
        {
            var point = line.At(stop);
            var onFriend = friends.Any(f => ExactMath.Near(point, f.Centre, mover.Radius + f.Radius, strict: true));
            if (!onFriend && Movement.CheckPath(battlefield, mover, line.PathTo(stop), reach, others, target.Id).IsLegal)
            {
                break;
            }
            stop--;
        }
        if (stop == 0)
        {
            return new ChargePlan([], Length.Zero, false);
        }

        var path = line.PathTo(stop);
        var cost = Movement.CheckPath(battlefield, mover, path, reach, others, target.Id).Cost;
        var reached = ExactMath.Near(path[^1], target.Centre, mover.Radius + target.Radius + Length.Inch, strict: false);
        return new ChargePlan(path, cost, reached);
    }

    // The straight line from the charger towards the target, in thousandths of an inch of travel.
    private sealed class Line
    {
        private readonly Point2 _from;
        private readonly long _dx;
        private readonly long _dy;
        private readonly Int128 _scaled;   // the centre distance in millionths of an inch, rounded down

        private Line(Point2 from, long dx, long dy, Int128 scaled, long engage, long contact)
        {
            _from = from;
            _dx = dx;
            _dy = dy;
            _scaled = scaled;
            Engage = engage;
            Contact = contact;
        }

        // The distance at which the model is first within 1" of the target, and the last distance at which it
        // does not overlap the target.
        public long Engage { get; }
        public long Contact { get; }

        public static Line Of(ModelBody mover, ModelBody target)
        {
            var dx = target.Centre.X.Thousandths - mover.Centre.X.Thousandths;
            var dy = target.Centre.Y.Thousandths - mover.Centre.Y.Thousandths;
            var squared = (Int128)dx * dx + (Int128)dy * dy;
            var scaled = ExactMath.FloorSqrt(squared * 1_000_000);
            var probe = new Line(mover.Centre, dx, dy, scaled, 0, 0);
            var reach = mover.Radius + target.Radius;

            // Contact: step back from the nominal distance until the base no longer overlaps the target.
            var contact = (long)(scaled / 1000) - reach.Thousandths;
            while (contact > 0 && ExactMath.Near(probe.At(contact), target.Centre, reach, strict: true))
            {
                contact--;
            }
            contact = Math.Max(contact, 0);

            // Engagement: the first distance within 1" of the target, found by bisection (the distance to the
            // target only falls along the line).
            long low = 0;
            long high = contact;
            while (low < high)
            {
                var mid = (low + high) / 2;
                if (ExactMath.Near(probe.At(mid), target.Centre, reach + Length.Inch, strict: false))
                {
                    high = mid;
                }
                else
                {
                    low = mid + 1;
                }
            }
            return new Line(mover.Centre, dx, dy, scaled, low, contact);
        }

        public Point2 At(long distance)
        {
            if (_scaled == 0)
            {
                return _from;
            }
            return new Point2(
                Length.FromThousandths(_from.X.Thousandths + Offset(_dx, distance)),
                Length.FromThousandths(_from.Y.Thousandths + Offset(_dy, distance)));
        }

        // delta * distance / length, rounded to the nearest thousandth, all in Int128.
        private long Offset(long delta, long distance)
        {
            var numerator = (Int128)delta * distance * 1000;
            var half = _scaled / 2;
            var rounded = numerator >= 0 ? (numerator + half) / _scaled : -((-numerator + half) / _scaled);
            return (long)rounded;
        }

        // The legs to stop at the given distance: first to the engagement point, then on to the stop.
        public ImmutableArray<Point2> PathTo(long stop) =>
            stop > Engage ? [At(Engage), At(stop)] : [At(stop)];

        public ImmutableArray<Point2> FullPath => PathTo(Contact);
    }
}
