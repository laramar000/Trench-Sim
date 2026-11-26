using Rules.Core.Geometry;
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
    private readonly Dictionary<ModelId, Point2> _lastPosition = [];
    private readonly Dictionary<ModelId, int> _markers = [];

    public void Clear()
    {
        _known.Clear();
        _lastPosition.Clear();
        _markers.Clear();
    }

    // Hit reactions (Jason, 2026-11-26: "an animation for when a unit takes a hit but doesn't die ... when a unit takes a
    // blood marker"): the models the last event gave at least one more BLOOD MARKER while it left them standing (a model
    // taken Down falls instead; one put Out of Action goes). Display only.
    public IReadOnlyList<ModelId> Wounded { get; private set; } = [];

    // Where the server's table last had a model (kept after it leaves the table), or null if it never did.
    public TablePoint? Place(ModelId id) => _lastPosition.TryGetValue(id, out var p) ? TablePoint.Of(p) : null;

    // Bodies (Jason, 2026-11-23: "bodies lay on the ground, not disappear"): each model Out of Action now, at the last
    // place the server's table had it (the server clears it). Only for models this tracker saw on the table. Display only.
    public IReadOnlyDictionary<ModelId, Point2> Fallen =>
        _known.Where(k => k.Value.Status == ModelStatus.OutOfAction && _lastPosition.ContainsKey(k.Key)).ToDictionary(k => k.Key, k => _lastPosition[k.Key]);

    public IReadOnlyList<StatusChange> Next(MatchState after)
    {
        ArgumentNullException.ThrowIfNull(after);
        List<StatusChange>? changes = null;
        List<ModelId>? wounded = null;
        foreach (var model in after.Models)
        {
            if (_markers.TryGetValue(model.Id, out var markers) && model.BloodMarkers > markers && model.Status == ModelStatus.Standing
                && _known.TryGetValue(model.Id, out var stood) && stood.Status == ModelStatus.Standing)
            {
                (wounded ??= []).Add(model.Id);
            }
            _markers[model.Id] = model.BloodMarkers;
            var at = model.Position is { } p ? TablePoint.Of(p) : (TablePoint?)null;
            if (_known.TryGetValue(model.Id, out var was) && was.Status != model.Status)
            {
                (changes ??= []).Add(new StatusChange(model.Id, was.Status, model.Status, at ?? was.At));
            }
            // An Out of Action model keeps the last place the table had it.
            _known[model.Id] = (model.Status, at ?? (model.Status == ModelStatus.OutOfAction ? was.At : null));
            if (model.Position is { } position)
            {
                _lastPosition[model.Id] = position;
            }
        }
        Wounded = wounded ?? (IReadOnlyList<ModelId>)[];
        return changes ?? (IReadOnlyList<StatusChange>)[];
    }
}
