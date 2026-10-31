using System.Collections.Immutable;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.State;

namespace Client.Core;

// XCOM-style UX, improvement 3: the path a model moved along, read only from the server's event (a Move, Dash, charge
// or retreat), so the battlefield can show the model travelling it instead of jumping. The path's points are where the
// model's centre went after its starting point, in inches; the last one is where the server's state has it. Display only:
// the model's position is always the state's.
public sealed record Motion(ModelId Model, IReadOnlyList<TablePoint> Path);

public static class Motions
{
    public static Motion? Of(GameEvent e) => Of(e, null);

    // With the state just after the event: a move that halts at a Mine Marker (Trench Warfare, U93) travels only to the
    // halt, and MoveResumed carries it on from there.
    public static Motion? Of(GameEvent e, MatchState? after) => e switch
    {
        ModelMoved m => new(m.Model, Cut(m.Model, m.Path, after)),
        ChargeMoved { Path.IsEmpty: false } c => new(c.Model, Cut(c.Model, c.Path, after)),
        RetreatResolved { Moved: true } r => new(r.Model, Cut(r.Model, r.Path, after)),
        MoveResumed { Path.IsEmpty: false } g => new(g.Model, [.. g.Path.Select(TablePoint.Of)]),
        _ => null,
    };

    private static List<TablePoint> Cut(ModelId model, ImmutableArray<Point2> path, MatchState? after) =>
        after?.MineStop is { Index: 0 } stop && stop.Model == model && stop.Path.SequenceEqual(path) && stop.Stops.Length > 0
            ? [.. Mines.ToFirst(path, stop.Stops[0]).Select(TablePoint.Of)]
            : [.. path.Select(TablePoint.Of)];
}
