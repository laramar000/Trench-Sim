using Rules.Core.Events;
using Rules.Core.State;

namespace Client.Core;

// XCOM-style UX, improvement 3: the path a model moved along, read only from the server's event (a Move, Dash, charge
// or retreat), so the battlefield can show the model travelling it instead of jumping. The path's points are where the
// model's centre went after its starting point, in inches; the last one is where the server's state has it. Display only:
// the model's position is always the state's.
public sealed record Motion(ModelId Model, IReadOnlyList<TablePoint> Path);

public static class Motions
{
    public static Motion? Of(GameEvent e) => e switch
    {
        ModelMoved m => new(m.Model, [.. m.Path.Select(TablePoint.Of)]),
        ChargeMoved { Path.IsEmpty: false } c => new(c.Model, [.. c.Path.Select(TablePoint.Of)]),
        RetreatResolved { Moved: true } r => new(r.Model, [.. r.Path.Select(TablePoint.Of)]),
        _ => null,
    };
}
