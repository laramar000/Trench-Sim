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

    // The top of a piece: a tower's climb height, else its profile's height. Every piece is a box from the ground up.
    public static Length TopOf(TerrainBlock piece)
    {
        ArgumentNullException.ThrowIfNull(piece);
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

    // How high the base stands: its own elevation (a tower's height), or the top of the walkable High piece it is on.
    public static Length StandingHeight(Battlefield battlefield, ModelBody body, ModelPosition position)
    {
        ArgumentNullException.ThrowIfNull(battlefield);
        ArgumentNullException.ThrowIfNull(body);
        if (position.Level == ModelLevel.High && position.PlatformId is { } id && battlefield.Terrain.FirstOrDefault(t => t.Id == id) is { } platform)
        {
            var top = TopOf(platform);
            return top > body.Elevation ? top : body.Elevation;
        }
        return body.Elevation;
    }
}

// Height-based line of sight (line-of-sight plan, stage L1): not yet used by any rule. Today's LineOfSight stays the
// rule until stage L2 switches over.
//  - Rays run from the attacker's eye (above its base centre) to nine points on the target: three heights (Points)
//    times left, middle and right, set across the line between the two models at 0.8 of the base radius.
//  - A ray is blocked by a piece when it passes through the inside of the piece's box: its footprint, from the ground
//    up to TopOf. Grazing a face, edge or corner does not count (as U13). All maths is exact, in thousandths.
//  - Other models never block (U3). The platform a model stands on never blocks sight to or from it (U19). There is
//    no high-ground shortcut (U20 is replaced by the heights): a raised eye sees over a piece only if it is low enough.
//  - Clear (Full) when all nine points are seen, Partial when some are, Blocked (None) when none are.
public static class HeightSight
{
    public static SightResult Between(Battlefield battlefield, ModelBody attacker, ModelBody target)
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
        // Order: legs, body, head, each left to right as the attacker sees it.
        foreach (var height in SightHeights.Points)
        {
            foreach (var point in points)
            {
                var top = foot + height;
                var crossed = battlefield.Terrain.Where(t => t.Footprint.SegmentCrossesInside(attacker.Centre, point)).ToList();
                var obstructing = crossed
                    .Where(t => t.Id != from.PlatformId && t.Id != to.PlatformId)
                    .Where(t => t.Footprint.SegmentCrossesBoxInside(attacker.Centre, eye, point, top, Length.Zero, SightHeights.TopOf(t)))
                    .Select(t => t.Id)
                    .ToArray();
                rays.Add(new SightRay(attacker.Centre, point, crossed.Select(t => t.Id).ToArray(), obstructing) { FromHeight = eye, ToHeight = top });
            }
        }
        var visible = rays.Count(r => r.Obstructing.Count == 0);
        var kind = visible == 0 ? Visibility.None : visible == rays.Count ? Visibility.Full : Visibility.Partial;
        return new SightResult(kind, visible, rays.Count, rays, from, to);
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
}
