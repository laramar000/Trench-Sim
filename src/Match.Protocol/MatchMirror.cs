using Rules.Core;
using Rules.Core.State;

namespace Match.Protocol;

// A client's copy of the match, rebuilt only from the server's events. Each event must be the next one in order, and
// the state after it must hash to what the server sent; otherwise the copy is no longer trustworthy and Apply throws.
public sealed class MatchMirror
{
    // State, event count and hash change together, in one step, so a reader on another thread (the screen) never sees
    // the state after one event with the count or hash of another. Read Current once to use more than one of them.
    private volatile MirrorCopy _copy = MirrorCopy.Empty;

    public MirrorCopy Current => _copy;

    public MatchState State => _copy.State;

    public int Events => _copy.Events;

    public string Hash => _copy.Hash;

    // Starts again from an empty copy (the server said this copy is not a prefix of the match).
    public void Reset() => _copy = MirrorCopy.Empty;

    public void Apply(EventPublished published)
    {
        ArgumentNullException.ThrowIfNull(published);
        var current = _copy;
        if (published.Seq != current.Events + 1)
        {
            throw new MirrorException($"Expected event {current.Events + 1}, got {published.Seq}.");
        }
        MatchState next;
        try
        {
            next = Reducer.Apply(current.State, published.Event);
        }
        catch (InvalidEventException e)
        {
            throw new MirrorException($"Event {published.Seq} does not apply: {e.Message}");
        }
        var hash = StateHash.Compute(next);
        if (hash != published.Hash)
        {
            throw new MirrorException($"State after event {published.Seq} hashes to {hash}, the server says {published.Hash}.");
        }
        _copy = new MirrorCopy(next, published.Seq, hash);
    }
}

// The client's copy at one moment: the state after Events events, and its hash.
public sealed record MirrorCopy(MatchState State, int Events, string Hash)
{
    public static readonly MirrorCopy Empty = new(MatchState.None, 0, StateHash.Compute(MatchState.None));
}

public sealed class MirrorException(string message) : Exception(message);
