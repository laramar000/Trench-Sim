using Rules.Core.State;

namespace Client.Core;

// Animation stage A3: a model's status changed by one event (Standing, Down or Out of Action), as the server's state has
// it just before and just after that event. Display only: the battlefield animates the fall, the rise or the removal.
// Audit item B2: At is where the server's state has the model on the table: just after the event, or for a model put Out
// of Action (which leaves the table, its position cleared) just before it. A model hit by a mine during its own move is
// there at the halt the server stopped it at, not where the move started; null when the state never placed it.
public sealed record StatusChange(ModelId Model, ModelStatus From, ModelStatus To, TablePoint? At = null);

// Follows each model's status (and where the server's state has it) from one event's state to the next and names what
// each event changed. A model seen for the first time (a new match, or the tracker just cleared) changes nothing.
public sealed class StatusTracker
{
    private readonly Dictionary<ModelId, (ModelStatus Status, TablePoint? At)> _known = [];

    public void Clear() => _known.Clear();

    public IReadOnlyList<StatusChange> Next(MatchState after)
    {
        ArgumentNullException.ThrowIfNull(after);
        List<StatusChange>? changes = null;
        foreach (var model in after.Models)
        {
            var at = model.Position is { } p ? TablePoint.Of(p) : (TablePoint?)null;
            if (_known.TryGetValue(model.Id, out var was) && was.Status != model.Status)
            {
                (changes ??= []).Add(new StatusChange(model.Id, was.Status, model.Status, at ?? was.At));
            }
            // An Out of Action model keeps the last place the table had it.
            _known[model.Id] = (model.Status, at ?? (model.Status == ModelStatus.OutOfAction ? was.At : null));
        }
        return changes ?? (IReadOnlyList<StatusChange>)[];
    }
}
