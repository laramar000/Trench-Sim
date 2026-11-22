using Rules.Core.State;

namespace Client.Core;

// Animation stage A3 (as BoardView._lastShapes), audit item R1: each model as the battlefield last drew it in this match,
// audit item T3: the only record of it (BoardView's separate point-and-radius copy, _lastSeen, is gone),
// so a model that has just left the table (put Out of Action) can still be drawn going, and a shot that took it out
// still lands where it stood. Remembered from each scene the battlefield draws; a model that leaves the table keeps its
// last shape. Cleared when a new match starts (BoardView.NewMatch), so no model, place or shape of one match is ever
// used in the next, even when the two share model ids (a1, b1, ...). Display only.
public sealed class LastDrawn
{
    private readonly Dictionary<ModelId, ModelShape> _shapes = [];

    public int Count => _shapes.Count;

    public IReadOnlyCollection<ModelId> Models => _shapes.Keys;

    public void Remember(BoardScene? scene)
    {
        foreach (var model in scene?.Models ?? [])
        {
            _shapes[model.Id] = model;
        }
    }

    public bool TryGet(ModelId id, out ModelShape shape) => _shapes.TryGetValue(id, out shape!);

    // Audit item T3: a model as the battlefield draws it now (on the table), else as it was last drawn in this match (just
    // taken off the table), else null. The one place for "where is this model drawn": effects, falls and casualty
    // figures all ask here (BoardView.Where, StartFall, StartCasualty).
    public ModelShape? Find(BoardScene? table, ModelId id) => table?.Find(id) ?? (_shapes.TryGetValue(id, out var last) ? last : null);

    public void Clear() => _shapes.Clear();
}
