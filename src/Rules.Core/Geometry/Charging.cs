using System.Collections.Immutable;

namespace Rules.Core.Geometry;

// What a charge move did: the waypoints the model followed (empty if it could not move at all), what they cost,
// and whether it finished within 1" of its target (a successful charge, p. 37).
public sealed record ChargePlan(ImmutableArray<Point2> Path, Length Cost, bool Reached);

// The charge move (Movement pp. 35-37). The rules say the model "must move by the shortest route available that takes
// it directly towards the target", may avoid obstacles as long as "the route you use to avoid the obstacles is as short
// as possible", and "must stop as soon as it moves into contact" with the target. Everything below is a convention
// for those words; the U-numbers are in docs/interpretations.md.
//
// The route. On open ground the shortest route is the straight line from the charger's centre towards the target's
// centre (U46), cut into two legs at the first point within 1" of the target so that the first leg is judged against
// every enemy (Interposing Enemy Models, p. 36) and the tail only against bases and terrain. When terrain that cannot
// be crossed lies in the way (or a friendly base the model cannot move entirely past, U57), the route is the shortest
// path of the model's centre around it, found by a visibility-graph search (U58): the model's base is swept along
// straight legs between the corners of the blocking terrain and the edges of the blocking bases, each inflated by the
// base's radius. Round shapes are replaced by a 32-sided polygon placed just outside the true outline, so every
// route is legal by the exact tests of Movement.CheckPath and is within about 0.5% of the true geodesic length.
// Enemy models are not obstacles to the routing before the target's 1": a route that passes within 1" of another
// enemy is refused as interposed (p. 36) rather than bent around it (U59). Inside the target's 1" the other enemies'
// bases are avoided by ending the route at one of 32 contact points around the target (U60).
//
// Everything is integer arithmetic, with Int128 intermediates and fixed-point constants; the same functions plan the
// move and check the recorded event. Ties between equal-cost routes go to the route through the lower point in
// (X, Y) order, so the result never depends on the order the obstacles were supplied in (U61).
public static class Charging
{
    // A charging model's Movement cannot be raised above 12" by the Charge Bonus (p. 36).
    public static readonly Length MaxMovement = Length.FromInches(12);

    // A routed charge is at most this many legs. A shortest route wraps each obstacle once, and a wrap is at most
    // 17 legs around a 32-gon, so 64 legs is more than any route inside the 12" a charge can cover; a route that
    // would need more is treated as having no route.
    public const int MaxLegs = 64;

    private static readonly Length Unlimited = Length.FromInches(1000);

    // Movement.LegCostMicro counts millionths of an inch; the most a charge can pay.
    private static readonly Int128 BudgetMicro = (Int128)MaxMovement.Thousandths * Movement.MicroPerThousandth;

    // Can the model reach the target at all, whatever the Charge Bonus turns out to be? A refusal here is a refusal of
    // the declaration: no die is rolled for it. Friendly models are passable here (they cannot end a move on a base,
    // but whether one blocks depends on how far the model gets, which the die decides).
    public static MoveResult CheckRoute(Battlefield battlefield, ModelBody mover, ModelBody target, IReadOnlyList<Obstacle> others)
    {
        var scene = new Scene(battlefield, mover, target, others);
        var route = scene.FindRoute(friendsBlock: false);
        if (route is null)
        {
            return new MoveResult(false, MoveRefusal.NoRoute,
                "No route of 12\" or less reaches the target around the terrain in the way.", "1.0.2 pp. 35, 37", Length.Zero, null);
        }
        return scene.Interposition(route);
    }

    public static ChargePlan Plan(Battlefield battlefield, ModelBody mover, ModelBody target, Length reach, IReadOnlyList<Obstacle> others)
    {
        var scene = new Scene(battlefield, mover, target, others);
        var none = new ChargePlan([], Length.Zero, false);

        var routeA = scene.FindRoute(friendsBlock: false);
        if (routeA is null || !scene.Interposition(routeA).IsLegal)
        {
            return none;
        }
        var pathA = scene.Farthest(routeA, reach);
        if (pathA.IsEmpty)
        {
            return none;
        }
        if (scene.Finish(pathA, reach) is { } whole)
        {
            return whole;
        }

        // The route cannot be followed as far as the die allows without ending on a friendly base (or part-way
        // across one): the model may only cross a friend if it can move past entirely (p. 35), so the shortest
        // route that is available goes round the friendly bases.
        if (scene.HasFriends)
        {
            var routeB = scene.FindRoute(friendsBlock: true);
            if (routeB is not null && scene.Interposition(routeB).IsLegal)
            {
                var pathB = scene.Farthest(routeB, reach);
                if (!pathB.IsEmpty && scene.Finish(pathB, reach) is { } around)
                {
                    return around;
                }
            }
        }

        // No way round: stop short of the friendly base.
        return scene.BackOff(routeA, reach) ?? none;
    }

    // ---- fixed-point trigonometry: cos(k * pi / 16) in billionths, k = 0..8 (sin is the same table reversed) ----
    private static readonly long[] CosTable = [1_000_000_000, 980_785_280, 923_879_533, 831_469_612, 707_106_781, 555_570_233, 382_683_432, 195_090_322, 0];

    private static readonly long[] RingTable = [1_000_000_000, 998_795_456, 995_184_727, 989_176_510, 980_785_280, 970_031_253, 956_940_336, 941_544_065, 923_879_533, 903_989_293, 881_921_264, 857_728_610, 831_469_612, 803_207_531, 773_010_453, 740_951_125, 707_106_781, 671_558_955, 634_393_284, 595_699_304, 555_570_233, 514_102_744, 471_396_737, 427_555_093, 382_683_432, 336_889_853, 290_284_677, 242_980_180, 195_090_322, 146_730_474, 98_017_140, 49_067_674, 0];

    private const int Around = 32;   // vertices on a full circle
    private const int Contacts = 128;  // contact points around the target

    // 1 / cos(pi / 32) is 1.0048386; this is a little more, so the polygon's edges stay outside the circle.
    private const long SecantNum = 1_004_900;
    private const long SecantDen = 1_000_000;

    // The radius of the circle the polygon's corners lie on, for a polygon whose edges must keep at least `radius`
    // from the centre. The extra 3 thousandths absorb rounding the corners to the lattice (at most 0.71).
    private static long CornerRadius(long radius) => (radius * SecantNum + SecantDen - 1) / SecantDen + 3;

    // Corner j (0 to 32) of the polygon around a centre, j measured in steps of 11.25 degrees anticlockwise.
    private static Point2 Corner(Point2 centre, long cornerRadius, int j)
    {
        var q = j / 8 % 4;
        var k = j % 8;
        long c = CosTable[k];
        long s = CosTable[8 - k];
        var (cx, cy) = q switch
        {
            0 => (c, s),
            1 => (-s, c),
            2 => (-c, -s),
            _ => (s, -c),
        };
        return new Point2(
            Length.FromThousandths(centre.X.Thousandths + Scale(cornerRadius, cx)),
            Length.FromThousandths(centre.Y.Thousandths + Scale(cornerRadius, cy)));
    }

    // Contact point j (0 to 127) around a centre, in steps of 2.8125 degrees anticlockwise.
    private static Point2 RingPoint(Point2 centre, long radius, int j)
    {
        var q = j / 32 % 4;
        var k = j % 32;
        long c = RingTable[k];
        long s = RingTable[32 - k];
        var (cx, cy) = q switch
        {
            0 => (c, s),
            1 => (-s, c),
            2 => (-c, -s),
            _ => (s, -c),
        };
        return new Point2(
            Length.FromThousandths(centre.X.Thousandths + Scale(radius, cx)),
            Length.FromThousandths(centre.Y.Thousandths + Scale(radius, cy)));
    }

    // radius * fraction / 1e9, rounded to the nearest thousandth (half away from zero).
    private static long Scale(long radius, long fraction)
    {
        var n = (Int128)radius * fraction;
        var half = (Int128)500_000_000;
        return (long)(n >= 0 ? (n + half) / 1_000_000_000 : -((-n + half) / 1_000_000_000));
    }

    // One route: the waypoints from the charger's centre to contact, and, when it is the straight line, that line
    // (kept so an unobstructed charge is planned exactly as before).
    private sealed record Route(ImmutableArray<Point2> Path, Line? Direct);

    // The charge in its surroundings.
    private sealed class Scene
    {
        private readonly Battlefield _field;
        private readonly ModelBody _mover;
        private readonly ModelBody _target;
        private readonly IReadOnlyList<Obstacle> _others;
        private readonly List<TerrainBlock> _blocking;
        private readonly List<Footprint> _difficult;
        private readonly List<ModelBody> _otherEnemies;
        private readonly List<ModelBody> _friends;
        private readonly List<Obstacle> _enemyObstacles;
        private readonly Length _zone;

        public Scene(Battlefield field, ModelBody mover, ModelBody target, IReadOnlyList<Obstacle> others)
        {
            ArgumentNullException.ThrowIfNull(field);
            ArgumentNullException.ThrowIfNull(mover);
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(others);
            _field = field;
            _mover = mover;
            _target = target;
            _others = others;
            _blocking = field.Terrain.Where(t => !t.IsWalkable).ToList();
            _difficult = field.Terrain.Where(t => t.IsDifficult).Select(t => t.Footprint).ToList();
            var sorted = others.Where(o => o.Body.Id != mover.Id).OrderBy(o => o.Body.Id, StringComparer.Ordinal).ToList();
            _enemyObstacles = sorted.Where(o => o.Relation == Relation.Enemy).ToList();
            _otherEnemies = _enemyObstacles.Where(o => o.Body.Id != target.Id).Select(o => o.Body).ToList();
            _friends = sorted.Where(o => o.Relation == Relation.Friendly).Select(o => o.Body).ToList();
            _zone = mover.Radius + target.Radius + Length.Inch;
        }

        public bool HasFriends => _friends.Count > 0;

        // ---- finding the route ----

        public Route? FindRoute(bool friendsBlock)
        {
            var line = Line.Of(_mover, _target);
            var direct = line.FullPath;
            if (GoalLegal(_mover.Centre, direct, friendsBlock))
            {
                return new Route(direct, line);
            }
            return GraphRoute(friendsBlock);
        }

        // The route is legal as a charge (terrain, bases, the 1" of other enemies before the target's 1"): the
        // exact test of Movement.CheckPath against the enemies. A route through another enemy's 1" is refused as
        // interposed (p. 36).
        public MoveResult Interposition(Route route)
        {
            var result = Movement.CheckPath(_field, _mover, route.Path, Unlimited, _enemyObstacles, _target.Id);
            if (!result.IsLegal && result.Reason is MoveRefusal.EntersEnemyZone or MoveRefusal.CrossesEnemyBase)
            {
                return result with
                {
                    Text = "Another enemy model is in the way of the shortest route: it must be chosen as the target of the charge, or the charge is not made.",
                    RuleRef = "1.0.2 p. 36",
                };
            }
            return result;
        }

        // May the model follow these legs from `from` to a contact point, judged without the other enemies' 1"
        // (that is Interposition's job) but with their bases inside the target's 1"?
        private bool GoalLegal(Point2 from, ImmutableArray<Point2> legs, bool friendsBlock)
        {
            var previous = from;
            for (var i = 0; i < legs.Length; i++)
            {
                var tail = ExactMath.Near(previous, _target.Centre, _zone, strict: false);
                if (!LegFree(previous, legs[i], friendsBlock) || tail && !TailFree(previous, legs[i], friendsBlock))
                {
                    return false;
                }
                previous = legs[i];
            }
            return _field.Area.MoreThanHalfInside(legs[^1], _mover.Radius) == AreaDecision.MoreThanHalf;
        }

        // Terrain that cannot be crossed (and, when asked, the bases of friendly models) along one leg.
        private bool LegFree(Point2 a, Point2 b, bool friendsBlock)
        {
            foreach (var terrain in _blocking)
            {
                if (terrain.Footprint.SweptCircleOverlaps(a, b, _mover.Radius))
                {
                    return false;
                }
            }
            if (friendsBlock)
            {
                foreach (var friend in _friends)
                {
                    if (Shapes.PointNearSegment(friend.Centre, a, b, _mover.Radius + friend.Radius, strict: true))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        // Inside the target's 1" the other enemies' bases (and the target's own) cannot be crossed.
        private bool TailFree(Point2 a, Point2 b, bool friendsBlock)
        {
            foreach (var enemy in _otherEnemies)
            {
                if (Shapes.PointNearSegment(enemy.Centre, a, b, _mover.Radius + enemy.Radius, strict: true))
                {
                    return false;
                }
            }
            return !Shapes.PointNearSegment(_target.Centre, a, b, _mover.Radius + _target.Radius, strict: true);
        }

        // ---- the visibility graph ----

        private Route? GraphRoute(bool friendsBlock)
        {
            var nodes = Vertices(friendsBlock);
            var count = nodes.Count;
            var difficult = _difficult;
            var dist = new Int128[count];
            var previous = new int[count];
            var legsInto = new ImmutableArray<Point2>[count];
            var settled = new bool[count];
            Array.Fill(dist, Int128.MaxValue);
            Array.Fill(previous, -1);
            dist[0] = 0;

            while (true)
            {
                var u = -1;
                for (var i = 0; i < count; i++)
                {
                    if (!settled[i] && dist[i] != Int128.MaxValue && (u < 0 || dist[i] < dist[u]))
                    {
                        u = i;
                    }
                }
                if (u < 0)
                {
                    break;
                }
                settled[u] = true;
                for (var v = 0; v < count; v++)
                {
                    if (settled[v] || !TryEdge(nodes[u], nodes[v], friendsBlock, out var legs))
                    {
                        continue;
                    }
                    var alt = dist[u] + LegsCost(nodes[u], legs);
                    if (alt > BudgetMicro)
                    {
                        continue;
                    }
                    if (alt < dist[v] || alt == dist[v] && u < previous[v])
                    {
                        dist[v] = alt;
                        previous[v] = u;
                        legsInto[v] = legs;
                    }
                }
            }

            // The best way into contact from any node the search reached. A node whose distance so far plus the
            // straight distance still to go cannot beat the best found is not examined. Ties go to the lower
            // node and then the lower contact point, so the order of the search does not matter.
            Int128 best = Int128.MaxValue;
            var bestNode = -1;
            var bestKey = -1;
            ImmutableArray<Point2> bestTail = default;
            var contactRadius = (_mover.Radius + _target.Radius).Thousandths + 2;
            var order = Enumerable.Range(0, count).Where(a => settled[a])
                .Select(a => (Node: a, Bound: dist[a] + Int128.Max(0, ExactMath.FloorSqrt(ExactMath.DistanceSquared(nodes[a], _target.Centre) * (Int128)1_000_000) - (Int128)contactRadius * 1000)))
                .OrderBy(x => x.Bound).ThenBy(x => x.Node).ToList();
            foreach (var (a, bound) in order)
            {
                if (bound > best)
                {
                    break;
                }
                foreach (var (key, tail) in Goals(nodes[a], friendsBlock))
                {
                    var total = dist[a] + LegsCost(nodes[a], tail);
                    if (total <= BudgetMicro && (total < best || total == best && (a, key).CompareTo((bestNode, bestKey)) < 0))
                    {
                        best = total;
                        bestNode = a;
                        bestKey = key;
                        bestTail = tail;
                    }
                }
            }
            if (bestNode < 0)
            {
                return null;
            }

            var path = new List<Point2>(bestTail);
            for (var n = bestNode; n > 0; n = previous[n])
            {
                path.InsertRange(0, legsInto[n]);
            }
            return path.Count > MaxLegs ? null : new Route([.. path], null);
        }

        private Int128 LegsCost(Point2 from, ImmutableArray<Point2> legs)
        {
            Int128 total = 0;
            var previous = from;
            foreach (var leg in legs)
            {
                total += Movement.LegCostMicro(previous, leg, _difficult);
                previous = leg;
            }
            return total;
        }

        // The legs of the straight move between two graph nodes, if it is allowed: not through blocking terrain, not
        // across a friendly base when friends block, and not into the target's 1" except by being split where the
        // model first comes within it, after which only bases and terrain count (p. 37). A model within the target's
        // 1" does not leave it again.
        private bool TryEdge(Point2 a, Point2 b, bool friendsBlock, out ImmutableArray<Point2> legs)
        {
            legs = default;
            if (a == b)
            {
                return false;
            }
            var from = InZone(a);
            var to = InZone(b);
            if (from && !to)
            {
                return false;
            }
            if (!from && !to)
            {
                if (!LegFree(a, b, friendsBlock) || Shapes.PointNearSegment(_target.Centre, a, b, _zone, strict: false))
                {
                    return false;
                }
                legs = [b];
                return true;
            }
            var split = from ? [b] : LegsTo(a, b);
            if (!GoalLegal(a, split, friendsBlock))
            {
                return false;
            }
            legs = split;
            return true;
        }

        private bool InZone(Point2 p) => ExactMath.Near(p, _target.Centre, _zone, strict: false);

        // Contact legs from a node: straight at the target if that is free (key -1), otherwise to each of the
        // contact points around it (key = the point's number).
        private IEnumerable<(int Key, ImmutableArray<Point2> Legs)> Goals(Point2 from, bool friendsBlock)
        {
            var line = Line.Of(_mover.At(from), _target);
            var radial = line.FullPath;
            if (line.Contact > 0 && GoalLegal(from, radial, friendsBlock))
            {
                yield return (-1, radial);
                yield break;
            }
            var radius = (_mover.Radius + _target.Radius).Thousandths + 1;
            for (var j = 0; j < Contacts; j++)
            {
                var legs = LegsTo(from, RingPoint(_target.Centre, radius, j));
                if (GoalLegal(from, legs, friendsBlock))
                {
                    yield return (j, legs);
                }
            }
        }

        // The legs from a node to a contact point, split where the model first comes within 1" of the target.
        private ImmutableArray<Point2> LegsTo(Point2 from, Point2 contact)
        {
            if (ExactMath.Near(from, _target.Centre, _zone, strict: false))
            {
                return [contact];
            }
            var leg = new Leg(from, contact);
            var low = 0L;
            var high = leg.Span;
            while (low < high)
            {
                var mid = (low + high) / 2;
                if (ExactMath.Near(leg.At(mid), _target.Centre, _zone, strict: false))
                {
                    high = mid;
                }
                else
                {
                    low = mid + 1;
                }
            }
            var entry = leg.At(low);
            return entry == contact ? [contact] : [entry, contact];
        }

        // The start, then the corners of everything the model has to go round, nearest 12" only, in (X, Y) order.
        private List<Point2> Vertices(bool friendsBlock)
        {
            var radius = _mover.Radius.Thousandths;
            var found = new SortedSet<(long X, long Y)>();
            void Offer(Point2 p)
            {
                if (ValidWaypoint(p, friendsBlock))
                {
                    found.Add((p.X.Thousandths, p.Y.Thousandths));
                }
            }

            var cornerRadius = CornerRadius(radius);
            foreach (var terrain in _blocking)
            {
                var r = terrain.Footprint.Bounds;
                // Each corner of the rectangle swept by the base is a quarter circle, from the straight edge
                // out to the next one.
                for (var j = 0; j <= 8; j++)
                {
                    Offer(Corner(r.Max, cornerRadius, j));
                    Offer(Corner(new Point2(r.Min.X, r.Max.Y), cornerRadius, 8 + j));
                    Offer(Corner(r.Min, cornerRadius, 16 + j));
                    Offer(Corner(new Point2(r.Max.X, r.Min.Y), cornerRadius, 24 + j));
                }
            }
            if (friendsBlock)
            {
                foreach (var friend in _friends)
                {
                    var around = CornerRadius((_mover.Radius + friend.Radius).Thousandths);
                    for (var j = 0; j < Around; j++)
                    {
                        Offer(Corner(friend.Centre, around, j));
                    }
                }
            }

            var nodes = new List<Point2>(found.Count + 1) { _mover.Centre };
            foreach (var (x, y) in found)
            {
                var p = new Point2(Length.FromThousandths(x), Length.FromThousandths(y));
                if (p != _mover.Centre)
                {
                    nodes.Add(p);
                }
            }
            return nodes;
        }

        // May the model's centre be here between legs: on the table, within 12" of the start, clear of blocking
        // terrain, not on the target's base, clear of the other enemies' bases inside the target's 1", and (when
        // friends block) clear of friendly bases?
        private bool ValidWaypoint(Point2 p, bool friendsBlock)
        {
            if (!ExactMath.Near(p, _mover.Centre, MaxMovement, strict: false)
                || _field.Area.MoreThanHalfInside(p, _mover.Radius) != AreaDecision.MoreThanHalf
                || ExactMath.Near(p, _target.Centre, _mover.Radius + _target.Radius, strict: true))
            {
                return false;
            }
            if (_blocking.Any(t => t.Footprint.CircleOverlaps(p, _mover.Radius)))
            {
                return false;
            }
            if (InZone(p) && _otherEnemies.Any(e => ExactMath.Near(p, e.Centre, _mover.Radius + e.Radius, strict: true)))
            {
                return false;
            }
            return !friendsBlock || _friends.All(f => !ExactMath.Near(p, f.Centre, _mover.Radius + f.Radius, strict: true));
        }

        // ---- following a route ----

        // The path of a model that follows the route as far as its movement goes.
        public ImmutableArray<Point2> Farthest(Route route, Length reach)
        {
            if (route.Direct is { } line)
            {
                // The farthest distance along the line the allowance reaches (cost never decreases along the line).
                long low = 0;
                long high = line.Contact;
                while (low < high)
                {
                    var mid = (low + high + 1) / 2;
                    if (Movement.Cost(_field, _mover, [line.At(mid)]) <= reach)
                    {
                        low = mid;
                    }
                    else
                    {
                        high = mid - 1;
                    }
                }
                return low == 0 ? [] : line.PathTo(low);
            }
            return Along(route.Path, reach);
        }

        // Walks the polyline until the allowance runs out, part-way along the leg it runs out on.
        private ImmutableArray<Point2> Along(ImmutableArray<Point2> route, Length reach)
        {
            var budget = (Int128)reach.Thousandths * Movement.MicroPerThousandth;
            Int128 spent = 0;
            var walked = new List<Point2>();
            var from = _mover.Centre;
            foreach (var to in route)
            {
                var cost = Movement.LegCostMicro(from, to, _difficult);
                if (spent + cost <= budget)
                {
                    walked.Add(to);
                    spent += cost;
                    from = to;
                    continue;
                }
                var leg = new Leg(from, to);
                long low = 0;
                long high = leg.Span;
                while (low < high)
                {
                    var mid = (low + high + 1) / 2;
                    if (spent + Movement.LegCostMicro(from, leg.At(mid), _difficult) <= budget)
                    {
                        low = mid;
                    }
                    else
                    {
                        high = mid - 1;
                    }
                }
                if (low > 0)
                {
                    walked.Add(leg.At(low));
                }
                break;
            }
            return [.. walked];
        }

        // The plan for a path, if it is legal against everything on the table (a base may not be ended on).
        public ChargePlan? Finish(ImmutableArray<Point2> path, Length reach)
        {
            var result = Movement.CheckPath(_field, _mover, path, reach, _others, _target.Id);
            if (!result.IsLegal)
            {
                return null;
            }
            var reached = ExactMath.Near(path[^1], _target.Centre, _zone, strict: false);
            return new ChargePlan(path, result.Cost, reached);
        }

        // Stops short of whatever stops the full move from being legal, as far along the route as it can.
        public ChargePlan? BackOff(Route route, Length reach)
        {
            if (route.Direct is { } line)
            {
                long low = 0;
                long high = line.Contact;
                while (low < high)
                {
                    var mid = (low + high + 1) / 2;
                    if (Movement.Cost(_field, _mover, [line.At(mid)]) <= reach)
                    {
                        low = mid;
                    }
                    else
                    {
                        high = mid - 1;
                    }
                }
                for (var stop = low - 1; stop > 0; stop--)
                {
                    if (Finish(line.PathTo(stop), reach) is { } plan)
                    {
                        return plan;
                    }
                }
                return null;
            }
            for (var back = 1L; back < reach.Thousandths; back++)
            {
                var path = Along(route.Path, Length.FromThousandths(reach.Thousandths - back));
                if (path.IsEmpty)
                {
                    return null;
                }
                if (Finish(path, reach) is { } plan)
                {
                    return plan;
                }
            }
            return null;
        }
    }

    // A straight leg between two points, in thousandths of an inch of travel.
    private sealed class Leg
    {
        private readonly Point2 _from;
        private readonly Point2 _to;
        private readonly long _dx;
        private readonly long _dy;
        private readonly Int128 _scaled;   // the length in millionths of an inch, rounded down

        public Leg(Point2 from, Point2 to)
        {
            _from = from;
            _to = to;
            _dx = to.X.Thousandths - from.X.Thousandths;
            _dy = to.Y.Thousandths - from.Y.Thousandths;
            _scaled = ExactMath.FloorSqrt(((Int128)_dx * _dx + (Int128)_dy * _dy) * 1_000_000);
            Span = (long)(_scaled / 1000);
        }

        public long Span { get; }

        // The lattice point nearest the one that far along the leg; the end of the leg itself at its full length.
        public Point2 At(long distance)
        {
            if (distance <= 0 || _scaled == 0)
            {
                return _from;
            }
            if (distance >= Span)
            {
                return _to;
            }
            return new Point2(
                Length.FromThousandths(_from.X.Thousandths + Offset(_dx, distance)),
                Length.FromThousandths(_from.Y.Thousandths + Offset(_dy, distance)));
        }

        private long Offset(long delta, long distance)
        {
            var numerator = (Int128)delta * distance * 1000;
            var half = _scaled / 2;
            var rounded = numerator >= 0 ? (numerator + half) / _scaled : -((-numerator + half) / _scaled);
            return (long)rounded;
        }
    }

    // The straight line from the charger towards the target's centre, in thousandths of an inch of travel.
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
