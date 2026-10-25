using System.Collections.Immutable;

namespace Rules.Core.Geometry;

// The three battlefield archetypes (1.0.2 pp. 65-67).
public enum Archetype
{
    NoMansLand,
    DecimatedRuins,
    TrenchLines,
}

public enum LayoutProblemKind
{
    ZoneHasNoRoom,
    ZoneBlockedIn,
    ObjectiveOutOfReach,
    TooClose,
    TrenchOutsideZone,
    TrenchNotAllowed,
    RuinTooFar,
    UnknownRuin,
    NoTrenchAcross,
}

public sealed record LayoutProblem(LayoutProblemKind Kind, string Text, string RuleRef, ImmutableArray<string> Ids)
{
    public override string ToString() => $"{Text} ({RuleRef})";
}

// Trench-maps stage S4: is a terrain layout one the rules allow? Overlaps, pieces off the table and the other shape
// rules are checked when the Battlefield is built; this adds the set-up rules that need the whole layout (U84-U86).
//   Blocking terrain (p. 146): from anywhere a model can be set up in its deployment zone it must be able to leave the
//   zone (its centre past the zone's edge) and reach every Objective, moving on the ground (towers and impassable or unwalkable pieces block; trenches,
//   difficult and dangerous ground do not). Checked for a base of the given radius on a 1/4" grid (U85).
//   Archetype spacing (pp. 65-67), measured edge to edge; trench sections that touch are one trench (U86).
// Pure and deterministic: problems come out in a fixed order.
public static class MapLayout
{
    public static readonly Length GridStep = Length.FromThousandths(250);

    private static readonly long Three = Length.FromInches(3).Thousandths;
    private static readonly long One = Length.Inch.Thousandths;
    private static readonly long Nine = Length.FromInches(9).Thousandths;

    public static ImmutableArray<LayoutProblem> Check(
        Battlefield battlefield,
        IReadOnlyCollection<string> objectiveIds,
        Length baseRadius,
        Archetype? archetype = null,
        IReadOnlyCollection<string>? ruinIds = null)
    {
        ArgumentNullException.ThrowIfNull(battlefield);
        ArgumentNullException.ThrowIfNull(objectiveIds);
        if (baseRadius < GridStep)
        {
            throw new ArgumentOutOfRangeException(nameof(baseRadius), "The base radius must be at least the grid step.");
        }
        var problems = new List<LayoutProblem>();
        problems.AddRange(Reach(battlefield, objectiveIds, baseRadius));
        if (archetype is { } a)
        {
            problems.AddRange(Spacing(battlefield, a, ruinIds ?? []));
        }
        return [.. problems];
    }

    // Squared edge-to-edge distance between two footprints, in square thousandths (0 when they touch).
    public static long GapSquared(Footprint a, Footprint b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        var best = long.MaxValue;
        foreach (var p in a.Parts)
        {
            foreach (var q in b.Parts)
            {
                var dx = Math.Max(0, Math.Max(q.Min.X.Thousandths - p.Max.X.Thousandths, p.Min.X.Thousandths - q.Max.X.Thousandths));
                var dy = Math.Max(0, Math.Max(q.Min.Y.Thousandths - p.Max.Y.Thousandths, p.Min.Y.Thousandths - q.Max.Y.Thousandths));
                best = Math.Min(best, dx * dx + dy * dy);
            }
        }
        return best;
    }

    // Two pieces join when they share an edge of positive length (a corner is not enough for a base to pass).
    private static bool Joined(Footprint a, Footprint b)
    {
        foreach (var p in a.Parts)
        {
            foreach (var q in b.Parts)
            {
                var xOverlap = Math.Min(p.Max.X.Thousandths, q.Max.X.Thousandths) - Math.Max(p.Min.X.Thousandths, q.Min.X.Thousandths);
                var yOverlap = Math.Min(p.Max.Y.Thousandths, q.Max.Y.Thousandths) - Math.Max(p.Min.Y.Thousandths, q.Min.Y.Thousandths);
                if ((xOverlap == 0 && yOverlap > 0) || (yOverlap == 0 && xOverlap > 0))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static IEnumerable<LayoutProblem> Reach(Battlefield battlefield, IReadOnlyCollection<string> objectiveIds, Length radius)
    {
        var step = GridStep.Thousandths;
        var nx = (int)(battlefield.Width.Thousandths / step) + 1;
        var ny = (int)(battlefield.Height.Thousandths / step) + 1;
        var blockers = battlefield.Terrain.Where(t => !t.IsWalkable || t.IsTower).ToList();
        Point2 At(int i, int j) => new(Length.FromThousandths(i * step), Length.FromThousandths(j * step));

        var free = new bool[nx, ny];
        for (var i = 0; i < nx; i++)
        {
            for (var j = 0; j < ny; j++)
            {
                var p = At(i, j);
                free[i, j] = battlefield.Area.MoreThanHalfInside(p, radius) == AreaDecision.MoreThanHalf
                    && !blockers.Any(t => t.Footprint.CircleOverlaps(p, radius));
            }
        }

        // Connected pieces of free ground, joined by quarter-inch steps the base can sweep along.
        var label = new int[nx, ny];
        var count = 0;
        var queue = new Queue<(int, int)>();
        for (var i = 0; i < nx; i++)
        {
            for (var j = 0; j < ny; j++)
            {
                if (!free[i, j] || label[i, j] != 0)
                {
                    continue;
                }
                label[i, j] = ++count;
                queue.Enqueue((i, j));
                while (queue.Count > 0)
                {
                    var (ci, cj) = queue.Dequeue();
                    foreach (var (di, dj) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        var (ni, nj) = (ci + di, cj + dj);
                        if (ni < 0 || nj < 0 || ni >= nx || nj >= ny || !free[ni, nj] || label[ni, nj] != 0)
                        {
                            continue;
                        }
                        if (blockers.Any(t => t.Footprint.SweptCircleOverlaps(At(ci, cj), At(ni, nj), radius)))
                        {
                            continue;
                        }
                        label[ni, nj] = count;
                        queue.Enqueue((ni, nj));
                    }
                }
            }
        }

        var objectives = objectiveIds.Order(StringComparer.Ordinal)
            .Select(id => battlefield.Terrain.FirstOrDefault(t => t.Id == id))
            .OfType<TerrainBlock>()
            .ToList();
        foreach (var zone in battlefield.Zones.OrderBy(z => z.ZoneId, StringComparer.Ordinal))
        {
            var rect = zone.Area.Bounds;
            var starts = new HashSet<int>();
            var exits = new HashSet<int>();
            var touching = objectives.ToDictionary(o => o.Id, _ => new HashSet<int>());
            for (var i = 0; i < nx; i++)
            {
                for (var j = 0; j < ny; j++)
                {
                    if (!free[i, j])
                    {
                        continue;
                    }
                    var p = At(i, j);
                    if (Shapes.CircleInsideRect(p, radius, rect))
                    {
                        starts.Add(label[i, j]);
                    }
                    else if (!rect.Contains(p))
                    {
                        // Out of the zone: the base centre is past its edge (an edge of the table is no way out).
                        exits.Add(label[i, j]);
                    }
                }
            }
            if (starts.Count == 0)
            {
                yield return new LayoutProblem(LayoutProblemKind.ZoneHasNoRoom,
                    $"Deployment zone '{zone.ZoneId}' has no room to set up a {radius * 2} base.", "1.0.2 pp. 31, 146", [zone.ZoneId]);
                continue;
            }
            if (!starts.All(exits.Contains))
            {
                yield return new LayoutProblem(LayoutProblemKind.ZoneBlockedIn,
                    $"A model set up in part of deployment zone '{zone.ZoneId}' cannot leave it.", "1.0.2 p. 146", [zone.ZoneId]);
            }
            foreach (var objective in objectives)
            {
                var reached = new HashSet<int>();
                for (var i = 0; i < nx; i++)
                {
                    for (var j = 0; j < ny; j++)
                    {
                        if (free[i, j] && objective.Footprint.CircleOverlaps(At(i, j), radius))
                        {
                            reached.Add(label[i, j]);
                        }
                    }
                }
                if (!starts.All(reached.Contains))
                {
                    yield return new LayoutProblem(LayoutProblemKind.ObjectiveOutOfReach,
                        $"A model set up in deployment zone '{zone.ZoneId}' cannot reach objective '{objective.Id}'.", "1.0.2 p. 146", [zone.ZoneId, objective.Id]);
                }
            }
        }
    }

    private static IEnumerable<LayoutProblem> Spacing(Battlefield battlefield, Archetype archetype, IReadOnlyCollection<string> ruinIds)
    {
        var pieces = battlefield.Terrain.OrderBy(t => t.Id, StringComparer.Ordinal).ToList();
        var trenches = pieces.Where(t => t.IsTrench).ToList();
        var network = Networks(trenches);
        var ruins = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ruinIds.Order(StringComparer.Ordinal))
        {
            if (pieces.Any(t => t.Id == id))
            {
                ruins.Add(id);
            }
            else
            {
                yield return new LayoutProblem(LayoutProblemKind.UnknownRuin, $"Ruin '{id}' is not a terrain piece on this battlefield.", "1.0.2 p. 66", [id]);
            }
        }
        var page = archetype switch
        {
            Archetype.NoMansLand => "1.0.2 p. 65",
            Archetype.DecimatedRuins => "1.0.2 p. 66",
            _ => "1.0.2 p. 67",
        };

        if (archetype == Archetype.DecimatedRuins)
        {
            foreach (var t in trenches)
            {
                yield return new LayoutProblem(LayoutProblemKind.TrenchNotAllowed, $"Trench '{t.Id}' is not on the Decimated Ruins terrain list.", page, [t.Id]);
            }
        }

        if (archetype == Archetype.NoMansLand)
        {
            foreach (var t in trenches)
            {
                if (!battlefield.Zones.Any(z => t.Footprint.Parts.All(p => Inside(p, z.Area.Bounds))))
                {
                    yield return new LayoutProblem(LayoutProblemKind.TrenchOutsideZone, $"Trench '{t.Id}' must be wholly within one deployment zone.", page, [t.Id]);
                }
            }
        }

        if (archetype == Archetype.TrenchLines && !Across(battlefield, trenches, network))
        {
            yield return new LayoutProblem(LayoutProblemKind.NoTrenchAcross,
                "The trenches must run unbroken from one edge of the battlefield to the opposite edge.", page, []);
        }

        for (var i = 0; i < pieces.Count; i++)
        {
            for (var j = i + 1; j < pieces.Count; j++)
            {
                var (a, b) = (pieces[i], pieces[j]);
                long? least;
                if (a.IsTrench && b.IsTrench)
                {
                    // Sections of one trench touch; trenches apart from each other keep the usual spacing on No Man's
                    // Land and are not limited on Trench Lines (each one connects to the rest).
                    least = network[a.Id] == network[b.Id] || archetype == Archetype.TrenchLines ? null : Three;
                }
                else if (archetype == Archetype.TrenchLines && (a.IsTrench || b.IsTrench))
                {
                    least = One;
                }
                else
                {
                    least = Three;
                }
                if (least is { } d && GapSquared(a.Footprint, b.Footprint) < d * d)
                {
                    yield return new LayoutProblem(LayoutProblemKind.TooClose,
                        $"'{a.Id}' and '{b.Id}' must be at least {Length.FromThousandths(d)} apart.", page, [a.Id, b.Id]);
                }
            }
        }

        if (archetype == Archetype.DecimatedRuins && ruins.Count > 1)
        {
            // Each ruin after the first is set up within 9" of one already there, so they must chain together (U84).
            var list = ruins.Order(StringComparer.Ordinal).Select(id => pieces.First(t => t.Id == id)).ToList();
            var placed = new HashSet<string>(StringComparer.Ordinal) { list[0].Id };
            var grew = true;
            while (grew)
            {
                grew = false;
                foreach (var r in list.Where(r => !placed.Contains(r.Id)))
                {
                    if (list.Any(o => placed.Contains(o.Id) && GapSquared(r.Footprint, o.Footprint) <= Nine * Nine))
                    {
                        placed.Add(r.Id);
                        grew = true;
                    }
                }
            }
            foreach (var r in list.Where(r => !placed.Contains(r.Id)))
            {
                yield return new LayoutProblem(LayoutProblemKind.RuinTooFar, $"Ruin '{r.Id}' is not within 9\" of a chain of ruins from '{list[0].Id}'.", page, [r.Id]);
            }
        }
    }

    private static bool Inside(Rect part, Rect zone) =>
        part.Min.X >= zone.Min.X && part.Max.X <= zone.Max.X && part.Min.Y >= zone.Min.Y && part.Max.Y <= zone.Max.Y;

    // Which trench network each trench belongs to: sections joined along an edge, directly or through others.
    private static Dictionary<string, int> Networks(List<TerrainBlock> trenches)
    {
        var group = trenches.ToDictionary(t => t.Id, _ => -1, StringComparer.Ordinal);
        var next = 0;
        foreach (var t in trenches)
        {
            if (group[t.Id] >= 0)
            {
                continue;
            }
            var stack = new Stack<TerrainBlock>([t]);
            group[t.Id] = next;
            while (stack.Count > 0)
            {
                var c = stack.Pop();
                foreach (var o in trenches.Where(o => group[o.Id] < 0 && Joined(c.Footprint, o.Footprint)))
                {
                    group[o.Id] = next;
                    stack.Push(o);
                }
            }
            next++;
        }
        return group;
    }

    // One network touches two opposite edges of the battlefield (west and east, or south and north).
    private static bool Across(Battlefield battlefield, List<TerrainBlock> trenches, Dictionary<string, int> network)
    {
        foreach (var g in trenches.GroupBy(t => network[t.Id]))
        {
            var parts = g.SelectMany(t => t.Footprint.Parts).ToList();
            bool Edge(Func<Rect, bool> on) => parts.Any(on);
            if ((Edge(p => p.Min.X == Length.Zero) && Edge(p => p.Max.X == battlefield.Width))
                || (Edge(p => p.Min.Y == Length.Zero) && Edge(p => p.Max.Y == battlefield.Height)))
            {
                return true;
            }
        }
        return false;
    }
}
