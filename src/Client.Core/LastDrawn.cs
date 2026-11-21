using Rules.Core.State;

namespace Client.Core;

// Animation stage A3 (as BoardView._lastShapes), audit item R1: each model as the battlefield last drew it in this match,
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

    public void Clear() => _shapes.Clear();
}
