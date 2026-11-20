namespace Client.Core;

// Animation stage A1: what the battlefield still has to play, in time order (seconds on the caller's clock). Each update's
// choreography is put after what is still playing; lines (an event's dice, notes and sound) and one-off beats (a flash, a
// tracer, a puff) come due at their time; beats that pose a model (glides, flinches, and since stage A2 turns, lunges and
// leans) are handed back at once with their absolute start, as they move models from then on. TakeAll shows everything at
// once (a command sent, a change of seat, Esc, a catch-up). Display timing only: nothing here reads or changes the match.
public sealed class PlaybackQueue
{
    public readonly record struct Item(double At, LogLine? Line, Beat? Beat);

    private readonly List<Item> _due = [];
    private readonly List<(Item Item, int Order)> _adding = [];

    // When everything queued so far has played.
    public double End { get; private set; }

    public int Count => _due.Count;

    // How far the queue is behind the clock: what is still to play, in seconds.
    public double Behind(double now) => Math.Max(0, End - now);

    // Puts the update after what is still playing (or now, if nothing is) and returns when it starts. Beats that pose a
    // model are added to moves with their Start made absolute; everything else waits in the queue.
    public double Add(Choreography plan, double now, List<Beat> moves)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(moves);
        var start = Math.Max(now, End);
        _adding.Clear();
        foreach (var cue in plan.Lines)
        {
            _adding.Add((new Item(start + cue.At, cue.Line, null), _adding.Count));
        }
        foreach (var beat in plan.Beats)
        {
            if (beat.PosesModel)
            {
                moves.Add(beat with { Start = start + beat.Start });
            }
            else
            {
                _adding.Add((new Item(start + beat.Start, null, beat), _adding.Count));
            }
        }
        // In time order; at the same time, in the order the choreography gave (a stable sort).
        _adding.Sort((a, b) => a.Item.At != b.Item.At ? a.Item.At.CompareTo(b.Item.At) : a.Order.CompareTo(b.Order));
        foreach (var (item, _) in _adding)
        {
            _due.Add(item);
        }
        _adding.Clear();
        End = Math.Max(End, start + plan.Length);
        return start;
    }

    // Moves everything due by now into `into`, in order (no allocation once `into` has grown).
    public void TakeDue(double now, List<Item> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        var taken = 0;
        while (taken < _due.Count && _due[taken].At <= now)
        {
            into.Add(_due[taken++]);
        }
        if (taken > 0)
        {
            _due.RemoveRange(0, taken);
        }
    }

    // Moves everything still queued into `into`, in order, and leaves nothing playing after now.
    public void TakeAll(double now, List<Item> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.AddRange(_due);
        _due.Clear();
        End = now;
    }
}
