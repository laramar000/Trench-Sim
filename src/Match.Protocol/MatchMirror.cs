using Rules.Core;
using Rules.Core.State;

namespace Match.Protocol;

// A client's copy of the match, rebuilt only from the server's events. Each event must be the next one in order, and
// the state after it must hash to what the server sent; otherwise the copy is no longer trustworthy and Apply throws.
public sealed class MatchMirror
{
    public MatchState State { get; private set; } = MatchState.None;

    public int Events { get; private set; }

    public string Hash { get; private set; } = StateHash.Compute(MatchState.None);

    public void Apply(EventPublished published)
    {
        ArgumentNullException.ThrowIfNull(published);
        if (published.Seq != Events + 1)
        {
            throw new MirrorException($"Expected event {Events + 1}, got {published.Seq}.");
        }
        MatchState next;
        try
        {
            next = Reducer.Apply(State, published.Event);
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
        State = next;
        Events = published.Seq;
        Hash = hash;
    }
}

public sealed class MirrorException(string message) : Exception(message);
