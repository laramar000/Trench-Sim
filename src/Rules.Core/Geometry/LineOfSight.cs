namespace Rules.Core.Geometry;

public enum Visibility
{
    None,
    Partial,
    Full,
}

// The single obstruction rule. Line of sight and cover both use it and nothing else decides whether a piece of
// terrain gets in the way, so they cannot disagree (U22). All of this is provisional gameplay abstraction, not a
// physical viewing simulation.
public static class SightRules
{
    // Does this piece obstruct the attacker's view of the target when a sight line crosses it?
    //  1. Only Walls obstruct; Low and Cover pieces never do.
    //  2. A platform never obstructs sight from or to a model standing on it (U19).
    //  3. An attacker with high ground sees over Ground-surface Walls (U20, an XCOM-style abstraction). It is
    //     still obstructed by High walls (other platforms, towers). The target's level adds no shelter (U21).
    public static bool Obstructs(TerrainBlock piece, ModelPosition attacker, ModelPosition target)
    {
        ArgumentNullException.ThrowIfNull(piece);
        if (piece.Profile != TerrainProfile.Wall)
        {
            return false;
        }
        if (piece.Id == attacker.PlatformId || piece.Id == target.PlatformId)
        {
            return false;
        }
        return !(attacker.Level == ModelLevel.High && piece.Surface == SurfaceLevel.Ground);
    }
}

// One test ray from the attacker's base centre to a sample point on the target's footprint.
// Crossed: every terrain piece whose inside the ray passes through (ids in order). Obstructing: those that
// SightRules.Obstructs says get in the way. The ray is blocked if Obstructing is not empty.
public sealed record SightRay(Point2 From, Point2 To, IReadOnlyList<string> Crossed, IReadOnlyList<string> Obstructing)
{
    public string? BlockedBy => Obstructing.Count == 0 ? null : Obstructing[0];

    // Heights of the two ends above the table, for height-based sight (HeightSight, stage L1). Zero for today's
    // top-down rays.
    public Length FromHeight { get; init; }

    public Length ToHeight { get; init; }
}

public sealed record SightResult(
    Visibility Visibility,
    int Visible,
    int Total,
    IReadOnlyList<SightRay> Rays,
    ModelPosition Attacker,
    ModelPosition Target);

// Chooses the points on the target that are tried for visibility. Replaceable: the rulebook says only that the
// attacker must be able to "see any part of the target model" (p. 30). In 2D the base footprint stands in for the
// model (U2) and which points stand in for "any part" is a software choice (U16).
public interface ISightSampler
{
    IReadOnlyList<Point2> Sample(ModelBody attacker, ModelBody target);
}

// First attempt (provisional, U16): the target's centre plus eight fixed compass points on its rim, keeping only
// the points on the side facing the attacker. All offsets are integers, so every machine tries the same points.
public sealed class LatticeSampler : ISightSampler
{
    // Cosine and sine of each compass direction in thousandths (0.707 stands in for sqrt(2)/2).
    private static readonly (long X, long Y)[] Directions =
    [
        (1000, 0), (707, 707), (0, 1000), (-707, 707), (-1000, 0), (-707, -707), (0, -1000), (707, -707),
    ];

    public IReadOnlyList<Point2> Sample(ModelBody attacker, ModelBody target)
    {
        var points = new List<Point2> { target.Centre };
        var toAttackerX = attacker.Centre.X.Thousandths - target.Centre.X.Thousandths;
        var toAttackerY = attacker.Centre.Y.Thousandths - target.Centre.Y.Thousandths;
        foreach (var (nx, ny) in Directions)
        {
            if (nx * toAttackerX + ny * toAttackerY <= 0)
            {
                continue;
            }
            points.Add(new Point2(
                target.Centre.X + Length.FromThousandths(RoundedDiv(nx * target.Radius.Thousandths, 1000)),
                target.Centre.Y + Length.FromThousandths(RoundedDiv(ny * target.Radius.Thousandths, 1000))));
        }
        return points;
    }

    // Division rounded half away from zero, so a rim point is the same on every machine.
    private static long RoundedDiv(long n, long d) => n >= 0 ? (n + d / 2) / d : -((-n + d / 2) / d);
}

// Line of sight from the attacker's base centre to the target. The rulebook: models see all around (360 degrees);
// sight is checked from the attacker's side; the target is seen if any part of it can be seen; partial line of
// sight means part but not all (p. 30). Provisional readings are in docs/interpretations.md: other models never
// block (U3); a ray is blocked only if it passes through the inside of an obstructing piece (U13).
public static class LineOfSight
{
    public static SightResult Between(Battlefield battlefield, ModelBody attacker, ModelBody target, ISightSampler? sampler = null)
    {
        ArgumentNullException.ThrowIfNull(battlefield);
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(target);

        var from = Elevation.Of(battlefield, attacker);
        var to = Elevation.Of(battlefield, target);
        var rays = new List<SightRay>();
        foreach (var point in (sampler ?? new LatticeSampler()).Sample(attacker, target))
        {
            var crossed = battlefield.Terrain.Where(t => t.Footprint.SegmentCrossesInside(attacker.Centre, point)).ToList();
            rays.Add(new SightRay(
                attacker.Centre,
                point,
                crossed.Select(t => t.Id).ToArray(),
                crossed.Where(t => SightRules.Obstructs(t, from, to)).Select(t => t.Id).ToArray()));
        }
        var visible = rays.Count(r => r.Obstructing.Count == 0);
        var kind = visible == 0 ? Visibility.None : visible == rays.Count ? Visibility.Full : Visibility.Partial;
        return new SightResult(kind, visible, rays.Count, rays, from, to);
    }
}
