namespace Client.Core;

// Battlefield UI 3, stage U3 (decision D1-A): how good the connection is, from what the client really knows. The
// protocol has no ping message, so the only timing is a command's round trip: from sending it to the server's Accepted
// or Rejected answer (it includes the server's rules check, normally well under a millisecond). Nothing is measured
// between commands, so "not measured yet" until the first answer; nothing is invented.
public sealed record RoundTrips(TimeSpan? Last, DateTimeOffset? LastAt, TimeSpan? Median, int Count)
{
    public static readonly RoundTrips None = new(null, null, null, 0);

    // How many of the latest round trips the median is taken over.
    public const int Window = 10;

    public static RoundTrips Of(IReadOnlyList<TimeSpan> latest, DateTimeOffset lastAt)
    {
        ArgumentNullException.ThrowIfNull(latest);
        if (latest.Count == 0)
        {
            return None;
        }
        var sorted = latest.TakeLast(Window).Order().ToList();
        var median = sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
        return new(latest[^1], lastAt, median, latest.Count);
    }
}

public static class ConnectionQuality
{
    // Bars by the last round trip: 3 under 150 ms, 2 under 400 ms, else 1.
    public static readonly TimeSpan Good = TimeSpan.FromMilliseconds(150);
    public static readonly TimeSpan Fair = TimeSpan.FromMilliseconds(400);

    // 0 to 3 bars, or null for a cross (no connection to the match and none coming back by itself). Connected with no
    // measurement yet is 3 bars: the connection is up and nothing says it is slow.
    public static int? Bars(ConnectionState state, RoundTrips trips)
    {
        ArgumentNullException.ThrowIfNull(trips);
        return state switch
        {
            ConnectionState.Connected => trips.Last is not { } last ? 3 : last < Good ? 3 : last < Fair ? 2 : 1,
            ConnectionState.Reconnecting => 1,
            ConnectionState.Finished => 3,
            _ => null,
        };
    }

    // One short word for the state, on the tooltip's first line.
    public static string StateWord(ConnectionState state) => state switch
    {
        ConnectionState.NotConnected => "Not connected",
        ConnectionState.Connected => "Connected",
        ConnectionState.Reconnecting => "Reconnecting",
        ConnectionState.GaveUp => "Gave up reconnecting",
        ConnectionState.TakenOver => "Seat opened elsewhere",
        ConnectionState.Refused => "Refused by the server",
        ConnectionState.CopyFailed => "Out of step with the server",
        ConnectionState.Aborted => "Match aborted",
        ConnectionState.Finished => "Match ended",
        _ => state.ToString(),
    };

    // The measured round trip in words: "last 42 ms (5 s ago), median of the last 10: 40 ms", or "not measured yet".
    public static string Timing(RoundTrips trips, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(trips);
        if (trips is not { Last: { } last, LastAt: { } at, Median: { } median })
        {
            return "Command round trip: not measured yet (it is measured when a command is answered).";
        }
        var ago = now - at;
        var agoText = ago < TimeSpan.FromSeconds(1) ? "just now" : ago < TimeSpan.FromMinutes(1) ? $"{(int)ago.TotalSeconds} s ago" : $"{(int)ago.TotalMinutes} min ago";
        return $"Command round trip: last {Ms(last)} ({agoText}), median of the last {Math.Min(trips.Count, RoundTrips.Window)}: {Ms(median)}.";
    }

    private static string Ms(TimeSpan time) => $"{Math.Max(0, (int)Math.Round(time.TotalMilliseconds))} ms";

    // Everything the indicator's details say, line by line: the state in full, the timing, the server, the opponent
    // away and any warning.
    public static IReadOnlyList<string> Details(ScreenState screen, string server, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(screen);
        var lines = new List<string>
        {
            $"{StateWord(screen.Connection)}: {screen.ConnectionText}",
            Timing(screen.RoundTrips ?? RoundTrips.None, now),
            $"Server: {server}",
        };
        if (screen.OpponentAway is { } away)
        {
            lines.Add(away);
        }
        if (screen.Warning is { } warning)
        {
            lines.Add($"Warning: {warning}");
        }
        return lines;
    }
}
