namespace Rules.Core.Geometry;

// Heights for the height-based line of sight (line-of-sight plan, stage L1, approved by Jason on 2026-10-25). The book
// prints no model or terrain heights, so these are software choices (U77): a figure 1.2" tall, the shooter's eye at
// 1.1" above its base, and terrain as boxes up to a height from its profile. The client draws terrain at the same
// profile heights (Client.Core BoardScene.DisplayHeight).
public static class SightHeights
{
    public static readonly Length Low = Length.Parse("0.25");
    public static readonly Length Cover = Length.Parse("0.75");
    public static readonly Length Wall = Length.FromInches(3);

    // The shooter's eye, above its base.
    public static readonly Length Eye = Length.Parse("1.1");

    // The heights of the target's sample points above its base: legs, body, head.
    public static readonly Length[] Points = [Length.Parse("0.3"), Length.Parse("0.7"), Length.Parse("1.1")];

    // How far out the side points sit, in thousandths of the base radius.
    public const long SideThousandths = 800;

    // The top of a piece: a tower's climb height, else its profile's height. Every piece is a box from the ground up,
    // except a trench, which has nothing above the ground (its floor is below it, U83).
    public static Length TopOf(TerrainBlock piece)
    {
        ArgumentNullException.ThrowIfNull(piece);
        if (piece.IsTrench)
        {
            return Length.Zero;
        }
        if (piece.Climb is { } climb)
        {
            return climb.Height;
        }
        return piece.Profile switch
        {
            TerrainProfile.Low => Low,
            TerrainProfile.Cover => Cover,
            _ => Wall,
        };
    }

    // Elevated position (p. 43): the attacker is at least 3" higher than the target, measured by their bases.
    public static readonly Length ElevatedBy = Length.FromInches(3);

    // Is the attacker's base at least 3" higher than the target's (p. 43, line-of-sight stage L3)? A separate fact from
    // sight and cover; it adds +1 DICE to a Ranged Attack. Bases stand at the heights StandingHeight gives.
    public static bool IsElevated(Battlefield battlefield, ModelBody attacker, ModelBody target)
    {
        ArgumentNullException.ThrowIfNull(battlefield);
        var a = StandingHeight(battlefield, attacker, Elevation.Of(battlefield, attacker));
        var t = StandingHeight(battlefield, target, Elevation.Of(battlefield, target));
        return a - t >= ElevatedBy;
    }

    // How high the base stands: its own elevation (a tower's height), the top of the walkable High piece it is on, or
    // the trench floor, below the ground, when it is in a trench (U81).
    public static Length StandingHeight(Battlefield battlefield, ModelBody body, ModelPosition position)
    {
        ArgumentNullException.ThrowIfNull(battlefield);
        ArgumentNullException.ThrowIfNull(body);
        if (position.Level == ModelLevel.High && position.PlatformId is { } id && battlefield.Terrain.FirstOrDefault(t => t.Id == id) is { } platform)
        {
            var top = TopOf(platform);
            return top > body.Elevation ? top : body.Elevation;
        }
        if (body.Elevation == Length.Zero && Trenches.Of(battlefield, body) is not null)
        {
            return Length.Zero - battlefield.TrenchDepth!.Value;
        }
        return body.Elevation;
    }
}

// Height-based line of sight (line-of-sight plan, stage L1). Since stage L2 it is the rules' line of sight: Cover.Of
// asks it, and shots, forced shots, charges, Fight and the client's previews all go through Cover.Of.
//  - Rays run from the attacker's eye (above its base centre) to nine points on the target: three heights (Points)
//    times left, middle and right, set across the line between the two models at 0.8 of the base radius.
//  - A ray is blocked by a piece when it passes through the inside of the piece's box: its footprint, from the ground
//    up to TopOf. Grazing a face, edge or corner does not count (as U13). All maths is exact, in thousandths.
//  - Other models never block (U3). The platform a model stands on never blocks sight to or from it (U19). There is
//    no high-ground shortcut (U20 is replaced by the heights): a raised eye sees over a piece only if it is low enough.
//  - Clear (Full) when all nine points are seen, Partial when some are, Blocked (None) when none are.
//  - Trench-maps stage S3 (U83): the ground is solid below the table's surface, except inside trenches down to their
//    floor. A ray through solid ground is blocked (grazing a trench's side or rim does not count). The blocker is named
//    as the target's trench when the edge of the trench the target is in hides the point, else the shooter's trench
//    when the edge of its own trench does, else GroundId. With no trench on the battlefield nothing here changes.
public static class HeightSight
{
    // Names solid ground in SightRay.Obstructing when no trench's edge can be named. Not a valid terrain id (it has a
    // space), so it never clashes with a piece.
    public const string GroundId = "the ground";

    // The rules' sight (U77, U95). The nine rays first; when they see nothing, the gap search (WithGapSearch) looks
    // between them, and a gap it finds makes the target partly seen: its result then holds the nine rays and the extra
    // ones, and counts the clear extra rays as the visible ones. Full and Partial answers are always the nine rays'.
    public static SightResult Between(Battlefield battlefield, ModelBody attacker, ModelBody target)
    {
        var searched = WithGapSearch(battlefield, attacker, target);
        if (searched.Visibility == searched.Nine.Visibility)
        {
            return searched.Nine;
        }
        var nine = searched.Nine;
        var rays = nine.Rays.Concat(searched.Extra).ToList();
        return new SightResult(Visibility.Partial, rays.Count(r => r.Obstructing.Count == 0), rays.Count, rays, nine.Attacker, nine.Target);
    }

    // The nine rays alone (stage L1 to 2b-1's sight).
    internal static SightResult NineRays(Battlefield battlefield, ModelBody attacker, ModelBody target)
    {
        ArgumentNullException.ThrowIfNull(battlefield);
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(target);

        var from = Elevation.Of(battlefield, attacker);
        var to = Elevation.Of(battlefield, target);
        var eye = SightHeights.StandingHeight(battlefield, attacker, from) + SightHeights.Eye;
        var foot = SightHeights.StandingHeight(battlefield, target, to);
        var rays = new List<SightRay>();
        var points = Sample(attacker, target);
        var attackerTrench = eye - SightHeights.Eye < Length.Zero ? Trenches.Of(battlefield, attacker)?.Id : null;
        var targetTrench = foot < Length.Zero ? Trenches.Of(battlefield, target)?.Id : null;
        // Order: legs, body, head, each left to right as the attacker sees it.
        foreach (var height in SightHeights.Points)
        {
            foreach (var point in points)
            {
                rays.Add(Ray(battlefield, attacker.Centre, eye, point, foot + height, from, to, attackerTrench, targetTrench));
            }
        }
        var visible = rays.Count(r => r.Obstructing.Count == 0);
        var kind = visible == 0 ? Visibility.None : visible == rays.Count ? Visibility.Full : Visibility.Partial;
        return new SightResult(kind, visible, rays.Count, rays, from, to);
    }

    // Line-of-sight stage L8: sight to a point on the battlefield or on a terrain piece (p. 30). The point is 1 mm across
    // and 1 mm high (a base of 0.02" radius, 0.039" tall) standing where a base there would stand: the ground, the top
    // of the walkable High piece under it, or a trench floor (U81). It is seen if any part of it is; with terrain as
    // boxes from the ground up, the top of the point is seen whenever any lower part is, so one ray, from the eye to its
    // top centre, decides (U92). The ray is blocked as Between's rays are. No scenario this build plays has Markers yet.
    public static readonly Length PointRadius = Length.FromThousandths(20);
    public static readonly Length PointHeight = Length.FromThousandths(39);

    public static SightRay ToPoint(Battlefield battlefield, ModelBody attacker, Point2 point)
    {
        ArgumentNullException.ThrowIfNull(battlefield);
        ArgumentNullException.ThrowIfNull(attacker);
        var marker = new ModelBody("point", point, PointRadius);
        var from = Elevation.Of(battlefield, attacker);
        var to = Elevation.Of(battlefield, marker);
        var eye = SightHeights.StandingHeight(battlefield, attacker, from) + SightHeights.Eye;
        var foot = SightHeights.StandingHeight(battlefield, marker, to);
        var top = foot + PointHeight;
        var attackerTrench = eye - SightHeights.Eye < Length.Zero ? Trenches.Of(battlefield, attacker)?.Id : null;
        var targetTrench = foot < Length.Zero ? Trenches.Of(battlefield, marker)?.Id : null;
        var crossed = battlefield.Terrain.Where(t => t.Footprint.SegmentCrossesInside(attacker.Centre, point)).ToList();
        var obstructing = crossed
            .Where(t => t.Id != from.PlatformId && t.Id != to.PlatformId)
            .Where(t => t.Footprint.SegmentCrossesBoxInside(attacker.Centre, eye, point, top, Length.Zero, SightHeights.TopOf(t)))
            .Select(t => t.Id)
            .ToArray();
        if (battlefield.TrenchDepth is { } depth && GroundBlock(battlefield, depth, attacker.Centre, eye, point, top, attackerTrench, targetTrench) is { } ground)
        {
            obstructing = [.. obstructing.Append(ground).Order(StringComparer.Ordinal)];
        }
        return new SightRay(attacker.Centre, point, crossed.Select(t => t.Id).ToArray(), obstructing) { FromHeight = eye, ToHeight = top };
    }

    public static bool SeesPoint(Battlefield battlefield, ModelBody attacker, Point2 point) => ToPoint(battlefield, attacker, point).Obstructing.Count == 0;

    // Line-of-sight stage 2b-1 (2026-10-26; the rules' sight through Between since stage 2b-2, U95): when the nine rays see nothing,
    // look for a gap between them. Seen from above, from the attacker, the pieces in front of the target can only start
    // or stop hiding it where a line from the attacker passes a corner of a piece, or where an edge of a piece meets the
    // line between the target's two side points (the left and right columns, 0.8 of the base radius out). Those places
    // cut that line into strips; one extra point is tried in the middle of each strip, at the same three heights, with
    // the same tests as the nine rays (boxes, solid ground, grazing does not block, U13). A strip too thin for its middle
    // to be put at a whole thousandth strictly inside it is skipped. Gaps in height need no search: the heights seen at a
    // spot always reach the top, so only a target showing nothing below 1.1" is missed (HeightBandTests). If the nine rays see the target at all, nothing more is tried and the answer is theirs.
    public static GapSearchResult WithGapSearch(Battlefield battlefield, ModelBody attacker, ModelBody target)
    {
        var nine = NineRays(battlefield, attacker, target);
        if (nine.Visibility != Visibility.None)
        {
            return new GapSearchResult(nine, []);
        }
        var columns = Sample(attacker, target);
        if (columns.Count < 3)
        {
            return new GapSearchResult(nine, []);
        }
        Point2 left = columns[0], right = columns[2];
        long ax = attacker.Centre.X.Thousandths, ay = attacker.Centre.Y.Thousandths;
        long lx = left.X.Thousandths, ly = left.Y.Thousandths, rx = right.X.Thousandths, ry = right.Y.Thousandths;
        Int128 dx = rx - lx, dy = ry - ly;
        // Cross product of (p - attacker) and (q - attacker): its sign says which side of the line attacker->p q is on.
        static Int128 Cross(Int128 ux, Int128 uy, Int128 vx, Int128 vy) => ux * vy - uy * vx;
        var zero = ExactMath.Ratio.Of(0, 1);
        var one = ExactMath.Ratio.Of(1, 1);
        var cuts = new List<ExactMath.Ratio> { zero, one };
        void Add(Int128 num, Int128 den)
        {
            if (den == 0)
            {
                return;
            }
            var r = den > 0 ? new ExactMath.Ratio(num, den) : new ExactMath.Ratio(-num, -den);
            if (r.CompareTo(zero) > 0 && r.CompareTo(one) < 0)
            {
                cuts.Add(r);
            }
        }
        foreach (var piece in battlefield.Terrain)
        {
            foreach (var part in piece.Footprint.Parts)
            {
                long x0 = part.Min.X.Thousandths, y0 = part.Min.Y.Thousandths, x1 = part.Max.X.Thousandths, y1 = part.Max.Y.Thousandths;
                // The line from the attacker through each corner meets left + t (right - left) where
                // cross(corner - attacker, left - attacker) + t cross(corner - attacker, right - left) = 0.
                foreach (var (cx, cy) in new[] { (x0, y0), (x1, y0), (x0, y1), (x1, y1) })
                {
                    Int128 ux = cx - ax, uy = cy - ay;
                    Add(-Cross(ux, uy, lx - ax, ly - ay), Cross(ux, uy, dx, dy));
                }
                // Where the part's sides cross the line between the side points.
                foreach (var x in new[] { x0, x1 })
                {
                    Add(x - lx, dx);
                }
                foreach (var y in new[] { y0, y1 })
                {
                    Add(y - ly, dy);
                }
            }
        }
        cuts.Sort((a, b) => a.CompareTo(b));
        var from = Elevation.Of(battlefield, attacker);
        var to = Elevation.Of(battlefield, target);
        var eye = SightHeights.StandingHeight(battlefield, attacker, from) + SightHeights.Eye;
        var foot = SightHeights.StandingHeight(battlefield, target, to);
        var attackerTrench = eye - SightHeights.Eye < Length.Zero ? Trenches.Of(battlefield, attacker)?.Id : null;
        var targetTrench = foot < Length.Zero ? Trenches.Of(battlefield, target)?.Id : null;
        var tried = new HashSet<Point2>(columns);
        var extra = new List<SightRay>();
        for (var i = 0; i + 1 < cuts.Count; i++)
        {
            var (lo, hi) = (cuts[i], cuts[i + 1]);
            if (lo.CompareTo(hi) >= 0)
            {
                continue;
            }
            // The middle of the strip, rounded to a whole thousandth; it must still lie strictly between the strip's two
            // ends (by the exact parameter of its projection), else the strip is too thin and is skipped.
            var den = 2 * lo.Den * hi.Den;
            var num = lo.Num * hi.Den + hi.Num * lo.Den;
            var px = lx + (long)RoundedDiv(num * dx, den);
            var py = ly + (long)RoundedDiv(num * dy, den);
            var point = new Point2(Length.FromThousandths(px), Length.FromThousandths(py));
            if (!tried.Add(point) || !StrictlyInside(px, py, lo, hi))
            {
                continue;
            }
            foreach (var height in SightHeights.Points)
            {
                extra.Add(Ray(battlefield, attacker.Centre, eye, point, foot + height, from, to, attackerTrench, targetTrench));
            }
        }
        return new GapSearchResult(nine, extra);

        // Seen from the attacker, the rounded point must still fall strictly inside the strip: the line from the attacker
        // through it meets left->right strictly between lo and hi. Rounding can move a point of a very thin strip onto or
        // past an end (where a ray would only graze a corner); such a strip is skipped.
        bool StrictlyInside(long px, long py, ExactMath.Ratio lo, ExactMath.Ratio hi)
        {
            // Where the line from the attacker through (px, py) meets left->right.
            Int128 ux = px - ax, uy = py - ay;
            var c = Cross(ux, uy, dx, dy);
            if (c == 0)
            {
                return false;
            }
            var n = -Cross(ux, uy, lx - ax, ly - ay);
            var t = c > 0 ? new ExactMath.Ratio(n, c) : new ExactMath.Ratio(-n, -c);
            return t.CompareTo(lo) > 0 && t.CompareTo(hi) < 0;
        }
    }

    // One ray from the eye to a point on the target at the height top: blocked by the inside of any box it crosses
    // (except the platforms the two models stand on) and by solid ground. Used by the nine rays and the gap search alike.
    private static SightRay Ray(Battlefield battlefield, Point2 eyeAt, Length eye, Point2 point, Length top, ModelPosition from, ModelPosition to, string? attackerTrench, string? targetTrench)
    {
        var crossed = battlefield.Terrain.Where(t => t.Footprint.SegmentCrossesInside(eyeAt, point)).ToList();
        var obstructing = crossed
            .Where(t => t.Id != from.PlatformId && t.Id != to.PlatformId)
            .Where(t => t.Footprint.SegmentCrossesBoxInside(eyeAt, eye, point, top, Length.Zero, SightHeights.TopOf(t)))
            .Select(t => t.Id)
            .ToArray();
        if (battlefield.TrenchDepth is { } depth && GroundBlock(battlefield, depth, eyeAt, eye, point, top, attackerTrench, targetTrench) is { } ground)
        {
            obstructing = [.. obstructing.Append(ground).Order(StringComparer.Ordinal)];
        }
        return new SightRay(eyeAt, point, crossed.Select(t => t.Id).ToArray(), obstructing) { FromHeight = eye, ToHeight = top };
    }

    // For tests (height-gap plan, 2026-10-26): the ray from the attacker's eye to a point at any height above the
    // target's base, blocked exactly as the nine rays are.
    internal static SightRay RayAtHeight(Battlefield battlefield, ModelBody attacker, ModelBody target, Point2 point, Length height)
    {
        var from = Elevation.Of(battlefield, attacker);
        var to = Elevation.Of(battlefield, target);
        var eye = SightHeights.StandingHeight(battlefield, attacker, from) + SightHeights.Eye;
        var foot = SightHeights.StandingHeight(battlefield, target, to);
        var attackerTrench = eye - SightHeights.Eye < Length.Zero ? Trenches.Of(battlefield, attacker)?.Id : null;
        var targetTrench = foot < Length.Zero ? Trenches.Of(battlefield, target)?.Id : null;
        return Ray(battlefield, attacker.Centre, eye, point, foot + height, from, to, attackerTrench, targetTrench);
    }

    // Left, middle and right on the target, across the line from the attacker (left as the attacker looks at it).
    // Rounded half away from zero, so every machine uses the same points. Bases at the same spot give the middle only.
    internal static IReadOnlyList<Point2> Sample(ModelBody attacker, ModelBody target)
    {
        var dx = (Int128)(target.Centre.X.Thousandths - attacker.Centre.X.Thousandths);
        var dy = (Int128)(target.Centre.Y.Thousandths - attacker.Centre.Y.Thousandths);
        var length = ExactMath.FloorSqrt(dx * dx + dy * dy);
        if (length == 0)
        {
            return [target.Centre];
        }
        var k = (Int128)RoundedDiv(target.Radius.Thousandths * SightHeights.SideThousandths, 1000);
        // Left of the direction (dx, dy) is (-dy, dx).
        var ox = (long)RoundedDiv(-dy * k, length);
        var oy = (long)RoundedDiv(dx * k, length);
        var c = target.Centre;
        return
        [
            new Point2(c.X + Length.FromThousandths(ox), c.Y + Length.FromThousandths(oy)),
            c,
            new Point2(c.X - Length.FromThousandths(ox), c.Y - Length.FromThousandths(oy)),
        ];
    }

    private static Int128 RoundedDiv(Int128 n, Int128 d) => n >= 0 ? (n + d / 2) / d : -((-n + d / 2) / d);

    // Does the ray from (a, za) to (b, zb) pass through solid ground (U83)? Null if not; else the trench whose edge
    // blocks it, or GroundId. The ray is below the ground on one stretch (its height is a straight line). There the
    // points where it enters or leaves a trench part, and where it meets the floor, cut it into pieces; each piece is
    // wholly in the open trench (inside a part, not under the floor) or wholly in the ground, and its middle decides.
    private static string? GroundBlock(Battlefield battlefield, Length floorDepth, Point2 a, Length za, Point2 b, Length zb, string? attackerTrench, string? targetTrench)
    {
        long zA = za.Thousandths, zB = zb.Thousandths, floor = -floorDepth.Thousandths;
        if (zA >= 0 && zB >= 0)
        {
            return null;
        }
        var lo = ExactMath.Ratio.Of(0, 1);
        var hi = ExactMath.Ratio.Of(1, 1);
        // Below the ground: heights in (-infinity, 0), open.
        if (!ExactMath.Slab(zA, zB - zA, long.MinValue / 4, 0, true, ref lo, ref hi) || lo.CompareTo(hi) >= 0)
        {
            return null;
        }
        var spans = new List<(ExactMath.Ratio Lo, ExactMath.Ratio Hi)>();
        foreach (var (_, part) in battlefield.TrenchParts)
        {
            var s = ExactMath.Ratio.Of(0, 1);
            var e = ExactMath.Ratio.Of(1, 1);
            if (ExactMath.Slab(a.X.Thousandths, b.X.Thousandths - a.X.Thousandths, part.Min.X.Thousandths, part.Max.X.Thousandths, false, ref s, ref e)
                && ExactMath.Slab(a.Y.Thousandths, b.Y.Thousandths - a.Y.Thousandths, part.Min.Y.Thousandths, part.Max.Y.Thousandths, false, ref s, ref e)
                && s.CompareTo(e) < 0)
            {
                spans.Add((s, e));
            }
        }
        var cuts = new List<ExactMath.Ratio> { lo, hi };
        void Cut(ExactMath.Ratio r)
        {
            if (r.CompareTo(lo) > 0 && r.CompareTo(hi) < 0)
            {
                cuts.Add(r);
            }
        }
        foreach (var (s, e) in spans)
        {
            Cut(s);
            Cut(e);
        }
        if (zB != zA)
        {
            Cut(ExactMath.Ratio.Of(floor - zA, zB - zA));
        }
        cuts.Sort((x, y) => x.CompareTo(y));
        // Each piece: (start, end, inside a trench part, solid ground).
        var pieces = new List<(ExactMath.Ratio S, ExactMath.Ratio E, bool InTrench, bool Solid)>();
        for (var i = 0; i + 1 < cuts.Count; i++)
        {
            var (s, e) = (cuts[i], cuts[i + 1]);
            if (s.CompareTo(e) >= 0)
            {
                continue;
            }
            var inTrench = spans.Any(span => span.Lo.CompareTo(s) <= 0 && span.Hi.CompareTo(e) >= 0);
            // The middle m = num / den; its height za + m (zb - za) against the floor.
            var num = s.Num * e.Den + e.Num * s.Den;
            var den = 2 * s.Den * e.Den;
            var aboveFloor = zA * den + num * (zB - zA) >= floor * den;
            pieces.Add((s, e, inTrench, !(inTrench && aboveFloor)));
        }
        var first = pieces.FindIndex(p => p.Solid);
        if (first < 0)
        {
            return null;
        }
        var last = pieces.FindLastIndex(p => p.Solid);
        var one = ExactMath.Ratio.Of(1, 1);
        if (targetTrench is not null && hi.CompareTo(one) == 0 && pieces.Skip(last + 1).All(p => p.InTrench))
        {
            return targetTrench;
        }
        if (attackerTrench is not null && lo.CompareTo(ExactMath.Ratio.Of(0, 1)) == 0 && pieces.Take(first).All(p => p.InTrench))
        {
            return attackerTrench;
        }
        return GroundId;
    }
}

// Line-of-sight stage 2b-1 (U95): the nine rays, and the extra rays the gap search tried when they saw nothing. Visibility is the nine rays' answer, except that None becomes Partial when an
// extra ray is unblocked.
public sealed record GapSearchResult(SightResult Nine, IReadOnlyList<SightRay> Extra)
{
    public Visibility Visibility =>
        Nine.Visibility == Visibility.None && Extra.Any(r => r.Obstructing.Count == 0) ? Visibility.Partial : Nine.Visibility;

    public SightRay? FirstSeen => Extra.FirstOrDefault(r => r.Obstructing.Count == 0);
}
