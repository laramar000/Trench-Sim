using Rules.Core.State;

namespace Client.Core;

// Animation stage A3: a model's status changed by one event (Standing, Down or Out of Action), as the server's state has
// it just before and just after that event. Display only: the battlefield animates the fall, the rise or the removal.
public sealed record StatusChange(ModelId Model, ModelStatus From, ModelStatus To);

// Follows each model's status from one event's state to the next and names what each event changed. A model seen for the
// first time (a new match, or the tracker just cleared) changes nothing.
public sealed class StatusTracker
{
    private readonly Dictionary<ModelId, ModelStatus> _known = [];

    public void Clear() => _known.Clear();

    public IReadOnlyList<StatusChange> Next(MatchState after)
    {
        ArgumentNullException.ThrowIfNull(after);
        List<StatusChange>? changes = null;
        foreach (var model in after.Models)
        {
            if (_known.TryGetValue(model.Id, out var was) && was != model.Status)
            {
                (changes ??= []).Add(new StatusChange(model.Id, was, model.Status));
            }
            _known[model.Id] = model.Status;
        }
        return changes ?? (IReadOnlyList<StatusChange>)[];
    }
}
